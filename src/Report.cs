// Тихий ПК — звіт-картинка про комп'ютер (щоб скинути другові, коли просиш допомоги) і навантаження для тесту «до і після».
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TyhyiPK
{
    class SysInfo
    {
        public string Os = "", Cpu = "", Board = "", Model = "", Ram = "", Uptime = "", Screen = "";
        public List<string> Gpus = new List<string>();

        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        static string Reg(string key, string name)
        {
            try
            {
                using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hk.OpenSubKey(key))
                    return k != null && k.GetValue(name) != null ? Convert.ToString(k.GetValue(name)).Trim() : "";
            }
            catch { return ""; }
        }

        static IEnumerable<ManagementObject> Wmi(string query)
        {
            var list = new List<ManagementObject>();
            try
            {
                using (var q = new ManagementObjectSearcher(query))
                    foreach (ManagementObject mo in q.Get()) list.Add(mo);
            }
            catch { }
            return list;
        }

        static string S(ManagementObject mo, string p)
        {
            try { return mo[p] == null ? "" : Convert.ToString(mo[p]).Trim(); } catch { return ""; }
        }

        static bool Junk(string s)
        {
            string l = (s ?? "").ToLowerInvariant();
            return l.Length == 0 || l.Contains("to be filled") || l.Contains("default string") || l.Contains("system product name") || l == "system manufacturer" || l == "o.e.m.";
        }

        public static SysInfo Read()
        {
            var i = new SysInfo();
            const string cv = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion";
            string product = Reg(cv, "ProductName"), disp = Reg(cv, "DisplayVersion"), build = Reg(cv, "CurrentBuild"), ubr = Reg(cv, "UBR");
            int b;
            if (int.TryParse(build, out b) && b >= 22000) product = product.Replace("Windows 10", "Windows 11");
            i.Os = (product.Length > 0 ? product : Environment.OSVersion.VersionString) + (disp.Length > 0 ? " " + disp : "") +
                (build.Length > 0 ? " (збірка " + build + (ubr.Length > 0 ? "." + ubr : "") + ")" : "");
            i.Cpu = System.Text.RegularExpressions.Regex.Replace(Reg("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0", "ProcessorNameString"), "\\s+", " ")
                + " · " + Environment.ProcessorCount + " потоків";
            foreach (var mo in Wmi("SELECT Name FROM Win32_VideoController"))
                using (mo) { string n = S(mo, "Name"); if (n.Length > 0 && !n.Contains("Basic Display") && !n.Contains("Remote")) i.Gpus.Add(n); }
            foreach (var mo in Wmi("SELECT Manufacturer, Product FROM Win32_BaseBoard"))
                using (mo) { string m = S(mo, "Manufacturer"), p = S(mo, "Product"); if (!Junk(p)) i.Board = (Junk(m) ? "" : m + " ") + p; }
            foreach (var mo in Wmi("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                using (mo) { string m = S(mo, "Manufacturer"), p = S(mo, "Model"); if (!Junk(p) && !Junk(m)) i.Model = m + " " + p; }
            double total = 0;
            int sticks = 0;
            string speed = "";
            foreach (var mo in Wmi("SELECT Capacity, Speed, ConfiguredClockSpeed FROM Win32_PhysicalMemory"))
            {
                using (mo)
                {
                    try { total += Convert.ToDouble(mo["Capacity"]); sticks++; } catch { }
                    string sp = S(mo, "ConfiguredClockSpeed");
                    if (sp.Length == 0 || sp == "0") sp = S(mo, "Speed");
                    if (sp.Length > 0 && sp != "0") speed = sp;
                }
            }
            if (total <= 0)
            {
                var m = new MemoryStatus();
                try { if (NativeMem(m)) total = m.TotalPhys; } catch { }
            }
            i.Ram = total > 0 ? Math.Round(total / 1073741824.0) + " ГБ" + (sticks > 0 ? " (" + sticks + " план.)" : "") + (speed.Length > 0 ? " · " + speed + " МГц" : "") : "";
            try
            {
                var up = TimeSpan.FromMilliseconds(GetTickCount64());
                i.Uptime = (up.Days > 0 ? up.Days + " дн. " : "") + up.Hours + " год " + up.Minutes + " хв";
            }
            catch { }
            try { var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds; i.Screen = sc.Width + "×" + sc.Height; } catch { }
            return i;
        }

        [DllImport("kernel32.dll")]
        static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus m);
        static bool NativeMem(MemoryStatus m) { return GlobalMemoryStatusEx(m); }
    }

    // Рядок звіту: назва, значення, колір значення (null — звичайний).
    class ReportLine
    {
        public string Key, Value;
        public Color? Color;
        public ReportLine(string k, string v, Color? c) { Key = k; Value = v; Color = c; }
    }

    class ReportSection
    {
        public string Title;
        public List<ReportLine> Lines = new List<ReportLine>();
        public ReportSection(string t) { Title = t; }
        public void Add(string k, string v) { Add(k, v, null); }
        public void Add(string k, string v, Color? c) { if (!string.IsNullOrEmpty(v)) Lines.Add(new ReportLine(k, v, c)); }
    }

    static class ReportImage
    {
        public static readonly Color Good = Color.FromArgb(36, 138, 61), Warn = Color.FromArgb(201, 110, 0), Bad = Color.FromArgb(215, 0, 21);

        // Малює текст згладженим (у звичайних картинках GDI-текст виходить «брудним»).
        static void T(Graphics g, string text, Font f, RectangleF r, Color c)
        {
            using (var sf = new StringFormat(StringFormatFlags.NoWrap))
            using (var b = new SolidBrush(c))
            {
                sf.LineAlignment = StringAlignment.Center;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                g.DrawString(text ?? "", f, b, r, sf);
            }
        }

        // Картинка вдвічі більша за макет — щоб текст був чітким навіть на екранах з масштабом 150–200%.
        public static Bitmap Draw(string title, string subtitle, List<ReportSection> sections)
        {
            const int W = 900, pad = 36, rowH = 30, headH = 44, gap = 18, K = 2;
            int h = 120 + sections.Sum(s => headH + Math.Max(1, s.Lines.Count) * rowH + gap) + 40;
            var bmp = new Bitmap(W * K, h * K, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(bmp))
            using (var fTitle = new Font("Segoe UI Semibold", 24f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fSub = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fHead = new Font("Segoe UI Semibold", 16f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fKey = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (var fVal = new Font("Segoe UI Semibold", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                g.ScaleTransform(K, K);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.FromArgb(242, 242, 247));
                var ink = Color.FromArgb(28, 28, 30);
                var ink2 = Color.FromArgb(110, 110, 115);
                var sep = Color.FromArgb(229, 229, 234);
                T(g, title, fTitle, new RectangleF(pad, 24, W - 2 * pad, 40), ink);
                T(g, subtitle, fSub, new RectangleF(pad, 62, W - 2 * pad, 24), ink2);
                int y = 104;
                foreach (var s in sections)
                {
                    T(g, s.Title, fHead, new RectangleF(pad + 4, y + 12, W - 2 * pad, 26), ink);
                    y += headH;
                    int ch = Math.Max(1, s.Lines.Count) * rowH + 8;
                    Ui.FillRound(g, new RectangleF(pad, y - 4, W - 2 * pad, ch), 14, Color.FromArgb(229, 229, 234));
                    Ui.FillRound(g, new RectangleF(pad + 1, y - 3, W - 2 * pad - 2, ch - 2), 13, Color.White);
                    const int keyW = 260;
                    for (int i = 0; i < s.Lines.Count; i++)
                    {
                        var l = s.Lines[i];
                        var r = new RectangleF(pad + 18, y + i * rowH, W - 2 * pad - 36, rowH);
                        T(g, l.Key, fKey, new RectangleF(r.X, r.Y, keyW - 10, r.Height), ink2);
                        T(g, l.Value, fVal, new RectangleF(r.X + keyW, r.Y, r.Width - keyW, r.Height), l.Color ?? ink);
                        if (i < s.Lines.Count - 1)
                            using (var pen = new Pen(sep)) g.DrawLine(pen, r.X, r.Bottom, r.Right, r.Bottom);
                    }
                    if (s.Lines.Count == 0) T(g, "немає даних", fKey, new RectangleF(pad + 18, y, 400, rowH), ink2);
                    y += ch - 8 + gap;
                }
                T(g, "Зроблено програмою «Тихий ПК»", fSub, new RectangleF(pad, h - 40, W - 2 * pad, 24), ink2);
            }
            return bmp;
        }
    }

    // Повне навантаження на всі ядра процесора — для тесту «до і після».
    class CpuBurner
    {
        volatile bool run;
        long work;
        readonly List<Thread> threads = new List<Thread>();

        public long Work { get { return Interlocked.Read(ref work); } }

        public void Start()
        {
            run = true;
            for (int i = 0; i < Environment.ProcessorCount; i++)
            {
                var t = new Thread(Burn);
                t.IsBackground = true;
                t.Priority = ThreadPriority.BelowNormal;   // вікно лишається чуйним
                threads.Add(t);
                t.Start();
            }
        }

        public void Stop()
        {
            run = false;
            foreach (var t in threads) t.Join(2000);
            threads.Clear();
        }

        void Burn()
        {
            double x = 1.0001;
            while (run)
            {
                for (int i = 0; i < 200000; i++) x = Math.Sqrt(x * 1.0000001 + i) * 0.999 + Math.Sin(x);
                Interlocked.Increment(ref work);
            }
            if (x == 42) work++;   // щоб компілятор не викинув обчислення
        }
    }
}
