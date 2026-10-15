// Sincronização: lê o FIPLAN, grava a tabela de validação e atualiza pagamentos/estornos das notas do sistema.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;

namespace FiplanSync
{
    public class Mudanca
    {
        public string Id, Numero;
        public int Exercicio;
        public decimal PagoAntes, PagoDepois, EstornosAntes, EstornosDepois, Saldo;
    }

    public class AvisoValor
    {
        public string Numero;
        public decimal Sistema, Fiplan;
    }

    public class Comparacao
    {
        public int Total, NaoEncontradas;
        public List<Mudanca> Mudancas = new List<Mudanca>();
        public List<AvisoValor> AvisosValor = new List<AvisoValor>();
    }

    public class Resumo
    {
        public int Lidas, Gravadas, NaoEncontradas;
        public List<Mudanca> Atualizadas = new List<Mudanca>();
        public List<AvisoValor> AvisosValor = new List<AvisoValor>();
    }

    public static class Sincronizacao
    {
        const int Lote = 1000;
        static readonly CultureInfo Br = new CultureInfo("pt-BR");

        public static string Fmt(decimal v) { return v.ToString("N2", Br); }

        static string SoDigitos(string s)
        {
            return new string((s ?? "").Where(char.IsDigit).ToArray());
        }

        static decimal Dec(Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? Convert.ToDecimal(v, CultureInfo.InvariantCulture) : 0m;
        }

        static bool Diferente(decimal a, decimal b) { return Math.Abs(a - b) > 0.005m; }

        // Compara as notas do sistema com o FIPLAN e lista o que mudaria em pagamentos/estornos.
        // O valor empenhado não é alterado: divergências de valor só viram aviso.
        public static Comparacao Comparar(List<NotaFiplan> fiplan, Supabase sb)
        {
            var porNumero = new Dictionary<string, NotaFiplan>();
            foreach (var n in fiplan) porNumero[n.NumrEmp] = n;

            var c = new Comparacao();
            var notas = sb.Listar("processo_notas_empenho", "id,numero,valor,pagamentos,estornos");
            c.Total = notas.Count;
            foreach (var n in notas)
            {
                NotaFiplan f;
                if (!porNumero.TryGetValue(SoDigitos(n["numero"] as string), out f)) { c.NaoEncontradas++; continue; }
                decimal valor = Dec(n, "valor"), pago = Dec(n, "pagamentos"), estornos = Dec(n, "estornos");
                if (Diferente(valor, f.Empenhado))
                    c.AvisosValor.Add(new AvisoValor { Numero = (string)n["numero"], Sistema = valor, Fiplan = f.Empenhado });
                if (Diferente(pago, f.Pago) || Diferente(estornos, f.Estornado))
                    c.Mudancas.Add(new Mudanca
                    {
                        Id = n["id"].ToString(), Numero = (string)n["numero"], Exercicio = f.Exercicio,
                        PagoAntes = pago, PagoDepois = f.Pago, EstornosAntes = estornos, EstornosDepois = f.Estornado, Saldo = f.Saldo,
                    });
            }
            return c;
        }

        static int GravarValidacao(Supabase sb, List<NotaFiplan> notas)
        {
            string agora = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            for (int i = 0; i < notas.Count; i += Lote)
            {
                var lote = notas.Skip(i).Take(Lote).Select(n => new Dictionary<string, object>
                {
                    { "numr_emp", n.NumrEmp }, { "numero", n.Numero }, { "cd_exercicio", n.Exercicio },
                    { "nome_credor", n.NomeCredor }, { "cpf_cnpj_credor", n.CpfCnpjCredor },
                    { "cd_elemento_despesa", n.ElementoCodigo }, { "ds_elemento_despesa", n.ElementoDescricao },
                    { "fonte", n.Fonte }, { "codg_dotacao_orcamentaria", n.Dotacao },
                    { "empenhado", n.Empenhado }, { "estornado", n.Estornado }, { "saldo", n.Saldo }, { "pago", n.Pago },
                    { "ds_situacao_emp", n.Situacao }, { "sincronizado_em", agora },
                }).ToList();
                sb.Exigir("POST", "fiplan_notas_empenho?on_conflict=numr_emp", lote, "resolution=merge-duplicates,return=minimal");
            }
            return notas.Count;
        }

        // Executa uma sincronização. Em simulação não grava nada. Erros saem como ErroSync.
        public static Resumo Executar(Config c, bool simulacao, Action<string> log)
        {
            var relogio = Stopwatch.StartNew();
            var resumo = new Resumo();
            log("[" + DateTime.Now.ToString("dd/MM/yyyy, HH:mm:ss") + "] Lendo FIPLAN (UO " + Fiplan.UnidadeOrcamentaria + ")...");
            var notas = Fiplan.Ler(c);
            resumo.Lidas = notas.Count;
            log(notas.Count + " notas lidas.");

            var sb = new Supabase(c);
            if (c.TabelaValidacao)
            {
                if (simulacao) log("Simulação: fiplan_notas_empenho não foi gravada.");
                else
                {
                    resumo.Gravadas = GravarValidacao(sb, notas);
                    log("fiplan_notas_empenho: " + resumo.Gravadas + " notas gravadas.");
                }
            }

            if (c.AtualizarNotas)
            {
                var cmp = Comparar(notas, sb);
                foreach (var a in cmp.AvisosValor)
                    log("  aviso: " + a.Numero + " valor no sistema " + Fmt(a.Sistema) + " ≠ FIPLAN " + Fmt(a.Fiplan) + " (não alterado)");
                foreach (var m in cmp.Mudancas)
                {
                    log("  " + m.Numero + ": pago " + Fmt(m.PagoAntes) + " → " + Fmt(m.PagoDepois) +
                        " | estornos " + Fmt(m.EstornosAntes) + " → " + Fmt(m.EstornosDepois));
                    if (simulacao) continue;
                    sb.Exigir("PATCH", "processo_notas_empenho?id=eq." + Uri.EscapeDataString(m.Id), new Dictionary<string, object>
                    {
                        { "pagamentos", m.PagoDepois }, { "estornos", m.EstornosDepois },
                        { "updated_at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) },
                    }, "return=minimal");
                }
                log("processo_notas_empenho: " + cmp.Mudancas.Count + " de " + cmp.Total + " notas " +
                    (simulacao ? "seriam atualizadas" : "atualizadas") +
                    (cmp.NaoEncontradas > 0 ? " (" + cmp.NaoEncontradas + " não encontradas no FIPLAN)" : "") + ".");
                if (!simulacao) resumo.Atualizadas = cmp.Mudancas;
                resumo.AvisosValor = cmp.AvisosValor;
                resumo.NaoEncontradas = cmp.NaoEncontradas;
            }
            if (!c.TabelaValidacao && !c.AtualizarNotas)
                log("Nada a fazer: a tabela de validação e a atualização das notas estão desligadas.");

            log("Concluído em " + relogio.Elapsed.TotalSeconds.ToString("0.0", Br) + "s.");
            return resumo;
        }

        // Texto do que a atualização mudaria, em colunas, para a tela de conferência
        public static string Simular(Config c)
        {
            var cmp = Comparar(Fiplan.Ler(c), new Supabase(c));
            var t = new StringBuilder();
            Func<decimal, string> valor = v => Fmt(v).PadLeft(12);
            t.AppendLine((cmp.Total - cmp.NaoEncontradas) + " de " + cmp.Total + " notas do sistema foram encontradas no FIPLAN.");
            if (cmp.Mudancas.Count == 0)
                t.AppendLine("Todas já estão iguais ao FIPLAN. Nada seria alterado agora.");
            else
            {
                t.AppendLine(cmp.Mudancas.Count + " notas seriam atualizadas (sistema → FIPLAN):");
                foreach (var m in cmp.Mudancas)
                {
                    t.AppendLine();
                    string numero = m.Numero;
                    if (Diferente(m.PagoAntes, m.PagoDepois))
                    {
                        t.AppendLine(numero.PadRight(22) + "  pago      " + valor(m.PagoAntes) + " → " + valor(m.PagoDepois));
                        numero = "";
                    }
                    if (Diferente(m.EstornosAntes, m.EstornosDepois))
                        t.AppendLine(numero.PadRight(22) + "  estornos  " + valor(m.EstornosAntes) + " → " + valor(m.EstornosDepois));
                }
            }
            if (cmp.AvisosValor.Count > 0)
            {
                t.AppendLine();
                t.AppendLine("Valor empenhado diferente do FIPLAN em " + cmp.AvisosValor.Count + " nota(s)");
                t.AppendLine("(não é alterado, só para conferência):");
                t.AppendLine();
                foreach (var a in cmp.AvisosValor)
                    t.AppendLine(a.Numero.PadRight(22) + "  sistema " + valor(a.Sistema) + "  FIPLAN " + valor(a.Fiplan));
            }
            return t.ToString().TrimEnd();
        }

        // Relatório completo (uma linha por nota do sistema), para conferência em planilha
        public static string CompararCsv(Config c)
        {
            var fiplan = Fiplan.Ler(c).ToDictionary(n => n.NumrEmp);
            var notas = new Supabase(c).Listar("processo_notas_empenho", "id,processo_id,numero,valor,pagamentos,estornos");
            Func<decimal, string> n2 = v => v.ToString("0.##", Br);
            var csv = new StringBuilder("processo_id;numero;numr_emp;exercicio;valor_sis;empenhado_fip;pagamentos_sis;pago_fip;estornos_sis;estornado_fip;saldo_fip;situacao\r\n");
            foreach (var n in notas)
            {
                NotaFiplan f;
                fiplan.TryGetValue(SoDigitos(n["numero"] as string), out f);
                decimal valor = Dec(n, "valor"), pago = Dec(n, "pagamentos"), est = Dec(n, "estornos");
                string situacao = "NAO ENCONTRADA";
                if (f != null)
                {
                    var d = new List<string>();
                    if (Diferente(valor, f.Empenhado)) d.Add("valor");
                    if (Diferente(pago, f.Pago)) d.Add("pago");
                    if (Diferente(est, f.Estornado)) d.Add("estorno");
                    situacao = d.Count == 0 ? "OK" : "DIFERENTE (" + string.Join(", ", d) + ")";
                }
                csv.Append(string.Join(";", new[]
                {
                    "\"" + n["processo_id"] + "\"", "\"" + n["numero"] + "\"", f == null ? "" : "\"" + f.NumrEmp + "\"",
                    f == null ? "" : f.Exercicio.ToString(), n2(valor), f == null ? "" : n2(f.Empenhado),
                    n2(pago), f == null ? "" : n2(f.Pago), n2(est), f == null ? "" : n2(f.Estornado),
                    f == null ? "" : n2(f.Saldo), "\"" + situacao + "\"",
                })).Append("\r\n");
            }
            return csv.ToString();
        }
    }
}
