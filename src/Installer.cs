// Тихий ПК — встановлення. Інсталятор Tykhyi-PK-setup.exe — це сама програма (без обгорток):
// якщо файл називається «…setup…» або запущений з ключем /install, він ставить себе в папку користувача.
// Без PowerShell, без прав адміністратора й без автозапуску: запуск разом з Windows користувач вмикає сам у програмі.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TyhyiPK
{
    static class Installer
    {
        public const string Version = AppVersion.Text;
        public static string Dir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\TyhyiPK"); }
        }

        static void Fail(string text)
        {
            MessageBox.Show(text, "Встановлення «Тихого ПК»", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        // Закрити інші копії програми, щоб можна було замінити файл.
        static bool CloseOthers()
        {
            int self = Process.GetCurrentProcess().Id;
            var left = new List<int>();
            foreach (var p in Process.GetProcessesByName("TyhyiPK"))
            {
                using (p)
                {
                    if (p.Id == self) continue;
                    try { p.Kill(); p.WaitForExit(3000); }
                    catch { left.Add(p.Id); }   // працює з правами адміністратора — закриємо з дозволом
                }
            }
            if (left.Count == 0) return true;
            try
            {
                var psi = new ProcessStartInfo("taskkill.exe", "/f /pid " + string.Join(" /pid ", left));
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                using (var k = Process.Start(psi)) if (k != null) k.WaitForExit(10000);
                Thread.Sleep(500);
                return true;
            }
            catch
            {
                return MessageBox.Show("Закрий «Тихий ПК» біля годинника (правою кнопкою → Вийти) і натисни «OK».", "Встановлення «Тихого ПК»",
                    MessageBoxButtons.OKCancel, MessageBoxIcon.Information) == DialogResult.OK;
            }
        }

        static bool CopyWithRetry(string from, string to)
        {
            for (int i = 0; i < 10; i++)
            {
                try { File.Copy(from, to, true); return true; }
                catch { Thread.Sleep(500); }
            }
            return false;
        }

        static void Shortcut(string lnkPath, string target)
        {
            object sh = null, lnk = null;
            try
            {
                var t = Type.GetTypeFromProgID("WScript.Shell");
                sh = Activator.CreateInstance(t);
                lnk = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnkPath });
                var lt = lnk.GetType();
                lt.InvokeMember("TargetPath", BindingFlags.SetProperty, null, lnk, new object[] { target });
                lt.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(target) });
                lt.InvokeMember("Description", BindingFlags.SetProperty, null, lnk, new object[] { "Тихий ПК" });
                lt.InvokeMember("Save", BindingFlags.InvokeMethod, null, lnk, null);
            }
            catch { }
            finally
            {
                if (lnk != null) Marshal.ReleaseComObject(lnk);
                if (sh != null) Marshal.ReleaseComObject(sh);
            }
        }

        public static void Install()
        {
            string me = Application.ExecutablePath;
            string src = Path.GetDirectoryName(me);
            string exe = Path.Combine(Dir, "TyhyiPK.exe");
            bool update = File.Exists(exe);
            if (MessageBox.Show(update
                    ? "Оновити «Тихий ПК» до версії " + Version + "?\n\nПрограма закриється й запуститься знову. Налаштування й історія збережуться."
                    : "Встановити «Тихий ПК» " + Version + "?\n\nПрограма встановиться в " + Dir + ", з'являться ярлики на Робочому столі й у меню «Пуск».",
                    "Встановлення «Тихого ПК»", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            if (!CloseOthers()) return;
            try { Directory.CreateDirectory(Dir); }
            catch (Exception ex) { Fail("Не вдалося створити папку " + Dir + ":\n" + ex.Message); return; }
            if (!string.Equals(Path.GetFullPath(me), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase) && !CopyWithRetry(me, exe))
            {
                Fail("Не вдалося скопіювати програму в " + Dir + ". Закрий «Тихий ПК» і спробуй ще раз.");
                return;
            }
            // Бібліотеки датчиків — звичайними файлами в папку lib.
            Lhm.Extract(Path.Combine(Dir, "lib"));

            // Ярлики на Робочому столі й у меню «Пуск».
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Тихий ПК.lnk"), exe);
            Shortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Тихий ПК.lnk"), exe);

            // Пункт у «Параметри → Програми».
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\TyhyiPK"))
                {
                    k.SetValue("DisplayName", "Тихий ПК");
                    k.SetValue("DisplayIcon", exe);
                    k.SetValue("DisplayVersion", Version);
                    k.SetValue("Publisher", "Дмитро Бричнік");
                    k.SetValue("InstallLocation", Dir);
                    k.SetValue("UninstallString", "\"" + exe + "\" /uninstall");
                    k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    k.SetValue("EstimatedSize", (int)(new FileInfo(exe).Length / 1024), RegistryValueKind.DWord);
                }
            }
            catch { }

            // Драйвер PawnIO не входить в інсталятор — його ставлять з офіційного сайту, за бажанням.
            if (!update && !Lhm.PawnIoInstalled() &&
                MessageBox.Show("«Тихий ПК» встановлено.\n\nЩоб бачити температуру, споживання й оберти кулера процесора, потрібен безкоштовний драйвер датчиків PawnIO з цифровим підписом. " +
                    "Відкрити його офіційну сторінку pawnio.eu?\n\nЦе можна зробити й пізніше: «Датчики» → «як увімкнути?».",
                    "Встановлення «Тихого ПК»", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo("https://pawnio.eu/") { UseShellExecute = true }); } catch { }
            }

            try { Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Dir }); } catch { }
        }
    }
}
