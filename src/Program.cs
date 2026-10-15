// FIPLAN Sync.exe: app de bandeja e, com argumentos, comandos para o instalador.
//
//   (sem argumentos)         abre o app de bandeja
//   --primeira-execucao      abre e já atualiza
//   --configuracoes          abre (ou traz) a janela de Configurações
//   --sair                   fecha a instância que está rodando
//   --importar <arquivo>     grava a configuração a partir de um arquivo KEY=VALUE (senhas criptografadas)
//   --exportar <arquivo>     escreve a configuração atual em KEY=VALUE (para o instalador preencher as telas)
//   --gerar-icone <arquivo>  gera o .ico do programa (usado pelo build)
//   --testar-oracle, --testar-supabase, --staging-existe, --simular,
//   --sync, --dry-run, --comparar, --desagendar      ver Comandos.cs
//
// Os comandos usam o config.json; com "--env <arquivo>" usam um arquivo KEY=VALUE
// (é assim que o instalador testa os acessos antes de instalar). Com "--saida <arquivo>"
// o resultado vai para esse arquivo, com o código de saída na primeira linha.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FiplanSync
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [STAThread]
        static int Main(string[] args)
        {
            // O driver do Oracle vai embutido no .exe: carrega da memória quando for pedido
            AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
            {
                if (!e.Name.StartsWith("Oracle.ManagedDataAccess,")) return null;
                using (var r = Assembly.GetExecutingAssembly().GetManifestResourceStream("Oracle.ManagedDataAccess.dll"))
                using (var m = new MemoryStream())
                {
                    r.CopyTo(m);
                    return Assembly.Load(m.ToArray());
                }
            };
            return Iniciar(args);
        }

        // Separado do Main para que nada que dependa do driver seja carregado antes do AssemblyResolve
        [MethodImpl(MethodImplOptions.NoInlining)]
        static int Iniciar(string[] args)
        {
            string modo = args.Length > 0 ? args[0] : "";
            if (Comandos.Nomes.Contains(modo)) return ComandoNoConsole(modo, args);
            try
            {
                switch (modo)
                {
                    case "--importar":
                        var c = Config.Carregar();
                        c.Importar(args[1]);
                        c.Salvar();
                        return 0;
                    case "--exportar":
                        Config.Carregar().Exportar(args[1]);
                        return 0;
                    case "--gerar-icone":
                        Icones.SalvarIco(args[1]);
                        return 0;
                    case "--sair":
                        return FecharInstancia() ? 0 : 1;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            bool primeira;
            using (var unica = new Mutex(true, @"Local\FIPLANSync", out primeira))
            {
                if (!primeira)
                {
                    // Já está aberto (ex.: clicou no atalho do menu Iniciar): mostra a janela pedida
                    EventWaitHandle sinal;
                    string nome = args.Contains("--configuracoes") ? AppBandeja.SinalConfig : AppBandeja.SinalMostrar;
                    if (EventWaitHandle.TryOpenExisting(nome, out sinal)) sinal.Set();
                    return 0;
                }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new AppBandeja(args.Contains("--primeira-execucao"), args.Contains("--configuracoes")));
            }
            return 0;
        }

        static int ComandoNoConsole(string comando, string[] args)
        {
            // Com "--saida <arquivo>" o resultado vai para um arquivo (é assim que o instalador acompanha
            // o comando sem travar a janela); sem ele, para a saída padrão em UTF-8
            int s = Array.IndexOf(args, "--saida");
            string arquivoSaida = s >= 0 && s + 1 < args.Length ? args[s + 1] : null;
            if (arquivoSaida == null)
                return Comando(comando, args, new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });

            var texto = new StringWriter();
            int codigo;
            try { codigo = Comando(comando, args, texto); }
            catch (Exception ex) { texto.WriteLine("Erro inesperado: " + ex.Message); codigo = 1; }
            // Primeira linha: o código de saída. Grava num temporário e renomeia, para o arquivo
            // só aparecer quando estiver completo
            File.WriteAllText(arquivoSaida + ".tmp", "CODIGO=" + codigo + "\r\n" + texto, new UTF8Encoding(true));
            if (File.Exists(arquivoSaida)) File.Delete(arquivoSaida);
            File.Move(arquivoSaida + ".tmp", arquivoSaida);
            return codigo;
        }

        static int Comando(string comando, string[] args, TextWriter saida)
        {
            Config c;
            int i = Array.IndexOf(args, "--env");
            if (i >= 0 && i + 1 < args.Length)
            {
                c = new Config();
                c.Importar(args[i + 1]);
            }
            else c = Config.Carregar();

            Resposta r;
            if (comando == "--sync" || comando == "--dry-run")
            {
                // Mostra o log enquanto roda (o Executar só devolveria no fim)
                try
                {
                    Sincronizacao.Executar(c, comando == "--dry-run", saida.WriteLine);
                    return 0;
                }
                catch (Exception ex)
                {
                    saida.WriteLine("ERRO: " + ex.Message);
                    return 1;
                }
            }
            r = Comandos.Executar(comando, c);
            if (r.Texto != "") saida.WriteLine(r.Texto);
            return r.Codigo;
        }

        static bool FecharInstancia()
        {
            EventWaitHandle sinal;
            if (!EventWaitHandle.TryOpenExisting(AppBandeja.SinalSair, out sinal)) return true;  // não está rodando
            sinal.Set();
            Mutex unica;
            if (!Mutex.TryOpenExisting(@"Local\FIPLANSync", out unica)) return true;
            try
            {
                if (!unica.WaitOne(15000)) return false;
                unica.ReleaseMutex();
            }
            catch (AbandonedMutexException) { }
            return true;
        }
    }

    // Abrir junto com o Windows (HKCU\...\Run): não precisa de administrador
    public static class Inicializacao
    {
        const string Chave = @"Software\Microsoft\Windows\CurrentVersion\Run";

        public static bool Ligada
        {
            get
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Chave))
                    return k != null && k.GetValue(AppBandeja.Nome) != null;
            }
        }

        public static void Definir(bool ligar)
        {
            using (var k = Registry.CurrentUser.CreateSubKey(Chave))
            {
                if (ligar) k.SetValue(AppBandeja.Nome, "\"" + Application.ExecutablePath + "\"");
                else if (k.GetValue(AppBandeja.Nome) != null) k.DeleteValue(AppBandeja.Nome);
            }
        }
    }
}
