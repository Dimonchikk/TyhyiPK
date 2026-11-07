// Тихий ПК — логіка вкладки «Сервіс»: очищення, інтернет і VPN, збої програм, сині екрани, перевірка Windows.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TyhyiPK
{
    // ── Очищення ПК ──
    class CleanTarget
    {
        public string Key, Title, Sub;
        public List<string> Dirs = new List<string>();
        public double MinAgeHours;
        public bool Recycle;
        public long Bytes;
        public int Files;
        public bool Checked = true;
    }

    static class Cleaner
    {
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        struct RbInfo64 { public int cbSize; public long Size; public long Items; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct RbInfo32 { public int cbSize; public long Size; public long Items; }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        static extern int QueryRb64(string root, ref RbInfo64 info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHQueryRecycleBinW")]
        static extern int QueryRb32(string root, ref RbInfo32 info);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        static string Env(string v) { return Environment.GetEnvironmentVariable(v) ?? ""; }
        static string Local { get { return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); } }

        public static List<CleanTarget> Targets()
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            var list = new List<CleanTarget>();
            list.Add(new CleanTarget { Key = "temp", Title = "Тимчасові файли", Sub = "залишки інсталяторів і програм (старші за добу)", MinAgeHours = 24,
                Dirs = { Path.GetTempPath(), Path.Combine(win, "Temp") } });
            list.Add(new CleanTarget { Key = "updates", Title = "Завантажені оновлення Windows", Sub = "файли вже встановлених оновлень", MinAgeHours = 72,
                Dirs = { Path.Combine(win, "SoftwareDistribution\\Download") } });
            var br = new CleanTarget { Key = "browsers", Title = "Кеш браузерів", Sub = "Chrome, Edge, Opera, Firefox · паролі й історія не зачіпаються" };
            br.Dirs.AddRange(BrowserCaches());
            list.Add(br);
            list.Add(new CleanTarget { Key = "crash", Title = "Звіти про збої програм", Sub = "дампи пам'яті й звіти, які Windows вже не надсилає",
                Dirs = { Path.Combine(Local, "CrashDumps"), Path.Combine(pd, "Microsoft\\Windows\\WER\\ReportArchive"), Path.Combine(pd, "Microsoft\\Windows\\WER\\ReportQueue") } });
            list.Add(new CleanTarget { Key = "recycle", Title = "Кошик", Sub = "файли, які ти вже видалив", Recycle = true });
            return list;
        }

        static IEnumerable<string> BrowserCaches()
        {
            var res = new List<string>();
            string[] chromium =
            {
                "Google\\Chrome\\User Data", "Microsoft\\Edge\\User Data", "BraveSoftware\\Brave-Browser\\User Data", "Vivaldi\\User Data", "Yandex\\YandexBrowser\\User Data",
            };
            string[] cacheNames = { "Cache", "Code Cache", "GPUCache" };
            foreach (string c in chromium)
            {
                string ud = Path.Combine(Local, c);
                if (!Directory.Exists(ud)) continue;
                res.Add(Path.Combine(ud, "GrShaderCache"));
                res.Add(Path.Combine(ud, "ShaderCache"));
                try
                {
                    foreach (string prof in Directory.GetDirectories(ud))
                        foreach (string n in cacheNames) res.Add(Path.Combine(prof, n));
                }
                catch { }
            }
            foreach (string o in new[] { "Opera Software\\Opera Stable", "Opera Software\\Opera GX Stable" })
                foreach (string n in cacheNames) res.Add(Path.Combine(Local, o, n));
            try
            {
                string ff = Path.Combine(Local, "Mozilla\\Firefox\\Profiles");
                if (Directory.Exists(ff))
                    foreach (string prof in Directory.GetDirectories(ff)) res.Add(Path.Combine(prof, "cache2"));
            }
            catch { }
            return res.Where(Directory.Exists).ToList();
        }

        // Обхід файлів без переходу за посиланнями-«точками повторного аналізу».
        static IEnumerable<FileInfo> Files(string dir)
        {
            var stack = new Stack<DirectoryInfo>();
            try { if (Directory.Exists(dir)) stack.Push(new DirectoryInfo(dir)); } catch { }
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                FileSystemInfo[] items;
                try { items = d.GetFileSystemInfos(); } catch { continue; }
                foreach (var i in items)
                {
                    if ((i.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                    var sub = i as DirectoryInfo;
                    if (sub != null) stack.Push(sub);
                    else yield return (FileInfo)i;
                }
            }
        }

        static bool OldEnough(FileInfo f, double hours)
        {
            if (hours <= 0) return true;
            try { return (DateTime.UtcNow - f.LastWriteTimeUtc).TotalHours >= hours; } catch { return false; }
        }

        public static void Measure(CleanTarget t)
        {
            t.Bytes = 0; t.Files = 0;
            if (t.Recycle)
            {
                long size, items;
                if (QueryRecycle(out size, out items)) { t.Bytes = size; t.Files = (int)Math.Min(int.MaxValue, items); }
                return;
            }
            foreach (string d in t.Dirs)
                foreach (var f in Files(d))
                {
                    if (!OldEnough(f, t.MinAgeHours)) continue;
                    try { t.Bytes += f.Length; t.Files++; } catch { }
                }
        }

        // Повертає, скільки байтів звільнено.
        public static long Clean(CleanTarget t)
        {
            if (t.Recycle)
            {
                long before, items;
                QueryRecycle(out before, out items);
                try { SHEmptyRecycleBin(IntPtr.Zero, null, 0x1 | 0x2 | 0x4); } catch { }
                long after;
                QueryRecycle(out after, out items);
                return Math.Max(0, before - after);
            }
            long freed = 0;
            foreach (string d in t.Dirs)
            {
                foreach (var f in Files(d).ToList())
                {
                    if (!OldEnough(f, t.MinAgeHours)) continue;
                    try
                    {
                        long len = f.Length;
                        if ((f.Attributes & FileAttributes.ReadOnly) != 0) f.Attributes = FileAttributes.Normal;
                        f.Delete();
                        freed += len;
                    }
                    catch { }   // файл зайнятий програмою — пропускаємо
                }
                RemoveEmptyDirs(d, true);
            }
            return freed;
        }

        static void RemoveEmptyDirs(string dir, bool isRoot)
        {
            try
            {
                var di = new DirectoryInfo(dir);
                if (!di.Exists || (di.Attributes & FileAttributes.ReparsePoint) != 0) return;
                foreach (var sub in di.GetDirectories()) RemoveEmptyDirs(sub.FullName, false);
                if (!isRoot && di.GetFileSystemInfos().Length == 0) di.Delete();
            }
            catch { }
        }

        static bool QueryRecycle(out long size, out long items)
        {
            size = 0; items = 0;
            try
            {
                if (IntPtr.Size == 8)
                {
                    var i = new RbInfo64 { cbSize = Marshal.SizeOf(typeof(RbInfo64)) };
                    if (QueryRb64(null, ref i) != 0) return false;
                    size = i.Size; items = i.Items;
                }
                else
                {
                    var i = new RbInfo32 { cbSize = Marshal.SizeOf(typeof(RbInfo32)) };
                    if (QueryRb32(null, ref i) != 0) return false;
                    size = i.Size; items = i.Items;
                }
                return true;
            }
            catch { return false; }
        }

        public static string Size(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.0") + " ГБ";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0") + " МБ";
            if (bytes > 0) return Math.Max(1, bytes / 1024) + " КБ";
            return "0";
        }
    }

    // ── Інтернет: пінг, швидкість, VPN ──
    class PingResult
    {
        public string Host;
        public int Sent, Lost;
        public double Avg = double.NaN, Jitter = double.NaN;
    }

    class VpnInfo
    {
        public List<string> Adapters = new List<string>();
        public string Ip = "", Country = "", Error;
        public bool Warp;
    }

    static class NetCheck
    {
        const string Agent = "TyhyiPK/" + AppVersion.Text;

        static NetCheck()
        {
            try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
            try { ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 16); } catch { }
            try { ServicePointManager.Expect100Continue = false; } catch { }
        }

        public static PingResult Ping(string host, int count)
        {
            var r = new PingResult { Host = host };
            var times = new List<double>();
            using (var p = new Ping())
            {
                for (int i = 0; i < count; i++)
                {
                    r.Sent++;
                    try
                    {
                        var reply = p.Send(host, 2000);
                        if (reply.Status == IPStatus.Success) times.Add(reply.RoundtripTime);
                        else r.Lost++;
                    }
                    catch { r.Lost++; }
                    System.Threading.Thread.Sleep(150);
                }
            }
            if (times.Count > 0)
            {
                r.Avg = times.Average();
                r.Jitter = times.Count > 1 ? times.Zip(times.Skip(1), (a, b) => Math.Abs(a - b)).Average() : 0;
            }
            return r;
        }

        public const int SpeedSeconds = 15;   // скільки секунд міряти завантаження і стільки ж відвантаження
        const int Streams = 4;                 // паралельні з'єднання, щоб виміряти й швидкий інтернет
        const double WarmUp = 2;               // перші секунди не рахуємо: з'єднання ще розганяється

        // Спільна логіка: кілька потоків працюють до кінця часу, рахуємо байти після розгону.
        // progress — поточна швидкість у Мбіт/с раз на секунду. Повертає Мбіт/с або NaN.
        static double Measure(Func<Func<bool>, Action<int>, bool> worker, Action<double> progress)
        {
            long counted = 0, total = 0;
            int ok = 0;
            var sw = Stopwatch.StartNew();
            Func<bool> done = () => sw.Elapsed.TotalSeconds >= SpeedSeconds;
            Action<int> add = n =>
            {
                System.Threading.Interlocked.Add(ref total, n);
                if (sw.Elapsed.TotalSeconds >= WarmUp) System.Threading.Interlocked.Add(ref counted, n);
            };
            var threads = new List<System.Threading.Thread>();
            for (int i = 0; i < Streams; i++)
            {
                var t = new System.Threading.Thread(() =>
                {
                    int fails = 0;
                    while (!done() && fails < 5)
                    {
                        if (worker(done, add)) System.Threading.Interlocked.Increment(ref ok);
                        else { fails++; System.Threading.Thread.Sleep(300); }
                    }
                });
                t.IsBackground = true;
                threads.Add(t);
                t.Start();
            }
            while (!done() && threads.Any(t => t.IsAlive))
            {
                System.Threading.Thread.Sleep(1000);
                double sec = sw.Elapsed.TotalSeconds - WarmUp;
                if (progress != null && sec > 0.5) progress(System.Threading.Interlocked.Read(ref counted) * 8 / sec / 1e6);
            }
            foreach (var t in threads) t.Join(5000);
            double measured = Math.Min(sw.Elapsed.TotalSeconds, SpeedSeconds) - WarmUp;
            long bytes = System.Threading.Interlocked.Read(ref counted);
            if (bytes <= 0 || measured <= 0.5)
            {
                // Дуже повільний інтернет або обрив — рахуємо все, що встигли.
                bytes = System.Threading.Interlocked.Read(ref total);
                measured = Math.Min(sw.Elapsed.TotalSeconds, SpeedSeconds);
            }
            return bytes > 0 ? bytes * 8 / measured / 1e6 : double.NaN;
        }

        // Один запит на завантаження 25 МБ; читаємо, поки не скінчиться час.
        static bool DownloadOnce(Func<bool> done, Action<int> add)
        {
            HttpWebRequest req = null;
            try
            {
                req = (HttpWebRequest)WebRequest.Create("https://speed.cloudflare.com/__down?bytes=25000000");
                req.UserAgent = Agent;
                req.Timeout = 10000;
                req.ReadWriteTimeout = 10000;
                using (var resp = req.GetResponse())
                using (var s = resp.GetResponseStream())
                {
                    var buf = new byte[65536];
                    int n;
                    while (!done() && (n = s.Read(buf, 0, buf.Length)) > 0) add(n);
                    if (done()) try { req.Abort(); } catch { }
                }
                return true;
            }
            catch
            {
                if (req != null) try { req.Abort(); } catch { }
                return done();   // обрив через кінець часу — не помилка
            }
        }

        // Один запит на відвантаження до 25 МБ; пишемо, поки не скінчиться час.
        static bool UploadOnce(Func<bool> done, Action<int> add)
        {
            HttpWebRequest req = null;
            try
            {
                const int size = 25000000;
                req = (HttpWebRequest)WebRequest.Create("https://speed.cloudflare.com/__up");
                req.Method = "POST";
                req.UserAgent = Agent;
                req.ContentType = "application/octet-stream";
                req.ContentLength = size;
                req.AllowWriteStreamBuffering = false;
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                var buf = new byte[65536];
                new Random().NextBytes(buf);
                using (var s = req.GetRequestStream())
                {
                    int left = size;
                    while (left > 0)
                    {
                        if (done()) { try { req.Abort(); } catch { } return true; }
                        int k = Math.Min(left, buf.Length);
                        s.Write(buf, 0, k);
                        add(k);
                        left -= k;
                    }
                }
                using (req.GetResponse()) { }
                return true;
            }
            catch
            {
                if (req != null) try { req.Abort(); } catch { }
                return done();
            }
        }

        public static double Download(Action<double> progress) { return Measure(DownloadOnce, progress); }
        public static double Upload(Action<double> progress) { return Measure(UploadOnce, progress); }

        static readonly Regex VpnWords = new Regex("vpn|wireguard|wintun|tap-windows|openvpn|nordlynx|proton|windscribe|surfshark|mullvad|warp|" +
            "tailscale|zerotier|anyconnect|forti|globalprotect|pangp|hamachi|softether|outline|amnezia|v2ray|xray|hiddify|sing-box|tun2socks|kaspersky secure",
            RegexOptions.IgnoreCase);
        static readonly Regex NotVpn = new Regex("teredo|6to4|isatap|ip-https|hyper-v|virtualbox|vmware|loopback|bluetooth|wi-fi direct|kernel debug", RegexOptions.IgnoreCase);

        public static VpnInfo Vpn()
        {
            var v = new VpnInfo();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    string d = ni.Description + " " + ni.Name;
                    if (NotVpn.IsMatch(d)) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Ppp || VpnWords.IsMatch(d)
                        || (ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel && !NotVpn.IsMatch(d)))
                        v.Adapters.Add(ni.Description);
                }
            }
            catch { }
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("https://www.cloudflare.com/cdn-cgi/trace");
                req.UserAgent = Agent;
                req.Timeout = 10000;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    foreach (string line in sr.ReadToEnd().Split('\n'))
                    {
                        int i = line.IndexOf('=');
                        if (i < 0) continue;
                        string k = line.Substring(0, i), val = line.Substring(i + 1).Trim();
                        if (k == "ip") v.Ip = val;
                        else if (k == "loc") v.Country = val.ToUpperInvariant();
                        else if (k == "warp") v.Warp = val == "on" || val == "plus";
                    }
                }
            }
            catch (Exception ex) { v.Error = ex.Message; }
            return v;
        }

        static readonly Dictionary<string, string> Countries = new Dictionary<string, string>
        {
            {"UA","Україна"},{"PL","Польща"},{"DE","Німеччина"},{"NL","Нідерланди"},{"FR","Франція"},{"GB","Велика Британія"},{"US","США"},
            {"CA","Канада"},{"SE","Швеція"},{"FI","Фінляндія"},{"NO","Норвегія"},{"DK","Данія"},{"CH","Швейцарія"},{"AT","Австрія"},
            {"CZ","Чехія"},{"SK","Словаччина"},{"HU","Угорщина"},{"RO","Румунія"},{"MD","Молдова"},{"BG","Болгарія"},{"LT","Литва"},
            {"LV","Латвія"},{"EE","Естонія"},{"IT","Італія"},{"ES","Іспанія"},{"PT","Португалія"},{"IE","Ірландія"},{"BE","Бельгія"},
            {"LU","Люксембург"},{"TR","Туреччина"},{"GE","Грузія"},{"IL","Ізраїль"},{"JP","Японія"},{"SG","Сінгапур"},{"HK","Гонконг"},
            {"AU","Австралія"},{"IN","Індія"},{"BR","Бразилія"},{"KZ","Казахстан"},{"AE","ОАЕ"},{"RS","Сербія"},{"HR","Хорватія"},
            {"SI","Словенія"},{"GR","Греція"},{"CY","Кіпр"},{"IS","Ісландія"},
        };

        public static string CountryName(string code)
        {
            string n;
            return code != null && Countries.TryGetValue(code, out n) ? n : code;
        }

        public static string HomeCountry()
        {
            try
            {
                string r = RegionInfo.CurrentRegion.TwoLetterISORegionName;
                if (!string.IsNullOrEmpty(r) && r.Length == 2 && r != "US") return r.ToUpperInvariant();
            }
            catch { }
            return "UA";
        }
    }

    // ── Збої програм і сині екрани (з журналів Windows) ──
    class CrashGroup
    {
        public string App;
        public int Crashes, Hangs;
        public DateTime Last;
        public readonly Dictionary<string, int> Modules = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, int> Codes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public string TopModule { get { return Modules.Count == 0 ? null : Modules.OrderByDescending(kv => kv.Value).First().Key; } }
        public string TopCode { get { return Codes.Count == 0 ? null : Codes.OrderByDescending(kv => kv.Value).First().Key; } }
    }

    class BlueScreen
    {
        public DateTime Time;
        public uint Code;
        public bool PowerLoss;   // раптове вимкнення без синього екрана
    }

    static class SysLogs
    {
        class LogEvent
        {
            public int Id;
            public string Source;
            public DateTime Time;
            public string[] Data;
            public string Prop(int i) { return Data != null && i < Data.Length && Data[i] != null ? Data[i].Trim() : ""; }
        }

        // Читає журнал від найновіших записів до найстаріших, поки не дійде до межі в днях.
        static List<LogEvent> Read(string log, int days, Func<int, string, bool> want)
        {
            var list = new List<LogEvent>();
            try
            {
                using (var el = new EventLog(log))
                {
                    var entries = el.Entries;
                    DateTime cut = DateTime.Now.AddDays(-days);
                    for (int i = entries.Count - 1; i >= 0 && list.Count < 3000; i--)
                    {
                        EventLogEntry e;
                        try { e = entries[i]; } catch { continue; }
                        using (e)
                        {
                            if (e.TimeGenerated < cut) break;
                            int id = (int)(e.InstanceId & 0xFFFF);
                            string src = e.Source ?? "";
                            if (!want(id, src)) continue;
                            list.Add(new LogEvent { Id = id, Source = src, Time = e.TimeGenerated, Data = e.ReplacementStrings });
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public static List<CrashGroup> AppCrashes(int days)
        {
            var groups = new Dictionary<string, CrashGroup>(StringComparer.OrdinalIgnoreCase);
            var events = Read("Application", days, (id, src) => (id == 1000 && src == "Application Error") || (id == 1002 && src == "Application Hang"));
            foreach (var e in events)
            {
                string app = e.Prop(0);
                if (app.Length == 0) continue;
                CrashGroup g;
                if (!groups.TryGetValue(app, out g)) { g = new CrashGroup { App = app }; groups[app] = g; }
                if (e.Time > g.Last) g.Last = e.Time;
                if (e.Id == 1002) { g.Hangs++; continue; }
                g.Crashes++;
                string mod = e.Prop(3), code = e.Prop(6).ToLowerInvariant();
                if (code.StartsWith("0x")) code = code.Substring(2);
                if (mod.Length > 0) g.Modules[mod] = (g.Modules.ContainsKey(mod) ? g.Modules[mod] : 0) + 1;
                if (code.Length > 0) g.Codes[code] = (g.Codes.ContainsKey(code) ? g.Codes[code] : 0) + 1;
            }
            return groups.Values.OrderByDescending(g => g.Last).ToList();
        }

        public static List<BlueScreen> BlueScreens(int days)
        {
            var bsod = new List<BlueScreen>();
            var power = new List<BlueScreen>();
            var events = Read("System", days, (id, src) =>
                (id == 1001 && (src == "BugCheck" || src == "Microsoft-Windows-WER-SystemErrorReporting")) || (id == 41 && src == "Microsoft-Windows-Kernel-Power"));
            foreach (var e in events)
            {
                if (e.Id == 1001)
                {
                    var m = Regex.Match(string.Join(" ", e.Data ?? new string[0]), "0x([0-9a-fA-F]{1,8})\\b");
                    uint code;
                    if (m.Success && uint.TryParse(m.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code))
                        bsod.Add(new BlueScreen { Time = e.Time, Code = code });
                }
                else
                {
                    uint code;
                    uint.TryParse(e.Prop(0), NumberStyles.Integer, CultureInfo.InvariantCulture, out code);
                    power.Add(new BlueScreen { Time = e.Time, Code = code, PowerLoss = code == 0 });
                }
            }
            // Синій екран дає і подію 1001, і подію 41 — лишаємо одну.
            foreach (var p in power)
            {
                bool dup = bsod.Any(b => Math.Abs((b.Time - p.Time).TotalMinutes) < 60 && (p.Code == 0 || p.Code == b.Code));
                if (!dup) bsod.Add(p);
            }
            return bsod.OrderByDescending(b => b.Time).ToList();
        }

        // ── Пояснення синіх екранів ──
        static readonly Dictionary<uint, string[]> Stops = new Dictionary<uint, string[]>
        {
            { 0x0A, new[] { "IRQL_NOT_LESS_OR_EQUAL", "Драйвер звернувся до пам'яті, до якої не мав доступу.", "Онови драйвери (особливо Wi-Fi, мережі, відеокарти). Якщо почалося після нового пристрою чи програми — прибери їх." } },
            { 0xD1, new[] { "DRIVER_IRQL_NOT_LESS_OR_EQUAL", "Помилка в драйвері — найчастіше мережі, Wi-Fi або антивірусу.", "Онови драйвер мережевої карти й Wi-Fi з сайту виробника ноутбука чи плати." } },
            { 0x1E, new[] { "KMODE_EXCEPTION_NOT_HANDLED", "Драйвер зробив помилку, яку Windows не змогла обробити.", "Онови драйвери. Якщо повторюється — перевір оперативну пам'ять." } },
            { 0x3B, new[] { "SYSTEM_SERVICE_EXCEPTION", "Збій у системній службі або драйвері (часто відеодрайвер або антивірус).", "Онови драйвер відеокарти й Windows. Перевір систему на вкладці «Windows»." } },
            { 0x50, new[] { "PAGE_FAULT_IN_NONPAGED_AREA", "Звернення до неіснуючої пам'яті: драйвер, антивірус або несправна оперативка.", "Онови драйвери, перевір оперативну пам'ять (Win+R → mdsched) і диск." } },
            { 0x7E, new[] { "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Системний потік драйвера впав з помилкою.", "Онови драйвери, особливо відеокарти. Якщо щойно оновлював — спробуй відкотити." } },
            { 0x1A, new[] { "MEMORY_MANAGEMENT", "Проблема з керуванням пам'яттю — часто несправна або розігнана оперативка.", "Вимкни розгін пам'яті (XMP/EXPO), перевір оперативку (Win+R → mdsched)." } },
            { 0x19, new[] { "BAD_POOL_HEADER", "Пошкоджено системну пам'ять — драйвер або оперативка.", "Онови драйвери, перевір оперативну пам'ять." } },
            { 0xC2, new[] { "BAD_POOL_CALLER", "Драйвер неправильно працює з пам'яттю.", "Онови драйвери; згадай, що встановлював перед появою помилки." } },
            { 0x9F, new[] { "DRIVER_POWER_STATE_FAILURE", "Драйвер не впорався з переходом у сон або виходом із нього.", "Онови драйвери (часто відеокарти, Wi-Fi, USB). Можна вимкнути швидкий запуск Windows." } },
            { 0x124, new[] { "WHEA_UNCORRECTABLE_ERROR", "Апаратна помилка: процесор, пам'ять або живлення. Часто — перегрів чи розгін.", "Прибери розгін, перевір температури й блок живлення. Тихий режим зменшує ризик перегріву." } },
            { 0x101, new[] { "CLOCK_WATCHDOG_TIMEOUT", "Одне з ядер процесора перестало відповідати. Часто — розгін, перегрів або BIOS.", "Прибери розгін, онови BIOS, перевір температуру процесора." } },
            { 0x133, new[] { "DPC_WATCHDOG_VIOLATION", "Драйвер завис надто надовго — часто драйвер SSD або старий драйвер накопичувача.", "Онови драйвер і прошивку SSD та драйвери чипсета." } },
            { 0x139, new[] { "KERNEL_SECURITY_CHECK_FAILURE", "Windows помітила пошкодження даних у ядрі — драйвер або пам'ять.", "Онови драйвери, перевір систему (вкладка «Windows») і оперативну пам'ять." } },
            { 0xEF, new[] { "CRITICAL_PROCESS_DIED", "Аварійно завершився важливий процес Windows.", "Перевір систему на вкладці «Windows» (Виправити). Перевір диск на помилки." } },
            { 0x116, new[] { "VIDEO_TDR_FAILURE", "Відеокарта не відповіла вчасно — драйвер, перегрів або нестабільний розгін.", "Чисто перевстанови драйвер відеокарти, прибери розгін карти, перевір її температуру." } },
            { 0x117, new[] { "VIDEO_TDR_TIMEOUT_DETECTED", "Відеокарта зависала — драйвер або перегрів.", "Онови драйвер відеокарти, перевір її температуру." } },
            { 0x7A, new[] { "KERNEL_DATA_INPAGE_ERROR", "Не вдалося прочитати дані з диска — можливо, диск або кабель несправний.", "Глянь здоров'я дисків на вкладці «Безпека» → «Здоров'я». Зроби копію важливих файлів." } },
            { 0x7B, new[] { "INACCESSIBLE_BOOT_DEVICE", "Windows не змогла дістатися до системного диска.", "Перевір режим диска в BIOS (AHCI/RAID) і стан диска." } },
            { 0xF4, new[] { "CRITICAL_OBJECT_TERMINATION", "Аварійно закрився критичний процес — часто через збій диска.", "Перевір стан дисків і кабелі." } },
            { 0x3D, new[] { "INTERRUPT_EXCEPTION_NOT_HANDLED", "Помилка обробки переривання — драйвер.", "Онови драйвери чипсета й пристроїв." } },
            { 0x7F, new[] { "UNEXPECTED_KERNEL_MODE_TRAP", "Апаратний збій або помилка драйвера — часто перегрів чи пам'ять.", "Перевір температури й оперативну пам'ять, прибери розгін." } },
            { 0x4E, new[] { "PFN_LIST_CORRUPT", "Пошкоджено таблицю пам'яті — драйвер або оперативка.", "Перевір оперативну пам'ять і онови драйвери." } },
            { 0x109, new[] { "CRITICAL_STRUCTURE_CORRUPTION", "Пошкоджено важливі дані ядра — драйвер або пам'ять.", "Перевір оперативну пам'ять; прибери старі утиліти розгону й «оптимізатори»." } },
            { 0x1000007E, new[] { "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "Системний потік драйвера впав з помилкою.", "Онови драйвери, особливо відеокарти." } },
            { 0x1000008E, new[] { "KERNEL_MODE_EXCEPTION_NOT_HANDLED", "Помилка драйвера або пам'яті.", "Онови драйвери й перевір оперативну пам'ять." } },
            { 0xC000021A, new[] { "STATUS_SYSTEM_PROCESS_TERMINATED", "Аварійно завершився системний процес.", "Перевір систему на вкладці «Windows» (Виправити)." } },
        };

        public static string StopName(uint code)
        {
            string[] s;
            return Stops.TryGetValue(code, out s) ? s[0] : "";
        }

        public static void Explain(BlueScreen b, out string title, out string what, out string advice)
        {
            if (b.PowerLoss)
            {
                title = "Раптове вимкнення";
                what = "Комп'ютер вимкнувся без синього екрана: зникло живлення, перегрів, затиснута кнопка живлення або збій блока живлення.";
                advice = "Якщо світло не вимикали — перевір температури під навантаженням і блок живлення.";
                return;
            }
            string[] s;
            string hex = "0x" + b.Code.ToString("X");
            if (Stops.TryGetValue(b.Code, out s) && s[1].Length > 0)
            {
                title = "Синій екран " + hex + " · " + s[0];
                what = s[1];
                advice = s[2];
            }
            else
            {
                title = "Синій екран " + hex + (s != null ? " · " + s[0] : "");
                what = "Рідкісний код помилки. Найчастіше сині екрани спричиняють драйвери, оперативна пам'ять або перегрів.";
                advice = "Пошукай код " + hex + " в інтернеті разом з моделлю ПК. Онови драйвери й перевір температури.";
            }
        }

        // ── Пояснення збоїв програм ──
        public static string ModuleText(string mod)
        {
            if (string.IsNullOrEmpty(mod)) return "";
            string m = mod.ToLowerInvariant();
            if (m.StartsWith("nvwgf2") || m.StartsWith("nvd3dum") || m.StartsWith("nvoglv") || m.StartsWith("nvlddmkm") || m.StartsWith("nvcuda")) return "драйвер відеокарти NVIDIA";
            if (m.StartsWith("atiu") || m.StartsWith("amdx") || m.StartsWith("aticfx") || m.StartsWith("atio")) return "драйвер відеокарти AMD";
            if (m.StartsWith("igd") || m.StartsWith("ig7") || m.StartsWith("ig9") || m.StartsWith("igxe")) return "драйвер графіки Intel";
            if (m == "ntdll.dll" || m == "kernelbase.dll" || m == "kernel32.dll") return "сама програма (помилка всередині неї)";
            if (m.StartsWith("ucrtbase") || m.StartsWith("msvcp") || m.StartsWith("vcruntime") || m.StartsWith("msvcr")) return "бібліотеки Visual C++";
            if (m == "clr.dll" || m == "coreclr.dll" || m == "clrjit.dll" || m.StartsWith("mscorlib")) return "платформа .NET";
            if (m == "d3d11.dll" || m == "d3d12.dll" || m == "dxgi.dll" || m.StartsWith("d3d9")) return "DirectX (графіка)";
            if (m.Contains("unityplayer")) return "рушій Unity";
            if (m.Contains("steam")) return "Steam";
            if (m.StartsWith("unknown")) return "невідомий модуль";
            return mod;
        }

        public static string CodeText(string code)
        {
            switch ((code ?? "").ToLowerInvariant())
            {
                case "c0000005": return "звернення до чужої пам'яті";
                case "c0000409": return "переповнення буфера (захист спрацював)";
                case "c0000374": return "пошкоджена пам'ять програми";
                case "c00000fd": return "переповнення стеку";
                case "e0434352": return "необроблена помилка .NET";
                case "c000001d": return "непідтримувана процесором інструкція";
                case "c0000094": return "ділення на нуль";
                case "80000003": return "аварійна зупинка програми";
                case "c0000602": return "програма сама аварійно завершилась";
                case "c0000142": return "не вдалося запуститися (DLL)";
                case "40000015": return "програма сама аварійно завершилась";
                default: return code != null && code.Length > 0 ? "код 0x" + code : "";
            }
        }

        public static string CrashAdvice(CrashGroup g)
        {
            string mod = (g.TopModule ?? "").ToLowerInvariant();
            string mt = ModuleText(g.TopModule);
            if (mt.StartsWith("драйвер відеокарти") || mt.StartsWith("драйвер графіки") || mt.StartsWith("DirectX"))
                return "Збій у " + mt + ". Онови драйвер з офіційного сайту (для NVIDIA/AMD — «чисте встановлення»), прибери розгін карти і глянь її температуру.";
            if (mt.StartsWith("бібліотеки Visual C++"))
                return "Збій у бібліотеках Visual C++. Перевстанови «Microsoft Visual C++ Redistributable» (x64 і x86) з сайту Microsoft.";
            if (mt.StartsWith("платформа .NET"))
                return "Збій у .NET. Онови програму або перевстанови .NET Desktop Runtime.";
            if (g.Hangs > 0 && g.Crashes == 0)
                return "Програма зависала й не відповідала. Онови її; якщо це гра — перевір цілісність файлів у лаунчері.";
            if (mod == "ntdll.dll" || mod == "kernelbase.dll")
                return "Помилка всередині самої програми. Онови її, перевір цілісність файлів (для ігор — у Steam/Epic) або перевстанови.";
            return "Онови програму. Якщо це гра — перевір цілісність файлів у лаунчері. Якщо падає багато різних програм — перевір оперативну пам'ять і систему.";
        }
    }

    // ── Перевірка системних файлів Windows ──
    static class WinHealth
    {
        static string SysTool(string name)
        {
            string win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!Environment.Is64BitProcess && Environment.Is64BitOperatingSystem) return Path.Combine(win, "Sysnative", name);
            return Path.Combine(Environment.SystemDirectory, name);
        }

        static string CbsLog { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Logs\\CBS\\CBS.log"); } }

        static long CbsLength()
        {
            try { return new FileInfo(CbsLog).Length; } catch { return 0; }
        }

        static string CbsTail(long from)
        {
            try
            {
                using (var fs = new FileStream(CbsLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                {
                    if (from > fs.Length) from = 0;
                    fs.Seek(from, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8)) return sr.ReadToEnd();
                }
            }
            catch { return null; }
        }

        // 0 — усе гаразд, 1 — знайдено/виправлено проблеми, 2 — є невиправні, -1 — не вдалося.
        public static int Dism(bool repair, out string text)
        {
            string o;
            int code = Tool.Run(SysTool("dism.exe"), "/Online /Cleanup-Image " + (repair ? "/RestoreHealth" : "/CheckHealth") + " /English", out o, 90 * 60 * 1000);
            o = o ?? "";
            if (code == -1 && o.Length == 0) { text = "Не вдалося запустити перевірку DISM (потрібні права адміністратора)."; return -1; }
            if (o.Contains("No component store corruption detected")) { text = "Сховище компонентів Windows у порядку."; return 0; }
            if (o.Contains("The restore operation completed successfully")) { text = "Сховище компонентів Windows відновлено."; return 1; }
            if (o.Contains("component store is repairable")) { text = "Сховище компонентів Windows пошкоджене, але його можна відновити (кнопка «Виправити»)."; return 1; }
            if (o.Contains("cannot be repaired")) { text = "Сховище компонентів Windows пошкоджене й не відновлюється автоматично."; return 2; }
            if (o.Contains("source files could not be found") || o.Contains("0x800f081f"))
            { text = "DISM не знайшов файлів для відновлення — перевір інтернет і спробуй ще раз."; return 2; }
            text = code == 0 ? "Перевірку сховища Windows завершено." : "DISM завершився з кодом " + code + ".";
            return code == 0 ? 0 : -1;
        }

        public static int Sfc(bool repair, out string text)
        {
            long start = CbsLength();
            string o;
            int code = Tool.Run(SysTool("sfc.exe"), repair ? "/scannow" : "/verifyonly", out o, 90 * 60 * 1000);
            string log = CbsTail(start);
            if (log == null) { text = "Перевірку файлів завершено (журнал недоступний, код " + code + ")."; return code == 0 ? 0 : -1; }
            int bad = 0, fixedN = 0, unfixed = 0;
            foreach (string line in log.Split('\n'))
            {
                if (!line.Contains("[SR]")) continue;
                if (line.Contains("Cannot repair member file")) unfixed++;
                else if (line.Contains("Repaired file") || line.Contains("Repairing corrupted file")) fixedN++;
                else if (line.IndexOf("corrupt", StringComparison.OrdinalIgnoreCase) >= 0 || line.Contains("do not match")) bad++;
            }
            if (!log.Contains("[SR]")) { text = "Перевірку файлів не вдалося виконати (потрібні права адміністратора)."; return -1; }
            if (unfixed > 0) { text = "Системні файли пошкоджені, і частину не вдалося відновити (" + unfixed + "). Спершу натисни «Виправити» — він відновить сховище з інтернету."; return 2; }
            if (fixedN > 0) { text = "Знайдено й виправлено пошкоджені системні файли. Перезавантаж ПК."; return 1; }
            if (bad > 0) { text = "Знайдено пошкоджені системні файли. Натисни «Виправити»."; return 1; }
            text = "Системні файли Windows у порядку.";
            return 0;
        }
    }
}
