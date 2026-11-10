// Тихий ПК — власні елементи інтерфейсу.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TyhyiPK
{
    class UiControl : Control
    {
        public bool OnBg;
        protected Color Back { get { return OnBg ? Ui.Bg : Ui.Card; } }

        public UiControl()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Font = Ui.Body;
        }

        protected Graphics Prep(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Back);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            return g;
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); Invalidate(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    }

    // Сегментований перемикач (як у macOS) з анімованим «бігунком».
    class Segmented : UiControl
    {
        string[] items = new string[0];
        public Func<int, Color> Accent;
        public bool FitText;   // ширина сегментів за довжиною тексту (для головних вкладок)
        int selected = -1, hover = -1;
        float thumb = -1;
        readonly Timer anim = new Timer();
        public event Action<int> ItemClicked;

        public Segmented()
        {
            Cursor = Cursors.Hand;
            anim.Interval = 15;
            anim.Tick += (s, e) =>
            {
                float d = selected - thumb;
                if (Math.Abs(d) < 0.02f) { thumb = selected; anim.Stop(); }
                else thumb += d * 0.3f;
                Invalidate();
            };
        }

        public string[] Items
        {
            get { return items; }
            set { items = value ?? new string[0]; Invalidate(); }
        }

        public int Selected
        {
            get { return selected; }
            set
            {
                if (value == selected) return;
                selected = value;
                if (thumb >= 0 && selected >= 0 && Visible && IsHandleCreated) anim.Start();
                else { thumb = selected; anim.Stop(); Invalidate(); }
            }
        }

        float Pad { get { return Ui.S(2); } }

        // Межі сегментів: рівні або пропорційні до тексту.
        float[] Edges()
        {
            int n = Math.Max(1, items.Length);
            var e = new float[n + 1];
            float total = Width - 2 * Pad;
            e[0] = Pad;
            if (!FitText || items.Length == 0)
            {
                for (int i = 1; i <= n; i++) e[i] = Pad + total * i / n;
                return e;
            }
            var w = new float[n];
            float sum = 0;
            for (int i = 0; i < n; i++)
            {
                w[i] = TextRenderer.MeasureText(items[i], Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                sum += w[i];
            }
            float extra = (total - sum) / n;
            for (int i = 0; i < n; i++) e[i + 1] = e[i] + w[i] + extra;
            e[n] = Pad + total;
            return e;
        }

        RectangleF Seg(float i)
        {
            var e = Edges();
            int n = e.Length - 1;
            int a = Math.Max(0, Math.Min(n - 1, (int)Math.Floor(i)));
            int b = Math.Min(n - 1, a + 1);
            float t = Math.Max(0, Math.Min(1, i - a));
            float x = e[a] + (e[b] - e[a]) * t;
            float w = (e[a + 1] - e[a]) + ((e[b + 1] - e[b]) - (e[a + 1] - e[a])) * t;
            return new RectangleF(x, Pad, w, Height - 2 * Pad);
        }

        int HitTest(Point p)
        {
            if (items.Length == 0) return -1;
            var e = Edges();
            for (int i = 0; i < items.Length; i++)
                if (p.X >= e[i] && p.X < e[i + 1]) return i;
            return -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            float rad = Ui.S(9);
            Ui.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), rad, Enabled ? Ui.Fill : Ui.SegDisabled);
            if (Enabled && hover >= 0 && hover != selected)
                Ui.FillRound(g, Seg(hover), rad - Pad, Ui.FillHover);
            using (var pen = new Pen(Ui.SegSep, Math.Max(1f, Ui.Scale)))
            {
                for (int i = 1; i < items.Length; i++)
                {
                    if (thumb >= 0 && i > thumb - 1 && i < thumb + 2) continue;
                    if (hover >= 0 && (i == hover || i == hover + 1)) continue;
                    var r = Seg(i);
                    g.DrawLine(pen, r.X, r.Y + r.Height * 0.28f, r.X, r.Bottom - r.Height * 0.28f);
                }
            }
            if (thumb >= 0 && items.Length > 0)
            {
                var t = Seg(thumb);
                Ui.FillRound(g, new RectangleF(t.X, t.Y + Ui.S(1), t.Width, t.Height), rad - Pad,
                    Color.FromArgb(Enabled ? Ui.ShadowAlpha : Ui.ShadowAlpha / 3, 0, 0, 0));
                Ui.FillRound(g, t, rad - Pad, Ui.Thumb);
            }
            for (int i = 0; i < items.Length; i++)
            {
                var r = Rectangle.Round(Seg(i));
                r.Inflate(-Ui.S(3), 0);
                bool sel = i == selected;
                Color c = Enabled ? (sel ? (Accent != null ? Accent(i) : Ui.Ink) : Ui.SegText) : Ui.Ink3;
                Ui.Text(g, items[i], sel ? Ui.BodySemi : Ui.Body, r, c, Ui.Center);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!(Enabled && e.Button == MouseButtons.Left)) return;
            int i = HitTest(e.Location);
            if (i >= 0 && ItemClicked != null) ItemClicked(i);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        }
    }

    // Перемикач «увімк./вимк.» як в iOS.
    class Toggle : UiControl
    {
        bool on;
        float pos;
        readonly Timer anim = new Timer();
        public event EventHandler Changed;

        public Toggle()
        {
            Cursor = Cursors.Hand;
            anim.Interval = 15;
            anim.Tick += (s, e) =>
            {
                float target = on ? 1 : 0;
                float d = target - pos;
                if (Math.Abs(d) < 0.03f) { pos = target; anim.Stop(); }
                else pos += d * 0.3f;
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return on; }
            set { on = value; pos = on ? 1 : 0; anim.Stop(); Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!(e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location))) return;
            on = !on;
            anim.Start();
            if (Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
            Ui.FillRound(g, r, r.Height / 2, Ui.Mix(Ui.ToggleOff, Ui.Green, pos));
            float pad = Ui.S(2);
            float d = r.Height - 2 * pad;
            float x = r.X + pad + pos * (r.Width - 2 * pad - d);
            using (var b = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
                g.FillEllipse(b, x, r.Y + pad + Ui.S(1), d, d);
            using (var b = new SolidBrush(Color.White))
                g.FillEllipse(b, x, r.Y + pad, d, d);
        }
    }

    // Текстове посилання (синє), за бажанням зі стрілкою «›» праворуч.
    class LinkBtn : UiControl
    {
        bool hover;
        public bool Chevron;
        public Func<Color> Tint;   // колір тексту, якщо не синій

        public LinkBtn() { Cursor = Cursors.Hand; }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            Color baseC = Tint != null ? Tint() : Ui.Blue;
            Color c = hover ? Ui.Mix(baseC, Back, 0.35f) : baseC;
            Ui.Text(g, Text, Font, ClientRectangle, c, Ui.Left);
            if (Chevron) Ui.Text(g, "›", Ui.Stat, new Rectangle(0, -Ui.S(2), Width, Height), Ui.Ink3, Ui.Right);
        }
    }

    // Кнопка-«пігулка».
    class PillBtn : UiControl
    {
        bool hover;
        public Func<Color> Fore = () => Ui.Blue;
        public Func<Color> FillColor;

        public PillBtn()
        {
            Cursor = Cursors.Hand;
            Font = Ui.BodySemi;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            Color c = FillColor != null
                ? (hover ? Ui.Mix(FillColor(), Color.Black, 0.12f) : FillColor())
                : (hover ? Ui.FillHover : Ui.Fill);
            Ui.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f, c);
            Ui.Text(g, Text, Font, ClientRectangle, Fore(), Ui.Center);
        }
    }

    // Поле часу (гг:хв) з меню на кожні 30 хвилин і прокручуванням коліщатком.
    class TimeBox : UiControl
    {
        TimeSpan value;
        bool hover;
        readonly ContextMenuStrip menu = new ContextMenuStrip();
        public event EventHandler Changed;

        public TimeBox()
        {
            Cursor = Cursors.Hand;
            Font = Ui.BodySemi;
            menu.ShowCheckMargin = true;
            menu.ShowImageMargin = false;
            EventHandler click = (s, e) => SetValue((TimeSpan)((ToolStripMenuItem)s).Tag, true);
            for (int m = 0; m < 1440; m += 30)
            {
                var t = TimeSpan.FromMinutes(m);
                var item = new ToolStripMenuItem(t.ToString("hh\\:mm"));
                item.Tag = t;
                item.Click += click;
                menu.Items.Add(item);
            }
        }

        public TimeSpan Value
        {
            get { return value; }
            set { SetValue(value, false); }
        }

        void SetValue(TimeSpan v, bool raise)
        {
            value = new TimeSpan((v.Hours % 24 + 24) % 24, v.Minutes, 0);
            Invalidate();
            if (raise && Changed != null) Changed(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            foreach (ToolStripMenuItem item in menu.Items) item.Checked = (TimeSpan)item.Tag == value;
            menu.Show(this, new Point(0, Height));
        }


        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            Ui.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Ui.S(8), hover ? Ui.FillHover : Ui.Fill);
            Ui.Text(g, value.ToString("hh\\:mm"), Font, ClientRectangle, Ui.Ink, Ui.Center);
        }
    }

    // Плитка показника: підпис, велике значення, рядок під ним (може бути посиланням).
    class StatTile : UiControl
    {
        string caption = "", value = "…", sub = "";
        Color valueColor;
        bool subLink, hoverSub, valueIsInk = true;
        public bool LeftLine;
        public event EventHandler SubClicked;

        public string Caption { set { caption = value; Invalidate(); } }

        public void Set(string v, Color? vc, string s, bool link)
        {
            bool ink = !vc.HasValue;
            Color c = vc.HasValue ? vc.GetValueOrDefault() : Color.Empty;
            if (v == value && ink == valueIsInk && c == valueColor && s == sub && link == subLink) return;
            value = v; valueIsInk = ink; valueColor = c; sub = s; subLink = link;
            Invalidate();
        }

        Rectangle SubRect { get { return new Rectangle(Ui.S(14), Ui.S(58), Width - Ui.S(20), Ui.S(18)); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (LeftLine)
                using (var pen = new Pen(Ui.Separator, Math.Max(1f, Ui.Scale)))
                    g.DrawLine(pen, 0, Height * 0.22f, 0, Height * 0.78f);
            Ui.Text(g, caption, Ui.Small, new Rectangle(Ui.S(14), Ui.S(8), Width - Ui.S(20), Ui.S(18)), Ui.Ink2, Ui.Left);
            Ui.Text(g, value, Ui.Stat, new Rectangle(Ui.S(14), Ui.S(26), Width - Ui.S(20), Ui.S(32)), valueIsInk ? Ui.Ink : valueColor, Ui.Left);
            Color sc = !subLink ? Ui.Ink3 : hoverSub ? Ui.Mix(Ui.Blue, Back, 0.35f) : Ui.Blue;
            Ui.Text(g, sub, Ui.Small, SubRect, sc, Ui.Left);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = subLink && SubRect.Contains(e.Location);
            if (h != hoverSub)
            {
                hoverSub = h;
                Cursor = h ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoverSub = false;
            Cursor = Cursors.Default;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (subLink && SubRect.Contains(e.Location) && SubClicked != null) SubClicked(this, EventArgs.Empty);
        }
    }

    // Графік температур за останні 10 хвилин.
    class TempChart : UiControl
    {
        public List<TempSample> Data = new List<TempSample>();
        public TimeSpan Window = TimeSpan.FromMinutes(10);
        Rectangle plot;
        double lo, hi;
        DateTime start;

        float X(DateTime t) { return plot.Left + (float)((t - start).TotalSeconds / Window.TotalSeconds) * plot.Width; }
        float Y(double v) { return plot.Bottom - (float)((v - lo) / (hi - lo)) * plot.Height; }

        static double Last(List<TempSample> pts, Func<TempSample, double> f)
        {
            for (int i = pts.Count - 1; i >= 0; i--)
                if (!double.IsNaN(f(pts[i]))) return f(pts[i]);
            return double.NaN;
        }

        int Legend(Graphics g, int x, Color c, string text)
        {
            int d = Ui.S(8), cy = Ui.S(14);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, x, cy - d / 2f, d, d);
            x += d + Ui.S(6);
            var size = TextRenderer.MeasureText(g, text, Ui.Small, Size.Empty, TextFormatFlags.NoPadding);
            Ui.Text(g, text, Ui.Small, new Rectangle(x, cy - Ui.S(10), size.Width + Ui.S(4), Ui.S(20)), Ui.Ink2, Ui.Left);
            return x + size.Width + Ui.S(18);
        }

        static string Deg(double t) { return double.IsNaN(t) ? "" : " " + Math.Round(t) + "°"; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            int left = Ui.S(40), right = Ui.S(14), top = Ui.S(34), bottom = Ui.S(24);
            plot = new Rectangle(left, top, Width - left - right, Height - top - bottom);
            start = DateTime.UtcNow - Window;
            var pts = Data.Where(d => d.T >= start).ToList();
            double cpu = Last(pts, p => p.Cpu), gpu = Last(pts, p => p.Gpu);
            int x = Legend(g, Ui.S(12), Ui.Blue, "Процесор" + Deg(cpu));
            Legend(g, x, Ui.Purple, "Відеокарта" + Deg(gpu));
            var all = pts.SelectMany(p => new[] { p.Cpu, p.Gpu }).Where(v => !double.IsNaN(v)).ToList();
            if (pts.Count < 2 || all.Count == 0)
            {
                Ui.Text(g, "Збираю дані…", Ui.Small, plot, Ui.Ink3, Ui.Center);
                return;
            }
            lo = Math.Floor((all.Min() - 3) / 10) * 10;
            hi = Math.Ceiling((all.Max() + 3) / 10) * 10;
            if (hi - lo < 30) { lo = Math.Floor(((hi + lo) / 2 - 15) / 10) * 10; hi = lo + 30; }
            if (lo < 0) { hi -= lo; lo = 0; }
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Ui.Separator, Math.Max(1f, Ui.Scale)))
            {
                for (double v = lo; v <= hi + 0.1; v += 10)
                {
                    float y = Y(v);
                    g.DrawLine(pen, plot.Left, y, plot.Right, y);
                    Ui.Text(g, v.ToString("0") + "°", Ui.Caps, new Rectangle(0, (int)y - Ui.S(9), left - Ui.S(8), Ui.S(18)), Ui.Ink3, Ui.Right);
                }
            }
            var axis = new Rectangle(plot.Left, plot.Bottom + Ui.S(4), plot.Width, Ui.S(18));
            Ui.Text(g, "10 хв тому", Ui.Caps, axis, Ui.Ink3, Ui.Left);
            Ui.Text(g, "5 хв", Ui.Caps, axis, Ui.Ink3, Ui.Center);
            Ui.Text(g, "зараз", Ui.Caps, axis, Ui.Ink3, Ui.Right);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Series(g, pts, p => p.Gpu, Ui.Purple);
            Series(g, pts, p => p.Cpu, Ui.Blue);
        }

        // Лінія з заливкою; розрив, якщо даних немає понад 20 секунд.
        void Series(Graphics g, List<TempSample> pts, Func<TempSample, double> f, Color color)
        {
            var seg = new List<PointF>();
            DateTime last = DateTime.MinValue;
            Action flush = () =>
            {
                if (seg.Count >= 2)
                {
                    var poly = new List<PointF>(seg);
                    poly.Add(new PointF(seg[seg.Count - 1].X, plot.Bottom));
                    poly.Add(new PointF(seg[0].X, plot.Bottom));
                    using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 40 : 28, color))) g.FillPolygon(b, poly.ToArray());
                    using (var pen = new Pen(color, Math.Max(1.5f, 2 * Ui.Scale)))
                    {
                        pen.LineJoin = LineJoin.Round;
                        g.DrawLines(pen, seg.ToArray());
                    }
                }
                else if (seg.Count == 1)
                {
                    using (var b = new SolidBrush(color))
                        g.FillEllipse(b, seg[0].X - Ui.S(2), seg[0].Y - Ui.S(2), Ui.S(4), Ui.S(4));
                }
                seg.Clear();
            };
            foreach (var p in pts)
            {
                double v = f(p);
                if (double.IsNaN(v) || (last != DateTime.MinValue && (p.T - last).TotalSeconds > 20)) flush();
                if (!double.IsNaN(v)) seg.Add(new PointF(X(p.T), Y(Math.Max(lo, Math.Min(hi, v)))));
                last = p.T;
            }
            flush();
        }
    }

    class ProcRow
    {
        public string Name;
        public double Cpu;
        public long Mem;
    }

    // Список запущених програм із навантаженням на процесор і пам'ять.
    class ProcList : UiControl
    {
        List<ProcRow> rows = new List<ProcRow>();
        public string SelectedName;
        int hover = -1;

        int RowH { get { return Ui.S(33); } }
        int HeadH { get { return Ui.S(26); } }

        public void SetRows(List<ProcRow> r) { rows = r; Invalidate(); }

        int HitTest(Point p)
        {
            if (p.Y < HeadH) return -1;
            int i = (p.Y - HeadH) / RowH;
            return i >= rows.Count ? -1 : i;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            int pad = Ui.S(12), cpuX = Width - Ui.S(200), memX = Width - Ui.S(96);
            Ui.Text(g, "ПРОГРАМА", Ui.Caps, new Rectangle(pad, 0, cpuX - pad, HeadH), Ui.Ink3, Ui.Left);
            Ui.Text(g, "ПРОЦЕСОР", Ui.Caps, new Rectangle(cpuX, 0, Ui.S(100), HeadH), Ui.Ink3, Ui.Left);
            Ui.Text(g, "ПАМ'ЯТЬ", Ui.Caps, new Rectangle(memX, 0, Width - memX - pad, HeadH), Ui.Ink3, Ui.Right);
            using (var pen = new Pen(Ui.Separator, Math.Max(1f, Ui.Scale)))
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    int y = HeadH + i * RowH;
                    bool sel = r.Name == SelectedName;
                    var bg = new RectangleF(Ui.S(2), y + Ui.S(1), Width - Ui.S(4), RowH - Ui.S(2));
                    if (sel) Ui.FillRound(g, bg, Ui.S(8), Ui.RowSel);
                    else if (i == hover) Ui.FillRound(g, bg, Ui.S(8), Ui.RowHover);
                    Ui.Text(g, r.Name, sel ? Ui.BodySemi : Ui.Body, new Rectangle(pad, y, cpuX - pad - Ui.S(8), RowH), Ui.Ink, Ui.Left);
                    var bar = new RectangleF(cpuX, y + RowH / 2f - Ui.S(2), Ui.S(46), Ui.S(4));
                    Ui.FillRound(g, bar, Ui.S(2), sel ? Ui.Card : Ui.Separator);
                    float frac = (float)Math.Min(1, r.Cpu / 100);
                    if (r.Cpu >= 0.5)
                        Ui.FillRound(g, new RectangleF(bar.X, bar.Y, Math.Max(Ui.S(4), bar.Width * frac), bar.Height), Ui.S(2), r.Cpu < 50 ? Ui.Blue : Ui.Orange);
                    Ui.Text(g, r.Cpu.ToString("N0") + "%", Ui.Body, new Rectangle(cpuX + Ui.S(54), y, Ui.S(46), RowH), Ui.Ink2, Ui.Left);
                    Ui.Text(g, (r.Mem / 1048576.0).ToString("N0") + " МБ", Ui.Body, new Rectangle(memX, y, Width - memX - pad, RowH), Ui.Ink2, Ui.Right);
                    bool nextSel = i + 1 < rows.Count && rows[i + 1].Name == SelectedName;
                    if (i < rows.Count - 1 && !sel && !nextSel && i != hover && i + 1 != hover)
                        g.DrawLine(pen, pad, y + RowH, Width - pad, y + RowH);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover)
            {
                hover = h;
                Cursor = h >= 0 ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = -1;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = HitTest(e.Location);
            if (i < 0) return;
            SelectedName = rows[i].Name;
            Invalidate();
        }
    }

    // Сторінка вкладки: малює білі картки, лінії-роздільники й підписи розділів.
    class Page : UiControl
    {
        public readonly List<Rectangle> Cards = new List<Rectangle>();
        public readonly List<Rectangle> Lines = new List<Rectangle>();
        public readonly Dictionary<string, KeyValuePair<Rectangle, string>> Captions = new Dictionary<string, KeyValuePair<Rectangle, string>>();

        public Page() { OnBg = true; }

        // Зсув підписів після перекомпонування на широкому екрані (X, Y — зсув, Width — розширення).
        public readonly Dictionary<string, Rectangle> CapMove = new Dictionary<string, Rectangle>();

        public void SetCaption(string id, Rectangle r, string text)
        {
            KeyValuePair<Rectangle, string> cur;
            Rectangle m;
            // Код передає координати зі звичайного макета — на широкому екрані їх треба зсунути так само, як і решту.
            bool same = Captions.TryGetValue(id, out cur) && cur.Key == r;
            if (!same && CapMove.TryGetValue(id, out m)) r = new Rectangle(r.X + m.X, r.Y + m.Y, r.Width + m.Width, r.Height);
            Captions[id] = new KeyValuePair<Rectangle, string>(r, text);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            foreach (var r in Cards)
            {
                Ui.FillRound(g, r, Ui.S(14), Ui.CardLine);
                Ui.FillRound(g, RectangleF.Inflate(r, -Math.Max(1f, Ui.Scale), -Math.Max(1f, Ui.Scale)), Ui.S(14) - 1, Ui.Card);
            }
            g.SmoothingMode = SmoothingMode.None;
            using (var pen = new Pen(Ui.Separator, Math.Max(1f, Ui.Scale)))
                foreach (var r in Lines) g.DrawLine(pen, r.X, r.Y, r.Right, r.Y);
            // Заголовок розділу: «Назва» або «Назва|пояснення» (пояснення дрібніше й сірим).
            g.SmoothingMode = SmoothingMode.AntiAlias;
            foreach (var c in Captions.Values)
            {
                var r = new Rectangle(c.Key.X - Ui.S(12), c.Key.Y - Ui.S(6), c.Key.Width + Ui.S(12), c.Key.Height + Ui.S(8));
                string text = c.Value, sub = null;
                int bar = text.IndexOf('|');
                if (bar >= 0) { sub = text.Substring(bar + 1); text = text.Substring(0, bar); }
                Ui.Text(g, text, Ui.Section, r, Ui.Ink, Ui.Left);
                if (sub != null)
                {
                    int w = TextRenderer.MeasureText(g, text, Ui.Section, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(8);
                    Ui.Text(g, sub, Ui.Small, new Rectangle(r.X + w, r.Y + Ui.S(1), r.Width - w, r.Height), Ui.Ink2, Ui.Left);
                }
            }
        }
    }

    // Рядок режимів (процесор або відеокарта).
    class Row
    {
        public string Id, Title, Current = "normal", Manual = "normal", Suppressed, Failed;
        public DateTime FailedAt;
        public bool Available = true;
        public string UnavailableText;
        public Func<string, bool> Apply;
        public Func<string, string> Desc;
        public Segmented Seg;
        public Label DescLabel;
        public bool Heat, HeatJustStarted;
        public DateTime HotSince, CoolSince;
        public double HeatTemp;
    }

    // Список із двома рядками в кожному пункті: назва, пояснення, значення праворуч і кольорова позначка.
    class InfoItem
    {
        public string Title = "", Sub = "", Right = "";
        public Color? RightColor, Dot;
        public bool? Check;   // галочка ліворуч (null — немає)
        public string IconPath;   // значок програми (якщо списку ввімкнено Icons)
        public string IconKey;    // або намальований значок на кольоровій плитці (колір — Dot)
        public object Tag;
    }

    // Значки програм із їхніх .exe (кешуються) і кольорові плитки з літерою, коли файлу немає.
    static class AppIcons
    {
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        static readonly Color[] Tiles =
        {
            Color.FromArgb(0, 122, 255), Color.FromArgb(52, 199, 89), Color.FromArgb(255, 149, 0), Color.FromArgb(175, 82, 222),
            Color.FromArgb(255, 45, 85), Color.FromArgb(90, 200, 250), Color.FromArgb(88, 86, 214), Color.FromArgb(162, 132, 94),
        };

        public static Image Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            Image img;
            if (cache.TryGetValue(path, out img)) return img;
            img = null;
            try
            {
                if (System.IO.File.Exists(path))
                    using (var ic = Icon.ExtractAssociatedIcon(path))
                        if (ic != null) img = new Icon(ic, 32, 32).ToBitmap();
            }
            catch { }
            if (cache.Count > 300) cache.Clear();
            cache[path] = img;
            return img;
        }

        public static void Draw(Graphics g, InfoItem it, RectangleF r)
        {
            if (it.IconKey != null)
            {
                var tc = it.Dot ?? Ui.Blue;
                using (var br = new LinearGradientBrush(r, Ui.Mix(tc, Color.White, 0.18f), tc, 90f))
                using (var p = Ui.Round(r, r.Width * 0.24f))
                    g.FillPath(br, p);
                float isz = r.Width * 0.56f;
                Icons.Draw(g, it.IconKey, new RectangleF(r.X + (r.Width - isz) / 2f, r.Y + (r.Height - isz) / 2f, isz, isz), Color.White);
                return;
            }
            var img = Get(it.IconPath);
            if (img != null)
            {
                var old = g.InterpolationMode;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(img, r);
                g.InterpolationMode = old;
                return;
            }
            string name = (it.Title ?? "?").Trim();
            int h = 0;
            foreach (char ch in name.ToLowerInvariant()) h = h * 31 + ch;
            var c = Tiles[(h & 0x7fffffff) % Tiles.Length];
            using (var br = new LinearGradientBrush(r, Ui.Mix(c, Color.White, 0.18f), c, 90f))
            using (var p = Ui.Round(r, r.Width * 0.24f))
                g.FillPath(br, p);
            string letter = name.Length > 0 ? char.ToUpperInvariant(name[0]).ToString() : "?";
            Ui.Text(g, letter, Ui.BodySemi, Rectangle.Round(r), Color.White, Ui.Center);
        }
    }

    class InfoList : UiControl
    {
        List<InfoItem> items = new List<InfoItem>();
        int hover = -1, scroll;
        public int Selected = -1;
        public string EmptyText = "";
        public bool Icons;       // значки програм ліворуч
        public bool EmptyGood;   // порожній список — це добре (зелена галочка)
        public string EmptyIcon;  // значок для порожнього списку (сірий)
        public event EventHandler SelectionChanged, CheckChanged;

        int RowH { get { return Ui.S(46); } }
        int VisibleRows { get { return Math.Max(1, Height / RowH); } }
        public List<InfoItem> Items { get { return items; } }
        public InfoItem SelectedItem { get { return Selected >= 0 && Selected < items.Count ? items[Selected] : null; } }

        // Оновлює список, намагаючись зберегти вибраний пункт (за Tag або назвою).
        public void SetItems(List<InfoItem> list)
        {
            var old = SelectedItem;
            items = list ?? new List<InfoItem>();
            Selected = -1;
            if (old != null)
                for (int i = 0; i < items.Count; i++)
                    if ((old.Tag != null && Equals(old.Tag, items[i].Tag)) || (old.Tag == null && old.Title == items[i].Title)) { Selected = i; break; }
            scroll = Math.Max(0, Math.Min(scroll, items.Count - VisibleRows));
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }

        int HitTest(Point p)
        {
            int i = p.Y / RowH + scroll;
            return i >= 0 && i < items.Count ? i : -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (items.Count == 0)
            {
                if (EmptyGood)
                {
                    float d = Ui.S(44);
                    var c = new RectangleF((Width - d) / 2f, Height / 2f - d - Ui.S(4), d, d);
                    using (var b = new SolidBrush(Color.FromArgb(Ui.Dark ? 50 : 34, Ui.Green))) g.FillEllipse(b, c);
                    using (var pen = new Pen(Ui.Green, Math.Max(2f, 2.4f * Ui.Scale)))
                    {
                        pen.StartCap = pen.EndCap = LineCap.Round;
                        pen.LineJoin = LineJoin.Round;
                        g.DrawLines(pen, new[] { new PointF(c.X + d * 0.3f, c.Y + d * 0.52f), new PointF(c.X + d * 0.45f, c.Y + d * 0.66f), new PointF(c.X + d * 0.71f, c.Y + d * 0.38f) });
                    }
                    Ui.Text(g, EmptyText, Ui.Body, new Rectangle(0, (int)(Height / 2f + Ui.S(8)), Width, Ui.S(24)), Ui.Ink2, Ui.Center);
                }
                else if (EmptyIcon != null)
                {
                    float d = Ui.S(52);
                    var c = new RectangleF((Width - d) / 2f, Height / 2f - d - Ui.S(4), d, d);
                    Ui.FillRound(g, c, Ui.S(16), Ui.Fill);
                    float isz = d * 0.5f;
                    TyhyiPK.Icons.Draw(g, EmptyIcon, new RectangleF(c.X + (d - isz) / 2f, c.Y + (d - isz) / 2f, isz, isz), Ui.Ink2);
                    TextRenderer.DrawText(g, EmptyText, Ui.Body, new Rectangle(Ui.S(30), (int)(Height / 2f + Ui.S(8)), Width - Ui.S(60), Ui.S(44)), Ui.Ink2,
                        TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                }
                else Ui.Text(g, EmptyText, Ui.Small, ClientRectangle, Ui.Ink3, Ui.Center);
                return;
            }
            int pad = Ui.S(10);
            using (var pen = new Pen(Ui.Separator, Math.Max(1f, Ui.Scale)))
            {
                for (int i = scroll; i < items.Count && (i - scroll) * RowH < Height; i++)
                {
                    var it = items[i];
                    int y = (i - scroll) * RowH;
                    bool sel = i == Selected;
                    var bg = new RectangleF(0, y + Ui.S(1), Width - 1, RowH - Ui.S(2));
                    if (sel) Ui.FillRound(g, bg, Ui.S(8), Ui.RowSel);
                    else if (i == hover) Ui.FillRound(g, bg, Ui.S(8), Ui.RowHover);
                    int x = pad;
                    if (it.Check.HasValue)
                    {
                        int d = Ui.S(20);
                        var box = new RectangleF(x, y + RowH / 2f - d / 2f, d, d);
                        if (it.Check.Value)
                        {
                            Ui.FillRound(g, box, Ui.S(6), Ui.Blue);
                            using (var ck = new Pen(Color.White, Math.Max(1.5f, 2 * Ui.Scale)))
                            {
                                ck.StartCap = ck.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                                g.DrawLines(ck, new[] { new PointF(box.X + d * 0.26f, box.Y + d * 0.52f), new PointF(box.X + d * 0.43f, box.Y + d * 0.69f), new PointF(box.X + d * 0.75f, box.Y + d * 0.33f) });
                            }
                        }
                        else
                        {
                            Ui.FillRound(g, box, Ui.S(6), Ui.Separator);
                            Ui.FillRound(g, RectangleF.Inflate(box, -Ui.S(1.5f), -Ui.S(1.5f)), Ui.S(5), Ui.Card);
                        }
                        x += d + Ui.S(10);
                    }
                    if (Icons)
                    {
                        float isz = Ui.S(30);
                        var ir = new RectangleF(x, y + (RowH - isz) / 2f, isz, isz);
                        AppIcons.Draw(g, it, ir);
                        if (it.Dot.HasValue && it.IconKey == null)
                        {
                            float d = Ui.S(11);
                            var dr = new RectangleF(ir.Right - d + Ui.S(3), ir.Bottom - d + Ui.S(3), d, d);
                            using (var b = new SolidBrush(sel ? Ui.RowSel : Ui.Card)) g.FillEllipse(b, RectangleF.Inflate(dr, Ui.S(2), Ui.S(2)));
                            using (var b = new SolidBrush(it.Dot.Value)) g.FillEllipse(b, dr);
                        }
                        x += (int)isz + Ui.S(12);
                    }
                    else if (it.Dot.HasValue)
                    {
                        int d = Ui.S(8);
                        using (var b = new SolidBrush(it.Dot.Value)) g.FillEllipse(b, x, y + RowH / 2f - d / 2f, d, d);
                        x += d + Ui.S(8);
                    }
                    int rightW = string.IsNullOrEmpty(it.Right) ? 0 : TextRenderer.MeasureText(g, it.Right, Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding).Width + Ui.S(6);
                    int w = Width - x - pad - rightW - Ui.S(6);
                    Ui.Text(g, it.Title, Ui.BodySemi, new Rectangle(x, y + Ui.S(5), w, Ui.S(20)), Ui.Ink, Ui.Left);
                    Ui.Text(g, it.Sub, Ui.Small, new Rectangle(x, y + Ui.S(25), w, Ui.S(17)), Ui.Ink2, Ui.Left);
                    if (rightW > 0)
                        Ui.Text(g, it.Right, Ui.BodySemi, new Rectangle(Width - pad - rightW, y, rightW, RowH), it.RightColor ?? Ui.Ink2, Ui.Right);
                    bool nextSel = i + 1 == Selected;
                    if (i < items.Count - 1 && !sel && !nextSel && i != hover && i + 1 != hover)
                        g.DrawLine(pen, pad, y + RowH, Width - pad, y + RowH);
                }
            }
            if (items.Count > VisibleRows)
            {
                float h = Height * (float)VisibleRows / items.Count;
                float top = Height * (float)scroll / items.Count;
                Ui.FillRound(g, new RectangleF(Width - Ui.S(4), top + 2, Ui.S(3), h - 4), Ui.S(1.5f), Ui.Separator);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int ns = Math.Max(0, Math.Min(items.Count - VisibleRows, scroll - Math.Sign(e.Delta)));
            if (ns != scroll)
            {
                scroll = ns;
                Invalidate();
                var h = e as HandledMouseEventArgs;
                if (h != null) h.Handled = true;   // інакше прокрутиться ще й сторінка
            }
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Focus(); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = HitTest(e.Location);
            if (h != hover) { hover = h; Cursor = h >= 0 ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int i = HitTest(e.Location);
            if (i < 0) return;
            Selected = i;
            if (items[i].Check.HasValue)
            {
                items[i].Check = !items[i].Check.Value;
                if (CheckChanged != null) CheckChanged(this, EventArgs.Empty);
            }
            Invalidate();
            if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
        }
    }

    // Рядки «назва — значення» (як у Налаштуваннях iPhone).
    class KeyValueList : UiControl
    {
        public readonly List<KeyValuePair<string, string>> Rows = new List<KeyValuePair<string, string>>();
        public readonly List<Color?> Colors = new List<Color?>();

        public void Set(int i, string key, string value, Color? color)
        {
            while (Rows.Count <= i) { Rows.Add(new KeyValuePair<string, string>("", "")); Colors.Add(null); }
            if (Rows[i].Key == key && Rows[i].Value == value && Colors[i] == color) return;
            Rows[i] = new KeyValuePair<string, string>(key, value);
            Colors[i] = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            int rh = Ui.S(44), pad = Ui.S(16);
            using (var pen = new Pen(Ui.Separator, Math.Max(1f, Ui.Scale)))
            {
                for (int i = 0; i < Rows.Count; i++)
                {
                    int y = i * rh;
                    Ui.Text(g, Rows[i].Key, Ui.Body, new Rectangle(pad, y, Width / 2 - pad, rh), Ui.Ink, Ui.Left);
                    Ui.Text(g, Rows[i].Value, Ui.BodySemi, new Rectangle(Width / 3, y, Width * 2 / 3 - pad, rh), Colors[i] ?? Ui.Ink2, Ui.Right);
                    if (i < Rows.Count - 1) g.DrawLine(pen, pad, y + rh, Width - pad, y + rh);
                }
            }
        }
    }

    // Поле для числа: колесо миші змінює на крок, можна ввести з клавіатури (кома або крапка).
    class NumBox : UiControl
    {
        readonly TextBox tb = new TextBox();
        double value;
        public double Min, Max = 1e9, Step = 1;
        public string Format = "0.##";
        public event EventHandler Changed;

        public NumBox()
        {
            tb.BorderStyle = BorderStyle.None;
            tb.TextAlign = HorizontalAlignment.Center;
            tb.Font = Ui.BodySemi;
            tb.Leave += (s, e) => Commit();
            tb.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Commit(); } };
            tb.MouseWheel += (s, e) =>
            {
                if (!tb.Focused) return;   // колесо над полем без фокусу прокручує сторінку
                Wheel(e.Delta);
                var h = e as HandledMouseEventArgs;
                if (h != null) h.Handled = true;
            };
            Controls.Add(tb);
            Ui.Rethemers.Add(() => { tb.BackColor = Ui.Fill; tb.ForeColor = Ui.Ink; Invalidate(); });
            tb.BackColor = Ui.Fill;
            tb.ForeColor = Ui.Ink;
        }

        public double Value
        {
            get { return value; }
            set { this.value = Math.Max(Min, Math.Min(Max, value)); tb.Text = this.value.ToString(Format); }
        }

        void Commit()
        {
            double v;
            string t = tb.Text.Trim().Replace(',', '.');
            if (double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) Set(v);
            else Value = value;
        }

        void Set(double v)
        {
            double old = value;
            Value = v;
            if (value != old && Changed != null) Changed(this, EventArgs.Empty);
        }

        void Wheel(int delta) { Set(Math.Round((value + (delta > 0 ? Step : -Step)) / Step) * Step); }

        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); tb.Focus(); tb.SelectAll(); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int h = tb.PreferredHeight;
            tb.Bounds = new Rectangle(Ui.S(8), (Height - h) / 2, Width - Ui.S(16), h);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            Ui.FillRound(g, new RectangleF(0, 0, Width - 1, Height - 1), Ui.S(8), Ui.Fill);
        }
    }

    // Стовпчики за останні дні (кВт·год), підпис над стовпчиком — у гривнях.
    class BarChart : UiControl
    {
        public List<KeyValuePair<DateTime, double>> Days = new List<KeyValuePair<DateTime, double>>();
        public double Price;

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (Days.Count == 0) return;
            int top = Ui.S(20), bottom = Ui.S(20);
            var plot = new Rectangle(Ui.S(8), top, Width - Ui.S(16), Height - top - bottom);
            double max = Math.Max(0.05, Days.Max(d => d.Value));
            float slot = plot.Width / (float)Days.Count;
            float bw = Math.Min(Ui.S(22), slot * 0.6f);
            for (int i = 0; i < Days.Count; i++)
            {
                var d = Days[i];
                bool today = d.Key.Date == DateTime.Today;
                float h = (float)(d.Value / max * plot.Height);
                float x = plot.X + slot * i + (slot - bw) / 2;
                var c = today ? Ui.Blue : Ui.Mix(Ui.Blue, Back, 0.55f);
                if (d.Value > 0) Ui.FillRound(g, new RectangleF(x, plot.Bottom - Math.Max(h, Ui.S(3)), bw, Math.Max(h, Ui.S(3))), Ui.S(3), c);
                else Ui.FillRound(g, new RectangleF(x, plot.Bottom - Ui.S(2), bw, Ui.S(2)), Ui.S(1), Ui.Separator);
                var lbl = new Rectangle((int)(plot.X + slot * i), plot.Bottom + Ui.S(3), (int)slot, Ui.S(16));
                Ui.Text(g, d.Key.Day.ToString(), Ui.Caps, lbl, today ? Ui.Ink : Ui.Ink3, Ui.Center);
                if (d.Value > 0)
                {
                    string money = Energy.Money(d.Value * Price, false);
                    var mr = new Rectangle((int)(plot.X + slot * i) - Ui.S(4), (int)(plot.Bottom - Math.Max(h, Ui.S(3))) - Ui.S(17), (int)slot + Ui.S(8), Ui.S(16));
                    Ui.Text(g, money, Ui.Caps, mr, Ui.Ink2, Ui.Center);
                }
            }
        }
    }
}
