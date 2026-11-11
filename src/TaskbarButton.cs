// Тихий ПК — кнопка програми на панелі завдань (стандартні можливості Windows 7+, ITaskbarList3):
// • смужка навантаження: зелена — спокійно, жовта — помітне навантаження, червона — на межі;
// • маленький значок у кутку кнопки (як лічильник повідомлень у месенджерах) — крутний вентилятор.
//   Сам значок кнопки міняти не вийде: програма працює з правами адміністратора, і панель завдань його не перечитує.
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
            void RegisterTab(IntPtr hwndTab, IntPtr hwndMdi);
            void UnregisterTab(IntPtr hwndTab);
            void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
            void SetTabActive(IntPtr hwndTab, IntPtr hwndMdi, uint reserved);
            void ThumbBarAddButtons(IntPtr hwnd, uint count, IntPtr buttons);
            void ThumbBarUpdateButtons(IntPtr hwnd, uint count, IntPtr buttons);
            void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
            void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string description);
        }

        [ComImport, Guid("56FDF344-FD6D-11d0-958A-006097C9A090"), ClassInterface(ClassInterfaceType.None)]
        class TaskbarList { }

        public const int Off = 0, Green = 2, Red = 4, Yellow = 8;   // TBPF_NOPROGRESS, NORMAL, ERROR, PAUSED
        static ITaskbarList3 list;
        static bool unavailable;

        static bool Ready()
        {
            if (unavailable) return false;
            if (list != null) return true;
            try
            {
                list = (ITaskbarList3)new TaskbarList();
                list.HrInit();
                return true;
            }
            catch { unavailable = true; return false; }
        }

        static IntPtr lastOverlay = (IntPtr)(-1);

        // Значок у кутку кнопки; IntPtr.Zero — прибрати.
        public static void Overlay(IntPtr hwnd, IntPtr icon)
        {
            if (hwnd == IntPtr.Zero || icon == lastOverlay || !Ready()) return;
            try { list.SetOverlayIcon(hwnd, icon, icon == IntPtr.Zero ? null : "Тихий ПК"); lastOverlay = icon; }
            catch { lastOverlay = (IntPtr)(-1); }
        }
        static int lastState = -1, lastValue = -1;

        public static void Set(IntPtr hwnd, int state, int percent)
        {
            if (unavailable || hwnd == IntPtr.Zero) return;
            percent = Math.Max(3, Math.Min(100, percent));
            if (state == lastState && (state == Off || percent == lastValue)) return;
            if (!Ready()) return;
            try
            {
                if (state != Off) list.SetProgressValue(hwnd, (ulong)percent, 100);
                list.SetProgressState(hwnd, state);
                lastState = state;
                lastValue = percent;
            }
            catch { lastState = -1; }   // кнопки ще немає — спробуємо з наступним заміром
        }

        public static void Reset() { lastState = -1; lastValue = -1; lastOverlay = (IntPtr)(-1); }
    }
}

