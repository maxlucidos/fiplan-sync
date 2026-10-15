// Acesso ao Supabase pela API REST (PostgREST) com a chave secret.
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web.Script.Serialization;

namespace FiplanSync
{
    public class Supabase
    {
        static readonly HttpClient http;
        static readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        readonly string url, chave;

        static Supabase()
        {
            // Sem isso o .NET Framework pode tentar TLS 1.0, que o Supabase recusa
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            // Por padrão o .NET Framework espera um "100-continue" antes de cada envio: uma pausa por lote
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;
            http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        }

        public Supabase(Config c)
        {
            if (c.SupabaseUrl == "" || c.SupabaseChave == "")
                throw new ErroSync("URL ou chave do Supabase não configuradas.", "supabase", true);
            url = new Uri(c.SupabaseUrl).GetLeftPart(UriPartial.Authority);
            chave = c.SupabaseChave;
        }

        public string Url { get { return url; } }

        // Envia e devolve (status, corpo). Não lança erro de HTTP: quem chama decide.
        public KeyValuePair<int, string> Enviar(string metodo, string caminho, object corpo, string prefer)
        {
            var req = new HttpRequestMessage(new HttpMethod(metodo), url + "/rest/v1/" + caminho);
            req.Headers.Add("apikey", chave);
            if (chave.Split('.').Length == 3) req.Headers.Add("Authorization", "Bearer " + chave);  // chave service_role antiga (JWT)
            if (prefer != null) req.Headers.Add("Prefer", prefer);
            if (corpo != null) req.Content = new StringContent(json.Serialize(corpo), Encoding.UTF8, "application/json");
            try
            {
                var res = http.SendAsync(req).GetAwaiter().GetResult();
                return new KeyValuePair<int, string>((int)res.StatusCode, res.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            }
            catch (Exception ex)
            {
                var causa = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                throw new ErroSync("Não consegui conectar ao Supabase: " + causa, "supabase", false);
            }
        }

        // Como Enviar, mas lança ErroSync se o Supabase responder com erro
        public string Exigir(string metodo, string caminho, object corpo, string prefer)
        {
            var r = Enviar(metodo, caminho, corpo, prefer);
            if (r.Key >= 200 && r.Key < 300) return r.Value;
            string tabela = caminho.Split('?')[0];
            if (r.Key == 401) throw new ErroSync("O Supabase recusou a chave (HTTP 401).", "supabase", true);
            throw new ErroSync("Supabase HTTP " + r.Key + " em " + tabela + ": " + r.Value, "supabase", false);
        }

        public List<Dictionary<string, object>> Listar(string tabela, string colunas)
        {
            var todas = new List<Dictionary<string, object>>();
            for (int offset = 0; ; offset += 1000)
            {
                var lote = json.Deserialize<List<Dictionary<string, object>>>(
                    Exigir("GET", tabela + "?select=" + colunas + "&order=id&offset=" + offset + "&limit=1000", null, null));
                todas.AddRange(lote);
                if (lote.Count < 1000) return todas;
            }
        }
    }
}
