// Atualização do próprio programa pelas releases do GitHub: procura a release mais nova,
// baixa o instalador, confere o SHA-256 publicado pelo GitHub e roda em modo de atualização.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace FiplanSync
{
    public class Lancamento
    {
        public Version Versao;
        public string Texto;          // "1.2.0"
        public string Notas;          // descrição da release
        public string UrlInstalador;
        public string Sha256;         // vazio se o GitHub não informar
        public string Pagina;         // página da release
    }

    public static class Atualizador
    {
        // Repositório com as releases. Pode ser trocado em config.json ("repositorioAtualizacao");
        // se o repositório for transferido de conta, o GitHub redireciona o endereço antigo.
        public const string RepositorioPadrao = "maxlucidos/fiplan-sync";
        const string NomeInstalador = "FIPLAN-Sync-Setup.exe";

        static readonly HttpClient http;

        static Atualizador()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("FIPLAN-Sync/" + VersaoAtualTexto);  // o GitHub exige
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        public static Version VersaoAtual
        {
            get { var v = Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, v.Build); }
        }

        public static string VersaoAtualTexto { get { return VersaoAtual.ToString(); } }

        // Release mais nova publicada, ou null se não houver ou não tiver o instalador anexado
        public static Lancamento ProcurarUltima(string repositorio)
        {
            var res = http.GetAsync("https://api.github.com/repos/" + repositorio + "/releases/latest").GetAwaiter().GetResult();
            if (res.StatusCode == HttpStatusCode.NotFound) return null;
            if (!res.IsSuccessStatusCode)
                throw new Exception("O GitHub respondeu HTTP " + (int)res.StatusCode + " ao procurar atualização.");
            var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(res.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            Version versao;
            string tag = ((d["tag_name"] as string) ?? "").TrimStart('v', 'V');
            if (!Version.TryParse(tag, out versao)) return null;

            var assets = d["assets"] as ArrayList ?? new ArrayList();
            var instalador = assets.Cast<Dictionary<string, object>>().FirstOrDefault(a => NomeInstalador.Equals(a["name"]));
            if (instalador == null) return null;

            object digest;
            string sha = instalador.TryGetValue("digest", out digest) && digest is string && ((string)digest).StartsWith("sha256:")
                ? ((string)digest).Substring(7) : "";
            return new Lancamento
            {
                Versao = new Version(versao.Major, versao.Minor, Math.Max(versao.Build, 0)),
                Texto = tag,
                Notas = (d["body"] as string) ?? "",
                UrlInstalador = (string)instalador["browser_download_url"],
                Sha256 = sha,
                Pagina = (d["html_url"] as string) ?? "",
            };
        }

        public static bool EhMaisNova(Lancamento l)
        {
            return l != null && l.Versao > VersaoAtual;
        }

        // Baixa o instalador para a pasta temporária e confere o SHA-256 (quando o GitHub informa)
        public static string Baixar(Lancamento l)
        {
            string arquivo = Path.Combine(Path.GetTempPath(), "FIPLAN-Sync-Setup-" + l.Texto + ".exe");
            var res = http.GetAsync(l.UrlInstalador).GetAwaiter().GetResult();
            if (!res.IsSuccessStatusCode) throw new Exception("Não consegui baixar o instalador (HTTP " + (int)res.StatusCode + ").");
            File.WriteAllBytes(arquivo, res.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult());

            if (l.Sha256 != "")
            {
                string calculado;
                using (var sha = SHA256.Create())
                using (var f = File.OpenRead(arquivo))
                    calculado = BitConverter.ToString(sha.ComputeHash(f)).Replace("-", "").ToLowerInvariant();
                if (calculado != l.Sha256.ToLowerInvariant())
                {
                    File.Delete(arquivo);
                    throw new Exception("O arquivo baixado não confere com o publicado no GitHub (SHA-256 diferente). A atualização foi cancelada.");
                }
            }
            return arquivo;
        }

        // Roda o instalador em modo de atualização: sem telas de configuração, mantendo a atual,
        // e reabrindo o programa no fim. Quem chama deve fechar o programa logo em seguida.
        public static void Instalar(string instalador)
        {
            Process.Start(new ProcessStartInfo(instalador, "/SILENT /SUPPRESSMSGBOXES /NORESTART /ATUALIZACAO") { UseShellExecute = true });
        }
    }
}
