// Leitura das notas de empenho na view ACWVW0111 do FIPLAN (Oracle 19c, driver gerenciado da Oracle).
using System;
using System.Collections.Generic;
using Oracle.ManagedDataAccess.Client;

namespace FiplanSync
{
    public class NotaFiplan
    {
        public string NumrEmp;      // 193010001150002931 (18 dígitos, lido como texto)
        public int Exercicio;
        public string NomeCredor, CpfCnpjCredor, ElementoDescricao, Fonte, Dotacao, Situacao;
        public int? ElementoCodigo;
        public decimal Empenhado, Estornado, Saldo, Pago;

        // 193010001150002931 -> 19301.0001.15.000293-1 (formato usado no sistema)
        public string Numero
        {
            get
            {
                var d = NumrEmp;
                return d.Substring(0, 5) + "." + d.Substring(5, 4) + "." + d.Substring(9, 2) + "." + d.Substring(11, 6) + "-" + d.Substring(17);
            }
        }
    }

    public static class Fiplan
    {
        // O banco usa SID (ormtp6), por isso o descritor completo
        public const string ConexaoPadrao = "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=192.168.221.21)(PORT=1521))(CONNECT_DATA=(SID=ormtp6)))";
        public const int UnidadeOrcamentaria = 19301;

        // Pago calculado: TOTAL_PAGO da view só reflete o exercício corrente e diverge nas notas de anos anteriores
        const string Consulta = @"
            SELECT cd_exercicio,
                   TO_CHAR(numr_emp) AS numr_emp,
                   nome_credor,
                   cpf_cnpj_credor,
                   cd_elemento_despesa,
                   ds_elemento_despesa,
                   REGEXP_SUBSTR(codg_dotacao_orcamentaria, '[^.]+', 1, 9) AS fonte,
                   codg_dotacao_orcamentaria,
                   valr_emp AS empenhado,
                   NVL(total_anulado,0) + NVL(total_cancelado_rpnp,0) AS estornado,
                   NVL(total_liquidar,0) + NVL(total_em_liq,0) + NVL(total_liq_pagar,0) AS saldo,
                   valr_emp
                     - (NVL(total_anulado,0) + NVL(total_cancelado_rpnp,0))
                     - (NVL(total_liquidar,0) + NVL(total_em_liq,0) + NVL(total_liq_pagar,0)) AS pago,
                   ds_situacao_emp
            FROM acwvw0111
            WHERE cd_unidade_orcamentaria = :uo";

        static OracleConnection Conectar(Config c)
        {
            if (c.OracleUsuario == "" || c.OracleSenha == "")
                throw new ErroSync("Usuário ou senha do FIPLAN não configurados.", "fiplan", true);
            var cs = new OracleConnectionStringBuilder
            {
                UserID = c.OracleUsuario,
                Password = c.OracleSenha,
                DataSource = c.OracleConexao != "" ? c.OracleConexao : ConexaoPadrao,
                ConnectionTimeout = 20,
                Pooling = false,
            };
            var conn = new OracleConnection(cs.ConnectionString);
            try
            {
                conn.Open();
                return conn;
            }
            catch (OracleException ex)
            {
                conn.Dispose();
                throw Traduzir(ex);
            }
        }

        public static int Contar(Config c)
        {
            using (var conn = Conectar(c))
            using (var cmd = new OracleCommand("SELECT COUNT(*) FROM acwvw0111 WHERE cd_unidade_orcamentaria = :uo", conn) { BindByName = true })
            {
                cmd.Parameters.Add("uo", UnidadeOrcamentaria);
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public static List<NotaFiplan> Ler(Config c)
        {
            var notas = new List<NotaFiplan>();
            using (var conn = Conectar(c))
            using (var cmd = new OracleCommand(Consulta, conn) { BindByName = true, CommandTimeout = 300, FetchSize = 4 * 1024 * 1024 })
            {
                cmd.Parameters.Add("uo", UnidadeOrcamentaria);
                try
                {
                    using (var r = cmd.ExecuteReader())
                    {
                        r.FetchSize = r.RowSize * 5000;  // menos idas e voltas ao servidor (são ~33 mil linhas)
                        while (r.Read())
                            notas.Add(new NotaFiplan
                            {
                                Exercicio = Convert.ToInt32(r.GetValue(0)),
                                NumrEmp = r.GetString(1),
                                NomeCredor = Texto(r, 2),
                                CpfCnpjCredor = Texto(r, 3),
                                ElementoCodigo = r.IsDBNull(4) ? (int?)null : Convert.ToInt32(r.GetValue(4)),
                                ElementoDescricao = Texto(r, 5),
                                Fonte = Texto(r, 6),
                                Dotacao = Texto(r, 7),
                                Empenhado = Numero(r, 8),
                                Estornado = Numero(r, 9),
                                Saldo = Numero(r, 10),
                                Pago = Numero(r, 11),
                                Situacao = Texto(r, 12),
                            });
                    }
                }
                catch (OracleException ex)
                {
                    throw Traduzir(ex);
                }
            }
            return notas;
        }

        static string Texto(OracleDataReader r, int i)
        {
            return r.IsDBNull(i) ? null : r.GetValue(i).ToString();
        }

        static decimal Numero(OracleDataReader r, int i)
        {
            return r.IsDBNull(i) ? 0m : r.GetDecimal(i);
        }

        // Mensagens de erro do Oracle em português, marcando as que só se resolvem nas Configurações
        static ErroSync Traduzir(OracleException ex)
        {
            switch (ex.Number)
            {
                case 1017: return new ErroSync("O FIPLAN recusou o usuário ou a senha.", "fiplan", true);
                case 28000: return new ErroSync("O usuário do FIPLAN está bloqueado.", "fiplan", true);
                case 28001: return new ErroSync("A senha do usuário do FIPLAN expirou.", "fiplan", true);
                case 12541:
                case 12170:
                case 12543:
                case 12545:
                case 12514:
                case 12505:
                    return new ErroSync("Não consegui alcançar o servidor do FIPLAN (192.168.221.21:1521). " +
                                        "Confira se este computador está na rede do DETRAN. (ORA-" + ex.Number.ToString("00000") + ")", "fiplan", false);
                default:
                    return new ErroSync("Erro no FIPLAN: " + ex.Message.Split('\n')[0].Trim(), "fiplan", false);
            }
        }
    }

    // Erro de uma etapa da sincronização, com a origem (fiplan/supabase) e se precisa mexer nas Configurações
    public class ErroSync : Exception
    {
        public readonly string Origem;
        public readonly bool Configurar;

        public ErroSync(string mensagem, string origem, bool configurar) : base(mensagem)
        {
            Origem = origem;
            Configurar = configurar;
        }
    }
}
