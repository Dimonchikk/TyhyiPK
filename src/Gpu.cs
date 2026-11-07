// Тихий ПК — ліміт споживання відеокарти NVIDIA через nvidia-smi.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace TyhyiPK
{
    static class Gpu
    {
        public static string Smi, Name;
        public static double Default = double.NaN, Min = double.NaN, Max = double.NaN, StartLimit = double.NaN;
        public static bool CanLimit;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static string FindSmi()
        {
            var list = new List<string>
            {
                Path.Combine(Environment.SystemDirectory, "nvidia-smi.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation\\NVSMI\\nvidia-smi.exe"),
            };
            try
            {
                foreach (string d in Directory.GetDirectories(Path.Combine(Environment.SystemDirectory, "DriverStore\\FileRepository"), "nv*"))
                    list.Add(Path.Combine(d, "nvidia-smi.exe"));
            }
            catch { }
            foreach (string p in list) if (File.Exists(p)) return p;
            return null;
        }

        // [0] заводський ліміт, [1] мін., [2] макс., [3] поточний ліміт, [4] споживання, [5] температура
        public static bool Query(out double[] v)
        {
            v = null;
            string o;
            if (!(Smi != null && Tool.Run(Smi, "-i 0 --query-gpu=power.default_limit,power.min_limit,power.max_limit,power.limit,power.draw,temperature.gpu,name --format=csv,noheader,nounits", out o, 6000) == 0))
                return false;
            string line = o.Split('\n').Select(s => s.Trim()).FirstOrDefault(s => s.Length > 0);
            if (line == null) return false;
            string[] parts = line.Split(',');
            if (parts.Length < 7) return false;
            v = new double[6];
            for (int i = 0; i < 6; i++)
            {
                double d;
                v[i] = double.TryParse(parts[i].Trim(), NumberStyles.Float, Inv, out d) ? d : double.NaN;
            }
            Name = string.Join(",", parts.Skip(6)).Trim();
            return true;
        }

        public static void Detect()
        {
            Smi = FindSmi();
            double[] v;
            if (!Query(out v)) return;
            Default = v[0]; Min = v[1]; Max = v[2]; StartLimit = v[3];
            CanLimit = !double.IsNaN(Default) && !double.IsNaN(Min) && !double.IsNaN(Max) && Default > 0 && Max >= Min;
        }

        public static bool HasHeadroom { get { return CanLimit && Max - Default >= 2; } }

        public static double Target(string mode)
        {
            double t;
            switch (mode)
            {
                case "silent": t = Default * 0.7; break;
                case "quiet": t = Default * 0.85; break;
                case "boost": t = Default + (Max - Default) / 2; break;
                case "super": t = Max; break;
                default: t = Default; break;
            }
            return Math.Max(Min, Math.Min(Max, Math.Round(t)));
        }

        public static bool Apply(string mode)
        {
            if (!CanLimit) return false;
            string o;
            return Tool.Run(Smi, "-i 0 -pl " + Target(mode).ToString("0", Inv), out o, 10000) == 0;
        }

        public static string ModeFor(double limit, string preferred)
        {
            if (double.IsNaN(limit) || !CanLimit) return "normal";
            if (preferred != null && Math.Abs(limit - Target(preferred)) < 1.5) return preferred;
            string best = "normal";
            double bestD = Math.Abs(limit - Target("normal"));
            foreach (string k in Modes.Keys)
            {
                double d = Math.Abs(limit - Target(k));
                if (d < bestD - 0.5) { best = k; bestD = d; }
            }
            return best;
        }

        public static string Desc(string mode)
        {
            string w = Target(mode).ToString("0") + " Вт";
            switch (mode)
            {
                case "silent": return "Ліміт споживання 70% (" + w + "). Карта помітно холодніша й тихіша, але FPS у важких іграх нижчий.";
                case "quiet": return "Ліміт споживання 85% (" + w + "). Тихіше й холодніше, а FPS майже не падає.";
                case "normal": return "Заводський ліміт (" + w + "), як задумав виробник.";
                case "boost":
                    return !HasHeadroom
                        ? "Ця відеокарта не дозволяє підняти ліміт вище заводського (" + w + "), тож це те саме, що «Звичайний»."
                        : "Ліміт піднято до " + w + ". Трохи більше FPS у важких іграх, але гарячіше й гучніше.";
                default:
                    return !HasHeadroom
                        ? "Ця відеокарта не дозволяє підняти ліміт вище заводського (" + w + "), тож це те саме, що «Звичайний»."
                        : "Максимальний ліміт, який дозволяє карта: " + w + ". Найбільше FPS, найгарячіше й найгучніше.";
            }
        }
    }
}
