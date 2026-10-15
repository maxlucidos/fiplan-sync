// Histórico das execuções (historico.jsonl, uma linha por execução) e registro completo (sync.log).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace FiplanSync
{
    public class Execucao
    {
        public string Data;          // ISO 8601, horário local
        public bool Ok;
        public bool Manual;
        public int Lidas;
        public int Atualizadas;
        public string Resumo = "";
        public string Detalhe = "";
        public string Origem = "";   // fiplan | supabase | outro (quando deu erro)
        public bool Configurar;      // erro que só se resolve nas Configurações
        public string Mensagem = ""; // mensagem do erro

        public DateTime Quando { get { return DateTime.Parse(Data, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind); } }

        public static Execucao De(Resumo r, Exception erro, bool manual, Config config, string log)
        {
            var e = new Execucao { Data = DateTime.Now.ToString("o", CultureInfo.InvariantCulture), Manual = manual };
            var detalhe = new StringBuilder();

            if (erro == null)
            {
                e.Ok = true;
                e.Lidas = r.Lidas;
                e.Atualizadas = r.Atualizadas.Count;
                if (config.AtualizarNotas)
                    e.Resumo = e.Atualizadas == 0 ? "Nenhuma nota mudou"
                             : e.Atualizadas == 1 ? "1 nota atualizada"
                             : e.Atualizadas + " notas atualizadas";
                else
                    e.Resumo = "Tabela de validação atualizada";

                if (r.Atualizadas.Count > 0)
                {
                    detalhe.AppendLine("Notas atualizadas (sistema → FIPLAN):");
                    foreach (var m in r.Atualizadas)
                    {
                        detalhe.Append("  " + m.Numero);
                        if (m.PagoAntes != m.PagoDepois)
                            detalhe.Append("   pago " + Sincronizacao.Fmt(m.PagoAntes) + " → " + Sincronizacao.Fmt(m.PagoDepois));
                        if (m.EstornosAntes != m.EstornosDepois)
                            detalhe.Append("   estornos " + Sincronizacao.Fmt(m.EstornosAntes) + " → " + Sincronizacao.Fmt(m.EstornosDepois));
                        detalhe.AppendLine();
                    }
                    detalhe.AppendLine();
                }
            }
            else
            {
                var es = erro as ErroSync;
                e.Origem = es != null ? es.Origem : "outro";
                e.Configurar = es != null && es.Configurar;
                e.Mensagem = erro.Message;
                e.Resumo = e.Origem == "fiplan" ? "Erro ao ler o FIPLAN"
                         : e.Origem == "supabase" ? "Erro ao gravar no Supabase"
                         : "Erro";
                detalhe.AppendLine(e.Mensagem);
                if (e.Configurar) detalhe.AppendLine("Confira o usuário, a senha ou a chave em Configurações.");
                if (es == null) detalhe.AppendLine(erro.ToString());
                detalhe.AppendLine();
            }
            detalhe.AppendLine("--- Registro da execução ---");
            detalhe.Append(log.Trim());
            e.Detalhe = detalhe.ToString();
            return e;
        }
    }

    public static class Historico
    {
        const int Maximo = 300;
        static string Arquivo { get { return Path.Combine(Config.Pasta, "historico.jsonl"); } }
        static string Log { get { return Path.Combine(Config.Pasta, "sync.log"); } }

        public static List<Execucao> Carregar()
        {
            var lista = new List<Execucao>();
            if (!File.Exists(Arquivo)) return lista;
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            foreach (var linha in File.ReadAllLines(Arquivo, Encoding.UTF8))
            {
                if (linha.Trim() == "") continue;
                try { lista.Add(js.Deserialize<Execucao>(linha)); } catch { /* linha corrompida: ignora */ }
            }
            return lista;
        }

        public static void Adicionar(List<Execucao> lista, Execucao e, string log)
        {
            lista.Add(e);
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            if (lista.Count > Maximo + 100)
            {
                lista.RemoveRange(0, lista.Count - Maximo);
                File.WriteAllLines(Arquivo, lista.Select(x => js.Serialize(x)).ToArray(), new UTF8Encoding(false));
            }
            else
            {
                File.AppendAllText(Arquivo, js.Serialize(e) + Environment.NewLine, new UTF8Encoding(false));
            }

            // Registro completo, para suporte; roda quando passa de 5 MB
            if (File.Exists(Log) && new FileInfo(Log).Length > 5 * 1024 * 1024)
            {
                File.Copy(Log, Log + ".1", true);
                File.Delete(Log);
            }
            File.AppendAllText(Log, log.Trim() + Environment.NewLine + Environment.NewLine, new UTF8Encoding(false));
        }
    }
}
