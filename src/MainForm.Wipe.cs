// Тихий ПК — кнопка «Видалити всі дані програми».
using System;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        void WipeAll()
        {
            var ask = MessageBox.Show(this,
                "Видалити всі дані «Тихого ПК» з цього комп'ютера?\n\n" +
                "• процесор і відеокарта повернуться до звичайного режиму\n" +
                "• запуск разом з Windows буде вимкнено\n" +
                "• програми, які ти вимкнув на вкладці «Автозапуск», повернуться на місце\n" +
                "• налаштування, лічильник енергії й списки ігор буде стерто\n\n" +
                "Після цього програма закриється.",
                "Видалити всі дані", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (ask != DialogResult.Yes) return;

            bool exe = MessageBox.Show(this,
                "Видалити також саму програму (TyhyiPK.exe, ярлики й пункт у «Параметри → Програми»)?\n\nЯкщо вибрати «Ні», програма залишиться, і її можна буде запустити знову.",
                "Видалити всі дані", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

            stop = true;
            autoTimer.Stop();
            procTimer.Stop();
            Cursor = Cursors.WaitCursor;
            Enabled = false;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                var problems = Wipe.Run(exe);
                try
                {
                    BeginInvoke(new MethodInvoker(() =>
                    {
                        Cursor = Cursors.Default;
                        var sb = new StringBuilder("Готово, усі дані програми видалено.");
                        if (problems.Count > 0)
                        {
                            sb.Append("\n\nАле є зауваження:");
                            foreach (string p in problems) sb.Append("\n• " + p);
                        }
                        MessageBox.Show(this, sb.ToString(), "Тихий ПК", MessageBoxButtons.OK,
                            problems.Count > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                        ExitApp();
                    }));
                }
                catch { }
            });
        }
    }
}
