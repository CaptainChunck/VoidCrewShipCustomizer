using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CG.UI.MainMenu;
using Client.Player.Interactions;
using Gameplay.CompositeWeapons;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Photon.Pun;
using ResourceAssets;
using UnityEngine;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// The CUSTOM loadout of each ship, and which one (if any) the room has chosen.
    ///
    /// Each ship gets a ShipLoadoutDataDef of its own that only this plugin knows about. The loadout terminal lists and
    /// shows it like any other loadout. When the host picks it, the game (and every other player) is told the ship's
    /// first loadout was picked instead, so players without the mod see nothing unusual; the host then swaps in the
    /// custom ship when the mission starts. Players with the mod learn about the custom choice from a room property.
    /// </summary>
    internal static class CustomLoadouts
    {
        private sealed class Entry
        {
            public HullInfo Hull;
            public GUIDUnion Guid;
            public ShipLoadoutDataDef Def;
            public ShipLoadoutData Data;
        }

        public const string Header = "CUSTOM";

        private static readonly Dictionary<GUIDUnion, Entry> ByShip = new Dictionary<GUIDUnion, Entry>();
        private static readonly Dictionary<GUIDUnion, Entry> ByGuid = new Dictionary<GUIDUnion, Entry>();
        private static readonly FieldInfo HeaderTextField = AccessTools.Field(typeof(ContextInfo), "headerTextDefaultable");
        private static readonly FieldInfo BodyTextField = AccessTools.Field(typeof(ContextInfo), "bodyTextDefaultable");

        /// <summary>True once every patch the CUSTOM loadout depends on is in place.</summary>
        public static bool Available;

        /// <summary>The ship whose CUSTOM loadout the room has chosen, or empty.</summary>
        public static GUIDUnion ActiveShip = GUIDUnion.Empty();

        /// <summary>For players other than the host: the host's CUSTOM loadout, read from the room.</summary>
        public static HullConfig RemoteConfig;

        /// <summary>The ship whose CUSTOM loadout the last mission used, so it is chosen again back in the hub.</summary>
        public static GUIDUnion ResumeShip = GUIDUnion.Empty();

        public static bool IsActive
        {
            get { return !ActiveShip.IsNull; }
        }

        // ---------------------------------------------------------------- the CUSTOM loadout definitions

        public static bool IsCustom(ShipLoadoutDataDef def)
        {
            return def != null && ByGuid.ContainsKey(def.AssetGuid);
        }

        public static bool TryGetHullOfCustomGuid(GUIDUnion guid, out HullInfo hull)
        {
            Entry entry;
            hull = ByGuid.TryGetValue(guid, out entry) ? entry.Hull : null;
            return hull != null;
        }

        public static bool TryGetHull(ShipLoadoutDataDef customDef, out HullInfo hull)
        {
            hull = null;
            return customDef != null && TryGetHullOfCustomGuid(customDef.AssetGuid, out hull);
        }

        /// <summary>The CUSTOM loadout of a ship, created the first time it is asked for.</summary>
        public static bool TryGetDef(GUIDUnion shipGuid, out ShipLoadoutDataDef def)
        {
            def = null;
            if (!Available || shipGuid.IsNull) return false;
            Entry entry;
            if (!ByShip.TryGetValue(shipGuid, out entry))
            {
                HullInfo hull;
                if (!Catalog.TryGetHull(shipGuid, out hull)) return false;
                try
                {
                    entry = Create(hull);
                }
                catch (Exception e)
                {
                    Log.Error("Could not create the CUSTOM loadout for the " + hull.Name + ": " + e);
                    return false;
                }
                ByShip[shipGuid] = entry;
                ByGuid[entry.Guid] = entry;
            }
            def = entry.Def;
            return true;
        }

        private static Entry Create(HullInfo hull)
        {
            GUIDUnion guid = CustomGuidFor(hull.ShipGuid);

            var data = ScriptableObject.CreateInstance<ShipLoadoutData>();
            data.name = "ShipCustomizer " + hull.Name;
            data.hideFlags = HideFlags.DontUnloadUnusedAsset;
            data.ContainerGuid = guid;
            data.ShipLoadout = Build(hull, ConfigToShow(hull));

            // The game's own API for assets added at runtime; ShipLoadoutDataDef.Asset finds the data through it.
            // Effect "None" keeps it off the list of assets a joining player must also have.
            if (!RuntimeAssetsRegister.Instance.HasAsset(guid))
            {
                RuntimeAssetsRegister.Instance.RegisterAsset(guid, data, SessionModificationEffect.None);
            }

            var def = new ShipLoadoutDataDef(guid, "");
            def.Ref.IsRuntime = true;
            def.LoadoutContextInfo = MakeContext(hull);

            Log.Info("Added a CUSTOM loadout for the " + hull.Name + " (shown to other players as " + LoadoutStore.PresetName(hull.BasePreset) + ").");
            return new Entry { Hull = hull, Guid = guid, Def = def, Data = data };
        }

        /// <summary>A GUID per ship that no game asset uses, the same for every player with the mod.</summary>
        private static GUIDUnion CustomGuidFor(GUIDUnion ship)
        {
            return new GUIDUnion(ship.m_Value0 ^ 0x50494853, ship.m_Value1 ^ 0x54535543, ship.m_Value2 ^ 0x5A494D4F, ship.m_Value3 ^ 0x21215245);
        }

        private static ShipLoadoutContextInfo MakeContext(HullInfo hull)
        {
            ShipLoadoutContextInfo context = UnityEngine.Object.Instantiate(hull.BasePreset.LoadoutContextInfo);
            context.name = "ShipCustomizer CUSTOM " + hull.Name;
            context.hideFlags = HideFlags.DontUnloadUnusedAsset;
            string body = "<b>Custom Loadout</b>\n<i>Click a hardpoint on the ship, or use HARDPOINTS, to fit any module or weapon.</i>\n" +
                          "Missions flown with it are marked modded and earn no XP or rewards.";
            if (HeaderTextField != null) HeaderTextField.SetValue(context, new DefaultableLocalizedString(Header, null));
            if (BodyTextField != null) BodyTextField.SetValue(context, new DefaultableLocalizedString(body, null));
            return context;
        }

        /// <summary>The loadout to show for a ship: the host's while the host has chosen it, otherwise the player's own.</summary>
        public static HullConfig ConfigToShow(HullInfo hull)
        {
            if (!PhotonNetwork.IsMasterClient && RemoteConfig != null && ActiveShip == hull.ShipGuid) return RemoteConfig;
            return LoadoutStore.Get(hull);
        }

        /// <summary>Whether this player may change what the terminal shows for this ship.</summary>
        public static bool CanEdit(HullInfo hull)
        {
            return PhotonNetwork.IsMasterClient || RemoteConfig == null || ActiveShip != hull.ShipGuid;
        }

        /// <summary>Rebuilds what the terminal shows for a ship after its loadout changed.</summary>
        public static void Refresh(HullInfo hull)
        {
            Entry entry;
            if (!ByShip.TryGetValue(hull.ShipGuid, out entry)) return;
            try
            {
                entry.Data.ShipLoadout = Build(hull, ConfigToShow(hull));
            }
            catch (Exception e)
            {
                Log.Failure("Rebuilding the CUSTOM loadout of the " + hull.Name, e);
            }
        }

        public static void RefreshAll()
        {
            foreach (Entry entry in ByShip.Values) Refresh(entry.Hull);
        }

        // ---------------------------------------------------------------- building the ship

        /// <summary>
        /// A ShipLoadout for the game to spawn: a copy of the ship's first loadout (core systems, extra items) with the
        /// hardpoints, storage racks and homunculus replaced by the Custom choices. Anything the game could not spawn is
        /// left out with a warning, so a bad entry can never stop a mission from starting.
        /// </summary>
        public static ShipLoadout Build(HullInfo hull, HullConfig config)
        {
            ShipLoadout loadout = ShipLoadout.FromJObject(hull.BasePreset.Asset.ShipLoadout.AsJObject());

            loadout.Sockets = new List<ShipSocketEntry>();
            var filled = new HashSet<int>();
            foreach (KeyValuePair<int, string> slot in config.Slots)
            {
                if (slot.Key < 0 || slot.Key >= hull.Sockets.Count) continue;
                if (hull.Sockets[slot.Key].Overlaps.Any(filled.Contains))
                {
                    Log.WarnOnce("overlap " + hull.ShipGuid + slot.Key, "Hardpoint position " + slot.Key + " of the " + hull.Name + " overlaps a module that is already fitted, so it is left empty.");
                    continue;
                }
                Option option;
                if (!Catalog.TryGetOption(slot.Value, out option) || (option.Kind != OptionKind.Module && option.Kind != OptionKind.Weapon) || !Catalog.CanSpawn(option.PrefabGuid))
                {
                    Log.WarnOnce("unknown " + slot.Value, "The CUSTOM " + hull.Name + " has a module the game no longer has (" + slot.Value + "); that hardpoint is left empty.");
                    continue;
                }
                ShipSocketEntry entry = SocketEntryFor(option, slot.Key);
                if (entry == null) continue;
                loadout.Sockets.Add(entry);
                filled.Add(slot.Key);
            }

            foreach (ShelfInfo shelf in hull.Shelves)
            {
                List<string> codes;
                config.Storage.TryGetValue(shelf.CoreIndex, out codes);
                ShipSocketEntry rack = loadout.CoreSystemSockets.FirstOrDefault(c => c != null && c.SocketId == shelf.CoreIndex);
                if (rack == null)
                {
                    if (!Catalog.CanSpawn(shelf.ModuleGuid)) continue;
                    rack = new ShipSocketEntry
                    {
                        SocketId = shelf.CoreIndex,
                        Module = new GameObjectRef(new ResourceAssetRef(shelf.ModuleGuid)),
                        BuildBox = new CarryableRef(),
                        Upgrades = new ShipAssetRef[0]
                    };
                    loadout.CoreSystemSockets.Add(rack);
                }
                var items = new ShipAssetRef[shelf.Slots.Count];
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = ItemFor(codes != null && i < codes.Count ? codes[i] : "", hull);
                }
                rack.Upgrades = items;
            }

            loadout.HomunculusOverride = new CarryableRef();
            Option homunculus;
            if (!string.IsNullOrEmpty(config.Homunculus) && Catalog.TryGetOption(config.Homunculus, out homunculus) && Catalog.CanSpawn(homunculus.Guid))
            {
                loadout.HomunculusOverride = new CarryableRef(homunculus.Guid);
            }
            return loadout;
        }

        private static ShipSocketEntry SocketEntryFor(Option option, int socketIndex)
        {
            ShipSocketEntry entry = null;
            JObject template;
            if (Settings.CopyPresetModuleContents.Value && Catalog.TryGetPresetContents(option.Guid, out template))
            {
                try
                {
                    entry = template.ToObject<ShipSocketEntry>();
                }
                catch (Exception e)
                {
                    Log.Verbose("Could not copy the contents of " + option.Name + " from the game's loadouts: " + e.Message);
                }
            }
            if (entry == null) entry = new ShipSocketEntry();
            entry.SocketId = socketIndex;
            entry.Module = new GameObjectRef(new ResourceAssetRef(option.PrefabGuid));
            entry.BuildBox = new CarryableRef();
            if (entry.Upgrades == null) entry.Upgrades = new ShipAssetRef[0];
            entry.Upgrades = entry.Upgrades.Select(u => u != null && !u.IsNull && Catalog.CanSpawn(u.AssetGuid) ? u : new ShipAssetRef()).ToArray();
            if (option.Kind == OptionKind.Weapon)
            {
                entry.ExtraData = WeaponData(option.Guid);
            }
            return entry;
        }

        private static ShipAssetRef ItemFor(string code, HullInfo hull)
        {
            if (string.IsNullOrEmpty(code)) return new ShipAssetRef();
            Option option;
            if (!Catalog.TryGetOption(code, out option))
            {
                Log.WarnOnce("unknown " + code, "The CUSTOM " + hull.Name + " has a storage item the game no longer has (" + code + "); that rack slot is left empty.");
                return new ShipAssetRef();
            }
            if (option.Kind == OptionKind.WeaponCrate)
            {
                if (!Catalog.CanSpawn(option.PrefabGuid)) return new ShipAssetRef();
                var crate = new ShipAssetRef(new ResourceAssetRef(option.PrefabGuid));
                crate.ExtraData = WeaponData(option.Guid);
                return crate;
            }
            if (option.Kind != OptionKind.Item || !Catalog.CanSpawn(option.Guid)) return new ShipAssetRef();
            return new ShipAssetRef(new ResourceAssetRef(option.Guid));
        }

        /// <summary>The extra data the game gives a weapon module or weapon crate to say which weapon it is.</summary>
        private static JObject WeaponData(GUIDUnion weapon)
        {
            CompositeWeaponDataDef def = ResourceAssetContainer<CompositeWeaponDataContainer, CompositeWeaponData, CompositeWeaponDataDef>.Instance.GetAssetDefById(weapon);
            return JObject.FromObject(new CompositeWeaponDataRef(def.Ref));
        }

        // ---------------------------------------------------------------- the room's choice

        /// <summary>
        /// If the room's chosen loadout is the stand-in for an active CUSTOM loadout, the CUSTOM loadout to show instead.
        /// Called from HubShipManager.CurrentShipSelected, so it must be cheap and never throw.
        /// </summary>
        public static bool TryMapToCustom(ShipLoadoutDataDef selected, out ShipLoadoutDataDef custom)
        {
            custom = null;
            if (selected == null || !IsActive || IsCustom(selected)) return false;
            Entry entry;
            if (!ByShip.TryGetValue(ActiveShip, out entry) || entry.Hull.BasePreset == null) return false;
            if (selected.AssetGuid != entry.Hull.BasePreset.AssetGuid) return false;
            custom = entry.Def;
            return true;
        }

        /// <summary>Host only: the room's loadout is now this ship's CUSTOM loadout.</summary>
        public static void ActivateAsHost(HullInfo hull)
        {
            bool changed = ActiveShip != hull.ShipGuid;
            ActiveShip = hull.ShipGuid;
            RemoteConfig = null;
            Refresh(hull);
            RoomSync.PublishCustom(hull, LoadoutStore.Get(hull));
            // The game's own flag for sessions changed by a mod: the room shows as [mod_session] in the server list.
            ModdingUtils.RegisterSessionMod();
            if (changed) Log.Info("The room's loadout is now your CUSTOM " + hull.Name + " (" + Describe(LoadoutStore.Get(hull)) + ").");
        }

        /// <summary>Host only: the room chose one of the game's loadouts (or none).</summary>
        public static void DeactivateAsHost()
        {
            if (!IsActive) return;
            HullInfo hull;
            string name = Catalog.TryGetHull(ActiveShip, out hull) ? hull.Name : "ship";
            ActiveShip = GUIDUnion.Empty();
            RoomSync.ClearCustom();
            Log.Info("The room's loadout is no longer the CUSTOM " + name + ".");
        }

        /// <summary>Host only: the host changed the active CUSTOM loadout, so tell the other players.</summary>
        public static void OnEdited(HullInfo hull)
        {
            Refresh(hull);
            if (PhotonNetwork.IsMasterClient && ActiveShip == hull.ShipGuid) RoomSync.PublishCustom(hull, LoadoutStore.Get(hull));
        }

        /// <summary>
        /// Called just before the game leaves the hub for a mission. On the host, swaps in the CUSTOM ship when the room
        /// chose one, and decides whether this run pauses progression.
        /// </summary>
        public static void OnMissionStarting(GameSession session, PreservedGameSession preserved)
        {
            if (!PhotonNetwork.IsMasterClient || session == null) return;

            bool custom = false;
            if (preserved != null)
            {
                // Continuing a saved run: it is a CUSTOM run if it was started as one.
                custom = LoadoutStore.WasCustomRun(preserved.GameSessionID);
                if (custom) Log.Info("Continuing a saved run that was started with a CUSTOM loadout; progression stays paused.");
            }
            else if (IsActive && session.ToLoadShipData != null)
            {
                HullInfo hull;
                if (Catalog.TryGetHull(ActiveShip, out hull) && hull.BasePreset != null && session.ToLoadShipGuid == hull.BasePreset.AssetGuid)
                {
                    HullConfig config = LoadoutStore.Get(hull);
                    session.ToLoadShipData = Build(hull, config);
                    custom = true;
                    Log.Info("Starting the mission with your CUSTOM " + hull.Name + ": " + Describe(config) + ".");
                }
            }

            ResumeShip = custom && preserved == null ? ActiveShip : GUIDUnion.Empty();
            ProgressionGuard.SetHostRun(custom);
            if (custom) ModdingUtils.RegisterSessionMod();
        }

        /// <summary>Back in the hub after a CUSTOM mission: choose that CUSTOM loadout again, as the game does for its own.</summary>
        public static void OnHubLoaded(HubShipManager manager)
        {
            if (!PhotonNetwork.IsMasterClient || ResumeShip.IsNull || manager == null) return;
            GUIDUnion ship = ResumeShip;
            ResumeShip = GUIDUnion.Empty();
            HullInfo hull;
            ShipLoadoutDataDef def;
            if (!Catalog.TryGetHull(ship, out hull) || manager.SelectedShipGuid != hull.BasePreset.AssetGuid || !TryGetDef(ship, out def)) return;
            manager.SelectShip(def.AssetGuid.AsIntArray());
        }

        /// <summary>The room changed (joined, left or a new one): forget the old room's choices.</summary>
        public static void OnRoomChanged()
        {
            ActiveShip = GUIDUnion.Empty();
            RemoteConfig = null;
            ResumeShip = GUIDUnion.Empty();
            RefreshAll();
        }

        public static string Describe(HullConfig config)
        {
            var names = new List<string>();
            foreach (string code in config.Slots.Values)
            {
                Option option;
                names.Add(Catalog.TryGetOption(code, out option) ? option.Name : code);
            }
            return (names.Count == 0 ? "no modules" : string.Join(", ", names.ToArray())) + "; " + config.StoredCount + " items in storage";
        }
    }
}
