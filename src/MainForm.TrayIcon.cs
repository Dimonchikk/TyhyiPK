// Тихий ПК — розділ «Значок»: анімований вентилятор біля годинника, його колір, швидкість і режим «температура числом».
using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        Page pageIcon;
        FanPreview fanPreview;
        Segmented segTrayShow, segTrayAnim;
        Swatches swTray;
        Toggle tgTrayBlink;
        Label lblTrayState;
        readonly TrayIcons trayIcons = new TrayIcons();
        System.Windows.Forms.Timer fanTimer;
        int fanFrame;
        double trayTemp = double.NaN;
        int trayLoad;
        bool trayHotNow, blinkPhase;
        DateTime blinkAt;
        static readonly string[] TrayShowKeys = { "fan", "temp" };
        static readonly string[] TrayAnimKeys = { "off", "on", "load" };

        string TrayShow { get { return Settings.Get("TrayShow", "fan"); } }
        string TrayAnim { get { return Settings.Get("TrayAnim", "load"); } }
        string TrayColorKey { get { return Settings.Get("TrayColor", "blue"); } }

        Label IconRow(string text, int y)
        {
            var l = Ui.MakeLabel(pageIcon, text, Ui.R(40, y, 260, 56), Ui.Body, Ui.CInk, Ui.CCard);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        void BuildIconPage()
        {
            var p = pageIcon;
            // Великий попередній перегляд.
            p.Cards.Add(Ui.R(24, 4, 552, 196));
            fanPreview = new FanPreview();
            fanPreview.Bounds = Ui.R(44, 22, 160, 160);
            fanPreview.ColorOf = TrayColorNow;
            fanPreview.ShowTemp = () => TrayShow == "temp";
            fanPreview.TempOf = () => trayTemp;
            p.Controls.Add(fanPreview);
            Ui.MakeLabel(p, "Значок біля годинника", Ui.R(228, 36, 330, 30), Ui.Brand, Ui.CInk, Ui.CCard);
            Ui.MakeLabel(p, "Вентилятор крутиться тим швидше, чим більше працює процесор, і червоніє, коли ПК перегрівається. Так видно стан ПК, не відкриваючи вікна.",
                Ui.R(228, 70, 330, 66), Ui.Small, Ui.CInk2, Ui.CCard);
            lblTrayState = Ui.MakeLabel(p, "", Ui.R(228, 140, 330, 40), Ui.Small, Ui.CInk, Ui.CCard);

            p.SetCaption("set", Ui.R(40, 216, 520, 18), "Налаштування");
            int y = 240;
            IconRow("Показувати", y);
            segTrayShow = new Segmented();
            segTrayShow.Items = new[] { "Вентилятор", "Температуру" };
            segTrayShow.Bounds = Ui.R(320, y + 12, 240, 32);
            segTrayShow.Selected = Math.Max(0, Array.IndexOf(TrayShowKeys, TrayShow));
            segTrayShow.ItemClicked += i => { segTrayShow.Selected = i; Settings.Set("TrayShow", TrayShowKeys[i]); ApplyTraySettings(); };
            p.Controls.Add(segTrayShow);
            y += 56;
            p.Lines.Add(Ui.R(40, y, 536, 0));
            IconRow("Обертання", y);
            segTrayAnim = new Segmented();
            segTrayAnim.Items = new[] { "Ні", "Завжди", "Від навантаження" };
            segTrayAnim.FitText = true;
            segTrayAnim.Bounds = Ui.R(240, y + 12, 320, 32);
            segTrayAnim.Selected = Math.Max(0, Array.IndexOf(TrayAnimKeys, TrayAnim));
            segTrayAnim.ItemClicked += i => { segTrayAnim.Selected = i; Settings.Set("TrayAnim", TrayAnimKeys[i]); ApplyTraySettings(); };
            p.Controls.Add(segTrayAnim);
            y += 56;
            p.Lines.Add(Ui.R(40, y, 536, 0));
            IconRow("Колір", y);
            swTray = new Swatches();
            swTray.Bounds = Ui.R(250, y + 8, 310, 40);
            swTray.Selected = Math.Max(0, Array.IndexOf(FanArt.ColorKeys, TrayColorKey));
            swTray.Changed += i => { Settings.Set("TrayColor", FanArt.ColorKeys[i]); ApplyTraySettings(); };
            p.Controls.Add(swTray);
            y += 56;
            p.Lines.Add(Ui.R(40, y, 536, 0));
            IconRow("Блимати червоним при перегріві", y);
            tgTrayBlink = new Toggle();
            tgTrayBlink.Bounds = Ui.R(514, y + 14, 46, 28);
            tgTrayBlink.Checked = Settings.GetBool("TrayBlink", true);
            tgTrayBlink.Changed += (s, e) => { Settings.SetBool("TrayBlink", tgTrayBlink.Checked); ApplyTraySettings(); };
            p.Controls.Add(tgTrayBlink);
            y += 56;
            p.Cards.Add(Ui.R(24, 240, 552, y - 240));
            Ui.MakeLabel(p, "«За температурою» — колір сам змінюється: синій, коли ПК холодний, зелений, жовтий і помаранчевий, коли гріється, і червоний, коли гарячий. " +
                "Режим «Температуру» показує замість вентилятора число — температуру процесора.", Ui.R(28, y + 10, 544, 60), Ui.Small, Ui.CInk2, Ui.CBg);

            fanTimer = new System.Windows.Forms.Timer();
            fanTimer.Tick += (s, e) => FanTick();
            ApplyTraySettings();
        }

        Color TrayColorNow()
        {
            if (trayHotNow && TrayColorKey != "temp") return Color.FromArgb(255, 59, 48);
            return FanArt.Base(TrayColorKey, trayTemp);
        }

        // Інтервал кадру: швидше, коли процесор більше навантажений.
        int FanInterval()
        {
            if (TrayAnim == "on") return 70;
            return Math.Max(35, 230 - (int)(trayLoad * 1.9));
        }

        void ApplyTraySettings()
        {
            if (fanTimer == null || tray == null) return;
            bool animate = TrayShow == "fan" && TrayAnim != "off";
            bool blink = trayHotNow && tgTrayBlink.Checked;
            if (animate) fanTimer.Interval = FanInterval();
            else if (blink) fanTimer.Interval = 500;
            fanTimer.Enabled = animate || blink || (Visible && pageIcon.Visible);
            if (!fanTimer.Enabled || !animate) RenderTray();
            UpdateTrayState();
            fanPreview.Invalidate();
        }

        void FanTick()
        {
            bool animate = TrayShow == "fan" && TrayAnim != "off";
            if (animate) fanFrame++;
            if (trayHotNow && tgTrayBlink.Checked && (DateTime.UtcNow - blinkAt).TotalMilliseconds >= 500)
            {
                blinkPhase = !blinkPhase;
                blinkAt = DateTime.UtcNow;
                if (!animate) RenderTray();
            }
            if (animate) RenderTray();
            if (Visible && pageIcon.Visible)
            {
                fanPreview.Angle = fanFrame * 90f / TrayIcons.Frames;
                fanPreview.Invalidate();
            }
        }

        void RenderTray()
        {
            try
            {
                var c = TrayColorNow();
                if (trayHotNow && tgTrayBlink.Checked && blinkPhase) c = Ui.Mix(c, Color.White, 0.55f);
                tray.Icon = TrayShow == "temp" ? trayIcons.Temp(c, trayTemp) : trayIcons.Fan(c, fanFrame);
            }
            catch { }
        }

        void UpdateTrayState()
        {
            if (lblTrayState == null) return;
            string what;
            if (TrayShow == "temp") what = "Зараз показує " + (double.IsNaN(trayTemp) ? "—" : Math.Round(trayTemp) + "°") + ".";
            else if (TrayAnim == "off") what = "Зараз: не крутиться.";
            else if (TrayAnim == "on") what = "Зараз: крутиться завжди однаково.";
            else what = "Зараз: процесор " + trayLoad + "% — " + (trayLoad < 15 ? "крутиться повільно." : trayLoad < 60 ? "крутиться помірно." : "крутиться швидко.");
            if (trayHotNow) what += " Перегрів!";
            lblTrayState.Text = what;
            lblTrayState.ForeColor = trayHotNow ? Ui.Red : Ui.Ink;
        }

        // Викликається з кожним новим заміром датчиків.
        void UpdateTrayIcon(Snapshot s)
        {
            if (tray == null || fanTimer == null) return;
            int limit = HeatLimits[Math.Max(0, segHeat.Selected)];
            double ct = s.CpuTempFromBoard ? double.NaN : s.CpuTemp;
            double t = Math.Max(double.IsNaN(ct) ? 0 : ct, double.IsNaN(s.GpuTemp) ? 0 : s.GpuTemp);
            trayTemp = t > 0 ? t : double.NaN;
            if (s.CpuLoad >= 0) trayLoad = s.CpuLoad;
            bool hot = rows.Any(r => r.Heat) || t >= limit;
            bool changed = hot != trayHotNow;
            trayHotNow = hot;
            if (changed || TrayShow == "temp" || TrayColorKey == "temp" || TrayAnim == "load") ApplyTraySettings();
            else UpdateTrayState();
        }
    }
}
