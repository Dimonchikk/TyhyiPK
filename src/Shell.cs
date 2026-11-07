// Тихий ПК — «оболонка» вікна в стилі macOS: бокове меню з іконками, прокручувані сторінки, статус-пігулка, головна картка.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TyhyiPK
{
    // Прості лінійні іконки, намальовані кодом (як SF Symbols).
    static class Icons
    {
        public static void Draw(Graphics g, string key, RectangleF r, Color c)
        {
            float s = r.Width, x = r.X, y = r.Y;
            using (var pen = new Pen(c, Math.Max(1.4f, s / 12f)))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch (key)
                {
                    case "overview":
                        float q = s * 0.38f, gap = s * 0.16f;
                        for (int i = 0; i < 2; i++)
                            for (int j = 0; j < 2; j++)
                                using (var p = Ui.Round(new RectangleF(x + s * 0.04f + i * (q + gap), y + s * 0.04f + j * (q + gap), q, q), s * 0.1f)) g.DrawPath(pen, p);
                        break;
                    case "sensors":
                        g.DrawLines(pen, new[] { P(x, y, s, 0.02f, 0.55f), P(x, y, s, 0.25f, 0.55f), P(x, y, s, 0.38f, 0.15f), P(x, y, s, 0.6f, 0.9f), P(x, y, s, 0.74f, 0.45f), P(x, y, s, 0.98f, 0.45f) });
                        break;
                    case "apps":
                        g.DrawPolygon(pen, new[] { P(x, y, s, 0.5f, 0.08f), P(x, y, s, 0.95f, 0.3f), P(x, y, s, 0.5f, 0.52f), P(x, y, s, 0.05f, 0.3f) });
                        g.DrawLines(pen, new[] { P(x, y, s, 0.05f, 0.5f), P(x, y, s, 0.5f, 0.72f), P(x, y, s, 0.95f, 0.5f) });
                        g.DrawLines(pen, new[] { P(x, y, s, 0.05f, 0.7f), P(x, y, s, 0.5f, 0.92f), P(x, y, s, 0.95f, 0.7f) });
                        break;
                    case "auto":
                        Spark(g, pen, x + s * 0.42f, y + s * 0.55f, s * 0.38f);
                        Spark(g, pen, x + s * 0.82f, y + s * 0.18f, s * 0.16f);
                        break;
                    case "energy":
                        g.DrawPolygon(pen, new[] { P(x, y, s, 0.58f, 0.04f), P(x, y, s, 0.2f, 0.56f), P(x, y, s, 0.48f, 0.56f), P(x, y, s, 0.4f, 0.96f), P(x, y, s, 0.8f, 0.42f), P(x, y, s, 0.52f, 0.42f) });
                        break;
                    case "service":
                        using (var p = new GraphicsPath())
                        {
                            p.AddLines(new[] { P(x, y, s, 0.5f, 0.04f), P(x, y, s, 0.9f, 0.18f), P(x, y, s, 0.88f, 0.5f) });
                            p.AddBezier(P(x, y, s, 0.88f, 0.5f), P(x, y, s, 0.85f, 0.75f), P(x, y, s, 0.65f, 0.88f), P(x, y, s, 0.5f, 0.97f));
                            p.AddBezier(P(x, y, s, 0.5f, 0.97f), P(x, y, s, 0.35f, 0.88f), P(x, y, s, 0.15f, 0.75f), P(x, y, s, 0.12f, 0.5f));
                            p.AddLines(new[] { P(x, y, s, 0.12f, 0.5f), P(x, y, s, 0.1f, 0.18f) });
                            p.CloseFigure();
                            g.DrawPath(pen, p);
                        }
                        g.DrawLines(pen, new[] { P(x, y, s, 0.33f, 0.5f), P(x, y, s, 0.46f, 0.63f), P(x, y, s, 0.68f, 0.38f) });
                        break;
                    case "leaf":
                        using (var p = new GraphicsPath())
                        {
                            p.AddBezier(P(x, y, s, 0.15f, 0.85f), P(x, y, s, 0.05f, 0.35f), P(x, y, s, 0.45f, 0.1f), P(x, y, s, 0.9f, 0.1f));
                            p.AddBezier(P(x, y, s, 0.9f, 0.1f), P(x, y, s, 0.9f, 0.55f), P(x, y, s, 0.65f, 0.9f), P(x, y, s, 0.15f, 0.85f));
                            g.DrawPath(pen, p);
                        }
                        g.DrawLine(pen, P(x, y, s, 0.15f, 0.85f), P(x, y, s, 0.6f, 0.4f));
                        break;
                    case "battery":
                        using (var p = Ui.Round(new RectangleF(x + s * 0.04f, y + s * 0.28f, s * 0.82f, s * 0.44f), s * 0.1f)) g.DrawPath(pen, p);
                        g.DrawLine(pen, P(x, y, s, 0.94f, 0.42f), P(x, y, s, 0.94f, 0.58f));
                        using (var b = new SolidBrush(c)) g.FillRectangle(b, x + s * 0.14f, y + s * 0.37f, s * 0.42f, s * 0.26f);
                        break;
                    case "disk":
                        using (var p = Ui.Round(new RectangleF(x + s * 0.08f, y + s * 0.2f, s * 0.84f, s * 0.6f), s * 0.12f)) g.DrawPath(pen, p);
                        g.DrawLine(pen, P(x, y, s, 0.08f, 0.56f), P(x, y, s, 0.92f, 0.56f));
                        using (var b = new SolidBrush(c)) g.FillEllipse(b, x + s * 0.7f, y + s * 0.63f, s * 0.1f, s * 0.1f);
                        break;
                    case "thermo":
                        using (var p = Ui.Round(new RectangleF(x + s * 0.38f, y + s * 0.04f, s * 0.24f, s * 0.6f), s * 0.12f)) g.DrawPath(pen, p);
                        g.DrawEllipse(pen, x + s * 0.3f, y + s * 0.58f, s * 0.4f, s * 0.38f);
                        g.DrawLine(pen, P(x, y, s, 0.5f, 0.3f), P(x, y, s, 0.5f, 0.68f));
                        break;
                    case "trash":
                        g.DrawLine(pen, P(x, y, s, 0.12f, 0.22f), P(x, y, s, 0.88f, 0.22f));
                        g.DrawLines(pen, new[] { P(x, y, s, 0.38f, 0.22f), P(x, y, s, 0.4f, 0.08f), P(x, y, s, 0.6f, 0.08f), P(x, y, s, 0.62f, 0.22f) });
                        g.DrawLines(pen, new[] { P(x, y, s, 0.2f, 0.22f), P(x, y, s, 0.26f, 0.94f), P(x, y, s, 0.74f, 0.94f), P(x, y, s, 0.8f, 0.22f) });
                        g.DrawLine(pen, P(x, y, s, 0.42f, 0.4f), P(x, y, s, 0.43f, 0.76f));
                        g.DrawLine(pen, P(x, y, s, 0.58f, 0.4f), P(x, y, s, 0.57f, 0.76f));
                        break;
                    case "report":
                        g.DrawLines(pen, new[] { P(x, y, s, 0.62f, 0.04f), P(x, y, s, 0.16f, 0.04f), P(x, y, s, 0.16f, 0.96f), P(x, y, s, 0.84f, 0.96f), P(x, y, s, 0.84f, 0.26f), P(x, y, s, 0.62f, 0.04f), P(x, y, s, 0.62f, 0.26f), P(x, y, s, 0.84f, 0.26f) });
                        foreach (float f in new[] { 0.48f, 0.64f, 0.8f }) g.DrawLine(pen, P(x, y, s, 0.3f, f), P(x, y, s, 0.7f, f));
                        break;
                    case "gauge":
                        g.DrawArc(pen, x + s * 0.06f, y + s * 0.14f, s * 0.88f, s * 0.88f, 160, 220);
                        g.DrawLine(pen, P(x, y, s, 0.5f, 0.58f), P(x, y, s, 0.72f, 0.34f));
                        using (var b = new SolidBrush(c)) g.FillEllipse(b, x + s * 0.43f, y + s * 0.51f, s * 0.14f, s * 0.14f);
                        break;
                    case "game":
                        using (var p = Ui.Round(new RectangleF(x + s * 0.04f, y + s * 0.24f, s * 0.92f, s * 0.54f), s * 0.24f)) g.DrawPath(pen, p);
                        g.DrawLine(pen, P(x, y, s, 0.24f, 0.51f), P(x, y, s, 0.42f, 0.51f));
                        g.DrawLine(pen, P(x, y, s, 0.33f, 0.42f), P(x, y, s, 0.33f, 0.6f));
                        using (var b = new SolidBrush(c))
                        {
                            g.FillEllipse(b, x + s * 0.62f, y + s * 0.4f, s * 0.09f, s * 0.09f);
                            g.FillEllipse(b, x + s * 0.72f, y + s * 0.52f, s * 0.09f, s * 0.09f);
                        }
                        break;
                    case "fan":
                        g.DrawEllipse(pen, x + s * 0.04f, y + s * 0.04f, s * 0.92f, s * 0.92f);
                        {
                            var st = g.Save();
                            g.TranslateTransform(x + s / 2f, y + s / 2f);
                            using (var b = new SolidBrush(c))
                                for (int k = 0; k < 4; k++)
                                {
                                    using (var p = new GraphicsPath())
                                    {
                                        p.AddBezier(0, -s * 0.06f, s * 0.14f, -s * 0.14f, s * 0.16f, -s * 0.3f, s * 0.03f, -s * 0.36f);
                                        p.AddBezier(s * 0.03f, -s * 0.36f, -s * 0.08f, -s * 0.3f, -s * 0.08f, -s * 0.16f, 0, -s * 0.06f);
                                        g.FillPath(b, p);
                                    }
                                    g.RotateTransform(90);
                                }
                            g.Restore(st);
                        }
                        break;
                    case "check":
                        g.DrawEllipse(pen, x + s * 0.05f, y + s * 0.05f, s * 0.9f, s * 0.9f);
                        g.DrawLines(pen, new[] { P(x, y, s, 0.3f, 0.52f), P(x, y, s, 0.45f, 0.66f), P(x, y, s, 0.71f, 0.38f) });
                        break;
                    case "net":
                        g.DrawEllipse(pen, x + s * 0.05f, y + s * 0.05f, s * 0.9f, s * 0.9f);
                        g.DrawEllipse(pen, x + s * 0.3f, y + s * 0.05f, s * 0.4f, s * 0.9f);
                        g.DrawLine(pen, P(x, y, s, 0.07f, 0.5f), P(x, y, s, 0.93f, 0.5f));
                        break;
                    case "cpu":
                        using (var p = Ui.Round(new RectangleF(x + s * 0.2f, y + s * 0.2f, s * 0.6f, s * 0.6f), s * 0.1f)) g.DrawPath(pen, p);
                        g.DrawRectangle(pen, x + s * 0.38f, y + s * 0.38f, s * 0.24f, s * 0.24f);
                        foreach (float f in new[] { 0.35f, 0.5f, 0.65f })
                        {
                            g.DrawLine(pen, P(x, y, s, f, 0.04f), P(x, y, s, f, 0.18f));
                            g.DrawLine(pen, P(x, y, s, f, 0.82f), P(x, y, s, f, 0.96f));
                            g.DrawLine(pen, P(x, y, s, 0.04f, f), P(x, y, s, 0.18f, f));
                            g.DrawLine(pen, P(x, y, s, 0.82f, f), P(x, y, s, 0.96f, f));
                        }
                        break;
                    case "sun":
                        g.DrawEllipse(pen, x + s * 0.3f, y + s * 0.3f, s * 0.4f, s * 0.4f);
                        for (int k = 0; k < 8; k++)
                        {
                            double a = k * Math.PI / 4;
                            g.DrawLine(pen, x + s * (0.5f + 0.32f * (float)Math.Cos(a)), y + s * (0.5f + 0.32f * (float)Math.Sin(a)),
                                x + s * (0.5f + 0.45f * (float)Math.Cos(a)), y + s * (0.5f + 0.45f * (float)Math.Sin(a)));
                        }
                        break;
                    case "moon":
                        using (var outer = new GraphicsPath())
                        using (var inner = new GraphicsPath())
                        {
                            outer.AddEllipse(x + s * 0.1f, y + s * 0.1f, s * 0.8f, s * 0.8f);
                            inner.AddEllipse(x + s * 0.34f, y - s * 0.04f, s * 0.68f, s * 0.68f);
                            using (var rg = new Region(outer))
                            using (var b = new SolidBrush(c))
                            {
                                rg.Exclude(inner);
                                g.FillRegion(b, rg);
                            }
                        }
                        break;
                    case "half":
                        g.DrawEllipse(pen, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f);
                        using (var b = new SolidBrush(c)) g.FillPie(b, x + s * 0.08f, y + s * 0.08f, s * 0.84f, s * 0.84f, 90, 180);
                        break;
                    case "warn":
                        g.DrawPolygon(pen, new[] { P(x, y, s, 0.5f, 0.08f), P(x, y, s, 0.95f, 0.88f), P(x, y, s, 0.05f, 0.88f) });
                        g.DrawLine(pen, P(x, y, s, 0.5f, 0.38f), P(x, y, s, 0.5f, 0.6f));
                        g.DrawLine(pen, P(x, y, s, 0.5f, 0.74f), P(x, y, s, 0.5f, 0.75f));
                        break;
                }
            }
        }

        static PointF P(float x, float y, float s, float fx, float fy) { return new PointF(x + s * fx, y + s * fy); }

        static void Spark(Graphics g, Pen pen, float cx, float cy, float r)
        {
            using (var p = new GraphicsPath())
            {
                float k = r * 0.22f;
                p.AddBezier(cx, cy - r, cx + k, cy - k, cx + k, cy - k, cx + r, cy);
                p.AddBezier(cx + r, cy, cx + k, cy + k, cx + k, cy + k, cx, cy + r);
                p.AddBezier(cx, cy + r, cx - k, cy + k, cx - k, cy + k, cx - r, cy);
                p.AddBezier(cx - r, cy, cx - k, cy - k, cx - k, cy - k, cx, cy - r);
                g.DrawPath(pen, p);
            }
        }
    }

    // Бокове меню, як у «Параметрах системи» macOS. У вибраного розділу під ним видно його підрозділи.
    class Sidebar : UiControl
    {
        public string[] Items = new string[0], IconKeys = new string[0];
        public string[][] Subs = new string[0][];
        public bool[] Dots = new bool[0];
        readonly System.Collections.Generic.HashSet<string> subDots = new System.Collections.Generic.HashSet<string>();

        public void SetSubDot(int i, int k, bool on)
        {
            string key = i + ":" + k;
            if (on ? subDots.Add(key) : subDots.Remove(key)) Invalidate();
        }
        int selected, subSelected = -1, hover = -1, hoverSub = -1;
        public event Action<int> ItemClicked;
        public event Action<int, int> SubClicked;

        public Sidebar() { Cursor = Cursors.Default; }

        public int Selected { get { return selected; } set { if (value != selected) subSelected = -1; selected = value; Invalidate(); } }
        public int SubSelected { get { return subSelected; } set { subSelected = value; Invalidate(); } }

        public void SetDot(int i, bool on)
        {
            if (i < 0 || i >= Dots.Length || Dots[i] == on) return;
            Dots[i] = on;
            Invalidate();
        }

        const int Top0 = 92, ItemH = 38, SubH = 28;

        string[] SubsOf(int i) { return i < Subs.Length && Subs[i] != null ? Subs[i] : new string[0]; }

        // Прямокутники всіх рядків: (пункт, підпункт або -1).
        System.Collections.Generic.List<Tuple<int, int, Rectangle>> Rows()
        {
            var list = new System.Collections.Generic.List<Tuple<int, int, Rectangle>>();
            int y = Top0;
            for (int i = 0; i < Items.Length; i++)
            {
                list.Add(Tuple.Create(i, -1, new Rectangle(Ui.S(12), Ui.S(y), Width - Ui.S(24), Ui.S(ItemH))));
                y += ItemH + 4;
                if (i == selected)
                {
                    var subs = SubsOf(i);
                    for (int k = 0; k < subs.Length; k++)
                    {
                        list.Add(Tuple.Create(i, k, new Rectangle(Ui.S(40), Ui.S(y), Width - Ui.S(52), Ui.S(SubH))));
                        y += SubH + 2;
                    }
                    if (subs.Length > 0) y += 6;
                }
            }
            return list;
        }

        public int ContentBottom { get { var r = Rows(); return r.Count == 0 ? 0 : r[r.Count - 1].Item3.Bottom; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            g.Clear(Ui.Side);
            using (var pen = new Pen(Ui.CardLine, Math.Max(1f, Ui.Scale))) g.DrawLine(pen, Width - 1, 0, Width - 1, Height);
            Ui.Text(g, "Тихий ПК", Ui.Brand, new Rectangle(Ui.S(24), Ui.S(26), Width - Ui.S(30), Ui.S(28)), Ui.Ink, Ui.Left);
            Ui.Text(g, "Менше шуму. Більше спокою.", Ui.Caps, new Rectangle(Ui.S(24), Ui.S(54), Width - Ui.S(30), Ui.S(18)), Ui.Ink2, Ui.Left);
            Ui.Text(g, "Версія " + AppVersion.Text, Ui.Caps, new Rectangle(Ui.S(24), Height - Ui.S(32), Width - Ui.S(30), Ui.S(18)), Ui.Ink3, Ui.Left);
            foreach (var row in Rows())
            {
                int i = row.Item1, k = row.Item2;
                var r = row.Item3;
                if (k < 0)
                {
                    bool sel = i == selected;
                    if (sel) Ui.FillRound(g, r, Ui.S(9), Ui.Blue);
                    else if (i == hover && hoverSub < 0) Ui.FillRound(g, r, Ui.S(9), Ui.Mix(Ui.Side, Ui.Ink, 0.07f));
                    float isz = Ui.S(17);
                    Icons.Draw(g, i < IconKeys.Length ? IconKeys[i] : "", new RectangleF(r.X + Ui.S(12), r.Y + (r.Height - isz) / 2f, isz, isz), sel ? Color.White : Ui.Blue);
                    Ui.Text(g, Items[i], sel ? Ui.BodySemi : Ui.Body, new Rectangle(r.X + Ui.S(40), r.Y, r.Width - Ui.S(56), r.Height), sel ? Color.White : Ui.Ink, Ui.Left);
                    if (i < Dots.Length && Dots[i])
                    {
                        float d = Ui.S(7);
                        using (var b = new SolidBrush(sel ? Color.White : Ui.Orange)) g.FillEllipse(b, r.Right - Ui.S(18), r.Y + (r.Height - d) / 2f, d, d);
                    }
                }
                else
                {
                    bool sel = k == subSelected;
                    if (sel) Ui.FillRound(g, r, Ui.S(8), Ui.Mix(Ui.Side, Ui.Blue, Ui.Dark ? 0.28f : 0.16f));
                    else if (i == hover && k == hoverSub) Ui.FillRound(g, r, Ui.S(8), Ui.Mix(Ui.Side, Ui.Ink, 0.07f));
                    Ui.Text(g, SubsOf(i)[k], sel ? Ui.BodySemi : Ui.Body, new Rectangle(r.X + Ui.S(12), r.Y, r.Width - Ui.S(30), r.Height), sel ? Ui.Blue : Ui.Ink2, Ui.Left);
                    if (subDots.Contains(i + ":" + k))
                    {
                        float d = Ui.S(6);
                        using (var b = new SolidBrush(Ui.Orange)) g.FillEllipse(b, r.Right - Ui.S(14), r.Y + (r.Height - d) / 2f, d, d);
                    }
                }
            }
        }

        Tuple<int, int, Rectangle> Hit(Point p)
        {
            foreach (var row in Rows()) if (row.Item3.Contains(p)) return row;
            return null;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var h = Hit(e.Location);
            int hi = h != null ? h.Item1 : -1, hs = h != null ? h.Item2 : -1;
            if (hi != hover || hs != hoverSub) { hover = hi; hoverSub = hs; Cursor = h != null ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; hoverSub = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            var h = Hit(e.Location);
            if (h == null) return;
            if (h.Item2 < 0) { if (ItemClicked != null) ItemClicked(h.Item1); }
            else if (SubClicked != null) SubClicked(h.Item1, h.Item2);
        }
    }

    // Пігулка стану біля заголовка: «● Усе гаразд».
    class StatusPill : UiControl
    {
        Color color = Color.Green;
        public StatusPill() { OnBg = true; }

        public void Set(string text, Color c)
        {
            if (Text == text && color == c) return;
            color = c;
            Text = text;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (string.IsNullOrEmpty(Text)) return;
            var size = TextRenderer.MeasureText(g, Text, Ui.Small, Size.Empty, TextFormatFlags.NoPadding);
            int w = size.Width + Ui.S(30), h = Ui.S(26);
            var r = new RectangleF(Width - w, (Height - h) / 2f, w, h);
            Ui.FillRound(g, r, h / 2f, Color.FromArgb(Ui.Dark ? 46 : 30, color));
            float d = Ui.S(6);
            using (var b = new SolidBrush(color)) g.FillEllipse(b, r.X + Ui.S(11), r.Y + (h - d) / 2f, d, d);
            Ui.Text(g, Text, Ui.Small, new Rectangle((int)r.X + Ui.S(21), (int)r.Y, w - Ui.S(24), h), color, Ui.Left);
        }
    }

    // Головна картка «Огляду»: великий висновок і кругла іконка праворуч.
    class HeroCard : UiControl
    {
        string title = "", sub = "", icon = "leaf";
        Color tint = Color.Green;
        public HeroCard() { OnBg = true; Cursor = Cursors.Default; }

        public void Set(string t, string s, string iconKey, Color c)
        {
            if (t == title && s == sub && iconKey == icon && c == tint) return;
            title = t; sub = s; icon = iconKey; tint = c;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, r, Ui.S(16), Ui.CardLine);
            Ui.FillRound(g, RectangleF.Inflate(r, -Math.Max(1f, Ui.Scale), -Math.Max(1f, Ui.Scale)), Ui.S(16) - 1, Ui.Card);
            int pad = Ui.S(22);
            int box = Height - Ui.S(24);
            int tw = Width - pad * 2 - box;
            bool twoLines = TextRenderer.MeasureText(g, sub, Ui.Small, new Size(tw, 1000), TextFormatFlags.WordBreak).Height > Ui.S(20);
            int ty = twoLines ? Ui.S(8) : Ui.S(14);
            Ui.Text(g, title, Ui.Brand, new Rectangle(pad, ty, tw, Ui.S(28)), Ui.Ink, Ui.Left);
            TextRenderer.DrawText(g, sub, Ui.Small, new Rectangle(pad, ty + Ui.S(27), tw, twoLines ? Ui.S(32) : Ui.S(20)), Ui.Ink2,
                TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            var ir = new RectangleF(Width - pad - box + Ui.S(6), Ui.S(12), box, box);
            Ui.FillRound(g, ir, Ui.S(14), Color.FromArgb(Ui.Dark ? 50 : 34, tint));
            float isz = box * 0.46f;
            Icons.Draw(g, icon, new RectangleF(ir.X + (box - isz) / 2f, ir.Y + (box - isz) / 2f, isz, isz), tint);
        }
    }

    // Перемикач теми: три круглі кнопки «авто / світла / темна» на спільній «пігулці».
    class ThemeSwitch : UiControl
    {
        static readonly string[] Keys = { "half", "sun", "moon" };
        static readonly string[] Tips = { "Як у Windows", "Світла тема", "Темна тема" };
        int selected, hover = -1;
        readonly ToolTip tip = new ToolTip();
        public event Action<int> Changed;

        public ThemeSwitch() { OnBg = true; Cursor = Cursors.Hand; }

        public int Selected { get { return selected; } set { selected = value; Invalidate(); } }

        RectangleF Seg(int i)
        {
            float pad = Ui.S(3), w = (Width - 2 * pad) / 3f;
            return new RectangleF(pad + i * w, pad, w, Height - 2 * pad);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            var bg = new RectangleF(0, 0, Width - 1, Height - 1);
            Ui.FillRound(g, bg, Height / 2f, Ui.Fill);
            for (int i = 0; i < 3; i++)
            {
                var r = Seg(i);
                if (i == selected)
                {
                    Ui.FillRound(g, new RectangleF(r.X, r.Y + Ui.S(1), r.Width, r.Height), r.Height / 2f, Color.FromArgb(Ui.ShadowAlpha, 0, 0, 0));
                    Ui.FillRound(g, r, r.Height / 2f, Ui.Thumb);
                }
                else if (i == hover) Ui.FillRound(g, r, r.Height / 2f, Ui.FillHover);
                float s = Ui.S(15);
                Icons.Draw(g, Keys[i], new RectangleF(r.X + (r.Width - s) / 2f, r.Y + (r.Height - s) / 2f, s, s), i == selected ? Ui.Ink : Ui.Ink2);
            }
        }

        int Hit(Point p) { for (int i = 0; i < 3; i++) if (Seg(i).Contains(p)) return i; return -1; }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = Hit(e.Location);
            if (h != hover) { hover = h; tip.SetToolTip(this, h >= 0 ? Tips[h] : ""); Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int h = Hit(e.Location);
            if (h < 0 || e.Button != MouseButtons.Left) return;
            selected = h;
            Invalidate();
            if (Changed != null) Changed(h);
        }
    }

    // Попередній перегляд картинки з гарною заглушкою, поки картинки немає.
    class ImageBox : UiControl
    {
        Image image;
        public string EmptyText = "", EmptyIcon = "report";
        public Image Image { get { return image; } set { image = value; Invalidate(); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (image == null)
            {
                float d = Ui.S(56);
                var c = new RectangleF((Width - d) / 2f, Height / 2f - d - Ui.S(6), d, d);
                Ui.FillRound(g, c, Ui.S(16), Color.FromArgb(Ui.Dark ? 50 : 30, Ui.Blue));
                float isz = d * 0.5f;
                Icons.Draw(g, EmptyIcon, new RectangleF(c.X + (d - isz) / 2f, c.Y + (d - isz) / 2f, isz, isz), Ui.Blue);
                TextRenderer.DrawText(g, EmptyText, Ui.Small, new Rectangle(Ui.S(40), (int)(Height / 2f + Ui.S(6)), Width - Ui.S(80), Ui.S(44)), Ui.Ink2,
                    TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                return;
            }
            float k = Math.Min((float)Width / image.Width, (float)Height / image.Height);
            float w = image.Width * k, h = image.Height * k;
            var r = new RectangleF((Width - w) / 2f, (Height - h) / 2f, w, h);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            using (var p = Ui.Round(r, Ui.S(8)))
            {
                g.SetClip(p);
                g.DrawImage(image, r);
                g.ResetClip();
            }
        }
    }

    // Смужка-шкала з підписом і значенням (як «Стан батареї» в iPhone).
    class MeterBar : UiControl
    {
        public string Label = "", Value = "";
        public float Frac;
        public Color BarColor = Color.Green;

        public void Set(string label, string value, float frac, Color c) { Label = label; Value = value; Frac = frac; BarColor = c; Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            Ui.Text(g, Label, Ui.Body, new Rectangle(0, 0, Width / 2, Ui.S(24)), Ui.Ink, Ui.Left);
            Ui.Text(g, Value, Ui.BodySemi, new Rectangle(Width / 2, 0, Width / 2, Ui.S(24)), BarColor, Ui.Right);
            var track = new RectangleF(0, Ui.S(30), Width, Ui.S(8));
            Ui.FillRound(g, track, Ui.S(4), Ui.Fill);
            if (Frac > 0)
                Ui.FillRound(g, new RectangleF(track.X, track.Y, Math.Max(track.Height, track.Width * Math.Min(1, Frac)), track.Height), Ui.S(4), BarColor);
        }
    }

    // Тонка смужка прокрутки, що з'являється лише тоді, коли є що прокручувати.
    class ThinBar : UiControl
    {
        public float Pos, Frac = 1;
        public ThinBar() { OnBg = true; }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            if (Frac >= 1) return;
            float h = Math.Max(Ui.S(30), Height * Frac);
            float y = (Height - h) * Pos;
            Ui.FillRound(g, new RectangleF(Ui.S(1), y, Width - Ui.S(2), h), (Width - Ui.S(2)) / 2f, Ui.Mix(Ui.Bg, Ui.Ink, 0.22f));
        }
    }

    // Сторінка з прокруткою колесом миші.
    class ScrollHost : UiControl
    {
        public readonly Page Content = new Page();
        readonly ThinBar bar = new ThinBar();
        int offset;

        public ScrollHost()
        {
            OnBg = true;
            Controls.Add(Content);
            Controls.Add(bar);
            bar.BringToFront();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Content.Width = Width;
            bar.Bounds = new Rectangle(Width - Ui.S(10), Ui.S(4), Ui.S(6), Height - Ui.S(8));
            Fit();
        }

        // Висота вмісту — до нижнього краю найнижчого елемента.
        public void Fit()
        {
            int bottom = 0;
            foreach (Control c in Content.Controls) if (c.Visible || !IsHandleCreated) bottom = Math.Max(bottom, c.Bottom);
            foreach (var r in Content.Cards) bottom = Math.Max(bottom, r.Bottom);
            bottom += Ui.S(20);
            Content.Height = Math.Max(Height, bottom);
            ScrollTo(offset);
        }

        public void ScrollTo(int y)
        {
            offset = Math.Max(0, Math.Min(Content.Height - Height, y));
            Content.Top = -offset;
            int range = Content.Height - Height;
            bar.Frac = range <= 0 ? 1 : (float)Height / Content.Height;
            bar.Pos = range <= 0 ? 0 : (float)offset / range;
            bar.Invalidate();
        }

        public void ScrollToControl(Control c, int margin)
        {
            int y = 0;
            for (var x = c; x != null && x != Content; x = x.Parent) y += x.Top;
            ScrollTo(y - margin);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollTo(offset - e.Delta * Ui.S(70) / 120);
            var h = e as HandledMouseEventArgs;
            if (h != null) h.Handled = true;
        }
    }
}
