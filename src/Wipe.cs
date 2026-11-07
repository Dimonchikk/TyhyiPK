// Тихий ПК — повне видалення власних даних програми з комп'ютера.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace TyhyiPK
{
    static class Wipe
    {
        // Після очищення програма нічого не записує назад на диск.
        public static volatile bool Done;

        // Повертає список того, що не вдалося зробити (порожній — усе гаразд).
        public static List<string> Run(bool deleteExe)
        {
            var problems = new List<string>();
            Done = true;

            // 1. Процесор і відеокарта — назад до звичайного режиму.
            // Якщо програма ніколи не міняла режим, збережених початкових налаштувань немає, і чіпати нічого не треба.
            try { if (Power.HasSavedState && !Power.Apply("normal")) problems.Add("не вдалося повернути звичайний режим процесора"); }
            catch { problems.Add("не вдалося повернути звичайний режим процесора"); }
            try
            {
                if (Gpu.CanLimit && !Gpu.Apply("normal"))
                    problems.Add("ліміт відеокарти не скинуто (потрібні права адміністратора). Перезавантаж ПК, і він скинеться сам");
            }
            catch { }
            Power.RemoveOwnScheme();

            // 2. Запуск разом з Windows.
            try { Autostart.Set(false); } catch { problems.Add("не вдалося прибрати автозапуск"); }

            // 3. Програми, які ти вимкнув на вкладці «Автозапуск», повертаються на місце.
            int back, failed;
            Autoruns.RestoreDisabled(out back, out failed);
            if (failed > 0) problems.Add("не вдалося повернути в автозапуск програм: " + failed + " (потрібні права адміністратора)");

            // 4. Ключ реєстру, файл завдання й папка з даними.
            try { Registry.CurrentUser.DeleteSubKeyTree("Software\\TyhyiPK", false); } catch { }
            try { File.Delete(Path.Combine(Path.GetTempPath(), "TyhyiPK_task.xml")); } catch { }
            try { if (Directory.Exists(Power.StateDir)) Directory.Delete(Power.StateDir, true); }
            catch { problems.Add("не вдалося видалити папку " + Power.StateDir); }

            // 5. Сама програма: ярлики, пункт у «Параметри → Програми» і файл (після того, як вона закриється).
            if (deleteExe)
            {
                foreach (var f in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.Programs })
                {
                    try
                    {
                        string lnk = Path.Combine(Environment.GetFolderPath(f), "Тихий ПК.lnk");
                        if (File.Exists(lnk)) File.Delete(lnk);
                    }
                    catch { }
                }
                try { Registry.CurrentUser.DeleteSubKeyTree("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\TyhyiPK", false); } catch { }
                try
                {
                    string exe = System.Windows.Forms.Application.ExecutablePath;
                    string dir = Path.GetDirectoryName(exe);
                    // Папку встановлення видаляємо цілком лише якщо це саме наша папка з інсталятора.
                    string own = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs\\TyhyiPK");
                    bool ownDir = string.Equals(dir.TrimEnd('\\'), own, StringComparison.OrdinalIgnoreCase);
                    string cmd = ownDir ? "rmdir /s /q \"" + dir + "\"" : "del /f /q \"" + exe + "\"";
                    var psi = new ProcessStartInfo("cmd.exe", "/c ping 127.0.0.1 -n 4 >nul & " + cmd);
                    psi.CreateNoWindow = true;
                    psi.UseShellExecute = false;
                    Process.Start(psi);
                }
                catch { problems.Add("не вдалося видалити сам файл програми"); }
            }
            return problems;
        }
    }
}
