// Тихий ПК — анімований значок-вентилятор біля годинника і великий вентилятор для попереднього перегляду.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TyhyiPK
{
    static class FanArt
    {
        public static readonly string[] ColorKeys = { "blue", "green", "orange", "purple", "pink", "graphite", "temp" };
        public static readonly string[] ColorNames = { "Синій", "Зелений", "Помаранчевий", "Фіолетовий", "Рожевий", "Графіт", "За температурою" };

        public static Color Base(string key, double temp)
        {
            switch (key)
            {
                case "green": return Color.FromArgb(52, 199, 89);
                case "orange": return Color.FromArgb(255, 149, 0);
                case "purple": return Color.FromArgb(175, 82, 222);
                case "pink": return Color.FromArgb(255, 45, 85);
                case "graphite": return Color.FromArgb(110, 110, 118);
                case "temp": return TempColor(temp);
                default: return Color.FromArgb(0, 122, 255);
            }
        }

        // Від холодного синього до гарячого червоного.
        public static Color TempColor(double t)
        {
            if (double.IsNaN(t)) return Color.FromArgb(0, 122, 255);
            if (t < 55) return Color.FromArgb(0, 122, 255);
            if (t < 68) return Color.FromArgb(52, 199, 89);
            if (t < 78) return Color.FromArgb(240, 168, 0);
            if (t < 86) return Color.FromArgb(255, 149, 0);
            return Color.FromArgb(255, 59, 48);
        }

        // Вентилятор: кольорове коло і чотири білі лопаті, повернуті на кут angle.
        public static void Draw(Graphics g, RectangleF r, Color c, float angle)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var br = new LinearGradientBrush(r, Ui.Mix(c, Color.White, 0.25f), Ui.Mix(c, Color.Black, 0.12f), 90f))
                g.FillEllipse(br, r);
            float s = r.Width, cx = r.X + s / 2f, cy = r.Y + s / 2f;
            var st = g.Save();
            g.TranslateTransform(cx, cy);
            g.RotateTransform(angle);
            using (var w = new SolidBrush(Color.White))
            {
                for (int k = 0; k < 4; k++)
                {
                    using (var p = new GraphicsPath())
                    {
                        // Вигнута лопать: від маточини до краю.
                        p.AddBezier(0, -s * 0.08f, s * 0.16f, -s * 0.18f, s * 0.2f, -s * 0.36f, s * 0.04f, -s * 0.41f);
                        p.AddBezier(s * 0.04f, -s * 0.41f, -s * 0.1f, -s * 0.36f, -s * 0.1f, -s * 0.2f, 0, -s * 0.08f);
                        g.FillPath(w, p);
                    }
                    g.RotateTransform(90);
                }
                g.FillEllipse(w, -s * 0.1f, -s * 0.1f, s * 0.2f, s * 0.2f);
            }
            using (var hub = new SolidBrush(c)) g.FillEllipse(hub, -s * 0.045f, -s * 0.045f, s * 0.09f, s * 0.09f);
            g.Restore(st);
        }

        // Температура числом на кольоровій плитці.
        public static void DrawTemp(Graphics g, RectangleF r, Color c, double t)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var p = Ui.Round(r, r.Width * 0.24f))
            using (var br = new LinearGradientBrush(r, Ui.Mix(c, Color.White, 0.2f), Ui.Mix(c, Color.Black, 0.1f), 90f))
                g.FillPath(br, p);
            string text = double.IsNaN(t) ? "–" : Math.Min(99, Math.Round(t)).ToString();
            using (var f = new Font("Segoe UI Semibold", r.Height * 0.62f, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var w = new SolidBrush(Color.White))
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.DrawString(text, f, w, new RectangleF(r.X - r.Width, r.Y + r.Height * 0.04f, r.Width * 3, r.Height), sf);
            }
        }
    }

    // Набір іконок для значка біля годинника; звільняє дескриптори Windows, коли іконки більше не потрібні.
    class TrayIcons : IDisposable
    {
        [DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);

        public const int Frames = 8;   // 4 лопаті повторюються кожні 90°, тож 8 кадрів по 11,25°
        readonly Dictionary<int, Icon[]> fans = new Dictionary<int, Icon[]>();
        readonly List<IntPtr> handles = new List<IntPtr>();
        Icon tempIcon;
        string tempKey;

        static int Size { get { return Math.Max(16, SystemInformation.SmallIconSize.Width); } }

        Icon Make(Action<Graphics, RectangleF> draw)
        {
            int n = Size;
            using (var bmp = new Bitmap(n, n, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    draw(g, new RectangleF(0.5f, 0.5f, n - 1, n - 1));
                }
                IntPtr h = bmp.GetHicon();
                handles.Add(h);
                return Icon.FromHandle(h);
            }
        }

        public Icon Fan(Color c, int frame)
        {
            Icon[] set;
            int key = c.ToArgb();
            if (!fans.TryGetValue(key, out set))
            {
                if (fans.Count > 6) Clear();
                set = new Icon[Frames];
                for (int i = 0; i < Frames; i++)
                {
                    float a = i * 90f / Frames;
                    set[i] = Make((g, r) => FanArt.Draw(g, r, c, a));
                }
                fans[key] = set;
            }
            return set[((frame % Frames) + Frames) % Frames];
        }

        public Icon Temp(Color c, double t)
        {
            string key = c.ToArgb() + "|" + (double.IsNaN(t) ? "-" : Math.Round(t).ToString());
            if (key == tempKey && tempIcon != null) return tempIcon;
            tempKey = key;
            tempIcon = Make((g, r) => FanArt.DrawTemp(g, r, c, t));
            if (handles.Count > 200) Clear();
            return tempIcon;
        }

        void Clear()
        {
            // Старі дескриптори звільняємо із запасом: останні кілька можуть ще бути на панелі задач.
            int keep = Math.Min(handles.Count, Frames * 2);
            for (int i = 0; i < handles.Count - keep; i++) DestroyIcon(handles[i]);
            handles.RemoveRange(0, handles.Count - keep);
            fans.Clear();
            tempIcon = null;
            tempKey = null;
        }

        public void Dispose()
        {
            foreach (var h in handles) DestroyIcon(h);
            handles.Clear();
            fans.Clear();
        }
    }

    // Великий вентилятор на сторінці «Значок» — крутиться так само, як значок біля годинника.
    class FanPreview : UiControl
    {
        public Func<Color> ColorOf = () => Color.FromArgb(0, 122, 255);
        public Func<bool> ShowTemp = () => false;
        public Func<double> TempOf = () => double.NaN;
        public float Angle;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            float d = Math.Min(Width, Height) - Ui.S(8);
            var r = new RectangleF((Width - d) / 2f, (Height - d) / 2f, d, d);
            using (var sh = new SolidBrush(Color.FromArgb(Ui.Dark ? 60 : 24, 0, 0, 0))) g.FillEllipse(sh, r.X + Ui.S(2), r.Y + Ui.S(5), d, d);
            if (ShowTemp()) FanArt.DrawTemp(g, r, ColorOf(), TempOf());
            else FanArt.Draw(g, r, ColorOf(), Angle);
        }
    }

    // Рядок кольорових кружечків для вибору кольору.
    class Swatches : UiControl
    {
        public int Selected;
        int hover = -1;
        readonly ToolTip tip = new ToolTip();
        public event Action<int> Changed;

        public Swatches() { Cursor = Cursors.Hand; }

        RectangleF Dot(int i)
        {
            float d = Ui.S(26), gap = Ui.S(10);
            return new RectangleF(Width - (FanArt.ColorKeys.Length - i) * (d + gap) + gap - Ui.S(2), (Height - d) / 2f, d, d);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            for (int i = 0; i < FanArt.ColorKeys.Length; i++)
            {
                var r = Dot(i);
                if (i == Selected || i == hover)
                    using (var pen = new Pen(i == Selected ? Ui.Ink2 : Ui.Separator, Math.Max(1.5f, 2 * Ui.Scale)))
                        g.DrawEllipse(pen, RectangleF.Inflate(r, Ui.S(3), Ui.S(3)));
                if (FanArt.ColorKeys[i] == "temp")
                {
                    using (var br = new LinearGradientBrush(r, Color.FromArgb(0, 122, 255), Color.FromArgb(255, 59, 48), 0f))
                    {
                        var blend = new ColorBlend(3);
                        blend.Colors = new[] { Color.FromArgb(0, 122, 255), Color.FromArgb(255, 204, 0), Color.FromArgb(255, 59, 48) };
                        blend.Positions = new[] { 0f, 0.5f, 1f };
                        br.InterpolationColors = blend;
                        g.FillEllipse(br, r);
                    }
                }
                else using (var b = new SolidBrush(FanArt.Base(FanArt.ColorKeys[i], double.NaN))) g.FillEllipse(b, r);
            }
        }

        int Hit(Point p) { for (int i = 0; i < FanArt.ColorKeys.Length; i++) if (RectangleF.Inflate(Dot(i), Ui.S(4), Ui.S(4)).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hover) { hover = h; tip.SetToolTip(this, h >= 0 ? FanArt.ColorNames[h] : ""); Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = Hit(e.Location);
            if (h < 0) return;
            Selected = h;
            Invalidate();
            if (Changed != null) Changed(h);
        }
    }
}
