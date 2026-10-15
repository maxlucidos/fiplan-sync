// Configuração do FIPLAN Sync (config.json ao lado do executável).
// Senha do Oracle e chave do Supabase ficam criptografadas com a DPAPI do Windows:
// só o mesmo usuário, no mesmo computador, consegue ler.
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;

namespace FiplanSync
{
    public class Config
    {
        public string OracleUsuario = "DETRAN_FIPLAN_USER";
        public string OracleSenha = "";
        public string OracleConexao = "";      // vazio = servidor padrão do FIPLAN (definido no motor)
        public string SupabaseUrl = "";
        public string SupabaseChave = "";
        public bool TabelaValidacao = true;
        public bool AtualizarNotas = false;
        // Agenda: a cada Intervalo minutos, das Inicio às Fim, nos Dias marcados.
        // Intervalo 0 = uma vez por dia (no horário de Inicio); -1 = só manual.
        public int Intervalo = 60;
        public int Inicio = 7 * 60;            // minutos desde a meia-noite
        public int Fim = 19 * 60;
        public string Dias = "12345";          // 1 = segunda ... 7 = domingo
        public string Notificar = "mudancas";  // tudo | mudancas | erros
        public bool Pausado = false;

        public static string Pasta { get { return AppDomain.CurrentDomain.BaseDirectory; } }
        static string Arquivo { get { return Path.Combine(Pasta, "config.json"); } }

        public bool Completa
        {
            get
            {
                return OracleUsuario != "" && OracleSenha != "" && SupabaseUrl != "" && SupabaseChave != "";
            }
        }

        public static Config Carregar()
        {
            var c = new Config();
            if (!File.Exists(Arquivo)) return c;
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(Arquivo, Encoding.UTF8));
            c.OracleUsuario = Texto(d, "oracleUsuario", c.OracleUsuario);
            c.OracleSenha = Abrir(Texto(d, "oracleSenha", ""));
            c.OracleConexao = Texto(d, "oracleConexao", "");
            c.SupabaseUrl = Texto(d, "supabaseUrl", "");
            c.SupabaseChave = Abrir(Texto(d, "supabaseChave", ""));
            c.TabelaValidacao = Logico(d, "tabelaValidacao", c.TabelaValidacao);
            c.AtualizarNotas = Logico(d, "atualizarNotas", c.AtualizarNotas);
            if (d.ContainsKey("intervalo"))
            {
                c.Intervalo = Inteiro(d, "intervalo", c.Intervalo);
                c.Inicio = Inteiro(d, "inicio", c.Inicio);
                c.Fim = Inteiro(d, "fim", c.Fim);
                c.Dias = Texto(d, "dias", c.Dias);
            }
            else c.FrequenciaAntiga(Texto(d, "frequencia", "hora"));
            c.Notificar = Texto(d, "notificar", c.Notificar);
            c.Pausado = Logico(d, "pausado", false);
            return c;
        }

        public void Salvar()
        {
            var d = new Dictionary<string, object>();
            d["oracleUsuario"] = OracleUsuario;
            d["oracleSenha"] = Fechar(OracleSenha);
            d["oracleConexao"] = OracleConexao;
            d["supabaseUrl"] = SupabaseUrl;
            d["supabaseChave"] = Fechar(SupabaseChave);
            d["tabelaValidacao"] = TabelaValidacao;
            d["atualizarNotas"] = AtualizarNotas;
            d["intervalo"] = Intervalo;
            d["inicio"] = Inicio;
            d["fim"] = Fim;
            d["dias"] = Dias;
            d["notificar"] = Notificar;
            d["pausado"] = Pausado;
            File.WriteAllText(Arquivo, new JavaScriptSerializer().Serialize(d), new UTF8Encoding(false));
        }

        public Config Copia()
        {
            return (Config)MemberwiseClone();
        }

        // Configuração em KEY=VALUE (formato trocado com o instalador)
        Dictionary<string, string> ParaArquivo()
        {
            var env = new Dictionary<string, string>();
            env["ORACLE_USER"] = OracleUsuario;
            env["ORACLE_PASSWORD"] = OracleSenha;
            if (OracleConexao != "") env["ORACLE_CONNECT_STRING"] = OracleConexao;
            env["SUPABASE_URL"] = SupabaseUrl;
            env["SUPABASE_SECRET_KEY"] = SupabaseChave;
            env["TABELA_VALIDACAO"] = TabelaValidacao ? "sim" : "nao";
            env["ATUALIZAR_NOTAS"] = AtualizarNotas ? "sim" : "nao";
            return env;
        }

        // --- Troca com o instalador: arquivo KEY=VALUE em texto, apagado logo depois ---

        // Senhas vão como <NOME>_HEX (UTF-16 em hexadecimal) para qualquer caractere passar intacto
        static string Hex(string texto)
        {
            var sb = new StringBuilder();
            foreach (char c in texto) sb.Append(((int)c).ToString("x4"));
            return sb.ToString();
        }

        static string DeHex(string hex)
        {
            var sb = new StringBuilder();
            for (int i = 0; i + 4 <= hex.Length; i += 4) sb.Append((char)Convert.ToInt32(hex.Substring(i, 4), 16));
            return sb.ToString();
        }

        public void Importar(string arquivo)
        {
            foreach (var linha in File.ReadAllLines(arquivo, Encoding.UTF8))
            {
                int i = linha.IndexOf('=');
                if (i <= 0) continue;
                string k = linha.Substring(0, i).Trim().TrimStart('﻿'), v = linha.Substring(i + 1).Trim();
                if (v.Length >= 2 && v[0] == '"' && v[v.Length - 1] == '"') v = v.Substring(1, v.Length - 2);
                if (k.EndsWith("_HEX"))
                {
                    k = k.Substring(0, k.Length - 4);
                    v = DeHex(v);
                }
                switch (k)
                {
                    case "ORACLE_USER": OracleUsuario = v; break;
                    case "ORACLE_PASSWORD": OracleSenha = v; break;
                    case "ORACLE_CONNECT_STRING": OracleConexao = v; break;
                    case "SUPABASE_URL": SupabaseUrl = v; break;
                    case "SUPABASE_SECRET_KEY": SupabaseChave = v; break;
                    case "TABELA_VALIDACAO": TabelaValidacao = v == "sim"; break;
                    case "ATUALIZAR_NOTAS": AtualizarNotas = v == "sim"; break;
                    case "FREQUENCIA": FrequenciaAntiga(v); break;  // versões de teste
                    case "INTERVALO": { int n; if (int.TryParse(v, out n)) Intervalo = n; } break;
                    case "INICIO": Inicio = Minutos(v, Inicio); break;
                    case "FIM": Fim = Minutos(v, Fim); break;
                    case "DIAS": Dias = v; break;
                    case "NOTIFICAR": Notificar = v; break;
                }
            }
        }

        public void Exportar(string arquivo)
        {
            var env = ParaArquivo();
            env.Remove("ORACLE_PASSWORD");
            env.Remove("SUPABASE_SECRET_KEY");
            env["ORACLE_PASSWORD_HEX"] = Hex(OracleSenha);
            env["SUPABASE_SECRET_KEY_HEX"] = Hex(SupabaseChave);
            env["ORACLE_CONNECT_STRING"] = OracleConexao;
            env["INTERVALO"] = Intervalo.ToString();
            env["INICIO"] = Hora(Inicio);
            env["FIM"] = Hora(Fim);
            env["DIAS"] = Dias;
            env["NOTIFICAR"] = Notificar;
            var linhas = new List<string>();
            foreach (var kv in env) linhas.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(arquivo, linhas.ToArray(), new UTF8Encoding(false));
        }

        // As versões de teste tinham só 4 opções fixas de frequência
        void FrequenciaAntiga(string codigo)
        {
            Inicio = 7 * 60;
            Fim = 19 * 60;
            Dias = "12345";
            switch (codigo)
            {
                case "meia": Intervalo = 30; break;
                case "dia": Intervalo = 0; Inicio = 8 * 60; break;
                case "manual": Intervalo = -1; break;
                default: Intervalo = 60; break;
            }
        }

        public static string Hora(int minutos)
        {
            return (minutos / 60).ToString("00") + ":" + (minutos % 60).ToString("00");
        }

        static int Minutos(string hhmm, int padrao)
        {
            var p = hhmm.Split(':');
            int h, m;
            return p.Length == 2 && int.TryParse(p[0], out h) && int.TryParse(p[1], out m) ? h * 60 + m : padrao;
        }

        // --- Utilitários ---

        static int Inteiro(Dictionary<string, object> d, string k, int padrao)
        {
            object v;
            return d.TryGetValue(k, out v) && v is int ? (int)v : padrao;
        }

        static string Texto(Dictionary<string, object> d, string k, string padrao)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? v.ToString() : padrao;
        }

        static bool Logico(Dictionary<string, object> d, string k, bool padrao)
        {
            object v;
            return d.TryGetValue(k, out v) && v is bool ? (bool)v : padrao;
        }

        static string Fechar(string texto)
        {
            if (texto == "") return "";
            var dados = ProtectedData.Protect(Encoding.UTF8.GetBytes(texto), null, DataProtectionScope.CurrentUser);
            return "dpapi:" + Convert.ToBase64String(dados);
        }

        static string Abrir(string guardado)
        {
            if (!guardado.StartsWith("dpapi:")) return guardado;
            try
            {
                var dados = Convert.FromBase64String(guardado.Substring(6));
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(dados, null, DataProtectionScope.CurrentUser));
            }
            catch (CryptographicException)
            {
                return "";  // config copiada de outro usuário/computador: pede de novo nas Configurações
            }
        }
    }
}
