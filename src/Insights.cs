// Тихий ПК — фонові спостереження: статистика ігор, підказка про пил, прогноз батареї, «не засинати під час завантаження».
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;

namespace TyhyiPK
{
    // ── Скільки гріє кожна гра ──
    class GameStat
    {
        public string Name;
        public double Seconds, CpuSum, CpuN, GpuSum, GpuN, CpuMax, GpuMax;
        public DateTime Last;
        public double CpuAvg { get { return CpuN > 0 ? CpuSum / CpuN : double.NaN; } }
        public double GpuAvg { get { return GpuN > 0 ? GpuSum / GpuN : double.NaN; } }
        public double Hottest { get { return Math.Max(Nz(CpuAvg), Nz(GpuAvg)); } }
        static double Nz(double v) { return double.IsNaN(v) ? 0 : v; }
    }

    static class GameStats
    {
        static readonly string FilePath = Path.Combine(Power.StateDir, "games_stats.txt");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static Dictionary<string, GameStat> data;
        static DateTime saved = DateTime.UtcNow;
        static bool dirty;

        static void Load()
        {
            if (data != null) return;
            data = new Dictionary<string, GameStat>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split('|');
                    if (p.Length < 9) continue;
                    var g = new GameStat { Name = p[0] };
                    double[] v = p.Skip(1).Take(7).Select(x => { double d; return double.TryParse(x, NumberStyles.Float, Inv, out d) ? d : 0; }).ToArray();
                    g.Seconds = v[0]; g.CpuSum = v[1]; g.CpuN = v[2]; g.GpuSum = v[3]; g.GpuN = v[4]; g.CpuMax = v[5]; g.GpuMax = v[6];
                    DateTime t;
                    g.Last = DateTime.TryParse(p[8], Inv, DateTimeStyles.RoundtripKind, out t) ? t : DateTime.MinValue;
                    data[g.Name] = g;
                }
            }
            catch { }
        }

        public static void Add(string game, double cpuTemp, double gpuTemp, double seconds)
        {
            lock (typeof(GameStats))
            {
                Load();
                GameStat g;
                if (!data.TryGetValue(game, out g)) { g = new GameStat { Name = game }; data[game] = g; }
                g.Seconds += seconds;
                g.Last = DateTime.Now;
                if (!double.IsNaN(cpuTemp)) { g.CpuSum += cpuTemp; g.CpuN++; g.CpuMax = Math.Max(g.CpuMax, cpuTemp); }
                if (!double.IsNaN(gpuTemp)) { g.GpuSum += gpuTemp; g.GpuN++; g.GpuMax = Math.Max(g.GpuMax, gpuTemp); }
                dirty = true;
            }
            if ((DateTime.UtcNow - saved).TotalMinutes >= 5) Save();
        }

        public static List<GameStat> All()
        {
            lock (typeof(GameStats))
            {
                Load();
                return data.Values.Where(g => g.Seconds >= 120).OrderByDescending(g => g.Hottest).ToList();
            }
        }

        public static void Save()
        {
            saved = DateTime.UtcNow;
            if (Wipe.Done) return;
            lock (typeof(GameStats))
            {
                if (!dirty || data == null) return;
                try
                {
                    Directory.CreateDirectory(Power.StateDir);
                    File.WriteAllLines(FilePath, data.Values.Select(g => string.Join("|", new[]
                    {
                        g.Name.Replace("|", ""), g.Seconds.ToString("0", Inv), g.CpuSum.ToString("0.#", Inv), g.CpuN.ToString("0", Inv),
                        g.GpuSum.ToString("0.#", Inv), g.GpuN.ToString("0", Inv), g.CpuMax.ToString("0", Inv), g.GpuMax.ToString("0", Inv),
                        g.Last.ToString("o", Inv),
                    })).ToArray());
                    dirty = false;
                }
                catch { }
            }
        }

        // Власний режим для окремої гри (null — як для всіх ігор).
        public static string ModeFor(string game)
        {
            if (game == null) return null;
            string m = Settings.Get("GameMode:" + game.ToLowerInvariant(), null);
            return Modes.Get(m) != null ? m : null;
        }

        public static void SetMode(string game, string mode)
        {
            Settings.Set("GameMode:" + game.ToLowerInvariant(), mode ?? "");
        }
    }

    // ── Підказка про пил: чи росте температура при тому самому навантаженні ──
    // По днях зберігається середня температура процесора у трьох «кошиках»:
    // простій (навантаження до 10%), сильне навантаження (від 70%) і відеокарта під навантаженням.
    static class Thermal
    {
        static readonly string FilePath = Path.Combine(Power.StateDir, "thermal.txt");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        public static readonly string[] Buckets = { "idle", "load", "gpu" };
        static Dictionary<string, double[]> data;   // "yyyy-MM-dd|bucket" → {сума, кількість}
        static DateTime saved = DateTime.UtcNow;
        static bool dirty;

        static void Load()
        {
            if (data != null) return;
            data = new Dictionary<string, double[]>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    var p = line.Split('|');
                    double s, n;
                    if (p.Length == 4 && double.TryParse(p[2], NumberStyles.Float, Inv, out s) && double.TryParse(p[3], NumberStyles.Float, Inv, out n))
                        data[p[0] + "|" + p[1]] = new[] { s, n };
                }
            }
            catch { }
        }

        public static void Add(Snapshot s)
        {
            if (s.CpuTempFromBoard) return;
            lock (typeof(Thermal))
            {
                Load();
                string day = DateTime.Today.ToString("yyyy-MM-dd", Inv);
                if (!double.IsNaN(s.CpuTemp) && s.CpuLoad >= 0)
                {
                    if (s.CpuLoad <= 10) Put(day, "idle", s.CpuTemp);
                    else if (s.CpuLoad >= 70) Put(day, "load", s.CpuTemp);
                }
                if (!double.IsNaN(s.GpuTemp) && !double.IsNaN(s.GpuDraw) && !double.IsNaN(s.GpuLimit) && s.GpuLimit > 0 && s.GpuDraw >= 0.7 * s.GpuLimit)
                    Put(day, "gpu", s.GpuTemp);
            }
            if ((DateTime.UtcNow - saved).TotalMinutes >= 10) Save();
        }

        static void Put(string day, string bucket, double t)
        {
            double[] v;
            string k = day + "|" + bucket;
            if (!data.TryGetValue(k, out v)) { v = new double[2]; data[k] = v; }
            v[0] += t; v[1]++;
            dirty = true;
        }

        public static void Save()
        {
            saved = DateTime.UtcNow;
            if (Wipe.Done) return;
            lock (typeof(Thermal))
            {
                if (!dirty || data == null) return;
                try
                {
                    string cut = DateTime.Today.AddDays(-200).ToString("yyyy-MM-dd", Inv);
                    Directory.CreateDirectory(Power.StateDir);
                    File.WriteAllLines(FilePath, data.Where(kv => string.CompareOrdinal(kv.Key, cut) >= 0)
                        .Select(kv => kv.Key + "|" + kv.Value[0].ToString("0.#", Inv) + "|" + kv.Value[1].ToString("0", Inv)).ToArray());
                    dirty = false;
                }
                catch { }
            }
        }

        // Середнє за проміжок днів [from; to). minCount — мінімум замірів (≈ 5 с кожен).
        static double Avg(string bucket, DateTime from, DateTime to, int minCount)
        {
            double s = 0, n = 0;
            foreach (var kv in data)
            {
                var p = kv.Key.Split('|');
                DateTime d;
                if (p[1] != bucket || !DateTime.TryParseExact(p[0], "yyyy-MM-dd", Inv, DateTimeStyles.None, out d)) continue;
                if (d < from || d >= to) continue;
                s += kv.Value[0]; n += kv.Value[1];
            }
            return n >= minCount ? s / n : double.NaN;
        }

        // Середня температура в кошику за останні days днів (NaN — мало даних).
        public static double Recent(string bucket, int days)
        {
            lock (typeof(Thermal))
            {
                Load();
                return Avg(bucket, DateTime.Today.AddDays(-days + 1), DateTime.Today.AddDays(1), 12);
            }
        }

        public class Verdict
        {
            public int Level;          // 0 — добре, 1 — варто глянути, 2 — час чистити, -1 — ще мало даних
            public double Rise;        // на скільки градусів зросла температура
            public string Short = "", Text = "";
            public int DaysCollected;
        }

        public static Verdict Analyze()
        {
            var v = new Verdict();
            lock (typeof(Thermal))
            {
                Load();
                var days = data.Keys.Select(k => k.Split('|')[0]).Distinct().ToList();
                v.DaysCollected = days.Count;
                DateTime today = DateTime.Today;
                DateTime first;
                if (days.Count == 0 || !DateTime.TryParseExact(days.Min(), "yyyy-MM-dd", Inv, DateTimeStyles.None, out first)) first = today;
                double bestRise = double.NaN;
                string bestBucket = null;
                foreach (string b in Buckets)
                {
                    // Початок спостережень (перші 14 днів) або 30–60 днів тому — проти останніх 7 днів.
                    DateTime baseFrom = first, baseTo = first.AddDays(14);
                    if ((today - first).TotalDays > 75) { baseFrom = today.AddDays(-60); baseTo = today.AddDays(-30); }
                    if (baseTo > today.AddDays(-14)) continue;   // замало часу між періодами
                    double baseT = Avg(b, baseFrom, baseTo, b == "idle" ? 360 : 120);
                    double nowT = Avg(b, today.AddDays(-7), today.AddDays(1), b == "idle" ? 240 : 60);
                    if (double.IsNaN(baseT) || double.IsNaN(nowT)) continue;
                    double rise = nowT - baseT;
                    if (double.IsNaN(bestRise) || rise > bestRise) { bestRise = rise; bestBucket = b; }
                }
                if (double.IsNaN(bestRise))
                {
                    v.Level = -1;
                    int left = Math.Max(1, 28 - (int)(today - first).TotalDays);
                    v.Short = "збираю дані";
                    v.Text = "Програма запам'ятовує, яка температура буває при однаковому навантаженні. Через " + left +
                        " дн. вона зможе підказати, чи не час почистити ПК від пилу.";
                    return v;
                }
                v.Rise = bestRise;
                string what = bestBucket == "idle" ? "у простої" : bestBucket == "load" ? "під навантаженням" : "відеокарти в іграх";
                if (bestRise >= 8)
                {
                    v.Level = 2;
                    v.Short = "+" + Math.Round(bestRise) + "° · час чистити";
                    v.Text = "Температура " + what + " виросла на " + Math.Round(bestRise) + "° порівняно з початком спостережень, хоча навантаження те саме. " +
                        "Найчастіша причина — пил у радіаторах і кулерах. Почисть ПК (або віднеси в сервіс), а для ноутбука — заміни термопасту, якщо йому понад 2–3 роки.";
                }
                else if (bestRise >= 5)
                {
                    v.Level = 1;
                    v.Short = "+" + Math.Round(bestRise) + "° · стежу";
                    v.Text = "Температура " + what + " трохи виросла (+" + Math.Round(bestRise) + "°). Це може бути і пил, і просто тепліша кімната. " +
                        "Якщо за кілька тижнів зросте ще — час чистити.";
                }
                else
                {
                    v.Level = 0;
                    v.Short = "в нормі";
                    v.Text = "Температура при однаковому навантаженні не зростає — охолодження працює як раніше.";
                }
            }
            return v;
        }
    }

    // ── Прогноз батареї ──
    static class BatteryForecast
    {
        static double avgW = double.NaN;
        static DateTime lastT;

        // Згладжене споживання (за ~2 хвилини), щоб прогноз не стрибав.
        public static void Add(double watts)
        {
            var now = DateTime.UtcNow;
            if (double.IsNaN(watts) || watts <= 0) { avgW = double.NaN; return; }
            if (double.IsNaN(avgW) || (now - lastT).TotalMinutes > 2) avgW = watts;
            else
            {
                double k = Math.Min(1, (now - lastT).TotalSeconds / 120);
                avgW += (watts - avgW) * k;
            }
            lastT = now;
        }

        // Скільки хвилин ще пропрацює ноутбук у поточному режимі (NaN — невідомо).
        public static double Minutes(double remainMWh)
        {
            if (double.IsNaN(avgW) || avgW < 0.5 || double.IsNaN(remainMWh) || remainMWh <= 0) return double.NaN;
            return remainMWh / 1000.0 / avgW * 60;
        }

        public static string Text(double minutes)
        {
            if (double.IsNaN(minutes)) return "";
            int m = (int)Math.Round(minutes);
            if (m < 60) return m + " хв";
            return (m / 60) + " год " + (m % 60).ToString("00") + " хв";
        }
    }

    // ── Не засинати, поки щось завантажується ──
    static class SleepGuard
    {
        [DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint flags);
        const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;
        const double ThresholdBytesPerSec = 300 * 1024;   // ≈ 2,4 Мбіт/с

        public static volatile bool Enabled;
        public static volatile bool Holding;
        public static double RateBytesPerSec;
        static Thread thread;

        public static void Start(Func<bool> stopped)
        {
            if (thread != null) return;
            thread = new Thread(() => Loop(stopped));
            thread.IsBackground = true;
            thread.Start();
        }

        static long Received()
        {
            long sum = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    try { sum += ni.GetIPStatistics().BytesReceived; } catch { }
                }
            }
            catch { }
            return sum;
        }

        static void Loop(Func<bool> stopped)
        {
            long prev = Received();
            DateTime prevT = DateTime.UtcNow, quietSince = DateTime.UtcNow;
            while (!stopped())
            {
                Thread.Sleep(5000);
                long now = Received();
                var t = DateTime.UtcNow;
                double sec = Math.Max(1, (t - prevT).TotalSeconds);
                RateBytesPerSec = Math.Max(0, (now - prev) / sec);
                prev = now; prevT = t;
                bool busy = RateBytesPerSec >= ThresholdBytesPerSec;
                if (busy) quietSince = t;
                // Відпускаємо сон лише після 2 хвилин тиші, щоб короткі паузи в завантаженні не присипляли ПК.
                bool want = Enabled && (busy || (Holding && (t - quietSince).TotalMinutes < 2));
                if (want != Holding)
                {
                    try { SetThreadExecutionState(want ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS); } catch { }
                    Holding = want;
                }
            }
            try { SetThreadExecutionState(ES_CONTINUOUS); } catch { }
        }
    }
}
