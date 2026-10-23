using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResourceAssets;
using UnityEngine;

namespace VoidCrewShipCustomizer
{
    /// <summary>A Custom loadout for one ship, as the player set it up.</summary>
    internal sealed class HullConfig
    {
        public const int MaxAlloys = 100000;
        /// <summary>The fabricator's levels are MK I to MK III (0 to 2); the game's terminal shows no more.</summary>
        public const int MaxFabricatorLevel = 2;

        /// <summary>Hardpoint position (the ship's build socket index) to an "m:" module or "w:" weapon code.</summary>
        public SortedDictionary<int, string> Slots = new SortedDictionary<int, string>();

        /// <summary>Storage rack (the ship's core system index) to one code per rack slot: "i:" item, "c:" weapon crate, "" empty.</summary>
        public SortedDictionary<int, List<string>> Storage = new SortedDictionary<int, List<string>>();

        /// <summary>
        /// What modules and weapons start loaded with (ammo, batteries, power cells, weapon mods), by hardpoint position:
        /// one "i:" code per item slot of the module, "" for an empty slot. A position without an entry starts with the
        /// game's usual contents for that module.
        /// </summary>
        public SortedDictionary<int, List<string>> Contents = new SortedDictionary<int, List<string>>();

        /// <summary>"i:" code of the homunculus the ship starts with; "" for the standard one.</summary>
        public string Homunculus = "";

        /// <summary>Name of the game loadout this one was last copied from; shown in the description only.</summary>
        public string CopiedFrom = "";

        /// <summary>Alloys the run starts with; -1 for the game's own amount (a bonus from the players' ranks).</summary>
        public int Alloys = -1;

        /// <summary>The fabricator's starting level: 0 is MK I (how every run starts), 1 MK II, 2 MK III.</summary>
        public int FabricatorLevel;

        /// <summary>What a newer version saved that this one doesn't know, kept so saving here doesn't drop it.</summary>
        #pragma warning disable 0649 // filled in by Newtonsoft
        [JsonExtensionData]
        public IDictionary<string, JToken> Newer;
        #pragma warning restore 0649

        public HullConfig Clone()
        {
            return JsonConvert.DeserializeObject<HullConfig>(JsonConvert.SerializeObject(this));
        }

        /// <summary>
        /// Fills in what older files (or older versions on other PCs) leave out, drops empty entries a damaged file can have
        /// (a missing rack, a missing code), and keeps the numbers in range.
        /// </summary>
        public HullConfig Normalized()
        {
            if (Slots == null) Slots = new SortedDictionary<int, string>();
            if (Storage == null) Storage = new SortedDictionary<int, List<string>>();
            foreach (int key in Slots.Keys.ToList())
            {
                if (string.IsNullOrEmpty(Slots[key])) Slots.Remove(key);
            }
            foreach (int key in Storage.Keys.ToList())
            {
                List<string> rack = Storage[key];
                if (rack == null) Storage[key] = new List<string>();
                else for (int i = 0; i < rack.Count; i++) rack[i] = rack[i] ?? "";
            }
            if (Contents == null) Contents = new SortedDictionary<int, List<string>>();
            foreach (int key in Contents.Keys.ToList())
            {
                // Contents belong to the module at that position: none there, none kept.
                List<string> inside = Contents[key];
                if (inside == null || !Slots.ContainsKey(key)) Contents.Remove(key);
                else for (int i = 0; i < inside.Count; i++) inside[i] = inside[i] ?? "";
            }
            if (Homunculus == null) Homunculus = "";
            if (CopiedFrom == null) CopiedFrom = "";
            Alloys = Math.Max(-1, Math.Min(Alloys, MaxAlloys));
            FabricatorLevel = Math.Max(0, Math.Min(FabricatorLevel, MaxFabricatorLevel));
            return this;
        }

        [JsonIgnore]
        public int FittedCount
        {
            get { return Slots == null ? 0 : Slots.Count; }
        }

        [JsonIgnore]
        public int StoredCount
        {
            get { return Storage == null ? 0 : Storage.Values.Where(rack => rack != null).Sum(rack => rack.Count(code => !string.IsNullOrEmpty(code))); }
        }
    }

    /// <summary>
    /// Saves each ship's Custom loadout in the game's save folder (ShipCustomizer\custom-loadouts.json), so it survives
    /// restarts and BepInEx reinstalls. Also remembers which saved runs were started with a Custom loadout.
    /// </summary>
    internal static class LoadoutStore
    {
        private sealed class StoreFile
        {
            public int Version = 1;
            public Dictionary<string, HullConfig> Ships = new Dictionary<string, HullConfig>();
            /// <summary>Ship 2's own CUSTOM loadouts (Void Crew Fleet), by ship type. Files from 0.3.0 don't have it.</summary>
            public Dictionary<string, HullConfig> SecondShips = new Dictionary<string, HullConfig>();
            /// <summary>
            /// Ships 3 to 6's own CUSTOM loadouts (Void Crew Fleet 0.4.0), by player ship ("3" to "6"), then ship type.
            /// Files from 0.8.0 and older don't have it.
            /// </summary>
            public Dictionary<string, Dictionary<string, HullConfig>> MoreShips = new Dictionary<string, Dictionary<string, HullConfig>>();
            public List<string> CustomRuns = new List<string>();
            /// <summary>What a newer version saved that this one doesn't know, kept so saving here doesn't drop it.</summary>
            #pragma warning disable 0649 // filled in by Newtonsoft
            [JsonExtensionData]
            public IDictionary<string, JToken> Newer;
            #pragma warning restore 0649
        }

        private const int MaxRememberedRuns = 50;
        private static StoreFile _file;
        /// <summary>The file is there but couldn't be read: it is never overwritten this session.</summary>
        private static bool _readOnly;
        /// <summary>Each ship type's stand-in game loadout as a CUSTOM loadout, read once.</summary>
        private static readonly Dictionary<GUIDUnion, HullConfig> Defaults = new Dictionary<GUIDUnion, HullConfig>();

        public static string Folder
        {
            get { return Path.Combine(Application.persistentDataPath, "ShipCustomizer"); }
        }

        private static string FilePath
        {
            get { return Path.Combine(Folder, "custom-loadouts.json"); }
        }

        private static StoreFile File
        {
            get
            {
                if (_file != null) return _file;
                if (SafeFile.Load(FilePath, "your Custom loadouts", out _file) == SafeFile.ReadResult.Unreadable) _readOnly = true;
                else if (_file != null) Log.Info("Loaded your Custom loadouts from " + FilePath + ".");
                if (_file == null) _file = new StoreFile();
                if (_file.Ships == null) _file.Ships = new Dictionary<string, HullConfig>();
                if (_file.SecondShips == null) _file.SecondShips = new Dictionary<string, HullConfig>();
                if (_file.MoreShips == null) _file.MoreShips = new Dictionary<string, Dictionary<string, HullConfig>>();
                if (_file.CustomRuns == null) _file.CustomRuns = new List<string>();
                return _file;
            }
        }

        /// <summary>
        /// The player's CUSTOM loadout of a ship type for a player ship. Ship 1's is a copy of the type's stand-in game
        /// loadout until it is changed. Ships 2 to 6 (Void Crew Fleet) have their own once changed under that ship; until
        /// then they follow ship 1's, except in a Fleet Battle, where they start from the stand-in game loadout, so no team
        /// sees or follows another team's choices.
        /// </summary>
        public static HullConfig Get(HullInfo hull, int ship = 1)
        {
            HullConfig config;
            if (ship >= 2)
            {
                if (TryGetOwn(hull, ship, out config)) return config;
                return FleetLink.IsTeamMatch ? Default(hull) : Get(hull);
            }
            if (File.Ships.TryGetValue(hull.ShipGuid.AsHex(), out config) && config != null) return Normalize(config);
            config = FromGameLoadout(hull, hull.BasePreset);
            File.Ships[hull.ShipGuid.AsHex()] = config;
            return config;
        }

        /// <summary>The loadout to change: ships 2 to 6 get their own copy of what they show the first time they are changed.</summary>
        public static HullConfig GetForEdit(HullInfo hull, int ship)
        {
            HullConfig own;
            if (ship < 2) return Get(hull);
            return TryGetOwn(hull, ship, out own) ? own : Get(hull, ship).Clone();
        }

        public static void Set(HullInfo hull, HullConfig config, int ship = 1)
        {
            OwnLayouts(ship, true)[hull.ShipGuid.AsHex()] = config;
            Save();
        }

        /// <summary>Ship 2 to 6 follows ship 1's loadout again (or, in a Fleet Battle, starts again from the stand-in's).</summary>
        public static void Forget(HullInfo hull, int ship)
        {
            Dictionary<string, HullConfig> own = ship >= 2 ? OwnLayouts(ship, false) : null;
            if (own != null && own.Remove(hull.ShipGuid.AsHex())) Save();
        }

        /// <summary>Whether ship 2 to 6 has its own loadout for this ship type (rather than following ship 1's).</summary>
        public static bool HasOwn(HullInfo hull, int ship)
        {
            HullConfig own;
            return ship >= 2 && TryGetOwn(hull, ship, out own);
        }

        private static bool TryGetOwn(HullInfo hull, int ship, out HullConfig config)
        {
            config = null;
            Dictionary<string, HullConfig> own = OwnLayouts(ship, false);
            if (own == null || !own.TryGetValue(hull.ShipGuid.AsHex(), out config) || config == null) return false;
            Normalize(config);
            return true;
        }

        /// <summary>A player ship's loadouts by ship type: ship 1's, ship 2's own, or ship 3 to 6's own (made when asked to).</summary>
        private static Dictionary<string, HullConfig> OwnLayouts(int ship, bool create)
        {
            if (ship <= 1) return File.Ships;
            if (ship == 2) return File.SecondShips;
            Dictionary<string, HullConfig> layouts;
            string key = ship.ToString();
            if (!File.MoreShips.TryGetValue(key, out layouts) || layouts == null)
            {
                if (!create) return null;
                layouts = new Dictionary<string, HullConfig>();
                File.MoreShips[key] = layouts;
            }
            return layouts;
        }

        /// <summary>The ship type's stand-in game loadout as a CUSTOM loadout (a fresh copy each time).</summary>
        public static HullConfig Default(HullInfo hull)
        {
            HullConfig config;
            if (!Defaults.TryGetValue(hull.ShipGuid, out config))
            {
                config = FromGameLoadout(hull, hull.BasePreset).Normalized();
                Defaults[hull.ShipGuid] = config;
            }
            return config.Clone();
        }

        private static HullConfig Normalize(HullConfig config)
        {
            return config.Normalized();
        }

        /// <summary>How long changes wait before they are written, so a burst of picks makes one save instead of many.</summary>
        private const float SaveDelaySeconds = 1f;
        private static bool _unsaved;
        private static float _unsavedSince;

        /// <summary>Saves the file shortly (see <see cref="Flush"/>): picks in quick succession are written together.</summary>
        public static void Save()
        {
            if (_unsaved) return;
            _unsaved = true;
            _unsavedSince = Time.unscaledTime;
        }

        /// <summary>
        /// Writes what is waiting: once it has waited <see cref="SaveDelaySeconds"/> (called a few times a second), or straight
        /// away with <paramref name="now"/> (a CUSTOM run to remember, a mission starting, the game quitting).
        /// </summary>
        public static void Flush(bool now)
        {
            if (!_unsaved || (!now && Time.unscaledTime - _unsavedSince < SaveDelaySeconds)) return;
            _unsaved = false;
            StoreFile file = File;
            if (_readOnly)
            {
                Log.WarnOnce("store read only", "Your Custom loadouts are not saved this session: " + FilePath + " could not be read when the game started.");
                return;
            }
            try
            {
                SafeFile.Write(FilePath, JsonConvert.SerializeObject(file, Formatting.Indented));
            }
            catch (Exception e)
            {
                Log.Failure("Saving the Custom loadouts to " + FilePath, e);
            }
        }

        /// <summary>A Custom loadout that matches one of the game's own loadouts for this ship.</summary>
        public static HullConfig FromGameLoadout(HullInfo hull, ShipLoadoutDataDef preset)
        {
            var config = new HullConfig { CopiedFrom = PresetName(preset) };
            ShipLoadout loadout = preset.Asset.ShipLoadout;

            foreach (ShipSocketEntry socket in loadout.Sockets)
            {
                // Game loadouts list every hardpoint; the empty ones have no module.
                if (socket == null || socket.Module == null || socket.Module.IsNull) continue;
                if (socket.SocketId < 0 || socket.SocketId >= hull.Sockets.Count) continue;
                Option option = Catalog.OptionFor(socket);
                if (option == null)
                {
                    Log.Warn("The " + config.CopiedFrom + " loadout has a module the customizer doesn't know (" + socket + "); it is left out of the copy.");
                    continue;
                }
                config.Slots[socket.SocketId] = option.Code;
                // What the loadout loads into it (ammo, power cells, mods), exactly.
                if (socket.Upgrades != null && socket.Upgrades.Any(u => u != null && !u.IsNull)) config.Contents[socket.SocketId] = socket.Upgrades.Select(Catalog.CodeFor).ToList();
            }

            foreach (ShelfInfo shelf in hull.Shelves)
            {
                ShipSocketEntry rack = loadout.CoreSystemSockets.FirstOrDefault(c => c != null && c.SocketId == shelf.CoreIndex);
                var codes = new List<string>();
                for (int i = 0; i < shelf.Slots.Count; i++)
                {
                    codes.Add(rack != null && rack.Upgrades != null && i < rack.Upgrades.Length ? Catalog.CodeFor(rack.Upgrades[i]) : "");
                }
                config.Storage[shelf.CoreIndex] = codes;
            }

            CarryableRef homunculus = loadout.HomunculusOverride;
            config.Homunculus = homunculus != null && !homunculus.IsNull ? "i:" + homunculus.AssetGuid.AsHex() : "";
            return config;
        }

        public static string PresetName(ShipLoadoutDataDef preset)
        {
            try
            {
                string header = preset.LoadoutContextInfo != null ? preset.LoadoutContextInfo.HeaderText : null;
                if (!string.IsNullOrEmpty(header)) return header;
            }
            catch
            {
                // fall back to the asset name
            }
            try
            {
                return preset.Asset != null ? preset.Asset.name : "loadout";
            }
            catch
            {
                return "loadout";
            }
        }

        // ---------------------------------------------------------------- runs started with a Custom loadout

        /// <summary>Remembers a run as a CUSTOM run, moving it to the newest end of the list if it is there already.</summary>
        public static void RememberCustomRun(string gameSessionId)
        {
            if (string.IsNullOrEmpty(gameSessionId)) return;
            int at = File.CustomRuns.IndexOf(gameSessionId);
            if (at >= 0 && at == File.CustomRuns.Count - 1) return;
            if (at >= 0) File.CustomRuns.RemoveAt(at);
            File.CustomRuns.Add(gameSessionId);
            while (File.CustomRuns.Count > MaxRememberedRuns) File.CustomRuns.RemoveAt(0);
            // Straight away: continuing this run from a save must keep progression paused, even after a crash.
            Save();
            Flush(true);
        }

        public static bool WasCustomRun(string gameSessionId)
        {
            return !string.IsNullOrEmpty(gameSessionId) && File.CustomRuns.Contains(gameSessionId);
        }
    }
}
