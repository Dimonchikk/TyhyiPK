// Тихий ПК — «Мережа й диск» на сторінці «Показники»: швидкість інтернету, робота диска і вільне місце.
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;

namespace TyhyiPK
{
    partial class MainForm
    {
        long netRx = -1, netTx = -1;
        DateTime netAt;
        volatile PerformanceCounter diskRead, diskWrite;
        volatile bool diskTried, diskFailed;

        static string SysDrive()
        {
            try { return Path.GetPathRoot(Environment.SystemDirectory).TrimEnd('\\'); } catch { return "C:"; }
        }

        static string Bits(double bytesPerSec)
        {
            double mbit = bytesPerSec * 8 / 1e6;
            if (mbit >= 100) return Math.Round(mbit) + " Мбіт/с";
            if (mbit >= 1) return mbit.ToString("0.0") + " Мбіт/с";
            return Math.Round(bytesPerSec * 8 / 1e3) + " Кбіт/с";
        }

        static string Bytes(double bytesPerSec)
        {
            double mb = bytesPerSec / 1048576.0;
            if (mb >= 10) return Math.Round(mb) + " МБ/с";
            if (mb >= 0.1) return mb.ToString("0.0") + " МБ/с";
            return Math.Round(bytesPerSec / 1024) + " КБ/с";
        }

        void ShowNetDisk()
        {
            // Мережа: різниця переданих байтів з минулого разу по всіх увімкнених адаптерах.
            try
            {
                long rx = 0, tx = 0;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPv4Statistics();
                    rx += st.BytesReceived; tx += st.BytesSent;
                }
                var now = DateTime.UtcNow;
                if (netRx >= 0)
                {
                    double sec = Math.Max(0.5, (now - netAt).TotalSeconds);
                    double down = Math.Max(0, (rx - netRx) / sec), up = Math.Max(0, (tx - netTx) / sec);
                    sNetDown.Set(Bits(down), null, "зараз", false);
                    sNetUp.Set(Bits(up), null, "зараз", false);
                }
                else
                {
                    sNetDown.Set("…", Ui.Ink3, "", false);
                    sNetUp.Set("…", Ui.Ink3, "", false);
                }
                netRx = rx; netTx = tx; netAt = now;
            }
            catch { sNetDown.Set("—", Ui.Ink3, "немає даних", false); sNetUp.Set("—", Ui.Ink3, "немає даних", false); }

            // Диск: читання + запис усіх фізичних дисків (лічильники Windows).
            try
            {
                if (!diskTried)
                {
                    // Лічильники Windows відкриваються повільно — у фоні, щоб вікно не підвисало.
                    diskTried = true;
                    sDiskIo.Set("…", Ui.Ink3, "", false);
                    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            var r = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
                            var w = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
                            r.NextValue(); w.NextValue();
                            diskWrite = w; diskRead = r;
                        }
                        catch { diskFailed = true; }
                    });
                }
                else if (diskRead != null && diskWrite != null)
                {
                    double r = diskRead.NextValue(), w = diskWrite.NextValue();
                    sDiskIo.Set(Bytes(r + w), null, "читання + запис", false);
                }
                else if (diskFailed) sDiskIo.Set("—", Ui.Ink3, "немає даних", false);
            }
            catch { diskRead = diskWrite = null; sDiskIo.Set("—", Ui.Ink3, "немає даних", false); }

            try
            {
                var d = new DriveInfo(SysDrive());
                double free = d.AvailableFreeSpace / 1073741824.0, total = d.TotalSize / 1073741824.0;
                int pct = (int)Math.Round(100 * free / Math.Max(1, total));
                sDiskFree.Set(Math.Round(free) + " ГБ", pct < 10 ? Ui.Red : pct < 20 ? Ui.Orange : (System.Drawing.Color?)null,
                    "з " + Math.Round(total) + " ГБ · " + pct + "%", false);
            }
            catch { sDiskFree.Set("—", Ui.Ink3, "немає даних", false); }
        }
    }
}
