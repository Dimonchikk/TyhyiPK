// Тихий ПК — лічильник електроенергії та стан батареї.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;

namespace TyhyiPK
{
    // Оцінка споживання всього ПК «з розетки» і накопичення кВт·год по днях.
    static class Energy
    {
        static readonly string FilePath = Path.Combine(Power.StateDir, "energy.txt");
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static readonly SortedDictionary<DateTime, double> days = new SortedDictionary<DateTime, double>();
        // За кожен день: [0] секунд роботи (поки працює програма), [1] макс. °C процесора, [2] макс. °C відеокарти.
        static readonly Dictionary<DateTime, double[]> stats = new Dictionary<DateTime, double[]>();
        static DateTime lastSample = DateTime.MinValue, lastSave = DateTime.UtcNow;
        static bool dirty;

        public static double Tariff = 4.32;   // ₴ за кВт·год
        public static double RestW;           // «Решта ПК» вручну: плата, пам'ять, диски, вентилятори
        public static int Efficiency = 85;    // ККД блока живлення вручну, %
        // «Авто»: решту програма рахує сама з того, що знайде в ПК, а ККД — за навантаженням на блок живлення.
        public static bool RestAuto = true, EffAuto = true;
        public static double AutoRest = double.NaN, EffNow = double.NaN, PsuW;
        public static string RestParts = "";

        // Останній розрахунок (для показу).
        public static double WallW = double.NaN, CpuW, GpuW;
        public static bool CpuEstimated, Measured;

        public static void Load(bool laptop)
        {
            Tariff = Parse(Settings.Get("Tariff", "4.32"), 4.32);
            RestW = Parse(Settings.Get("RestW", laptop ? "8" : "45"), laptop ? 8 : 45);
            Efficiency = (int)Parse(Settings.Get("PsuEff", laptop ? "90" : "85"), 85);
            RestAuto = Settings.GetBool("RestAuto", true);
            EffAuto = Settings.GetBool("EffAuto", true);
            gpuRef = Parse(Settings.Get("PsuGpuRef", "0"), 0);
            System.Threading.ThreadPool.QueueUserWorkItem(_ => ScanHardware());
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    // Рядок: «дата=кВт·год» або «дата=кВт·год;секунд;макс.ЦП;макс.карти».
                    int i = line.IndexOf('=');
                    DateTime d;
                    double v;
                    if (i <= 0 || !DateTime.TryParseExact(line.Substring(0, i), "yyyy-MM-dd", Inv, DateTimeStyles.None, out d)) continue;
                    string[] parts = line.Substring(i + 1).Split(';');
                    if (!double.TryParse(parts[0], NumberStyles.Float, Inv, out v)) continue;
                    days[d] = v;
                    if (parts.Length >= 4)
                    {
                        var st = new double[3];
                        for (int k = 0; k < 3; k++) double.TryParse(parts[k + 1], NumberStyles.Float, Inv, out st[k]);
                        stats[d] = st;
                    }
                }
            }
            catch { }
        }

        static double Parse(string s, double def)
        {
            double v;
            return double.TryParse((s ?? "").Replace(',', '.'), NumberStyles.Float, Inv, out v) ? v : def;
        }

        public static void SaveSettings()
        {
            Settings.Set("Tariff", Tariff.ToString(Inv));
            Settings.Set("RestW", RestW.ToString(Inv));
            Settings.Set("PsuEff", Efficiency.ToString(Inv));
            Settings.SetBool("RestAuto", RestAuto);
            Settings.SetBool("EffAuto", EffAuto);
        }

        public static void Save()
        {
            if (!dirty || Wipe.Done) return;
            try
            {
                Directory.CreateDirectory(Power.StateDir);
                var keep = days.Where(kv => kv.Key >= DateTime.Today.AddDays(-400));
                File.WriteAllLines(FilePath, keep.Select(kv =>
                {
                    string line = kv.Key.ToString("yyyy-MM-dd", Inv) + "=" + kv.Value.ToString("0.#####", Inv);
                    double[] st;
                    if (stats.TryGetValue(kv.Key, out st))
                        line += ";" + Math.Round(st[0]).ToString(Inv) + ";" + Math.Round(st[1]).ToString(Inv) + ";" + Math.Round(st[2]).ToString(Inv);
                    return line;
                }).ToArray());
                dirty = false;
            }
            catch { }
        }

        // Рахує потужність з розетки за знімком датчиків і додає енергію за час від попереднього знімка.
        public static void Add(Snapshot s, bool laptop)
        {
            CpuEstimated = double.IsNaN(s.CpuPower);
            if (!CpuEstimated) CpuW = s.CpuPower;
            else
            {
                double load = Math.Max(0, s.CpuLoad) / 100.0;
                CpuW = laptop ? 3 + 22 * load : 10 + 55 * load;   // приблизно, якщо немає датчика
            }
            GpuW = !double.IsNaN(s.GpuDraw) ? s.GpuDraw : !double.IsNaN(s.GpuPowerLhm) ? s.GpuPowerLhm : 0;
            AutoRest = EstimateRest(s, laptop);
            double rest = RestAuto ? AutoRest : RestW;
            PsuW = PsuCapacity(s, laptop);
            EffNow = EffAuto ? (laptop ? 0.88 : EffCurve((CpuW + GpuW + rest) / PsuW)) : Math.Max(50, Math.Min(100, Efficiency)) / 100.0;
            if (!double.IsNaN(s.BatteryW) && s.BatteryW > 0)
            {
                WallW = s.BatteryW;      // на батареї — справжнє виміряне споживання
                Measured = true;
            }
            else
            {
                WallW = (CpuW + GpuW + rest) / EffNow;
                Measured = false;
            }

            var today = s.Time.ToLocalTime().Date;
            double[] st;
            if (!stats.TryGetValue(today, out st)) { st = new double[3]; stats[today] = st; }
            if (!s.CpuTempFromBoard && !double.IsNaN(s.CpuTemp) && s.CpuTemp > st[1]) { st[1] = s.CpuTemp; dirty = true; }
            if (!double.IsNaN(s.GpuTemp) && s.GpuTemp > st[2]) { st[2] = s.GpuTemp; dirty = true; }
            if (lastSample != DateTime.MinValue)
            {
                double sec = (s.Time - lastSample).TotalSeconds;
                if (sec > 0 && sec <= 20)   // більші проміжки — сон/глибока пауза, не рахуємо
                {
                    double kwh = WallW * sec / 3600.0 / 1000.0;
                    double cur;
                    days.TryGetValue(today, out cur);
                    days[today] = cur + kwh;
                    st[0] += sec;
                    dirty = true;
                }
            }
            lastSample = s.Time;
            if ((DateTime.UtcNow - lastSave).TotalSeconds >= 60) { Save(); lastSave = DateTime.UtcNow; }
        }

        // ── «Авто»: з чого складається решта ПК ──
        static volatile bool scanned;
        static int ramModules, nvme, sataSsd, hdd;
        static bool ddr5;
        static double gpuRef;   // найбільший ліміт/споживання відеокарти — щоб прикинути потужність блока живлення

        static void ScanHardware()
        {
            try
            {
                using (var q = new ManagementObjectSearcher("SELECT SMBIOSMemoryType FROM Win32_PhysicalMemory"))
                    foreach (ManagementObject mo in q.Get())
                        using (mo)
                        {
                            ramModules++;
                            try { if (Convert.ToInt32(mo["SMBIOSMemoryType"]) >= 34) ddr5 = true; } catch { }
                        }
            }
            catch { }
            try
            {
                using (var q = new ManagementObjectSearcher("root\\Microsoft\\Windows\\Storage", "SELECT MediaType, BusType FROM MSFT_PhysicalDisk"))
                    foreach (ManagementObject mo in q.Get())
                        using (mo)
                        {
                            int media = 0, bus = 0;
                            try { media = Convert.ToInt32(mo["MediaType"]); } catch { }
                            try { bus = Convert.ToInt32(mo["BusType"]); } catch { }
                            if (bus == 7) continue;              // USB-диски живляться окремо або мало
                            if (bus == 17) nvme++;
                            else if (media == 3) hdd++;
                            else sataSsd++;
                        }
            }
            catch { }
            scanned = true;
        }

        // Скільки споживає все, крім процесора й відеокарти (у ватах, на виході блока живлення).
        static double EstimateRest(Snapshot s, bool laptop)
        {
            // Плата, чипсет, мережа, USB (клавіатура й мишка) + втрати в живленні процесора на платі (≈13% від його споживання).
            double board = laptop ? 7 : 13.5 + CpuW * 0.13;
            double ram = (ramModules > 0 ? ramModules : 2) * (laptop ? 1.0 : ddr5 ? 2.5 : 2.0);
            double disks = scanned && nvme + sataSsd + hdd > 0 ? nvme * 2.5 + sataSsd * 1.5 + hdd * 6 : 4;
            int fans = s.FanCount > 0 ? s.FanCount : (laptop ? 1 : 3);
            double fanW = fans * (laptop ? 0.8 : 1.5);
            RestParts = "плата " + Math.Round(board) + " + пам'ять " + Math.Round(ram) + " + диски " + Math.Round(disks) +
                        " + вентилятори " + Math.Round(fanW) + " Вт";
            return board + ram + disks + fanW;
        }

        // Потужність блока живлення: прикидаємо за відеокартою (так їх зазвичай і підбирають).
        static double PsuCapacity(Snapshot s, bool laptop)
        {
            if (laptop) return 120;
            double g = Math.Max(double.IsNaN(Gpu.Default) ? 0 : Gpu.Default, GpuW);
            if (!double.IsNaN(s.GpuLimit)) g = Math.Max(g, s.GpuLimit);
            if (g > gpuRef + 1) { gpuRef = g; Settings.Set("PsuGpuRef", Math.Round(g).ToString(Inv)); }
            double cap = Math.Round((gpuRef + 150) * 1.6 / 50) * 50;
            return Math.Max(300, Math.Min(1200, cap));
        }

        // ККД типового блока живлення (80 PLUS Bronze, мережа 230 В) залежно від навантаження.
        static readonly double[,] Curve = { { 0, 0.70 }, { 0.05, 0.76 }, { 0.10, 0.83 }, { 0.20, 0.87 }, { 0.50, 0.90 }, { 0.80, 0.89 }, { 1.00, 0.87 }, { 1.30, 0.84 } };
        public static double EffCurve(double load)
        {
            int n = Curve.GetLength(0);
            if (load <= Curve[0, 0]) return Curve[0, 1];
            for (int i = 1; i < n; i++)
                if (load <= Curve[i, 0])
                {
                    double k = (load - Curve[i - 1, 0]) / (Curve[i, 0] - Curve[i - 1, 0]);
                    return Curve[i - 1, 1] + k * (Curve[i, 1] - Curve[i - 1, 1]);
                }
            return Curve[n - 1, 1];
        }

        public static double Day(DateTime d)
        {
            double v;
            return days.TryGetValue(d.Date, out v) ? v : 0;
        }

        public static double Month(DateTime d)
        {
            return days.Where(kv => kv.Key.Year == d.Year && kv.Key.Month == d.Month).Sum(kv => kv.Value);
        }

        // Прогноз на місяць: середнє за дні, коли ПК працював з програмою, × кількість днів у місяці.
        public static double MonthForecast(DateTime now)
        {
            var month = days.Where(kv => kv.Key.Year == now.Year && kv.Key.Month == now.Month && kv.Value > 0.001).ToList();
            if (month.Count == 0) return 0;
            double elapsed = (now.Day - 1) + now.TimeOfDay.TotalHours / 24.0;
            double sum = month.Sum(kv => kv.Value);
            // Якщо записів мало (програма недавно стоїть), рахуємо за середнім днем із записами.
            double perDay = elapsed >= 3 ? sum / Math.Max(1, elapsed) : sum / Math.Max(1.0, month.Count - 1 + now.TimeOfDay.TotalHours / 24.0);
            return perDay * DateTime.DaysInMonth(now.Year, now.Month);
        }

        public static List<KeyValuePair<DateTime, double>> LastDays(int n)
        {
            var list = new List<KeyValuePair<DateTime, double>>();
            for (int i = n - 1; i >= 0; i--)
            {
                var d = DateTime.Today.AddDays(-i);
                list.Add(new KeyValuePair<DateTime, double>(d, Day(d)));
            }
            return list;
        }

        // ── Підсумок тижня (понеділок — неділя) ──
        public class WeekStat
        {
            public DateTime Start;
            public double Kwh, Hours, MaxCpu, MaxGpu;
            public bool HasData { get { return Hours >= 0.25 || Kwh > 0.001; } }
        }

        public static DateTime WeekStart(DateTime d)
        {
            return d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        }

        public static WeekStat Week(DateTime monday)
        {
            var w = new WeekStat { Start = monday.Date };
            for (int i = 0; i < 7; i++)
            {
                var d = w.Start.AddDays(i);
                w.Kwh += Day(d);
                double[] st;
                if (!stats.TryGetValue(d, out st)) continue;
                w.Hours += st[0] / 3600.0;
                w.MaxCpu = Math.Max(w.MaxCpu, st[1]);
                w.MaxGpu = Math.Max(w.MaxGpu, st[2]);
            }
            return w;
        }

        public static string Hours(double h)
        {
            if (h < 1) return Math.Round(h * 60) + " хв";
            return (h < 10 ? h.ToString("0.#") : Math.Round(h).ToString("0")) + " год";
        }

#if LINUXTEST
        public static void DemoSeed()
        {
            var mon = WeekStart(DateTime.Today);
            var rnd = new Random(7);
            for (int i = -10; i <= 0; i++)
            {
                var d = DateTime.Today.AddDays(i);
                days[d] = 0.6 + rnd.NextDouble() * 0.9;
                stats[d] = new double[] { 3600 * (2 + rnd.NextDouble() * 5), 60 + rnd.Next(20), 50 + rnd.Next(25) };
            }
        }
#endif

        public static string Money(double uah, bool exact)
        {
            if (!exact && uah >= 100) return Math.Round(uah).ToString("N0") + " ₴";
            return uah.ToString(uah < 10 || exact ? "0.00" : "0.0") + " ₴";
        }

        public static string Kwh(double kwh) { return kwh.ToString(kwh < 10 ? "0.00" : "0.0") + " кВт·год"; }
    }

    // Стан батареї ноутбука (з драйвера батареї через WMI root\wmi).
    class BatteryInfo
    {
        public bool Present;
        public double DesignMWh, FullMWh, RemainingMWh, Cycles = double.NaN, RateW = double.NaN;
        public bool Charging, Discharging, PowerOnline;
        public string Name = "";

        public double HealthPct { get { return DesignMWh > 0 && FullMWh > 0 ? FullMWh / DesignMWh * 100 : double.NaN; } }
        public double ChargePct { get { return FullMWh > 0 ? Math.Min(100, RemainingMWh / FullMWh * 100) : double.NaN; } }

        static IEnumerable<ManagementObject> Query(string cls)
        {
            var list = new List<ManagementObject>();
            try
            {
                using (var q = new ManagementObjectSearcher("root\\wmi", "SELECT * FROM " + cls))
                    foreach (ManagementObject mo in q.Get()) list.Add(mo);
            }
            catch { }
            return list;
        }

        static double D(ManagementObject mo, string prop)
        {
            try { object v = mo[prop]; return v == null ? double.NaN : Convert.ToDouble(v); } catch { return double.NaN; }
        }

        static bool B(ManagementObject mo, string prop)
        {
            try { object v = mo[prop]; return v != null && Convert.ToBoolean(v); } catch { return false; }
        }

        // full = false: лише заряд і потужність (швидко, для лічильника енергії).
        public static BatteryInfo Read(bool full)
        {
            var b = new BatteryInfo();
            foreach (var mo in Query("BatteryStatus"))
            {
                using (mo)
                {
                    b.Present = true;
                    double rem = D(mo, "RemainingCapacity");
                    if (!double.IsNaN(rem)) b.RemainingMWh += rem;
                    b.Charging |= B(mo, "Charging");
                    b.Discharging |= B(mo, "Discharging");
                    b.PowerOnline |= B(mo, "PowerOnline");
                    double dr = D(mo, "DischargeRate"), cr = D(mo, "ChargeRate");
                    if (b.Discharging && dr > 0 && dr < 500000) b.RateW = (double.IsNaN(b.RateW) ? 0 : b.RateW) + dr / 1000.0;
                    else if (b.Charging && cr > 0 && cr < 500000) b.RateW = (double.IsNaN(b.RateW) ? 0 : b.RateW) + cr / 1000.0;
                }
            }
            foreach (var mo in Query("BatteryFullChargedCapacity"))
                using (mo) { double v = D(mo, "FullChargedCapacity"); if (v > 0) b.FullMWh += v; }
            if (!full) return b;
            foreach (var mo in Query("BatteryStaticData"))
            {
                using (mo)
                {
                    double v = D(mo, "DesignedCapacity");
                    if (v > 0) b.DesignMWh += v;
                    try
                    {
                        string n = Convert.ToString(mo["DeviceName"]).Trim();
                        string m = Convert.ToString(mo["ManufactureName"]).Trim();
                        if (b.Name == "") b.Name = (m.Length > 0 && !n.StartsWith(m) ? m + " " : "") + n;
                    }
                    catch { }
                }
            }
            foreach (var mo in Query("BatteryCycleCount"))
                using (mo) { double v = D(mo, "CycleCount"); if (v > 0) b.Cycles = (double.IsNaN(b.Cycles) ? 0 : b.Cycles) + v; }
            return b;
        }

        // Пояснення в стилі iPhone.
        public string Verdict()
        {
            double h = HealthPct;
            if (double.IsNaN(h)) return "Драйвер батареї не повідомляє ємність, тому знос оцінити не вдалося.";
            if (h >= 80) return "Батарея в нормі: тримає заряд майже як нова й підтримує звичайну роботу ноутбука.";
            if (h >= 60) return "Ємність помітно зменшилась: від одного заряду ноутбук працює менше, ніж новий. Заміна поки не обов'язкова.";
            return "Батарея сильно зношена: ноутбук швидко сідає й може раптово вимикатися. Варто подумати про заміну.";
        }
    }
}
