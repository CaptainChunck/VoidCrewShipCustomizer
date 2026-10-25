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

        /// <summary>
        /// Puts an item code ("" to empty it) in one slot of a storage rack with <paramref name="slotCount"/> slots. A rack
        /// the loadout has no list for yet gets one, with its other slots empty; a slot the rack doesn't have is left alone.
        /// </summary>
        public void SetRackSlot(int rack, int slotCount, int index, string code)
        {
            if (Storage == null) Storage = new SortedDictionary<int, List<string>>();
            List<string> codes;
            if (!Storage.TryGetValue(rack, out codes) || codes == null)
            {
                codes = new List<string>();
                Storage[rack] = codes;
            }
            while (codes.Count < slotCount) codes.Add("");
            if (index >= 0 && index < codes.Count) codes[index] = code ?? "";
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
        /// <summary>
        /// The file is there but couldn't be read (another program held it): nothing is written over it until it can be
        /// read, which is tried again at each save (see <see cref="ReadAgain"/>). Meanwhile the terminal shows stand-in copies.
        /// </summary>
        private static bool _readOnly;
        /// <summary>Each ship type's stand-in game loadout as a CUSTOM loadout, read once.</summary>
        private static readonly Dictionary<GUIDUnion, HullConfig> Defaults = new Dictionary<GUIDUnion, HullConfig>();

        /// <summary>
        /// While the file can't be read: each loadout changed meanwhile (player ship, ship type) with what it was before the
        /// first change (null: none of its own), to put the change on top of the file once it can be read.
        /// </summary>
        private static readonly Dictionary<KeyValuePair<int, string>, HullConfig> ChangedUnread = new Dictionary<KeyValuePair<int, string>, HullConfig>();
        /// <summary>While the file can't be read: the CUSTOM runs remembered meanwhile.</summary>
        private static readonly List<string> RunsUnread = new List<string>();
        /// <summary>The player was told that their changes can't be saved for now.</summary>
        private static bool _toldUnsaved;
        /// <summary>A message about saving for the player, shown by <see cref="CustomizerRuntime"/> (see <see cref="TakeNotice"/>).</summary>
        private static string _notice;
        /// <summary>The file was read after all: what the terminal and the room show must come from it now (see <see cref="TakeReadLate"/>).</summary>
        private static bool _readLate;
        /// <summary>A CUSTOM run asked about while the file couldn't be read: not tried again before this time.</summary>
        private static float _nextRunRead;

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
                StoreFile read;
                if (SafeFile.Load(FilePath, "your Custom loadouts", out read) == SafeFile.ReadResult.Unreadable) _readOnly = true;
                else if (read != null) Log.Info("Loaded your Custom loadouts from " + FilePath + ".");
                _file = Complete(read);
                return _file;
            }
        }

        /// <summary>
        /// A file as read, with what older files (or a damaged one) leave out filled in, and every loadout in it normalized
        /// once, so reading a loadout needn't do it each time (changes are normalized as they are made, see <see cref="Set"/>).
        /// </summary>
        private static StoreFile Complete(StoreFile file)
        {
            if (file == null) file = new StoreFile();
            if (file.Ships == null) file.Ships = new Dictionary<string, HullConfig>();
            if (file.SecondShips == null) file.SecondShips = new Dictionary<string, HullConfig>();
            if (file.MoreShips == null) file.MoreShips = new Dictionary<string, Dictionary<string, HullConfig>>();
            if (file.CustomRuns == null) file.CustomRuns = new List<string>();
            foreach (HullConfig config in file.Ships.Values.Concat(file.SecondShips.Values)) if (config != null) config.Normalized();
            foreach (Dictionary<string, HullConfig> layouts in file.MoreShips.Values)
            {
                if (layouts == null) continue;
                foreach (HullConfig config in layouts.Values) if (config != null) config.Normalized();
            }
            return file;
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
            if (File.Ships.TryGetValue(hull.ShipGuid.AsHex(), out config) && config != null) return config;
            config = FromGameLoadout(hull, hull.BasePreset).Normalized();
            File.Ships[hull.ShipGuid.AsHex()] = config;
            return config;
        }

        /// <summary>The loadout to change: ships 2 to 6 get their own copy of what they show the first time they are changed.</summary>
        public static HullConfig GetForEdit(HullInfo hull, int ship)
        {
            string key = hull.ShipGuid.AsHex();
            HullConfig own;
            if (ship < 2)
            {
                HullConfig config = Get(hull);
                // While the file can't be read, what is changed is a stand-in copy: what it was is kept (see ReadAgain).
                if (_readOnly) NoteUnreadChange(1, key, config);
                return config;
            }
            if (_readOnly) NoteUnreadChange(ship, key, Own(ship, key));
            return TryGetOwn(hull, ship, out own) ? own : Get(hull, ship).Clone();
        }

        public static void Set(HullInfo hull, HullConfig config, int ship = 1)
        {
            string key = hull.ShipGuid.AsHex();
            // Every change goes through here (picks, other players' changes, LOAD LOADOUT): normalized once, for every read.
            if (_readOnly) NoteUnreadChange(ship, key, null);
            OwnLayouts(ship, true)[key] = config.Normalized();
            Save();
        }

        /// <summary>Ship 2 to 6 follows ship 1's loadout again (or, in a Fleet Battle, starts again from the stand-in's).</summary>
        public static void Forget(HullInfo hull, int ship)
        {
            string key = hull.ShipGuid.AsHex();
            Dictionary<string, HullConfig> own = ship >= 2 ? OwnLayouts(ship, false) : null;
            if (own == null || !own.ContainsKey(key)) return;
            if (_readOnly) NoteUnreadChange(ship, key, Own(ship, key));
            own.Remove(key);
            Save();
        }

        /// <summary>
        /// While the file can't be read: a loadout is about to change, so what it was before its first change is kept, to
        /// tell later whether the file holds something else for it (see <see cref="ReadAgain"/>). <paramref name="before"/>
        /// is null when it had none of its own, or what it was isn't known (a change that didn't come through GetForEdit).
        /// </summary>
        private static void NoteUnreadChange(int ship, string hullHex, HullConfig before)
        {
            var key = new KeyValuePair<int, string>(Math.Max(1, ship), hullHex);
            if (!ChangedUnread.ContainsKey(key)) ChangedUnread[key] = before != null ? before.Clone() : null;
        }

        /// <summary>A player ship's own loadout of a ship type as stored now (ship 1's is its only one), or null.</summary>
        private static HullConfig Own(int ship, string hullHex)
        {
            return Own(File, ship, hullHex);
        }

        private static HullConfig Own(StoreFile file, int ship, string hullHex)
        {
            Dictionary<string, HullConfig> layouts = Layouts(file, ship, false);
            HullConfig config;
            return layouts != null && layouts.TryGetValue(hullHex, out config) ? config : null;
        }

        /// <summary>Whether ship 2 to 6 has its own loadout for this ship type (rather than following ship 1's).</summary>
        public static bool HasOwn(HullInfo hull, int ship)
        {
            HullConfig own;
            return ship >= 2 && TryGetOwn(hull, ship, out own);
        }

        private static bool TryGetOwn(HullInfo hull, int ship, out HullConfig config)
        {
            config = Own(ship, hull.ShipGuid.AsHex());
            return config != null;
        }

        /// <summary>A player ship's loadouts by ship type: ship 1's, ship 2's own, or ship 3 to 6's own (made when asked to).</summary>
        private static Dictionary<string, HullConfig> OwnLayouts(int ship, bool create)
        {
            return Layouts(File, ship, create);
        }

        private static Dictionary<string, HullConfig> Layouts(StoreFile file, int ship, bool create)
        {
            if (ship <= 1) return file.Ships;
            if (ship == 2) return file.SecondShips;
            Dictionary<string, HullConfig> layouts;
            string key = ship.ToString();
            if (!file.MoreShips.TryGetValue(key, out layouts) || layouts == null)
            {
                if (!create) return null;
                layouts = new Dictionary<string, HullConfig>();
                file.MoreShips[key] = layouts;
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

        /// <summary>How long changes wait before they are written, so a burst of picks makes one save instead of many.</summary>
        private const float SaveDelaySeconds = 1f;
        /// <summary>How long changes wait for the next try after a write failed (a virus scanner held the file, say).</summary>
        private const float RetryDelaySeconds = 5f;
        /// <summary>How many times a write that can't wait (see <see cref="Flush(bool)"/>) is tried, a moment apart.</summary>
        private const int QuickTries = 5;
        private static bool _unsaved;
        private static float _unsavedSince;
        /// <summary>After a failed write: not tried again by the regular check before this time.</summary>
        private static float _retryAt;
        private static int _failedWrites;

        /// <summary>Saves the file shortly (see <see cref="Flush(bool)"/>): picks in quick succession are written together.</summary>
        public static void Save()
        {
            Save(Time.unscaledTime);
        }

        /// <summary><see cref="Save()"/> at a given time (so the offline checks can run it without the game).</summary>
        internal static void Save(float time)
        {
            if (_unsaved) return;
            _unsaved = true;
            _unsavedSince = time;
        }

        /// <summary>
        /// Writes what is waiting: once it has waited <see cref="SaveDelaySeconds"/> (called a few times a second), or straight
        /// away with <paramref name="now"/> (a CUSTOM run to remember, a mission starting, the game quitting). Changes stay
        /// waiting until a write works: a virus scanner or a backup tool can hold the file open for a moment, and a CUSTOM
        /// run that never reaches the file would earn progression when it is continued later. A failed write is tried again
        /// <see cref="RetryDelaySeconds"/> later (less often while it keeps failing), and one that can't wait is tried a few
        /// times a moment apart.
        /// </summary>
        public static void Flush(bool now)
        {
            float time = Time.unscaledTime;
            if (Due(now, time)) Flush(now, time, FilePath);
        }

        private static bool Due(bool now, float time)
        {
            return _unsaved && (now || (time - _unsavedSince >= SaveDelaySeconds && time >= _retryAt));
        }

        /// <summary><see cref="Flush(bool)"/> at a given time, to a given file (so the offline checks can run it without the game).</summary>
        internal static void Flush(bool now, float time, string path)
        {
            if (!Due(now, time)) return;
            // Changes are only made to the file as read, so it has been read by now (and whether it can be saved is known).
            if (_file == null)
            {
                _unsaved = false;
                return;
            }
            // A file that couldn't be read is never written over: it is read again first, and the changes wait until it can be.
            if (_readOnly && !ReadAgain(path, now ? QuickTries : 1))
            {
                TryAgainLater(time);
                Log.WarnOnce("store read only", "Your Custom loadouts are not saved for now: " + path + " can't be read (another program may be holding it). " +
                                                "It is read again at each save, and the changes wait until then.");
                if (!_toldUnsaved)
                {
                    _toldUnsaved = true;
                    _notice = "Your CUSTOM changes aren't saved yet: your saved CUSTOM loadouts couldn't be read (another program may be holding the file), " +
                              "so stand-in copies are shown. Ship Customizer keeps trying.";
                }
                return;
            }
            try
            {
                SafeFile.WriteWithRetries(path, JsonConvert.SerializeObject(_file, Formatting.Indented), now ? QuickTries : 1);
            }
            catch (Exception e)
            {
                TryAgainLater(time);
                Log.Failure("Saving the Custom loadouts to " + path + " (tried again a few seconds later, before a mission and as the game closes)", e);
                return;
            }
            _unsaved = false;
            _retryAt = 0f;
            if (_failedWrites > 0) Log.Info("Saved the Custom loadouts to " + path + " after all.");
            _failedWrites = 0;
        }

        /// <summary>A save that didn't happen is tried again a few seconds later; less often while it keeps failing (a full disk, say): every 30 seconds at most.</summary>
        private static void TryAgainLater(float time)
        {
            _failedWrites++;
            _retryAt = time + RetryDelaySeconds * Math.Min(_failedWrites, 6);
        }

        /// <summary>
        /// The file couldn't be read before (another program held it), so the terminal showed stand-in copies: it is read
        /// again. Once it can be, what this session changed meanwhile goes on top of what was read: the CUSTOM runs
        /// remembered, and each changed loadout, unless the file holds another loadout of its own there (one the player had
        /// saved: it is kept rather than written over with a change made to a stand-in copy).
        /// </summary>
        private static bool ReadAgain(string path, int tries)
        {
            StoreFile read;
            if (SafeFile.Load(path, "your Custom loadouts", out read, tries, true) == SafeFile.ReadResult.Unreadable) return false;
            read = Complete(read);
            int kept = 0;
            int left = 0;
            foreach (KeyValuePair<KeyValuePair<int, string>, HullConfig> changed in ChangedUnread)
            {
                int ship = changed.Key.Key;
                string hullHex = changed.Key.Value;
                HullConfig mine = Own(_file, ship, hullHex);
                // Taken to change, but not changed in the end (a player's change that was refused, say): nothing to add.
                if (Same(mine, changed.Value)) continue;
                HullConfig saved = Own(read, ship, hullHex);
                if (!ChangeGoesOnTop(saved, changed.Value))
                {
                    left++;
                    continue;
                }
                if (mine != null) Layouts(read, ship, true)[hullHex] = mine;
                else if (saved != null) Layouts(read, ship, false).Remove(hullHex);
                kept++;
            }
            foreach (string run in RunsUnread)
            {
                read.CustomRuns.Remove(run);
                read.CustomRuns.Add(run);
            }
            while (read.CustomRuns.Count > MaxRememberedRuns) read.CustomRuns.RemoveAt(0);

            _file = read;
            _readOnly = false;
            ChangedUnread.Clear();
            RunsUnread.Clear();
            _readLate = true;
            Log.Info("Read your Custom loadouts from " + path + " after all" + (kept + left == 0 ? "." :
                     ": " + kept + " loadout(s) changed meanwhile are saved, " + left + " kept as saved (they were changed on a stand-in copy)."));
            string notice = ReadLateNotice(kept, left, _toldUnsaved);
            if (notice != null) _notice = notice;
            return true;
        }

        /// <summary>
        /// Whether a loadout changed while the file couldn't be read goes into it: when the file has none of its own there,
        /// or the same one the change was made on (the stand-in it had saved before, say). Not over another one: that is a
        /// loadout the player had saved, and the change was made to a stand-in copy, not to it. When what the change was made
        /// on isn't known (<paramref name="before"/> null), only where the file has none.
        /// </summary>
        internal static bool ChangeGoesOnTop(HullConfig saved, HullConfig before)
        {
            return saved == null || (before != null && Same(saved, before));
        }

        /// <summary>Whether two loadouts are the same, everything in them (both none counts too).</summary>
        private static bool Same(HullConfig a, HullConfig b)
        {
            if (a == null || b == null) return a == b;
            return JsonConvert.SerializeObject(a) == JsonConvert.SerializeObject(b);
        }

        /// <summary>
        /// What the player is told once the file could be read after all: whether their changes made meanwhile are saved,
        /// or where their saved loadout is back instead. Nothing when there were no changes and they weren't told before.
        /// </summary>
        internal static string ReadLateNotice(int kept, int left, bool toldBefore)
        {
            if (left > 0)
            {
                return "Your saved CUSTOM loadouts could be read after all and are back. " +
                       (kept > 0 ? "Changes you made meanwhile to a stand-in copy of a saved one weren't kept; your other changes are saved."
                                 : "The changes you made meanwhile were on stand-in copies, so they weren't kept.");
            }
            if (kept > 0 || toldBefore) return "Your saved CUSTOM loadouts could be read after all, and your changes are saved.";
            return null;
        }

        /// <summary>A message about saving the CUSTOM loadouts for the player, once, or null (CustomizerRuntime shows it).</summary>
        public static string TakeNotice()
        {
            string notice = _notice;
            _notice = null;
            return notice;
        }

        /// <summary>
        /// Whether the file was read after all since this was last asked: the CUSTOM loadouts the terminal shows, and the
        /// host shares, come from it now instead of the stand-in copies.
        /// </summary>
        public static bool TakeReadLate()
        {
            bool late = _readLate;
            _readLate = false;
            return late;
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
            // While the file can't be read, the run waits to go into it once it can (see ReadAgain).
            if (_readOnly && !RunsUnread.Contains(gameSessionId)) RunsUnread.Add(gameSessionId);
            // Straight away: continuing this run from a save must keep progression paused, even after a crash.
            Save();
            Flush(true);
        }

        public static bool WasCustomRun(string gameSessionId)
        {
            if (string.IsNullOrEmpty(gameSessionId)) return false;
            // A file that couldn't be read may hold the run: it is read again, once every few seconds at most (the game asks
            // every frame while a saved run starts).
            if (_readOnly && Time.unscaledTime >= _nextRunRead)
            {
                _nextRunRead = Time.unscaledTime + RetryDelaySeconds;
                ReadAgain(FilePath, 1);
            }
            return File.CustomRuns.Contains(gameSessionId);
        }
    }
}
