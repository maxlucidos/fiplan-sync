// Sincronização: lê o FIPLAN, compara com as notas do sistema e manda as diferenças para validação
// (staging_notas_empenho) e/ou atualiza pagamentos/estornos direto em processo_notas_empenho.
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
        public string Id, Numero, ProcessoId;
        public int Exercicio;
        public decimal PagoAntes, PagoDepois, EstornosAntes, EstornosDepois, Saldo, Empenhado;
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
        public int Lidas, NaoEncontradas;
        public List<Mudanca> Atualizadas = new List<Mudanca>();      // aplicadas direto em processo_notas_empenho
        public List<Mudanca> ParaValidar = new List<Mudanca>();      // pendentes na staging_notas_empenho
        public int StagingNovas, StagingAtualizadas, StagingRemovidas;
        public List<AvisoValor> AvisosValor = new List<AvisoValor>();
    }

    public static class Sincronizacao
    {
        // Início do contexto_extracao das linhas criadas pelo programa: é assim que ele reconhece
        // as próprias linhas na staging, sem mexer nas da IA nem nas já validadas
        public const string Marca = "FIPLAN Sync:";
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

        static string Agora() { return DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture); }

        // Compara as notas do sistema com o FIPLAN e lista as que têm pagamentos/estornos diferentes.
        // O valor empenhado não é alterado: divergências de valor só viram aviso.
        public static Comparacao Comparar(List<NotaFiplan> fiplan, Supabase sb)
        {
            var porNumero = new Dictionary<string, NotaFiplan>();
            foreach (var n in fiplan) porNumero[n.NumrEmp] = n;

            var c = new Comparacao();
            var notas = sb.Listar("processo_notas_empenho", "id,processo_id,numero,valor,pagamentos,estornos", "");
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
                        Id = n["id"].ToString(), Numero = (string)n["numero"], ProcessoId = n["processo_id"] as string,
                        Exercicio = f.Exercicio, Empenhado = f.Empenhado, Saldo = f.Saldo,
                        PagoAntes = pago, PagoDepois = f.Pago, EstornosAntes = estornos, EstornosDepois = f.Estornado,
                    });
            }
            return c;
        }

        static string Contexto(Mudanca m)
        {
            return Marca + " view ACWVW0111 em " + DateTime.Now.ToString("dd/MM/yyyy HH:mm") +
                   " · empenhado R$ " + Fmt(m.Empenhado) + " · pago R$ " + Fmt(m.PagoDepois) +
                   " · estornado R$ " + Fmt(m.EstornosDepois) + " · saldo R$ " + Fmt(m.Saldo) +
                   " (no sistema: pago R$ " + Fmt(m.PagoAntes) + " · estornos R$ " + Fmt(m.EstornosAntes) + ")";
        }

        // Mantém na staging_notas_empenho uma linha "pendente" por nota diferente do FIPLAN:
        // cria as que faltam, atualiza as que mudaram e apaga as que já não são necessárias
        // (nota corrigida). Só mexe em linhas pendentes criadas pelo programa (Marca no contexto).
        static void EnviarParaValidacao(Supabase sb, List<Mudanca> diferencas, bool simulacao, Resumo r, Action<string> log)
        {
            var pendentes = sb.Listar("staging_notas_empenho", "id,empenho_id,valor_pgto_ia,valor_estorno_ia",
                "&status=eq.pendente&contexto_extracao=like." + Uri.EscapeDataString(Marca + "*"));
            var porEmpenho = pendentes.GroupBy(p => p["empenho_id"] as string).ToDictionary(g => g.Key, g => g.ToList());

            var novas = new List<Dictionary<string, object>>();
            var remover = new List<string>();
            var atendidas = new HashSet<string>();
            foreach (var m in diferencas)
            {
                if (string.IsNullOrEmpty(m.ProcessoId))
                {
                    log("  aviso: " + m.Numero + " está sem processo no sistema; não vai para a validação.");
                    continue;
                }
                r.ParaValidar.Add(m);
                atendidas.Add(m.Id);
                List<Dictionary<string, object>> existentes;
                if (porEmpenho.TryGetValue(m.Id, out existentes))
                {
                    var p = existentes[0];
                    remover.AddRange(existentes.Skip(1).Select(x => x["id"].ToString()));  // duplicadas
                    if (!Diferente(Dec(p, "valor_pgto_ia"), m.PagoDepois) && !Diferente(Dec(p, "valor_estorno_ia"), m.EstornosDepois)) continue;
                    r.StagingAtualizadas++;
                    if (!simulacao)
                        sb.Exigir("PATCH", "staging_notas_empenho?id=eq." + Uri.EscapeDataString(p["id"].ToString()), new Dictionary<string, object>
                        {
                            { "valor_pgto_ia", m.PagoDepois }, { "valor_estorno_ia", m.EstornosDepois },
                            { "contexto_extracao", Contexto(m) }, { "updated_at", Agora() },
                        }, "return=minimal");
                }
                else
                {
                    r.StagingNovas++;
                    novas.Add(new Dictionary<string, object>
                    {
                        { "empenho_id", m.Id }, { "processo_id", m.ProcessoId }, { "numero_empenho", m.Numero },
                        { "valor_pgto_ia", m.PagoDepois }, { "valor_estorno_ia", m.EstornosDepois },
                        { "contexto_extracao", Contexto(m) }, { "status", "pendente" },
                    });
                }
            }
            remover.AddRange(pendentes.Where(p => !atendidas.Contains(p["empenho_id"] as string)).Select(p => p["id"].ToString()));
            r.StagingRemovidas = remover.Count;

            if (!simulacao)
            {
                for (int i = 0; i < novas.Count; i += 500)
                    sb.Exigir("POST", "staging_notas_empenho", novas.Skip(i).Take(500).ToList(), "return=minimal");
                for (int i = 0; i < remover.Count; i += 100)
                    sb.Exigir("DELETE", "staging_notas_empenho?id=in.(" + string.Join(",", remover.Skip(i).Take(100)) + ")", null, "return=minimal");
            }
            log("staging_notas_empenho: " + r.ParaValidar.Count + (r.ParaValidar.Count == 1 ? " nota" : " notas") + " para validar (" +
                r.StagingNovas + " novas, " + r.StagingAtualizadas + " atualizadas, " +
                r.StagingRemovidas + " removidas porque já batem com o FIPLAN)" + (simulacao ? " [simulação, nada gravado]" : "") + ".");
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

            if (!c.EnviarValidacao && !c.AtualizarNotas)
            {
                log("Nada a fazer: o envio para validação e a atualização das notas estão desligados.");
                return resumo;
            }

            var sb = new Supabase(c);
            var cmp = Comparar(notas, sb);
            resumo.AvisosValor = cmp.AvisosValor;
            resumo.NaoEncontradas = cmp.NaoEncontradas;
            foreach (var a in cmp.AvisosValor)
                log("  aviso: " + a.Numero + " valor no sistema " + Fmt(a.Sistema) + " ≠ FIPLAN " + Fmt(a.Fiplan) + " (não alterado)");
            log(cmp.Total + " notas no sistema, " + cmp.Mudancas.Count + " com pago ou estorno diferente do FIPLAN" +
                (cmp.NaoEncontradas > 0 ? " (" + cmp.NaoEncontradas + " não encontradas no FIPLAN)" : "") + ".");

            if (c.AtualizarNotas)
            {
                foreach (var m in cmp.Mudancas)
                {
                    log("  " + m.Numero + ": pago " + Fmt(m.PagoAntes) + " → " + Fmt(m.PagoDepois) +
                        " | estornos " + Fmt(m.EstornosAntes) + " → " + Fmt(m.EstornosDepois));
                    if (simulacao) continue;
                    sb.Exigir("PATCH", "processo_notas_empenho?id=eq." + Uri.EscapeDataString(m.Id), new Dictionary<string, object>
                    {
                        { "pagamentos", m.PagoDepois }, { "estornos", m.EstornosDepois }, { "updated_at", Agora() },
                    }, "return=minimal");
                }
                log("processo_notas_empenho: " + cmp.Mudancas.Count + " notas " + (simulacao ? "seriam atualizadas" : "atualizadas") + ".");
                if (!simulacao) resumo.Atualizadas = cmp.Mudancas;
            }

            // Com a atualização direta ligada as diferenças já foram resolvidas: a validação só
            // limpa as pendências antigas do programa
            if (c.EnviarValidacao)
                EnviarParaValidacao(sb, c.AtualizarNotas && !simulacao ? new List<Mudanca>() : cmp.Mudancas, simulacao, resumo, log);

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
            var notas = new Supabase(c).Listar("processo_notas_empenho", "id,processo_id,numero,valor,pagamentos,estornos", "");
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
