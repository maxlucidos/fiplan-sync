// Janela "Histórico": uma linha por execução e, embaixo, o detalhe da execução selecionada.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace FiplanSync
{
    public class JanelaHistorico : Form
    {
        readonly ListView lista;
        readonly TextBox detalhe;
        List<Execucao> execucoes = new List<Execucao>();

        public JanelaHistorico(Action atualizarAgora)
        {
            Text = "FIPLAN Sync · Histórico";
            Icon = Icones.Ok;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(820, 540);
            MinimumSize = new Size(560, 380);
            StartPosition = FormStartPosition.CenterScreen;

            lista = new ListView
            {
                Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false,
                HideSelection = false, HeaderStyle = ColumnHeaderStyle.Nonclickable,
            };
            lista.Columns.Add("Data", 130);
            lista.Columns.Add("Situação", 80);
            lista.Columns.Add("Notas lidas", 90, HorizontalAlignment.Right);
            lista.Columns.Add("Atualizadas", 90, HorizontalAlignment.Right);
            lista.Columns.Add("Resumo", 380);
            lista.SelectedIndexChanged += (s, e) => MostrarDetalhe();

            detalhe = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both,
                WordWrap = false, Font = new Font("Consolas", 9.5f), BackColor = SystemColors.Window,
            };

            var divisao = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
            divisao.Panel1.Controls.Add(lista);
            divisao.Panel2.Controls.Add(detalhe);

            var botoes = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(8),
            };
            var fechar = new Button { Text = "Fechar", AutoSize = true };
            fechar.Click += (s, e) => Close();
            var atualizar = new Button { Text = "Atualizar agora", AutoSize = true };
            atualizar.Click += (s, e) => atualizarAgora();
            var pasta = new Button { Text = "Abrir pasta", AutoSize = true };
            pasta.Click += (s, e) => Process.Start("explorer.exe", "\"" + Config.Pasta + "\"");
            botoes.Controls.AddRange(new Control[] { fechar, atualizar, pasta });
            CancelButton = fechar;

            Controls.Add(divisao);
            Controls.Add(botoes);
            Load += (s, e) => divisao.SplitterDistance = (int)(divisao.Height * 0.45);
        }

        public void Recarregar(List<Execucao> todas)
        {
            execucoes = new List<Execucao>(todas);
            execucoes.Reverse();  // mais recente primeiro
            lista.BeginUpdate();
            lista.Items.Clear();
            foreach (var e in execucoes)
            {
                var item = new ListViewItem(e.Quando.ToString("dd/MM/yyyy HH:mm"));
                item.SubItems.Add(e.Ok ? "OK" : "Erro");
                item.SubItems.Add(e.Ok ? e.Lidas.ToString("N0") : "");
                item.SubItems.Add(e.Ok ? e.Atualizadas.ToString("N0") : "");
                item.SubItems.Add(e.Resumo + (e.Manual ? " (manual)" : ""));
                if (!e.Ok) item.ForeColor = Color.FromArgb(180, 35, 24);
                lista.Items.Add(item);
            }
            lista.EndUpdate();
            if (lista.Items.Count > 0) lista.Items[0].Selected = true;
            else detalhe.Text = "Ainda não houve nenhuma atualização.";
        }

        void MostrarDetalhe()
        {
            if (lista.SelectedIndices.Count == 0) return;
            detalhe.Text = execucoes[lista.SelectedIndices[0]].Detalhe.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        }
    }
}
