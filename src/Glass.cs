// Тихий ПК — тема «Скло»: під вікном розмите тло робочого стола, а картки й панелі напівпрозорі.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TyhyiPK
{
    static partial class Ui
    {
        public static bool Glass;
        public static string Theme = "light";
        public static Control Host;      // головне вікно — від нього рахуємо, яку частину тла показувати
        static Bitmap backdrop;          // розмите тло розміром з вікно
        static Bitmap wallSmall;         // маленька розмита копія шпалер
        static string wallKey;
        static Color glassAvg = Color.FromArgb(34, 38, 52);

        // Затемнення поверх шпалер, щоб білий текст читався на будь-якому фоні (на світлих шпалерах — сильніше).
        static Color Veil = Color.FromArgb(128, 12, 14, 22);

        static Color A(int a, int r, int g, int b) { return Color.FromArgb(a, r, g, b); }

        static void GlassPalette()
        {
            EnsureWall();
            Ink = C(255, 255, 255); Ink2 = C(224, 226, 234); Ink3 = C(172, 176, 190);
            Card = A(36, 255, 255, 255); CardLine = A(52, 255, 255, 255); Separator = A(34, 255, 255, 255);
            Fill = A(30, 255, 255, 255); FillHover = A(50, 255, 255, 255);
            SegText = C(236, 236, 242); SegSep = A(46, 255, 255, 255); SegDisabled = A(14, 255, 255, 255);
            Thumb = A(80, 255, 255, 255); ToggleOff = A(64, 255, 255, 255);
            RowSel = A(96, 10, 132, 255); RowHover = A(26, 255, 255, 255);
            Side = A(52, 0, 0, 0);
            ShadowAlpha = 40;
            Bg = glassAvg;   // непрозорий середній колір — для стандартних полів і заголовка вікна
        }

        // Напівпрозорий колір → непрозорий (як він виглядає поверх середнього тла).
        public static Color Solid(Color c)
        {
            if (c.A == 255) return c;
            float a = c.A / 255f;
            return Color.FromArgb((int)(Bg.R + (c.R - Bg.R) * a), (int)(Bg.G + (c.G - Bg.G) * a), (int)(Bg.B + (c.B - Bg.B) * a));
        }

        // Фон елемента: у звичайних темах — суцільний колір, у «Склі» — шматок розмитого тла під ним
        // плюс напівпрозорий відтінок поверхні (картки, бокової панелі).
        public static void PaintBack(Graphics g, Control c, Color surface)
        {
            try { PaintBackCore(g, c, surface); }
            catch { g.Clear(Solid(surface)); }
        }

        static void PaintBackCore(Graphics g, Control c, Color surface)
        {
            if (!Glass || backdrop == null || Host == null || c.IsDisposed || !c.IsHandleCreated || c.FindForm() != Host)
            {
                g.Clear(Solid(surface));
                return;
            }
            Point o;
            try { o = c == Host ? Point.Empty : Host.PointToClient(c.PointToScreen(Point.Empty)); }
            catch { g.Clear(Solid(surface)); return; }
            var oldInterp = g.InterpolationMode;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.Clear(Bg);
            var src = new Rectangle(o.X, o.Y, c.Width, c.Height);
            src.Intersect(new Rectangle(0, 0, backdrop.Width, backdrop.Height));
            if (src.Width > 0 && src.Height > 0)
                g.DrawImage(backdrop, new Rectangle(src.X - o.X, src.Y - o.Y, src.Width, src.Height), src, GraphicsUnit.Pixel);
            g.InterpolationMode = oldInterp;
            if (surface.A < 255 && surface.A > 0)
                using (var b = new SolidBrush(surface)) g.FillRectangle(b, 0, 0, c.Width, c.Height);
        }

        // Картка з тонкою рамкою: у «Склі» рамка малюється лінією, щоб прозорі шари не накладалися.
        public static void CardShape(Graphics g, RectangleF r, float rad)
        {
            if (!Glass)
            {
                FillRound(g, r, rad, CardLine);
                FillRound(g, RectangleF.Inflate(r, -Math.Max(1f, Scale), -Math.Max(1f, Scale)), rad - 1, Card);
                return;
            }
            FillRound(g, r, rad, Card);
            float w = Math.Max(1f, Scale);
            using (var p = Round(RectangleF.Inflate(r, -w / 2, -w / 2), rad - w / 2))
            using (var pen = new Pen(CardLine, w))
                g.DrawPath(pen, p);
        }

        // Перебудувати розмите тло під новий розмір вікна.
        public static void BuildBackdrop(Size size)
        {
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
            if (!Glass || size.Width <= 0 || size.Height <= 0) return;
            EnsureWall();
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                // «cover»: заповнити вікно без перекосу, обрізавши зайве по краях.
                float k = Math.Max((float)size.Width / wallSmall.Width, (float)size.Height / wallSmall.Height);
                float w = wallSmall.Width * k, h = wallSmall.Height * k;
                using (var ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);   // без темної облямівки по краях
                    g.DrawImage(wallSmall, new Rectangle((int)((size.Width - w) / 2), (int)((size.Height - h) / 2), (int)Math.Ceiling(w), (int)Math.Ceiling(h)),
                        0, 0, wallSmall.Width, wallSmall.Height, GraphicsUnit.Pixel, ia);
                }
                using (var b = new SolidBrush(Veil)) g.FillRectangle(b, 0, 0, size.Width, size.Height);
                // Легкий світлий відблиск угорі — як на справжньому склі.
                using (var lg = new LinearGradientBrush(new Rectangle(0, 0, size.Width, Math.Max(2, size.Height / 2)),
                    Color.FromArgb(18, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90f))
                    g.FillRectangle(lg, 0, 0, size.Width, Math.Max(2, size.Height / 2));
            }
            backdrop = bmp;
        }

        // Чи змінилися шпалери (після зміни налаштувань Windows).
        public static bool WallpaperChanged()
        {
            return wallSmall != null && WallKey() != wallKey;
        }

        public static void ResetWallpaper()
        {
            if (wallSmall != null) { wallSmall.Dispose(); wallSmall = null; }
            wallKey = null;
        }

        static string WallPath()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop"))
                {
                    string p = k != null ? k.GetValue("WallPaper") as string : null;
                    if (!string.IsNullOrEmpty(p) && File.Exists(p)) return p;
                }
                string t = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft\\Windows\\Themes\\TranscodedWallpaper");
                if (File.Exists(t)) return t;
            }
            catch { }
            return null;
        }

        static string WallKey()
        {
            string p = WallPath();
            if (p == null) return "";
            try { return p + "|" + File.GetLastWriteTimeUtc(p).Ticks; } catch { return p; }
        }

        static void EnsureWall()
        {
            if (wallSmall != null) return;
            wallKey = WallKey();
            Bitmap src = null;
            string p = WallPath();
            if (p != null)
            {
                try
                {
                    // Читаємо в пам'ять, щоб не тримати файл шпалер відкритим.
                    using (var ms = new MemoryStream(File.ReadAllBytes(p)))
                    using (var img = Image.FromStream(ms))
                        src = Shrink(img, 64);
                }
                catch { src = null; }
            }
            if (src == null) src = Aurora();
            Blur(src, 2);
            Blur(src, 2);
            wallSmall = src;
            glassAvg = Average(src);
        }

        static void PickVeil(Color avg)
        {
            double lum = 0.2126 * avg.R + 0.7152 * avg.G + 0.0722 * avg.B;
            // Щоб середня яскравість під текстом була не більша за ~60 з 255.
            double a = lum > 60 ? (lum - 60) / Math.Max(1, lum - 16) : 0;
            a = Math.Max(0.45, Math.Min(0.82, a));
            Veil = Color.FromArgb((int)(a * 255), 12, 14, 22);
        }

        static Bitmap Shrink(Image img, int width)
        {
            int h = Math.Max(1, (int)Math.Round((double)img.Height * width / Math.Max(1, img.Width)));
            var b = new Bitmap(width, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(img, 0, 0, width, h);
            }
            return b;
        }

        // Якщо шпалер немає (суцільний колір) — м'яке «полярне сяйво» в синьо-фіолетових тонах.
        static Bitmap Aurora()
        {
            var b = new Bitmap(64, 40, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(b))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.FromArgb(18, 22, 44));
                Blob(g, -10, -14, 46, 38, Color.FromArgb(40, 110, 255));
                Blob(g, 30, -8, 44, 34, Color.FromArgb(150, 70, 230));
                Blob(g, 14, 18, 44, 32, Color.FromArgb(0, 170, 190));
                Blob(g, 44, 22, 30, 24, Color.FromArgb(230, 90, 160));
            }
            return b;
        }

        static void Blob(Graphics g, float x, float y, float w, float h, Color c)
        {
            using (var path = new GraphicsPath())
            {
                path.AddEllipse(x, y, w, h);
                using (var br = new PathGradientBrush(path))
                {
                    br.CenterColor = Color.FromArgb(210, c);
                    br.SurroundColors = new[] { Color.FromArgb(0, c) };
                    g.FillPath(br, path);
                }
            }
        }

        // Просте розмиття «коробкою» на маленькій картинці.
        static void Blur(Bitmap b, int rad)
        {
            int w = b.Width, h = b.Height;
            var px = new int[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) px[y * w + x] = b.GetPixel(x, y).ToArgb();
            var tmp = new int[w * h];
            for (int pass = 0; pass < 2; pass++)
            {
                bool horiz = pass == 0;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        int r = 0, gg = 0, bb = 0, n = 0;
                        for (int k = -rad; k <= rad; k++)
                        {
                            int xx = horiz ? Math.Max(0, Math.Min(w - 1, x + k)) : x;
                            int yy = horiz ? y : Math.Max(0, Math.Min(h - 1, y + k));
                            int c = px[yy * w + xx];
                            r += (c >> 16) & 255; gg += (c >> 8) & 255; bb += c & 255; n++;
                        }
                        tmp[y * w + x] = (255 << 24) | ((r / n) << 16) | ((gg / n) << 8) | (bb / n);
                    }
                var t = px; px = tmp; tmp = t;
            }
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) b.SetPixel(x, y, Color.FromArgb(px[y * w + x]));
        }

        static Color Average(Bitmap b)
        {
            long r = 0, g = 0, bl = 0, n = 0;
            for (int y = 0; y < b.Height; y++)
                for (int x = 0; x < b.Width; x++)
                {
                    var c = b.GetPixel(x, y);
                    r += c.R; g += c.G; bl += c.B; n++;
                }
            var avg = Color.FromArgb((int)(r / n), (int)(g / n), (int)(bl / n));
            PickVeil(avg);
            float a = Veil.A / 255f;
            return Color.FromArgb((int)(avg.R + (Veil.R - avg.R) * a), (int)(avg.G + (Veil.G - avg.G) * a), (int)(avg.B + (Veil.B - avg.B) * a));
        }
    }
}
