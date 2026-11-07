// Тихий ПК — дані датчиків і збережені налаштування.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace TyhyiPK
{
    [StructLayout(LayoutKind.Sequential)]
    class MemoryStatus
    {
        public uint Length = (uint)Marshal.SizeOf(typeof(MemoryStatus));
        public uint Load;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }

    class Snapshot
    {
        public int CpuLoad = -1;
        public double CpuTemp = double.NaN, GpuTemp = double.NaN, GpuDraw = double.NaN, GpuLimit = double.NaN,
            CpuPower = double.NaN, Fan = double.NaN, MemUsed = double.NaN, MemTotal = double.NaN;
        public bool CpuTempFromBoard, FanIsCpu;
        public DateTime Time = DateTime.UtcNow;
        public double GpuPowerLhm = double.NaN;   // споживання карти AMD/NVIDIA з LibreHardwareMonitor (якщо немає nvidia-smi)
        public double BatteryW = double.NaN;      // споживання з батареї (лише коли ноутбук не на зарядці)
        public double BatteryPct = double.NaN, BatteryRemainMWh = double.NaN;
        public bool BatteryOnAc = true, BatteryCharging;
    }

    class TempSample
    {
        public DateTime T;
        public double Cpu, Gpu;
    }

    static class Settings
    {
        static readonly string FilePath = Path.Combine(Power.StateDir, "settings.txt");
        static Dictionary<string, string> data;
        static readonly object sync = new object();

        static void Load()
        {
            if (data != null) return;
            data = new Dictionary<string, string>();
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) data[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
            }
            catch { }
        }

        public static string Get(string key, string def)
        {
            lock (sync)
            {
                Load();
                string v;
                return data.TryGetValue(key, out v) ? v : def;
            }
        }

        public static bool GetBool(string key, bool def) { return Get(key, def ? "1" : "0") == "1"; }

        public static void Set(string key, string value)
        {
            if (Wipe.Done) return;
            lock (sync)
            {
                Load();
                data[key] = value;
                try
                {
                    Directory.CreateDirectory(Power.StateDir);
                    File.WriteAllLines(FilePath, data.Select(kv => kv.Key + "=" + kv.Value).ToArray());
                }
                catch { }
            }
        }

        public static void SetBool(string key, bool value) { Set(key, value ? "1" : "0"); }

        public static List<string> LoadList(string name)
        {
            try
            {
                string p = Path.Combine(Power.StateDir, name);
                if (File.Exists(p)) return File.ReadAllLines(p).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
            }
            catch { }
            return new List<string>();
        }

        public static void SaveList(string name, List<string> list)
        {
            if (Wipe.Done) return;
            try
            {
                Directory.CreateDirectory(Power.StateDir);
                File.WriteAllLines(Path.Combine(Power.StateDir, name), list.ToArray());
            }
            catch { }
        }
    }
}
