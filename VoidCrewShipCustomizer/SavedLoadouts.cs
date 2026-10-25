using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// The player's saved CUSTOM loadouts: numbered slots per ship type, kept on this PC in the game's save folder
    /// (ShipCustomizer\saved-loadouts.json), to load again in any later match, as the host or as another player. Slots
    /// rather than names, so nothing has to be typed (hard with a VR headset on).
    /// </summary>
    internal static class SavedLoadouts
    {
        public const int SlotCount = 10;

        internal sealed class Saved
        {
            /// <summary>1 to <see cref="SlotCount"/>.</summary>
            public int Slot;
            /// <summary>When it was saved (local time), as the list shows it.</summary>
            public string SavedAt = "";
            public HullConfig Config;
            /// <summary>What a newer version saved that this one doesn't know, kept so saving here doesn't drop it.</summary>
            #pragma warning disable 0649 // filled in by Newtonsoft
            [JsonExtensionData]
            public IDictionary<string, JToken> Newer;
            #pragma warning restore 0649
        }

        internal sealed class SavedFile
        {
            public int Version = 1;
            /// <summary>By ship type (its GUID in hex).</summary>
            public Dictionary<string, List<Saved>> Ships = new Dictionary<string, List<Saved>>();
            /// <summary>What a newer version saved that this one doesn't know, kept so saving here doesn't drop it.</summary>
            #pragma warning disable 0649 // filled in by Newtonsoft
            [JsonExtensionData]
            public IDictionary<string, JToken> Newer;
            #pragma warning restore 0649
        }

        private static SavedFile _file;
        /// <summary>The file is there but couldn't be read: it is never written over this session (nothing is changed, so nothing is lost).</summary>
        private static bool _readOnly;

        private static string FilePath
        {
            // Not inlined into what uses the file: the offline checks run that without the game, which alone knows where its
            // save folder is (asking it can't even be compiled outside the game).
            [MethodImpl(MethodImplOptions.NoInlining)]
            get { return Path.Combine(LoadoutStore.Folder, "saved-loadouts.json"); }
        }

        private static SavedFile File
        {
            get
            {
                if (_file != null) return _file;
                if (SafeFile.Load(FilePath, "your saved loadouts", out _file) == SafeFile.ReadResult.Unreadable) _readOnly = true;
                _file = Normalize(_file);
                return _file;
            }
        }

        /// <summary>What a file from this or an older version holds, with anything missing or out of range left out.</summary>
        public static SavedFile Normalize(SavedFile file)
        {
            if (file == null) file = new SavedFile();
            if (file.Ships == null) file.Ships = new Dictionary<string, List<Saved>>();
            foreach (string ship in file.Ships.Keys.ToList())
            {
                List<Saved> saved = file.Ships[ship];
                file.Ships[ship] = saved == null
                    ? new List<Saved>()
                    : saved.Where(s => s != null && s.Config != null && s.Slot >= 1 && s.Slot <= SlotCount)
                           .GroupBy(s => s.Slot).Select(g => g.Last()).OrderBy(s => s.Slot).ToList();
                foreach (Saved s in file.Ships[ship])
                {
                    s.Config.Normalized();
                    if (s.SavedAt == null) s.SavedAt = "";
                }
            }
            return file;
        }

        /// <summary>The loadout saved in a slot for this ship type, or null.</summary>
        public static Saved Get(HullInfo hull, int slot)
        {
            List<Saved> saved;
            return File.Ships.TryGetValue(hull.ShipGuid.AsHex(), out saved) ? saved.FirstOrDefault(s => s.Slot == slot) : null;
        }

        /// <summary>Every loadout saved for this ship type, by slot.</summary>
        public static List<Saved> All(HullInfo hull)
        {
            List<Saved> saved;
            return File.Ships.TryGetValue(hull.ShipGuid.AsHex(), out saved) ? saved.ToList() : new List<Saved>();
        }

        /// <summary>How saving or clearing a slot went.</summary>
        internal enum Result
        {
            Done,
            /// <summary>The file couldn't be read when it was first needed, so nothing is saved over it this session.</summary>
            Unreadable,
            /// <summary>The file couldn't be written just now (another program held it, say); nothing changed.</summary>
            NotWritten
        }

        /// <summary>Saves a copy of this CUSTOM loadout in a slot (replacing what was there).</summary>
        public static Result Save(HullInfo hull, int slot, HullConfig config)
        {
            return Save(hull, slot, config, FilePath);
        }

        /// <summary><see cref="Save(HullInfo, int, HullConfig)"/> to a given file (so the offline checks can run it without the game).</summary>
        internal static Result Save(HullInfo hull, int slot, HullConfig config, string path)
        {
            if (slot < 1 || slot > SlotCount || config == null) return Result.NotWritten;
            SavedFile file = File;
            if (_readOnly) return ReadOnly(path);
            string key = hull.ShipGuid.AsHex();
            List<Saved> saved;
            bool had = file.Ships.TryGetValue(key, out saved);
            List<Saved> before = had ? saved.ToList() : null;
            if (!had)
            {
                saved = new List<Saved>();
                file.Ships[key] = saved;
            }
            saved.RemoveAll(s => s.Slot == slot);
            saved.Add(new Saved { Slot = slot, SavedAt = DateTime.Now.ToString("d MMM HH:mm", CultureInfo.InvariantCulture), Config = config.Clone().Normalized() });
            saved.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            if (Write(file, path)) return Result.Done;
            // Not on the disk, so not in the lists either: the slot keeps what it held (REPLACE had taken it out).
            if (had) file.Ships[key] = before;
            else file.Ships.Remove(key);
            return Result.NotWritten;
        }

        /// <summary>Empties a slot (done when it holds nothing).</summary>
        public static Result Clear(HullInfo hull, int slot)
        {
            return Clear(hull, slot, FilePath);
        }

        /// <summary><see cref="Clear(HullInfo, int)"/> on a given file (so the offline checks can run it without the game).</summary>
        internal static Result Clear(HullInfo hull, int slot, string path)
        {
            SavedFile file = File;
            List<Saved> saved;
            if (!file.Ships.TryGetValue(hull.ShipGuid.AsHex(), out saved) || saved.All(s => s.Slot != slot)) return Result.Done;
            if (_readOnly) return ReadOnly(path);
            List<Saved> before = saved.ToList();
            saved.RemoveAll(s => s.Slot == slot);
            if (Write(file, path)) return Result.Done;
            file.Ships[hull.ShipGuid.AsHex()] = before;
            return Result.NotWritten;
        }

        private static Result ReadOnly(string path)
        {
            Log.WarnOnce("saved read only", "Your saved loadouts are not saved this session: " + path + " could not be read when it was first needed.");
            return Result.Unreadable;
        }

        private static bool Write(SavedFile file, string path)
        {
            try
            {
                SafeFile.Write(path, JsonConvert.SerializeObject(file, Formatting.Indented));
                return true;
            }
            catch (Exception e)
            {
                Log.Failure("Saving your loadouts to " + path, e);
                return false;
            }
        }
    }
}
