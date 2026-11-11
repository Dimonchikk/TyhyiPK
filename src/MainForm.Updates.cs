// Тихий ПК — оновлення й підсумок тижня.
// • Раз на добу питає GitHub про нову версію; якщо є — сповіщення, а синя кнопка внизу бокового меню
//   стає «Оновити до …». Інакше ця кнопка — «Шукати оновлення» (перевірити зараз).
// • У неділю ввечері (або при першому запуску після неї) — сповіщення з підсумком тижня.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        Toggle tgUpdates, tgWeekly;
        System.Windows.Forms.Timer updTimer;
        Action balloonAction;
        DateTime balloonAt;
        volatile bool updChecking;

        void SetupUpdates()
        {
            tgUpdates.Checked = Updates.Enabled;
            tgUpdates.Changed += (s, e) => { Updates.Enabled = tgUpdates.Checked; if (tgUpdates.Checked) CheckUpdates(false); };
            tgWeekly.Checked = Settings.GetBool("WeekSummary", true);
            tgWeekly.Changed += (s, e) => Settings.SetBool("WeekSummary", tgWeekly.Checked);
            nav.VersionClicked += () => CheckUpdates(true);
            nav.UpdateClicked += OfferUpdate;
            nav.TelegramClicked += () => OpenUrl(Updates.Telegram);
            tray.BalloonTipClicked += (s, e) =>
            {
                var a = balloonAction;
                balloonAction = null;
                if (a != null && (DateTime.UtcNow - balloonAt).TotalSeconds < 60) a();
            };
            ShowUpdateState();
            // Перша перевірка — одразу після запуску (за кілька секунд), далі щогодини.
            updTimer = new System.Windows.Forms.Timer { Interval = 5 * 1000 };
            bool first = true;
            updTimer.Tick += (s, e) =>
            {
                updTimer.Interval = 10 * 60 * 1000;
                if (Updates.Enabled && (first || Updates.Due)) CheckUpdates(false);
                first = false;
                CheckWeekly();
            };
            updTimer.Start();
        }

        void Balloon(string title, string text, ToolTipIcon icon, Action click)
        {
            balloonAction = click;
            balloonAt = DateTime.UtcNow;
            tray.ShowBalloonTip(8000, title, text, icon);
        }

        // Кнопка «Оновити до …» у меню та місце для підпису над нею.
        void ShowUpdateState()
        {
            nav.Update = Updates.Latest;
            lblStatus.Top = nav.FootTop - Ui.S(8) - lblStatus.Height;
            lblStatus.Visible = nav.ContentBottom < lblStatus.Top - Ui.S(8);
        }

        void CheckUpdates(bool manual)
        {
            if (updChecking) return;
            updChecking = true;
            if (manual) { nav.VersionNote = null; nav.Checking = true; }
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var t0 = DateTime.UtcNow;
                string v = Updates.CheckNow();
                // Щоб «Шукаю оновлення…» не блимнуло на мить.
                int left = 900 - (int)(DateTime.UtcNow - t0).TotalMilliseconds;
                if (manual && left > 0) Thread.Sleep(left);
                try
                {
                    BeginInvoke(new MethodInvoker(() =>
                    {
                        updChecking = false;
                        nav.Checking = false;
                        string latest = Updates.Latest;
                        ShowUpdateState();
                        if (manual)
                        {
                            nav.VersionNote = v == null ? "Немає зв'язку з GitHub" : latest == null ? "Версія " + AppVersion.Text + " — найновіша ✓" : null;
                            if (nav.VersionNote != null)
                            {
                                var t = new System.Windows.Forms.Timer { Interval = 5000 };
                                t.Tick += (a, b) => { t.Stop(); t.Dispose(); nav.VersionNote = null; };
                                t.Start();
                            }
                            if (latest != null) OfferUpdate();
                        }
                        else if (latest != null && Settings.Get("UpdateNotified", "") != latest)
                        {
                            Settings.Set("UpdateNotified", latest);
                            Balloon("Тихий ПК: вийшла версія " + latest, "Натисни, щоб оновити. Налаштування й історія збережуться.", ToolTipIcon.Info, OfferUpdate);
                        }
                    }));
                }
                catch { updChecking = false; }
            });
        }

        void OfferUpdate()
        {
            string v = Updates.Latest;
            if (v == null) return;
            ShowWindow();
            var r = MessageBox.Show(this,
                "Вийшла нова версія «Тихого ПК» — " + v + " (у тебе " + AppVersion.Text + ").\n\n" +
                "Завантажити інсталятор? Коли він завантажиться, запусти Tykhyi-PK-setup.exe — він сам закриє «Тихий ПК» і поставить нову версію. " +
                "Налаштування й історія збережуться.\n\n«Ні» — подивитися, що нового.",
                "Оновлення «Тихого ПК»", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
            if (r == DialogResult.Yes) OpenUrl(Updates.Download);
            else if (r == DialogResult.No) OpenUrl(Updates.ReleasesPage);
        }

        // Підсумок тижня: показується один раз за тиждень — у неділю після 19:00 або при першому запуску після неї.
        void CheckWeekly()
        {
            if (!Settings.GetBool("WeekSummary", true)) return;
            var now = DateTime.Now;
            var mon = Energy.WeekStart(now);
            var target = now.DayOfWeek == DayOfWeek.Sunday && now.Hour >= 19 ? mon : mon.AddDays(-7);
            string key = target.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (string.CompareOrdinal(Settings.Get("WeekShown", ""), key) >= 0) return;
            Settings.Set("WeekShown", key);
            var w = Energy.Week(target);
            if (!w.HasData) return;
            var temps = new List<string>();
            if (w.MaxCpu > 0) temps.Add("процесор " + Deg(w.MaxCpu));
            if (w.MaxGpu > 0) temps.Add("відеокарта " + Deg(w.MaxGpu));
            string text = (w.Hours >= 0.25 ? "ПК працював " + Energy.Hours(w.Hours) + ". " : "") +
                (temps.Count > 0 ? "Найвищі температури: " + string.Join(", ", temps) + ". " : "") +
                "Електрика ≈ " + Energy.Money(w.Kwh * Energy.Tariff, false) + " (" + Energy.Kwh(w.Kwh) + ").";
            Balloon("Тихий ПК: підсумок тижня", text, ToolTipIcon.Info, () => { ShowWindow(); ShowView(4, 0); });
        }
    }
}
