// Тихий ПК — головне вікно: побудова інтерфейсу, вікно й значок біля годинника.
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TyhyiPK
{
    partial class MainForm
    {
        // Сторінка — це вміст ScrollHost, тож довгі сторінки прокручуються колесом миші.
        Page NewPage()
        {
            var host = new ScrollHost();
            host.Bounds = Ui.R(SideW, HeaderH, 600, AutoPageH);
            host.Visible = false;
            Controls.Add(host);
            return host.Content;
        }

        static ScrollHost HostOf(Page p) { return p.Parent as ScrollHost; }

        void FitPages()
        {
            foreach (var group in views)
                foreach (var p in group) { var h = HostOf(p); if (h != null) h.Fit(); }
        }

        // Розділ сторінки: окрема Page, що вставляється в іншу сторінку на висоті y (з підписом над нею).
        void Place(Page target, Page section, ref int y, string caption)
        {
            if (caption != null)
            {
                target.SetCaption("sec" + target.Captions.Count, Ui.R(40, y + 4, 520, 18), caption);
                y += 26;
            }
            section.Location = new Point(0, Ui.S(y));
            section.Width = Ui.S(600);
            section.Visible = true;
            target.Controls.Add(section);
            y += (int)Math.Round(section.Height / Ui.Scale) + 12;
        }

        StatTile Tile(Page page, int i, int y, int h, string caption)
        {
            var t = new StatTile();
            t.Caption = caption;
            t.LeftLine = i > 0;
            t.Bounds = Ui.R(30 + i * 135, y, 135, h);
            page.Controls.Add(t);
            return t;
        }

        void BuildRow(Row row, int y)
        {
            row.Seg = new Segmented();
            row.Seg.Items = Modes.All.Select(m => m.Title).ToArray();
            row.Seg.Accent = i => Modes.All[i].Color;
            row.Seg.Bounds = Ui.R(40, y, 520, 34);
            row.Seg.ItemClicked += i => UserSelect(row, Modes.Keys[i], true);
            pageModes.Controls.Add(row.Seg);
            row.DescLabel = Ui.MakeLabel(pageModes, "", Ui.R(40, y + 42, 520, 48), Ui.Small, Ui.CInk2, Ui.CCard);
        }

        Segmented MiniSeg(string[] keys, Rectangle r)
        {
            var s = new Segmented();
            s.Items = keys.Select(k => Modes.Title(k)).ToArray();
            s.Accent = i => Modes.Get(keys[i]).Color;
            s.Bounds = r;
            pageAuto.Controls.Add(s);
            return s;
        }

        Label RowLabel(string text, Rectangle r)
        {
            var l = Ui.MakeLabel(pageAuto, text, r, Ui.Body, Ui.CInk, Ui.CCard);
            l.TextAlign = ContentAlignment.MiddleLeft;
            return l;
        }

        Toggle NewToggle(int y)
        {
            var t = new Toggle();
            t.Bounds = Ui.R(514, y, 46, 28);
            pageAuto.Controls.Add(t);
            return t;
        }

        void ShowPage(int i) { ShowView(i, 0); }

        bool SensorsVisible() { return views != null && views[1].Any(v => v.Visible); }

        // Показати сторінку i, підрозділ k (кожен підрозділ — окрема сторінка).
        void ShowView(int i, int k)
        {
            if (views == null) return;
            k = Math.Max(0, Math.Min(views[i].Length - 1, k));
            var show = views[i][k];
            if (HostOf(show).Visible) HostOf(show).ScrollTo(0);
            foreach (var group in views)
                foreach (var v in group)
                    if (v != show) HostOf(v).Visible = false;
            curPage = show;
            EnsureLaid(show);
            HostOf(show).Visible = true;
            nav.Selected = i;
            bool hasSubs = views[i].Length > 1;
            nav.SubSelected = hasSubs ? k : -1;
            lblStatus.Visible = nav.ContentBottom < lblStatus.Top - Ui.S(8);
            lblTitle.Text = hasSubs ? nav.Subs[i][k] : NavTitles[i];
            lblSub.Text = hasSubs && viewSubs[i] != null ? viewSubs[i][k] : NavSubs[i];
            if (show == pageProcs) { UpdateProcs(); procTimer.Interval = 1000; }
            if (i == 2 && k == 1) ReloadAutoruns();
            if (show == pageEnergy) RefreshEnergy();
            if (i == 1) RefreshHealth();
            if (i == 1 && k == sensDust) RefreshAdvice();
            if (i == 3 && k == 1) RefreshAdvice();
            if (i == 5) OnSvcShown(k);
            if (show == pageModes) UpdateHome();
            if (fanTimer != null) ApplyTraySettings();
            if (layoutReady) PlaceHeader();
        }

        static string ValidMode(string m, string fallback)
        {
            if (Modes.Get(m) != null) return m;
            return Modes.Get(fallback) != null ? fallback : "normal";
        }

        static TimeSpan ParseTime(string s, int defHour)
        {
            TimeSpan t;
            return TimeSpan.TryParse(s, out t) ? t : new TimeSpan(defHour, 0, 0);
        }

        static int ParseInt(string s, int def)
        {
            int v;
            return int.TryParse(s, out v) ? v : def;
        }

        // Тема: «Авто» — як у Windows, або вибрана вручну (кнопки вгорі праворуч).
        public static bool WantDark()
        {
            string t = Settings.Get("Theme", "auto");
            return t == "dark" || (t != "light" && Ui.SystemDark());
        }

        void ApplyThemeNow()
        {
            bool dark = WantDark();
            if (dark == Ui.Dark) return;
            Ui.ApplyTheme(dark);
            BackColor = Ui.Bg;
            foreach (var a in Ui.Rethemers.ToList()) a();
            Ui.StyleCaption(this);
            Invalidate(true);
        }

        void CheckTheme()
        {
            bool dark = WantDark();
            if (dark == Ui.Dark) return;
            Ui.ApplyTheme(dark);
            BackColor = Ui.Bg;
            foreach (var a in Ui.Rethemers.ToList()) a();
            Ui.StyleCaption(this);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TaskbarIcon.AllowIconQueries(Handle);
            Ui.StyleCaption(this);
        }
    
        protected override void SetVisibleCore(bool value)
        {
            if (!allowShow)
            {
                value = false;
                if (!IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        void ShowWindow()
        {
            allowShow = true;
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = restoreMax ? FormWindowState.Maximized : FormWindowState.Normal;
            Activate();
            ShowSnapshotTiles(last);
            if (pageProcs.Visible) UpdateProcs();
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
                if (!Settings.GetBool("TrayHint", false))
                {
                    Settings.SetBool("TrayHint", true);
                    tray.ShowBalloonTip(4000, "Тихий ПК працює далі",
                        "Він тут, біля годинника. Клікни на значок, щоб відкрити, або правою кнопкою — щоб вийти.", ToolTipIcon.Info);
                }
                return;
            }
            stop = true;
            Energy.Save();
            GameStats.Save();
            Thermal.Save();
            tray.Visible = false;
        }

        void ExitApp()
        {
            exiting = true;
            stop = true;
            Energy.Save();
            GameStats.Save();
            Thermal.Save();
            tray.Visible = false;
            Application.Exit();
        }

        void Restart(bool elevate)
        {
            try
            {
                var psi = new ProcessStartInfo(Application.ExecutablePath, "/restart");
                psi.UseShellExecute = true;
                if (elevate) psi.Verb = "runas";
                Process.Start(psi);
                ExitApp();
            }
            catch { }
        }

        void SaveAuto()
        {
            if (loading) return;
            Settings.SetBool("AutoGame", tgGame.Checked);
            Settings.Set("GameMode", GameModeKeys[Math.Max(0, segGame.Selected)]);
            Settings.SetBool("Night", tgNight.Checked);
            Settings.Set("NightFrom", tbFrom.Value.ToString("hh\\:mm"));
            Settings.Set("NightTo", tbTo.Value.ToString("hh\\:mm"));
            Settings.Set("NightMode", QuietModeKeys[Math.Max(0, segNight.Selected)]);
            if (hasBattery)
            {
                Settings.SetBool("Battery", tgBattery.Checked);
                Settings.Set("BatteryMode", QuietModeKeys[Math.Max(0, segBattery.Selected)]);
            }
            Settings.SetBool("Heat", tgHeat.Checked);
            Settings.Set("HeatLimit", HeatLimits[Math.Max(0, segHeat.Selected)].ToString());
            if (!tgHeat.Checked)
                foreach (var r in rows) { r.Heat = false; r.HotSince = r.CoolSince = DateTime.MinValue; }
            EvaluateAuto();
        }

        void RefreshRow(Row row)
        {
            row.Seg.Enabled = row.Available;
            row.Seg.Selected = Modes.Index(row.Current);
            row.DescLabel.Text = row.Available ? row.Desc(row.Current) : row.UnavailableText;
        }

        void UpdateTrayText()
        {
            string t = "Тихий ПК — процесор: " + Modes.Title(cpu.Current) + (gpu.Available ? ", карта: " + Modes.Title(gpu.Current) : "");
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }
    
        bool ApplyRow(Row row, string mode, bool byUser)
        {
            Cursor = Cursors.WaitCursor;
            bool ok = row.Available && row.Apply(mode);
            Cursor = Cursors.Default;
            if (ok)
            {
                row.Current = mode;
                row.Failed = null;
                RefreshRow(row);
                UpdateTrayText();
                return true;
            }
            row.Failed = mode;
            row.FailedAt = DateTime.UtcNow;
            RefreshRow(row);
            if (byUser)
            {
                if (!Program.IsAdmin)
                {
                    if (MessageBox.Show("Windows не дозволив змінити налаштування.\nПерезапустити Тихий ПК з правами адміністратора?", "Тихий ПК",
                            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                        Restart(true);
                }
                else
                {
                    MessageBox.Show("Не вдалося змінити режим (" + row.Title.ToLowerInvariant() + "). Windows або драйвер не прийняли нове значення.",
                        "Тихий ПК", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            return false;
        }

        void UserSelect(Row row, string mode, bool evaluate)
        {
            row.Manual = mode;
            Settings.Set(row == cpu ? "Manual" : "GpuManual", mode);
            string reason;
            CurrentReason(out reason);
            row.Suppressed = reason;
            if (row.Heat && Modes.Index(mode) > Modes.Index("quiet") && evaluate)
                MessageBox.Show(row.Title + " зараз гарячий (" + Math.Round(row.HeatTemp) +
                    "°), тому діє захист від перегріву: режим обмежено до «Тихий», поки не охолоне. Вибраний режим увімкнеться сам.\n\nВимкнути захист можна на вкладці «Автоматика».",
                    "Тихий ПК", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (evaluate) EvaluateAuto(row);
        }

        // Яка умова автоматики зараз діє: "game", "battery", "night" або null. Для гри повертає її назву.
        string CurrentReason(out string kind)
        {
            string game = DetectGame();
            runningGame = game;
            if (!tgGame.Checked) game = null;
            if (game != null) { kind = "game"; return game; }
            if (hasBattery && tgBattery.Checked && OnBattery()) { kind = "battery"; return null; }
            if (tgNight.Checked && IsNight(DateTime.Now)) { kind = "night"; return null; }
            kind = null;
            return null;
        }

        static bool OnBattery()
        {
            try { return SystemInformation.PowerStatus.PowerLineStatus == PowerLineStatus.Offline; }
            catch { return false; }
        }

        bool IsNight(DateTime now)
        {
            var t = new TimeSpan(now.Hour, now.Minute, 0);
            TimeSpan from = tbFrom.Value, to = tbTo.Value;
            if (from == to) return false;
            if (from < to) return t >= from && t < to;
            return t >= from || t < to;
        }

        void EvaluateAuto() { EvaluateAuto(null); }
    
        // Головна логіка автоматики: вирішує, який режим має діяти зараз для кожного рядка.
        void EvaluateAuto(Row userRow)
        {
            if (perfTest) return;   // під час тесту «до і після» режими перемикає сам тест
            string kind;
            string game = CurrentReason(out kind);
            string target = kind == "game" ? (GameStats.ModeFor(game) ?? GameModeKeys[Math.Max(0, segGame.Selected)])
                : kind == "battery" ? QuietModeKeys[Math.Max(0, segBattery.Selected)]
                : kind == "night" ? QuietModeKeys[Math.Max(0, segNight.Selected)]
                : null;
            var changed = new List<string>();
            bool autoActive = false;
            foreach (var r in rows)
            {
                if (r.Suppressed != null && kind != r.Suppressed) r.Suppressed = null;
                if (!r.Available) continue;
                bool auto = kind != null && kind != r.Suppressed;
                if (auto) autoActive = true;
                string want = auto ? target : r.Manual;
                if (r.Heat && Modes.Index(want) > Modes.Index("quiet")) want = "quiet";
                bool heatStart = r.HeatJustStarted;
                r.HeatJustStarted = false;
                if (want == r.Current) continue;
                bool byUser = r == userRow;
                if (!byUser && want == r.Failed && (DateTime.UtcNow - r.FailedAt).TotalSeconds < 60) continue;
                if (!(ApplyRow(r, want, byUser) && !byUser)) continue;
                if (heatStart)
                    tray.ShowBalloonTip(5000, "Тихий ПК: перегрів", r.Title + ": " + Math.Round(r.HeatTemp) + "°. Вмикаю «Тихий», поки не охолоне.", ToolTipIcon.Warning);
                else
                    changed.Add(r.Title.ToLowerInvariant() + " — «" + Modes.Title(want) + "»");
            }

            string status;
            var hot = rows.Where(r => r.Heat).ToList();
            if (hot.Count > 0)
                status = "Перегрів: " + string.Join(", ", hot.Select(r => r.Title.ToLowerInvariant() + " " + Math.Round(r.HeatTemp) + "°")) + " — режим обмежено до «Тихий», поки не охолоне.";
            else if (kind == "game")
                status = autoActive ? "Запущено гру «" + game + "» — увімкнено «" + Modes.Title(target) + "»."
                    : "Запущено гру «" + game + "», але режим вибрано вручну — автоматика чекає, поки гра закриється.";
            else if (kind == "battery")
                status = autoActive ? "Працює від батареї — увімкнено «" + Modes.Title(target) + "»."
                    : "Працює від батареї, але режим вибрано вручну — автоматика чекає на зарядку.";
            else if (kind == "night")
                status = autoActive ? "Нічний час — увімкнено «" + Modes.Title(target) + "» до " + tbTo.Value.ToString("hh\\:mm") + "."
                    : "Нічний час, але режим вибрано вручну — автоматика повернеться наступної ночі.";
            else
                status = tgGame.Checked || tgNight.Checked || tgBattery.Checked || tgHeat.Checked
                    ? "Автоматика стежить, але зараз нічого не змінює." : "Автоматика вимкнена.";
            lblStatus.Text = status;
            if (heroRules != null)
            {
                bool anyOn = tgGame.Checked || tgNight.Checked || tgBattery.Checked || tgHeat.Checked;
                if (hot.Count > 0) heroRules.Set("Захист від перегріву спрацював.", status, "warn", Ui.Orange);
                else if (autoActive) heroRules.Set("Автоматика працює.", status, "auto", Ui.Blue);
                else if (anyOn) heroRules.Set("Автоматика стежить.", status, "auto", Ui.Green);
                else heroRules.Set("Автоматика вимкнена.", "Увімкни потрібні правила нижче — далі все робитиметься саме.", "auto", Ui.Ink3);
            }
            if (changed.Count > 0 && started && !Visible)
                tray.ShowBalloonTip(3000, "Тихий ПК", "Увімкнено: " + string.Join(", ", changed) + ".", ToolTipIcon.Info);
        }
    
        // Захист від перегріву: вмикається після 10 с вище межі, вимикається після 30 с нижче межі−8°.
        bool TrackHeat(Row row, double t)
        {
            var now = DateTime.UtcNow;
            if (!(tgHeat.Checked && row.Available && !double.IsNaN(t)))
            {
                row.HotSince = row.CoolSince = DateTime.MinValue;
                if (!row.Heat) return false;
                row.Heat = false;
                return true;
            }
            int limit = HeatLimits[Math.Max(0, segHeat.Selected)];
            if (!row.Heat)
            {
                if (t < limit) { row.HotSince = DateTime.MinValue; return false; }
                if (row.HotSince == DateTime.MinValue) { row.HotSince = now; return false; }
                if ((now - row.HotSince).TotalSeconds < 10) return false;
                row.Heat = true;
                row.HeatJustStarted = true;
                row.HeatTemp = t;
                row.CoolSince = DateTime.MinValue;
                return true;
            }
            row.HeatTemp = t;
            if (t > limit - 8) { row.CoolSince = DateTime.MinValue; return false; }
            if (row.CoolSince == DateTime.MinValue) { row.CoolSince = now; return false; }
            if ((now - row.CoolSince).TotalSeconds < 30) return false;
            row.Heat = false;
            row.HotSince = DateTime.MinValue;
            return true;
        }

        string ProcessPath(int pid, string name)
        {
            KeyValuePair<string, string> c;
            if (pathCache.TryGetValue(pid, out c) && c.Key == name) return c.Value;
            string path = null;
#if LINUXTEST
            return null;
#endif
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h != IntPtr.Zero)
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (QueryFullProcessImageName(h, 0, sb, ref size)) path = sb.ToString();
                CloseHandle(h);
            }
            pathCache[pid] = new KeyValuePair<string, string>(name, path);
            return path;
        }

        string DetectGame()
        {
            string found = null;
            var alive = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    int id = p.Id;
                    if (id <= 4) continue;
                    string name;
                    try { name = p.ProcessName; } catch { continue; }
                    alive.Add(id);
                    if (found != null || Games.IsIgnored(name)) continue;
                    if (Games.IsKnown(name) || Games.IsGamePath(name, ProcessPath(id, name))) found = name;
                }
            }
            foreach (int k in pathCache.Keys.Where(k => !alive.Contains(k)).ToList()) pathCache.Remove(k);
            return found;
        }
    
        // Фоновий потік: раз на 2 с (вікно відкрите) або 5 с (сховане) знімає показники датчиків.
        void SensorLoop()
        {
            Lhm.Init();
            long pIdle = 0, pKernel = 0, pUser = 0;
            double board = double.NaN;
            double[] gpuVals = null;
            int tick = 0;
            while (!stop)
            {
                bool vis = uiVisible;
                var s = new Snapshot();
                try
                {
                long idle, kernel, user;
                if (GetSystemTimes(out idle, out kernel, out user))
                {
                    long total = (kernel - pKernel) + (user - pUser);
                    if (pKernel != 0 && total > 0)
                        s.CpuLoad = Math.Max(0, Math.Min(100, (int)Math.Round(100.0 * (total - (idle - pIdle)) / total)));
                    pIdle = idle; pKernel = kernel; pUser = user;
                }
                var d = Lhm.Read();
                double ct = d.CpuTemp;
                if (double.IsNaN(ct))
                {
                    if (tick % 5 == 0) board = Lhm.ReadBoard();
                    ct = board;
                    s.CpuTempFromBoard = !double.IsNaN(board);
                }
                s.CpuTemp = ct;
                s.CpuPower = d.CpuPower;
                s.Fan = d.Fan;
                s.FanIsCpu = d.FanIsCpu;
                s.FanCount = d.FanCount;
                if (Gpu.Smi != null && (!vis || tick % 2 == 0))
                {
                    double[] v;
                    gpuVals = Gpu.Query(out v) ? v : null;
                }
                if (gpuVals != null)
                {
                    s.GpuLimit = gpuVals[3];
                    s.GpuDraw = gpuVals[4];
                    s.GpuTemp = gpuVals[5];
                }
                if (double.IsNaN(s.GpuTemp)) s.GpuTemp = d.GpuTemp;
                try
                {
                    var m = new MemoryStatus();
                    if (GlobalMemoryStatusEx(m))
                    {
                        s.MemTotal = m.TotalPhys;
                        s.MemUsed = m.TotalPhys - m.AvailPhys;
                    }
                }
                catch { }
                if (hasBattery)
                {
                    try
                    {
                        var bat = BatteryInfo.Read(false);
                        if (bat.Present && bat.Discharging && !bat.PowerOnline) s.BatteryW = bat.RateW;
                        if (bat.Present)
                        {
                            s.BatteryPct = bat.ChargePct;
                            s.BatteryRemainMWh = bat.RemainingMWh;
                            s.BatteryOnAc = bat.PowerOnline;
                            s.BatteryCharging = bat.Charging;
                        }
                    }
                    catch { }
                }
                s.GpuPowerLhm = d.GpuPower;
                s.Time = DateTime.UtcNow;
                }
                catch { }   // датчики не повинні зупиняти програму
                tick++;
                try { BeginInvoke(new MethodInvoker(() => OnSnapshot(s))); } catch { }
                Thread.Sleep(tick == 1 ? 700 : vis ? 2000 : 5000);
            }
        }

        void OnSnapshot(Snapshot s)
        {
            if (s.CpuLoad < 0) s.CpuLoad = last.CpuLoad;
            last = s;
            if (!(double.IsNaN(s.CpuTemp) && double.IsNaN(s.GpuTemp)))
            {
                var now = DateTime.UtcNow;
                var t = new TempSample();
                t.T = now;
                t.Cpu = s.CpuTempFromBoard ? double.NaN : s.CpuTemp;
                t.Gpu = s.GpuTemp;
                history.Add(t);
                history.RemoveAll(h => h.T < now - chart.Window);
            }
            Energy.Add(s, hasBattery);
            if (Visible && pageEnergy.Visible) RefreshEnergy();
            // Спостереження для порад: ігри, пил, батарея.
            double dt = lastStatT == DateTime.MinValue ? 0 : Math.Min(30, (s.Time - lastStatT).TotalSeconds);
            lastStatT = s.Time;
            if (runningGame != null && dt > 0 && !perfTest)
                GameStats.Add(runningGame, s.CpuTempFromBoard ? double.NaN : s.CpuTemp, s.GpuTemp, dt);
            if (!perfTest) Thermal.Add(s);
            if (hasBattery)
            {
                BatteryForecast.Add(s.BatteryW);
                batteryPct = s.BatteryPct;
                batteryRemainMWh = s.BatteryRemainMWh;
                batteryOnAc = s.BatteryOnAc;
                batteryCharging = s.BatteryCharging;
            }
            UpdateTrayIcon(s);
            if (Visible && pageModes.Visible) UpdateHome();
            if (Visible) ShowSnapshotTiles(s);
            bool changed = TrackHeat(cpu, s.CpuTempFromBoard ? double.NaN : s.CpuTemp);
            changed |= TrackHeat(gpu, s.GpuTemp);
            if (changed) EvaluateAuto();
        }

        static string Watts(double w) { return Math.Round(w) + " Вт"; }
        static string Deg(double t) { return Math.Round(t) + "°"; }
    
        void ShowSnapshotTiles(Snapshot s)
        {
            foreach (var t in new[] { mCpuTemp, sCpuTemp })
            {
                if (!double.IsNaN(s.CpuTemp))
                {
                    if (s.CpuTempFromBoard) t.Set(Deg(s.CpuTemp), null, "датчик плати · точніше?", true);
                    else t.Set(Deg(s.CpuTemp), Ui.TempColor(s.CpuTemp), "температура", false);
                }
                else if (!Lhm.Tried) t.Set("…", Ui.Ink3, "", false);
                else t.Set("—", Ui.Ink3, "як увімкнути?", true);
            }
            if (s.CpuLoad >= 0)
                sCpuLoad.Set(s.CpuLoad + "%", s.CpuLoad >= 90 ? Ui.Orange : (Color?)null, "процесора", false);
            if (!double.IsNaN(s.CpuPower)) sCpuPower.Set(Watts(s.CpuPower), null, "пакет процесора", false);
            else sCpuPower.Set("—", Ui.Ink3, Lhm.Loaded ? "немає датчика" : "як увімкнути?", !Lhm.Loaded);
            foreach (var t in new[] { mFan, sFan })
            {
                if (!double.IsNaN(s.Fan)) t.Set(Math.Round(s.Fan).ToString(), null, s.FanIsCpu ? "об/хв" : "об/хв · вентилятор", false);
                else t.Set("—", Ui.Ink3, Lhm.Loaded ? "немає датчика" : "як увімкнути?", !Lhm.Loaded);
            }
            foreach (var t in new[] { mGpuTemp, sGpuTemp })
            {
                if (!double.IsNaN(s.GpuTemp)) t.Set(Deg(s.GpuTemp), Ui.TempColor(s.GpuTemp), "температура", false);
                else t.Set("—", Ui.Ink3, Gpu.Smi != null ? "" : "немає даних", false);
            }
            if (!double.IsNaN(s.GpuDraw))
                sGpuPower.Set(Watts(s.GpuDraw), null, !double.IsNaN(s.GpuLimit) ? "з " + Watts(s.GpuLimit) + " ліміту" : "", false);
            else
                sGpuPower.Set("—", Ui.Ink3, Gpu.Smi != null ? "" : "немає даних", false);
            if (!double.IsNaN(s.MemTotal) && s.MemTotal > 0)
            {
                int pct = (int)Math.Round(100 * s.MemUsed / s.MemTotal);
                sMem.Set((s.MemUsed / GB).ToString("0.0") + " ГБ", pct >= 90 ? Ui.Red : pct >= 80 ? Ui.Orange : (Color?)null,
                    "з " + Math.Round(s.MemTotal / GB) + " ГБ · " + pct + "%", false);
            }
            bool hasCpu = !double.IsNaN(s.CpuPower), hasGpu = !double.IsNaN(s.GpuDraw);
            foreach (var t in new[] { mTotal, sTotal })
            {
                if (hasCpu && hasGpu) t.Set(Watts(s.CpuPower + s.GpuDraw), null, "процесор + карта", false);
                else if (hasGpu) t.Set(Watts(s.GpuDraw), null, "лише карта", false);
                else if (hasCpu) t.Set(Watts(s.CpuPower), null, "лише процесор", false);
                else t.Set("—", Ui.Ink3, "немає даних", false);
            }
            if (pageSensors.Visible) chart.Invalidate();
            if (heroSens != null)
            {
                double ct = s.CpuTempFromBoard ? double.NaN : s.CpuTemp, gt = s.GpuTemp;
                double t = Math.Max(double.IsNaN(ct) ? 0 : ct, double.IsNaN(gt) ? 0 : gt);
                var parts = new List<string>();
                if (!double.IsNaN(ct)) parts.Add("процесор " + Deg(ct));
                if (!double.IsNaN(gt)) parts.Add("відеокарта " + Deg(gt));
                if (s.CpuLoad >= 0) parts.Add("навантаження " + s.CpuLoad + "%");
                string sub = parts.Count > 0 ? char.ToUpper(parts[0][0]) + string.Join(" · ", parts).Substring(1) + "." : "";
                if (t <= 0) heroSens.Set("Температура процесора недоступна.", "Натисни «як увімкнути?» нижче — потрібен драйвер датчиків PawnIO.", "thermo", Ui.Ink3);
                else if (t >= 85) heroSens.Set("ПК гарячий.", sub, "thermo", Ui.Red);
                else if (t >= 72) heroSens.Set("ПК теплий.", sub, "thermo", Ui.Orange);
                else heroSens.Set("Температури в нормі.", sub, "thermo", Ui.Green);
            }
        }
    
        void ShowTempHelp()
        {
            if (!Program.IsAdmin)
            {
                if (MessageBox.Show("Датчики процесора можна читати лише з правами адміністратора.\nПерезапустити Тихий ПК з цими правами?",
                        "Датчики процесора", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    Restart(true);
                return;
            }
            if (!Lhm.Embedded && Lhm.Dir == null)
            {
                var r = MessageBox.Show("Щоб бачити температуру, споживання й оберти кулера процесора, потрібна безкоштовна бібліотека LibreHardwareMonitor:\n\n" +
                    "1. Завантаж файл LibreHardwareMonitor.zip (саме цей, не .NET.10).\n2. Розпакуй його на робочий стіл.\n" +
                    "3. Запусти LibreHardwareMonitor.exe і погодься встановити драйвер PawnIO.\n4. Перезапусти Тихий ПК — він сам знайде папку.\n\n" +
                    "«Так» — відкрити сторінку завантаження.\n«Ні» — вказати папку з LibreHardwareMonitor вручну.",
                    "Датчики процесора", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Information);
                if (r == DialogResult.Yes) OpenUrl(Lhm.Url);
                else if (r == DialogResult.No) PickLhmFolder();
                return;
            }
            if (!Lhm.Loaded && Lhm.Embedded)
            {
                MessageBox.Show("Не вдалося підключити вбудовану бібліотеку датчиків:\n" + Lhm.Error, "Датчики процесора", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!Lhm.Loaded)
            {
                if (MessageBox.Show("Не вдалося підключити LibreHardwareMonitor з папки:\n" + Lhm.Dir + "\n\n" + Lhm.Error +
                        "\n\nМожливо, це версія для .NET 10 — потрібен саме файл LibreHardwareMonitor.zip.\nВказати іншу папку?",
                        "Датчики процесора", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                    PickLhmFolder();
                return;
            }
            if (!Lhm.PawnIoInstalled())
            {
                InstallPawnIo();
                return;
            }
            MessageBox.Show("LibreHardwareMonitor підключено, але він не бачить цього датчика на твоєму комп'ютері.\nПеревір, чи показує його сам LibreHardwareMonitor. Якщо так — перезапусти Тихий ПК.",
                "Датчики процесора", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        // Драйвер PawnIO ставиться з офіційного сайту (старі версії клали PawnIO_setup.exe поруч із програмою).
        void InstallPawnIo()
        {
            string setup = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "PawnIO_setup.exe");
            if (!File.Exists(setup))
            {
                if (MessageBox.Show("Бракує драйвера PawnIO, через який читаються температура й споживання процесора.\n\n" +
                        "Це безкоштовний драйвер з цифровим підписом.\n1. На сторінці pawnio.eu завантаж PawnIO_setup.exe.\n2. Запусти його й встанови драйвер.\n3. Перезапусти «Тихий ПК».\n\nВідкрити сторінку?",
                        "Датчики процесора", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenUrl("https://pawnio.eu/");
                return;
            }
            if (MessageBox.Show("Бракує драйвера PawnIO, через який читаються температура й споживання процесора.\n\n" +
                    "Це безкоштовний драйвер з цифровим підписом. Встановити його зараз?",
                    "Датчики процесора", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes) return;
            try
            {
                using (var p = Process.Start(new ProcessStartInfo(setup, "-install") { UseShellExecute = true }))
                    if (p != null) p.WaitForExit();
            }
            catch { return; }
            if (Lhm.PawnIoInstalled() && MessageBox.Show("Драйвер встановлено. Перезапустити Тихий ПК, щоб увімкнути датчики?", "Датчики процесора",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
                Restart(false);
        }

        void PickLhmFolder()
        {
            using (var d = new FolderBrowserDialog())
            {
                d.Description = "Вибери папку, де лежить LibreHardwareMonitorLib.dll";
                if (d.ShowDialog(this) != DialogResult.OK) return;
                if (!File.Exists(Path.Combine(d.SelectedPath, "LibreHardwareMonitorLib.dll")))
                {
                    MessageBox.Show("У цій папці немає файлу LibreHardwareMonitorLib.dll.", "Датчики процесора");
                    return;
                }
                Settings.Set("LhmDir", d.SelectedPath);
                if (MessageBox.Show("Готово. Перезапустити Тихий ПК, щоб підключити датчики?", "Датчики процесора", MessageBoxButtons.YesNo) == DialogResult.Yes)
                    Restart(false);
            }
        }

        static void OpenUrl(string url)
        {
            try { Process.Start("explorer.exe", "\"" + url + "\""); }
            catch
            {
                try { Process.Start(url); } catch { }
            }
        }

        void UpdateProcs()
        {
            var now = DateTime.UtcNow;
            double dt = (now - prevTime).TotalMilliseconds;
            prevTime = now;
            var cpuNow = new Dictionary<int, double>();
            var byName = new Dictionary<string, ProcRow>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == 0) continue;
                    string name;
                    double ms;
                    long mem;
                    try
                    {
                        name = p.ProcessName;
                        ms = p.TotalProcessorTime.TotalMilliseconds;
                        mem = p.WorkingSet64;
                    }
                    catch { continue; }
                    cpuNow[p.Id] = ms;
                    ProcRow row;
                    if (!byName.TryGetValue(name, out row))
                    {
                        row = new ProcRow();
                        row.Name = name;
                        byName[name] = row;
                    }
                    row.Mem += mem;
                    double prev;
                    if (prevCpu.TryGetValue(p.Id, out prev) && dt > 0 && dt < 30000)
                        row.Cpu += Math.Max(0, ms - prev) / dt / cores * 100;
                }
            }
            prevCpu = cpuNow;
            var allRows = byName.Values.Where(a => a.Name != "Idle").ToList();
            procList.SetRows(allRows.OrderByDescending(a => a.Cpu).ThenByDescending(a => a.Mem).Take(8).ToList());
            if (heroProcs != null && allRows.Count > 0)
            {
                var top = allRows.OrderByDescending(a => a.Cpu).First();
                var mem = allRows.OrderByDescending(a => a.Mem).First();
                string sub = (top.Cpu >= 1 ? "Найбільше вантажить процесор: " + top.Name + " (" + Math.Round(top.Cpu) + "%)" : "Процесор майже не навантажений") +
                    " · найбільше пам'яті: " + mem.Name + " (" + (mem.Mem / 1048576.0).ToString("N0") + " МБ).";
                heroProcs.Set("Запущено програм: " + allRows.Count + ".", sub, "apps", top.Cpu >= 50 ? Ui.Orange : Ui.Blue);
            }
        }

        void KillSelected()
        {
            string name = procList.SelectedName;
            if (name == null) { MessageBox.Show("Спочатку вибери програму у списку.", "Тихий ПК"); return; }
            if (Protected.Contains(name))
            {
                MessageBox.Show("«" + name + "» — частина Windows, її закривати не варто.", "Тихий ПК", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (string.Equals(name, selfName, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Це сам «Тихий ПК». Щоб вийти, клікни правою кнопкою на його значок біля годинника.", "Тихий ПК");
                return;
            }
            var all = Process.GetProcessesByName(name);
            var windowed = all.Where(p =>
            {
                try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; }
            }).ToList();
            if (windowed.Count > 0)
            {
                if (MessageBox.Show("Закрити «" + name + "»?\nЯкщо там є незбережене — програма сама запитає.", "Тихий ПК",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                foreach (var p in windowed) { try { p.CloseMainWindow(); } catch { } }
            }
            else
            {
                if (MessageBox.Show("«" + name + "» працює у фоні без вікна.\nЗупинити примусово?", "Тихий ПК",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                foreach (var p in all) { try { p.Kill(); } catch { } }
            }
            procList.SelectedName = null;
            UpdateProcs();
        }
    }
}
