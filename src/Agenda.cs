// Horários de execução: a cada N minutos dentro de uma janela do dia, nos dias da semana escolhidos.
using System;
using System.Collections.Generic;
using System.Linq;

namespace FiplanSync
{
    public static class Agenda
    {
        // Opções de intervalo (minutos). 0 = uma vez por dia; -1 = só manual.
        public static readonly int[] Intervalos = { 15, 30, 60, 120, 180, 240, 0, -1 };
        public static readonly string[] TextosIntervalo =
        {
            "A cada 15 minutos", "A cada 30 minutos", "De hora em hora", "A cada 2 horas",
            "A cada 3 horas", "A cada 4 horas", "Uma vez por dia", "Só manual (pelo \"Atualizar agora\")",
        };
        public static readonly string[] NomesDias = { "Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom" };
        static readonly string[] NomesDiasLongos = { "segunda", "terça", "quarta", "quinta", "sexta", "sábado", "domingo" };

        static List<int> MinutosDoDia(Config c)
        {
            var lista = new List<int>();
            if (c.Intervalo < 0) return lista;
            if (c.Intervalo == 0) { lista.Add(c.Inicio); return lista; }
            for (int m = c.Inicio; m <= c.Fim; m += c.Intervalo) lista.Add(m);
            return lista;
        }

        // 1 = segunda ... 7 = domingo
        static bool DiaPermitido(Config c, DateTime d)
        {
            int dia = d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek;
            return c.Dias.Contains(dia.ToString());
        }

        // Primeiro horário depois de "depois", ou null se não há agendamento
        public static DateTime? Proxima(Config c, DateTime depois)
        {
            var minutos = MinutosDoDia(c);
            for (int dia = 0; dia < 8 && minutos.Count > 0; dia++)
            {
                var data = depois.Date.AddDays(dia);
                if (!DiaPermitido(c, data)) continue;
                foreach (int m in minutos)
                {
                    var h = data.AddMinutes(m);
                    if (h > depois) return h;
                }
            }
            return null;
        }

        // Último horário que já passou (para recuperar execuções perdidas com o PC desligado)
        public static DateTime? Anterior(Config c, DateTime agora)
        {
            var minutos = MinutosDoDia(c);
            for (int dia = 0; dia < 8 && minutos.Count > 0; dia++)
            {
                var data = agora.Date.AddDays(-dia);
                if (!DiaPermitido(c, data)) continue;
                for (int i = minutos.Count - 1; i >= 0; i--)
                {
                    var h = data.AddMinutes(minutos[i]);
                    if (h <= agora) return h;
                }
            }
            return null;
        }

        // Problema na combinação escolhida, ou null se está tudo certo
        public static string Validar(Config c)
        {
            if (c.Intervalo < 0) return null;
            if (c.Dias == "") return "Marque pelo menos um dia da semana.";
            if (c.Intervalo > 0 && c.Fim <= c.Inicio) return "O horário final precisa ser depois do inicial.";
            return null;
        }

        // "De hora em hora, das 07:00 às 19:00, de segunda a sexta"
        public static string Descrever(Config c)
        {
            if (c.Intervalo < 0) return "Só manual, pelo \"Atualizar agora\"";
            int i = Array.IndexOf(Intervalos, c.Intervalo);
            string freq = i >= 0 ? TextosIntervalo[i] : "A cada " + c.Intervalo + " minutos";
            string quando = c.Intervalo == 0 ? "às " + Config.Hora(c.Inicio) : "das " + Config.Hora(c.Inicio) + " às " + Config.Hora(c.Fim);
            return freq + ", " + quando + ", " + DescreverDias(c.Dias);
        }

        static string DescreverDias(string dias)
        {
            var d = dias.Where(char.IsDigit).Select(ch => ch - '0').Where(n => n >= 1 && n <= 7).Distinct().OrderBy(n => n).ToList();
            if (d.Count == 7) return "todos os dias";
            if (d.SequenceEqual(new[] { 1, 2, 3, 4, 5 })) return "de segunda a sexta";
            if (d.SequenceEqual(new[] { 6, 7 })) return "aos sábados e domingos";
            bool seguidos = d.Count > 2 && d.Last() - d.First() == d.Count - 1;
            if (seguidos) return "de " + NomesDiasLongos[d.First() - 1] + " a " + NomesDiasLongos[d.Last() - 1];
            var nomes = d.Select(n => NomesDiasLongos[n - 1]).ToList();
            return nomes.Count == 1 ? "só " + nomes[0]
                 : string.Join(", ", nomes.Take(nomes.Count - 1)) + " e " + nomes.Last();
        }
    }
}
