// Тихий ПК — перевірка оновлень: сама питає GitHub (кожні ~6 годин), який номер останнього релізу.
// Програма нічого сама не завантажує й не запускає: кнопка «Оновити» відкриває завантаження інсталятора в браузері,
// а інсталятор сам закриває стару версію й ставить нову поверх (налаштування зберігаються).
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text.RegularExpressions;

namespace TyhyiPK
{
    static class Updates
    {
        public const string Repo = "Dimonchikk/TyhyiPK";
        public const string Download = "https://github.com/" + Repo + "/releases/latest/download/Tykhyi-PK-setup.exe";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        static readonly TimeSpan Every = TimeSpan.FromHours(6);   // сама перевіряє кожні ~6 годин (і за хвилину після запуску)

        public static bool Enabled
        {
            get { return Settings.GetBool("UpdateCheck", true); }
            set { Settings.SetBool("UpdateCheck", value); }
        }

        // Новіша версія, якщо вона є (запам'ятовується між запусками), інакше null.
        public static string Latest
        {
            get
            {
                string v = Settings.Get("UpdateLatest", "");
                return v != "" && Newer(v, AppVersion.Text) ? v : null;
            }
        }

        // a новіша за b? Порівнюються числа: 3.10 новіша за 3.9.
        public static bool Newer(string a, string b)
        {
            int[] x = Parts(a), y = Parts(b);
            for (int i = 0; i < Math.Max(x.Length, y.Length); i++)
            {
                int p = i < x.Length ? x[i] : 0, q = i < y.Length ? y[i] : 0;
                if (p != q) return p > q;
            }
            return false;
        }

        static int[] Parts(string v)
        {
            var list = new System.Collections.Generic.List<int>();
            foreach (string s in (v ?? "").TrimStart('v', 'V').Split('.'))
            {
                int n;
                list.Add(int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out n) ? n : 0);
            }
            return list.ToArray();
        }

        // Номер останнього опублікованого релізу або null (немає інтернету тощо).
        public static string Fetch()
        {
            try
            {
                try { ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; } catch { }   // TLS 1.2
                var req = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/" + Repo + "/releases/latest");
                req.UserAgent = "TyhyiPK/" + AppVersion.Text;
                req.Accept = "application/vnd.github+json";
                req.Timeout = 15000;
                req.ReadWriteTimeout = 15000;
                using (var resp = req.GetResponse())
                using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    var m = Regex.Match(sr.ReadToEnd(), "\"tag_name\"\\s*:\\s*\"[vV]?([0-9]+(?:\\.[0-9]+)*)\"");
                    return m.Success ? m.Groups[1].Value : null;
                }
            }
            catch { return null; }
        }

        // Чи пора перевіряти (раз на ~добу).
        public static bool Due
        {
            get
            {
                long ticks;
                if (!long.TryParse(Settings.Get("UpdateLast", "0"), out ticks)) return true;
                return DateTime.UtcNow - new DateTime(Math.Max(0, Math.Min(ticks, DateTime.MaxValue.Ticks)), DateTimeKind.Utc) >= Every;
            }
        }

        // Перевірити зараз. Повертає номер останнього релізу (або null, якщо не вдалося).
        public static string CheckNow()
        {
            string v = Fetch();
            if (v == null) return null;
            Settings.Set("UpdateLast", DateTime.UtcNow.Ticks.ToString(CultureInfo.InvariantCulture));
            Settings.Set("UpdateLatest", v);
            return v;
        }
    }
}
