using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

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
        }

        internal sealed class SavedFile
        {
            public int Version = 1;
            /// <summary>By ship type (its GUID in hex).</summary>
            public Dictionary<string, List<Saved>> Ships = new Dictionary<string, List<Saved>>();
        }

        private static SavedFile _file;

        private static string FilePath
        {
            get { return Path.Combine(LoadoutStore.Folder, "saved-loadouts.json"); }
        }

        private static SavedFile File
        {
            get
            {
                if (_file != null) return _file;
                try
                {
                    if (System.IO.File.Exists(FilePath)) _file = JsonConvert.DeserializeObject<SavedFile>(System.IO.File.ReadAllText(FilePath));
                }
                catch (Exception e)
                {
                    string broken = FilePath + ".broken";
                    Log.Error("Could not read " + FilePath + ", so there are no saved loadouts. The old file is kept as " + broken + ". Details: " + e.Message);
                    try
                    {
                        System.IO.File.Copy(FilePath, broken, true);
                    }
                    catch
                    {
                        // nothing more to do
                    }
                }
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

        /// <summary>Saves a copy of this CUSTOM loadout in a slot (replacing what was there).</summary>
        public static void Save(HullInfo hull, int slot, HullConfig config)
        {
            if (slot < 1 || slot > SlotCount || config == null) return;
            List<Saved> saved;
            if (!File.Ships.TryGetValue(hull.ShipGuid.AsHex(), out saved))
            {
                saved = new List<Saved>();
                File.Ships[hull.ShipGuid.AsHex()] = saved;
            }
            saved.RemoveAll(s => s.Slot == slot);
            saved.Add(new Saved { Slot = slot, SavedAt = DateTime.Now.ToString("d MMM HH:mm", CultureInfo.InvariantCulture), Config = config.Clone().Normalized() });
            saved.Sort((a, b) => a.Slot.CompareTo(b.Slot));
            Write();
        }

        public static void Clear(HullInfo hull, int slot)
        {
            List<Saved> saved;
            if (File.Ships.TryGetValue(hull.ShipGuid.AsHex(), out saved) && saved.RemoveAll(s => s.Slot == slot) > 0) Write();
        }

        private static void Write()
        {
            try
            {
                Directory.CreateDirectory(LoadoutStore.Folder);
                string temp = FilePath + ".tmp";
                System.IO.File.WriteAllText(temp, JsonConvert.SerializeObject(File, Formatting.Indented));
                if (System.IO.File.Exists(FilePath)) System.IO.File.Delete(FilePath);
                System.IO.File.Move(temp, FilePath);
            }
            catch (Exception e)
            {
                Log.Failure("Saving your loadouts to " + FilePath, e);
            }
        }
    }
}
