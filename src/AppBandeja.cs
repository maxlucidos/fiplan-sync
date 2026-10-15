// O app em si: ícone perto do relógio, menu, relógio de agendamento e notificações.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FiplanSync
{
    public class AppBandeja : ApplicationContext
    {
        public const string Nome = "FIPLAN Sync";

        readonly NotifyIcon icone;
        readonly ToolStripMenuItem itUltima, itProxima, itAtualizar, itPausar, itVersao;
        readonly System.Windows.Forms.Timer relogio;
        readonly Control ui;  // para trazer sinais de outras threads para a thread da interface

        Config config;
        readonly List<Execucao> historico;
        bool rodando;
        DateTime? proxima;
        Action acaoDoBalao;
        JanelaHistorico janelaHistorico;
        JanelaConfig janelaConfig;
        DateTime proximaVerificacao = DateTime.Now.AddMinutes(2);  // procura versão nova 2 min depois de abrir e 1x por dia
        bool verificando;
        Lancamento avisada;  // versão já avisada, para não repetir o aviso todo dia

        public AppBandeja(bool executarAgora, bool abrirConfig)
        {
            ui = new Control();
            ui.CreateControl();

            config = Config.Carregar();
            historico = Historico.Carregar();

            var menu = new ContextMenuStrip();
            itUltima = new ToolStripMenuItem { Enabled = false };
            itProxima = new ToolStripMenuItem { Enabled = false };
            itAtualizar = new ToolStripMenuItem("Atualizar agora", null, (s, e) => Executar(true));
            itPausar = new ToolStripMenuItem("Pausar atualizações", null, (s, e) => AlternarPausa());
            menu.Items.Add(itUltima);
            menu.Items.Add(itProxima);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itAtualizar);
            menu.Items.Add(new ToolStripMenuItem("Histórico...", null, (s, e) => AbrirHistorico()));
            menu.Items.Add(new ToolStripMenuItem("Configurações...", null, (s, e) => AbrirConfig()));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(itPausar);
            menu.Items.Add(new ToolStripSeparator());
            itVersao = new ToolStripMenuItem("Procurar atualização...", null, (s, e) => ProcurarAtualizacao(true));
            menu.Items.Add(itVersao);
            menu.Items.Add(new ToolStripMenuItem("Versão " + Atualizador.VersaoAtualTexto) { Enabled = false });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Sair", null, (s, e) => Sair()));
            itAtualizar.Font = new Font(itAtualizar.Font, FontStyle.Bold);

            icone = new NotifyIcon { ContextMenuStrip = menu, Visible = true, Icon = Icones.Ok, Text = Nome };
            icone.DoubleClick += (s, e) => AbrirHistorico();
            icone.BalloonTipClicked += (s, e) => { if (acaoDoBalao != null) acaoDoBalao(); };

            relogio = new System.Windows.Forms.Timer { Interval = 15000 };
            relogio.Tick += (s, e) =>
            {
                if (!rodando && proxima.HasValue && DateTime.Now >= proxima.Value) Executar(false);
                if (!verificando && DateTime.Now >= proximaVerificacao) ProcurarAtualizacao(false);
            };
            relogio.Start();

            OuvirSinais();
            Agendar(true);
            AtualizarEstado();

            if (abrirConfig) AbrirConfig();
            if (!config.Completa)
                Avisar("Configuração incompleta", "Clique aqui para informar o acesso ao FIPLAN e ao Supabase.", ToolTipIcon.Warning, AbrirConfig);
            else if (executarAgora)
                Executar(true);
        }

        // --- Agendamento ---

        void Agendar(bool recuperarPerdidas)
        {
            proxima = null;
            if (config.Pausado || !config.Completa) return;
            var agora = DateTime.Now;
            proxima = Agenda.Proxima(config, agora);

            // PC estava desligado no último horário: roda 1 minuto depois de abrir (dá tempo da rede subir)
            if (recuperarPerdidas)
            {
                var anterior = Agenda.Anterior(config, agora);
                var ultima = historico.Count > 0 ? historico[historico.Count - 1].Quando : (DateTime?)null;
                if (anterior.HasValue && (!ultima.HasValue || ultima.Value < anterior.Value))
                    proxima = agora.AddMinutes(1);
            }
        }

        // --- Execução ---

        async void Executar(bool manual)
        {
            if (rodando) return;
            if (!config.Completa)
            {
                Avisar("Configuração incompleta", "Clique aqui para informar o acesso ao FIPLAN e ao Supabase.", ToolTipIcon.Warning, AbrirConfig);
                return;
            }
            rodando = true;
            AtualizarEstado();

            var cfg = config.Copia();
            var log = new StringBuilder();
            Resumo resumo = null;
            Exception erro = null;
            await Task.Run(() =>
            {
                try { resumo = Sincronizacao.Executar(cfg, false, l => { lock (log) log.AppendLine(l); }); }
                catch (Exception ex) { erro = ex; lock (log) log.AppendLine("ERRO: " + ex.Message); }
            });
            var e = Execucao.De(resumo, erro, manual, cfg, log.ToString());
            var anterior = historico.Count > 0 ? historico[historico.Count - 1] : null;
            try { Historico.Adicionar(historico, e, log.ToString()); } catch { historico.Add(e); }

            rodando = false;
            Agendar(false);
            AtualizarEstado();
            Notificar(e, anterior);
            if (janelaHistorico != null && !janelaHistorico.IsDisposed) janelaHistorico.Recarregar(historico);
        }

        void Notificar(Execucao e, Execucao anterior)
        {
            if (!e.Ok)
            {
                // O mesmo erro de novo: o ícone já está vermelho, não repete o aviso (a não ser que tenha sido pedido)
                bool repetido = anterior != null && !anterior.Ok && anterior.Mensagem == e.Mensagem;
                if (repetido && !e.Manual) return;
                string texto = e.Mensagem;
                if (texto.Length > 180) texto = texto.Substring(0, 177) + "...";
                if (proxima.HasValue) texto += " Nova tentativa às " + proxima.Value.ToString("HH:mm") + ".";
                if (e.Configurar)
                    Avisar(e.Resumo, texto + " Clique para abrir as Configurações.", ToolTipIcon.Error, AbrirConfig);
                else
                    Avisar(e.Resumo, texto, ToolTipIcon.Error, AbrirHistorico);
                return;
            }
            bool houveMudanca = e.Atualizadas > 0 || e.Novas > 0;
            if (e.Manual || config.Notificar == "tudo" || (config.Notificar == "mudancas" && houveMudanca))
                Avisar(houveMudanca ? e.Resumo : "Atualizado com o FIPLAN",
                       houveMudanca ? "Clique para ver quais notas mudaram." : e.Resumo + ".",
                       ToolTipIcon.Info, AbrirHistorico);
        }

        void Avisar(string titulo, string texto, ToolTipIcon tipo, Action aoClicar)
        {
            acaoDoBalao = aoClicar;
            icone.ShowBalloonTip(10000, titulo, texto, tipo);
        }

        // --- Atualização do programa ---

        async void ProcurarAtualizacao(bool manual)
        {
            if (verificando) return;
            verificando = true;
            proximaVerificacao = DateTime.Now.AddDays(1);
            Lancamento l = null;
            Exception falha = null;
            string repositorio = config.RepositorioAtualizacao;
            await Task.Run(() => { try { l = Atualizador.ProcurarUltima(repositorio); } catch (Exception ex) { falha = ex; } });
            verificando = false;

            if (falha != null)
            {
                // Na verificação automática, sem internet ou GitHub fora do ar: tenta de novo amanhã, sem incomodar
                if (manual) MessageBox.Show("Não consegui procurar atualização:\n\n" + falha.Message, Nome, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Atualizador.EhMaisNova(l))
            {
                if (manual) MessageBox.Show("Você já está na versão mais recente (" + Atualizador.VersaoAtualTexto + ").", Nome, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            itVersao.Text = "Atualizar para a versão " + l.Texto + "...";
            if (manual) { Atualizar(l); return; }
            if (avisada != null && avisada.Versao == l.Versao) return;
            avisada = l;
            Avisar("FIPLAN Sync " + l.Texto + " disponível", "Clique para atualizar (você está na " + Atualizador.VersaoAtualTexto + ").",
                   ToolTipIcon.Info, () => Atualizar(l));
        }

        async void Atualizar(Lancamento l)
        {
            if (rodando)
            {
                MessageBox.Show("Espere a atualização das notas terminar e tente de novo.", Nome, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string notas = l.Notas.Length > 700 ? l.Notas.Substring(0, 700) + "..." : l.Notas;
            if (MessageBox.Show("Instalar a versão " + l.Texto + " agora?\n\n" + notas +
                                "\n\nO FIPLAN Sync fecha, atualiza e abre de novo sozinho. A configuração é mantida.",
                                Nome, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string arquivo = null;
            Exception falha = null;
            icone.Icon = Icones.Rodando;
            icone.Text = Nome + " · baixando a versão " + l.Texto + "...";
            await Task.Run(() => { try { arquivo = Atualizador.Baixar(l); } catch (Exception ex) { falha = ex; } });
            if (falha != null)
            {
                AtualizarEstado();
                MessageBox.Show("A atualização não foi feita:\n\n" + falha.Message, Nome, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            Atualizador.Instalar(arquivo);
            Encerrar();
        }

        // --- Ícone, dica e menu ---

        void AtualizarEstado()
        {
            var ultima = historico.Count > 0 ? historico[historico.Count - 1] : null;
            string dica;

            if (rodando)
            {
                icone.Icon = Icones.Rodando;
                dica = "atualizando...";
            }
            else if (!config.Completa)
            {
                icone.Icon = Icones.Parado;
                dica = "configuração incompleta";
            }
            else if (ultima != null && !ultima.Ok)
            {
                icone.Icon = Icones.Erro;
                dica = ultima.Resumo.ToLower() + " (" + ultima.Quando.ToString("HH:mm") + ")";
            }
            else if (config.Pausado)
            {
                icone.Icon = Icones.Parado;
                dica = "pausado";
            }
            else
            {
                icone.Icon = Icones.Ok;
                dica = ultima == null ? "aguardando a primeira atualização"
                     : ultima.Quando.ToString("HH:mm") + " · " + ultima.Resumo.ToLower();
            }
            string texto = Nome + " · " + dica;
            icone.Text = texto.Length > 63 ? texto.Substring(0, 63) : texto;  // limite do Windows

            itUltima.Text = ultima == null ? "Ainda não atualizou"
                : "Última: " + Quando(ultima.Quando) + " · " + ultima.Resumo;
            itProxima.Text = rodando ? "Atualizando agora..."
                : config.Pausado ? "Atualizações pausadas"
                : !config.Completa ? "Falta configurar"
                : proxima.HasValue ? "Próxima: " + Quando(proxima.Value)
                : "Sem agendamento";
            itAtualizar.Enabled = !rodando;
            itPausar.Checked = config.Pausado;
        }

        static string Quando(DateTime d)
        {
            if (d.Date == DateTime.Today) return "hoje " + d.ToString("HH:mm");
            if (d.Date == DateTime.Today.AddDays(1)) return "amanhã " + d.ToString("HH:mm");
            if (d.Date == DateTime.Today.AddDays(-1)) return "ontem " + d.ToString("HH:mm");
            return d.ToString("dd/MM HH:mm");
        }

        // --- Ações do menu ---

        void AlternarPausa()
        {
            config.Pausado = !config.Pausado;
            config.Salvar();
            Agendar(false);
            AtualizarEstado();
        }

        void AbrirHistorico()
        {
            if (janelaHistorico == null || janelaHistorico.IsDisposed)
            {
                janelaHistorico = new JanelaHistorico(() => Executar(true));
                janelaHistorico.Recarregar(historico);
            }
            Trazer(janelaHistorico);
        }

        void AbrirConfig()
        {
            if (janelaConfig == null || janelaConfig.IsDisposed)
            {
                janelaConfig = new JanelaConfig(config);
                janelaConfig.Salvo += novo =>
                {
                    // Faltava configuração ou o último erro era de acesso: tenta de novo já com o que foi salvo
                    var ultima = historico.Count > 0 ? historico[historico.Count - 1] : null;
                    bool tentarDeNovo = !config.Completa || (ultima != null && !ultima.Ok && ultima.Configurar);
                    config = novo;
                    Agendar(false);
                    AtualizarEstado();
                    if (tentarDeNovo && config.Completa) Executar(true);
                };
            }
            Trazer(janelaConfig);
        }

        static void Trazer(Form f)
        {
            if (!f.Visible) f.Show();
            if (f.WindowState == FormWindowState.Minimized) f.WindowState = FormWindowState.Normal;
            f.Activate();
        }

        void Sair()
        {
            var r = MessageBox.Show(
                "Enquanto o FIPLAN Sync estiver fechado, as notas não são atualizadas.\n\nEle volta a abrir sozinho quando o Windows iniciar.",
                Nome, MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
            if (r == DialogResult.OK) Encerrar();
        }

        void Encerrar()
        {
            relogio.Stop();
            icone.Visible = false;
            icone.Dispose();
            ExitThread();
        }

        // --- Sinais de outras instâncias (instalador fechando o app, atalho abrindo a janela) ---

        public const string SinalSair = @"Local\FIPLANSync.Sair";
        public const string SinalMostrar = @"Local\FIPLANSync.Mostrar";
        public const string SinalConfig = @"Local\FIPLANSync.Config";

        void OuvirSinais()
        {
            var sair = new EventWaitHandle(false, EventResetMode.AutoReset, SinalSair);
            var mostrar = new EventWaitHandle(false, EventResetMode.AutoReset, SinalMostrar);
            var configurar = new EventWaitHandle(false, EventResetMode.AutoReset, SinalConfig);
            var t = new Thread(() =>
            {
                while (true)
                {
                    int i = WaitHandle.WaitAny(new WaitHandle[] { sair, mostrar, configurar });
                    if (i == 0) { ui.BeginInvoke(new Action(Encerrar)); return; }
                    ui.BeginInvoke(new Action(i == 1 ? (Action)AbrirHistorico : AbrirConfig));
                }
            }) { IsBackground = true };
            t.Start();
        }
    }
}
