// Тихий ПК — розгортання вікна на весь екран.
// Сторінки намальовані під ширину 600 (у вікні звичайного розміру). Коли вікно розгорнуте:
// • вміст стоїть по центру й не розтягується безмежно;
// • якщо місця вистачає на дві колонки — розділи сторінки (заголовок + картка) розкладаються у дві колонки,
//   а графіки (температура, енергія) займають усю ширину;
// • сторінка з одним розділом (списки, налаштування) розтягується на всю ширину: списки й картки ширшають,
//   перемикачі й кнопки лишаються свого розміру біля свого краю;
// • велика картка-висновок угорі теж на всю ширину.
// Розгорнутість вікна запам'ятовується.
// Snap Layouts Windows 11: вікно можна поставити в половину чи чверть екрана (наведи на «Розгорнути»).
// Мишкою за край розмір не тягнеться — розмір змінюють лише розгортання й Snap. Якщо місця менше, ніж 600,
// сторінки акуратно звужуються (широкі елементи вужчають, перемикачі лишаються біля правого краю),
// а бокове меню прокручується коліщатком. Перекомпонування — лише для показаної сторінки, решта — коли відкриєш.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        const int ColW = 600;   // ширина однієї колонки (як у звичайному вікні)

        // Як сторінка виглядала в звичайному макеті — щоб щоразу будувати новий макет з нуля.
        class Snap
        {
            public List<KeyValuePair<Control, Rectangle>> Ctl = new List<KeyValuePair<Control, Rectangle>>();
            public List<Rectangle> Cards = new List<Rectangle>(), Lines = new List<Rectangle>();
            public Dictionary<string, Rectangle> Caps = new Dictionary<string, Rectangle>();
        }
        readonly Dictionary<Page, Snap> snaps = new Dictionary<Page, Snap>();

        // Елемент сторінки: елемент керування, картка, лінія або підпис.
        class Item
        {
            public Control C;
            public int Card = -1, Line = -1;
            public string Cap;
            public Rectangle R;
        }
        class Block
        {
            public List<Item> Items = new List<Item>();
            public int Top = int.MaxValue, Bottom = int.MinValue;
            public bool Wide;
            public int Height { get { return Bottom - Top; } }
        }

        // Де стоїть вміст кожної сторінки (для заголовка над нею).
        readonly Dictionary<Page, Point> pageGeom = new Dictionary<Page, Point>();   // X — відступ зліва, Y — ширина
        bool layoutReady, restoreMax;
        Page curPage;
        FormWindowState lastState = FormWindowState.Normal;

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);

        // Межі вікна не тягнуться мишкою: Windows бачить вікно «змінюваним» (для Snap), але край — це просто рамка.
        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            const int WM_NCHITTEST = 0x84, HTBORDER = 18;
            if (m.Msg == WM_NCHITTEST && WindowState == FormWindowState.Normal)
            {
                int hit = m.Result.ToInt32();
                if (hit >= 10 && hit <= 17) m.Result = (IntPtr)HTBORDER;   // HTLEFT … HTBOTTOMRIGHT
            }
        }

        static void Freeze(Control c, bool on)
        {
            try
            {
                if (!c.IsHandleCreated) return;
                SendMessage(c.Handle, 0x000B, on ? IntPtr.Zero : (IntPtr)1, IntPtr.Zero);   // WM_SETREDRAW
                if (!on) c.Invalidate(true);
            }
            catch { }
        }

        int layoutAvail;
        readonly Dictionary<Page, int> laidFor = new Dictionary<Page, int>();

        // Перед показом сторінки — підлаштувати її під поточну ширину, якщо ще не підлаштована.
        void EnsureLaid(Page p)
        {
            if (!layoutReady || p == null) return;
            int w;
            if (laidFor.TryGetValue(p, out w) && w == layoutAvail) return;
            var host = HostOf(p);
            if (host == null) return;
            bool vis = host.Visible;
            if (vis) Freeze(host, true);
            try
            {
                Arrange(p, layoutAvail);
                laidFor[p] = layoutAvail;
                host.Fit();
                if (p == pageEnergy)
                {
                    bool wide = bars != null && bars.Width > Ui.S(800);
                    if (wide != energyWide) { energyWide = wide; RefreshEnergy(); }
                }
            }
            finally { if (vis) Freeze(host, false); }
        }

        void SetupLayout()
        {
            restoreMax = Settings.GetBool("Maximized", false);
            if (restoreMax) WindowState = FormWindowState.Maximized;
            lastState = WindowState;
            Resize += (s, e) =>
            {
                if (WindowState == FormWindowState.Minimized) return;
                if (WindowState != lastState)
                {
                    lastState = WindowState;
                    restoreMax = WindowState == FormWindowState.Maximized;
                    Settings.SetBool("Maximized", restoreMax);
                }
                Relayout();
            };
            layoutReady = true;
            Relayout();
        }

        void Relayout()
        {
            if (!layoutReady) return;
            var cs = ClientSize;
            if (cs.Width < Ui.S(SideW + 200) || cs.Height < Ui.S(HeaderH + 150)) return;
            SuspendLayout();
            try
            {
                int side = Ui.S(SideW), head = Ui.S(HeaderH);
                nav.Bounds = new Rectangle(0, 0, side, cs.Height);
                ShowUpdateState();
                int areaW = cs.Width - side;
                layoutAvail = areaW;
                foreach (var group in views)
                    foreach (var p in group)
                    {
                        var host = HostOf(p);
                        if (host != null) host.Bounds = new Rectangle(side, head, areaW, cs.Height - head);
                    }
                EnsureLaid(curPage);
                PlaceHeader();
            }
            finally { ResumeLayout(true); }
        }

        // Заголовок, перемикач теми й статус — над вмістом поточної сторінки.
        void PlaceHeader()
        {
            Page cur = curPage;
            Point g;
            if (cur == null || !pageGeom.TryGetValue(cur, out g)) g = new Point(0, Ui.S(ColW));
            int left = Ui.S(SideW) + g.X, right = left + g.Y;
            lblTitle.Left = left + Ui.S(22);
            lblSub.Left = left + Ui.S(25);
            lblTitle.Width = Math.Max(Ui.S(120), Math.Min(Ui.S(380), right - Ui.S(24) - themeSeg.Width - Ui.S(8) - lblTitle.Left));
            lblSub.Width = Math.Max(Ui.S(120), Math.Min(Ui.S(360), right - Ui.S(24) - pill.Width - Ui.S(8) - lblSub.Left));
            themeSeg.Left = right - Ui.S(24) - themeSeg.Width;
            pill.Left = right - Ui.S(24) - pill.Width;
        }

        // ── Знімок і відновлення звичайного макета ──
        void Restore(Page p)
        {
            Snap o;
            if (!snaps.TryGetValue(p, out o)) return;
            foreach (var kv in o.Ctl) if (kv.Key.Parent == p) kv.Key.Bounds = kv.Value;
            if (o.Cards.Count == p.Cards.Count) { p.Cards.Clear(); p.Cards.AddRange(o.Cards); }
            if (o.Lines.Count == p.Lines.Count) { p.Lines.Clear(); p.Lines.AddRange(o.Lines); }
            foreach (var kv in o.Caps)
            {
                KeyValuePair<Rectangle, string> cur;
                if (p.Captions.TryGetValue(kv.Key, out cur)) p.Captions[kv.Key] = new KeyValuePair<Rectangle, string>(kv.Value, cur.Value);
            }
            p.CapMove.Clear();
            snaps.Remove(p);
            foreach (Control c in p.Controls) { var sub = c as Page; if (sub != null) Restore(sub); }
        }

        void TakeSnap(Page p)
        {
            var o = new Snap();
            foreach (Control c in p.Controls) o.Ctl.Add(new KeyValuePair<Control, Rectangle>(c, c.Bounds));
            o.Cards.AddRange(p.Cards);
            o.Lines.AddRange(p.Lines);
            foreach (var kv in p.Captions) o.Caps[kv.Key] = kv.Value.Key;
            snaps[p] = o;
        }

        static List<Item> ItemsOf(Page p, Control skip)
        {
            var list = new List<Item>();
            foreach (Control c in p.Controls) if (c != skip) list.Add(new Item { C = c, R = c.Bounds });
            for (int i = 0; i < p.Cards.Count; i++) list.Add(new Item { Card = i, R = p.Cards[i] });
            for (int i = 0; i < p.Lines.Count; i++) list.Add(new Item { Line = i, R = p.Lines[i] });
            foreach (var kv in p.Captions) list.Add(new Item { Cap = kv.Key, R = kv.Value.Key });
            return list;
        }

        // Розділи сторінки: кожен підпис починає новий розділ; усе, що нижче нього (до наступного підпису), — його вміст.
        static List<Block> BlocksOf(List<Item> items)
        {
            var caps = items.Where(i => i.Cap != null).Select(i => i.R.Top).OrderBy(y => y).ToList();
            var blocks = new SortedDictionary<int, Block>();
            foreach (var it in items)
            {
                int key = int.MinValue;
                foreach (int y in caps) if (y <= it.R.Top + Ui.S(2)) key = y;
                Block b;
                if (!blocks.TryGetValue(key, out b)) { b = new Block(); blocks[key] = b; }
                b.Items.Add(it);
                b.Top = Math.Min(b.Top, it.R.Top);
                b.Bottom = Math.Max(b.Bottom, it.R.Bottom);
                if (it.C != null && HasChart(it.C)) b.Wide = true;
            }
            return blocks.Values.Where(b => b.Items.Count > 0).ToList();
        }

        static bool HasChart(Control c)
        {
            if (c is TempChart || c is BarChart) return true;
            foreach (Control k in c.Controls) if (HasChart(k)) return true;
            return false;
        }

        // Написи — під елементами керування: коли рядок вужчає, довгий напис не перекриває перемикач.
        static void LabelsBack(Control parent)
        {
            foreach (Control c in parent.Controls.Cast<Control>().ToList())
            {
                if (c is Label) c.SendToBack();
                else if (c is Page) LabelsBack(c);
            }
        }

        void Arrange(Page p, int availW)
        {
            Restore(p);
            TakeSnap(p);
            LabelsBack(p);
            var hero = p.Controls.OfType<HeroCard>().FirstOrDefault();
            var items = ItemsOf(p, hero);

            // Сторінка з одним вкладеним розділом (Антивірус, Майнери, Мережа…) — розкладаємо його вміст.
            Page single = items.Count == 1 ? items[0].C as Page : null;
            Page target = single ?? p;
            if (single != null) { TakeSnap(single); items = ItemsOf(single, null); }

            var blocks = BlocksOf(items);
            bool two = availW >= Ui.S(2 * ColW + 24) && blocks.Count >= 1;
            bool narrow = !two && availW < Ui.S(ColW);
            int totalW = two ? Ui.S(2 * ColW) : narrow ? availW : Ui.S(ColW);
            int x0 = Math.Max(0, (availW - totalW) / 2);
            TW = totalW;
            pageGeom[p] = new Point(x0, totalW);

            if (hero != null) hero.Bounds = new Rectangle(x0 + Ui.S(24), hero.Top, totalW - Ui.S(48), hero.Height);

            int dx = x0;
            if (single != null)
            {
                int oldH = single.Height, oldBottom = blocks.Count > 0 ? blocks.Max(b => b.Bottom) : 0;
                single.Bounds = new Rectangle(x0, single.Top, totalW, single.Height);
                dx = 0;
                if (two)
                {
                    int bottom = Flow(single, blocks, 0);
                    single.Height = Math.Max(Ui.S(20), bottom + (oldH - oldBottom));
                    single.Invalidate();
                }
                else if (narrow) StretchInside(single);
                return;
            }
            if (narrow)
            {
                foreach (var b in blocks) Move(target, b, 0, 0, true);
                return;
            }
            if (!two)
            {
                foreach (var b in blocks) Move(target, b, dx, 0, false);
                return;
            }
            Flow(target, blocks, dx);
        }

        // Розкласти розділи у дві колонки: кожен — у ту, що коротша. Графіки — на всю ширину;
        // розділ, якому між графіками немає пари, теж розтягується на всю ширину (щоб не було дірок).
        int Flow(Page p, List<Block> blocks, int x0)
        {
            if (blocks.Count == 0) return 0;
            int gap = Ui.S(16), col = Ui.S(ColW);
            int y = blocks.Min(b => b.Top);
            int i = 0;
            while (i < blocks.Count)
            {
                if (blocks[i].Wide)
                {
                    Move(p, blocks[i], x0, y - blocks[i].Top, true);
                    y += blocks[i].Height + gap;
                    i++;
                    continue;
                }
                var seg = new List<Block>();
                while (i < blocks.Count && !blocks[i].Wide) seg.Add(blocks[i++]);
                if (seg.Count == 1)
                {
                    Move(p, seg[0], x0, y - seg[0].Top, true);
                    y += seg[0].Height + gap;
                    continue;
                }
                int[] cy = { y, y };
                foreach (var b in seg)
                {
                    int c = cy[1] < cy[0] ? 1 : 0;
                    Move(p, b, x0 + c * col, cy[c] - b.Top, false);
                    cy[c] += b.Height + gap;
                }
                y = Math.Max(cy[0], cy[1]);
            }
            return y - gap;
        }

        // Розтягування на дві колонки: поля по 24 лишаються, усе між ними пропорційно ширшає.
        // TW — ширина, до якої розтягуємо (дві колонки) або звужуємо (вузьке вікно) колонку шириною 600.
        static int TW;
        static int SX(int x) { int m = Ui.S(24); return m + (int)Math.Round((x - m) * StretchK); }
        static double StretchK { get { return (TW - 2.0 * Ui.S(24)) / (Ui.S(ColW) - 2.0 * Ui.S(24)); } }
        static Rectangle Stretch(Rectangle r)
        {
            int l = SX(r.Left), rr = SX(r.Right);
            return new Rectangle(l, r.Y, Math.Max(0, rr - l), r.Height);
        }

        // Розтягування окремого елемента: картки, лінії, підписи, списки, графіки, плитки й довгі написи — ширшають;
        // дрібні елементи (перемикачі, кнопки, поля) зберігають розмір і лишаються біля свого краю.
        static Rectangle StretchItem(Item it)
        {
            var r = it.R;
            int shift = TW - Ui.S(ColW);
            if (shift < 0 && it.C is Label && r.Width >= Ui.S(320))
            {
                var q0 = Stretch(r);
                return q0.X < r.X ? new Rectangle(r.X, q0.Y, Math.Max(Ui.S(40), q0.Right - r.X), q0.Height) : q0;
            }
            if (it.C == null || it.C is StatTile || r.Width >= Ui.S(320)) return Stretch(r);
            if (shift < 0)
            {
                // Вузьке вікно: перемикачі тримають розмір біля правого краю, кнопки під карткою — свої місця,
                // решта (вибір режимів, поля, короткі написи) пропорційно звужується разом із рядком.
                if (it.C is Toggle) return new Rectangle(r.X + shift, r.Y, r.Width, r.Height);
                if (it.C is PillBtn || it.C is LinkBtn)
                    return r.Right >= Ui.S(ColW - 60) ? new Rectangle(r.X + shift, r.Y, r.Width, r.Height) : r;
                var q = Stretch(r);
                // Написи не зсуваємо лівіше (щоб не налазили на заокруглений край картки).
                if (it.C is Label && q.X < r.X) q = new Rectangle(r.X, q.Y, Math.Max(Ui.S(40), q.Right - r.X), q.Height);
                return q;
            }
            // Кнопки під карткою тримаються купкою зліва; праворуч їде лише та, що й була притиснута до правого краю.
            if (it.C is PillBtn || it.C is LinkBtn)
                return r.Right >= Ui.S(ColW - 60) ? new Rectangle(r.X + shift, r.Y, r.Width, r.Height) : r;
            // Перемикачі, поля, значення праворуч у рядку — зберігають відстань до правого краю.
            if (r.Left + r.Width / 2 >= Ui.S(ColW / 2)) return new Rectangle(r.X + shift, r.Y, r.Width, r.Height);
            return r;
        }

        // Вкладений розділ (окрема Page на всю ширину) — розтягнути разом із вмістом.
        void StretchInside(Page sub)
        {
            if (!snaps.ContainsKey(sub)) TakeSnap(sub);
            foreach (var it in ItemsOf(sub, null)) Place(sub, it, StretchItem(it), true);
            sub.Invalidate();
        }

        void Place(Page p, Item it, Rectangle r, bool stretched)
        {
            if (it.C != null)
            {
                var sub = it.C as Page;
                if (stretched && sub != null && it.R.X <= Ui.S(2) && it.R.Width >= Ui.S(ColW - 4))
                {
                    sub.Bounds = new Rectangle(it.R.X, r.Y, TW, r.Height);
                    StretchInside(sub);
                }
                else it.C.Bounds = r;
            }
            else if (it.Card >= 0) p.Cards[it.Card] = r;
            else if (it.Line >= 0) p.Lines[it.Line] = r;
            else if (it.Cap != null)
            {
                p.CapMove[it.Cap] = new Rectangle(r.X - it.R.X, r.Y - it.R.Y, r.Width - it.R.Width, 0);
                p.Captions[it.Cap] = new KeyValuePair<Rectangle, string>(r, p.Captions[it.Cap].Value);
            }
        }

        // Зсунути розділ на dx, dy; stretch — розтягнути його на обидві колонки.
        void Move(Page p, Block b, int dx, int dy, bool stretch)
        {
            foreach (var it in b.Items)
            {
                var r = stretch ? StretchItem(it) : it.R;
                r.Offset(dx, dy);
                if (stretch && it.C is Page && it.R.X <= Ui.S(2) && it.R.Width >= Ui.S(ColW - 4))
                {
                    var sub = (Page)it.C;
                    sub.Bounds = new Rectangle(it.R.X + dx, it.R.Y + dy, TW, it.R.Height);
                    StretchInside(sub);
                    continue;
                }
                Place(p, it, r, false);
            }
            p.Invalidate();
        }
    }
}
