// Тихий ПК — точка входу.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace TyhyiPK
{
    static class Program
    {
        public static bool IsAdmin;

        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        // Ключі запуску: /tray — стартувати схованим біля годинника, /restart — перезапуск самої програми,
        // /noadmin — не просити права адміністратора, /dark — темна тема, /wipe — стерти всі дані й вийти, /install — встановити, /uninstall — видалити програму.
        [STAThread]
        static void Main(string[] args)
        {
            bool tray = args.Any(a => a.Equals("/tray", StringComparison.OrdinalIgnoreCase));
            bool restart = args.Any(a => a.Equals("/restart", StringComparison.OrdinalIgnoreCase));
            bool noAdmin = args.Any(a => a.Equals("/noadmin", StringComparison.OrdinalIgnoreCase));
            bool dark = args.Any(a => a.Equals("/dark", StringComparison.OrdinalIgnoreCase));

            try { IsAdmin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { IsAdmin = false; }

            // Інсталятор — це сама програма під назвою «Tykhyi-PK-setup.exe» (або ключ /install).
            // Ставиться без прав адміністратора: усе в папку користувача.
            bool asSetup = System.IO.Path.GetFileNameWithoutExtension(Application.ExecutablePath).IndexOf("setup", StringComparison.OrdinalIgnoreCase) >= 0;
            if (asSetup || args.Any(a => a.Equals("/install", StringComparison.OrdinalIgnoreCase)))
            {
                try { SetProcessDPIAware(); } catch { }
                Application.EnableVisualStyles();
                Installer.Install();
                return;
            }

            if (!IsAdmin && !noAdmin)
            {
                try
                {
                    var psi = new ProcessStartInfo(Application.ExecutablePath, string.Join(" ", args));
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    Process.Start(psi);
                    return;
                }
                catch { }
            }

            // /wipe — тихе очищення без вікна (його викликає програма видалення).
            if (args.Any(a => a.Equals("/wipe", StringComparison.OrdinalIgnoreCase)))
            {
                try { Gpu.Detect(); } catch { }
                Wipe.Run(false);
                return;
            }

            try { SetProcessDPIAware(); } catch { }
            try
            {
                using (var g = Graphics.FromHwnd(IntPtr.Zero))
                    Ui.Scale = Math.Max(1f, g.DpiX / 96f);
            }
            catch { }
            Ui.ApplyTheme(dark || MainForm.WantDark());
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // /uninstall — видалення з «Параметри → Програми».
            if (args.Any(a => a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                Uninstall();
                return;
            }

            Mutex mutex;
            try { mutex = new Mutex(false, "TyhyiPK_SingleInstance"); }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Тихий ПК уже працює — його значок біля годинника.", "Тихий ПК");
                return;
            }
            bool owned;
            try { owned = mutex.WaitOne(restart ? 10000 : 0); }
            catch (AbandonedMutexException) { owned = true; }
            EventWaitHandle show = null;
            try { show = new EventWaitHandle(false, EventResetMode.AutoReset, "TyhyiPK_Show"); } catch { }
            if (!owned)
            {
                if (show != null) show.Set();
                return;
            }
            Application.ThreadException += (s, e) =>
                MessageBox.Show("Щось пішло не так:\n" + e.Exception.Message, "Тихий ПК", MessageBoxButtons.OK, MessageBoxIcon.Error);
            try { Application.Run(new MainForm(tray, show)); }
            catch (Exception ex)
            {
                MessageBox.Show("Щось пішло не так:\n" + ex.Message, "Тихий ПК", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            GC.KeepAlive(mutex);
        }

        static void Uninstall()
        {
            if (MessageBox.Show("Видалити «Тихий ПК» з комп'ютера?\n\n" +
                    "Процесор і відеокарта повернуться до звичайного режиму, автозапуск, налаштування, ярлики й сама програма будуть видалені.\n\n" +
                    "Драйвер датчиків PawnIO (якщо встановлено) залишиться — його можна прибрати окремо в «Параметри → Програми».",
                    "Видалення «Тихого ПК»", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int self = Process.GetCurrentProcess().Id;
            foreach (var p in Process.GetProcessesByName("TyhyiPK"))
            {
                using (p)
                {
                    if (p.Id == self) continue;
                    try { p.Kill(); p.WaitForExit(3000); } catch { }
                }
            }
            try { Gpu.Detect(); } catch { }
            var problems = Wipe.Run(true);
            MessageBox.Show(problems.Count == 0 ? "«Тихий ПК» видалено." : "«Тихий ПК» видалено, але є зауваження:\n• " + string.Join("\n• ", problems),
                "Видалення «Тихого ПК»", MessageBoxButtons.OK, problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
    }
}
