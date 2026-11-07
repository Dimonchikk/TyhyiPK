// Тихий ПК — вікно «Список ігор».
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace TyhyiPK
{
    class GamesForm : Form
    {
        readonly ComboBox cmb;
        readonly ListBox lbGames, lbIgnore;
        readonly int themeMark;

        PillBtn Btn(Control parent, string text, Rectangle r, EventHandler click)
        {
            var b = new PillBtn();
            b.Text = text;
            b.Bounds = r;
            b.Click += click;
            parent.Controls.Add(b);
            return b;
        }

        ListBox List(Page page, Rectangle r)
        {
            var lb = new ListBox();
            lb.Bounds = r;
            lb.BorderStyle = BorderStyle.None;
            lb.Font = Ui.Body;
            lb.IntegralHeight = false;
            lb.BackColor = Ui.Fill;
            lb.ForeColor = Ui.Ink;
            page.Controls.Add(lb);
            return lb;
        }

        public GamesForm()
        {
            themeMark = Ui.Rethemers.Count;
            Text = "Список ігор";
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Ui.S(500), Ui.S(560));
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = Ui.Body;
            BackColor = Ui.Bg;

            var page = new Page();
            page.Bounds = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
            Controls.Add(page);
            Ui.MakeLabel(page, "Грою вважається все, що запущено з папок Steam, Epic Games, GOG, Ubisoft, EA, Xbox і Riot, а також популярні ігри (CS2, Dota 2, Valorant, Fortnite, GTA V, Minecraft та інші) і все зі списку «Мої ігри».",
                Ui.R(24, 14, 452, 60), Ui.Small, Ui.CInk2, Ui.CBg);

            page.SetCaption("run", Ui.R(40, 82, 420, 18), "Запущені програми");
            page.Cards.Add(Ui.R(24, 102, 452, 52));
            cmb = new ComboBox();
            cmb.DropDownStyle = ComboBoxStyle.DropDownList;
            cmb.FlatStyle = FlatStyle.Flat;
            cmb.Font = Ui.Body;
            cmb.BackColor = Ui.Fill;
            cmb.ForeColor = Ui.Ink;
            cmb.Bounds = Ui.R(38, 114, 300, 28);
            page.Controls.Add(cmb);
            Btn(page, "Оновити", Ui.R(350, 111, 112, 32), (s, e) => FillRunning());

            page.SetCaption("games", Ui.R(40, 168, 420, 18), "Мої ігри");
            page.Cards.Add(Ui.R(24, 188, 452, 150));
            lbGames = List(page, Ui.R(38, 200, 290, 126));
            Btn(page, "Додати вибрану", Ui.R(340, 198, 124, 32), (s, e) => AddTo(Games.User, cmb.SelectedItem as string));
            Btn(page, "Вибрати .exe…", Ui.R(340, 236, 124, 32), (s, e) => BrowseExe());
            Btn(page, "Видалити", Ui.R(340, 274, 124, 32), (s, e) => RemoveFrom(Games.User, lbGames)).Fore = () => Ui.Red;

            page.SetCaption("ignore", Ui.R(40, 352, 420, 18), "Не вважати грою");
            page.Cards.Add(Ui.R(24, 372, 452, 120));
            lbIgnore = List(page, Ui.R(38, 384, 290, 96));
            Btn(page, "Додати вибрану", Ui.R(340, 382, 124, 32), (s, e) => AddTo(Games.Ignore, cmb.SelectedItem as string));
            Btn(page, "Видалити", Ui.R(340, 420, 124, 32), (s, e) => RemoveFrom(Games.Ignore, lbIgnore)).Fore = () => Ui.Red;

            var done = Btn(page, "Готово", Ui.R(364, 508, 112, 36), (s, e) => Close());
            done.OnBg = true;
            done.FillColor = () => Ui.Blue;
            done.Fore = () => Color.White;

            // Підписи цього вікна реєструються в Ui.Rethemers — прибираємо їх після закриття.
            FormClosed += (s, e) =>
            {
                if (Ui.Rethemers.Count > themeMark) Ui.Rethemers.RemoveRange(themeMark, Ui.Rethemers.Count - themeMark);
            };
            FillRunning();
            RefreshLists();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Ui.StyleCaption(this);
        }

        void FillRunning()
        {
            cmb.Items.Clear();
            var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            string self = Process.GetCurrentProcess().ProcessName;
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try { if (p.MainWindowHandle != IntPtr.Zero && p.ProcessName != self) names.Add(p.ProcessName); }
                    catch { }
                }
            }
            foreach (string n in names) cmb.Items.Add(n);
            if (cmb.Items.Count > 0) cmb.SelectedIndex = 0;
        }

        void BrowseExe()
        {
            using (var d = new OpenFileDialog())
            {
                d.Filter = "Програми (*.exe)|*.exe";
                d.Title = "Вибери .exe гри";
                if (d.ShowDialog(this) == DialogResult.OK) AddTo(Games.User, Path.GetFileNameWithoutExtension(d.FileName));
            }
        }

        void AddTo(List<string> list, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!list.Any(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase))) list.Add(name);
            var other = list != Games.User ? Games.User : Games.Ignore;
            other.RemoveAll(s => string.Equals(s, name, StringComparison.OrdinalIgnoreCase));
            Games.Save();
            RefreshLists();
        }

        void RemoveFrom(List<string> list, ListBox lb)
        {
            string name = lb.SelectedItem as string;
            if (name == null) return;
            list.RemoveAll(s => s == name);
            Games.Save();
            RefreshLists();
        }

        void RefreshLists()
        {
            lbGames.Items.Clear();
            foreach (string s in Games.User) lbGames.Items.Add(s);
            lbIgnore.Items.Clear();
            foreach (string s in Games.Ignore) lbIgnore.Items.Add(s);
        }
    }
}
