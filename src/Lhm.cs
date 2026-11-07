// Тихий ПК — датчики через LibreHardwareMonitor: бібліотека йде разом із програмою (ресурси «lhm.*.dll»),
// при встановленні розкладається звичайними файлами в папку lib і підключається з диска через рефлексію.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Management;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TyhyiPK
{
    class LhmData
    {
        public double CpuTemp = double.NaN, GpuTemp = double.NaN, CpuPower = double.NaN, Fan = double.NaN, GpuPower = double.NaN;
        public bool FanIsCpu;
        public int FanCount;
    }

    static class Lhm
    {
        public const string Url = "https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/releases/latest";
        public static string Dir, Error;
        public static volatile bool Loaded, Tried, Embedded;
        static Dictionary<string, string> embeddedRes;   // ім'я збірки → ім'я ресурсу

        // Чи вшита бібліотека в цей файл програми.
        public static bool HasEmbedded
        {
            get
            {
                if (embeddedRes == null)
                {
                    embeddedRes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    try
                    {
                        foreach (string r in Assembly.GetExecutingAssembly().GetManifestResourceNames())
                            if (r.StartsWith("lhm.", StringComparison.OrdinalIgnoreCase) && r.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                                embeddedRes[r.Substring(4, r.Length - 8)] = r;
                    }
                    catch { }
                }
                return embeddedRes.ContainsKey("LibreHardwareMonitorLib");
            }
        }

        // Бібліотеки, вшиті в програму, розкладаються звичайними файлами в папку lib
        // і підключаються з диска (не з пам'яті) — так їх видно антивірусу й людині.
        public static string LibDir
        {
            get
            {
                string local = Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "lib");
                if (File.Exists(Path.Combine(local, "LibreHardwareMonitorLib.dll"))) return local;
                return Path.Combine(Installer.Dir, "lib");
            }
        }

        // Розкласти вшиті бібліотеки в папку dir (лише ті, яких там немає або які змінились).
        public static bool Extract(string dir)
        {
            if (!HasEmbedded) return false;
            try
            {
                Directory.CreateDirectory(dir);
                var me = Assembly.GetExecutingAssembly();
                foreach (var kv in embeddedRes)
                {
                    string path = Path.Combine(dir, kv.Key + ".dll");
                    using (var s = me.GetManifestResourceStream(kv.Value))
                    {
                        if (s == null) continue;
                        if (File.Exists(path) && new FileInfo(path).Length == s.Length) continue;
                        using (var f = File.Create(path)) s.CopyTo(f);
                    }
                }
                using (var r = me.GetManifestResourceStream("lhm.LICENSE.txt"))
                    if (r != null)
                        using (var f = File.Create(Path.Combine(dir, "LibreHardwareMonitor_LICENSE.txt"))) r.CopyTo(f);
                return File.Exists(Path.Combine(dir, "LibreHardwareMonitorLib.dll"));
            }
            catch { return false; }
        }

        static object computer;
        static PropertyInfo hwProp;
        static readonly HashSet<string> resolving = new HashSet<string>();

        public static string FindDir()
        {
            var list = new List<string>();
            string saved = Settings.Get("LhmDir", null);
            if (!string.IsNullOrEmpty(saved)) list.Add(saved);
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            list.Add(exeDir);
            string[] roots =
            {
                exeDir,
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            };
            foreach (string root in roots)
            {
                try
                {
                    foreach (string d in Directory.GetDirectories(root, "*LibreHardwareMonitor*"))
                    {
                        list.Add(d);
                        try { list.AddRange(Directory.GetDirectories(d)); } catch { }
                    }
                }
                catch { }
            }
            // Спершу версія для .NET Framework (без *.runtimeconfig.json), потім будь-яка.
            foreach (string d in list)
            {
                try
                {
                    if (File.Exists(Path.Combine(d, "LibreHardwareMonitorLib.dll")) && Directory.GetFiles(d, "*.runtimeconfig.json").Length == 0)
                        return d;
                }
                catch { }
            }
            foreach (string d in list)
                if (File.Exists(Path.Combine(d, "LibreHardwareMonitorLib.dll"))) return d;
            return null;
        }

        public static bool PawnIoInstalled()
        {
            try
            {
                using (var hk = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (var k = hk.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\PawnIO"))
                    return k != null;
            }
            catch { return false; }
        }

        public static void Init()
        {
            try
            {
                try
                {
                    Assembly asm = null;
                    AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                    if (HasEmbedded)
                    {
                        string lib = LibDir;
                        if (Extract(lib))
                        {
                            Dir = lib;
                            Embedded = true;
                            asm = Assembly.UnsafeLoadFrom(Path.Combine(lib, "LibreHardwareMonitorLib.dll"));
                        }
                    }
                    if (asm == null)
                    {
                        Dir = FindDir();
                        if (Dir == null) { Error = "nolib"; return; }
                        asm = Assembly.UnsafeLoadFrom(Path.Combine(Dir, "LibreHardwareMonitorLib.dll"));
                    }
                    var t = asm.GetType("LibreHardwareMonitor.Hardware.Computer", true);
                    computer = Activator.CreateInstance(t);
                    t.GetProperty("IsCpuEnabled").SetValue(computer, true, null);
                    t.GetProperty("IsGpuEnabled").SetValue(computer, true, null);
                    t.GetProperty("IsMotherboardEnabled").SetValue(computer, true, null);
                    t.GetMethod("Open", Type.EmptyTypes).Invoke(computer, null);
                    hwProp = t.GetProperty("Hardware");
                    Loaded = true;
                }
                catch (Exception ex)
                {
                    var e = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                    Error = e.Message;
                }
            }
            finally { Tried = true; }
        }

        static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name;
            if (Dir == null) return null;
            string path = Path.Combine(Dir, name + ".dll");
            if (!File.Exists(path)) return null;
            lock (resolving) { if (!resolving.Add(name)) return null; }
            try
            {
                try { return Assembly.UnsafeLoadFrom(path); }
                catch { return null; }
            }
            finally { lock (resolving) resolving.Remove(name); }
        }

        static object Prop(object o, string name)
        {
            var p = o.GetType().GetProperty(name);
            return p != null ? p.GetValue(o, null) : null;
        }

        static int CpuRank(string n)
        {
            switch (n)
            {
                case "CPU Package": return 0;
                case "Core (Tctl/Tdie)": return 1;
                case "Core (Tdie)": return 2;
                case "Core (Tctl)": return 3;
                case "Core Max": return 4;
                default: return 6;
            }
        }

        static void Update(object hw)
        {
            var m = hw.GetType().GetMethod("Update", Type.EmptyTypes);
            if (m != null) m.Invoke(hw, null);
            var subs = Prop(hw, "SubHardware") as IEnumerable;
            if (subs != null) foreach (object s in subs) Update(s);
        }

        static IEnumerable<object> Sensors(object hw)
        {
            var own = Prop(hw, "Sensors") as IEnumerable;
            if (own != null) foreach (object s in own) yield return s;
            var subs = Prop(hw, "SubHardware") as IEnumerable;
            if (subs != null)
                foreach (object sub in subs)
                    foreach (object s in Sensors(sub)) yield return s;
        }

        public static LhmData Read()
        {
            var d = new LhmData();
            if (!Loaded) return d;
            try
            {
                var hws = hwProp.GetValue(computer, null) as IEnumerable;
                if (hws == null) return d;
                foreach (object hw in hws)
                {
                    string type = Convert.ToString(Prop(hw, "HardwareType"));
                    bool isCpu = type == "Cpu", isGpu = type.StartsWith("Gpu"), isBoard = type == "Motherboard";
                    if (!(isCpu || isGpu || isBoard)) continue;
                    Update(hw);
                    double best = double.NaN;
                    int bestRank = 99;
                    foreach (object s in Sensors(hw))
                    {
                        object val = Prop(s, "Value");
                        if (val == null) continue;
                        double v = Convert.ToDouble(val);
                        string st = Convert.ToString(Prop(s, "SensorType"));
                        string name = Convert.ToString(Prop(s, "Name"));
                        if (isBoard)
                        {
                            if (st == "Fan" && v > 0)
                            {
                                d.FanCount++;
                                bool cpuFan = name.IndexOf("CPU", StringComparison.OrdinalIgnoreCase) >= 0;
                                if (double.IsNaN(d.Fan) || (cpuFan && !d.FanIsCpu)) { d.Fan = v; d.FanIsCpu = cpuFan; }
                            }
                            continue;
                        }
                        if (isCpu && st == "Power" && (name == "Package" || name == "CPU Package") && v > 0) d.CpuPower = v;
                        // Дискретна карта (вбудована Intel уже входить у споживання процесора).
                        if (isGpu && type != "GpuIntel" && st == "Power" && (name == "GPU Package" || name == "GPU Power" || name == "GPU Board Power") && v > 0 && v < 1000)
                            d.GpuPower = double.IsNaN(d.GpuPower) ? v : Math.Max(d.GpuPower, v);
                        if (!(st == "Temperature" && v > 1 && v <= 125)) continue;
                        int rank = isCpu ? CpuRank(name) : name == "GPU Core" ? 0 : name == "GPU Hot Spot" ? 2 : 5;
                        if (rank < bestRank || (rank == bestRank && v > best)) { best = v; bestRank = rank; }
                    }
                    if (double.IsNaN(best)) continue;
                    if (isCpu && (double.IsNaN(d.CpuTemp) || best > d.CpuTemp)) d.CpuTemp = best;
                    if (isGpu && double.IsNaN(d.GpuTemp)) d.GpuTemp = best;
                }
            }
            catch { }
            return d;
        }

        // Температура з термозон ACPI (без LibreHardwareMonitor), °C або NaN.
        public static double ReadBoard()
        {
            double best = -1;
            try
            {
                using (var q = new ManagementObjectSearcher("root\\cimv2", "SELECT * FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                foreach (ManagementObject mo in q.Get())
                {
                    using (mo)
                    {
                        double k = -1;
                        try { k = Convert.ToDouble(mo["HighPrecisionTemperature"]) / 10; } catch { }
                        if (!(k > 0)) { try { k = Convert.ToDouble(mo["Temperature"]); } catch { } }
                        double c = k - 273.15;
                        if (c > 1 && c < 125 && c > best) best = c;
                    }
                }
            }
            catch { }
            return best > 0 ? best : double.NaN;
        }
    }
}
