// Тихий ПК — вкладка «Сервіс»: очищення, інтернет, збої, перевірка Windows, поради, звіт.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        Page pageService;
        Segmented svcTabs;
        Page[] svcPages;
        static readonly string[] SvcTitles = { "Антивірус", "Майнери", "Мережа", "Очищення", "Інтернет", "Windows", "Збої", "Звіт" };
        HeroCard heroCrash;

        // Очищення
        InfoList lstClean;
        Label lblCleanTotal, lblCleanResult;
        PillBtn btnClean;
        List<CleanTarget> cleanTargets = new List<CleanTarget>();
        bool cleanBusy;
        DateTime cleanMeasured = DateTime.MinValue;

        // Інтернет
        KeyValueList kvSpeed, kvVpn;
        PillBtn btnSpeed, btnVpn;
        Label lblNet, lblSleep;
        Toggle tgSleep;
        bool speedBusy, vpnBusy;
        System.Windows.Forms.Timer sleepTimer;

        // Збої
        InfoList lstCrash;
        Label lblCrash;
        bool crashBusy;
        DateTime crashLoaded = DateTime.MinValue;
        int bsodCount = -1, appCrashCount = -1;

        // Windows
        KeyValueList kvWin;
        PillBtn btnWinCheck, btnWinFix;
        Label lblWin;
        bool winBusy;

        // Поради
        Label lblDust, lblGames, lblTest;
        InfoList lstGames;
        PillBtn btnTest;
        volatile bool perfTest;
        Thermal.Verdict dust;

        // Звіт
        ImageBox picReport;
        StatTile dDays, dIdle, dLoad, dGpu, tTemp, tPower, tSpeed, tWhen;
        HeroCard heroSens, heroBat, heroDisks, heroDust, heroTest, heroProcs, heroAutorun, heroRules, heroGames, heroEnergy, heroClean, heroSpeed, heroWin, heroReport;
        Label lblReport;
        PillBtn btnReport;
        string reportPath;
        bool reportBusy;

        string runningGame;
        DateTime lastStatT = DateTime.MinValue;

        Page dustSec, testSec, gamesSec;

        // Підрозділ «Сервісу» — окрема сторінка (вибирається в боковому меню).
        Page SvcPage() { return NewPage(); }

        void BuildService()
        {
            svcTabs = new Segmented();
            svcTabs.OnBg = true;
            svcTabs.Items = SvcTitles.ToArray();
            svcTabs.Bounds = Ui.R(24, 4, 552, 32);
            svcTabs.Selected = 0;
            svcTabs.ItemClicked += ShowSvcPage;   // перемикач не показується: підрозділи вибираються збоку
            svcPages = new Page[SvcTitles.Length];
            // Антивірус, майнери й мережа — окремі сторінки з розділів «Захисту».
            svcPages[0] = View(secPages[0]);
            svcPages[1] = View(secPages[1]);
            svcPages[2] = View(secPages[2]);
            for (int i = 3; i < svcPages.Length; i++) svcPages[i] = SvcPage();
            int y;

            // ── Очищення ──
            var p = svcPages[3];
            Note(p, "Прибирає лише те, що безпечно видаляти: тимчасові файли, кеш і непотрібні звіти. Особисті файли, паролі, історія й вкладки браузера не зачіпаються.",
                Ui.R(28, 4, 544, 40));
            lstClean = SecList(p, Ui.R(24, 50, 552, 246), "Рахую, скільки можна звільнити…");
            lstClean.CheckChanged += (s, e) => UpdateCleanTotal();
            lblCleanTotal = Ui.MakeLabel(p, "", Ui.R(28, 306, 544, 24), Ui.BodySemi, Ui.CInk, Ui.CBg);
            btnClean = SecBtn(p, "Очистити вибране", Ui.R(24, 336, 190, 36), (s, e) => CleanNow(), true);
            btnCleanRecount = SecBtn(p, "Порахувати ще раз", Ui.R(222, 336, 180, 36), (s, e) => MeasureClean(), false);
            pgClean = AddStrip(p, Ui.R(24, 334, 552, 40));
            lblCleanResult = Note(p, "Порада: закрий браузери перед очищенням — тоді звільниться більше.", Ui.R(28, 382, 544, 48));

            // ── Інтернет ──
            p = svcPages[4];
            p.Cards.Add(Ui.R(24, 4, 552, 180));
            kvSpeed = new KeyValueList();
            kvSpeed.Bounds = Ui.R(32, 6, 536, 176);
            kvSpeed.Set(0, "Пінг", "—", Ui.Ink3);
            kvSpeed.Set(1, "Стабільність", "—", Ui.Ink3);
            kvSpeed.Set(2, "Завантаження", "—", Ui.Ink3);
            kvSpeed.Set(3, "Відвантаження", "—", Ui.Ink3);
            p.Controls.Add(kvSpeed);
            btnSpeed = SecBtn(p, "Перевірити швидкість", Ui.R(24, 194, 210, 36), (s, e) => SpeedTest(), true);
            btnVpn = SecBtn(p, "Перевірити VPN", Ui.R(242, 194, 170, 36), (s, e) => VpnCheck(), false);
            pgSpeed = AddStrip(p, Ui.R(24, 192, 552, 40));
            p.Cards.Add(Ui.R(24, 242, 552, 92));
            kvVpn = new KeyValueList();
            kvVpn.Bounds = Ui.R(32, 244, 536, 88);
            kvVpn.Set(0, "VPN", "—", Ui.Ink3);
            kvVpn.Set(1, "Сайти бачать тебе з", "—", Ui.Ink3);
            p.Controls.Add(kvVpn);
            lblNet = Note(p, "Швидкість міряється через сервери Cloudflare (близько 35 секунд). Пінг — затримка до 1.1.1.1: для ігор добре до 40 мс.",
                Ui.R(28, 342, 544, 44));

            // «Не засинати під час завантажень» — перемикач на сторінці «Автоматика».
            tgSleep.Checked = Settings.GetBool("SmartSleep", true);
            SleepGuard.Enabled = tgSleep.Checked;
            tgSleep.Changed += (s, e) => { SleepGuard.Enabled = tgSleep.Checked; Settings.SetBool("SmartSleep", tgSleep.Checked); UpdateSleepLabel(); };
            SleepGuard.Start(() => stop);
            sleepTimer = new System.Windows.Forms.Timer();
            sleepTimer.Interval = 5000;
            sleepTimer.Tick += (s, e) => { if (Visible && pageAuto.Visible) UpdateSleepLabel(); };
            sleepTimer.Start();
            UpdateSleepLabel();

            // ── Windows: цілісність системи ──
            p = svcPages[5];
            Note(p, "Перевіряє, чи не пошкоджені системні файли Windows (вбудовані засоби DISM і SFC). Пошкоджені файли бувають причиною вильотів і синіх екранів. " +
                "Перевірка триває 5–15 хвилин, виправлення — до 30 хвилин і потребує інтернету.", Ui.R(28, 4, 544, 60));
            p.Cards.Add(Ui.R(24, 68, 552, 136));
            kvWin = new KeyValueList();
            kvWin.Bounds = Ui.R(32, 70, 536, 132);
            kvWin.Set(0, "Сховище компонентів", "не перевірялось", Ui.Ink3);
            kvWin.Set(1, "Системні файли", "не перевірялись", Ui.Ink3);
            kvWin.Set(2, "Остання перевірка", Settings.Get("WinCheckAt", "ще не було"), Ui.Ink3);
            p.Controls.Add(kvWin);
            btnWinCheck = SecBtn(p, "Перевірити", Ui.R(24, 214, 150, 36), (s, e) => WinCheck(false), true);
            btnWinFix = SecBtn(p, "Виправити", Ui.R(182, 214, 150, 36), (s, e) => WinCheck(true), false);
            pgWin = AddStrip(p, Ui.R(24, 212, 552, 40));
            lblWin = Note(p, "", Ui.R(28, 258, 544, 60));

            // ── Збої й сині екрани ──
            var crash = Section(452);
            heroCrash = Hero(crash, "Читаю журнали Windows…", "Сині екрани за 90 днів і вильоти програм за 30 днів.", "check");
            lstCrash = SecList(crash, Ui.R(24, 84, 552, 278), "Збоїв не знайдено — усе стабільно");
            lstCrash.Icons = true;
            lstCrash.EmptyGood = true;
            lstCrash.SelectionChanged += (s, e) => ShowCrashDetail();
            lblCrash = Note(crash, "", Ui.R(28, 368, 544, 40));
            SecBtn(crash, "Оновити", Ui.R(24, 412, 130, 36), (s, e) => LoadCrashes(true), false);
            y = 8;
            Place(svcPages[6], crash, ref y, null);

            // ── Пил і охолодження (Датчики) ──
            p = dustSec = Section(268);
            p.Cards.Add(Ui.R(24, 0, 552, 88));
            dDays = Tile(p, 0, 4, 80, "Зібрано даних");
            dIdle = Tile(p, 1, 4, 80, "У простої");
            dLoad = Tile(p, 2, 4, 80, "Під навантаженням");
            dGpu = Tile(p, 3, 4, 80, "Карта в іграх");
            p.Cards.Add(Ui.R(24, 100, 552, 96));
            lblDust = Ui.MakeLabel(p, "", Ui.R(40, 108, 520, 82), Ui.Body, Ui.CInk, Ui.CCard);
            Note(p, "Програма запам'ятовує, яка температура буває при однаковому навантаженні. Якщо вона з тижнями росте — у радіаторах накопичився пил.",
                Ui.R(28, 204, 544, 50));

            // ── Тест режимів (Датчики) ──
            p = testSec = Section(256);
            p.Cards.Add(Ui.R(24, 154, 552, 88));
            tTemp = Tile(p, 0, 158, 80, "Температура");
            tPower = Tile(p, 1, 158, 80, "Споживання");
            tSpeed = Tile(p, 2, 158, 80, "Швидкість");
            tWhen = Tile(p, 3, 158, 80, "Коли");
            p.Cards.Add(Ui.R(24, 0, 552, 96));
            lblTest = Ui.MakeLabel(p, "Процесор 2 рази по хвилині працює на повну: у «Звичайному» і «Тихому» режимі. Програма порівняє температуру, споживання й швидкість.",
                Ui.R(40, 8, 360, 82), Ui.Body, Ui.CInk, Ui.CCard);
            lblTestNote = Note(p, "Під час тесту вентилятори можуть зашуміти — так і має бути. Краще закрити ігри й важкі програми.", Ui.R(28, 104, 544, 40));
            pgTest = AddStrip(p, Ui.R(24, 104, 552, 40));
            btnTest = new PillBtn();
            btnTest.Text = "Почати тест";
            btnTest.Bounds = Ui.R(412, 30, 148, 36);
            btnTest.FillColor = () => Ui.Blue;
            btnTest.Fore = () => Color.White;
            btnTest.Click += (s, e) => StartPerfTest();
            p.Controls.Add(btnTest);

            // ── Ігри (сторінка «Автоматика») ──
            p = gamesSec = Section(420);
            lstGames = SecList(p, Ui.R(24, 0, 552, 290), "Ще не було ігор — пограй, і тут з'явиться статистика");
            lstGames.SelectionChanged += (s, e) => ShowGameAdvice();
            lstGames.EmptyIcon = "game";
            lstGames.Icons = true;
            lblGames = Note(p, "", Ui.R(28, 296, 544, 56));
            SecBtn(p, "Для цієї гри — «Тихий»", Ui.R(24, 356, 210, 36), (s, e) => SetGameMode("quiet"), false);
            SecBtn(p, "Як для всіх ігор", Ui.R(242, 356, 170, 36), (s, e) => SetGameMode(null), false);

            // ── Звіт ──
            p = svcPages[7];
            Note(p, "Одна картинка з характеристиками ПК, температурами, станом дисків, батареї й захисту, а також збоями. Зручно скинути другові, коли просиш допомоги. " +
                "IP-адреса й ім'я користувача у звіт не потрапляють.", Ui.R(28, 4, 544, 58));
            p.Cards.Add(Ui.R(24, 66, 552, 318));
            picReport = new ImageBox();
            picReport.Bounds = Ui.R(32, 72, 536, 306);
            picReport.EmptyText = "Натисни «Зробити звіт» — він збережеться на Робочий стіл і скопіюється, щоб одразу вставити в чат (Ctrl+V).";
            p.Controls.Add(picReport);
            lblReport = new Label();
            btnReport = SecBtn(p, "Зробити звіт", Ui.R(24, 394, 170, 36), (s, e) => MakeReport(), true);
            SecBtn(p, "Скопіювати", Ui.R(202, 394, 140, 36), (s, e) => CopyReport(), false);
            SecBtn(p, "Показати файл", Ui.R(350, 394, 160, 36), (s, e) => { if (reportPath != null && File.Exists(reportPath)) Native.ShowInFolder(reportPath); }, false);

        }

        void ShowSvcPage(int i) { if (views != null) ShowView(5, i); }

        // Що оновити, коли відкрили підрозділ «Сервісу».
        void OnSvcShown(int i)
        {
            svcTabs.Selected = i;
            if (i == 0 || i == 2) OnProtectShown();
            if (i == 3 && !cleanBusy && (DateTime.UtcNow - cleanMeasured).TotalMinutes > 2) MeasureClean();
            if (i == 6 && (DateTime.UtcNow - crashLoaded).TotalMinutes > 2) LoadCrashes(false);
        }

        // ── Очищення ──
        void MeasureClean()
        {
#if LINUXTEST
            return;
#endif
            if (cleanBusy) return;
            cleanBusy = true;
            lblCleanTotal.Text = "Рахую…";
            var old = cleanTargets.ToDictionary(t => t.Key, t => t.Checked);
            RunBg(() =>
            {
                var list = Cleaner.Targets();
                foreach (var t in list)
                {
                    bool c;
                    if (old.TryGetValue(t.Key, out c)) t.Checked = c;
                    Cleaner.Measure(t);
                }
                Ui_(() => { cleanBusy = false; cleanMeasured = DateTime.UtcNow; ShowClean(list); });
            });
        }

        void ShowClean(List<CleanTarget> list)
        {
            cleanTargets = list;
            lstClean.SetItems(list.Select(t => new InfoItem
            {
                Title = t.Title,
                Sub = t.Sub,
                Right = t.Bytes > 0 ? Cleaner.Size(t.Bytes) : "порожньо",
                RightColor = t.Bytes >= (1L << 30) ? Ui.Orange : t.Bytes > 0 ? (Color?)null : Ui.Ink3,
                Check = t.Checked,
                Tag = t.Key,
            }).ToList());
            UpdateCleanTotal();
        }

        void UpdateCleanTotal()
        {
            long sum = 0;
            foreach (var it in lstClean.Items)
            {
                var t = cleanTargets.FirstOrDefault(x => x.Key == (string)it.Tag);
                if (t == null) continue;
                t.Checked = it.Check == true;
                if (t.Checked) sum += t.Bytes;
            }
            lblCleanTotal.Text = sum > 0 ? "Можна звільнити: " + Cleaner.Size(sum) : "Нічого чистити — вибери пункти зі списку.";
            if (heroClean != null && cleanTargets.Count > 0)
            {
                var big = cleanTargets.OrderByDescending(t => t.Bytes).First();
                long all = cleanTargets.Sum(t => t.Bytes);
                if (all < (50L << 20)) heroClean.Set("Тут чисто.", "Сміття майже немає — менше 50 МБ.", "check", Ui.Green);
                else heroClean.Set("Можна звільнити " + Cleaner.Size(sum > 0 ? sum : all) + ".", "Найбільше займає: " + big.Title.ToLowerInvariant() + " (" + Cleaner.Size(big.Bytes) + ").",
                    "trash", all >= (5L << 30) ? Ui.Orange : Ui.Blue);
            }
        }

        void CleanNow()
        {
            if (cleanBusy) return;
            var sel = cleanTargets.Where(t => t.Checked && t.Bytes > 0).ToList();
            if (sel.Count == 0) { MessageBox.Show("Нічого вибраного для очищення.", "Тихий ПК"); return; }
            if (sel.Any(t => t.Recycle) && MessageBox.Show("Кошик буде очищено назавжди. Продовжити?", "Тихий ПК", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            cleanBusy = true;
            btnClean.Enabled = false;
            btnClean.Text = "Очищую…";
            pgClean.Start("Очищення", 4 + sel.Count * 3, btnClean, btnCleanRecount);
            RunBg(() =>
            {
                long freed = 0;
                for (int i = 0; i < sel.Count; i++)
                {
                    var t = sel[i];
                    double a = 0.9 * i / sel.Count, b = 0.9 * (i + 1) / sel.Count;
                    Ui_(() => pgClean.Phase(t.Title.ToLowerInvariant(), a, b, 4));
                    freed += Cleaner.Clean(t);
                }
                Ui_(() => pgClean.Phase("рахую, що лишилось", 0.9, 1, 4));
                var list = Cleaner.Targets();
                var keep = cleanTargets.ToDictionary(t => t.Key, t => t.Checked);
                foreach (var t in list) { bool c; if (keep.TryGetValue(t.Key, out c)) t.Checked = c; Cleaner.Measure(t); }
                Ui_(() =>
                {
                    cleanBusy = false;
                    btnClean.Enabled = true;
                    btnClean.Text = "Очистити вибране";
                    pgClean.Finish(true, "Звільнено " + (freed > 0 ? Cleaner.Size(freed) : "трохи місця"));
                    cleanMeasured = DateTime.UtcNow;
                    ShowClean(list);
                    if (heroClean != null) heroClean.Set("Готово! Звільнено " + (freed > 0 ? Cleaner.Size(freed) : "трохи місця") + ".", "Диск став трохи вільнішим.", "check", Ui.Green);
                    lblCleanResult.Text = "Готово! Звільнено " + (freed > 0 ? Cleaner.Size(freed) : "трохи місця") + ". " +
                        (list.Where(t => keep.ContainsKey(t.Key) && keep[t.Key]).Sum(t => t.Bytes) > 0
                            ? "Частина файлів зараз зайнята програмами — їх можна прибрати пізніше або після перезавантаження."
                            : "");
                });
            });
        }

        // ── Інтернет ──
        void SpeedTest()
        {
            if (speedBusy) return;
            speedBusy = true;
            btnSpeed.Enabled = false;
            btnSpeed.Text = "Перевіряю…";
            kvSpeed.Set(0, "Пінг", "міряю…", Ui.Ink3);
            kvSpeed.Set(1, "Стабільність", "", Ui.Ink3);
            kvSpeed.Set(2, "Завантаження", "", Ui.Ink3);
            kvSpeed.Set(3, "Відвантаження", "", Ui.Ink3);
            double ss = NetCheck.SpeedSeconds + 1.5;
            pgSpeed.Start("Перевірка швидкості", 4 + 2 * ss, btnSpeed, btnVpn);
            pgSpeed.Phase("пінг", 0, 0.1, 4);
            RunBg(() =>
            {
                var cf = NetCheck.Ping("1.1.1.1", 8);
                var gg = NetCheck.Ping("8.8.8.8", 4);
                var best = !double.IsNaN(cf.Avg) ? cf : gg;
                Ui_(() =>
                {
                    if (double.IsNaN(best.Avg))
                    {
                        kvSpeed.Set(0, "Пінг", "немає відповіді", Ui.Red);
                        kvSpeed.Set(1, "Стабільність", "", Ui.Ink3);
                    }
                    else
                    {
                        double a = best.Avg;
                        kvSpeed.Set(0, "Пінг", Math.Round(a) + " мс" + (a <= 40 ? " · чудово для ігор" : a <= 80 ? " · нормально" : " · високий"),
                            a <= 40 ? Ui.Green : a <= 80 ? (Color?)null : Ui.Orange);
                        int lossPct = (int)Math.Round(100.0 * (cf.Lost + gg.Lost) / Math.Max(1, cf.Sent + gg.Sent));
                        bool bad = lossPct > 2 || best.Jitter > 15;
                        kvSpeed.Set(1, "Стабільність", (lossPct > 0 ? "втрати " + lossPct + "% · " : "без втрат · ") + "розкид " + Math.Round(best.Jitter) + " мс",
                            bad ? Ui.Orange : Ui.Green);
                    }
                    kvSpeed.Set(2, "Завантаження", "міряю…", Ui.Ink3);
                    pgSpeed.Phase("завантаження", 0.1, 0.55, ss);
                });
                double down = NetCheck.Download(v => Ui_(() => kvSpeed.Set(2, "Завантаження", "міряю… " + Mbps(v), Ui.Ink3)));
                Ui_(() =>
                {
                    kvSpeed.Set(2, "Завантаження", double.IsNaN(down) ? "не вдалося" : Mbps(down), double.IsNaN(down) ? Ui.Red : (Color?)null);
                    kvSpeed.Set(3, "Відвантаження", "міряю…", Ui.Ink3);
                    pgSpeed.Phase("відвантаження", 0.55, 1, ss);
                });
                double up = NetCheck.Upload(v => Ui_(() => kvSpeed.Set(3, "Відвантаження", "міряю… " + Mbps(v), Ui.Ink3)));
                Ui_(() =>
                {
                    kvSpeed.Set(3, "Відвантаження", double.IsNaN(up) ? "не вдалося" : Mbps(up), double.IsNaN(up) ? Ui.Red : (Color?)null);
                    speedBusy = false;
                    btnSpeed.Enabled = true;
                    btnSpeed.Text = "Перевірити швидкість";
                    pgSpeed.Finish(!double.IsNaN(down), double.IsNaN(down) ? "Не вдалося виміряти" : "Завантаження " + Mbps(down) + (double.IsNaN(up) ? "" : " · відвантаження " + Mbps(up)));
                    string verdict;
                    if (double.IsNaN(best.Avg) && double.IsNaN(down)) verdict = "Інтернету немає або його блокує брандмауер/антивірус.";
                    else if (!double.IsNaN(down) && down < 10) verdict = "Інтернет повільний: відео в HD може підвисати. Якщо через Wi-Fi — спробуй ближче до роутера або кабель.";
                    else if (!double.IsNaN(best.Avg) && best.Avg > 80) verdict = "Швидкість нормальна, але затримка висока — в онлайн-іграх можуть бути лаги. Кабель замість Wi-Fi зазвичай допомагає.";
                    else verdict = "З інтернетом усе добре. Якщо гра все одно лагає — причина, швидше за все, в самому ПК (глянь температури) або на сервері гри.";
                    lblNet.Text = verdict;
                    if (heroSpeed != null)
                    {
                        string sub = (double.IsNaN(best.Avg) ? "" : "Пінг " + Math.Round(best.Avg) + " мс") + (double.IsNaN(up) ? "" : " · відвантаження " + Mbps(up)) + ".";
                        if (double.IsNaN(down) && double.IsNaN(best.Avg)) heroSpeed.Set("Інтернету немає.", "Перевір Wi-Fi або кабель.", "warn", Ui.Red);
                        else if (double.IsNaN(down)) heroSpeed.Set("Завантаження виміряти не вдалося.", sub, "net", Ui.Orange);
                        else heroSpeed.Set("Завантаження — " + Mbps(down) + ".", sub, "net", down < 10 ? Ui.Orange : Ui.Green);
                    }
                });
            });
        }

        static string Mbps(double v) { return (v >= 100 ? Math.Round(v).ToString() : v.ToString("0.0")) + " Мбіт/с"; }

        void VpnCheck()
        {
            if (vpnBusy) return;
            vpnBusy = true;
            btnVpn.Enabled = false;
            kvVpn.Set(0, "VPN", "перевіряю…", Ui.Ink3);
            kvVpn.Set(1, "Сайти бачать тебе з", "", Ui.Ink3);
            RunBg(() =>
            {
                var v = NetCheck.Vpn();
                Ui_(() =>
                {
                    vpnBusy = false;
                    btnVpn.Enabled = true;
                    bool adapter = v.Adapters.Count > 0 || v.Warp;
                    string home = NetCheck.HomeCountry();
                    kvVpn.Set(0, "VPN", adapter ? "увімкнено · " + (v.Warp ? "Cloudflare WARP" : v.Adapters[0]) : "не знайдено", adapter ? (Color?)null : Ui.Ink2);
                    if (v.Error != null || v.Country == "")
                    {
                        kvVpn.Set(1, "Сайти бачать тебе з", "не вдалося дізнатися", Ui.Orange);
                        lblNet.Text = "Не вдалося зв'язатися з сервером перевірки. Перевір інтернет.";
                        return;
                    }
                    bool away = v.Country != home;
                    kvVpn.Set(1, "Сайти бачать тебе з", NetCheck.CountryName(v.Country) + " · IP " + v.Ip, away ? Ui.Green : (Color?)null);
                    if (adapter && away) lblNet.Text = "VPN працює: сайти бачать тебе з країни «" + NetCheck.CountryName(v.Country) + "», а не з твоєї справжньої адреси.";
                    else if (adapter) lblNet.Text = "VPN-адаптер увімкнено, але сайти все одно бачать тебе з країни «" + NetCheck.CountryName(v.Country) +
                        "». Схоже, VPN не пропускає трафік або працює лише для окремих програм (раздільне тунелювання).";
                    else if (away) lblNet.Text = "Окремого VPN-адаптера не видно, але адреса — з країни «" + NetCheck.CountryName(v.Country) + "». Можливо, працює VPN у браузері або проксі.";
                    else lblNet.Text = "VPN вимкнено — сайти бачать твою справжню адресу. Для звичайного користування це нормально.";
                });
            });
        }

        void UpdateSleepLabel()
        {
            if (lblSleep == null) return;
            if (!tgSleep.Checked) lblSleep.Text = "Вимкнено: ПК засинає за звичайним розкладом Windows.";
            else if (SleepGuard.Holding) lblSleep.Text = "Зараз щось завантажується (" + (SleepGuard.RateBytesPerSec * 8 / 1e6).ToString("0.#") + " Мбіт/с) — ПК не засне, поки не закінчиться.";
            else lblSleep.Text = "Якщо щось качається, ПК не засне, поки завантаження не закінчиться.";
        }

        // ── Збої ──
        void LoadCrashes(bool manual)
        {
#if LINUXTEST
            return;
#endif
            if (crashBusy) return;
            crashBusy = true;
            if (manual) lblCrash.Text = "Читаю журнали Windows…";
            RunBg(() =>
            {
                var bs = SysLogs.BlueScreens(90);
                var apps = SysLogs.AppCrashes(30);
                Ui_(() => { crashBusy = false; crashLoaded = DateTime.UtcNow; ShowCrashes(bs, apps); });
            });
        }

        static string When(DateTime t)
        {
            var d = (DateTime.Today - t.Date).TotalDays;
            string time = t.ToString("HH:mm");
            if (d < 1) return "сьогодні о " + time;
            if (d < 2) return "вчора о " + time;
            return t.ToString("dd.MM") + " о " + time;
        }

        void ShowCrashes(List<BlueScreen> bs, List<CrashGroup> apps)
        {
            var items = new List<InfoItem>();
            foreach (var b in bs)
            {
                string title, what, advice;
                SysLogs.Explain(b, out title, out what, out advice);
                items.Add(new InfoItem { Title = title, Sub = When(b.Time) + " · " + what, Dot = b.PowerLoss ? Ui.Orange : Ui.Red, IconKey = b.PowerLoss ? "energy" : "warn", Tag = b });
            }
            foreach (var g in apps)
            {
                var parts = new List<string> { "востаннє " + When(g.Last) };
                if (g.TopModule != null) parts.Add("модуль: " + SysLogs.ModuleText(g.TopModule));
                if (g.TopCode != null) parts.Add(SysLogs.CodeText(g.TopCode));
                int n = g.Crashes + g.Hangs;
                items.Add(new InfoItem
                {
                    Title = g.App + (g.Crashes == 0 ? " · зависала" : ""),
                    Sub = string.Join(" · ", parts),
                    Right = n + " " + Plural(n, "раз", "рази", "разів"),
                    RightColor = n >= 5 ? Ui.Orange : (Color?)null,
                    Dot = Ui.Mix(Ui.Orange, Ui.Card, 0.35f),
                    Tag = g,
                });
            }
            lstCrash.SetItems(items);
            bsodCount = bs.Count(b => !b.PowerLoss);
            appCrashCount = apps.Sum(g => g.Crashes + g.Hangs);
            int power = bs.Count(b => b.PowerLoss);
            if (bsodCount > 0)
                heroCrash.Set("Синіх екранів: " + bsodCount + ".", "Останній " + When(bs.First(b => !b.PowerLoss).Time) + ". Вибери його в списку — поясню, що сталося.", "warn", Ui.Red);
            else if (appCrashCount > 0 || power > 0)
                heroCrash.Set("Синіх екранів не було.", (appCrashCount > 0 ? "Вильотів і зависань програм: " + appCrashCount : "") +
                    (power > 0 ? (appCrashCount > 0 ? ", раптових вимкнень: " : "Раптових вимкнень: ") + power : "") + ".", "check", Ui.Orange);
            else heroCrash.Set("Система стабільна.", "Ні синіх екранів, ні вильотів програм за останні місяці.", "check", Ui.Green);
            if (lstCrash.SelectedItem == null)
                lblCrash.Text = items.Count == 0 ? "За останні місяці Windows не записала жодного синього екрана чи вильоту програм."
                    : (bsodCount > 0 ? "Синіх екранів: " + bsodCount + ". " : "") + (appCrashCount > 0 ? "Вильотів і зависань програм: " + appCrashCount + ". " : "") + "Вибери запис, щоб побачити пояснення.";
            else ShowCrashDetail();
        }

        void ShowCrashDetail()
        {
            var it = lstCrash.SelectedItem;
            if (it == null) return;
            var b = it.Tag as BlueScreen;
            if (b != null)
            {
                string title, what, advice;
                SysLogs.Explain(b, out title, out what, out advice);
                lblCrash.Text = what + " Що робити: " + advice;
                return;
            }
            var g = it.Tag as CrashGroup;
            if (g != null) lblCrash.Text = SysLogs.CrashAdvice(g);
        }

        // ── Windows ──
        void WinCheck(bool repair)
        {
            if (winBusy) return;
            if (!Program.IsAdmin)
            {
                if (MessageBox.Show("Перевірка Windows потребує прав адміністратора. Перезапустити Тихий ПК з ними?", "Тихий ПК",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Restart(true);
                return;
            }
            if (repair && MessageBox.Show("Виправлення завантажить чисті системні файли з серверів Microsoft і замінить пошкоджені. Це може тривати до 30 хвилин. Почати?",
                    "Тихий ПК", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            winBusy = true;
            btnWinCheck.Enabled = btnWinFix.Enabled = false;
            (repair ? btnWinFix : btnWinCheck).Text = repair ? "Виправляю…" : "Перевіряю…";
            kvWin.Set(0, "Сховище компонентів", "перевіряю…", Ui.Ink3);
            kvWin.Set(1, "Системні файли", "чекає", Ui.Ink3);
            lblWin.Text = repair ? "Крок 1 з 2: відновлення сховища Windows (DISM). Не вимикай ПК." : "Крок 1 з 2: перевірка сховища Windows.";
            double d1 = repair ? 600 : 120, d2 = repair ? 600 : 420;
            pgWin.Start(repair ? "Виправлення Windows" : "Перевірка Windows", d1 + d2, btnWinCheck, btnWinFix);
            pgWin.Phase("крок 1 з 2: сховище компонентів", 0, d1 / (d1 + d2), d1);
            RunBg(() =>
            {
                string t1, t2;
                int r1 = WinHealth.Dism(repair, out t1);
                Ui_(() =>
                {
                    kvWin.Set(0, "Сховище компонентів", r1 == 0 ? "у порядку" : r1 == 1 ? (repair ? "відновлено" : "є проблеми") : r1 == 2 ? "пошкоджено" : "не вдалося",
                        r1 == 0 ? Ui.Green : r1 == 1 ? (repair ? Ui.Green : Ui.Orange) : Ui.Red);
                    kvWin.Set(1, "Системні файли", "перевіряю…", Ui.Ink3);
                    pgWin.Phase("крок 2 з 2: системні файли", d1 / (d1 + d2), 1, d2);
                    lblWin.Text = "Крок 2 з 2: " + (repair ? "перевірка й виправлення" : "перевірка") + " системних файлів (SFC). Це найдовша частина.";
                });
                int r2 = WinHealth.Sfc(repair, out t2);
                Ui_(() =>
                {
                    winBusy = false;
                    btnWinCheck.Enabled = btnWinFix.Enabled = true;
                    pgWin.Finish(r1 >= 0 && r2 >= 0 && (Math.Max(r1, r2) == 0 || repair && Math.Max(r1, r2) == 1),
                        r1 < 0 && r2 < 0 ? "Не вдалося перевірити" : Math.Max(r1, r2) == 0 ? "Windows у порядку" : repair && Math.Max(r1, r2) == 1 ? "Windows відновлено" : "Є проблеми — натисни «Виправити»");
                    btnWinCheck.Text = "Перевірити";
                    btnWinFix.Text = "Виправити";
                    kvWin.Set(1, "Системні файли", r2 == 0 ? "у порядку" : r2 == 1 ? (repair ? "виправлено" : "є пошкоджені") : r2 == 2 ? "не все виправлено" : "не вдалося",
                        r2 == 0 ? Ui.Green : r2 == 1 ? (repair ? Ui.Green : Ui.Orange) : Ui.Red);
                    string at = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
                    Settings.Set("WinCheckAt", at);
                    kvWin.Set(2, "Остання перевірка", at, Ui.Ink3);
                    lblWin.Text = t1 + " " + t2;
                    if (heroWin != null)
                    {
                        int worst = Math.Max(r1, r2);
                        if (r1 < 0 && r2 < 0) heroWin.Set("Перевірку не вдалося виконати.", "Потрібні права адміністратора.", "warn", Ui.Orange);
                        else if (worst == 0) heroWin.Set("Windows у порядку.", "Системні файли цілі · перевірено " + at + ".", "check", Ui.Green);
                        else if (worst == 1 && repair) heroWin.Set("Windows відновлено.", "Перезавантаж ПК, щоб зміни запрацювали.", "check", Ui.Green);
                        else if (worst == 1) heroWin.Set("Знайдено пошкоджені файли.", "Натисни «Виправити» — Windows відновить їх.", "warn", Ui.Orange);
                        else heroWin.Set("Не все вдалося виправити.", "Спробуй «Виправити» ще раз з інтернетом.", "warn", Ui.Red);
                    }
                });
            });
        }

        // ── Поради: пил і ігри ──
        void RefreshAdvice()
        {
            dust = Thermal.Analyze();
            lblDust.Text = dust.Text;
            lblDust.ForeColor = dust.Level == 2 ? Ui.Red : Ui.Ink;
            if (heroDust != null)
            {
                if (dust.Level < 0) heroDust.Set("Збираю дані про охолодження.", "Перша оцінка буде, коли накопичиться 4 тижні спостережень.", "thermo", Ui.Blue);
                else if (dust.Level == 0) heroDust.Set("Охолодження працює як раніше.", "Температура при тому самому навантаженні не росте — пилу не видно.", "check", Ui.Green);
                else if (dust.Level == 1) heroDust.Set("Температура трохи виросла (" + dust.Short.Split(' ')[0] + ").", "Може бути і пил, і тепліша кімната. Стежу далі.", "thermo", Ui.Orange);
                else heroDust.Set("Час почистити ПК від пилу.", "Температура при тому самому навантаженні виросла на " + Math.Round(dust.Rise) + "°.", "warn", Ui.Red);
                int days = dust.DaysCollected;
                dDays.Set(days + " дн.", null, days >= 28 ? "достатньо" : "з 28 потрібних", false);
                SetTempTile(dIdle, Thermal.Recent("idle", 7), "за 7 днів");
                SetTempTile(dLoad, Thermal.Recent("load", 7), "за 7 днів");
                SetTempTile(dGpu, Thermal.Recent("gpu", 7), "за 7 днів");
            }
            var games = GameStats.All();
            lstGames.SetItems(games.Take(30).Select(g =>
            {
                double hot = g.Hottest;
                string own = GameStats.ModeFor(g.Name);
                var parts = new List<string>();
                if (!double.IsNaN(g.CpuAvg)) parts.Add("процесор " + Math.Round(g.CpuAvg) + "° (макс. " + Math.Round(g.CpuMax) + "°)");
                if (!double.IsNaN(g.GpuAvg)) parts.Add("карта " + Math.Round(g.GpuAvg) + "° (макс. " + Math.Round(g.GpuMax) + "°)");
                parts.Add(g.Seconds >= 3600 ? (g.Seconds / 3600).ToString("0.#") + " год" : Math.Round(g.Seconds / 60) + " хв");
                return new InfoItem
                {
                    Title = g.Name,
                    Sub = string.Join(" · ", parts),
                    Right = own != null ? "«" + Modes.Title(own) + "»" : hot > 0 ? Math.Round(hot) + "°" : "",
                    RightColor = own != null ? Modes.Get(own).Color : hot >= 85 ? Ui.Red : hot >= 75 ? Ui.Orange : (Color?)null,
                    Dot = hot >= 85 ? Ui.Red : hot >= 75 ? Ui.Orange : Ui.Green,
                    Tag = g.Name,
                };
            }).ToList());
            ShowGameAdvice();
            if (heroGames != null)
            {
                var g0 = games.FirstOrDefault();
                if (g0 == null) heroGames.Set("Ігор ще не було.", "Пограй — і тут з'явиться, яка гра найбільше гріє ПК.", "game", Ui.Blue);
                else heroGames.Set("Найгарячіша гра — " + g0.Name + ".", "У середньому " + Math.Round(g0.Hottest) + "° · ігор у статистиці: " + games.Count + ".",
                    "game", g0.Hottest >= 85 ? Ui.Red : g0.Hottest >= 75 ? Ui.Orange : Ui.Green);
            }
        }

        static void SetTempTile(StatTile t, double v, string sub)
        {
            if (double.IsNaN(v)) t.Set("—", Ui.Ink3, "мало даних", false);
            else t.Set(Math.Round(v) + "°", Ui.TempColor(v), sub, false);
        }

        // Останній результат тесту «до і після» (зберігається між запусками).
        void ShowTestResult()
        {
            var p = (Settings.Get("PerfTest", "") ?? "").Split('|');
            if (p.Length < 6 || tTemp == null) { foreach (var t in new[] { tTemp, tPower, tSpeed, tWhen }) if (t != null) t.Set("—", Ui.Ink3, "ще не було", false); return; }
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            Func<string, double> D = x => { double v; return double.TryParse(x, System.Globalization.NumberStyles.Float, inv, out v) ? v : double.NaN; };
            double nT = D(p[0]), qT = D(p[1]), nP = D(p[2]), qP = D(p[3]), sp = D(p[4]);
            if (double.IsNaN(nT) || double.IsNaN(qT)) tTemp.Set("—", Ui.Ink3, "немає датчика", false);
            else tTemp.Set(Signed(qT - nT) + "°", Ui.Green, Math.Round(nT) + "° → " + Math.Round(qT) + "°", false);
            if (double.IsNaN(nP) || double.IsNaN(qP)) tPower.Set("—", Ui.Ink3, "немає датчика", false);
            else tPower.Set(Signed(qP - nP) + " Вт", Ui.Green, Math.Round(nP) + " → " + Math.Round(qP) + " Вт", false);
            if (double.IsNaN(sp)) tSpeed.Set("—", Ui.Ink3, "", false);
            else tSpeed.Set(Math.Round(sp) + "%", null, "від звичайної", false);
            tWhen.Set(p[5].Split(' ')[0], null, p[5].Contains(" ") ? p[5].Split(' ')[1] : "", false);
            if (heroTest != null && !double.IsNaN(nT) && !double.IsNaN(qT) && qT - nT > -1.5)
                heroTest.Set("Різниці майже немає.", "Під цим навантаженням «Тихий» і «Звичайний» працюють однаково — процесор не впирається в ліміти.", "gauge", Ui.Blue);
            else if (heroTest != null && !double.IsNaN(nT) && !double.IsNaN(qT))
                heroTest.Set("«Тихий» холодніший на " + Math.Abs(Math.Round(qT - nT)) + "°.", "Ціною " + (double.IsNaN(sp) ? "" : Math.Round(100 - sp) + "% ") + "швидкості під повним навантаженням. В іграх різниця зазвичай менша.", "gauge", Ui.Green);
            else if (heroTest != null)
                heroTest.Set("Тест завершено.", double.IsNaN(sp) ? "" : "«Тихий» дає " + Math.Round(sp) + "% швидкості від звичайної.", "gauge", Ui.Blue);
        }

        void ShowGameAdvice()
        {
            var games = GameStats.All();
            var it = lstGames.SelectedItem;
            GameStat g = it != null ? games.FirstOrDefault(x => x.Name == (string)it.Tag) : games.FirstOrDefault();
            if (g == null) { lblGames.Text = ""; return; }
            double hot = g.Hottest;
            string own = GameStats.ModeFor(g.Name);
            if (own != null) lblGames.Text = "Для «" + g.Name + "» діє власний режим «" + Modes.Title(own) + "».";
            else if (hot >= 80) lblGames.Text = "«" + g.Name + "» гріє ПК найбільше (у середньому " + Math.Round(hot) + "°). Режим «Тихий» для неї зазвичай знижує температуру на 5–15°, а кадрів стає ненабагато менше.";
            else lblGames.Text = "«" + g.Name + "» гріє ПК помірно (" + Math.Round(hot) + "°) — окремий режим не потрібен.";
        }

        void SetGameMode(string mode)
        {
            var it = lstGames.SelectedItem;
            if (it == null) { MessageBox.Show("Спочатку вибери гру у списку.", "Тихий ПК"); return; }
            GameStats.SetMode((string)it.Tag, mode);
            RefreshAdvice();
            EvaluateAuto();
        }

        // ── Тест «до і після» ──
        void StartPerfTest()
        {
            if (perfTest) return;
            if (!cpu.Available) { MessageBox.Show("Режими процесора зараз недоступні.", "Тихий ПК"); return; }
            if (MessageBox.Show("Процесор 2,5 хвилини працюватиме на повну, вентилятори можуть зашуміти. Краще закрити ігри й важкі програми.\n\nПочати тест?",
                    "Тест «до і після»", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            perfTest = true;
            btnTest.Enabled = false;
            btnTest.Text = "Триває…";
            pgTest.Start("Тест режимів", 140, lblTestNote);
            pgTest.Phase("етап 1 з 2: «" + Modes.Title("normal") + "»", 0, 0.5, 70);
            string before = cpu.Current;
            var th = new Thread(() => PerfTestRun(before));
            th.IsBackground = true;
            th.Start();
        }

        class Phase { public string Mode; public double Temp = double.NaN, Power = double.NaN, Speed; }

        void PerfTestRun(string before)
        {
            var phases = new[] { new Phase { Mode = "normal" }, new Phase { Mode = "quiet" } };
            var burn = new CpuBurner();
            string abort = null;
            try
            {
                burn.Start();
                for (int k = 0; k < phases.Length && abort == null; k++)
                {
                    var ph = phases[k];
                    bool ok = false;
                    Invoke(new MethodInvoker(() => { ok = Power.Apply(ph.Mode); if (ok) { cpu.Current = ph.Mode; RefreshRow(cpu); } }));
                    if (!ok) { abort = "не вдалося перемкнути режим (потрібні права адміністратора)"; break; }
                    if (k > 0) { int kk = k; Ui_(() => pgTest.Phase("етап " + (kk + 1) + " з 2: «" + Modes.Title(ph.Mode) + "»", 0.5 * kk, 0.5 * (kk + 1), 70)); }
                    var temps = new List<double>();
                    var watts = new List<double>();
                    long w0 = 0;
                    DateTime t0 = DateTime.MinValue;
                    for (int sec = 0; sec < 70 && !stop; sec++)
                    {
                        Thread.Sleep(1000);
                        var s = last;
                        if (!double.IsNaN(s.CpuTemp) && !s.CpuTempFromBoard && s.CpuTemp >= 97) { abort = "процесор нагрівся до " + Math.Round(s.CpuTemp) + "° — тест зупинено"; break; }
                        if (sec == 30) { w0 = burn.Work; t0 = DateTime.UtcNow; }
                        if (sec >= 30)
                        {
                            if (!double.IsNaN(s.CpuTemp) && !s.CpuTempFromBoard) temps.Add(s.CpuTemp);
                            if (!double.IsNaN(s.CpuPower)) watts.Add(s.CpuPower);
                        }
                        string txt = "Етап " + (k + 1) + " з 2: «" + Modes.Title(ph.Mode) + "» · ще ≈ " + ((phases.Length - k - 1) * 70 + 70 - sec) + " с";
                        Ui_(() => lblTest.Text = txt);
                    }
                    if (t0 != DateTime.MinValue) ph.Speed = (burn.Work - w0) / Math.Max(1, (DateTime.UtcNow - t0).TotalSeconds);
                    if (temps.Count > 0) ph.Temp = temps.Average();
                    if (watts.Count > 0) ph.Power = watts.Average();
                }
            }
            catch (Exception ex) { abort = ex.Message; }
            finally
            {
                burn.Stop();
                try
                {
                    Invoke(new MethodInvoker(() =>
                    {
                        if (Power.Apply(before)) cpu.Current = before;
                        RefreshRow(cpu);
                        perfTest = false;
                        btnTest.Enabled = true;
                        btnTest.Text = "Почати тест";
                        pgTest.Finish(abort == null, abort == null ? "Тест завершено" : "Тест зупинено");
                        EvaluateAuto();
                    }));
                }
                catch { perfTest = false; }
            }
            if (abort == null)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                Settings.Set("PerfTest", string.Join("|", new[] { phases[0].Temp, phases[1].Temp, phases[0].Power, phases[1].Power,
                    phases[0].Speed > 0 ? phases[1].Speed / phases[0].Speed * 100 : double.NaN }.Select(v => v.ToString("0.#", inv))) + "|" + DateTime.Now.ToString("dd.MM HH:mm"));
            }
            Ui_(() =>
            {
                lblTest.Text = abort != null ? "Тест перервано: " + abort + "." : PerfVerdict(phases[0], phases[1]);
                if (abort != null && heroTest != null) heroTest.Set("Тест перервано.", abort, "warn", Ui.Orange);
                else ShowTestResult();
            });
        }

        static string PerfVerdict(Phase n, Phase q)
        {
            var parts = new List<string>();
            if (!double.IsNaN(n.Temp) && !double.IsNaN(q.Temp)) parts.Add("температура " + Math.Round(n.Temp) + "° → " + Math.Round(q.Temp) + "° (" + Signed(q.Temp - n.Temp) + "°)");
            if (!double.IsNaN(n.Power) && !double.IsNaN(q.Power)) parts.Add("споживання " + Math.Round(n.Power) + " → " + Math.Round(q.Power) + " Вт");
            double speed = n.Speed > 0 ? q.Speed / n.Speed * 100 : double.NaN;
            if (!double.IsNaN(speed)) parts.Add("швидкість " + Math.Round(speed) + "% від звичайної");
            if (parts.Count == 0) return "Не вдалося зібрати дані — немає датчиків процесора.";
            string s = "«Тихий» проти «Звичайного»: " + string.Join(", ", parts) + ".";
            if (double.IsNaN(n.Temp)) s += " Температуру не видно — потрібен драйвер датчиків.";
            return s;
        }

        static string Signed(double v) { return (v > 0 ? "+" : v < 0 ? "−" : "") + Math.Abs(Math.Round(v)); }

        // ── Звіт ──
        void MakeReport()
        {
            if (reportBusy) return;
            reportBusy = true;
            btnReport.Enabled = false;
            btnReport.Text = "Збираю…";
            var snap = last;
            RunBg(() =>
            {
                Bitmap bmp = null;
                string err = null;
                try { bmp = BuildReport(snap); }
                catch (Exception ex) { err = ex.Message; }
                string path = null;
                if (bmp != null)
                {
                    try
                    {
                        string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                        path = Path.Combine(desk, "Тихий ПК — звіт " + DateTime.Now.ToString("yyyy-MM-dd HH-mm") + ".png");
                        bmp.Save(path, ImageFormat.Png);
                    }
                    catch (Exception ex) { err = ex.Message; path = null; }
                }
                Ui_(() =>
                {
                    reportBusy = false;
                    btnReport.Enabled = true;
                    btnReport.Text = "Зробити звіт";
                    if (bmp == null)
                    {
                        heroReport.Set("Не вдалося зробити звіт.", err ?? "", "warn", Ui.Orange);
                        return;
                    }
                    var old = picReport.Image;
                    picReport.Image = bmp;
                    if (old != null) old.Dispose();
                    reportPath = path;
                    heroReport.Set("Звіт готовий.", path != null ? "Збережено на Робочий стіл о " + DateTime.Now.ToString("HH:mm") + " і скопійовано — встав у чат (Ctrl+V)."
                        : "Скопійовано — встав у чат (Ctrl+V).", "report", Ui.Green);
                    try { Clipboard.SetImage(bmp); } catch { }
                    tray.ShowBalloonTip(4000, "Тихий ПК", path != null ? "Звіт збережено на Робочий стіл і скопійовано — встав його в чат (Ctrl+V)."
                        : "Звіт скопійовано — встав його в чат (Ctrl+V). Зберегти файл не вдалося: " + err, ToolTipIcon.Info);
                });
            });
        }

        void CopyReport()
        {
            if (picReport.Image == null) { MessageBox.Show("Спочатку зроби звіт.", "Тихий ПК"); return; }
            try { Clipboard.SetImage(picReport.Image); MessageBox.Show("Скопійовано — встав у чат (Ctrl+V).", "Тихий ПК"); } catch { }
        }

        Bitmap BuildReport(Snapshot s)
        {
            var info = SysInfo.Read();
            var secs = new List<ReportSection>();
            var sys = new ReportSection("Система");
            sys.Add("Windows", info.Os);
            sys.Add("Модель", info.Model);
            sys.Add("Процесор", info.Cpu);
            for (int i = 0; i < info.Gpus.Count; i++) sys.Add(i == 0 ? "Відеокарта" : "Відеокарта " + (i + 1), info.Gpus[i]);
            sys.Add("Оперативна пам'ять", info.Ram);
            sys.Add("Материнська плата", info.Board);
            sys.Add("Екран", info.Screen);
            sys.Add("Працює без перезавантаження", info.Uptime);
            secs.Add(sys);

            var now = new ReportSection("Зараз");
            Func<double, Color?> tc = t => t >= 85 ? ReportImage.Bad : t >= 75 ? ReportImage.Warn : (Color?)null;
            if (!double.IsNaN(s.CpuTemp)) now.Add("Температура процесора", Math.Round(s.CpuTemp) + "°" + (s.CpuTempFromBoard ? " (датчик плати)" : ""), tc(s.CpuTemp));
            else now.Add("Температура процесора", "немає датчика", null);
            if (!double.IsNaN(s.GpuTemp)) now.Add("Температура відеокарти", Math.Round(s.GpuTemp) + "°", tc(s.GpuTemp));
            if (s.CpuLoad >= 0) now.Add("Навантаження процесора", s.CpuLoad + "%");
            if (!double.IsNaN(s.CpuPower)) now.Add("Споживання процесора", Math.Round(s.CpuPower) + " Вт");
            if (!double.IsNaN(s.GpuDraw)) now.Add("Споживання відеокарти", Math.Round(s.GpuDraw) + " Вт");
            if (!double.IsNaN(s.Fan)) now.Add("Кулер", Math.Round(s.Fan) + " об/хв");
            if (!double.IsNaN(s.MemTotal) && s.MemTotal > 0) now.Add("Зайнято пам'яті", (s.MemUsed / GB).ToString("0.0") + " з " + Math.Round(s.MemTotal / GB) + " ГБ");
            now.Add("Режим процесора", Modes.Title(cpu.Current));
            if (gpu.Available) now.Add("Режим відеокарти", Modes.Title(gpu.Current));
            var d = Thermal.Analyze();
            if (d.Level >= 0) now.Add("Охолодження", d.Short, d.Level == 2 ? ReportImage.Bad : d.Level == 1 ? ReportImage.Warn : ReportImage.Good);
            secs.Add(now);

            var disks = new ReportSection("Диски");
            foreach (var di in DiskInfo.Read())
            {
                double left = double.IsNaN(di.Wear) ? double.NaN : Math.Max(0, 100 - di.Wear);
                bool bad = di.Health == 2 || left < 10, warn = di.Health == 1 || left < 30;
                string v = di.Kind + " · " + (di.SizeGb >= 1000 ? (di.SizeGb / 1000).ToString("0.#") + " ТБ" : Math.Round(di.SizeGb) + " ГБ") + " · " + di.HealthText +
                    (double.IsNaN(left) ? "" : " · ресурс " + Math.Round(left) + "%") + (double.IsNaN(di.Temp) ? "" : " · " + Math.Round(di.Temp) + "°");
                disks.Add(di.Name, v, bad ? ReportImage.Bad : warn ? ReportImage.Warn : ReportImage.Good);
            }
            secs.Add(disks);

            if (hasBattery)
            {
                var b = BatteryInfo.Read(true);
                var bat = new ReportSection("Батарея");
                if (b.Present)
                {
                    double h = b.HealthPct;
                    if (!double.IsNaN(h)) bat.Add("Максимальна ємність", Math.Min(100, Math.Round(h)) + "% (" + Math.Round(b.FullMWh / 1000) + " з " + Math.Round(b.DesignMWh / 1000) + " Вт·год)",
                        h < 60 ? ReportImage.Bad : h < 80 ? ReportImage.Warn : ReportImage.Good);
                    if (!double.IsNaN(b.Cycles)) bat.Add("Цикли заряджання", Math.Round(b.Cycles).ToString());
                    if (!double.IsNaN(b.ChargePct)) bat.Add("Заряд", Math.Round(b.ChargePct) + "%" + (b.Charging ? " · заряджається" : b.PowerOnline ? " · від мережі" : " · від батареї"));
                    double m = BatteryForecast.Minutes(b.RemainingMWh);
                    if (!b.PowerOnline && !double.IsNaN(m)) bat.Add("Вистачить ще", "≈ " + BatteryForecast.Text(m));
                    bat.Add("Модель", b.Name);
                }
                else bat.Add("Стан", "драйвер не повідомляє про батарею");
                secs.Add(bat);
            }

            var safe = new ReportSection("Захист і стабільність");
            var def = DefenderStatus.Read();
            safe.Add("Антивірус", def.Antivirus);
            if (def.RealTime.HasValue) safe.Add("Захист у реальному часі", def.RealTime.Value ? "увімкнено" : "вимкнено", def.RealTime.Value ? ReportImage.Good : ReportImage.Bad);
            if (def.Firewall.HasValue) safe.Add("Брандмауер", def.Firewall.Value ? "увімкнено" : "вимкнено", def.Firewall.Value ? ReportImage.Good : ReportImage.Bad);
            var bs = SysLogs.BlueScreens(90);
            int nb = bs.Count(x => !x.PowerLoss), np = bs.Count(x => x.PowerLoss);
            safe.Add("Сині екрани за 90 днів", nb == 0 ? "немає" : nb + " (останній " + When(bs.First(x => !x.PowerLoss).Time) + ", " +
                ("0x" + bs.First(x => !x.PowerLoss).Code.ToString("X")) + ")", nb == 0 ? ReportImage.Good : ReportImage.Bad);
            if (np > 0) safe.Add("Раптові вимкнення", np.ToString(), ReportImage.Warn);
            var apps = SysLogs.AppCrashes(30);
            int na = apps.Sum(g => g.Crashes + g.Hangs);
            safe.Add("Вильоти програм за 30 днів", na == 0 ? "немає" : na + " (найчастіше " + apps.OrderByDescending(g => g.Crashes + g.Hangs).First().App + ")",
                na == 0 ? ReportImage.Good : na >= 10 ? ReportImage.Warn : (Color?)null);
            secs.Add(safe);

            return ReportImage.Draw("Звіт про комп'ютер", DateTime.Now.ToString("dd.MM.yyyy HH:mm"), secs);
        }
    }
}
