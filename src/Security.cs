// Тихий ПК — вкладка «Безпека»: антивірус, детектор майнерів, мережа, автозапуск, здоров'я дисків.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace TyhyiPK
{
    static class Native
    {
        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")]
        public static extern bool CloseHandle(IntPtr h);

        [StructLayout(LayoutKind.Sequential)]
        struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
        [DllImport("user32.dll")]
        static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("iphlpapi.dll", SetLastError = true)]
        public static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int af, int tableClass, int reserved);

        public static string ProcessPath(int pid)
        {
#if LINUXTEST
            return null;
#endif
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        // Скільки секунд користувач нічого не натискав і не рухав мишею.
        public static double IdleSeconds()
        {
            var li = new LASTINPUTINFO();
            li.cbSize = (uint)Marshal.SizeOf(typeof(LASTINPUTINFO));
            if (!GetLastInputInfo(ref li)) return 0;
            return unchecked((uint)Environment.TickCount - li.dwTime) / 1000.0;
        }

        public static void ShowInFolder(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                if (File.Exists(path)) Process.Start("explorer.exe", "/select,\"" + path + "\"");
                else if (Directory.Exists(path)) Process.Start("explorer.exe", "\"" + path + "\"");
            }
            catch { }
        }
    }

    // Перевірка цифрового підпису файлу (лише локально, без звернення до інтернету).
    static class Sig
    {
        static readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WINTRUST_FILE_INFO { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

        [StructLayout(LayoutKind.Sequential)]
        struct WINTRUST_DATA
        {
            public uint cbStruct; public IntPtr pPolicyCallbackData; public IntPtr pSIPClientData; public uint dwUIChoice;
            public uint fdwRevocationChecks; public uint dwUnionChoice; public IntPtr pFile; public uint dwStateAction;
            public IntPtr hWVTStateData; public IntPtr pwszURLReference; public uint dwProvFlags; public uint dwUIContext; public IntPtr pSignatureSettings;
        }

        [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
        static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);

        static bool Verify(string path)
        {
            var fi = new WINTRUST_FILE_INFO();
            fi.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_FILE_INFO));
            fi.pcwszFilePath = path;
            IntPtr pFile = Marshal.AllocHGlobal(Marshal.SizeOf(fi));
            IntPtr pData = IntPtr.Zero;
            try
            {
                Marshal.StructureToPtr(fi, pFile, false);
                var wd = new WINTRUST_DATA();
                wd.cbStruct = (uint)Marshal.SizeOf(typeof(WINTRUST_DATA));
                wd.dwUIChoice = 2;           // без вікон
                wd.fdwRevocationChecks = 0;  // без перевірки відкликання
                wd.dwUnionChoice = 1;        // файл
                wd.pFile = pFile;
                wd.dwStateAction = 0;
                wd.dwProvFlags = 0x1000;     // лише кеш, без мережі
                pData = Marshal.AllocHGlobal(Marshal.SizeOf(wd));
                Marshal.StructureToPtr(wd, pData, false);
                var action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
                return WinVerifyTrust(new IntPtr(-1), action, pData) == 0;
            }
            catch { return false; }
            finally
            {
                Marshal.FreeHGlobal(pFile);
                if (pData != IntPtr.Zero) Marshal.FreeHGlobal(pData);
            }
        }

        public static bool IsWindowsFile(string path)
        {
            return path != null && path.StartsWith(WinDir + "\\", StringComparison.OrdinalIgnoreCase);
        }

        // Назва видавця, "" — немає підпису, "!" — підпис недійсний.
        public static string Publisher(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            lock (cache)
            {
                string v;
                if (cache.TryGetValue(path, out v)) return v;
            }
            string res = "";
            try
            {
                var cert = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                res = Verify(path) ? cert.GetNameInfo(X509NameType.SimpleName, false) : "!";
            }
            catch
            {
                // Системні файли Windows підписані через каталог, без вбудованого підпису.
                res = IsWindowsFile(path) && Verify(path) ? "Microsoft Windows" : IsWindowsFile(path) ? "Windows" : "";
            }
            lock (cache) cache[path] = res;
            return res;
        }

        public static string Describe(string publisher)
        {
            if (publisher == "!") return "підпис недійсний";
            if (publisher == "") return "без цифрового підпису";
            return "підпис: " + publisher;
        }
    }

    // ── Антивірус і брандмауер ──
    class DefenderStatus
    {
        public string Antivirus = "невідомо";
        public bool? RealTime, AvEnabled, Firewall, Tamper;
        public int SigAgeDays = -1, QuickScanAgeDays = -1, FullScanAgeDays = -1;
        public bool Passive;
        public string Error;

        public static DefenderStatus Read()
        {
            var st = new DefenderStatus();
            var others = new List<string>();
            try
            {
                using (var q = new ManagementObjectSearcher("root\\SecurityCenter2", "SELECT displayName, productState FROM AntiVirusProduct"))
                foreach (ManagementObject mo in q.Get())
                {
                    using (mo)
                    {
                        string name = Convert.ToString(mo["displayName"]);
                        uint ps = Convert.ToUInt32(mo["productState"]);
                        string hex = ps.ToString("X6");
                        bool on = hex.Substring(2, 2) == "10" || hex.Substring(2, 2) == "11";
                        if (on && name.IndexOf("Defender", StringComparison.OrdinalIgnoreCase) < 0) others.Add(name);
                    }
                }
            }
            catch { }
            try
            {
                using (var q = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Defender", "SELECT * FROM MSFT_MpComputerStatus"))
                foreach (ManagementObject mo in q.Get())
                {
                    using (mo)
                    {
                        st.AvEnabled = Bool(mo, "AntivirusEnabled");
                        st.RealTime = Bool(mo, "RealTimeProtectionEnabled");
                        st.Tamper = Bool(mo, "IsTamperProtected");
                        st.SigAgeDays = Age(mo, "AntivirusSignatureAge");
                        st.QuickScanAgeDays = Age(mo, "QuickScanAge");
                        st.FullScanAgeDays = Age(mo, "FullScanAge");
                        string mode = Convert.ToString(mo["AMRunningMode"] ?? "");
                        st.Passive = mode.IndexOf("Passive", StringComparison.OrdinalIgnoreCase) >= 0 || mode.IndexOf("Disabled", StringComparison.OrdinalIgnoreCase) >= 0;
                    }
                }
            }
            catch (Exception ex) { st.Error = ex.Message; }
            if (others.Count > 0) st.Antivirus = string.Join(", ", others) + (st.Passive || st.AvEnabled != true ? "" : " + Defender");
            else if (st.AvEnabled.HasValue) st.Antivirus = "Microsoft Defender";
            st.Firewall = FirewallOn();
            return st;
        }

        static bool? Bool(ManagementObject mo, string p)
        {
            try { object v = mo[p]; return v == null ? (bool?)null : Convert.ToBoolean(v); } catch { return null; }
        }

        static int Age(ManagementObject mo, string p)
        {
            try
            {
                object v = mo[p];
                if (v == null) return -1;
                long d = Convert.ToInt64(v);
                return d < 0 || d > 100000 ? -1 : (int)d;
            }
            catch { return -1; }
        }

        // Брандмауер Windows: увімкнений для поточного мережевого профілю?
        static bool? FirewallOn()
        {
            try
            {
                var t = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
                object fw = Activator.CreateInstance(t);
                int profiles = (int)t.InvokeMember("CurrentProfileTypes", System.Reflection.BindingFlags.GetProperty, null, fw, null);
                bool all = true;
                foreach (int p in new[] { 1, 2, 4 })
                {
                    if ((profiles & p) == 0) continue;
                    bool on = (bool)t.InvokeMember("FirewallEnabled", System.Reflection.BindingFlags.GetProperty, null, fw, new object[] { p });
                    all &= on;
                }
                Marshal.ReleaseComObject(fw);
                return all;
            }
            catch { return null; }
        }

        public static string MpCmdRun()
        {
            string p = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Windows Defender\\MpCmdRun.exe");
            return File.Exists(p) ? p : null;
        }
    }

    // ── Детектор прихованих майнерів ──
    class Finding
    {
        public int Pid;
        public string Name, Path, Publisher;
        public double IdleCpu;            // середнє навантаження на весь процесор, коли ПК простоює, %
        public List<string> Reasons = new List<string>();
        public int Level;                 // 0 — до відома, 1 — підозріло, 2 — небезпечно
    }

    class MinerWatch
    {
        class Track { public string Name; public double LastMs; public int IdleSamples; public double IdleSum; }

        static readonly string[] MinerWords =
        {
            "xmrig", "xmr-stak", "nicehash", "excavator", "phoenixminer", "t-rex", "trex", "nbminer", "lolminer", "gminer", "ethminer",
            "cpuminer", "ccminer", "nanominer", "teamredminer", "srbminer", "bminer", "claymore", "minerd", "minergate", "cgminer", "bfgminer",
            "kawpowminer", "rigel", "onezerominer", "wildrig",
        };
        // Ці процеси Windows можуть законно навантажувати ПК у простої (оновлення, індексація, перевірка антивірусом).
        static readonly HashSet<string> IdleOk = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "MsMpEng", "MpDefenderCoreService", "NisSrv", "TiWorker", "TrustedInstaller", "MoUsoCoreWorker", "usocoreworker", "wuauclt",
            "SearchIndexer", "SearchProtocolHost", "SearchFilterHost", "CompatTelRunner", "sppsvc", "MusNotification", "WmiPrvSE",
            "dllhost", "svchost", "System", "Registry", "Memory Compression", "Idle", "dwm", "csrss", "audiodg", "WaaSMedicAgent",
            "MpCmdRun", "SgrmBroker", "OneDrive", "OneDrive.Sync.Service", "steamwebhelper", "ngen", "ngentask", "mscorsvw",
        };
        // Системні імена, під які часто маскуються майнери.
        static readonly string[] SystemNames = { "svchost", "csrss", "lsass", "winlogon", "services", "smss", "dwm", "explorer", "conhost", "taskhostw", "rundll32", "dllhost" };
        public static readonly int[] PoolPorts = { 3333, 4444, 5555, 7777, 14433, 14444, 45560, 45700, 20535, 10128, 1800, 2222 };

        readonly Dictionary<int, Track> tracks = new Dictionary<int, Track>();
        DateTime lastTime = DateTime.MinValue;
        readonly int cores = Environment.ProcessorCount;
        readonly int selfPid = Process.GetCurrentProcess().Id;
        public double IdleSec;
        public List<Finding> Findings = new List<Finding>();

        public const double IdleAfterSec = 120, CpuThreshold = 15;

        // Один замір (приблизно раз на 10 с). Повертає список знахідок.
        public List<Finding> Sample()
        {
            IdleSec = Native.IdleSeconds();
            bool idle = IdleSec >= IdleAfterSec;
            var cpuNow = Measure();
            foreach (var kv in cpuNow)
            {
                var t = tracks[kv.Key];
                if (idle) { t.IdleSamples++; t.IdleSum += kv.Value; }
                else { t.IdleSamples = 0; t.IdleSum = 0; }
            }
            HashSet<int> gpuPids = null;
            Dictionary<int, int> pool = null;
            try { if (idle) gpuPids = NvidiaComputePids(); } catch { }
            try { pool = Net.PoolConnections(); } catch { }
            var res = new List<Finding>();
            foreach (var kv in tracks)
            {
                var t = kv.Value;
                bool heavy = t.IdleSamples >= 6 && t.IdleSum / t.IdleSamples >= CpuThreshold;
                bool gpu = gpuPids != null && gpuPids.Contains(kv.Key) && t.IdleSamples >= 6;
                var f = Evaluate(kv.Key, t.Name, t.IdleSamples > 0 ? t.IdleSum / t.IdleSamples : 0, heavy, gpu, pool, true);
                if (f != null) res.Add(f);
            }
            Findings = res.OrderByDescending(f => f.Level).ThenByDescending(f => f.IdleCpu).ToList();
            return Findings;
        }

        // Перевірка «просто зараз», без очікування простою: хто вантажить ПК саме в цю мить.
        public List<Finding> CheckNow()
        {
            Measure();
            System.Threading.Thread.Sleep(2000);
            var cpuNow = Measure();
            HashSet<int> gpuPids = null;
            Dictionary<int, int> pool = null;
            try { gpuPids = NvidiaComputePids(); } catch { }
            try { pool = Net.PoolConnections(); } catch { }
            var res = new List<Finding>();
            foreach (var kv in tracks)
            {
                double cpu;
                cpuNow.TryGetValue(kv.Key, out cpu);
                bool heavy = cpu >= CpuThreshold;
                bool gpu = gpuPids != null && gpuPids.Contains(kv.Key);
                var f = Evaluate(kv.Key, kv.Value.Name, cpu, heavy, gpu, pool, false);
                if (f != null) res.Add(f);
            }
            return res.OrderByDescending(f => f.Level).ThenByDescending(f => f.IdleCpu).ToList();
        }

        // Навантаження кожного процесу на весь процесор (%) з попереднього заміру.
        Dictionary<int, double> Measure()
        {
            var now = DateTime.UtcNow;
            double dt = lastTime == DateTime.MinValue ? 0 : (now - lastTime).TotalMilliseconds;
            lastTime = now;
            var alive = new HashSet<int>();
            var cpuNow = new Dictionary<int, double>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    int id = p.Id;
                    if (id <= 4 || id == selfPid) continue;
                    double ms;
                    string name;
                    try { name = p.ProcessName; ms = p.TotalProcessorTime.TotalMilliseconds; }
                    catch { continue; }
                    alive.Add(id);
                    Track t;
                    if (!tracks.TryGetValue(id, out t) || t.Name != name) { tracks[id] = new Track { Name = name, LastMs = ms }; continue; }
                    cpuNow[id] = dt > 0 && dt < 60000 ? Math.Max(0, ms - t.LastMs) / dt / cores * 100 : 0;
                    t.LastMs = ms;
                }
            }
            foreach (int k in tracks.Keys.Where(k => !alive.Contains(k)).ToList()) tracks.Remove(k);
            return cpuNow;
        }

        Finding Evaluate(int pid, string name, double cpu, bool heavy, bool gpu, Dictionary<int, int> pool, bool idleMode)
        {
            string lname = name.ToLowerInvariant();
            bool known = MinerWords.Any(w => lname.Contains(w));
            int poolConn = 0;
            bool hasPool = pool != null && pool.TryGetValue(pid, out poolConn);
            bool fakeCandidate = SystemNames.Contains(lname);
            if (!(known || heavy || gpu || hasPool || fakeCandidate)) return null;

            var f = new Finding { Pid = pid, Name = name, IdleCpu = cpu };
            f.Path = Native.ProcessPath(pid);
            bool fakeSystem = fakeCandidate && f.Path != null && !Sig.IsWindowsFile(f.Path);
            if (!(known || heavy || gpu || hasPool || fakeSystem)) return null;
            f.Publisher = Sig.Publisher(f.Path);
            bool signedOk = f.Publisher != "" && f.Publisher != "!";
            string lp = (f.Path ?? "").ToLowerInvariant();
            bool oddPlace = lp.Contains("\\temp\\") || lp.Contains("\\appdata\\") || lp.Contains("\\programdata\\") || lp.Contains("\\users\\public\\");

            if (known) { f.Reasons.Add("назва як у відомого майнера"); f.Level = 2; }
            if (fakeSystem) { f.Reasons.Add("видає себе за системний процес, але лежить не в папці Windows"); f.Level = 2; }
            if (heavy) f.Reasons.Add("вантажить процесор на " + Math.Round(cpu) + "%" + (idleMode ? ", коли ти не користуєшся ПК" : ""));
            if (gpu) f.Reasons.Add(idleMode ? "рахує на відеокарті у простої" : "рахує на відеокарті");
            if (hasPool) { f.Reasons.Add("з'єднання з портом, типовим для майнінг-пулів"); f.Level = Math.Max(f.Level, 1); }
            if (!signedOk) f.Reasons.Add(Sig.Describe(f.Publisher));
            if (oddPlace && (heavy || gpu)) f.Reasons.Add("запущено зі службової папки користувача");

            if (f.Level < 2)
            {
                if (IdleOk.Contains(name) && Sig.IsWindowsFile(f.Path) && !hasPool) return null;   // звичайна фонова робота Windows
                if (!idleMode && signedOk && !hasPool) return null;   // підписані програми, що просто працюють зараз
                if ((heavy || gpu) && !signedOk) f.Level = Math.Max(f.Level, 1);
                if ((heavy || gpu) && hasPool) f.Level = 2;
                if ((heavy || gpu) && oddPlace && !signedOk) f.Level = 2;
            }
            return f;
        }

        // Процеси, які рахують на відеокарті NVIDIA (CUDA/compute) — так працюють GPU-майнери.
        public static HashSet<int> NvidiaComputePids()
        {
            var set = new HashSet<int>();
            if (Gpu.Smi == null) return set;
            string o;
            if (Tool.Run(Gpu.Smi, "--query-compute-apps=pid --format=csv,noheader", out o, 5000) != 0) return set;
            foreach (string line in o.Split('\n'))
            {
                int pid;
                if (int.TryParse(line.Trim(), out pid)) set.Add(pid);
            }
            return set;
        }
    }

    // ── Мережеві з'єднання програм ──
    class Conn
    {
        public int Pid;
        public IPAddress Remote;
        public int RemotePort, State;
    }

    static class Net
    {
        public static List<Conn> Tcp()
        {
            var list = new List<Conn>();
            Read(2, list);
            Read(23, list);
            return list;
        }

        static void Read(int af, List<Conn> list)
        {
            int size = 0;
            Native.GetExtendedTcpTable(IntPtr.Zero, ref size, false, af, 5, 0);
            if (size <= 0) return;
            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                if (Native.GetExtendedTcpTable(buf, ref size, false, af, 5, 0) != 0) return;
                int n = Marshal.ReadInt32(buf);
                IntPtr row = buf + 4;
                for (int i = 0; i < n; i++)
                {
                    var c = new Conn();
                    if (af == 2)
                    {
                        c.State = Marshal.ReadInt32(row);
                        c.Remote = new IPAddress((uint)Marshal.ReadInt32(row + 12) & 0xFFFFFFFFL);
                        c.RemotePort = Port(Marshal.ReadInt32(row + 16));
                        c.Pid = Marshal.ReadInt32(row + 20);
                        row += 24;
                    }
                    else
                    {
                        var addr = new byte[16];
                        Marshal.Copy(row + 24, addr, 0, 16);
                        c.Remote = new IPAddress(addr);
                        c.RemotePort = Port(Marshal.ReadInt32(row + 44));
                        c.State = Marshal.ReadInt32(row + 48);
                        c.Pid = Marshal.ReadInt32(row + 52);
                        row += 56;
                    }
                    list.Add(c);
                }
            }
            finally { Marshal.FreeHGlobal(buf); }
        }

        static int Port(int raw) { return ((raw & 0xFF) << 8) | ((raw >> 8) & 0xFF); }

        public static bool IsLocal(IPAddress a)
        {
            if (IPAddress.IsLoopback(a) || a.Equals(IPAddress.Any) || a.Equals(IPAddress.IPv6Any)) return true;
            if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
            var b = a.GetAddressBytes();
            if (b.Length == 4) return b[0] == 10 || b[0] == 127 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] >= 16 && b[1] < 32) || (b[0] == 169 && b[1] == 254);
            return a.IsIPv6LinkLocal || a.IsIPv6SiteLocal || (b[0] & 0xFE) == 0xFC;
        }

        // Скільки з'єднань кожного процесу йде на «пульні» порти.
        public static Dictionary<int, int> PoolConnections()
        {
            var d = new Dictionary<int, int>();
            foreach (var c in Tcp())
            {
                if (c.State != 5 || !MinerWatch.PoolPorts.Contains(c.RemotePort) || IsLocal(c.Remote)) continue;
                int n;
                d.TryGetValue(c.Pid, out n);
                d[c.Pid] = n + 1;
            }
            return d;
        }
    }

    class NetApp
    {
        public int Pid, Count;
        public string Name, Path, Publisher;
        public List<string> Remotes = new List<string>();
        public bool PoolPort;
    }

    // ── Автозапуск ──
    class AutoItem
    {
        public string Source, Name, Command, Path, Key, Publisher;
        public bool Disabled, IsNew;
        public string Where
        {
            get
            {
                if (Source.StartsWith("startup")) return Source == "startup-user" ? "папка «Автозавантаження»" : "спільна папка «Автозавантаження»";
                return (Source.StartsWith("HKLM") ? "реєстр, усі користувачі" : "реєстр, ти") + (Source.EndsWith("Once") ? " (один раз)" : "");
            }
        }
    }

    static class Autoruns
    {
        static readonly string[][] Keys =
        {
            new[] { "HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Run" },
            new[] { "HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce" },
            new[] { "HKLM", "Software\\Microsoft\\Windows\\CurrentVersion\\Run" },
            new[] { "HKLM", "Software\\Microsoft\\Windows\\CurrentVersion\\RunOnce" },
            new[] { "HKLM32", "Software\\WOW6432Node\\Microsoft\\Windows\\CurrentVersion\\Run" },
        };
        const string DisabledKey = "Software\\TyhyiPK\\DisabledAutorun";
        static readonly string KnownFile = Path.Combine(Power.StateDir, "autostart_known.txt");
        static readonly string DisabledDir = Path.Combine(Power.StateDir, "DisabledStartup");

        static string SourceName(string[] k) { return k[0] + (k[1].EndsWith("RunOnce") ? "-RunOnce" : "-Run"); }

        static RegistryKey Root(string hive, bool write)
        {
            var baseKey = hive == "HKCU" ? RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                : RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            return baseKey;
        }

        static string StartupDir(bool common)
        {
            return Environment.GetFolderPath(common ? Environment.SpecialFolder.CommonStartup : Environment.SpecialFolder.Startup);
        }

        public static List<AutoItem> Read()
        {
            var list = new List<AutoItem>();
            foreach (var k in Keys)
            {
                try
                {
                    using (var root = Root(k[0], false))
                    using (var key = root.OpenSubKey(k[1]))
                    {
                        if (key == null) continue;
                        foreach (string name in key.GetValueNames())
                        {
                            string cmd = Convert.ToString(key.GetValue(name));
                            if (string.IsNullOrWhiteSpace(cmd)) continue;
                            list.Add(Item(SourceName(k) + (k[0] == "HKLM32" ? "32" : ""), name, cmd, false));
                        }
                    }
                }
                catch { }
            }
            foreach (bool common in new[] { false, true })
            {
                try
                {
                    string dir = StartupDir(common);
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        if (System.IO.Path.GetFileName(f).Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                        list.Add(Item(common ? "startup-common" : "startup-user", System.IO.Path.GetFileNameWithoutExtension(f), f, false));
                    }
                }
                catch { }
            }
            // Вимкнені цією програмою.
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(DisabledKey))
                {
                    if (k != null)
                        foreach (string sub in k.GetSubKeyNames())
                            using (var s = k.OpenSubKey(sub))
                                foreach (string name in s.GetValueNames())
                                    list.Add(Item(sub, name, Convert.ToString(s.GetValue(name)), true));
                }
            }
            catch { }
            foreach (bool common in new[] { false, true })
            {
                try
                {
                    string dir = System.IO.Path.Combine(DisabledDir, common ? "common" : "user");
                    if (Directory.Exists(dir))
                        foreach (string f in Directory.GetFiles(dir))
                            list.Add(Item(common ? "startup-common" : "startup-user", System.IO.Path.GetFileNameWithoutExtension(f), f, true));
                }
                catch { }
            }
            var known = LoadKnown();
            if (known == null)
            {
                // Перший запуск — усе, що вже є, вважаємо відомим.
                SaveKnown(list.Select(i => i.Key));
            }
            else foreach (var i in list) i.IsNew = !i.Disabled && !known.Contains(i.Key) && !i.Name.Equals("TyhyiPK", StringComparison.OrdinalIgnoreCase);
            return list.OrderBy(i => i.Disabled).ThenByDescending(i => i.IsNew).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        static AutoItem Item(string source, string name, string cmd, bool disabled)
        {
            var i = new AutoItem { Source = source, Name = name, Command = cmd, Disabled = disabled };
            i.Key = source.TrimEnd('3', '2') + "|" + name + "|" + cmd;
            i.Path = ExePath(cmd);
            i.Publisher = Sig.Publisher(i.Path);
            return i;
        }

        // Витягує шлях до .exe з командного рядка або ярлика.
        public static string ExePath(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return null;
            string c = Environment.ExpandEnvironmentVariables(cmd.Trim());
            if (c.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) && File.Exists(c))
            {
                try
                {
                    var t = Type.GetTypeFromProgID("WScript.Shell");
                    object sh = Activator.CreateInstance(t);
                    object lnk = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { c });
                    string target = (string)lnk.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, lnk, null);
                    Marshal.ReleaseComObject(lnk);
                    Marshal.ReleaseComObject(sh);
                    return string.IsNullOrEmpty(target) ? c : target;
                }
                catch { return c; }
            }
            if (c.StartsWith("\""))
            {
                int e = c.IndexOf('"', 1);
                return e > 1 ? c.Substring(1, e - 1) : c.Trim('"');
            }
            int x = c.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (x > 0) return c.Substring(0, x + 4);
            if (File.Exists(c)) return c;
            int sp = c.IndexOf(' ');
            return sp > 0 ? c.Substring(0, sp) : c;
        }

        static HashSet<string> LoadKnown()
        {
            try { return File.Exists(KnownFile) ? new HashSet<string>(File.ReadAllLines(KnownFile)) : null; }
            catch { return new HashSet<string>(); }
        }

        static void SaveKnown(IEnumerable<string> keys)
        {
            if (Wipe.Done) return;
            try
            {
                Directory.CreateDirectory(Power.StateDir);
                File.WriteAllLines(KnownFile, keys.Distinct().ToArray());
            }
            catch { }
        }

        public static void AcceptAll(List<AutoItem> items)
        {
            var known = LoadKnown() ?? new HashSet<string>();
            foreach (var i in items) known.Add(i.Key);
            SaveKnown(known);
        }

        static string[] KeyFor(string source)
        {
            foreach (var k in Keys)
                if (SourceName(k) + (k[0] == "HKLM32" ? "32" : "") == source) return k;
            return null;
        }

        // Повертає назад усе, що вимкнула ця програма.
        public static void RestoreDisabled(out int restored, out int failed)
        {
            restored = 0; failed = 0;
            try
            {
                foreach (var i in Read())
                {
                    if (!i.Disabled) continue;
                    if (Toggle(i) == null) restored++; else failed++;
                }
            }
            catch { }
        }

        // Вимикає (переносить у збережене місце) або вмикає назад. Повертає текст помилки або null.
        public static string Toggle(AutoItem i)
        {
            try
            {
                if (i.Source.StartsWith("startup"))
                {
                    bool common = i.Source == "startup-common";
                    string dis = System.IO.Path.Combine(DisabledDir, common ? "common" : "user");
                    if (!i.Disabled)
                    {
                        Directory.CreateDirectory(dis);
                        File.Move(i.Command, System.IO.Path.Combine(dis, System.IO.Path.GetFileName(i.Command)));
                    }
                    else File.Move(i.Command, System.IO.Path.Combine(StartupDir(common), System.IO.Path.GetFileName(i.Command)));
                    return null;
                }
                var k = KeyFor(i.Source);
                if (k == null) return "невідоме місце автозапуску";
                using (var root = Root(k[0], true))
                using (var run = root.CreateSubKey(k[1]))
                using (var store = Registry.CurrentUser.CreateSubKey(DisabledKey + "\\" + i.Source))
                {
                    if (!i.Disabled)
                    {
                        store.SetValue(i.Name, i.Command);
                        run.DeleteValue(i.Name, false);
                    }
                    else
                    {
                        run.SetValue(i.Name, i.Command);
                        store.DeleteValue(i.Name, false);
                    }
                }
                return null;
            }
            catch (Exception ex) { return ex.Message; }
        }
    }

    // ── Здоров'я дисків ──
    class DiskInfo
    {
        public string Name, Kind;
        public double SizeGb, Wear = double.NaN, Temp = double.NaN, Hours = double.NaN, ReadErr = double.NaN, WriteErr = double.NaN;
        public int Health = -1;   // 0 — у нормі, 1 — попередження, 2 — несправний

        public static List<DiskInfo> Read()
        {
            var list = new List<DiskInfo>();
            try
            {
                using (var q = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Storage", "SELECT * FROM MSFT_PhysicalDisk"))
                foreach (ManagementObject mo in q.Get())
                {
                    using (mo)
                    {
                        var d = new DiskInfo();
                        d.Name = Convert.ToString(mo["FriendlyName"]).Trim();
                        int media = ToInt(mo["MediaType"]), bus = ToInt(mo["BusType"]);
                        d.Kind = (bus == 17 ? "NVMe SSD" : media == 4 ? "SSD" : media == 3 ? "HDD" : "диск") + (bus == 7 ? " · USB" : "");
                        try { d.SizeGb = Convert.ToDouble(mo["Size"]) / 1e9; } catch { }
                        d.Health = ToInt(mo["HealthStatus"]);
                        try
                        {
                            foreach (ManagementObject rc in mo.GetRelated("MSFT_StorageReliabilityCounter"))
                            {
                                using (rc)
                                {
                                    d.Wear = ToD(rc["Wear"]);
                                    d.Temp = ToD(rc["Temperature"]);
                                    d.Hours = ToD(rc["PowerOnHours"]);
                                    d.ReadErr = ToD(rc["ReadErrorsUncorrected"]);
                                    d.WriteErr = ToD(rc["WriteErrorsUncorrected"]);
                                }
                            }
                        }
                        catch { }
                        if (d.Temp <= 0 || d.Temp > 150) d.Temp = double.NaN;
                        list.Add(d);
                    }
                }
            }
            catch { }
            return list;
        }

        static int ToInt(object o) { try { return o == null ? -1 : Convert.ToInt32(o); } catch { return -1; } }
        static double ToD(object o) { try { return o == null ? double.NaN : Convert.ToDouble(o); } catch { return double.NaN; } }

        public string HealthText
        {
            get
            {
                switch (Health)
                {
                    case 0: return "у нормі";
                    case 1: return "попередження";
                    case 2: return "несправний";
                    default: return "невідомо";
                }
            }
        }
    }
}
