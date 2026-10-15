// Comandos de linha de comando usados pelo instalador (e pela janela de Configurações, na mesma
// forma). Cada um escreve o resultado em texto e devolve o código de saída:
// 0 = ok, 1 = erro.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace FiplanSync
{
    public class Resposta
    {
        public int Codigo;
        public string Texto = "";
        public bool Ok { get { return Codigo == 0; } }
    }

    public static class Comandos
    {
        public static readonly string[] Nomes =
        {
            "--sync", "--dry-run", "--comparar", "--testar-oracle", "--testar-supabase",
            "--staging-existe", "--simular", "--desagendar",
        };

        // Executa um comando e devolve o código e o texto (erros viram mensagem, não exceção)
        public static Resposta Executar(string comando, Config c)
        {
            var saida = new StringWriter();
            int codigo;
            try
            {
                codigo = Rodar(comando, c, saida);
            }
            catch (ErroSync ex)
            {
                saida.WriteLine(ex.Message);
                codigo = 1;
            }
            catch (Exception ex)
            {
                saida.WriteLine("Erro inesperado: " + ex.Message);
                codigo = 1;
            }
            return new Resposta { Codigo = codigo, Texto = saida.ToString().Trim() };
        }

        static int Rodar(string comando, Config c, TextWriter o)
        {
            switch (comando)
            {
                case "--testar-oracle":
                    o.WriteLine("OK " + Fiplan.Contar(c) + " notas encontradas no FIPLAN");
                    return 0;

                case "--testar-supabase":
                    return TestarSupabase(c, o);

                case "--staging-existe":
                    {
                        // A staging é do sistema (já existe no Supabase); confere se tem as colunas usadas
                        var r = new Supabase(c).Enviar("GET",
                            "staging_notas_empenho?select=id,empenho_id,processo_id,numero_empenho,valor_pgto_ia,valor_estorno_ia,contexto_extracao,status&limit=1",
                            null, null);
                        if (r.Key == 200) return 0;
                        o.WriteLine(r.Key == 401 ? "O Supabase recusou a chave."
                                  : r.Key == 404 ? "A tabela staging_notas_empenho não existe neste projeto do Supabase."
                                  : "A tabela staging_notas_empenho não tem as colunas esperadas (HTTP " + r.Key + "): " + r.Value);
                        return 1;
                    }

                case "--simular":
                    o.WriteLine(Sincronizacao.Simular(c));
                    return 0;

                case "--sync":
                case "--dry-run":
                    Sincronizacao.Executar(c, comando == "--dry-run", o.WriteLine);
                    return 0;

                case "--comparar":
                    {
                        string arquivo = Path.Combine(Environment.CurrentDirectory, "comparacao.csv");
                        File.WriteAllText(arquivo, Sincronizacao.CompararCsv(c), new UTF8Encoding(true));
                        o.WriteLine("Detalhe em " + arquivo);
                        return 0;
                    }

                case "--desagendar":
                    {
                        // A tarefa do Agendador era usada antes do app de bandeja; o instalador a remove na atualização
                        string tarefa = Environment.GetEnvironmentVariable("FIPLAN_TAREFA") ?? "FIPLAN Sync";
                        var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/delete /tn \"" + tarefa + "\" /f")
                        {
                            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                        });
                        p.WaitForExit();
                        o.WriteLine(p.ExitCode == 0 ? "Tarefa \"" + tarefa + "\" removida." : "Não havia tarefa \"" + tarefa + "\".");
                        return 0;
                    }
            }
            o.WriteLine("Comando desconhecido: " + comando);
            return 1;
        }

        static int TestarSupabase(Config c, TextWriter o)
        {
            Uri uri;
            if (!Uri.TryCreate(c.SupabaseUrl, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http"))
            {
                o.WriteLine("URL inválida. Use o formato https://xxxx.supabase.co");
                return 1;
            }
            string chave = c.SupabaseChave.Trim();
            string tipo = TipoChave(chave);
            if (tipo == "publica")
            {
                o.WriteLine("Essa é a chave pública (anon/publishable). O programa precisa da chave secret (sb_secret_...).");
                return 1;
            }
            if (tipo == "invalida")
            {
                o.WriteLine("Isso não parece uma chave do Supabase. Copie a chave secret em Project Settings > API Keys.");
                return 1;
            }
            var sb = new Supabase(c);
            var r = sb.Enviar("GET", "processo_notas_empenho?select=id&limit=1", null, null);
            if (r.Key != 200)
            {
                o.WriteLine(r.Key == 401
                    ? "O Supabase recusou a chave. Confira se copiou a chave secret inteira."
                    : "Não consegui conectar ao Supabase (HTTP " + r.Key + "): " + r.Value);
                return 1;
            }
            o.WriteLine("OK " + sb.Url);
            return 0;
        }

        static string TipoChave(string chave)
        {
            if (chave.StartsWith("sb_secret_")) return "secret";
            if (chave.StartsWith("sb_publishable_")) return "publica";
            var partes = chave.Split('.');
            if (partes.Length == 3)
            {
                try
                {
                    string b64 = partes[1].Replace('-', '+').Replace('_', '/');
                    b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
                    var dados = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(
                        Encoding.UTF8.GetString(Convert.FromBase64String(b64)));
                    object role;
                    return dados.TryGetValue("role", out role) && "service_role".Equals(role) ? "secret" : "publica";
                }
                catch { }
            }
            return "invalida";
        }
    }
}
