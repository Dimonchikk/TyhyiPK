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
        static DateTime lastSample = DateTime.MinValue, lastSave = DateTime.UtcNow;
        static bool dirty;

        public static double Tariff = 4.32;   // ₴ за кВт·год
        public static double RestW;           // плата, пам'ять, диски, вентилятори, монітор
        public static int Efficiency = 85;    // ККД блока живлення, %

        // Останній розрахунок (для показу).
        public static double WallW = double.NaN, CpuW, GpuW;
        public static bool CpuEstimated, Measured;

        public static void Load(bool laptop)
        {
            Tariff = Parse(Settings.Get("Tariff", "4.32"), 4.32);
            RestW = Parse(Settings.Get("RestW", laptop ? "8" : "45"), laptop ? 8 : 45);
            Efficiency = (int)Parse(Settings.Get("PsuEff", laptop ? "90" : "85"), 85);
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    DateTime d;
                    double v;
                    if (i > 0 && DateTime.TryParseExact(line.Substring(0, i), "yyyy-MM-dd", Inv, DateTimeStyles.None, out d) &&
                        double.TryParse(line.Substring(i + 1), NumberStyles.Float, Inv, out v))
                        days[d] = v;
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
        }

        public static void Save()
        {
            if (!dirty || Wipe.Done) return;
            try
            {
                Directory.CreateDirectory(Power.StateDir);
                var keep = days.Where(kv => kv.Key >= DateTime.Today.AddDays(-400));
                File.WriteAllLines(FilePath, keep.Select(kv => kv.Key.ToString("yyyy-MM-dd", Inv) + "=" + kv.Value.ToString("0.#####", Inv)).ToArray());
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
            if (!double.IsNaN(s.BatteryW) && s.BatteryW > 0)
            {
                WallW = s.BatteryW;      // на батареї — справжнє виміряне споживання
                Measured = true;
            }
            else
            {
                WallW = (CpuW + GpuW + RestW) / (Math.Max(50, Math.Min(100, Efficiency)) / 100.0);
                Measured = false;
            }

            if (lastSample != DateTime.MinValue)
            {
                double sec = (s.Time - lastSample).TotalSeconds;
                if (sec > 0 && sec <= 20)   // більші проміжки — сон/глибока пауза, не рахуємо
                {
                    var day = s.Time.ToLocalTime().Date;
                    double kwh = WallW * sec / 3600.0 / 1000.0;
                    double cur;
                    days.TryGetValue(day, out cur);
                    days[day] = cur + kwh;
                    dirty = true;
                }
            }
            lastSample = s.Time;
            if ((DateTime.UtcNow - lastSave).TotalSeconds >= 60) { Save(); lastSave = DateTime.UtcNow; }
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
