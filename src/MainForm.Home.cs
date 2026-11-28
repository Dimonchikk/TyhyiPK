// Тихий ПК — головна сторінка: стан ПК одним поглядом, і значок біля годинника, що змінює колір від температури.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        StatTile hSec, hBat, hToday, hCool, hCpuT, hGpuT, hTotalW, hModes;
        DefenderStatus lastDefender;
        int diskState = -1;        // -1 невідомо, 0 добре, 1 увага, 2 погано
        int diskCount;
        double batteryRemainMWh = double.NaN, batteryPct = double.NaN;
        bool batteryOnAc = true, batteryCharging;

        void BuildHome()
        {
            // «Зараз»: температури, споживання й поточні режими — а самі режими перемикаються в «Режими → Керування».
            pageHome.SetCaption("now", Ui.R(40, 88, 300, 18), "Зараз");
            var go = new LinkBtn();
            go.OnBg = true;
            go.Text = "Змінити режим";
            go.Chevron = true;
            go.Bounds = Ui.R(386, 84, 174, 24);
            go.Click += (s, e) => ShowView(SecModes, 0);
            pageHome.Controls.Add(go);
            pageHome.Cards.Add(Ui.R(24, 108, 552, 88));
            hCpuT = Tile(pageHome, 0, 112, 80, "Процесор");
            hGpuT = Tile(pageHome, 1, 112, 80, "Відеокарта");
            hTotalW = Tile(pageHome, 2, 112, 80, "Разом");
            hModes = Tile(pageHome, 3, 112, 80, "Режим");
            hModes.SubClicked += (s, e) => ShowView(SecModes, 0);

            pageHome.SetCaption("home", Ui.R(40, 208, 300, 18), "Стан ПК");
            var report = new LinkBtn();
            report.OnBg = true;
            report.Text = "Зробити звіт про ПК";
            report.Chevron = true;
            report.Bounds = Ui.R(386, 204, 174, 24);
            report.Click += (s, e) => ShowView(SecHome, 1);
            pageHome.Controls.Add(report);
            pageHome.Cards.Add(Ui.R(24, 228, 552, 88));
            hSec = Tile(pageHome, 0, 232, 80, "Безпека");
            hBat = Tile(pageHome, 1, 232, 80, hasBattery ? "Батарея" : "Диски");
            hToday = Tile(pageHome, 2, 232, 80, "Сьогодні");
            hCool = Tile(pageHome, 3, 232, 80, "Охолодження");
            hSec.SubClicked += (s, e) =>
            {
                if (secAlert[1]) ShowView(SecProtect, 1);
                else if (secAlert[2]) ShowView(SecProtect, 2);
                else if (secAlert[3] && !secAlert[0]) ShowView(SecApps, 1);
                else ShowView(SecProtect, 0);
            };
            hBat.SubClicked += (s, e) => ShowView(SecSens, hasBattery ? sensBattery : sensDisks);
            hToday.SubClicked += (s, e) => ShowView(SecEnergy, 0);
            hCool.SubClicked += (s, e) => ShowView(SecSens, sensDust);
            hSec.Set("…", Ui.Ink3, "перевіряю", false);
            hBat.Set("…", Ui.Ink3, "", false);
            hToday.Set("…", Ui.Ink3, "", false);
            UpdateHome();

            // Через 20 с після запуску — тихо перевірити захист і диски, щоб головна сторінка мала дані.
            var once = new System.Windows.Forms.Timer();
            once.Interval = 20000;
            once.Tick += (s, e) =>
            {
                once.Stop();
                once.Dispose();
                RefreshDefender();
                RefreshHealth();
                LoadCrashes(false);
            };
            once.Start();
        }

        void UpdateHome()
        {
            if (hSec == null) return;
            if (hModes != null)
            {
                string cm = cpu.Available ? Modes.Title(cpu.Current) : "—";
                hModes.Set(ecoActive ? "Еко" : cm, ecoActive ? Ui.Green : (Color?)null,
                    gpu.Available ? "карта: " + Modes.Title(gpu.Current) : "процесор", true);
            }
            // Безпека
            bool any = secAlert.Any(a => a);
            bool defBad = lastDefender != null && (lastDefender.RealTime == false || lastDefender.Firewall == false);
            if (lastDefender == null && !any) hSec.Set("…", Ui.Ink3, "перевіряю", false);
            else if (defBad) hSec.Set("Увага", Ui.Red, "захист вимкнено", true);
            else if (secAlert[1] || secAlert[2]) hSec.Set("Увага", Ui.Red, "підозрілий процес", true);
            else if (any) hSec.Set("Глянь", Ui.Orange, secAlert[3] ? "новий автозапуск" : secAlert[4] ? "диски чи батарея" : "деталі", true);
            else hSec.Set("Гаразд", Ui.Green, "деталі", true);

            // Батарея або диски
            if (hasBattery)
            {
                if (double.IsNaN(batteryPct)) hBat.Set("—", Ui.Ink3, "немає даних", false);
                else
                {
                    string sub;
                    if (batteryCharging) sub = "заряджається";
                    else if (batteryOnAc) sub = "від мережі";
                    else
                    {
                        double m = BatteryForecast.Minutes(batteryRemainMWh);
                        sub = double.IsNaN(m) ? "від батареї" : "ще ≈ " + BatteryForecast.Text(m);
                    }
                    hBat.Set(Math.Round(batteryPct) + "%", batteryPct <= 15 && !batteryOnAc ? Ui.Red : (Color?)null, sub, true);
                }
            }
            else
            {
                if (diskState < 0) hBat.Set("…", Ui.Ink3, "", false);
                else hBat.Set(diskState == 0 ? "Гаразд" : diskState == 1 ? "Увага" : "Погано", diskState == 0 ? Ui.Green : diskState == 1 ? Ui.Orange : Ui.Red,
                    diskCount + " " + Plural(diskCount, "диск", "диски", "дисків"), true);
            }

            // Енергія
            double kwh = Energy.Day(DateTime.Today);
            hToday.Set(Energy.Kwh(kwh).Replace(" кВт·год", ""), null, "кВт·год · " + Energy.Money(kwh * Energy.Tariff, false), true);

            // Охолодження
            if (dust == null || (DateTime.UtcNow - dustAt).TotalMinutes > 30) { dust = Thermal.Analyze(); dustAt = DateTime.UtcNow; }
            if (dust.Level < 0) hCool.Set("—", Ui.Ink3, "збираю дані", true);
            else hCool.Set(dust.Level == 0 ? "Добре" : dust.Level == 1 ? "Стежу" : "Почисти", dust.Level == 0 ? Ui.Green : dust.Level == 1 ? Ui.Orange : Ui.Red,
                dust.Level == 0 ? "пилу не видно" : dust.Short, true);

            // Головна картка й пігулка біля заголовка.
            var issues = new List<string>();
            if (defBad) issues.Add("захист Windows вимкнено");
            if (secAlert[1] || secAlert[2]) issues.Add("знайдено підозрілий процес");
            if (secAlert[3]) issues.Add("новий запис в автозапуску");
            if (diskState >= 1) issues.Add("диск потребує уваги");
            if (rows.Any(r => r.Heat)) issues.Add("ПК перегрівається");
            if (dust.Level == 2) issues.Add("час почистити від пилу");
            if (hasBattery && !double.IsNaN(batteryPct) && batteryPct <= 15 && !batteryOnAc) issues.Add("батарея майже сіла");
            bool serious = defBad || secAlert[1] || secAlert[2] || diskState == 2 || rows.Any(r => r.Heat);
            if (issues.Count == 0)
            {
                string sub = lastDefender == null ? "Перевіряю захист і диски…" :
                    "Захист активний." + (diskState == 0 ? " Диски в нормі." : "") + (!double.IsNaN(last.CpuTemp) && last.CpuTemp < 70 ? " Температури спокійні." : "")
                    + (TotalW(last) > 0 ? " Зараз ПК бере ≈" + Math.Round(TotalW(last)) + " Вт." : "");
                if (ecoActive) { hero.Set("Режим «Еко» увімкнено.", "Мінімальне споживання" + (TotalW(last) > 0 ? ": зараз ≈" + Math.Round(TotalW(last)) + " Вт" : "") + ". Вимкнути — кнопка «Еко» вгорі.", "leaf", Ui.Green); pill.Set("Усе гаразд", Ui.Green); return; }
                hero.Set("Усе працює як слід.", sub, "leaf", Ui.Green);
                pill.Set("Усе гаразд", Ui.Green);
            }
            else
            {
                string first = issues[0];
                hero.Set(serious ? "Потрібна увага." : "Є на що глянути.", char.ToUpper(first[0]) + first.Substring(1) + (issues.Count > 1 ? " і ще " + (issues.Count - 1) + "." : "."),
                    "warn", serious ? Ui.Red : Ui.Orange);
                pill.Set(serious ? "Потрібна увага" : "Є на що глянути", serious ? Ui.Red : Ui.Orange);
            }
        }

        // Підрозділи в боковому меню: кожен — окрема сторінка лише зі своїм вмістом.
        Page[][] views;
        string[][] viewSubs;
        int sensBattery = -1, sensDisks, sensDust;

        Page View(Page section)
        {
            var v = NewPage();
            int y = 8;
            Place(v, section, ref y, null);
            return v;
        }

        Page testView, gamesView, autorunView;

        void SetupNavSubs()
        {
            var sensNames = new List<string> { "Показники" };
            var sensViews = new List<Page> { pageSensors };
            if (hasBattery) { sensBattery = sensNames.Count; sensNames.Add("Батарея"); sensViews.Add(View(secPages[4])); }
            sensDisks = sensNames.Count; sensNames.Add("Диски"); sensViews.Add(View(secPages[5]));
            sensDust = sensNames.Count; sensNames.Add("Охолодження"); sensViews.Add(View(dustSec));
            testView = View(testSec);
            autorunView = View(secPages[3]);
            gamesView = View(gamesSec);

            // Розділ → підрозділи (як у боковому меню): назви, сторінки й підписи під заголовком.
            nav.Subs = new[]
            {
                new[] { "Зведення", "Звіт про ПК" },
                new[] { "Керування", "Тест режимів" },
                sensNames.ToArray(),
                new[] { "Запущені", "Автозапуск", "Ігри" },
                null,
                null,
                new[] { "Антивірус", "Майнери", "Мережа" },
                new[] { "Очищення", "Windows", "Збої", "Інтернет" },
                new[] { "Загальні", "Значок" },
            };
            views = new[]
            {
                new[] { pageHome, svcPages[7] },
                new[] { pageModes, testView },
                sensViews.ToArray(),
                new[] { pageProcs, autorunView, gamesView },
                new[] { pageAuto },
                new[] { pageEnergy },
                new[] { svcPages[0], svcPages[1], svcPages[2] },
                new[] { svcPages[3], svcPages[5], svcPages[6], svcPages[4] },
                new[] { pageGeneral, pageIcon },
            };
            var sensSubs = new List<string> { "Температури, навантаження й споживання наживо." };
            if (hasBattery) sensSubs.Add("Знос і заряд батареї — як «Стан батареї» в iPhone.");
            sensSubs.AddRange(new[] { "Стан SSD і жорстких дисків.", "Чи не час почистити ПК від пилу." });
            viewSubs = new[]
            {
                new[] { "Твій комп'ютер з першого погляду.", "Одна картинка про ПК, щоб попросити допомоги." },
                new[] { "Тихо чи швидко працюють процесор і відеокарта.", "Що дає «Тихий» режим саме на твоєму ПК." },
                sensSubs.ToArray(),
                new[] { "Що працює зараз.", "Що стартує разом з Windows.", "Яка гра найбільше гріє ПК." },
                null,
                null,
                new[] { "Антивірус і брандмауер Windows.", "Хто вантажить ПК, коли ти ним не користуєшся.", "Які програми зараз в інтернеті." },
                new[] { "Звільни місце на диску.", "Цілісність системних файлів.", "Сині екрани й вильоти програм.", "Швидкість, пінг і VPN." },
                new[] { "Запуск, оновлення й дані програми. Тема — перемикач угорі праворуч.", "Живий вентилятор біля годинника." },
            };
            nav.SubClicked += GoSub;
            RefreshNavDots();

            // Велика картка-висновок угорі кожної сторінки.
            heroSens = AddHero(pageSensors, "Читаю датчики…", "Температури з'являться за кілька секунд.", "thermo");
            if (hasBattery) heroBat = AddHero(views[SecSens][sensBattery], "Перевіряю батарею…", "", "battery");
            heroDisks = AddHero(views[SecSens][sensDisks], "Перевіряю диски…", "", "disk");
            heroDust = AddHero(views[SecSens][sensDust], "Збираю дані про охолодження.", "", "thermo");
            heroTest = AddHero(testView, "Тест ще не проводився.", "Дізнайся, наскільки «Тихий» режим холодніший на твоєму ПК.", "gauge");
            heroProcs = AddHero(pageProcs, "Програми, що працюють зараз.", "", "apps");
            heroAutorun = AddHero(autorunView, "Автозапуск.", "Читаю список…", "apps");
            heroRules = AddHero(pageAuto, "Автоматика.", "", "auto");
            heroGames = AddHero(gamesView, "Ігор ще не було.", "Пограй — і тут з'явиться, яка гра найбільше гріє ПК.", "game");
            heroEnergy = AddHero(pageEnergy, "Енергія.", "", "energy");
            heroClean = AddHero(svcPages[3], "Рахую, скільки можна звільнити…", "", "trash");
            heroSpeed = AddHero(svcPages[4], "Швидкість ще не перевіряли.", "Натисни «Перевірити швидкість» — це займе близько 35 секунд.", "net");
            string at = Settings.Get("WinCheckAt", null);
            heroWin = AddHero(svcPages[5], at == null ? "Windows ще не перевіряли." : "Остання перевірка — " + at + ".",
                "Перевірка шукає пошкоджені системні файли й лагодить їх.", "overview");
            if (at == null) heroWin.Set("Windows ще не перевіряли.", "Перевірка шукає пошкоджені системні файли й лагодить їх.", "overview", Ui.Blue);
            heroReport = AddHero(svcPages[7], "Звіт ще не зроблено.", "Одна картинка про твій ПК — зручно скинути, коли просиш допомоги.", "report");
            heroReport.Set("Звіт ще не зроблено.", "Одна картинка про твій ПК — зручно скинути, коли просиш допомоги.", "report", Ui.Blue);
            heroTest.Set("Тест ще не проводився.", "Дізнайся, наскільки «Тихий» режим холодніший на твоєму ПК.", "gauge", Ui.Blue);
            heroSpeed.Set("Швидкість ще не перевіряли.", "Натисни «Перевірити швидкість» — це займе близько 35 секунд.", "net", Ui.Blue);
            ShowTestResult();
        }

        // Зсуває вміст сторінки вниз і ставить угорі велику картку-висновок.
        HeroCard AddHero(Page p, string title, string sub, string icon)
        {
            int dy = Ui.S(84);
            foreach (Control c in p.Controls) c.Top += dy;
            for (int i = 0; i < p.Cards.Count; i++) { var r = p.Cards[i]; r.Offset(0, dy); p.Cards[i] = r; }
            for (int i = 0; i < p.Lines.Count; i++) { var r = p.Lines[i]; r.Offset(0, dy); p.Lines[i] = r; }
            foreach (var k in p.Captions.Keys.ToList())
            {
                var kv = p.Captions[k];
                var r = kv.Key;
                r.Offset(0, dy);
                p.Captions[k] = new KeyValuePair<Rectangle, string>(r, kv.Value);
            }
            var h = new HeroCard();
            h.Bounds = Ui.R(24, 4, 552, 72);
            h.Set(title, sub, icon, Ui.Green);
            p.Controls.Add(h);
            return h;
        }

        void GoSub(int page, int sub) { ShowView(page, sub); }

        DateTime dustAt = DateTime.MinValue;

    }
}
