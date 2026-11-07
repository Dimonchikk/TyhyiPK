// Тихий ПК — номер версії (одне місце на всю програму).
// Правило: кожне оновлення додає одиницю до другої цифри (3.0 → 3.1 → … → 3.10), після 3.10 — 4.0.
using System.Reflection;

[assembly: AssemblyTitle("Тихий ПК")]
[assembly: AssemblyProduct("Тихий ПК")]
[assembly: AssemblyDescription("Тихий ПК — тихі режими процесора й відеокарти, датчики, енергія, захист і сервіс")]
[assembly: AssemblyCompany("Дмитро Бричнік")]
[assembly: AssemblyCopyright("© 2026 Дмитро Бричнік")]
[assembly: AssemblyVersion(TyhyiPK.AppVersion.Full)]
[assembly: AssemblyFileVersion(TyhyiPK.AppVersion.Full)]
[assembly: AssemblyInformationalVersion(TyhyiPK.AppVersion.Text)]

namespace TyhyiPK
{
    static class AppVersion
    {
        public const string Text = "3.3";        // показується в програмі
        public const string Full = "3.3.0.0";   // для «Властивості → Подробиці»
    }
}
