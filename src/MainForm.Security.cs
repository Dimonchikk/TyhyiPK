// Тихий ПК — вкладка «Безпека».
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        Page[] secPages;
        readonly bool[] secAlert = new bool[5];   // 0 захист, 1 майнери, 2 мережа, 3 автозапуск, 4 диски й батарея

        KeyValueList kvDefender;
        PillBtn btnScan;
        Label lblDef, lblMinerState, lblHealth;
        InfoList lstMiners, lstNet, lstAuto, lstDisks;
        HeroCard heroDef, heroMiner, heroNet;
        MeterBar barHealth, barCharge;

        HeroCard Hero(Page p, string title, string sub, string icon)
        {
            var h = new HeroCard();
            h.Bounds = Ui.R(24, 0, 552, 72);
            h.Set(title, sub, icon, Ui.Green);
            p.Controls.Add(h);
            return h;
        }
        PillBtn btnAutoToggle;
        StatTile bMax, bCycles, bCharge, bRate;

        readonly MinerWatch miner = new MinerWatch();
        readonly HashSet<string> notified = new HashSet<string>();
        List<AutoItem> autoItems = new List<AutoItem>();
        bool scanning, secBusyDef, secBusyNet, secBusyHealth, minerChecking;
        DateTime defLoaded = DateTime.MinValue;
        System.Windows.Forms.Timer secTimer;

        // Розділ без власного місця: його вставляють у потрібну сторінку через Place().
        static Page Section(int h)
        {
            var p = new Page();
            p.Bounds = Ui.R(0, 0, 600, h);
            return p;
        }

        ProgressStrip pgScan, pgClean, pgSpeed, pgWin, pgTest;
        PillBtn btnDefOpen, btnCleanRecount;
        Label lblTestNote;

        ProgressStrip AddStrip(Page p, Rectangle r)
        {
            var s = new ProgressStrip();
            s.Bounds = r;
            p.Controls.Add(s);
            return s;
        }

        PillBtn SecBtn(Page p, string text, Rectangle r, EventHandler click, bool primary)
        {
            var b = new PillBtn();
            b.Text = text;
            b.Bounds = r;
            b.OnBg = true;
            if (primary) { b.FillColor = () => Ui.Blue; b.Fore = () => Color.White; }
            b.Click += click;
            p.Controls.Add(b);
            return b;
        }

        Label Note(Page p, string text, Rectangle r)
        {
            return Ui.MakeLabel(p, text, r, Ui.Small, Ui.CInk2, Ui.CBg);
        }

        InfoList SecList(Page p, Rectangle card, string empty)
        {
            p.Cards.Add(card);
            var l = new InfoList();
            l.Bounds = new Rectangle(card.X + Ui.S(8), card.Y + Ui.S(4), card.Width - Ui.S(16), card.Height - Ui.S(8));
            l.EmptyText = empty;
            p.Controls.Add(l);
            return l;
        }

        void BuildSecurity()
        {
            secPages = new Page[6];

            // ── Антивірус ──
            var p = secPages[0] = Section(420);
            heroDef = Hero(p, "Перевіряю захист…", "Читаю стан антивірусу й брандмауера.", "service");
            p.Cards.Add(Ui.R(24, 84, 552, 228));
            kvDefender = new KeyValueList();
            kvDefender.Bounds = Ui.R(32, 88, 536, 220);
            kvDefender.Set(0, "Антивірус", "перевіряю…", Ui.Ink3);
            kvDefender.Set(1, "Захист у реальному часі", "", null);
            kvDefender.Set(2, "Антивірусні бази", "", null);
            kvDefender.Set(3, "Остання перевірка ПК", "", null);
            kvDefender.Set(4, "Брандмауер Windows", "", null);
            p.Controls.Add(kvDefender);
            btnScan = SecBtn(p, "Швидка перевірка", Ui.R(24, 324, 190, 36), (s, e) => QuickScan(), true);
            btnDefOpen = SecBtn(p, "Відкрити «Безпеку Windows»", Ui.R(222, 324, 250, 36), (s, e) => OpenUrl("windowsdefender:"), false);
            pgScan = AddStrip(p, Ui.R(24, 322, 552, 40));
            lblDef = Note(p, "", Ui.R(28, 368, 544, 48));

            // ── Майнери ──
            p = secPages[1] = Section(452);
            heroMiner = Hero(p, "Стежу за простоєм…", "Збираю дані.", "cpu");
            lblMinerState = new Label();   // стан показує головна картка
            Note(p, "Приховані майнери крадуть електрику й гріють ПК, коли ти ним не користуєшся. Раз на 10 секунд програма дивиться, хто вантажить процесор і відеокарту у простої.",
                Ui.R(28, 80, 544, 40));
            lstMiners = SecList(p, Ui.R(24, 126, 552, 276), "Нічого підозрілого не знайдено");
            lstMiners.Icons = true;
            lstMiners.EmptyGood = true;
            SecBtn(p, "Перевірити зараз", Ui.R(24, 412, 170, 36), (s, e) => MinerCheckNow(), true);
            SecBtn(p, "Показати файл", Ui.R(202, 412, 150, 36), (s, e) => ShowSelectedFile(lstMiners), false);
            SecBtn(p, "Закрити процес", Ui.R(360, 412, 160, 36), (s, e) => KillFromList(lstMiners), false).Fore = () => Ui.Red;

            // ── Мережа ──
            p = secPages[2] = Section(452);
            heroNet = Hero(p, "Перевіряю з'єднання…", "Дивлюся, які програми в інтернеті.", "net");
            Note(p, "Програми, які зараз з'єднані з інтернетом. Червоним позначено з'єднання з портами майнінг-пулів, помаранчевим — програми без цифрового підпису.",
                Ui.R(28, 80, 544, 40));
            lstNet = SecList(p, Ui.R(24, 126, 552, 276), "Немає з'єднань з інтернетом");
            lstNet.Icons = true;
            lstNet.EmptyIcon = "net";
            SecBtn(p, "Оновити", Ui.R(24, 412, 130, 36), (s, e) => RefreshNet(), true);
            SecBtn(p, "Показати файл", Ui.R(162, 412, 150, 36), (s, e) => ShowSelectedFile(lstNet), false);
            SecBtn(p, "Закрити процес", Ui.R(320, 412, 160, 36), (s, e) => KillFromList(lstNet), false).Fore = () => Ui.Red;

            // ── Автозапуск ──
            p = secPages[3] = Section(448);
            Note(p, "Програми, що стартують разом з Windows. Про новий запис Тихий ПК повідомить біля годинника. Вимкнений запис нікуди не зникає — його можна увімкнути назад.",
                Ui.R(28, 0, 544, 40));
            lstAuto = SecList(p, Ui.R(24, 46, 552, 350), "Автозапуск порожній");
            lstAuto.Icons = true;
            lstAuto.EmptyIcon = "apps";
            btnAutoToggle = SecBtn(p, "Вимкнути", Ui.R(24, 406, 130, 36), (s, e) => ToggleAutorun(), true);
            SecBtn(p, "Показати файл", Ui.R(162, 406, 150, 36), (s, e) => ShowSelectedFile(lstAuto), false);
            SecBtn(p, "Усе гаразд", Ui.R(320, 406, 130, 36), (s, e) => AcceptAutoruns(), false);
            lstAuto.SelectionChanged += (s, e) =>
            {
                var it = lstAuto.SelectedItem;
                var a = it != null ? it.Tag as AutoItem : null;
                btnAutoToggle.Text = a != null && a.Disabled ? "Увімкнути" : "Вимкнути";
            };

            // ── Батарея (лише ноутбук) ──
            if (hasBattery)
            {
                p = secPages[4] = Section(270);
                p.Cards.Add(Ui.R(24, 160, 552, 104));
                barHealth = new MeterBar();
                barHealth.Bounds = Ui.R(44, 168, 512, 42);
                p.Controls.Add(barHealth);
                barCharge = new MeterBar();
                barCharge.Bounds = Ui.R(44, 214, 512, 42);
                p.Controls.Add(barCharge);
                p.Cards.Add(Ui.R(24, 0, 552, 88));
                bMax = Tile(p, 0, 4, 80, "Макс. ємність");
                bCycles = Tile(p, 1, 4, 80, "Цикли заряджання");
                bCharge = Tile(p, 2, 4, 80, "Заряд");
                bRate = Tile(p, 3, 4, 80, "Зараз");
                lblHealth = Note(p, "", Ui.R(28, 96, 544, 60));
            }

            // ── Накопичувачі ──
            p = secPages[5] = Section(380);
            Note(p, "Стан SSD і жорстких дисків за даними самих дисків (SMART): ресурс, температура, години роботи й помилки.", Ui.R(28, 0, 544, 40));
            lstDisks = SecList(p, Ui.R(24, 46, 552, 330), "Не вдалося прочитати стан дисків");
            lstDisks.EmptyIcon = "disk";

            secTimer = new System.Windows.Forms.Timer();
            secTimer.Interval = 10000;
            secTimer.Tick += (s, e) =>
            {
                if (!Visible) return;
                if (ProtectVisible())
                {
                    if ((DateTime.UtcNow - defLoaded).TotalMinutes >= 2) RefreshDefender();
                    RefreshNet();
                }
                if (SensorsVisible()) RefreshHealth();
            };
            secTimer.Start();
        }

        bool ProtectVisible() { return svcPages != null && (svcPages[0].Visible || svcPages[2].Visible); }

        void OnProtectShown()
        {
            if ((DateTime.UtcNow - defLoaded).TotalSeconds > 30) RefreshDefender();
            RefreshNet();
        }

        // Позначки: помаранчева крапка в боковому меню й «•» на підвкладці «Захист».
        void SetSecAlert(int i, bool on)
        {
            if (secAlert[i] == on) return;
            secAlert[i] = on;
            RefreshNavDots();
            UpdateHome();
        }

        void RefreshNavDots()
        {
            bool protect = secAlert[0] || secAlert[1] || secAlert[2];
            nav.SetDot(5, protect);
            nav.SetDot(2, secAlert[3]);
            nav.SetDot(1, secAlert[4]);
            nav.SetSubDot(5, 0, secAlert[0]);
            nav.SetSubDot(5, 1, secAlert[1]);
            nav.SetSubDot(5, 2, secAlert[2]);
            nav.SetSubDot(2, 1, secAlert[3]);
            if (views != null) nav.SetSubDot(1, sensDisks, secAlert[4]);
        }

        void RunBg(Action work)
        {
            ThreadPool.QueueUserWorkItem(_ => { try { work(); } catch { } });
        }

        void Ui_(Action a)
        {
            try { BeginInvoke(new MethodInvoker(() => { try { a(); } catch { } })); } catch { }
        }

        // ── Захист ──
        void RefreshDefender()
        {
            if (secBusyDef) return;
            secBusyDef = true;
            RunBg(() =>
            {
                var st = DefenderStatus.Read();
                Ui_(() => { secBusyDef = false; defLoaded = DateTime.UtcNow; ShowDefender(st); });
            });
        }

        static string DaysAgo(int d)
        {
            if (d < 0) return "ще не було";
            if (d == 0) return "сьогодні";
            if (d == 1) return "вчора";
            int m = d % 10, h = d % 100;
            string word = m == 1 && h != 11 ? "день" : m >= 2 && m <= 4 && (h < 12 || h > 14) ? "дні" : "днів";
            return d + " " + word + " тому";
        }

        void ShowDefender(DefenderStatus st)
        {
            lastDefender = st;
            bool problem = false;
            kvDefender.Set(0, "Антивірус", st.Antivirus, null);
            if (st.Passive && st.Antivirus != "Microsoft Defender")
            {
                kvDefender.Set(1, "Захист у реальному часі", "забезпечує " + st.Antivirus, Ui.Green);
                kvDefender.Set(2, "Антивірусні бази", "див. у самому антивірусі", Ui.Ink3);
                kvDefender.Set(3, "Остання перевірка ПК", "див. у самому антивірусі", Ui.Ink3);
            }
            else
            {
                if (st.RealTime == true) kvDefender.Set(1, "Захист у реальному часі", "увімкнено", Ui.Green);
                else if (st.RealTime == false) { kvDefender.Set(1, "Захист у реальному часі", "ВИМКНЕНО", Ui.Red); problem = true; }
                else kvDefender.Set(1, "Захист у реальному часі", "невідомо", Ui.Ink3);
                if (st.SigAgeDays < 0) kvDefender.Set(2, "Антивірусні бази", "невідомо", Ui.Ink3);
                else
                {
                    bool old = st.SigAgeDays > 3;
                    kvDefender.Set(2, "Антивірусні бази", "оновлено " + DaysAgo(st.SigAgeDays), old ? Ui.Orange : Ui.Green);
                    problem |= old;
                }
                int scan = st.QuickScanAgeDays >= 0 && st.FullScanAgeDays >= 0 ? Math.Min(st.QuickScanAgeDays, st.FullScanAgeDays)
                    : Math.Max(st.QuickScanAgeDays, st.FullScanAgeDays);
                kvDefender.Set(3, "Остання перевірка ПК", DaysAgo(scan), scan < 0 || scan > 14 ? Ui.Orange : (Color?)null);
            }
            if (st.Firewall == true) kvDefender.Set(4, "Брандмауер Windows", "увімкнено", Ui.Green);
            else if (st.Firewall == false) { kvDefender.Set(4, "Брандмауер Windows", "ВИМКНЕНО", Ui.Red); problem = true; }
            else kvDefender.Set(4, "Брандмауер Windows", "невідомо", Ui.Ink3);
            if (st.RealTime == null && st.Firewall == null)
                heroDef.Set("Не вдалося перевірити захист.", "Відкрий «Безпеку Windows», щоб глянути стан антивірусу.", "warn", Ui.Orange);
            else if (st.RealTime == false || st.Firewall == false)
                heroDef.Set("Захист вимкнено.", "Увімкни захист у «Безпеці Windows» — зараз ПК вразливий.", "warn", Ui.Red);
            else if (problem)
                heroDef.Set("Захист працює, але є зауваження.", "Онови антивірусні бази або запусти перевірку.", "warn", Ui.Orange);
            else
                heroDef.Set("Захист увімкнено.", st.Antivirus + (st.SigAgeDays >= 0 ? " · бази оновлено " + DaysAgo(st.SigAgeDays) : "") + ".", "service", Ui.Green);
            if (!scanning)
                lblDef.Text = problem
                    ? "Захист не в повному порядку. Відкрий «Безпеку Windows», щоб увімкнути захист і оновити бази."
                    : "Захист у порядку. Швидка перевірка займає кілька хвилин — можна працювати як завжди.";
            SetSecAlert(0, problem);
        }

        void QuickScan()
        {
            if (scanning) return;
            string mp = DefenderStatus.MpCmdRun();
            if (mp == null)
            {
                MessageBox.Show("Не знайдено Microsoft Defender. Запусти перевірку у своєму антивірусі.", "Тихий ПК", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            scanning = true;
            btnScan.Enabled = false;
            btnScan.Text = "Перевіряю…";
            lblDef.Text = "Швидка перевірка триває (зазвичай 2–10 хвилин). Можна користуватися ПК як завжди.";
            pgScan.Start("Швидка перевірка", 300, btnScan, btnDefOpen);
            pgScan.Phase("шукаю загрози", 0, 1, 300);
            RunBg(() =>
            {
                string o;
                int code = Tool.Run(mp, "-Scan -ScanType 1", out o, 60 * 60 * 1000);
                Ui_(() =>
                {
                    scanning = false;
                    btnScan.Enabled = true;
                    btnScan.Text = "Швидка перевірка";
                    pgScan.Finish(code == 0, code == 0 ? "Загроз не знайдено" : code == 2 ? "Знайдено загрози" : "Не вдалося перевірити");
                    if (code == 0) lblDef.Text = "Перевірку завершено: загроз не знайдено.";
                    else if (code == 2)
                    {
                        lblDef.Text = "Знайдено загрози! Відкрий «Безпеку Windows» → «Журнал захисту», щоб переглянути й видалити їх.";
                        tray.ShowBalloonTip(8000, "Тихий ПК: знайдено загрози", "Defender знайшов шкідливі файли. Відкрий «Безпеку Windows».", ToolTipIcon.Warning);
                    }
                    else lblDef.Text = "Не вдалося виконати перевірку (код " + code + "). Спробуй через «Безпеку Windows».";
                    RefreshDefender();
                });
            });
        }

        // ── Майнери ──
        void SecurityLoop()
        {
            int i = 0;
            Thread.Sleep(15000);
            while (!stop)
            {
                try
                {
                    List<Finding> f;
                    lock (miner) f = miner.Sample();
                    double idle = miner.IdleSec;
                    Ui_(() => ShowMiners(f, idle, false));
                    if (i % 6 == 0)
                    {
                        var a = Autoruns.Read();
                        Ui_(() => ShowAutoruns(a));
                    }
                }
                catch { }
                i++;
                for (int k = 0; k < 10 && !stop; k++) Thread.Sleep(1000);
            }
        }

        void MinerCheckNow()
        {
            if (minerChecking) return;
            minerChecking = true;
            lblMinerState.Text = "Перевіряю, хто вантажить ПК просто зараз…";
            RunBg(() =>
            {
                List<Finding> f;
                lock (miner) f = miner.CheckNow();
                Ui_(() => { minerChecking = false; ShowMiners(f, 0, true); });
            });
        }

        void ShowMiners(List<Finding> f, double idleSec, bool manual)
        {
            if (minerChecking && !manual) return;
            var items = f.Select(x => new InfoItem
            {
                Title = x.Name + "  · PID " + x.Pid,
                IconPath = x.Path,
                Sub = string.Join(" · ", x.Reasons),
                Right = x.IdleCpu >= 1 ? Math.Round(x.IdleCpu) + "%" : "",
                RightColor = x.Level == 2 ? Ui.Red : x.Level == 1 ? Ui.Orange : (Color?)null,
                Dot = x.Level == 2 ? Ui.Red : x.Level == 1 ? Ui.Orange : (Color?)null,
                Tag = x,
            }).ToList();
            lstMiners.SetItems(items);
            int bad = f.Count(x => x.Level >= 1);
            string state;
            if (manual) state = bad > 0 ? "Перевірка зараз: підозрілих процесів — " + bad + "." : "Перевірка зараз: нічого підозрілого.";
            else if (idleSec >= MinerWatch.IdleAfterSec)
                state = "ПК простоює " + Math.Round(idleSec / 60) + " хв — стежу за навантаженням." + (bad > 0 ? " Підозрілих: " + bad + "." : "");
            else
                state = "Ти зараз користуєшся ПК. Перевірка у простої почнеться, коли 2 хв нічого не натискатимеш." + (bad > 0 ? " Підозрілих: " + bad + "." : "");
            lblMinerState.Text = state;
            if (bad > 0) heroMiner.Set("Підозрілих процесів: " + bad + ".", state, "warn", f.Any(x => x.Level == 2) ? Ui.Red : Ui.Orange);
            else heroMiner.Set("Майнерів не знайдено.", state, "cpu", Ui.Green);
            SetSecAlert(1, bad > 0);
            foreach (var x in f.Where(x => x.Level >= 1))
            {
                if (!notified.Add("miner|" + x.Name)) continue;
                tray.ShowBalloonTip(8000, "Тихий ПК: підозрілий процес", x.Name + ": " + (x.Reasons.FirstOrDefault() ?? "") + ". Глянь вкладку «Безпека» → «Майнери».",
                    x.Level == 2 ? ToolTipIcon.Warning : ToolTipIcon.Info);
            }
        }

        // ── Мережа ──
        void RefreshNet()
        {
            if (secBusyNet) return;
            secBusyNet = true;
            RunBg(() =>
            {
                var apps = new Dictionary<string, NetApp>(StringComparer.OrdinalIgnoreCase);
                foreach (var c in Net.Tcp())
                {
                    if (c.State != 5 || c.Pid <= 4 || Net.IsLocal(c.Remote)) continue;
                    string name;
                    try { using (var p = Process.GetProcessById(c.Pid)) name = p.ProcessName; } catch { continue; }
                    string path = Native.ProcessPath(c.Pid);
                    string key = name + "|" + path;
                    NetApp a;
                    if (!apps.TryGetValue(key, out a)) { a = new NetApp { Pid = c.Pid, Name = name, Path = path }; apps[key] = a; }
                    a.Count++;
                    var ip = c.Remote.IsIPv4MappedToIPv6 ? c.Remote.MapToIPv4() : c.Remote;
                    string ep = (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "[" + ip + "]" : ip.ToString()) + ":" + c.RemotePort;
                    if (a.Remotes.Count < 3 && !a.Remotes.Contains(ep)) a.Remotes.Add(ep);
                    if (MinerWatch.PoolPorts.Contains(c.RemotePort)) a.PoolPort = true;
                }
                foreach (var a in apps.Values) a.Publisher = Sig.Publisher(a.Path);
                var list = apps.Values.OrderByDescending(a => a.PoolPort).ThenByDescending(a => a.Count).ToList();
                Ui_(() => { secBusyNet = false; ShowNet(list); });
            });
        }

        static string Plural(int n, string one, string few, string many)
        {
            int m = n % 10, h = n % 100;
            return m == 1 && h != 11 ? one : m >= 2 && m <= 4 && (h < 12 || h > 14) ? few : many;
        }

        void ShowNet(List<NetApp> list)
        {
            var items = list.Select(a =>
            {
                bool unsigned = a.Publisher == "" || a.Publisher == "!";
                return new InfoItem
                {
                    Title = a.Name,
                    IconPath = a.Path,
                    Sub = string.Join(", ", a.Remotes) + (a.Count > a.Remotes.Count ? " …" : "") + (unsigned && a.Path != null ? " · " + Sig.Describe(a.Publisher) : ""),
                    Right = a.Count + " " + Plural(a.Count, "з'єднання", "з'єднання", "з'єднань"),
                    RightColor = a.PoolPort ? Ui.Red : (Color?)null,
                    Dot = a.PoolPort ? Ui.Red : unsigned && a.Path != null ? Ui.Orange : (Color?)null,
                    Tag = new KeyValuePair<int, string>(a.Pid, a.Path),
                };
            }).ToList();
            lstNet.SetItems(items);
            SetSecAlert(2, list.Any(a => a.PoolPort));
            int unsignedN = list.Count(a => (a.Publisher == "" || a.Publisher == "!") && a.Path != null);
            string n = list.Count + " " + Plural(list.Count, "програма", "програми", "програм") + " в інтернеті";
            if (list.Any(a => a.PoolPort)) heroNet.Set("Є з'єднання з майнінг-пулом!", n + ". Глянь червоні рядки нижче.", "warn", Ui.Red);
            else heroNet.Set("Підозрілих з'єднань немає.", n + (unsignedN > 0 ? ", без цифрового підпису: " + unsignedN : "") + ".", "net", Ui.Green);
        }

        // ── Автозапуск ──
        void ShowAutoruns(List<AutoItem> list)
        {
            autoItems = list;
            var items = list.Select(a =>
            {
                bool unsigned = a.Publisher == "" || a.Publisher == "!";
                bool missing = a.Path != null && !a.Source.StartsWith("startup") && !File.Exists(a.Path) && a.Path.Contains("\\");
                return new InfoItem
                {
                    Title = a.Name,
                    IconPath = a.Path,
                    Sub = a.Where + " · " + (missing ? "файл не знайдено" : Sig.Describe(a.Publisher)),
                    Right = a.Disabled ? "вимкнено" : a.IsNew ? "нове" : "",
                    RightColor = a.Disabled ? Ui.Ink3 : a.IsNew ? Ui.Orange : (Color?)null,
                    Dot = a.Disabled ? (Color?)null : a.IsNew ? Ui.Orange : unsigned && !missing ? Ui.Mix(Ui.Orange, Ui.Card, 0.45f) : (Color?)null,
                    Tag = a.Key,
                };
            }).ToList();
            lstAuto.SetItems(items);
            if (heroAutorun != null)
            {
                int on = list.Count(a => !a.Disabled), news = list.Count(a => a.IsNew);
                string n = on + " " + Plural(on, "програма стартує", "програми стартують", "програм стартують") + " разом з Windows.";
                if (news > 0) heroAutorun.Set("Нові записи в автозапуску: " + news + ".", n + " Перевір помаранчеві рядки нижче.", "apps", Ui.Orange);
                else heroAutorun.Set(n, "Нових записів немає. Чим менше програм в автозапуску, тим швидше вмикається ПК.", "apps", on > 12 ? Ui.Orange : Ui.Green);
            }
            SetSecAlert(3, list.Any(a => a.IsNew));
            foreach (var a in list.Where(a => a.IsNew))
            {
                if (!notified.Add("auto|" + a.Key)) continue;
                tray.ShowBalloonTip(8000, "Тихий ПК: новий автозапуск", "«" + a.Name + "» тепер запускатиметься разом з Windows (" + Sig.Describe(a.Publisher) + ").", ToolTipIcon.Info);
            }
        }

        AutoItem SelectedAuto()
        {
            var it = lstAuto.SelectedItem;
            if (it == null) return null;
            return autoItems.FirstOrDefault(a => a.Key == (string)it.Tag);
        }

        void ReloadAutoruns()
        {
            RunBg(() => { var a = Autoruns.Read(); Ui_(() => ShowAutoruns(a)); });
        }

        void ToggleAutorun()
        {
            var a = SelectedAuto();
            if (a == null) { MessageBox.Show("Спочатку вибери запис у списку.", "Тихий ПК"); return; }
            if (!a.Disabled && MessageBox.Show("Вимкнути автозапуск «" + a.Name + "»?\nПрограма не видаляється, просто не стартуватиме разом з Windows. Увімкнути назад можна тут же.",
                    "Тихий ПК", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            string err = Autoruns.Toggle(a);
            if (err != null) MessageBox.Show("Не вдалося змінити автозапуск:\n" + err, "Тихий ПК", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (a.Name.Equals("TyhyiPK", StringComparison.OrdinalIgnoreCase)) ThreadPool.QueueUserWorkItem(_ => { bool en = Autostart.Enabled(); Ui_(() => tgAutorun.Checked = en); });
            ReloadAutoruns();
        }

        void AcceptAutoruns()
        {
            Autoruns.AcceptAll(autoItems);
            ReloadAutoruns();
        }

        // ── Здоров'я: батарея й диски ──
        void RefreshHealth()
        {
#if LINUXTEST
            return;
#endif
            if (secBusyHealth) return;
            secBusyHealth = true;
            RunBg(() =>
            {
                var bat = hasBattery ? BatteryInfo.Read(true) : null;
                var disks = DiskInfo.Read();
                Ui_(() => { secBusyHealth = false; ShowHealth(bat, disks); });
            });
        }

        void ShowHealth(BatteryInfo b, List<DiskInfo> disks)
        {
            bool alert = false;
            if (bMax != null && b != null)
            {
                if (!b.Present)
                {
                    foreach (var t in new[] { bMax, bCycles, bCharge, bRate }) t.Set("—", Ui.Ink3, "", false);
                    lblHealth.Text = "Драйвер не повідомляє про батарею (можливо, її вийнято).";
                }
                else
                {
                    double h = b.HealthPct;
                    if (double.IsNaN(h)) bMax.Set("—", Ui.Ink3, "немає даних", false);
                    else
                    {
                        double shown = Math.Min(100, Math.Round(h));
                        bMax.Set(shown + "%", h < 60 ? Ui.Red : h < 80 ? Ui.Orange : (Color?)null,
                            Math.Round(b.FullMWh / 1000) + " з " + Math.Round(b.DesignMWh / 1000) + " Вт·год", false);
                        alert |= h < 60;
                    }
                    if (double.IsNaN(b.Cycles)) bCycles.Set("—", Ui.Ink3, "драйвер не рахує", false);
                    else bCycles.Set(Math.Round(b.Cycles).ToString(), null, "повних циклів", false);
                    double c = b.ChargePct;
                    bCharge.Set(double.IsNaN(c) ? "—" : Math.Round(c) + "%", double.IsNaN(c) ? Ui.Ink3 : (Color?)null,
                        b.Charging ? "заряджається" : b.PowerOnline ? "від мережі" : "від батареї", false);
                    if (double.IsNaN(b.RateW)) bRate.Set("—", Ui.Ink3, b.PowerOnline && !b.Charging ? "не заряджається" : "", false);
                    else
                    {
                        double mins = b.Charging || b.PowerOnline ? double.NaN : BatteryForecast.Minutes(b.RemainingMWh);
                        bRate.Set((b.Charging ? "+" : "−") + Watts(b.RateW), null,
                            b.Charging ? "в батарею" : !double.IsNaN(mins) ? "вистачить ≈ " + BatteryForecast.Text(mins) : "з батареї", false);
                    }
                    lblHealth.Text = (b.Name != "" ? b.Name + ". " : "") + b.Verdict();
                    if (barHealth != null)
                    {
                        if (double.IsNaN(h)) barHealth.Set("Максимальна ємність", "невідомо", 0, Ui.Ink3);
                        else barHealth.Set("Максимальна ємність", Math.Min(100, Math.Round(h)) + "%", (float)(h / 100), h < 60 ? Ui.Red : h < 80 ? Ui.Orange : Ui.Green);
                        if (double.IsNaN(c)) barCharge.Set("Заряд", "—", 0, Ui.Ink3);
                        else barCharge.Set("Заряд" + (b.Charging ? " · заряджається" : ""), Math.Round(c) + "%", (float)(c / 100), c <= 15 ? Ui.Red : b.Charging ? Ui.Green : Ui.Blue);
                    }
                    if (heroBat != null)
                    {
                        string sub = (double.IsNaN(h) ? "" : "Максимальна ємність " + Math.Min(100, Math.Round(h)) + "%") +
                            (double.IsNaN(b.Cycles) ? "" : " · циклів: " + Math.Round(b.Cycles)) + (double.IsNaN(c) ? "" : " · заряд " + Math.Round(c) + "%") + ".";
                        if (double.IsNaN(h)) heroBat.Set("Стан батареї невідомий.", "Драйвер не повідомляє ємність.", "battery", Ui.Ink3);
                        else if (h >= 80) heroBat.Set("Батарея в нормі.", sub, "battery", Ui.Green);
                        else if (h >= 60) heroBat.Set("Батарея трохи зношена.", sub, "battery", Ui.Orange);
                        else heroBat.Set("Батарея сильно зношена.", sub, "battery", Ui.Red);
                    }
                }
            }
            var items = disks.Select(d =>
            {
                var parts = new List<string> { d.Kind, d.SizeGb >= 1000 ? (d.SizeGb / 1000).ToString("0.#") + " ТБ" : Math.Round(d.SizeGb) + " ГБ", "стан: " + d.HealthText };
                if (!double.IsNaN(d.Temp)) parts.Add(Math.Round(d.Temp) + "°");
                if (!double.IsNaN(d.Hours)) parts.Add(Math.Round(d.Hours).ToString("N0") + " год роботи");
                if (d.ReadErr > 0 || d.WriteErr > 0) parts.Add("помилок: " + (Math.Max(0, d.ReadErr) + Math.Max(0, d.WriteErr)));
                double left = double.IsNaN(d.Wear) ? double.NaN : Math.Max(0, 100 - d.Wear);
                bool bad = d.Health == 2 || left < 10, warn = d.Health == 1 || left < 30 || (!double.IsNaN(d.Temp) && d.Temp >= 70);
                alert |= bad || warn;
                return new InfoItem
                {
                    Title = d.Name,
                    Sub = string.Join(" · ", parts),
                    Right = double.IsNaN(left) ? "" : "ресурс " + Math.Round(left) + "%",
                    RightColor = bad ? Ui.Red : warn ? Ui.Orange : Ui.Green,
                    Dot = bad ? Ui.Red : warn ? Ui.Orange : Ui.Green,
                };
            }).ToList();
            lstDisks.SetItems(items);
            diskCount = disks.Count;
            if (heroDisks != null)
            {
                double hot = disks.Where(d => !double.IsNaN(d.Temp)).Select(d => d.Temp).DefaultIfEmpty(double.NaN).Max();
                string sub = disks.Count + " " + Plural(disks.Count, "накопичувач", "накопичувачі", "накопичувачів") + (double.IsNaN(hot) ? "" : " · найгарячіший " + Math.Round(hot) + "°") + ".";
                bool bad = items.Any(i => i.Dot == Ui.Red), warn = items.Any(i => i.Dot == Ui.Orange);
                if (disks.Count == 0) heroDisks.Set("Не вдалося прочитати диски.", "Потрібні права адміністратора.", "disk", Ui.Ink3);
                else if (bad) heroDisks.Set("Диск у поганому стані!", sub + " Зроби копію важливих файлів.", "warn", Ui.Red);
                else if (warn) heroDisks.Set("Диск потребує уваги.", sub, "disk", Ui.Orange);
                else heroDisks.Set("Диски в порядку.", sub, "disk", Ui.Green);
            }
            diskState = disks.Count == 0 ? -1 : items.Any(i => i.Dot == Ui.Red) ? 2 : items.Any(i => i.Dot == Ui.Orange) ? 1 : 0;
            SetSecAlert(4, alert);
            UpdateHome();
        }

        // ── Спільне ──
        void ShowSelectedFile(InfoList list)
        {
            var it = list.SelectedItem;
            if (it == null) { MessageBox.Show("Спочатку вибери запис у списку.", "Тихий ПК"); return; }
            string path = null;
            if (it.Tag is Finding) path = ((Finding)it.Tag).Path;
            else if (it.Tag is KeyValuePair<int, string>) path = ((KeyValuePair<int, string>)it.Tag).Value;
            else if (it.Tag is string) { var a = autoItems.FirstOrDefault(x => x.Key == (string)it.Tag); if (a != null) path = a.Source.StartsWith("startup") ? a.Command : a.Path; }
            if (string.IsNullOrEmpty(path) || (!File.Exists(path) && !Directory.Exists(path)))
            {
                MessageBox.Show("Файл не знайдено або до нього немає доступу.", "Тихий ПК");
                return;
            }
            Native.ShowInFolder(path);
        }

        void KillFromList(InfoList list)
        {
            var it = list.SelectedItem;
            if (it == null) { MessageBox.Show("Спочатку вибери запис у списку.", "Тихий ПК"); return; }
            int pid = it.Tag is Finding ? ((Finding)it.Tag).Pid : it.Tag is KeyValuePair<int, string> ? ((KeyValuePair<int, string>)it.Tag).Key : 0;
            Process p;
            try { p = Process.GetProcessById(pid); } catch { MessageBox.Show("Цей процес уже завершився.", "Тихий ПК"); return; }
            using (p)
            {
                string name = p.ProcessName;
                string path = Native.ProcessPath(pid);
                if (Protected.Contains(name) && Sig.IsWindowsFile(path))
                {
                    MessageBox.Show("«" + name + "» — частина Windows, її закривати не варто.", "Тихий ПК", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show("Примусово закрити «" + name + "» (PID " + pid + ")?\nНезбережені дані в цій програмі пропадуть." +
                        (list == lstMiners ? "\n\nЯкщо це справді майнер, він може запуститися знову після перезавантаження — тоді перевір автозапуск і зроби повну перевірку антивірусом." : ""),
                        "Тихий ПК", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                try { p.Kill(); }
                catch (Exception ex) { MessageBox.Show("Не вдалося закрити: " + ex.Message, "Тихий ПК"); return; }
            }
            if (list == lstNet) RefreshNet();
            else lstMiners.SetItems(lstMiners.Items.Where(i => i != it).ToList());
        }
    }
}
