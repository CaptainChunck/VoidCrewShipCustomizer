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
        /// <summary>Hardpoint position (the ship's build socket index) to an "m:" module or "w:" weapon code.</summary>
        public SortedDictionary<int, string> Slots = new SortedDictionary<int, string>();

        /// <summary>Storage rack (the ship's core system index) to one code per rack slot: "i:" item, "c:" weapon crate, "" empty.</summary>
        public SortedDictionary<int, List<string>> Storage = new SortedDictionary<int, List<string>>();

        /// <summary>"i:" code of the homunculus the ship starts with; "" for the standard one.</summary>
        public string Homunculus = "";

        /// <summary>Name of the game loadout this one was last copied from; shown in the description only.</summary>
        public string CopiedFrom = "";

        public HullConfig Clone()
        {
            return JsonConvert.DeserializeObject<HullConfig>(JsonConvert.SerializeObject(this));
        }

        public int FittedCount
        {
            get { return Slots.Count; }
        }

        public int StoredCount
        {
            get { return Storage.Values.Sum(rack => rack.Count(code => !string.IsNullOrEmpty(code))); }
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
            public List<string> CustomRuns = new List<string>();
        }

        private const int MaxRememberedRuns = 50;
        private static StoreFile _file;

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
                try
                {
                    if (System.IO.File.Exists(FilePath))
                    {
                        _file = JsonConvert.DeserializeObject<StoreFile>(System.IO.File.ReadAllText(FilePath));
                        Log.Info("Loaded your Custom loadouts from " + FilePath + ".");
                    }
                }
                catch (Exception e)
                {
                    string broken = FilePath + ".broken";
                    Log.Error("Could not read " + FilePath + ", so the Custom loadouts start fresh. The old file is kept as " + broken + ". Details: " + e.Message);
                    try
                    {
                        System.IO.File.Copy(FilePath, broken, true);
                    }
                    catch
                    {
                        // nothing more to do
                    }
                }
                if (_file == null) _file = new StoreFile();
                if (_file.Ships == null) _file.Ships = new Dictionary<string, HullConfig>();
                if (_file.CustomRuns == null) _file.CustomRuns = new List<string>();
                return _file;
            }
        }

        /// <summary>The player's Custom loadout for a ship; a copy of the ship's first loadout until they change it.</summary>
        public static HullConfig Get(HullInfo hull)
        {
            HullConfig config;
            if (File.Ships.TryGetValue(hull.ShipGuid.AsHex(), out config) && config != null)
            {
                if (config.Slots == null) config.Slots = new SortedDictionary<int, string>();
                if (config.Storage == null) config.Storage = new SortedDictionary<int, List<string>>();
                if (config.Homunculus == null) config.Homunculus = "";
                return config;
            }
            config = FromGameLoadout(hull, hull.BasePreset);
            File.Ships[hull.ShipGuid.AsHex()] = config;
            return config;
        }

        public static void Set(HullInfo hull, HullConfig config)
        {
            File.Ships[hull.ShipGuid.AsHex()] = config;
            Save();
        }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                string temp = FilePath + ".tmp";
                System.IO.File.WriteAllText(temp, JsonConvert.SerializeObject(File, Formatting.Indented));
                if (System.IO.File.Exists(FilePath)) System.IO.File.Delete(FilePath);
                System.IO.File.Move(temp, FilePath);
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
                if (socket == null || socket.SocketId < 0 || socket.SocketId >= hull.Sockets.Count) continue;
                Option option = Catalog.OptionFor(socket);
                if (option != null) config.Slots[socket.SocketId] = option.Code;
                else Log.Warn("The " + config.CopiedFrom + " loadout has a module the customizer doesn't know (" + socket + "); it is left out of the copy.");
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
                return preset.LoadoutContextInfo.HeaderText;
            }
            catch
            {
                return "game";
            }
        }

        // ---------------------------------------------------------------- runs started with a Custom loadout

        public static void RememberCustomRun(string gameSessionId)
        {
            if (string.IsNullOrEmpty(gameSessionId) || File.CustomRuns.Contains(gameSessionId)) return;
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
