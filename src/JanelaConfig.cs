// Janela "Configurações": acesso ao FIPLAN, ao Supabase e opções de funcionamento.
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FiplanSync
{
    public class JanelaConfig : Form
    {
        public event Action<Config> Salvo;

        readonly Config original;
        readonly TextBox txUsuario, txSenha, txServidor, txUrl, txChave;
        readonly Label lbOracle, lbSupabase;
        readonly CheckBox ckValidacao, ckAtualizar, ckIniciar;
        readonly ComboBox cbIntervalo, cbNotificar;
        readonly DateTimePicker dtInicio, dtFim;
        readonly Label lbDas, lbAs, lbAgenda;
        readonly CheckBox[] ckDias = new CheckBox[7];
        readonly Button btSalvar;
        readonly TabControl abas;

        static readonly string[] NotificarCodigos = { "tudo", "mudancas", "erros" };
        static readonly string[] NotificarTextos =
        {
            "Sempre que atualizar",
            "Só quando alguma nota mudar ou der erro",
            "Só quando der erro",
        };

        public JanelaConfig(Config config)
        {
            original = config;
            Text = "FIPLAN Sync " + Atualizador.VersaoAtualTexto + " · Configurações";
            Icon = Icones.Ok;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, 400);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            abas = new TabControl { Dock = DockStyle.Fill };

            // FIPLAN
            var fiplan = Grade();
            txUsuario = Campo(fiplan, "Usuário:", config.OracleUsuario);
            txSenha = Campo(fiplan, "Senha:", config.OracleSenha);
            txSenha.UseSystemPasswordChar = true;
            txServidor = Campo(fiplan, "Servidor:", config.OracleConexao);
            Dica(fiplan, "Deixe o servidor em branco para usar o padrão do FIPLAN (192.168.221.21, SID ormtp6).");
            lbOracle = Teste(fiplan, "Testar conexão", TestarOracle);
            abas.TabPages.Add(Aba("FIPLAN", fiplan));

            // Supabase
            var supa = Grade();
            txUrl = Campo(supa, "URL do projeto:", config.SupabaseUrl);
            txChave = Campo(supa, "Chave secret:", config.SupabaseChave);
            txChave.UseSystemPasswordChar = true;
            var mostrar = new CheckBox { Text = "Mostrar a chave", AutoSize = true };
            mostrar.CheckedChanged += (s, e) => txChave.UseSystemPasswordChar = !mostrar.Checked;
            supa.Controls.Add(new Label(), 0, supa.RowCount);
            supa.Controls.Add(mostrar, 1, supa.RowCount++);
            Dica(supa, "No Supabase: Project Settings > API Keys > Secret keys (sb_secret_...).");
            lbSupabase = Teste(supa, "Testar conexão", TestarSupabase);
            abas.TabPages.Add(Aba("Supabase", supa));

            // Opções
            var opcoes = Grade();
            ckValidacao = Opcao(opcoes, "Enviar as diferenças para validação (staging_notas_empenho)", config.EnviarValidacao,
                "As notas do sistema com pago ou estorno diferente do FIPLAN ficam pendentes para um operador validar. Não altera as notas.");
            ckAtualizar = Opcao(opcoes, "Atualizar pagamentos e estornos das notas do sistema", config.AtualizarNotas,
                "Ao ligar, mostra antes a lista do que mudaria. O valor empenhado nunca é alterado.");
            cbNotificar = Lista(opcoes, "Notificações:", NotificarTextos, Array.IndexOf(NotificarCodigos, config.Notificar));
            if (!NotificacoesDoWindowsLigadas())
                Dica(opcoes, "⚠ As notificações estão desligadas no Windows deste computador " +
                             "(Configurações > Sistema > Notificações). O ícone continua mostrando o estado, mas os avisos não aparecem.");
            ckIniciar = new CheckBox { Text = "Abrir o FIPLAN Sync quando o Windows iniciar", AutoSize = true, Checked = Inicializacao.Ligada };
            opcoes.Controls.Add(ckIniciar, 0, opcoes.RowCount++);
            opcoes.SetColumnSpan(ckIniciar, 2);
            abas.TabPages.Add(Aba("Opções", opcoes));

            // Agendamento
            var agenda = Grade();
            agenda.AutoScroll = false;  // cabe inteiro; com AutoScroll aparecia uma barra sem ter o que rolar
            cbIntervalo = Lista(agenda, "Frequência:", Agenda.TextosIntervalo, Math.Max(0, Array.IndexOf(Agenda.Intervalos, config.Intervalo)));
            lbDas = new Label { AutoSize = true, Margin = new Padding(3, 7, 3, 3) };
            dtInicio = Horario(config.Inicio);
            lbAs = new Label { Text = "às", AutoSize = true, Margin = new Padding(6, 7, 3, 3) };
            dtFim = Horario(config.Fim);
            var horario = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            horario.Controls.AddRange(new Control[] { lbDas, dtInicio, lbAs, dtFim });
            agenda.Controls.Add(new Label { Text = "Horário:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }, 0, agenda.RowCount);
            agenda.Controls.Add(horario, 1, agenda.RowCount++);
            var dias = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 2, 0, 2) };
            for (int i = 0; i < 7; i++)
            {
                ckDias[i] = new CheckBox { Text = Agenda.NomesDias[i], AutoSize = true, Checked = config.Dias.Contains((i + 1).ToString()) };
                ckDias[i].CheckedChanged += (s2, e2) => AtualizarAgenda();
                dias.Controls.Add(ckDias[i]);
            }
            agenda.Controls.Add(new Label { Text = "Dias:", AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }, 0, agenda.RowCount);
            agenda.Controls.Add(dias, 1, agenda.RowCount++);
            lbAgenda = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 10, 3, 3) };
            agenda.Controls.Add(new Label(), 0, agenda.RowCount);
            agenda.Controls.Add(lbAgenda, 1, agenda.RowCount++);
            Dica(agenda, "Se o computador estiver desligado ou dormindo no horário, a atualização acontece um minuto depois de ele voltar.");
            cbIntervalo.SelectedIndexChanged += (s2, e2) => AtualizarAgenda();
            dtInicio.ValueChanged += (s2, e2) => AtualizarAgenda();
            dtFim.ValueChanged += (s2, e2) => AtualizarAgenda();
            abas.TabPages.Add(Aba("Agendamento", agenda));
            AtualizarAgenda();

            var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
            var cancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
            btSalvar = new Button { Text = "Salvar", AutoSize = true };
            btSalvar.Click += (s, e) => Salvar();
            botoes.Controls.AddRange(new Control[] { cancelar, btSalvar });
            AcceptButton = btSalvar;
            CancelButton = cancelar;

            Controls.Add(abas);
            Controls.Add(botoes);

            if (!config.Completa) abas.SelectedIndex = config.OracleSenha == "" ? 0 : 1;
        }

        // Chave "Notificações" em Configurações > Sistema do Windows
        static bool NotificacoesDoWindowsLigadas()
        {
            using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\PushNotifications"))
                return k == null || !(0.Equals(k.GetValue("ToastEnabled")));
        }

        // --- Agendamento ---

        static DateTimePicker Horario(int minutos)
        {
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true, Width = 70,
                Value = DateTime.Today.AddMinutes(minutos), Margin = new Padding(3, 4, 3, 4),
            };
        }

        void LerAgenda(Config c)
        {
            c.Intervalo = Agenda.Intervalos[cbIntervalo.SelectedIndex];
            c.Inicio = dtInicio.Value.Hour * 60 + dtInicio.Value.Minute;
            c.Fim = dtFim.Value.Hour * 60 + dtFim.Value.Minute;
            c.Dias = "";
            for (int i = 0; i < 7; i++) if (ckDias[i].Checked) c.Dias += (i + 1).ToString();
        }

        // Liga/desliga os campos conforme a frequência e mostra o resumo do que foi escolhido
        void AtualizarAgenda()
        {
            int intervalo = Agenda.Intervalos[cbIntervalo.SelectedIndex];
            bool manual = intervalo < 0, umaVez = intervalo == 0;
            lbDas.Text = umaVez ? "às" : "das";
            lbAs.Visible = dtFim.Visible = !umaVez;
            dtInicio.Enabled = dtFim.Enabled = !manual;
            foreach (var ck in ckDias) ck.Enabled = !manual;

            var c = new Config();
            LerAgenda(c);
            string problema = Agenda.Validar(c);
            lbAgenda.ForeColor = problema == null ? SystemColors.ControlText : Color.FromArgb(180, 35, 24);
            lbAgenda.Text = problema ?? Agenda.Descrever(c) + ".";
        }

        // --- Montagem dos campos ---

        static TableLayoutPanel Grade()
        {
            var g = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Padding = new Padding(12), AutoScroll = true };
            g.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            g.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return g;
        }

        static TabPage Aba(string titulo, Control conteudo)
        {
            var aba = new TabPage(titulo) { UseVisualStyleBackColor = true };
            aba.Controls.Add(conteudo);
            return aba;
        }

        static TextBox Campo(TableLayoutPanel g, string rotulo, string valor)
        {
            var tx = new TextBox { Text = valor, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };
            g.Controls.Add(new Label { Text = rotulo, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }, 0, g.RowCount);
            g.Controls.Add(tx, 1, g.RowCount++);
            return tx;
        }

        static void Dica(TableLayoutPanel g, string texto)
        {
            var lb = new Label { Text = texto, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(420, 0), Margin = new Padding(3, 2, 3, 8) };
            g.Controls.Add(new Label(), 0, g.RowCount);
            g.Controls.Add(lb, 1, g.RowCount++);
        }

        Label Teste(TableLayoutPanel g, string texto, Func<Task> acao)
        {
            var bt = new Button { Text = texto, AutoSize = true };
            var lb = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Margin = new Padding(3, 6, 3, 3) };
            var linha = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
            linha.Controls.Add(bt);
            linha.Controls.Add(lb);
            bt.Click += async (s, e) => await acao();
            g.Controls.Add(new Label(), 0, g.RowCount);
            g.Controls.Add(linha, 1, g.RowCount++);
            return lb;
        }

        static CheckBox Opcao(TableLayoutPanel g, string texto, bool valor, string descricao)
        {
            var ck = new CheckBox { Text = texto, Checked = valor, AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
            var lb = new Label { Text = descricao, AutoSize = true, ForeColor = SystemColors.GrayText, MaximumSize = new Size(480, 0), Margin = new Padding(21, 0, 3, 8) };
            g.Controls.Add(ck, 0, g.RowCount);
            g.SetColumnSpan(ck, 2);
            g.RowCount++;
            g.Controls.Add(lb, 0, g.RowCount);
            g.SetColumnSpan(lb, 2);
            g.RowCount++;
            return ck;
        }

        static ComboBox Lista(TableLayoutPanel g, string rotulo, string[] itens, int selecionado)
        {
            var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Margin = new Padding(3, 4, 3, 4) };
            cb.Items.AddRange(itens);
            cb.SelectedIndex = Math.Max(0, selecionado);
            g.Controls.Add(new Label { Text = rotulo, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 7, 8, 3) }, 0, g.RowCount);
            g.Controls.Add(cb, 1, g.RowCount++);
            return cb;
        }

        // --- Ações ---

        Config DaTela()
        {
            var c = original.Copia();
            c.OracleUsuario = txUsuario.Text.Trim();
            c.OracleSenha = txSenha.Text;
            c.OracleConexao = txServidor.Text.Trim();
            c.SupabaseUrl = txUrl.Text.Trim();
            c.SupabaseChave = txChave.Text.Trim();
            c.EnviarValidacao = ckValidacao.Checked;
            c.AtualizarNotas = ckAtualizar.Checked;
            LerAgenda(c);
            c.Notificar = NotificarCodigos[cbNotificar.SelectedIndex];
            return c;
        }

        async Task<Resposta> Rodar(string comando, Config c)
        {
            UseWaitCursor = true;
            abas.Enabled = btSalvar.Enabled = false;
            try
            {
                return await Task.Run(() => Comandos.Executar(comando, c));
            }
            finally
            {
                UseWaitCursor = false;
                abas.Enabled = btSalvar.Enabled = true;
            }
        }

        static void Mostrar(Label lb, Resposta r)
        {
            lb.ForeColor = r.Ok ? Color.FromArgb(29, 122, 70) : Color.FromArgb(180, 35, 24);
            lb.Text = r.Ok ? "✓ " + r.Texto.Replace("OK ", "") : r.Texto;
        }

        async Task TestarOracle()
        {
            lbOracle.ForeColor = SystemColors.ControlText;
            lbOracle.Text = "Testando...";
            Mostrar(lbOracle, await Rodar("--testar-oracle", DaTela()));
        }

        async Task TestarSupabase()
        {
            lbSupabase.ForeColor = SystemColors.ControlText;
            lbSupabase.Text = "Testando...";
            var r = await Rodar("--testar-supabase", DaTela());
            if (r.Ok)
            {
                txUrl.Text = r.Texto.Substring(3).Trim();  // "OK <url normalizada>"
                lbSupabase.ForeColor = Color.FromArgb(29, 122, 70);
                lbSupabase.Text = "✓ Conectado";
            }
            else Mostrar(lbSupabase, r);
        }

        async void Salvar()
        {
            var c = DaTela();
            if (!c.Completa)
            {
                MessageBox.Show(this, "Preencha o usuário e a senha do FIPLAN, a URL e a chave do Supabase.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string problemaAgenda = Agenda.Validar(c);
            if (problemaAgenda != null)
            {
                MessageBox.Show(this, problemaAgenda, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                abas.SelectedIndex = 3;
                return;
            }
            if (!c.EnviarValidacao && !c.AtualizarNotas)
            {
                MessageBox.Show(this, "Ligue pelo menos uma opção: o envio para validação ou a atualização das notas.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                abas.SelectedIndex = 2;
                return;
            }

            if (c.EnviarValidacao && !await ConferirStaging(c)) return;

            // Ligando a atualização agora: mostra o que mudaria antes de confirmar
            if (c.AtualizarNotas && !original.AtualizarNotas)
            {
                var r = await Rodar("--simular", c);
                if (!r.Ok)
                {
                    MessageBox.Show(this, "Não consegui comparar com o FIPLAN:\n\n" + r.Texto, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                if (!JanelaSimulacao.Confirmar(this, r.Texto)) return;
            }

            c.Salvar();
            Inicializacao.Definir(ckIniciar.Checked);
            if (Salvo != null) Salvo(c);
            Close();
        }

        async Task<bool> ConferirStaging(Config c)
        {
            var r = await Rodar("--staging-existe", c);
            if (r.Ok) return true;
            MessageBox.Show(this, r.Texto == "" ? "Não consegui consultar o Supabase." : r.Texto, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            abas.SelectedIndex = 1;
            return false;
        }
    }

    // Lista do que mudaria nas notas, com confirmação explícita
    public class JanelaSimulacao : Form
    {
        public static bool Confirmar(IWin32Window dono, string texto)
        {
            using (var j = new JanelaSimulacao(texto)) return j.ShowDialog(dono) == DialogResult.OK;
        }

        JanelaSimulacao(string texto)
        {
            Text = "FIPLAN Sync · O que mudaria nas notas";
            Icon = Icones.Ok;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(720, 500);
            StartPosition = FormStartPosition.CenterParent;

            var tx = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false,
                Font = new Font("Consolas", 9.5f), BackColor = SystemColors.Window,
                Text = texto.Replace("\r\n", "\n").Replace("\n", Environment.NewLine),
            };
            var ck = new CheckBox { Text = "&Conferi e quero aplicar essas mudanças agora e nas próximas atualizações", AutoSize = true, Dock = DockStyle.Bottom, Padding = new Padding(8, 8, 8, 0) };
            var botoes = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8) };
            var cancelar = new Button { Text = "Cancelar", AutoSize = true, DialogResult = DialogResult.Cancel };
            var ok = new Button { Text = "Ligar a atualização", AutoSize = true, DialogResult = DialogResult.OK, Enabled = false };
            ck.CheckedChanged += (s, e) => ok.Enabled = ck.Checked;
            botoes.Controls.AddRange(new Control[] { cancelar, ok });
            CancelButton = cancelar;

            Controls.Add(tx);
            Controls.Add(ck);
            Controls.Add(botoes);
        }
    }
}
