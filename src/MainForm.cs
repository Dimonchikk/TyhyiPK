// Тихий ПК — головне вікно.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TyhyiPK
{
    partial class MainForm : Form
    {
        const double GB = 1073741824.0;
        static readonly HashSet<string> Protected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "System", "Idle", "Registry", "smss", "csrss", "wininit", "winlogon", "services", "lsass", "LsaIso", "svchost", "dwm",
            "explorer", "fontdrvhost", "Memory Compression", "MsMpEng", "NisSrv", "SecurityHealthService", "conhost", "sihost",
            "ctfmon", "audiodg", "spoolsv", "WmiPrvSE", "RuntimeBroker", "taskhostw", "StartMenuExperienceHost", "SearchHost",
            "SearchIndexer", "ShellExperienceHost", "TextInputHost", "dllhost", "WUDFHost", "Secure System", "vmmem",
        };
        static readonly string[] GameModeKeys = { "boost", "super" };
        static readonly string[] QuietModeKeys = { "silent", "quiet" };
        static readonly int[] HeatLimits = { 80, 85, 90 };
        static readonly string[] NavTitles = { "Огляд", "Датчики", "Програми", "Автоматика", "Енергія", "Сервіс", "Значок" };
        static readonly string[] NavIcons = { "overview", "sensors", "apps", "auto", "energy", "service", "fan" };
        static readonly string[] NavSubs =
        {
            "Твій комп'ютер з першого погляду.", "Температури, диски й батарея.", "Що працює зараз і що стартує з Windows.",
            "Налаштуй один раз — далі все саме.", "Скільки споживає ПК і скільки це коштує.", "Захист, очищення й турбота про систему.",
            "Живий вентилятор біля годинника.",
        };
        const int SideW = 200, HeaderH = 92;
        int autoBottom;
        static int AutoPageH = 600;   // висота області сторінки (менша на маленьких екранах)

        readonly Row cpu = new Row(), gpu = new Row();
        readonly Row[] rows;
        readonly Page pageModes, pageSensors, pageProcs, pageAuto;
        readonly Page[] pages;
        readonly Segmented segGame, segNight, segBattery, segHeat;
        readonly Sidebar nav;
        readonly Label lblTitle, lblSub;
        readonly StatusPill pill;
        readonly ThemeSwitch themeSeg;
        static readonly string[] ThemeKeys = { "auto", "light", "dark" };
        readonly HeroCard hero;
        readonly Label lblStatus;
        readonly StatTile mCpuTemp, mGpuTemp, mTotal, mFan;
        readonly StatTile sCpuTemp, sCpuLoad, sCpuPower, sFan, sGpuTemp, sGpuPower, sMem, sTotal;
        readonly TempChart chart;
        readonly ProcList procList;
        readonly Toggle tgGame, tgNight, tgBattery, tgHeat, tgAutorun;
        readonly TimeBox tbFrom, tbTo;
        readonly NotifyIcon tray;
        readonly ContextMenuStrip trayMenu;
        readonly System.Windows.Forms.Timer procTimer, autoTimer;
        readonly int cores = Environment.ProcessorCount;
        readonly string selfName = Process.GetCurrentProcess().ProcessName;
        readonly bool hasBattery;
        readonly List<TempSample> history = new List<TempSample>();
        readonly UserPreferenceChangedEventHandler themeHandler;
        bool allowShow, exiting, started, loading = true;
        volatile bool uiVisible, stop;
        Snapshot last = new Snapshot();
        Dictionary<int, double> prevCpu = new Dictionary<int, double>();
        DateTime prevTime = DateTime.UtcNow;
        readonly Dictionary<int, KeyValuePair<string, string>> pathCache = new Dictionary<int, KeyValuePair<string, string>>();

        [DllImport("kernel32.dll")]
        static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("kernel32.dll")]
        static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus m);

        public MainForm(bool startHidden, EventWaitHandle showSignal)
        {
            allowShow = !startHidden;
            rows = new[] { cpu, gpu };
            try
            {
                var bs = SystemInformation.PowerStatus.BatteryChargeStatus;
                hasBattery = (bs & BatteryChargeStatus.NoSystemBattery) == 0 && bs != BatteryChargeStatus.Unknown;
            }
            catch { hasBattery = false; }
#if LINUXTEST
            hasBattery = true;
#endif

            Text = "Тихий ПК";
            AutoScaleMode = AutoScaleMode.None;
            try
            {
                int avail = (int)((Screen.PrimaryScreen.WorkingArea.Height - SystemInformation.CaptionHeight - Ui.S(24)) / Ui.Scale) - HeaderH;
                AutoPageH = Math.Max(420, Math.Min(600, avail));
            }
            catch { }
            // Змінюване вікно — щоб працювали «Розгорнути» й Snap Layouts; тягнути край мишкою не можна (див. WndProc).
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            ClientSize = new Size(Ui.S(SideW + 600), Ui.S(HeaderH + AutoPageH));
            MinimumSize = new Size(Ui.S(700), Ui.S(420));
            StartPosition = FormStartPosition.CenterScreen;
            Font = Ui.Body;
            BackColor = Ui.Bg;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            Gpu.Detect();

            // ── Бокове меню й заголовок ──
            nav = new Sidebar();
            nav.Items = NavTitles;
            nav.IconKeys = NavIcons;
            nav.Dots = new bool[NavTitles.Length];
            nav.Bounds = Ui.R(0, 0, SideW, HeaderH + AutoPageH);
            nav.ItemClicked += i => ShowPage(i);
            Controls.Add(nav);
            lblStatus = Ui.MakeLabel(nav, "", Ui.R(24, HeaderH + AutoPageH - 134, SideW - 40, 96), Ui.Caps, Ui.CInk2, Ui.CSide);
            lblStatus.TextAlign = ContentAlignment.BottomLeft;
            lblTitle = Ui.MakeLabel(this, "", Ui.R(SideW + 22, 18, 380, 44), Ui.H1, Ui.CInk, Ui.CBg);
            lblSub = Ui.MakeLabel(this, "", Ui.R(SideW + 25, 60, 360, 22), Ui.Small, Ui.CInk2, Ui.CBg);
            themeSeg = new ThemeSwitch();
            themeSeg.Bounds = Ui.R(SideW + 468, 20, 108, 32);
            themeSeg.Selected = Math.Max(0, Array.IndexOf(ThemeKeys, Settings.Get("Theme", "auto")));
            themeSeg.Changed += i => { Settings.Set("Theme", ThemeKeys[i]); ApplyThemeNow(); };
            Controls.Add(themeSeg);
            pill = new StatusPill();
            pill.Bounds = Ui.R(SideW + 376, 56, 200, 28);
            Controls.Add(pill);

            pageModes = NewPage();
            pageSensors = NewPage();
            pageProcs = NewPage();
            pageAuto = NewPage();
            pageEnergy = NewPage();
            pageService = NewPage();
            pageIcon = NewPage();
            pages = new[] { pageModes, pageSensors, pageProcs, pageAuto, pageEnergy, pageService, pageIcon };

            // ── Огляд ──
            hero = new HeroCard();
            hero.Bounds = Ui.R(24, 4, 552, 72);
            hero.Set("Перевіряю комп'ютер…", "Це займе кілька секунд.", "leaf", Ui.Green);
            pageModes.Controls.Add(hero);
            pageModes.Cards.Add(Ui.R(24, 88, 552, 88));
            mCpuTemp = Tile(pageModes, 0, 92, 80, "Процесор");
            mGpuTemp = Tile(pageModes, 1, 92, 80, "Відеокарта");
            mTotal = Tile(pageModes, 2, 92, 80, "Разом");
            mFan = Tile(pageModes, 3, 92, 80, "Кулер ЦП");

            cpu.Id = "cpu";
            cpu.Title = "Процесор";
            cpu.Apply = Power.Apply;
            cpu.Desc = m => Modes.Get(m).Desc;
            pageModes.SetCaption("cpu", Ui.R(40, 188, 520, 18), "Процесор");
            pageModes.Cards.Add(Ui.R(24, 208, 552, 112));
            BuildRow(cpu, 224);

            gpu.Id = "gpu";
            gpu.Title = "Відеокарта";
            gpu.Apply = Gpu.Apply;
            gpu.Desc = Gpu.Desc;
            pageModes.SetCaption("gpu", Ui.R(40, 332, 520, 18), "Відеокарта" + (!string.IsNullOrEmpty(Gpu.Name) ? "|" + Gpu.Name : ""));
            pageModes.Cards.Add(Ui.R(24, 352, 552, 112));
            BuildRow(gpu, 368);

            // ── Датчики ──
            pageSensors.Cards.Add(Ui.R(24, 8, 552, 172));
            pageSensors.Lines.Add(Ui.R(40, 92, 520, 0));
            sCpuTemp = Tile(pageSensors, 0, 12, 80, "Процесор");
            sCpuLoad = Tile(pageSensors, 1, 12, 80, "Навантаження");
            sCpuPower = Tile(pageSensors, 2, 12, 80, "Споживання ЦП");
            sFan = Tile(pageSensors, 3, 12, 80, "Кулер ЦП");
            sGpuTemp = Tile(pageSensors, 0, 93, 79, "Відеокарта");
            sGpuPower = Tile(pageSensors, 1, 93, 79, "Споживання карти");
            sMem = Tile(pageSensors, 2, 93, 79, "Пам'ять");
            sTotal = Tile(pageSensors, 3, 93, 79, "Разом");
            pageSensors.SetCaption("chart", Ui.R(40, 192, 520, 18), "Температура|за останні 10 хвилин");
            pageSensors.Cards.Add(Ui.R(24, 212, 552, 192));
            chart = new TempChart();
            chart.Data = history;
            chart.Bounds = Ui.R(32, 216, 536, 184);
            pageSensors.Controls.Add(chart);
            foreach (var t in new[] { mCpuTemp, sCpuTemp }) t.SubClicked += (s, e) => ShowTempHelp();
            foreach (var t in new[] { mFan, sFan, sCpuPower }) t.SubClicked += (s, e) => ShowTempHelp();

            // ── Процеси ──
            pageProcs.Cards.Add(Ui.R(24, 8, 552, 372));
            procList = new ProcList();
            procList.Bounds = Ui.R(32, 14, 536, 290);
            pageProcs.Controls.Add(procList);
            Ui.MakeLabel(pageProcs, "Вибери програму у списку, щоб її закрити.", Ui.R(40, 330, 320, 36), Ui.Small, Ui.CInk3, Ui.CCard)
                .TextAlign = ContentAlignment.MiddleLeft;
            var kill = new PillBtn();
            kill.Text = "Закрити вибрану";
            kill.Fore = () => Ui.Red;
            kill.Bounds = Ui.R(388, 330, 172, 36);
            kill.Click += (s, e) => KillSelected();
            pageProcs.Controls.Add(kill);

            // ── Автоматика ──
            int y = 8;
            RowLabel("Коли запущена гра", Ui.R(40, y, 176, 55));
            segGame = MiniSeg(GameModeKeys, Ui.R(220, y + 12, 230, 32));
            tgGame = NewToggle(y + 14);
            y += 56;
            pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            var games = new LinkBtn();
            games.Text = "Список ігор";
            games.Chevron = true;
            games.Bounds = Ui.R(40, y + 1, 520, 42);
            games.Click += (s, e) =>
            {
                using (var f = new GamesForm()) f.ShowDialog(this);
                EvaluateAuto();
            };
            pageAuto.Controls.Add(games);
            y += 44;
            pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            RowLabel("Вночі з", Ui.R(40, y + 1, 68, 55));
            tbFrom = new TimeBox();
            tbFrom.Bounds = Ui.R(108, y + 13, 66, 30);
            pageAuto.Controls.Add(tbFrom);
            RowLabel("до", Ui.R(174, y + 1, 34, 55)).TextAlign = ContentAlignment.MiddleCenter;
            tbTo = new TimeBox();
            tbTo.Bounds = Ui.R(208, y + 13, 66, 30);
            pageAuto.Controls.Add(tbTo);
            segNight = MiniSeg(QuietModeKeys, Ui.R(290, y + 12, 200, 32));
            tgNight = NewToggle(y + 14);
            y += 56;
            pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            segBattery = new Segmented();
            tgBattery = new Toggle();
            if (hasBattery)
            {
                RowLabel("Від батареї", Ui.R(40, y + 1, 176, 55));
                segBattery = MiniSeg(QuietModeKeys, Ui.R(220, y + 12, 230, 32));
                tgBattery = NewToggle(y + 14);
                y += 56;
                pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            }
            RowLabel("Захист від перегріву", Ui.R(40, y + 1, 176, 55));
            segHeat = new Segmented();
            segHeat.Items = HeatLimits.Select(l => l + "°").ToArray();
            segHeat.Bounds = Ui.R(220, y + 12, 180, 32);
            pageAuto.Controls.Add(segHeat);
            tgHeat = NewToggle(y + 14);
            y += 56;
            pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            RowLabel("Не засинати під час завантажень", Ui.R(40, y + 4, 420, 26));
            lblSleep = Ui.MakeLabel(pageAuto, "", Ui.R(40, y + 29, 460, 22), Ui.Small, Ui.CInk2, Ui.CCard);
            tgSleep = NewToggle(y + 14);
            y += 56;
            pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            RowLabel("Перевіряти оновлення", Ui.R(40, y + 4, 420, 26));
            Ui.MakeLabel(pageAuto, "Програма сама кожні кілька годин питає GitHub, чи вийшла нова версія.", Ui.R(40, y + 29, 460, 22), Ui.Small, Ui.CInk2, Ui.CCard);
            tgUpdates = NewToggle(y + 14);
            y += 56;
            pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            RowLabel("Підсумок тижня", Ui.R(40, y + 4, 420, 26));
            Ui.MakeLabel(pageAuto, "У неділю ввечері: години роботи, найвищі температури й вартість електрики.", Ui.R(40, y + 29, 460, 22), Ui.Small, Ui.CInk2, Ui.CCard);
            tgWeekly = NewToggle(y + 14);
            y += 56;
            pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            RowLabel("Запускати разом з Windows", Ui.R(40, y + 1, 400, 55));
            tgAutorun = NewToggle(y + 14);
            y += 56;
            pageAuto.Lines.Add(Ui.R(40, y, 536, 0));
            var wipe = new LinkBtn();
            wipe.Text = "Видалити всі дані програми…";
            wipe.Tint = () => Ui.Red;
            wipe.Chevron = true;
            wipe.Bounds = Ui.R(40, y + 1, 520, 42);
            wipe.Click += (s, e) => WipeAll();
            pageAuto.Controls.Add(wipe);
            y += 44;
            pageAuto.Cards.Add(Ui.R(24, 8, 552, y - 8));
            Ui.MakeLabel(pageAuto,
                "Автоматика вмикає режим одразу для процесора й відеокарти. Якщо вибрати режим вручну, вона не втручається, поки умова не зникне" +
                (hasBattery ? " (гра закриється, підключиться зарядка, мине ніч)." : " (гра закриється або мине ніч).") +
                " Захист від перегріву спрацьовує, коли температура тримається вище межі понад 10 секунд, і обмежує режим до «Тихого», поки не охолоне. Хрестик ховає вікно біля годинника.",
                Ui.R(28, y + 12, 544, 80), Ui.Small, Ui.CInk2, Ui.CBg);
            autoBottom = y + 100;

            BuildEnergy();
            BuildSecurity();
            BuildService();
            BuildHome();
            SetupNavSubs();

            // ── Значок біля годинника ──
            trayMenu = new ContextMenuStrip();
            foreach (var m in Modes.All)
            {
                var item = new ToolStripMenuItem(m.Title);
                item.Tag = m.Key;
                item.Click += (s, e) =>
                {
                    string key = (string)((ToolStripMenuItem)s).Tag;
                    foreach (var r in rows) if (r.Available) UserSelect(r, key, false);
                    EvaluateAuto();
                };
                trayMenu.Items.Add(item);
            }
            trayMenu.Items.Add(new ToolStripSeparator());
            var open = new ToolStripMenuItem("Відкрити вікно");
            open.Font = new Font(trayMenu.Font, FontStyle.Bold);
            open.Click += (s, e) => ShowWindow();
            trayMenu.Items.Add(open);
            var exit = new ToolStripMenuItem("Вийти");
            exit.Click += (s, e) => ExitApp();
            var wipeItem = new ToolStripMenuItem("Видалити всі дані програми…");
            wipeItem.Click += (s, e) => { ShowWindow(); WipeAll(); };
            trayMenu.Items.Add(wipeItem);
            trayMenu.Items.Add(exit);
            trayMenu.Opening += (s, e) =>
            {
                foreach (ToolStripItem it in trayMenu.Items)
                {
                    var mi = it as ToolStripMenuItem;
                    if (mi != null && mi.Tag is string) mi.Checked = (string)mi.Tag == cpu.Current;
                }
            };
            tray = new NotifyIcon();
            tray.Icon = Icon ?? SystemIcons.Application;
            tray.ContextMenuStrip = trayMenu;
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowWindow(); };
            tray.Visible = true;
            BuildIconPage();
            SetupUpdates();

            // ── Початковий стан ──
            cpu.Current = Power.CurrentMode();
            cpu.Manual = ValidMode(Settings.Get("Manual", null), cpu.Current);
            Settings.Set("Manual", cpu.Manual);
            if (Gpu.Smi == null)
            {
                gpu.Available = false;
                gpu.UnavailableText = "Відеокарту NVIDIA не знайдено. Керування є лише для карт NVIDIA з їхнім драйвером.";
            }
            else if (!Gpu.CanLimit)
            {
                gpu.Available = false;
                gpu.UnavailableText = "Драйвер NVIDIA не дозволяє змінювати ліміт споживання цієї відеокарти (так часто буває на ноутбуках).";
            }
            else if (!Program.IsAdmin)
            {
                gpu.Available = false;
                gpu.UnavailableText = "Щоб керувати відеокартою, запусти програму від імені адміністратора.";
            }
            string gm = Settings.Get("GpuManual", null);
            gpu.Current = Gpu.ModeFor(Gpu.StartLimit, gm);
            gpu.Manual = ValidMode(gm, gpu.Current);
            if (gpu.Available) Settings.Set("GpuManual", gpu.Manual);

            tgGame.Checked = Settings.GetBool("AutoGame", true);
            segGame.Selected = Math.Max(0, Array.IndexOf(GameModeKeys, Settings.Get("GameMode", "boost")));
            tgNight.Checked = Settings.GetBool("Night", false);
            tbFrom.Value = ParseTime(Settings.Get("NightFrom", "23:00"), 23);
            tbTo.Value = ParseTime(Settings.Get("NightTo", "07:00"), 7);
            segNight.Selected = Math.Max(0, Array.IndexOf(QuietModeKeys, Settings.Get("NightMode", "silent")));
            tgBattery.Checked = hasBattery && Settings.GetBool("Battery", true);
            segBattery.Selected = Math.Max(0, Array.IndexOf(QuietModeKeys, Settings.Get("BatteryMode", "quiet")));
            tgHeat.Checked = Settings.GetBool("Heat", true);
            segHeat.Selected = Math.Max(0, Array.IndexOf(HeatLimits, ParseInt(Settings.Get("HeatLimit", "85"), 85)));

            tgGame.Changed += (s, e) => SaveAuto();
            segGame.ItemClicked += i => { segGame.Selected = i; SaveAuto(); };
            tgNight.Changed += (s, e) => SaveAuto();
            tbFrom.Changed += (s, e) => SaveAuto();
            tbTo.Changed += (s, e) => SaveAuto();
            segNight.ItemClicked += i => { segNight.Selected = i; SaveAuto(); };
            tgBattery.Changed += (s, e) => SaveAuto();
            segBattery.ItemClicked += i => { segBattery.Selected = i; SaveAuto(); };
            tgHeat.Changed += (s, e) => SaveAuto();
            segHeat.ItemClicked += i => { segHeat.Selected = i; SaveAuto(); };
            tgAutorun.Changed += (s, e) =>
            {
                bool on = tgAutorun.Checked;
                ThreadPool.QueueUserWorkItem(_ => Autostart.Set(on));
            };
            loading = false;
            foreach (var r in rows) RefreshRow(r);
            UpdateTrayText();
            FitPages();
            ShowPage(0);
            SetupLayout();

            procTimer = new System.Windows.Forms.Timer();
            procTimer.Interval = 2000;
            procTimer.Tick += (s, e) =>
            {
                procTimer.Interval = 2000;
                if (Visible && pageProcs.Visible) UpdateProcs();
            };
            autoTimer = new System.Windows.Forms.Timer();
            autoTimer.Interval = 5000;
            autoTimer.Tick += (s, e) => EvaluateAuto();
            FormClosing += OnClosing;
            VisibleChanged += (s, e) => uiVisible = Visible;
            Shown += (s, e) => Activate();
            themeHandler = (s, e) => { try { BeginInvoke(new MethodInvoker(CheckTheme)); } catch { } };
            SystemEvents.UserPreferenceChanged += themeHandler;
            FormClosed += (s, e) => SystemEvents.UserPreferenceChanged -= themeHandler;
            if (!IsHandleCreated) CreateHandle();
            if (showSignal != null)
                ThreadPool.RegisterWaitForSingleObject(showSignal, (st, to) => { try { BeginInvoke(new MethodInvoker(ShowWindow)); } catch { } }, null, -1, false);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool en = Autostart.Enabled();   // лише читаємо: автозапуск змінює тільки сам користувач перемикачем
                try { BeginInvoke(new MethodInvoker(() => tgAutorun.Checked = en)); } catch { }
            });
            var th = new Thread(SensorLoop);
            th.IsBackground = true;
            th.Start();
            var sec = new Thread(SecurityLoop);
            sec.IsBackground = true;
            sec.Priority = ThreadPriority.BelowNormal;
            sec.Start();
            procTimer.Start();
            autoTimer.Start();
            EvaluateAuto();
#if LINUXTEST
            Demo();
#endif
            started = true;
        }
    }
}
