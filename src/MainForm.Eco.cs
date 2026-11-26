// Тихий ПК — режим «Еко»: мінімальне споживання, коли вимкнули світло і ПК працює від батареї чи ДБЖ.
// Вмикається кнопкою «Еко» посередині над сторінкою. Процесор до 50% без прискорення, відеокарта на найменшому
// ліміті, екран тьмяніший і гасне через 3 хвилини, програма сама рідше опитує датчики й не анімує значок.
using System;
using System.Linq;
using System.Management;
using System.Threading;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        HeadButton ecoBtn, aiBtn;
        volatile bool ecoActive;          // «Еко» зараз діє (вручну або через автоматику)
        bool ecoExtrasOn;

        const int EcoBrightness = 40;     // яскравість екрана ноутбука в «Еко», %
        const int EcoScreenOff = 180;     // екран гасне через 3 хвилини

        bool EcoManual { get { return Settings.GetBool("Eco", false); } }

        void BuildEcoButton()
        {
            ecoBtn = new HeadButton("Еко", "leaf", () => Ui.Green,
                "Еко — мінімальне споживання, коли вимкнули світло", "Еко увімкнено — натисни, щоб вимкнути");
            ecoBtn.Bounds = Ui.R(SideW + 200, 20, 92, 32);
            ecoBtn.Click += (s, e) => ToggleEco();
            Controls.Add(ecoBtn);
            ecoBtn.BringToFront();
            aiBtn = new HeadButton("ШІ", "ai", () => Modes.Get("ai").Color,
                "ШІ — для локальних моделей: відеокарта 90% і примусові вентилятори", "ШІ увімкнено — натисни, щоб вимкнути");
            aiBtn.Bounds = Ui.R(SideW + 300, 20, 84, 32);
            aiBtn.Click += (s, e) => ToggleAi();
            Controls.Add(aiBtn);
            aiBtn.BringToFront();
        }

        bool AiManual { get { return Settings.GetBool("AiOn", false); } }
        bool aiDismissed;

        // «ШІ» вручну: відеокарта в режимі «ШІ». Вимикає «Еко» — разом вони не мають сенсу.
        void ToggleAi()
        {
            bool on = !(gpu.Current == "ai" || gpu.WantRaw == "ai");
            Settings.SetBool("AiOn", on);
            aiDismissed = !on && runningAi != null;   // вимкнув, поки працює модель — не вмикати знову до наступного запуску
            if (on) Settings.SetBool("Eco", false);
            foreach (var r in rows) if (r.Suppressed == "ai") r.Suppressed = null;
            EvaluateAuto();
        }

        void ToggleEco()
        {
            bool on = !ecoActive;
            Settings.SetBool("Eco", on);
            if (on) Settings.SetBool("AiOn", false);
            foreach (var r in rows) if (r.Suppressed == "eco") r.Suppressed = null;
            EvaluateAuto();
        }

        bool ecoChecked;

        // «Еко» вмикається лише кнопкою.
        bool WantEco(out string why)
        {
            why = EcoManual ? "manual" : null;
            return EcoManual;
        }

        // Після кожної оцінки автоматики: увімкнути чи прибрати додаткову економію.
        void SyncEco(bool active)
        {
            ecoActive = active;
            if (ecoBtn != null) ecoBtn.On = active;
            if (!ecoChecked)
            {
                ecoChecked = true;
                // Програму закрили під час «Еко» — повернути екран як було.
                if (!active && (Settings.Get("EcoPrevVideo", "") != "" || Settings.Get("EcoPrevBright", "") != "")) EcoExtrasOff();
            }
            if (active == ecoExtrasOn) return;
            ecoExtrasOn = active;
            if (active) EcoExtrasOn(); else EcoExtrasOff();
            UpdateTraySpin();
        }

        void UpdateTraySpin() { try { ApplyTraySettings(); } catch { } }

        // ── Екран: яскравість (лише вбудований екран ноутбука) і час до вимкнення ──
        void EcoExtrasOn()
        {
            new Thread(() =>
            {
                try
                {
                    if (Settings.Get("EcoPrevVideo", "") == "")
                    {
                        var p = Power.GetVideoIdle();
                        if (p != null) Settings.Set("EcoPrevVideo", p.AC + "," + p.DC);
                    }
                    var cur = Power.GetVideoIdle();
                    int ac = cur == null || cur.AC == 0 ? EcoScreenOff : Math.Min(cur.AC, EcoScreenOff);
                    int dc = cur == null || cur.DC == 0 ? EcoScreenOff : Math.Min(cur.DC, EcoScreenOff);
                    Power.SetVideoIdle(ac, dc);
                }
                catch { }
                try
                {
                    int b = GetBrightness();
                    if (b >= 0)
                    {
                        if (Settings.Get("EcoPrevBright", "") == "") Settings.Set("EcoPrevBright", b.ToString());
                        if (b > EcoBrightness) SetBrightness(EcoBrightness);
                    }
                }
                catch { }
            }) { IsBackground = true }.Start();
        }

        void EcoExtrasOff()
        {
            new Thread(EcoRestore) { IsBackground = true }.Start();
        }

        // Повернути екран як було (також при запуску, якщо програму закрили під час «Еко»).
        static void EcoRestore()
        {
            try
            {
                string v = Settings.Get("EcoPrevVideo", "");
                if (v != "")
                {
                    var parts = v.Split(',');
                    Power.SetVideoIdle(int.Parse(parts[0]), int.Parse(parts[1]));
                    Settings.Set("EcoPrevVideo", "");
                }
            }
            catch { }
            try
            {
                string b = Settings.Get("EcoPrevBright", "");
                if (b != "")
                {
                    SetBrightness(int.Parse(b));
                    Settings.Set("EcoPrevBright", "");
                }
            }
            catch { }
        }

        static int GetBrightness()
        {
            using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=TRUE"))
                foreach (ManagementObject o in s.Get())
                    return Convert.ToInt32(o["CurrentBrightness"]);
            return -1;
        }

        static void SetBrightness(int pct)
        {
            using (var s = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=TRUE"))
                foreach (ManagementObject o in s.Get())
                    o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)Math.Max(0, Math.Min(100, pct)) });
        }

        string EcoStatus(bool autoActive, string why)
        {
            if (!autoActive) return "«Еко» увімкнено, але режими вибрано вручну.";
            string w = TotalW(last) > 0 ? " Зараз ≈" + Math.Round(TotalW(last)) + " Вт." : "";
            return "Увімкнено «Еко»: процесор до 50%, відеокарта на мінімумі, екран тьмяніший." + w;
        }
    }
}
