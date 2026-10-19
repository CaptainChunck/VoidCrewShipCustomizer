using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using CG;
using CG.Client.Utils;
using CG.Game.SpaceObjects.Controllers;
using CG.Objects;
using CG.Ship.Hull;
using CG.Ship.Modules;
using CG.Ship.Object;
using CG.Space;
using Client.Player.Interactions;
using Gameplay.CompositeWeapons;
using Newtonsoft.Json.Linq;
using ResourceAssets;
using UnityEngine;

namespace VoidCrewShipCustomizer
{
    internal enum OptionKind
    {
        Module,
        Weapon,
        Item,
        WeaponCrate
    }

    /// <summary>One thing a player can pick: a module or weapon for a hardpoint, or an item for a storage-rack slot.</summary>
    internal sealed class Option
    {
        /// <summary>What <see cref="LoadoutStore"/> saves: "m:", "w:", "i:" or "c:" followed by the GUID in hex.</summary>
        public string Code;
        public OptionKind Kind;
        /// <summary>The module, composite weapon or carryable GUID.</summary>
        public GUIDUnion Guid;
        /// <summary>What actually spawns: the module prefab (a weapon's core element) or the carryable.</summary>
        public GUIDUnion PrefabGuid;
        public string Name;
        public ContextInfo Context;
        public Sprite Icon;
        public RarityType Rarity;
        public string Category;
        public int CategoryOrder;
        /// <summary>
        /// The category a Void Crew Fleet match's rules allow or refuse (see <see cref="MatchRules.Categories"/>). Unlike
        /// <see cref="Category"/> it is never translated, and the module and item lists' SUPPORT get names of their own.
        /// </summary>
        public string RuleCategory = "OTHER";
        public bool HasSize;
        public BuildSize Size;
        public bool SingleInstance;
        public Type ModuleType;
        /// <summary>Read once by <see cref="Catalog.BlessingOf"/>: a Blessed Homunculus's blessing, "" for none, null until read.</summary>
        public string Blessing;
    }

    /// <summary>One build socket (hardpoint position) of a ship, read from the ship prefab.</summary>
    internal sealed class SocketInfo
    {
        public int Index;
        public BuildSize Size;
        /// <summary>Centres of the 4 m grid cells the socket covers, in prefab space.</summary>
        public Vector3[] Cells = new Vector3[0];
        /// <summary>Sockets whose cells overlap this one; only one of two overlapping sockets can hold a module.</summary>
        public readonly List<int> Overlaps = new List<int>();
        /// <summary>Overlapping sockets form one hardpoint, e.g. a 2x1 socket and the two 1x1 sockets inside it.</summary>
        public int Group;
    }

    /// <summary>A storage rack: a core system of the ship whose sockets hold carryables.</summary>
    internal sealed class ShelfInfo
    {
        public int CoreIndex;
        public GUIDUnion ModuleGuid;
        public string Name;
        public List<CarryablesSocket> Slots = new List<CarryablesSocket>();
        public readonly Dictionary<string, bool> AcceptCache = new Dictionary<string, bool>();
    }

    internal sealed class HullInfo
    {
        public GUIDUnion ShipGuid;
        public string Name;
        /// <summary>
        /// The loadout a CUSTOM loadout is built on (core systems, extra items) and shown to other players as: the game's
        /// default loadout when it is for this ship (ROOKIE), otherwise the lowest-rank loadout the terminal lists.
        /// </summary>
        public ShipLoadoutDataDef BasePreset;
        /// <summary>Every loadout for this ship: the ones the terminal lists first, then challenge and test loadouts.</summary>
        public readonly List<ShipLoadoutDataDef> Presets = new List<ShipLoadoutDataDef>();
        /// <summary>The loadouts the terminal lists for this ship (the game's rank unlocks), lowest rank first.</summary>
        public readonly List<ShipLoadoutDataDef> RegularPresets = new List<ShipLoadoutDataDef>();
        public readonly List<SocketInfo> Sockets = new List<SocketInfo>();
        public readonly List<ShelfInfo> Shelves = new List<ShelfInfo>();
        public int GroupCount;
    }

    /// <summary>
    /// Everything the customizer needs to know about the game's ships and items, read from the game's own asset
    /// containers and prefabs the first time it is needed. Nothing here is hard-coded, so new ships, modules and items
    /// from game updates show up by themselves.
    /// </summary>
    internal static class Catalog
    {
        private static readonly Dictionary<GUIDUnion, HullInfo> Hulls = new Dictionary<GUIDUnion, HullInfo>();
        private static readonly HashSet<GUIDUnion> FailedHulls = new HashSet<GUIDUnion>();
        private static readonly Dictionary<string, Option> ByCode = new Dictionary<string, Option>();
        private static readonly Dictionary<GUIDUnion, JObject> PresetContents = new Dictionary<GUIDUnion, JObject>();
        private static List<Option> _slotOptions;
        private static List<Option> _itemOptions;
        private static List<Option> _homunculusOptions;
        private static bool _presetContentsRead;
        private static GUIDUnion _weaponCrate;
        private static bool _weaponCrateRead;

        // ---------------------------------------------------------------- ships

        /// <summary>The ships the loadout terminal lists, in its order.</summary>
        public static List<GUIDUnion> ShipGuids()
        {
            var result = new List<GUIDUnion>();
            foreach (MonoBehaviourRef ship in ResourceAssetContainer<ShipLoadoutDataContainer, ShipLoadoutData, ShipLoadoutDataDef>.Instance.Ships)
            {
                if (ship != null && !ship.IsNull) result.Add(ship.AssetGuid);
            }
            return result;
        }

        public static bool TryGetHull(GUIDUnion shipGuid, out HullInfo hull)
        {
            if (Hulls.TryGetValue(shipGuid, out hull)) return true;
            if (shipGuid.IsNull || FailedHulls.Contains(shipGuid)) return false;
            try
            {
                var timer = Stopwatch.StartNew();
                hull = BuildHull(shipGuid);
                Hulls[shipGuid] = hull;
                Log.Info(string.Format("Read the {0}: {1} hardpoint positions in {2} hardpoints, {3} storage racks, {4} loadouts ({5} ms).",
                    hull.Name, hull.Sockets.Count, hull.GroupCount, hull.Shelves.Count, hull.Presets.Count, timer.ElapsedMilliseconds));
                return true;
            }
            catch (Exception e)
            {
                FailedHulls.Add(shipGuid);
                Log.Error("Could not read ship " + shipGuid + ", so it gets no CUSTOM loadout: " + e);
                hull = null;
                return false;
            }
        }

        private static HullInfo BuildHull(GUIDUnion shipGuid)
        {
            var hull = new HullInfo { ShipGuid = shipGuid, Name = HeaderOf(ContextOf(shipGuid), "SHIP") };

            var presets = new List<ShipLoadoutDataDef>();
            foreach (ShipLoadoutDataDef def in ResourceAssetContainer<ShipLoadoutDataContainer, ShipLoadoutData, ShipLoadoutDataDef>.Instance.AssetDescriptions)
            {
                try
                {
                    if (def != null && !def.IsNull && def.Asset != null && def.Asset.ShipLoadout != null && def.ShipGuid == shipGuid) presets.Add(def);
                }
                catch (Exception e)
                {
                    Log.Verbose("Skipped a loadout while reading " + hull.Name + ": " + e.Message);
                }
            }
            if (presets.Count == 0) throw new InvalidOperationException("the game has no loadouts for this ship");

            // The terminal lists the loadouts unlocked by rank; challenge and test loadouts (Artillery Boat, Empty, ...)
            // are in the same container but never listed, so they must not stand in for CUSTOM.
            ShipLoadoutDataContainer container = ResourceAssetContainer<ShipLoadoutDataContainer, ShipLoadoutData, ShipLoadoutDataDef>.Instance;
            var listed = new List<ShipLoadoutDataDef>();
            try
            {
                listed.AddRange(container.GetUnlockedShipDefinitionsFromHighestRankPlayer());
                listed.AddRange(container.GetLockedShipDefinitionsFromHighestRankPlayer());
            }
            catch (Exception e)
            {
                Log.Warn("Could not read which loadouts the terminal lists: " + e.Message);
            }
            hull.RegularPresets.AddRange(presets.Where(listed.Contains).OrderBy(RankRequirement));
            hull.Presets.AddRange(hull.RegularPresets);
            hull.Presets.AddRange(presets.Where(p => !listed.Contains(p)));

            ShipLoadoutDataDef gameDefault = null;
            try
            {
                gameDefault = container.GetDefault();
            }
            catch
            {
                // fall back to the lowest-rank listed loadout
            }
            hull.BasePreset = gameDefault != null && presets.Contains(gameDefault) ? gameDefault : (hull.RegularPresets.FirstOrDefault() ?? presets[0]);

            MonoBehaviour shipPrefab = hull.BasePreset.Asset.ShipLoadout.ReferenceShip.Asset;
            if (shipPrefab == null) throw new InvalidOperationException("the ship prefab could not be loaded");

            BuildSocketController controller = shipPrefab.GetComponent<BuildSocketController>();
            List<BuildSocket> sockets = controller != null && controller.Sockets != null && controller.Sockets.Count > 0
                ? controller.Sockets
                : new List<BuildSocket>(shipPrefab.GetComponentsInChildren<BuildSocket>(true));
            for (int i = 0; i < sockets.Count; i++)
            {
                var info = new SocketInfo { Index = i };
                if (sockets[i] != null)
                {
                    info.Size = sockets[i].Size;
                    info.Cells = CellsOf(sockets[i]);
                }
                hull.Sockets.Add(info);
            }
            hull.GroupCount = Hardpoints.LinkOverlaps(hull.Sockets);

            PlayerControlledShip ship = shipPrefab.GetComponent<PlayerControlledShip>();
            CellModule[] core = ship != null ? ship.CoreSystems : null;
            if (core != null)
            {
                for (int i = 0; i < core.Length; i++)
                {
                    CarryablesShelf shelf = core[i] != null ? core[i].GetComponent<CarryablesShelf>() : null;
                    if (shelf == null) continue;
                    var info = new ShelfInfo { CoreIndex = i, ModuleGuid = shelf.assetGuid };
                    info.Slots.AddRange(shelf.CarryablesSockets.Where(s => s != null));
                    info.Name = "STORAGE RACK " + (hull.Shelves.Count + 1);
                    hull.Shelves.Add(info);
                }
            }
            return hull;
        }

        private static Vector3[] CellsOf(BuildSocket socket)
        {
            Transform t = socket.transform;
            return Hardpoints.CellsFor(socket.Size, t.position, t.right, t.forward);
        }

        private static int RankRequirement(ShipLoadoutDataDef def)
        {
            try
            {
                UnlockItemDef unlock = ResourceAssetContainer<UnlockContainer, UnityEngine.Object, UnlockItemDef>.Instance.GetAssetDefById(def.AssetGuid, false);
                return unlock != null ? unlock.UnlockOptions.RankRequirement : int.MaxValue;
            }
            catch
            {
                return int.MaxValue;
            }
        }

        // ---------------------------------------------------------------- options

        /// <summary>Every module and weapon a hardpoint can hold, sorted by category, rarity and name.</summary>
        public static List<Option> SlotOptions
        {
            get
            {
                if (_slotOptions != null) return _slotOptions;
                var timer = Stopwatch.StartNew();
                var list = new List<Option>();
                AddWeaponOptions(list);
                AddModuleOptions(list);
                _slotOptions = list.OrderBy(o => o.CategoryOrder).ThenBy(o => o.Rarity == RarityType.None ? 0 : (int)o.Rarity).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (Option option in _slotOptions) ByCode[option.Code] = option;
                Log.Info(string.Format("Found {0} weapons and {1} modules that fit in hardpoints ({2} ms).",
                    _slotOptions.Count(o => o.Kind == OptionKind.Weapon), _slotOptions.Count(o => o.Kind == OptionKind.Module), timer.ElapsedMilliseconds));
                return _slotOptions;
            }
        }

        /// <summary>Every item a storage rack can hold, including crates that build a weapon.</summary>
        public static List<Option> ItemOptions
        {
            get
            {
                if (_itemOptions != null) return _itemOptions;
                var timer = Stopwatch.StartNew();
                var list = new List<Option>();
                AddItemOptions(list);
                _itemOptions = list.OrderBy(o => o.CategoryOrder).ThenBy(o => o.Rarity == RarityType.None ? 0 : (int)o.Rarity).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (Option option in _itemOptions) ByCode[option.Code] = option;
                Log.Info(string.Format("Found {0} items for the storage racks ({1} ms).", _itemOptions.Count, timer.ElapsedMilliseconds));
                return _itemOptions;
            }
        }

        /// <summary>The homunculus variants the game's own loadouts use (each Blessed Homunculus suits its loadout).</summary>
        public static List<Option> HomunculusOptions
        {
            get
            {
                if (_homunculusOptions != null) return _homunculusOptions;
                var seen = new HashSet<GUIDUnion>();
                var list = new List<Option>();
                foreach (ShipLoadoutDataDef def in ResourceAssetContainer<ShipLoadoutDataContainer, ShipLoadoutData, ShipLoadoutDataDef>.Instance.AssetDescriptions)
                {
                    try
                    {
                        CarryableRef homunculus = def.Asset.ShipLoadout.HomunculusOverride;
                        if (homunculus == null || homunculus.IsNull || !seen.Add(homunculus.AssetGuid)) continue;
                        Option option = ItemOption(homunculus.AssetGuid, "HOMUNCULUS", 0);
                        if (option != null) list.Add(option);
                    }
                    catch (Exception e)
                    {
                        Log.Verbose("Skipped a loadout's homunculus: " + e.Message);
                    }
                }
                _homunculusOptions = list.OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (Option option in _homunculusOptions) ByCode[option.Code] = option;
                return _homunculusOptions;
            }
        }

        /// <summary>Whether an "i:" code is one of the homunculus variants (a Blessed Homunculus), wherever it is used.</summary>
        public static bool IsHomunculus(string code)
        {
            return !string.IsNullOrEmpty(code) && HomunculusOptions.Any(o => o.Code == code);
        }

        /// <summary>
        /// What a Blessed Homunculus's blessing does, from the game's own description of it (the lines under "Blessing" in
        /// its tooltip, with the game's highlight colours), so the menus can show it without hovering; null without one.
        /// </summary>
        public static string BlessingOf(Option option)
        {
            if (option == null || option.Context == null) return null;
            if (option.Blessing == null)
            {
                try
                {
                    option.Blessing = RichText.LastSection(option.Context.BodyText) ?? "";
                }
                catch (Exception e)
                {
                    option.Blessing = "";
                    Log.Verbose("Could not read the blessing of " + option.Name + ": " + e.Message);
                }
            }
            return option.Blessing.Length > 0 ? option.Blessing : null;
        }

        public static bool TryGetOption(string code, out Option option)
        {
            option = null;
            if (string.IsNullOrEmpty(code)) return false;
            if (ByCode.TryGetValue(code, out option)) return true;
            // Make sure the list the code belongs to has been read.
            if (code.StartsWith("m:") || code.StartsWith("w:")) { List<Option> unused = SlotOptions; }
            else { List<Option> unused = ItemOptions; }
            if (ByCode.TryGetValue(code, out option)) return true;
            // Something no list shows (a hidden module from a game loadout, a homunculus, a quest item while those are
            // hidden): describe it anyway so copying a game loadout keeps everything in it.
            if (code.Length > 2 && GUIDUnion.IsValidString(code.Substring(2)))
            {
                var guid = new GUIDUnion(code.Substring(2));
                if (code.StartsWith("m:")) option = RawModuleOption(guid);
                else if (code.StartsWith("i:")) option = ItemOption(guid, "OTHER", 99);
                if (option != null) ByCode[code] = option;
            }
            return option != null;
        }

        /// <summary>Whether the game can spawn this GUID (ObjectFactory looks every spawned object up here).</summary>
        public static bool CanSpawn(GUIDUnion guid)
        {
            return !guid.IsNull && ResourceAssetContainer<CloneStarObjectContainer, AbstractCloneStarObject, CloneStarObjectDef>.Instance.HasItem(guid);
        }

        private static Option RawModuleOption(GUIDUnion guid)
        {
            if (!CanSpawn(guid)) return null;
            ContextInfo context = ContextOf(guid);
            var option = new Option
            {
                Code = "m:" + guid.AsHex(),
                Kind = OptionKind.Module,
                Guid = guid,
                PrefabGuid = guid,
                Name = HeaderOf(context, "Module"),
                Context = context,
                Icon = context != null ? context.Icon : null,
                Rarity = RarityOf(guid),
                Category = "OTHER",
                CategoryOrder = 9
            };
            try
            {
                ModuleDef def = ResourceAssetContainer<ModuleContainer, CellModule, ModuleDef>.Instance.GetAssetDefById(guid, false);
                CellModule prefab = def != null ? def.Asset : null;
                BuildSize size;
                if (prefab != null && TryGetSize(prefab.BuildingConstraints, out size))
                {
                    option.HasSize = true;
                    option.Size = size;
                    option.SingleInstance = prefab.BuildingConstraints.AllowOnlySingleInstance;
                    option.ModuleType = prefab.GetType();
                }
            }
            catch (Exception e)
            {
                Log.Verbose("Could not read the size of module " + option.Name + ": " + e.Message);
            }
            return option;
        }

        private static void AddWeaponOptions(List<Option> list)
        {
            foreach (CompositeWeaponDataDef def in ResourceAssetContainer<CompositeWeaponDataContainer, CompositeWeaponData, CompositeWeaponDataDef>.Instance.AssetDescriptions)
            {
                try
                {
                    if (def == null || def.IsNull) continue;
                    CompositeWeaponData data = def.Asset;
                    if (data == null || data.Data == null || data.Data.CoreElement == null || data.Data.CoreElement.IsNull) continue;
                    if (!CanSpawn(data.Data.CoreElement.AssetGuid)) continue;
                    CompositeWeaponModule core = data.Data.CoreElement.Asset;
                    if (core == null) continue;
                    BuildSize size;
                    if (!TryGetSize(core.BuildingConstraints, out size)) continue;
                    ContextInfo context = def.ContextInfo as ContextInfo;
                    if (IsMissing(context)) continue;
                    list.Add(new Option
                    {
                        Code = "w:" + def.AssetGuid.AsHex(),
                        Kind = OptionKind.Weapon,
                        Guid = def.AssetGuid,
                        PrefabGuid = data.Data.CoreElement.AssetGuid,
                        Name = HeaderOf(context, data.name),
                        Context = context,
                        Icon = context.Icon,
                        Rarity = RarityOf(def.AssetGuid),
                        Category = CategoryName(ECategory.Weapons),
                        CategoryOrder = (int)ECategory.Weapons,
                        RuleCategory = "WEAPONS",
                        HasSize = true,
                        Size = size,
                        SingleInstance = core.BuildingConstraints.AllowOnlySingleInstance,
                        ModuleType = core.GetType()
                    });
                }
                catch (Exception e)
                {
                    Log.Verbose("Skipped weapon " + (def != null ? def.Path : "?") + ": " + e.Message);
                }
            }
        }

        private static void AddModuleOptions(List<Option> list)
        {
            foreach (ModuleDef def in ResourceAssetContainer<ModuleContainer, CellModule, ModuleDef>.Instance.AssetDescriptions)
            {
                try
                {
                    if (def == null || def.IsNull || def.Category == ECategory.Hidden || !CanSpawn(def.AssetGuid)) continue;
                    CellModule prefab = def.Asset;
                    if (prefab == null || prefab is CompositeWeaponModule || prefab.GetComponent<CarryablesShelf>() != null) continue;
                    BuildSize size;
                    if (!TryGetSize(prefab.BuildingConstraints, out size)) continue;
                    ContextInfo context = ContextOf(def.AssetGuid);
                    if (IsMissing(context)) continue;
                    list.Add(new Option
                    {
                        Code = "m:" + def.AssetGuid.AsHex(),
                        Kind = OptionKind.Module,
                        Guid = def.AssetGuid,
                        PrefabGuid = def.AssetGuid,
                        Name = HeaderOf(context, prefab.name),
                        Context = context,
                        Icon = context.Icon,
                        Rarity = RarityOf(def.AssetGuid),
                        Category = CategoryName(def.Category),
                        CategoryOrder = (int)def.Category,
                        RuleCategory = ModuleRuleCategory(def.Category),
                        HasSize = true,
                        Size = size,
                        SingleInstance = prefab.BuildingConstraints.AllowOnlySingleInstance,
                        ModuleType = prefab.GetType()
                    });
                }
                catch (Exception e)
                {
                    Log.Verbose("Skipped module " + (def != null ? def.Path : "?") + ": " + e.Message);
                }
            }
        }

        /// <summary>The game's crate that builds whichever weapon its extra data names (an Inscribed Animus Crate).</summary>
        public static GUIDUnion WeaponCrateGuid
        {
            get
            {
                if (_weaponCrateRead) return _weaponCrate;
                _weaponCrateRead = true;
                try
                {
                    _weaponCrate = ResourceAssetContainer<CarryableContainer, CarryableObject, CarryableDef>.Instance.WeaponBuildBox.AssetGuid;
                }
                catch (Exception e)
                {
                    Log.Warn("Could not find the game's weapon crate, so weapons can't be put on storage racks: " + e.Message);
                }
                return _weaponCrate;
            }
        }

        private static void AddItemOptions(List<Option> list)
        {
            CarryableContainer items = ResourceAssetContainer<CarryableContainer, CarryableObject, CarryableDef>.Instance;
            GUIDUnion weaponCrate = WeaponCrateGuid;

            foreach (CarryableDef def in items.AssetDescriptions)
            {
                try
                {
                    if (def == null || def.IsNull || def.AssetGuid == weaponCrate) continue;
                    if (def.Category == PurchasableItemSubCategory.Special_Quest && !Settings.ShowQuestItems.Value) continue;
                    Option option = ItemOption(def.AssetGuid, ItemCategoryName(def.Category), ItemCategoryOrder(def.Category));
                    if (option != null) list.Add(option);
                }
                catch (Exception e)
                {
                    Log.Verbose("Skipped item " + (def != null ? def.Path : "?") + ": " + e.Message);
                }
            }

            if (weaponCrate.IsNull || !CanSpawn(weaponCrate)) return;
            foreach (Option weapon in SlotOptions.Where(o => o.Kind == OptionKind.Weapon))
            {
                Sprite crateIcon = weapon.Icon;
                try
                {
                    crateIcon = ResourceAssetContainer<CompositeWeaponDataContainer, CompositeWeaponData, CompositeWeaponDataDef>.Instance.GetAssetDefById(weapon.Guid).BuildBoxIcon;
                }
                catch
                {
                    // keep the weapon's own icon
                }
                list.Add(new Option
                {
                    Code = "c:" + weapon.Guid.AsHex(),
                    Kind = OptionKind.WeaponCrate,
                    Guid = weapon.Guid,
                    PrefabGuid = weaponCrate,
                    Name = weapon.Name,
                    Context = weapon.Context,
                    Icon = crateIcon,
                    Rarity = weapon.Rarity,
                    Category = "WEAPON CRATES",
                    CategoryOrder = 5,
                    RuleCategory = "WEAPON CRATES"
                });
            }
        }

        private static Option ItemOption(GUIDUnion guid, string category, int order)
        {
            if (!CanSpawn(guid)) return null;
            ContextInfo context = ContextOf(guid);
            if (IsMissing(context)) return null;
            return new Option
            {
                Code = "i:" + guid.AsHex(),
                Kind = OptionKind.Item,
                Guid = guid,
                PrefabGuid = guid,
                Name = HeaderOf(context, "Item"),
                Context = context,
                Icon = context.Icon,
                Rarity = RarityOf(guid),
                Category = category,
                CategoryOrder = order,
                RuleCategory = category == "SUPPORT" ? "SUPPORT ITEMS" : category
            };
        }

        /// <summary>A module's category for a match's rules: the module list's SUPPORT is SUPPORT MODULES there.</summary>
        private static string ModuleRuleCategory(ECategory category)
        {
            switch (category)
            {
                case ECategory.Weapons: return "WEAPONS";
                case ECategory.Defense: return "DEFENSE";
                case ECategory.Support: return "SUPPORT MODULES";
                default: return "OTHER";
            }
        }

        private static string ItemCategoryName(PurchasableItemSubCategory category)
        {
            switch (category)
            {
                case PurchasableItemSubCategory.Resources_Ammo: return "AMMO & POWER";
                case PurchasableItemSubCategory.Resources_Support: return "SUPPORT";
                case PurchasableItemSubCategory.Resources_Materials: return "MATERIALS";
                case PurchasableItemSubCategory.AnimusCrates_Offensive:
                case PurchasableItemSubCategory.AnimusCrates_Defensive:
                case PurchasableItemSubCategory.AnimusCrates_Tactical:
                case PurchasableItemSubCategory.AnimusCrates_Utility: return "ANIMUS CRATES";
                case PurchasableItemSubCategory.Mods_Offensive: return "WEAPON MODS";
                case PurchasableItemSubCategory.Mods_Relic: return "RELICS";
                case PurchasableItemSubCategory.Special_Quest:
                case PurchasableItemSubCategory.Special_Unique: return "SPECIAL";
                default: return "OTHER";
            }
        }

        private static int ItemCategoryOrder(PurchasableItemSubCategory category)
        {
            switch (ItemCategoryName(category))
            {
                case "AMMO & POWER": return 1;
                case "SUPPORT": return 2;
                case "MATERIALS": return 3;
                case "ANIMUS CRATES": return 4;
                case "WEAPON MODS": return 6;
                case "RELICS": return 7;
                case "SPECIAL": return 8;
                default: return 9;
            }
        }

        private static string CategoryName(ECategory category)
        {
            try
            {
                string localized = category.ToStringLocalized();
                if (!string.IsNullOrEmpty(localized)) return localized.ToUpperInvariant();
            }
            catch
            {
                // fall back to the enum name
            }
            return category.ToString().ToUpperInvariant();
        }

        // ---------------------------------------------------------------- storage racks

        /// <summary>
        /// Whether a storage rack normally takes an item: the accepted / rejected rules its slots check when a player puts
        /// an item in by hand. The game doesn't check them when it fills the racks at mission start, so any item can start
        /// on any rack; the picker only uses this when the AnyItemOnAnyRack setting is off.
        /// </summary>
        public static bool ShelfAccepts(ShelfInfo shelf, Option item)
        {
            if (shelf.Slots.Count == 0) return true;
            string key = item.Kind == OptionKind.WeaponCrate ? "crate" : item.Code;
            bool accepted;
            if (shelf.AcceptCache.TryGetValue(key, out accepted)) return accepted;

            accepted = true;
            try
            {
                CarryablesSocket slot = shelf.Slots[0];
                bool needPrefab = false;
                if (slot.rejectedList != null)
                {
                    foreach (CsObjectReference rejected in slot.rejectedList)
                    {
                        bool? match = QuickMatch(rejected, item.PrefabGuid);
                        if (match == true) accepted = false;
                        if (match == null) needPrefab = true;
                    }
                }
                if (accepted)
                {
                    bool? match = QuickMatch(slot.accepted, item.PrefabGuid);
                    if (match == false) accepted = false;
                    if (match == null) needPrefab = true;
                }
                if (accepted && needPrefab)
                {
                    // A rule by type or tag: only the item's prefab can answer it.
                    CarryableDef def = ResourceAssetContainer<CarryableContainer, CarryableObject, CarryableDef>.Instance.GetAssetDefById(item.PrefabGuid, false);
                    var asset = (def != null ? def.Asset : null) as ISelfReferencingResourceAsset;
                    if (asset != null)
                    {
                        if (slot.rejectedList != null && slot.rejectedList.Any(r => r != null && r.IsInstanceOf(asset))) accepted = false;
                        else if (slot.accepted != null) accepted = slot.accepted.IsInstanceOf(asset);
                    }
                }
            }
            catch (Exception e)
            {
                Log.Verbose("Could not check whether " + shelf.Name + " takes " + item.Name + ": " + e.Message);
                accepted = true;
            }
            shelf.AcceptCache[key] = accepted;
            return accepted;
        }

        private static readonly System.Reflection.FieldInfo CompareMethodField = HarmonyLib.AccessTools.Field(typeof(CsObjectReference), "compareMethod");
        private static readonly System.Reflection.FieldInfo PrefabRuleField = HarmonyLib.AccessTools.Field(typeof(CsObjectReference), "orbitObjectRef");

        /// <summary>Answers a rack rule without loading the item's prefab when it can; null when the prefab is needed.</summary>
        private static bool? QuickMatch(CsObjectReference rule, GUIDUnion item)
        {
            if (rule == null) return false;
            if (CompareMethodField == null) return null;
            switch ((CsObjectReference.Comparison)CompareMethodField.GetValue(rule))
            {
                case CsObjectReference.Comparison.None:
                    return false;
                case CsObjectReference.Comparison.Any:
                    return true;
                case CsObjectReference.Comparison.Prefab:
                    var prefab = PrefabRuleField != null ? PrefabRuleField.GetValue(rule) as ResourceAssetRef : null;
                    return prefab != null && !prefab.IsNull && prefab.AssetGuid == item;
                default:
                    return null;
            }
        }

        // ---------------------------------------------------------------- loadout entries

        /// <summary>The option a game loadout's socket entry corresponds to, or null.</summary>
        public static Option OptionFor(ShipSocketEntry entry)
        {
            if (entry == null || entry.Module == null || entry.Module.IsNull) return null;
            GUIDUnion weapon = WeaponGuidOf(entry.ExtraData);
            Option option;
            if (!weapon.IsNull && TryGetOption("w:" + weapon.AsHex(), out option)) return option;
            if (TryGetOption("m:" + entry.Module.AssetGuid.AsHex(), out option)) return option;
            return null;
        }

        /// <summary>The code for an item on a storage rack in a game loadout, or "" for an empty slot.</summary>
        public static string CodeFor(ShipAssetRef item)
        {
            if (item == null || item.AssetRef == null || item.IsNull) return "";
            GUIDUnion weapon = WeaponGuidOf(item.ExtraData);
            if (!weapon.IsNull) return "c:" + weapon.AsHex();
            return "i:" + item.AssetGuid.AsHex();
        }

        public static GUIDUnion WeaponGuidOf(JObject extraData)
        {
            JToken guid;
            if (extraData == null || !extraData.TryGetValue("AssetGuid", out guid)) return GUIDUnion.Empty();
            var result = new GUIDUnion(guid.ToString());
            return ResourceAssetContainer<CompositeWeaponDataContainer, CompositeWeaponData, CompositeWeaponDataDef>.Instance.HasItem(result) ? result : GUIDUnion.Empty();
        }

        /// <summary>
        /// A socket entry from one of the game's loadouts that has the given module or weapon, as JSON, so a custom
        /// loadout can start that module with the same contents (ammo, power cells) the game's designers gave it.
        /// </summary>
        public static bool TryGetPresetContents(GUIDUnion moduleOrWeapon, out JObject entry)
        {
            if (!_presetContentsRead)
            {
                _presetContentsRead = true;
                foreach (ShipLoadoutDataDef def in ResourceAssetContainer<ShipLoadoutDataContainer, ShipLoadoutData, ShipLoadoutDataDef>.Instance.AssetDescriptions)
                {
                    try
                    {
                        foreach (ShipSocketEntry socket in def.Asset.ShipLoadout.Sockets)
                        {
                            if (socket == null || socket.Module == null || socket.Module.IsNull) continue;
                            GUIDUnion key = WeaponGuidOf(socket.ExtraData);
                            if (key.IsNull) key = socket.Module.AssetGuid;
                            bool hasContents = socket.Upgrades != null && socket.Upgrades.Any(u => u != null && !u.IsNull);
                            JObject existing;
                            // Prefer an entry that actually has something inside the module.
                            if (PresetContents.TryGetValue(key, out existing) && (!hasContents || HasContents(existing))) continue;
                            PresetContents[key] = JObject.FromObject(socket);
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Verbose("Skipped a loadout while reading module contents: " + e.Message);
                    }
                }
            }
            return PresetContents.TryGetValue(moduleOrWeapon, out entry);
        }

        private static bool HasContents(JObject entry)
        {
            var upgrades = entry["Upgrades"] as JArray;
            return upgrades != null && upgrades.Any(u => u is JObject && u["AssetRef"] is JObject && !new GUIDUnion(((JObject)u["AssetRef"])["AssetGuid"]?.ToString()).IsNull);
        }

        // ---------------------------------------------------------------- helpers

        public static ContextInfo ContextOf(GUIDUnion guid)
        {
            if (guid.IsNull) return null;
            return ResourceAssetContainer<CloneStarObjectContainer, AbstractCloneStarObject, CloneStarObjectDef>.Instance.GetContext(guid) as ContextInfo;
        }

        public static string HeaderOf(ContextInfo context, string fallback)
        {
            try
            {
                string header = context != null ? context.HeaderText : null;
                return string.IsNullOrEmpty(header) ? fallback : header;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool IsMissing(ContextInfo context)
        {
            if (context == null) return true;
            try
            {
                return context == (DataTable<DefaultAssetTable>.Instance.MissingContextInfo as ContextInfo);
            }
            catch
            {
                return false;
            }
        }

        public static RarityType RarityOf(GUIDUnion guid)
        {
            try
            {
                return ResourceAssetContainer<UnlockContainer, UnityEngine.Object, UnlockItemDef>.Instance.GetRarity(guid);
            }
            catch
            {
                return RarityType.None;
            }
        }

        private static bool TryGetSize(CG.Game.Configuration.BuildingConstraints constraints, out BuildSize size)
        {
            size = BuildSize.Small;
            if (constraints == null) return false;
            try
            {
                size = BuildSizeExtensions.GetSize(constraints.Width, constraints.Height, constraints.Depth);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string SizeName(BuildSize size)
        {
            switch (size)
            {
                case BuildSize.Small: return "1x1";
                case BuildSize.Medium: return "2x1";
                default: return "2x2";
            }
        }
    }
}
