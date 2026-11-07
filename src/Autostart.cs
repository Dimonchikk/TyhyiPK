// Тихий ПК — запуск разом з Windows (планувальник завдань з правами або ключ Run).
using System;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TyhyiPK
{
    static class Autostart
    {
        const string TaskName = "TyhyiPK";
        const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

        static bool TaskExists()
        {
            string o;
            return Tool.Run("schtasks.exe", "/query /tn " + TaskName, out o, 10000) == 0;
        }

        static bool RunKeyExists()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(RunKey))
                    return k != null && k.GetValue(TaskName) != null;
            }
            catch { return false; }
        }

        static void SetRunKey(bool on)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) k.SetValue(TaskName, "\"" + Application.ExecutablePath + "\" /tray");
                    else k.DeleteValue(TaskName, false);
                }
            }
            catch { }
        }

        public static bool Enabled() { return TaskExists() || RunKeyExists(); }

        public static void Set(bool on)
        {
            if (!on)
            {
                SetRunKey(false);
                if (TaskExists()) Tool.Run("schtasks.exe", "/delete /tn " + TaskName + " /f");
                return;
            }
            if (Program.IsAdmin && CreateTask()) SetRunKey(false);
            else SetRunKey(true);
        }

        static bool CreateTask()
        {
            try
            {
                string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
                string exe = SecurityElement.Escape(Application.ExecutablePath);
                string xml =
                    "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                    "  <RegistrationInfo><Description>Тихий ПК</Description></RegistrationInfo>\r\n" +
                    "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>\r\n" +
                    "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                    "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>\r\n" +
                    "  <Actions Context=\"Author\"><Exec><Command>" + exe + "</Command><Arguments>/tray</Arguments></Exec></Actions>\r\n</Task>\r\n";
                string path = Path.Combine(Path.GetTempPath(), "TyhyiPK_task.xml");
                File.WriteAllText(path, xml, Encoding.Unicode);
                string o;
                int code = Tool.Run("schtasks.exe", "/create /tn " + TaskName + " /xml \"" + path + "\" /f", out o, 15000);
                try { File.Delete(path); } catch { }
                return code == 0;
            }
            catch { return false; }
        }
    }
}
