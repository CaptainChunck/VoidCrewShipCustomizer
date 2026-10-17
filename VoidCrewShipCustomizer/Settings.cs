using BepInEx.Configuration;

namespace VoidCrewShipCustomizer
{
    /// <summary>The plugin's settings file (BepInEx\config\com.captainchunck.voidcrew.shipcustomizer.cfg).</summary>
    internal static class Settings
    {
        public static ConfigEntry<bool> Enabled;
        public static ConfigEntry<bool> AllowAnySize;
        public static ConfigEntry<bool> CopyPresetModuleContents;
        public static ConfigEntry<bool> ShowQuestItems;
        public static ConfigEntry<bool> WriteGameDataReport;
        public static ConfigEntry<bool> VerboseLogging;

        public static void Bind(ConfigFile config)
        {
            Enabled = config.Bind("General", "Enabled", true,
                "Turn the Ship Customizer on or off. When off, the CUSTOM loadout is not offered and the game is untouched.");

            AllowAnySize = config.Bind("Rules", "AllowAnySize", false,
                "Let any module or weapon go in any slot, whatever its size. Off by default: a 2x1 module in a 1x1 slot can stick through walls or block a corridor.");
            CopyPresetModuleContents = config.Bind("Rules", "CopyPresetModuleContents", true,
                "When you fit a module or weapon that one of the game's own loadouts also uses, it starts with the same contents that loadout gives it (for example a loaded ammo crate or power cell).");
            ShowQuestItems = config.Bind("Rules", "ShowQuestItems", false,
                "List quest items in the storage-rack picker. Starting a mission with them can confuse its objectives.");

            WriteGameDataReport = config.Bind("Diagnostics", "WriteGameDataReport", true,
                "The first time you open the ship loadout terminal, write every ship, slot, module, weapon and item the customizer found to " +
                "'ShipCustomizer\\game-data.txt' in the game's save folder (%USERPROFILE%\\AppData\\LocalLow\\Hutlihut Games ApS\\Void Crew).");
            VerboseLogging = config.Bind("Diagnostics", "VerboseLogging", false,
                "Write extra detail to the BepInEx log.");
        }
    }
}
