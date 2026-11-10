// Тихий ПК — вкладка «Енергія»: скільки споживає ПК і скільки це коштує.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        Page pageEnergy;
        StatTile eNow, eToday, eMonth, eForecast, wHours, wCpu, wGpu, wCost;
        bool energyWide;
        BarChart bars;
        NumBox nbTariff, nbRest;
        Segmented segEff, segRest;
        static readonly int[] EffValues = { 80, 85, 90 };

        void BuildEnergy()
        {
            Energy.Load(hasBattery);
            var p = pageEnergy;
            p.SetCaption("now", Ui.R(40, 6, 520, 18), "Зараз");
            p.Cards.Add(Ui.R(24, 26, 552, 88));
            eNow = Tile(p, 0, 30, 80, "Зараз");
            eToday = Tile(p, 1, 30, 80, "Сьогодні");
            eMonth = Tile(p, 2, 30, 80, "Цей місяць");
            eForecast = Tile(p, 3, 30, 80, "Прогноз на місяць");

            p.SetCaption("days", Ui.R(40, 126, 520, 18), "Останні 14 днів");
            p.Cards.Add(Ui.R(24, 146, 552, 104));
            bars = new BarChart();
            bars.Bounds = Ui.R(32, 150, 536, 96);
            p.Controls.Add(bars);

            p.SetCaption("week", Ui.R(40, 262, 520, 18), "Цей тиждень|з понеділка, поки працює «Тихий ПК»");
            p.Cards.Add(Ui.R(24, 282, 552, 88));
            wHours = Tile(p, 0, 286, 80, "Працював");
            wCpu = Tile(p, 1, 286, 80, "Процесор, макс.");
            wGpu = Tile(p, 2, 286, 80, "Відеокарта, макс.");
            wCost = Tile(p, 3, 286, 80, "Електрика");

            p.SetCaption("set", Ui.R(40, 382, 520, 18), "Налаштування|«Авто» рахує сам — як показала б розумна розетка");
            p.Cards.Add(Ui.R(24, 402, 552, 132));
            EnergyLabel("Тариф, ₴ за кВт·год", 402);
            nbTariff = new NumBox { Min = 0.01, Max = 100, Step = 0.01, Format = "0.00" };
            nbTariff.Bounds = Ui.R(466, 409, 94, 30);
            nbTariff.Value = Energy.Tariff;
            p.Controls.Add(nbTariff);
            p.Lines.Add(Ui.R(40, 446, 536, 0));
            EnergyLabel("Решта ПК, Вт", 446);
            segRest = new Segmented();
            segRest.Items = new[] { "Авто", "Вручну" };
            segRest.Bounds = Ui.R(290, 452, 160, 32);
            segRest.Selected = Energy.RestAuto ? 0 : 1;
            p.Controls.Add(segRest);
            nbRest = new NumBox { Min = 0, Max = 1000, Step = 5, Format = "0" };
            nbRest.Bounds = Ui.R(466, 453, 94, 30);
            nbRest.Value = Energy.RestAuto && !double.IsNaN(Energy.AutoRest) ? Math.Round(Energy.AutoRest) : Energy.RestW;
            p.Controls.Add(nbRest);
            p.Lines.Add(Ui.R(40, 490, 536, 0));
            EnergyLabel(hasBattery ? "ККД зарядного пристрою" : "ККД блока живлення", 490);
            segEff = new Segmented();
            segEff.Items = new[] { "Авто", "80%", "85%", "90%" };
            segEff.Bounds = Ui.R(290, 496, 270, 32);
            segEff.Selected = Energy.EffAuto ? 0 : 1 + Math.Max(0, Array.IndexOf(EffValues, Energy.Efficiency));
            p.Controls.Add(segEff);

            nbTariff.Changed += (s, e) => { Energy.Tariff = nbTariff.Value; Energy.SaveSettings(); RefreshEnergy(); };
            nbRest.Changed += (s, e) => { Energy.RestW = nbRest.Value; Energy.RestAuto = false; segRest.Selected = 1; Energy.SaveSettings(); RefreshEnergy(); };
            segRest.ItemClicked += i =>
            {
                segRest.Selected = i;
                Energy.RestAuto = i == 0;
                if (i == 1) Energy.RestW = nbRest.Value;   // «Вручну» починається з того, що порахувало «Авто»
                Energy.SaveSettings();
                RefreshEnergy();
            };
            segEff.ItemClicked += i =>
            {
                segEff.Selected = i;
                Energy.EffAuto = i == 0;
                if (i > 0) Energy.Efficiency = EffValues[i - 1];
                Energy.SaveSettings();
                RefreshEnergy();
            };
            RefreshEnergy();
        }

        // Картка «Цей тиждень» з порівнянням із минулим.
        void RefreshWeek(double t)
        {
            if (wHours == null) return;
            var mon = Energy.WeekStart(DateTime.Today);
            var w = Energy.Week(mon);
            var prev = Energy.Week(mon.AddDays(-7));
            if (w.Hours >= 0.25)
                wHours.Set(Energy.Hours(w.Hours), null, prev.Hours >= 0.25 ? "минулого " + Energy.Hours(prev.Hours) : "роботи", false);
            else wHours.Set("—", Ui.Ink3, "ще немає даних", false);
            if (w.MaxCpu > 0) wCpu.Set(Deg(w.MaxCpu), Ui.TempColor(w.MaxCpu), prev.MaxCpu > 0 ? "минулого " + Deg(prev.MaxCpu) : "найвища", false);
            else wCpu.Set("—", Ui.Ink3, "немає датчика", false);
            if (w.MaxGpu > 0) wGpu.Set(Deg(w.MaxGpu), Ui.TempColor(w.MaxGpu), prev.MaxGpu > 0 ? "минулого " + Deg(prev.MaxGpu) : "найвища", false);
            else wGpu.Set("—", Ui.Ink3, "немає датчика", false);
            string cmp = "";
            if (prev.Kwh > 0.01 && w.Kwh > 0)
            {
                int pct = (int)Math.Round((w.Kwh - prev.Kwh) / prev.Kwh * 100);
                cmp = pct == 0 ? "як минулого" : (pct > 0 ? "+" : "−") + Math.Abs(pct) + "% до минулого";
            }
            wCost.Set(Energy.Money(w.Kwh * t, false), null, cmp != "" ? cmp : Energy.Kwh(w.Kwh), false);
        }

        void EnergyLabel(string text, int y)
        {
            Ui.MakeLabel(pageEnergy, text, Ui.R(40, y + 1, 240, 42), Ui.Body, Ui.CInk, Ui.CCard).TextAlign = ContentAlignment.MiddleLeft;
        }

        void RefreshEnergy()
        {
            if (eNow == null) return;
            double t = Energy.Tariff;
            if (double.IsNaN(Energy.WallW)) eNow.Set("…", Ui.Ink3, "", false);
            else eNow.Set(Watts(Energy.WallW), null, Energy.Measured ? "від батареї" : "з розетки, оцінка", false);
            double today = Energy.Day(DateTime.Today);
            eToday.Set(Energy.Money(today * t, true), null, Energy.Kwh(today), false);
            double month = Energy.Month(DateTime.Today);
            eMonth.Set(Energy.Money(month * t, false), null, Energy.Kwh(month), false);
            double f = Energy.MonthForecast(DateTime.Now);
            if (f > 0) eForecast.Set("≈ " + Energy.Money(f * t, false), null, "≈ " + Energy.Kwh(f), false);
            else eForecast.Set("—", Ui.Ink3, "мало даних", false);

            string cap;
            if (double.IsNaN(Energy.WallW)) cap = "Зараз";
            else if (Energy.Measured) cap = "Зараз|від батареї, виміряно драйвером батареї";
            else
                cap = "Зараз|із розетки: ЦП " + (Energy.CpuEstimated ? "≈" : "") + Watts(Energy.CpuW) +
                      (Energy.GpuW > 0 ? " + карта " + Watts(Energy.GpuW) : "") +
                      " + решта " + (Energy.RestAuto ? "≈" + Watts(Energy.AutoRest) : Watts(Energy.RestW)) +
                      ", ККД " + (Energy.EffAuto ? "≈" : "") + Math.Round(Energy.EffNow * 100) + "%";
            if (Energy.RestAuto && !double.IsNaN(Energy.AutoRest) && !nbRest.ContainsFocus) nbRest.Value = Math.Round(Energy.AutoRest);
            pageEnergy.SetCaption("now", pageEnergy.Captions.ContainsKey("now") ? pageEnergy.Captions["now"].Key : Ui.R(40, 6, 520, 18), cap);
            if (heroEnergy != null)
            {
                string sub = (double.IsNaN(Energy.WallW) ? "" : "Зараз ≈ " + Watts(Energy.WallW) + " · ") + Energy.Kwh(today) +
                    (f > 0 ? " · прогноз на місяць ≈ " + Energy.Money(f * t, false) : "");
                heroEnergy.Set("Сьогодні — " + Energy.Money(today * t, true) + ".", sub, "energy", Ui.Blue);
            }
            RefreshWeek(t);
            // На розгорнутому вікні графік ширший — показуємо місяць.
            int nDays = energyWide ? 30 : 14;
            bars.Days = Energy.LastDays(nDays);
            pageEnergy.SetCaption("days", pageEnergy.Captions["days"].Key, "Останні " + nDays + " днів");
            bars.Price = t;
            bars.Invalidate();
        }
    }
}
