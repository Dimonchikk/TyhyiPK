// Тихий ПК — розпізнавання запущених ігор.
using System;
using System.Collections.Generic;
using System.Linq;

namespace TyhyiPK
{
    static class Games
    {
        static readonly HashSet<string> BuiltIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cs2", "csgo", "dota2", "VALORANT-Win64-Shipping", "FortniteClient-Win64-Shipping", "GTA5", "GTA5_Enhanced", "RDR2",
            "r5apex", "r5apex_dx12", "eldenring", "Cyberpunk2077", "witcher3", "RocketLeague", "TslGame", "Overwatch",
            "RobloxPlayerBeta", "Minecraft.Windows", "League of Legends", "RainbowSix", "eurotrucks2", "amtrucks", "Terraria",
            "RustClient", "bg3", "bg3_dx11", "WorldOfTanks", "Warframe.x64", "destiny2", "helldivers2", "Palworld-Win64-Shipping",
            "Stalker2-Win64-Shipping", "cod", "bf2042", "FC25", "FC24",
        };
        static readonly string[] GameFolders =
        {
            "\\steamapps\\common\\", "\\epic games\\", "\\gog galaxy\\games\\", "\\gog games\\", "\\ubisoft game launcher\\games\\",
            "\\ea games\\", "\\origin games\\", "\\xboxgames\\", "\\riot games\\",
        };
        static readonly string[] NotGameFolders =
        {
            "\\epic games\\launcher\\", "\\riot games\\riot client\\", "\\steamapps\\common\\steamworks shared\\",
            "\\steamapps\\common\\steamvr\\", "\\steamapps\\common\\wallpaper_engine\\",
        };
        static readonly string[] NotGameWords =
        {
            "crash", "setup", "install", "redist", "launcher", "updater", "helper", "service", "report", "webview", "anticheat", "wallpaper", "ui32",
        };
        public static List<string> User = Settings.LoadList("games.txt");
        public static List<string> Ignore = Settings.LoadList("ignore.txt");

        public static void Save()
        {
            Settings.SaveList("games.txt", User);
            Settings.SaveList("ignore.txt", Ignore);
        }

        static bool InList(List<string> list, string name)
        {
            foreach (string s in list) if (string.Equals(s, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool IsIgnored(string name) { return InList(Ignore, name); }
        public static bool IsKnown(string name) { return BuiltIn.Contains(name) || InList(User, name); }

        public static bool IsGamePath(string name, string path)
        {
            if (path == null) return false;
            string lp = path.ToLowerInvariant(), ln = name.ToLowerInvariant();
            if (!GameFolders.Any(f => lp.Contains(f))) return false;
            if (NotGameFolders.Any(f => lp.Contains(f))) return false;
            if (NotGameWords.Any(w => ln.Contains(w))) return false;
            return true;
        }
    }
}
