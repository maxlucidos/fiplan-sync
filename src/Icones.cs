// Ícones desenhados em código: um círculo colorido com uma letra, um para cada estado.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace FiplanSync
{
    public static class Icones
    {
        static readonly Color Azul = Color.FromArgb(31, 95, 174);
        static readonly Color Ambar = Color.FromArgb(214, 140, 0);
        static readonly Color Vermelho = Color.FromArgb(196, 43, 28);
        static readonly Color Cinza = Color.FromArgb(122, 133, 145);

        public static readonly Icon Ok = Criar(Azul, "F");
        public static readonly Icon Rodando = Criar(Ambar, "F");
        public static readonly Icon Erro = Criar(Vermelho, "!");
        public static readonly Icon Parado = Criar(Cinza, "F");

        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        static Bitmap Desenhar(Color cor, string letra, int tamanho)
        {
            var bmp = new Bitmap(tamanho, tamanho, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            using (var fundo = new SolidBrush(cor))
            using (var fonte = new Font("Segoe UI", tamanho * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var formato = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                float m = tamanho / 32f;
                g.FillEllipse(fundo, m, m, tamanho - 2 * m, tamanho - 2 * m);
                g.DrawString(letra, fonte, Brushes.White, new RectangleF(0, tamanho * 0.03f, tamanho, tamanho), formato);
            }
            return bmp;
        }

        static Icon Criar(Color cor, string letra)
        {
            using (var bmp = Desenhar(cor, letra, 32))
            {
                IntPtr h = bmp.GetHicon();
                var icone = (Icon)Icon.FromHandle(h).Clone();
                DestroyIcon(h);
                return icone;
            }
        }

        // .ico com vários tamanhos (PNG dentro do ICO, aceito desde o Windows Vista); usado pelo build
        public static void SalvarIco(string arquivo)
        {
            int[] tamanhos = { 16, 24, 32, 48, 256 };
            var imagens = new List<byte[]>();
            foreach (int t in tamanhos)
                using (var bmp = Desenhar(Azul, "F", t))
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    imagens.Add(ms.ToArray());
                }

            using (var w = new BinaryWriter(File.Create(arquivo)))
            {
                w.Write((short)0); w.Write((short)1); w.Write((short)tamanhos.Length);
                int deslocamento = 6 + 16 * tamanhos.Length;
                for (int i = 0; i < tamanhos.Length; i++)
                {
                    w.Write((byte)(tamanhos[i] >= 256 ? 0 : tamanhos[i]));
                    w.Write((byte)(tamanhos[i] >= 256 ? 0 : tamanhos[i]));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(imagens[i].Length);
                    w.Write(deslocamento);
                    deslocamento += imagens[i].Length;
                }
                foreach (var img in imagens) w.Write(img);
            }
        }
    }
}
