// Тихий ПК — смужка на кнопці програми на панелі завдань (стандартна можливість Windows 7+, ITaskbarList3).
// Зелена — спокійно, жовта — помітне навантаження, червона — на межі.
using System;
using System.Runtime.InteropServices;

namespace TyhyiPK
{
    static class TaskbarBar
    {
        [ComImport, Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface ITaskbarList3
        {
            void HrInit();
            void AddTab(IntPtr hwnd);
            void DeleteTab(IntPtr hwnd);
            void ActivateTab(IntPtr hwnd);
            void SetActiveAlt(IntPtr hwnd);
            void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
            void SetProgressValue(IntPtr hwnd, ulong completed, ulong total);
            void SetProgressState(IntPtr hwnd, int flags);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        class TaskbarList { }

        public const int Off = 0, Green = 2, Red = 4, Yellow = 8;   // TBPF_NOPROGRESS, NORMAL, ERROR, PAUSED
        static ITaskbarList3 list;
        static bool unavailable;
        static int lastState = -1, lastValue = -1;

        public static void Set(IntPtr hwnd, int state, int percent)
        {
            if (unavailable || hwnd == IntPtr.Zero) return;
            percent = Math.Max(3, Math.Min(100, percent));
            if (state == lastState && (state == Off || percent == lastValue)) return;
            try
            {
                if (list == null)
                {
                    list = (ITaskbarList3)new TaskbarList();
                    list.HrInit();
                }
            }
            catch { unavailable = true; return; }
            try
            {
                if (state != Off) list.SetProgressValue(hwnd, (ulong)percent, 100);
                list.SetProgressState(hwnd, state);
                lastState = state;
                lastValue = percent;
            }
            catch { lastState = -1; }   // кнопки ще немає — спробуємо з наступним заміром
        }

        public static void Reset() { lastState = -1; lastValue = -1; }
    }
}
