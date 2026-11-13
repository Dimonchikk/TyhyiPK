// Тихий ПК — «стрічка виконання» для довгих завдань (перевірка швидкості, антивірус, Windows, тест, очищення).
// Показує назву кроку, відсотки й скільки приблизно лишилося; смужка плавно росте й переливається.
// Якщо точного прогресу немає — він оцінюється за часом (ніколи не «застрягає» на 100% раніше часу).
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace TyhyiPK
{
    class ProgressStrip : UiControl
    {
        readonly Timer anim = new Timer { Interval = 33 };
        readonly List<Control> hidden = new List<Control>();
        DateTime started, phaseStart;
        double total, phaseFrom, phaseTo, phaseSec, exact = -1, shown;
        string title = "", step = "";
        bool done, ok;
        DateTime doneAt;

        public ProgressStrip()
        {
            OnBg = true;
            Visible = false;
            anim.Tick += (s, e) => Tick();
        }

        // Почати: title — що робимо, seconds — скільки приблизно триватиме все завдання, hide — що сховати на цей час.
        public void Start(string title, double seconds, params Control[] hide)
        {
            this.title = title;
            step = "";
            total = Math.Max(1, seconds);
            started = DateTime.UtcNow;
            done = false;
            shown = 0;
            exact = -1;
            Phase("", 0, 1, seconds);
            foreach (var c in hidden) c.Visible = true;
            hidden.Clear();
            foreach (var c in hide) if (c != null && c.Visible) { hidden.Add(c); c.Visible = false; }
            Visible = true;
            BringToFront();
            anim.Start();
            Invalidate();
        }

        // Крок усередині завдання: від from до to (частки від 0 до 1), орієнтовно seconds секунд.
        public void Phase(string step, double from, double to, double seconds)
        {
            if (step != null) this.step = step;
            phaseFrom = from;
            phaseTo = to;
            phaseSec = Math.Max(0.5, seconds);
            phaseStart = DateTime.UtcNow;
            exact = -1;
        }

        // Точний прогрес (коли він відомий), 0…1.
        public void Set(double frac, string step)
        {
            exact = Math.Max(0, Math.Min(1, frac));
            if (step != null) this.step = step;
        }

        public void Finish(bool success, string text)
        {
            done = true;
            ok = success;
            step = text ?? "";
            doneAt = DateTime.UtcNow;
        }

        public bool Busy { get { return Visible && !done; } }

        double Target()
        {
            if (exact >= 0) return exact;
            double k = (DateTime.UtcNow - phaseStart).TotalSeconds / phaseSec;
            // Рівно до 85% кроку, далі все повільніше — щоб не дійти до кінця раніше, ніж завдання справді закінчиться.
            double e = k < 0.85 ? k : 0.85 + 0.13 * (1 - Math.Exp(-(k - 0.85) * 2.5));
            return phaseFrom + (phaseTo - phaseFrom) * Math.Min(0.98, e);
        }

        void Tick()
        {
            double target = done ? 1 : Target();
            shown += (target - shown) * (done ? 0.18 : 0.12);
            if (done && (DateTime.UtcNow - doneAt).TotalSeconds > 2.6)
            {
                anim.Stop();
                Visible = false;
                foreach (var c in hidden) c.Visible = true;
                hidden.Clear();
                return;
            }
            Invalidate();
        }

        static string Left(double sec)
        {
            if (sec < 5) return "ще трохи";
            if (sec < 60) return "≈ " + (int)(Math.Ceiling(sec / 5) * 5) + " с";
            return "≈ " + (int)Math.Ceiling(sec / 60) + " хв";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = Prep(e);
            int pct = (int)Math.Round(Math.Min(1, shown) * 100);
            double elapsed = (DateTime.UtcNow - started).TotalSeconds;
            Color accent = done ? (ok ? Ui.Green : Ui.Orange) : Ui.Blue;

            // Верхній рядок: назва · крок — праворуч відсотки й час.
            string right = done ? (ok ? "100%" : "") : pct + "%   " + Left(Math.Max(0, total - elapsed));
            var rf = TextRenderer.MeasureText(g, right, Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding);
            var leftRect = new Rectangle(0, 0, Width - rf.Width - Ui.S(12), Ui.S(20));
            if (done)
            {
                var ic = new Rectangle(0, Ui.S(1), Ui.S(18), Ui.S(18));
                Icons.Draw(g, ok ? "check" : "warn", ic, accent);
                Ui.Text(g, step, Ui.BodySemi, new Rectangle(ic.Right + Ui.S(6), 0, leftRect.Width - ic.Width - Ui.S(6), Ui.S(20)), Ui.Ink, Ui.Left);
            }
            else
            {
                var hw = TextRenderer.MeasureText(g, title, Ui.BodySemi, Size.Empty, TextFormatFlags.NoPadding).Width;
                Ui.Text(g, title, Ui.BodySemi, leftRect, Ui.Ink, Ui.Left);
                if (step != "")
                {
                    int x = hw + Ui.S(8);
                    using (var b = new SolidBrush(Ui.Ink3)) g.FillEllipse(b, x, Ui.S(9), Ui.S(4), Ui.S(4));
                    x += Ui.S(12);
                    Ui.Text(g, step, Ui.Body, new Rectangle(x, 0, Math.Max(0, leftRect.Width - x), Ui.S(20)), Ui.Ink2, Ui.Left);
                }
            }
            Ui.Text(g, right, Ui.BodySemi, new Rectangle(Width - rf.Width - Ui.S(2), 0, rf.Width + Ui.S(2), Ui.S(20)), done ? accent : Ui.Ink2, Ui.Left);

            // Смужка.
            var track = new RectangleF(0, Height - Ui.S(10), Width, Ui.S(8));
            Ui.FillRound(g, track, track.Height / 2, Ui.Fill);
            float w = (float)Math.Max(track.Height, track.Width * Math.Min(1, shown));
            var fill = new RectangleF(track.X, track.Y, w, track.Height);
            using (var path = Ui.Round(fill, track.Height / 2))
            {
                if (done) using (var b = new SolidBrush(accent)) g.FillPath(b, path);
                else
                {
                    using (var lg = new LinearGradientBrush(new RectangleF(0, 0, Math.Max(1, track.Width), 1), Ui.Blue, Color.FromArgb(175, 82, 222), 0f))
                        g.FillPath(lg, path);
                    // Відблиск, що біжить по заповненій частині.
                    double ph = (elapsed % 1.8) / 1.8;
                    float band = Ui.S(60), bx = fill.X - band + (float)ph * (fill.Width + band);
                    var clip = g.Clip;
                    g.SetClip(path);
                    using (var sh = new LinearGradientBrush(new RectangleF(bx, 0, band, 1), Color.Transparent, Color.Transparent, 0f))
                    {
                        var cb = new ColorBlend(3);
                        cb.Colors = new[] { Color.FromArgb(0, 255, 255, 255), Color.FromArgb(140, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) };
                        cb.Positions = new[] { 0f, 0.5f, 1f };
                        sh.InterpolationColors = cb;
                        g.FillRectangle(sh, bx, track.Y, band, track.Height);
                    }
                    g.Clip = clip;
                }
            }
        }
    }
}
