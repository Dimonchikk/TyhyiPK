// Тихий ПК — режими процесора (powercfg), запуск зовнішніх утиліт.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TyhyiPK
{
    class ModeInfo
    {
        public string Key, Title, Desc;
        readonly Color light, dark;
        public Color Color { get { return Ui.Dark ? dark : light; } }

        public ModeInfo(string key, string title, Color light, Color dark, string desc)
        {
            Key = key; Title = title; this.light = light; this.dark = dark; Desc = desc;
        }
    }

    static class Modes
    {
        public static readonly ModeInfo[] All =
        {
            new ModeInfo("silent", "Дуже тихий", Color.FromArgb(0, 113, 227), Color.FromArgb(100, 170, 255),
                "Частоту обмежено до 70%. Найтихіше, але важкі програми (ігри, рендер, великі розрахунки) працюватимуть повільніше."),
            new ModeInfo("quiet", "Тихий", Color.FromArgb(36, 138, 61), Color.FromArgb(80, 210, 120),
                "Турбо-буст вимкнено. На навчання, браузер, Office і відео майже не впливає, а процесор гріється помітно менше."),
            new ModeInfo("normal", "Звичайний", Color.FromArgb(201, 92, 0), Color.FromArgb(255, 170, 60),
                "Налаштування Windows як до першого запуску програми: повна потужність з турбо-бустом."),
            new ModeInfo("boost", "Прискорений", Color.FromArgb(215, 0, 21), Color.FromArgb(255, 105, 97),
                "Процесор не скидає частоту в простої, турбо-буст працює на повну. Програми відгукуються трохи швидше, але гарячіше й гучніше."),
            new ModeInfo("super", "Супер", Color.FromArgb(137, 68, 171), Color.FromArgb(200, 140, 255),
                "Як «Прискорений», плюс прихований план живлення «Максимальна продуктивність». Найшвидше, але найгарячіше; ноутбук швидше сідає."),
        };
        public static readonly string[] Keys = All.Select(m => m.Key).ToArray();

        public static ModeInfo Get(string key)
        {
            foreach (var m in All) if (m.Key == key) return m;
            return null;
        }

        public static string Title(string key)
        {
            var m = Get(key);
            return m != null ? m.Title : "невідомо";
        }

        public static int Index(string key) { return Array.IndexOf(Keys, key); }
    }

    static class Tool
    {
        public static int Run(string exe, string args, out string output, int timeoutMs)
        {
            output = "";
            try
            {
                var psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                using (var p = Process.Start(psi))
                {
                    var read = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        return -1;
                    }
                    output = read.Result;
                    return p.ExitCode;
                }
            }
            catch { return -1; }
        }

        public static bool Run(string exe, string args)
        {
            string o;
            return Run(exe, args, out o, 15000) == 0;
        }
    }

    class Pair
    {
        public int AC, DC;
        public Pair(int ac, int dc) { AC = ac; DC = dc; }
    }

    class PowerState
    {
        public string Scheme, Mode;
        public Pair Max, Min, Boost, Epp;
        public bool HasScheme, HasEpp;
    }

    static class Power
    {
        const string UltimateTemplate = "e9a42b02-d5df-448d-aa00-03f14749eb61";
        const string HighPerformance = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        public static readonly string StateDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TyhyiPK");
        static readonly string StateFile = Path.Combine(StateDir, "original.json");
        static readonly string UltimateFile = Path.Combine(StateDir, "ultimate.txt");
        static readonly Regex GuidRx = new Regex("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

        static int Run(string args, out string output) { return Tool.Run("powercfg.exe", args, out output, 15000); }
        static bool Run(string args) { return Tool.Run("powercfg.exe", args); }

        public static Pair Get(string alias)
        {
            string o;
            if (Run("/q SCHEME_CURRENT SUB_PROCESSOR " + alias, out o) != 0) return null;
            var ms = Regex.Matches(o, "0x[0-9a-fA-F]+");
            if (ms.Count < 2) return null;
            return new Pair((int)Convert.ToInt64(ms[ms.Count - 2].Value, 16), (int)Convert.ToInt64(ms[ms.Count - 1].Value, 16));
        }

        public static bool Set(string alias, int ac, int dc)
        {
            bool a = Run("/setacvalueindex SCHEME_CURRENT SUB_PROCESSOR " + alias + " " + ac);
            bool d = Run("/setdcvalueindex SCHEME_CURRENT SUB_PROCESSOR " + alias + " " + dc);
            return a && d;
        }

        static bool Set(string alias, Pair p) { return p != null ? Set(alias, p.AC, p.DC) : true; }

        static string ActiveScheme()
        {
            string o;
            if (Run("/getactivescheme", out o) != 0) return null;
            var m = GuidRx.Match(o);
            return m.Success ? m.Value.ToLowerInvariant() : null;
        }

        static List<string> Schemes()
        {
            var list = new List<string>();
            string o;
            if (Run("/list", out o) != 0) return list;
            foreach (Match m in GuidRx.Matches(o)) list.Add(m.Value.ToLowerInvariant());
            return list;
        }

        static bool SwitchTo(string scheme)
        {
            if (scheme == null || scheme == ActiveScheme()) return true;
            return Run("/setactive " + scheme);
        }

        static string FastScheme()
        {
            var list = Schemes();
            try
            {
                if (File.Exists(UltimateFile))
                {
                    string g = File.ReadAllText(UltimateFile).Trim().ToLowerInvariant();
                    if (list.Contains(g)) return g;
                }
            }
            catch { }
            if (list.Contains(UltimateTemplate)) return UltimateTemplate;
            string o;
            if (Run("-duplicatescheme " + UltimateTemplate, out o) == 0)
            {
                foreach (Match m in GuidRx.Matches(o))
                {
                    string g = m.Value.ToLowerInvariant();
                    if (g == UltimateTemplate) continue;
                    try { if (!Wipe.Done) { Directory.CreateDirectory(StateDir); File.WriteAllText(UltimateFile, g); } } catch { }
                    return g;
                }
            }
            if (list.Contains(HighPerformance)) return HighPerformance;
            return null;
        }

        public static bool HasSavedState { get { return File.Exists(StateFile); } }

        // Прибирає схему живлення «Максимальна продуктивність», яку створила програма.
        public static void RemoveOwnScheme()
        {
            try
            {
                if (!File.Exists(UltimateFile)) return;
                string g = File.ReadAllText(UltimateFile).Trim().ToLowerInvariant();
                if (g.Length == 0 || g == ActiveScheme()) return;
                Run("/delete " + g);
            }
            catch { }
        }

        static string J(Pair p) { return p != null ? "{ \"AC\": " + p.AC + ", \"DC\": " + p.DC + " }" : "null"; }
        static string Js(string s) { return s != null ? "\"" + s + "\"" : "null"; }

        static Pair ReadPair(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\\{\\s*\"AC\"\\s*:\\s*(\\d+)\\s*,\\s*\"DC\"\\s*:\\s*(\\d+)");
            return m.Success ? new Pair(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : null;
        }

        static string ReadString(string json, string key)
        {
            var m = Regex.Match(json, "\"" + key + "\"\\s*:\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : null;
        }

        static PowerState LoadState()
        {
            try
            {
                if (!File.Exists(StateFile)) return null;
                string j = File.ReadAllText(StateFile);
                var st = new PowerState();
                st.Scheme = ReadString(j, "Scheme");
                st.Mode = ReadString(j, "Mode");
                st.Max = ReadPair(j, "Max");
                st.Min = ReadPair(j, "Min");
                st.Boost = ReadPair(j, "Boost");
                st.Epp = ReadPair(j, "Epp");
                st.HasScheme = j.Contains("\"Scheme\"");
                st.HasEpp = j.Contains("\"Epp\"");
                return st;
            }
            catch { return null; }
        }

        static void SaveState(PowerState st)
        {
            if (Wipe.Done) return;
            try
            {
                Directory.CreateDirectory(StateDir);
                File.WriteAllText(StateFile, "{\r\n  \"Scheme\": " + Js(st.Scheme) + ",\r\n  \"Mode\": " + Js(st.Mode) +
                    ",\r\n  \"Max\": " + J(st.Max) + ",\r\n  \"Min\": " + J(st.Min) + ",\r\n  \"Boost\": " + J(st.Boost) +
                    ",\r\n  \"Epp\": " + J(st.Epp) + "\r\n}\r\n");
            }
            catch { }
        }

        static PowerState EnsureState()
        {
            var st = LoadState();
            if (st == null)
            {
                st = new PowerState();
                st.Scheme = ActiveScheme();
                st.Max = Get("PROCTHROTTLEMAX");
                st.Min = Get("PROCTHROTTLEMIN");
                st.Boost = Get("PERFBOOSTMODE");
                st.Epp = Get("PERFEPP");
                st.Mode = "normal";
                SaveState(st);
                return st;
            }
            bool changed = false;
            if (!st.HasScheme) { st.Scheme = ActiveScheme(); changed = true; }
            if (!st.HasEpp) { st.Epp = Get("PERFEPP"); changed = true; }
            if (changed) SaveState(st);
            return st;
        }

        static int MinFor(Pair origMin, bool ac, int target)
        {
            if (origMin == null) return 5;
            int v = ac ? origMin.AC : origMin.DC;
            return v >= target ? 5 : v;
        }

        public static bool Apply(string mode)
        {
            bool ok;
            if (mode == "normal")
            {
                var st0 = LoadState();
                if (st0 == null)
                {
                    ok = Set("PROCTHROTTLEMAX", 100, 100);
                    var b = Get("PERFBOOSTMODE");
                    if (b != null && (b.AC == 0 || b.DC == 0)) Set("PERFBOOSTMODE", 2, 2);
                }
                else
                {
                    ok = SwitchTo(st0.Scheme);
                    if (st0.Max != null) ok = Set("PROCTHROTTLEMAX", st0.Max) && ok;
                    else ok = Set("PROCTHROTTLEMAX", 100, 100) && ok;
                    Set("PROCTHROTTLEMIN", st0.Min);
                    Set("PERFBOOSTMODE", st0.Boost);
                    Set("PERFEPP", st0.Epp);
                }
                Run("/setactive SCHEME_CURRENT");
                if (ok) { try { File.Delete(StateFile); } catch { } }
                return ok;
            }

            var st = EnsureState();
            if (mode == "super")
            {
                string fast = FastScheme();
                if (fast != null) SwitchTo(fast);
                ok = Set("PROCTHROTTLEMAX", 100, 100);
                Set("PROCTHROTTLEMIN", 100, 100);
                Set("PERFBOOSTMODE", 2, 2);
                Set("PERFEPP", 0, 0);
            }
            else
            {
                ok = SwitchTo(st.Scheme);
                if (mode == "boost")
                {
                    ok = Set("PROCTHROTTLEMAX", 100, 100) && ok;
                    Set("PROCTHROTTLEMIN", 100, 100);
                    Set("PERFBOOSTMODE", 2, 2);
                    Set("PERFEPP", 0, 0);
                }
                else
                {
                    int max = mode == "quiet" ? 99 : 70;
                    ok = Set("PROCTHROTTLEMAX", max, max) && ok;
                    Set("PROCTHROTTLEMIN", MinFor(st.Min, true, max), MinFor(st.Min, false, max));
                    Set("PERFBOOSTMODE", 0, 0);
                    Set("PERFEPP", st.Epp);
                }
            }
            Run("/setactive SCHEME_CURRENT");
            if (ok) { st.Mode = mode; SaveState(st); }
            return ok;
        }

        public static string CurrentMode()
        {
            var st = LoadState();
            if (st == null) return "normal";
            if (st.Mode != null && Modes.Get(st.Mode) != null) return st.Mode;
            var max = Get("PROCTHROTTLEMAX");
            var boost = Get("PERFBOOSTMODE");
            if (max == null) return "normal";
            if (max.AC <= 80) return "silent";
            if (max.AC < 100 || (boost != null && boost.AC == 0)) return "quiet";
            return "normal";
        }
    }
}
