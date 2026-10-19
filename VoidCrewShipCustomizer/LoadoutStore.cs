using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
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
            /// <summary>Ship 2's own CUSTOM loadouts (Void Crew Fleet), by ship. Files from 0.3.0 don't have it.</summary>
            public Dictionary<string, HullConfig> SecondShips = new Dictionary<string, HullConfig>();
            public List<string> CustomRuns = new List<string>();
        }

        private const int MaxRememberedRuns = 50;
        private static StoreFile _file;
        /// <summary>The file is there but couldn't be read: it is never overwritten this session.</summary>
        private static bool _readOnly;

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
                string text;
                if (SafeFile.Read(FilePath, out text) == SafeFile.ReadResult.Unreadable) _readOnly = true;
                else if (text != null)
                {
                    try
                    {
                        _file = JsonConvert.DeserializeObject<StoreFile>(text);
                        Log.Info("Loaded your Custom loadouts from " + FilePath + ".");
                    }
                    catch (Exception e)
                    {
                        Log.Error("Could not understand " + FilePath + ", so the Custom loadouts start fresh. The old file is kept as " + FilePath + ".broken. Details: " + e.Message);
                        SafeFile.KeepBroken(FilePath);
                    }
                }
                if (_file == null) _file = new StoreFile();
                if (_file.Ships == null) _file.Ships = new Dictionary<string, HullConfig>();
                if (_file.SecondShips == null) _file.SecondShips = new Dictionary<string, HullConfig>();
                if (_file.CustomRuns == null) _file.CustomRuns = new List<string>();
                return _file;
            }
        }

        /// <summary>
        /// The player's CUSTOM loadout for a ship: ship 1's (a copy of the ship's first loadout until they change it), or
        /// ship 2's own (Void Crew Fleet), which is ship 1's until it is changed under SHIP 2.
        /// </summary>
        public static HullConfig Get(HullInfo hull, int ship = 1)
        {
            HullConfig config;
            if (ship == 2) return File.SecondShips.TryGetValue(hull.ShipGuid.AsHex(), out config) && config != null ? Normalize(config) : Get(hull);
            if (File.Ships.TryGetValue(hull.ShipGuid.AsHex(), out config) && config != null) return Normalize(config);
            config = FromGameLoadout(hull, hull.BasePreset);
            File.Ships[hull.ShipGuid.AsHex()] = config;
            return config;
        }

        /// <summary>The loadout to change: ship 2 gets its own copy of ship 1's the first time it is changed.</summary>
        public static HullConfig GetForEdit(HullInfo hull, int ship)
        {
            HullConfig own;
            if (ship != 2) return Get(hull);
            return File.SecondShips.TryGetValue(hull.ShipGuid.AsHex(), out own) && own != null ? Normalize(own) : Get(hull).Clone();
        }

        public static void Set(HullInfo hull, HullConfig config, int ship = 1)
        {
            (ship == 2 ? File.SecondShips : File.Ships)[hull.ShipGuid.AsHex()] = config;
            Save();
        }

        /// <summary>Ship 2 follows ship 1's loadout again.</summary>
        public static void ForgetShip2(HullInfo hull)
        {
            if (File.SecondShips.Remove(hull.ShipGuid.AsHex())) Save();
        }

        /// <summary>Whether ship 2 has its own loadout for this ship (rather than following ship 1's).</summary>
        public static bool HasOwnShip2(HullInfo hull)
        {
            HullConfig own;
            return File.SecondShips.TryGetValue(hull.ShipGuid.AsHex(), out own) && own != null;
        }

        private static HullConfig Normalize(HullConfig config)
        {
            return config.Normalized();
        }

        public static void Save()
        {
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
            Save();
        }

        public static bool WasCustomRun(string gameSessionId)
        {
            return !string.IsNullOrEmpty(gameSessionId) && File.CustomRuns.Contains(gameSessionId);
        }
    }
}
