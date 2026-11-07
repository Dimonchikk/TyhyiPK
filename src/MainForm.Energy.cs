// Тихий ПК — вкладка «Енергія»: скільки споживає ПК і скільки це коштує.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace TyhyiPK
{
    partial class MainForm
    {
        Page pageEnergy;
        StatTile eNow, eToday, eMonth, eForecast;
        BarChart bars;
        NumBox nbTariff, nbRest;
        Segmented segEff;
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

            p.SetCaption("set", Ui.R(40, 262, 520, 18), "Налаштування");
            p.Cards.Add(Ui.R(24, 282, 552, 132));
            EnergyLabel("Тариф, ₴ за кВт·год", 282);
            nbTariff = new NumBox { Min = 0.01, Max = 100, Step = 0.01, Format = "0.00" };
            nbTariff.Bounds = Ui.R(466, 289, 94, 30);
            nbTariff.Value = Energy.Tariff;
            p.Controls.Add(nbTariff);
            p.Lines.Add(Ui.R(40, 326, 536, 0));
            EnergyLabel("Решта ПК, Вт (плата, пам'ять, диски, монітор)", 326);
            nbRest = new NumBox { Min = 0, Max = 1000, Step = 5, Format = "0" };
            nbRest.Bounds = Ui.R(466, 333, 94, 30);
            nbRest.Value = Energy.RestW;
            p.Controls.Add(nbRest);
            p.Lines.Add(Ui.R(40, 370, 536, 0));
            EnergyLabel(hasBattery ? "ККД зарядного пристрою" : "ККД блока живлення", 370);
            segEff = new Segmented();
            segEff.Items = new[] { "80%", "85%", "90%" };
            segEff.Bounds = Ui.R(360, 376, 200, 32);
            segEff.Selected = Math.Max(0, Array.IndexOf(EffValues, Energy.Efficiency));
            p.Controls.Add(segEff);

            nbTariff.Changed += (s, e) => { Energy.Tariff = nbTariff.Value; Energy.SaveSettings(); RefreshEnergy(); };
            nbRest.Changed += (s, e) => { Energy.RestW = nbRest.Value; Energy.SaveSettings(); RefreshEnergy(); };
            segEff.ItemClicked += i => { segEff.Selected = i; Energy.Efficiency = EffValues[i]; Energy.SaveSettings(); RefreshEnergy(); };
            RefreshEnergy();
        }

        void EnergyLabel(string text, int y)
        {
            Ui.MakeLabel(pageEnergy, text, Ui.R(40, y + 1, 310, 42), Ui.Body, Ui.CInk, Ui.CCard).TextAlign = ContentAlignment.MiddleLeft;
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
                      " + решта " + Watts(Energy.RestW) + ", ККД " + Energy.Efficiency + "%";
            pageEnergy.SetCaption("now", pageEnergy.Captions.ContainsKey("now") ? pageEnergy.Captions["now"].Key : Ui.R(40, 6, 520, 18), cap);
            if (heroEnergy != null)
            {
                string sub = (double.IsNaN(Energy.WallW) ? "" : "Зараз ≈ " + Watts(Energy.WallW) + " · ") + Energy.Kwh(today) +
                    (f > 0 ? " · прогноз на місяць ≈ " + Energy.Money(f * t, false) : "");
                heroEnergy.Set("Сьогодні — " + Energy.Money(today * t, true) + ".", sub, "energy", Ui.Blue);
            }
            bars.Days = Energy.LastDays(14);
            bars.Price = t;
            bars.Invalidate();
        }
    }
}
