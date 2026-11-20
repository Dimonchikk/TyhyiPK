// Тихий ПК — режим «ШІ»: для локальних моделей (Ollama, LM Studio, llama.cpp) відеокарта трохи обмежена,
// а її вентилятори крутяться примусово за власною кривою, щоб карта не нагрівалась під довгим навантаженням.
using System;
using System.Collections.Generic;

namespace TyhyiPK
{
    partial class MainForm
    {
        // Ім'я процесу → назва програми. Ollama визначається окремо (див. DetectGame).
        static readonly Dictionary<string, string> AiApps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "ollama_llama_server", "Ollama" },
            { "llama-server", "llama.cpp" },
            { "llama-cli", "llama.cpp" },
            { "LM Studio", "LM Studio" },
            { "koboldcpp", "KoboldCpp" },
        };

        string runningAi;                 // назва ШІ-програми, яка зараз працює з моделлю, або null
        volatile bool gpuFansWant;        // чи має фоновий потік примусово крутити вентилятори карти

        // Крива: 50% до 45°, далі рівно вгору до 100% на 80°. Без температури — 70%.
        static double AiFanCurve(double t)
        {
            if (double.IsNaN(t)) return 70;
            return Math.Max(50, Math.Min(100, 50 + (t - 45) * 50.0 / 35.0));
        }

        // Викликається з потоку датчиків після кожного зчитування.
        void DriveGpuFans(Snapshot s, int tick)
        {
            if (gpuFansWant && Lhm.GpuFansControllable == true)
            {
                double pct = Math.Round(AiFanCurve(s.GpuTemp));
                // Раз на хвилину ставимо значення ще раз — на випадок, якщо драйвер повернув своє.
                Lhm.SetGpuFans(pct, tick % 30 == 0);
            }
            else if (Lhm.GpuFansForced) Lhm.ResetGpuFans();
            s.GpuFansForced = Lhm.GpuFansForced;
        }

        // Після зміни режиму чи даних датчиків: що робити з вентиляторами і що показати під режимами карти.
        void UpdateGpuFans(Snapshot s)
        {
            gpuFansWant = gpu.Available && (gpu.Current == "ai" || gpu.WantRaw == "ai");
            if (!gpu.Available || gpu.DescLabel == null) return;
            string text = gpu.Desc(gpu.Current);
            if (gpu.Current == "ai" && s != null && s.GpuFansForced)
            {
                text = "Для локальних ШІ-моделей: ліміт 90% (" + Gpu.Target("ai").ToString("0") + " Вт). Вентилятори примусово: "
                    + (double.IsNaN(s.GpuFanPct) ? Math.Round(AiFanCurve(s.GpuTemp)) : Math.Round(s.GpuFanPct)) + "%"
                    + (double.IsNaN(s.GpuFanRpm) ? "" : " · " + Math.Round(s.GpuFanRpm) + " об/хв")
                    + (double.IsNaN(s.GpuTemp) ? "" : " при " + Math.Round(s.GpuTemp) + "°") + ".";
            }
            if (gpu.DescLabel.Text != text) gpu.DescLabel.Text = text;
        }
    }
}
