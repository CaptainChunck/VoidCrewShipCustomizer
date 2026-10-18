using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CG.Game;
using CG.Ship.Modules;
using CG.Space;
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

        /// <summary>Which ship the loadout terminal is setting up: 1, or 2 while another plugin (Void Crew Fleet) says so.</summary>
        public static int TerminalShip = 1;

        /// <summary>The ship whose CUSTOM loadout the room's ship 2 uses (Void Crew Fleet), or empty.</summary>
        public static GUIDUnion ActiveShip2 = GUIDUnion.Empty();

        /// <summary>For players other than the host: the host's ship 2 CUSTOM loadout, read from the room.</summary>
        public static HullConfig RemoteConfig2;

        /// <summary>Host: this mission's ship 2 started with a CUSTOM loadout.</summary>
        private static bool _ship2CustomRun;

        /// <summary>Host: the mission now starting continues a saved run.</summary>
        private static bool _missionIsSavedRun;

        /// <summary>Host: the fabricator level ship 1 and ship 2 still have to start at (0: nothing to do).</summary>
        private static readonly int[] PendingFabricatorLevels = new int[2];
        private static float _shipSeenAt = -1f;

        /// <summary>
        /// For players other than a host that shares them (protocol 2): the host's CUSTOM loadouts by ship type, for
        /// ship 1, and ship 2's own where it has one (otherwise ship 2 follows ship 1's).
        /// </summary>
        private static readonly Dictionary<GUIDUnion, HullConfig> HostLayouts = new Dictionary<GUIDUnion, HullConfig>();
        private static readonly Dictionary<GUIDUnion, HullConfig> HostLayouts2 = new Dictionary<GUIDUnion, HullConfig>();
        private static readonly Dictionary<int, float> LastChangeToast = new Dictionary<int, float>();

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

        /// <summary>
        /// The CUSTOM definition behind any CUSTOM GUID, created if needed: a request from another player, or a GUID read
        /// from the room, can arrive before this player has opened that ship's list.
        /// </summary>
        public static bool TryGetDefOfCustomGuid(GUIDUnion guid, out ShipLoadoutDataDef def)
        {
            def = null;
            Entry entry;
            if (ByGuid.TryGetValue(guid, out entry))
            {
                def = entry.Def;
                return true;
            }
            if (!Available || guid.IsNull) return false;
            // The XOR undoes itself. Checking the ship list first keeps a random GUID from reading as a broken ship.
            GUIDUnion ship = CustomGuidFor(guid);
            if (!Catalog.ShipGuids().Contains(ship)) return false;
            return TryGetDef(ship, out def) && def.AssetGuid == guid;
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

        /// <summary>Whether a GUID is some ship's CUSTOM id, whether or not this player can use CUSTOM.</summary>
        public static bool LooksCustom(GUIDUnion guid)
        {
            if (guid.IsNull) return false;
            try
            {
                return Catalog.ShipGuids().Contains(CustomGuidFor(guid));
            }
            catch
            {
                return false;
            }
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

        /// <summary>
        /// The loadout to show for a ship, for the ship the terminal is setting up. The host shows its own. Other players
        /// show the host's when it shares them (0.5.0 on); with an older host, the host's while the room uses it,
        /// otherwise their own.
        /// </summary>
        public static HullConfig ConfigToShow(HullInfo hull)
        {
            if (!PhotonNetwork.IsMasterClient)
            {
                HullConfig shared = HostLayout(hull, TerminalShip);
                if (shared != null) return shared;
                HullConfig remote = TerminalShip == 2 ? RemoteConfig2 : RemoteConfig;
                if (remote != null && RoomUses(hull)) return remote;
            }
            return LoadoutStore.Get(hull, TerminalShip);
        }

        /// <summary>Whether this player may change what the terminal shows for this ship.</summary>
        public static bool CanEdit(HullInfo hull)
        {
            return PhotonNetwork.IsMasterClient || EditsGoToHost(hull) || (TerminalShip == 2 ? RemoteConfig2 : RemoteConfig) == null || !RoomUses(hull);
        }

        /// <summary>A player other than the host whose changes go to the host's CUSTOM loadout (the host shares it).</summary>
        public static bool EditsGoToHost(HullInfo hull)
        {
            return !PhotonNetwork.IsMasterClient && HostLayouts.ContainsKey(hull.ShipGuid) && RoomSync.HostSharesLayouts();
        }

        /// <summary>The host's shared CUSTOM loadout for a ship (ship 2 follows ship 1 unless it has its own), or null.</summary>
        public static HullConfig HostLayout(HullInfo hull, int ship)
        {
            HullConfig config;
            if (ship == 2 && HostLayouts2.TryGetValue(hull.ShipGuid, out config)) return config;
            return HostLayouts.TryGetValue(hull.ShipGuid, out config) ? config : null;
        }

        /// <summary>Ship 1's CUSTOM loadout as the terminal works with it: the host's when shared, otherwise this player's.</summary>
        public static HullConfig ShipOneConfig(HullInfo hull)
        {
            return EditsGoToHost(hull) ? HostLayout(hull, 1) : LoadoutStore.Get(hull);
        }

        /// <summary>Whether ship 2 uses ship 1's CUSTOM loadout (rather than one of its own) for this ship type.</summary>
        public static bool Ship2FollowsShip1(HullInfo hull)
        {
            return EditsGoToHost(hull) ? !HostLayouts2.ContainsKey(hull.ShipGuid) : !LoadoutStore.HasOwnShip2(hull);
        }

        /// <summary>Other players: the host's shared loadout as read from the room (null: ship 2 follows ship 1 / none shared).</summary>
        public static void SetHostLayout(int ship, GUIDUnion hullGuid, HullConfig config)
        {
            Dictionary<GUIDUnion, HullConfig> layouts = ship == 2 ? HostLayouts2 : HostLayouts;
            if (config == null) layouts.Remove(hullGuid);
            else layouts[hullGuid] = Normalize(config);
            HullInfo hull;
            if (ByShip.ContainsKey(hullGuid) && Catalog.TryGetHull(hullGuid, out hull)) Refresh(hull);
        }

        /// <summary>The host changed (or this player became the host): the old host's shared loadouts no longer apply.</summary>
        public static void ForgetHostLayouts()
        {
            HostLayouts.Clear();
            HostLayouts2.Clear();
            RefreshAll();
        }

        /// <summary>
        /// A player other than the host changed the host's CUSTOM loadout: show it straight away and send it to the host,
        /// which saves it and shares it with everyone.
        /// </summary>
        public static bool SendEditToHost(HullInfo hull, int ship, HullConfig config, bool followShip1)
        {
            if (!RoomSync.SendEdit(hull, ship, config, followShip1)) return false;
            if (followShip1) HostLayouts2.Remove(hull.ShipGuid);
            else (ship == 2 ? HostLayouts2 : HostLayouts)[hull.ShipGuid] = config;
            Refresh(hull);
            return true;
        }

        /// <summary>
        /// Host: another player changed one of its CUSTOM loadouts. The change is limited to what the ship has, saved on
        /// this PC like the host's own changes, and shared with everyone.
        /// </summary>
        public static void ApplyEditAsHost(string who, int actor, RoomSync.RoomEdit edit)
        {
            if (edit == null || string.IsNullOrEmpty(edit.Hull) || (edit.Ship != 1 && edit.Ship != 2)) return;
            var hullGuid = new GUIDUnion(edit.Hull);
            HullInfo hull;
            // Only the game's ships: a made-up GUID must not make the catalog try to read a ship that isn't there.
            if (!Catalog.ShipGuids().Contains(hullGuid) || !Catalog.TryGetHull(hullGuid, out hull)) return;
            if (edit.FollowShip1)
            {
                if (edit.Ship != 2) return;
                LoadoutStore.ForgetShip2(hull);
            }
            else
            {
                if (edit.Config == null) return;
                LoadoutStore.Set(hull, Sanitize(hull, edit.Config), edit.Ship);
            }
            OnEdited(hull, actor, edit.N);
            TerminalUi.OnLoadoutChangedRemotely();
            string what = (edit.Ship == 2 ? "ship 2's " : "") + "CUSTOM " + hull.Name;
            Log.Info(who + " changed " + what + ".");
            float last;
            if (!LastChangeToast.TryGetValue(actor, out last) || Time.unscaledTime - last > 8f)
            {
                LastChangeToast[actor] = Time.unscaledTime;
                TerminalUi.ShowToast(who + " changed " + what + ".");
            }
        }

        /// <summary>A loadout from another player, limited to this ship's hardpoints and racks (it is saved on this PC).</summary>
        public static HullConfig Sanitize(HullInfo hull, HullConfig config)
        {
            var clean = new HullConfig
            {
                Homunculus = Code(config.Homunculus),
                CopiedFrom = config.CopiedFrom != null && config.CopiedFrom.Length <= 100 ? config.CopiedFrom : "",
                Alloys = config.Alloys,
                FabricatorLevel = config.FabricatorLevel
            };
            clean.Normalized();
            if (config.Slots != null)
            {
                foreach (KeyValuePair<int, string> slot in config.Slots)
                {
                    if (slot.Key >= 0 && slot.Key < hull.Sockets.Count && !string.IsNullOrEmpty(Code(slot.Value))) clean.Slots[slot.Key] = slot.Value;
                }
            }
            if (config.Storage != null)
            {
                foreach (ShelfInfo shelf in hull.Shelves)
                {
                    List<string> codes;
                    if (config.Storage.TryGetValue(shelf.CoreIndex, out codes) && codes != null) clean.Storage[shelf.CoreIndex] = codes.Take(shelf.Slots.Count).Select(Code).ToList();
                }
            }
            return clean;
        }

        /// <summary>An item or module code as saved ("m:", "w:", "i:" or "c:" and a GUID); anything else becomes empty.</summary>
        private static string Code(string code)
        {
            return code != null && code.Length <= 80 ? code : "";
        }

        private static HullConfig Normalize(HullConfig config)
        {
            return config.Normalized();
        }

        /// <summary>Whether the room uses this ship's CUSTOM loadout for the ship the terminal is setting up.</summary>
        public static bool RoomUses(HullInfo hull)
        {
            return (TerminalShip == 2 ? ActiveShip2 : ActiveShip) == hull.ShipGuid;
        }

        /// <summary>Another plugin switched the ship the terminal sets up; every CUSTOM definition now shows that ship's layout.</summary>
        public static void SetTerminalShip(int ship)
        {
            if (TerminalShip == ship) return;
            TerminalShip = ship;
            RefreshAll();
            TerminalUi.OnTerminalShipChanged();
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

        /// <summary>
        /// A CUSTOM loadout was changed and saved on this PC. On the host, tell the other players: the room's chosen
        /// loadouts, and the shared loadouts (<paramref name="by"/> and <paramref name="n"/> name the player's change, 0 for the host's own).
        /// </summary>
        public static void OnEdited(HullInfo hull, int by = 0, int n = 0)
        {
            Refresh(hull);
            if (!PhotonNetwork.IsMasterClient) return;
            // Both: ship 2's layout follows ship 1's until it has its own.
            if (ActiveShip == hull.ShipGuid) RoomSync.PublishCustom(hull, LoadoutStore.Get(hull));
            if (ActiveShip2 == hull.ShipGuid) RoomSync.PublishCustom2(hull, LoadoutStore.Get(hull, 2));
            RoomSync.PublishLayouts(hull, by, n);
        }

        // ---------------------------------------------------------------- ship 2 (Void Crew Fleet)

        /// <summary>Host: the room's ship 2 now uses this ship's CUSTOM loadout.</summary>
        public static void ActivateShip2AsHost(HullInfo hull)
        {
            bool changed = ActiveShip2 != hull.ShipGuid;
            ActiveShip2 = hull.ShipGuid;
            RemoteConfig2 = null;
            Refresh(hull);
            RoomSync.PublishCustom2(hull, LoadoutStore.Get(hull, 2));
            ModdingUtils.RegisterSessionMod();
            if (changed) Log.Info("Ship 2's loadout is now your ship 2 CUSTOM " + hull.Name + " (" + Describe(LoadoutStore.Get(hull, 2)) + ").");
        }

        /// <summary>Host: the room's ship 2 uses a game loadout (or none).</summary>
        public static void DeactivateShip2AsHost()
        {
            if (ActiveShip2.IsNull) return;
            HullInfo hull;
            string name = Catalog.TryGetHull(ActiveShip2, out hull) ? hull.Name : "ship";
            ActiveShip2 = GUIDUnion.Empty();
            RoomSync.ClearCustom2();
            Log.Info("Ship 2's loadout is no longer the CUSTOM " + name + ".");
        }

        /// <summary>
        /// Host, at mission start: ship 2's CUSTOM loadout built fresh, or null when CUSTOM can't be used (the host is told
        /// why, and the caller uses the stand-in). The same rules as ship 1; a CUSTOM ship 2 makes the run a CUSTOM run.
        /// </summary>
        public static ShipLoadout BuildShip2ForMission(GUIDUnion guid)
        {
            ShipLoadoutDataDef def;
            HullInfo hull;
            if (!PhotonNetwork.IsMasterClient || !TryGetDefOfCustomGuid(guid, out def) || !TryGetHullOfCustomGuid(guid, out hull)) return null;
            string instead = "Ship 2 starts with " + LoadoutStore.PresetName(hull.BasePreset) + " instead of CUSTOM: ";
            List<string> missing = RoomSync.PlayersWithoutMod(false);
            if (missing.Count > 0)
            {
                Notice.Show(instead + string.Join(", ", missing.ToArray()) + " doesn't have Ship Customizer, and nobody may earn progression in a CUSTOM run.");
                return null;
            }
            if (!EnsureRoomMarkedModded())
            {
                Notice.Show(instead + "the room is not marked [mod_session] yet.");
                return null;
            }
            HullConfig config = LoadoutStore.Get(hull, 2);
            ShipLoadout loadout = Build(hull, config);
            // Continuing a saved run, the game puts the fabricators back as they were.
            if (!_missionIsSavedRun) PendingFabricatorLevels[1] = config.FabricatorLevel;
            _ship2CustomRun = true;
            // Only ever raises the pause; ship 1's check at mission start keeps it raised (see OnMissionStarting).
            ProgressionGuard.SetHostRun(true);
            ModdingUtils.RegisterSessionMod();
            RoomSync.CloseForCustomMission();
            Log.Info("Ship 2 starts with your ship 2 CUSTOM " + hull.Name + ": " + Describe(config) + ".");
            return loadout;
        }

        /// <summary>
        /// Called just before the game leaves the hub for a mission. On the host, swaps in the CUSTOM ship when the room
        /// chose one, and decides whether this run pauses progression.
        /// </summary>
        public static void OnMissionStarting(GameSession session, PreservedGameSession preserved)
        {
            if (!PhotonNetwork.IsMasterClient || session == null) return;
            _missionIsSavedRun = preserved != null;

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
                    List<string> missing = RoomSync.PlayersWithoutMod(false);
                    if (missing.Count > 0)
                    {
                        Notice.Show("This mission starts with " + LoadoutStore.PresetName(hull.BasePreset) + " instead of CUSTOM: " + string.Join(", ", missing.ToArray()) +
                                    " doesn't have Ship Customizer, and nobody may earn progression in a CUSTOM run.");
                    }
                    else if (!EnsureRoomMarkedModded())
                    {
                        Notice.Show("This mission starts with " + LoadoutStore.PresetName(hull.BasePreset) + " instead of CUSTOM: the room is not marked [mod_session] yet.");
                    }
                    else
                    {
                        HullConfig config = LoadoutStore.Get(hull);
                        session.ToLoadShipData = Build(hull, config);
                        // The game's own starting amount for the run (-1 means its usual bonus from the players' ranks).
                        if (config.Alloys >= 0) session.InitialAlloys = config.Alloys;
                        PendingFabricatorLevels[0] = config.FabricatorLevel;
                        custom = true;
                        Log.Info("Starting the mission with your CUSTOM " + hull.Name + ": " + Describe(config) + ".");
                    }
                }
            }

            if (custom && preserved != null)
            {
                List<string> missing = RoomSync.PlayersWithoutMod(false);
                if (missing.Count > 0) Log.Warn("Continuing a saved CUSTOM run while " + string.Join(", ", missing.ToArray()) + " doesn't have Ship Customizer; their progression can't be paused.");
            }

            bool shipOneCustom = custom;
            // Ship 2 (Void Crew Fleet) is CUSTOM too, whichever of the two mission-start hooks ran first.
            if (_ship2CustomRun && preserved == null) custom = true;
            ResumeShip = shipOneCustom && preserved == null ? ActiveShip : GUIDUnion.Empty();
            ProgressionGuard.SetHostRun(custom);
            if (custom)
            {
                ModdingUtils.RegisterSessionMod();
                RoomSync.CloseForCustomMission();
            }
        }

        /// <summary>
        /// The modding guidelines: a [mod_session] mod may only change the session while the room is marked
        /// [mod_session]. The plugin marks rooms at start-up; this re-checks (and marks) just before it is needed.
        /// </summary>
        public static bool EnsureRoomMarkedModded()
        {
            if (ModdingUtils.IsCurrentSessionModded()) return true;
            ModdingUtils.RegisterSessionMod();
            return ModdingUtils.IsCurrentSessionModded();
        }

        // ---------------------------------------------------------------- the fabricators' starting level

        /// <summary>
        /// Host, early in a fresh CUSTOM mission: each player ship's fabricator starts at its CUSTOM level, once the ship
        /// is in the world, the way the game itself puts a saved run's fabricator back. Ship 1 is the game's player
        /// ship; ship 2 (Void Crew Fleet) is the other one. Called a few times a second; gives up after a minute.
        /// </summary>
        public static void ApplyFabricatorLevels()
        {
            if (PendingFabricatorLevels[0] == 0 && PendingFabricatorLevels[1] == 0) return;
            if (!PhotonNetwork.IsMasterClient || GameSessionManager.InHub || ClientGame.Current == null) return;
            PlayerControlledShip shipOne = ClientGame.Current.PlayerShip;
            if (shipOne == null) return;
            if (_shipSeenAt < 0f) _shipSeenAt = Time.unscaledTime;
            float waited = Time.unscaledTime - _shipSeenAt;
            // A moment for the ship's own start-up (its fabricator terminal and upgrade visuals listen for the level).
            if (waited < 1f) return;

            if (PendingFabricatorLevels[0] > 0 && SetFabricatorLevel(shipOne, PendingFabricatorLevels[0], "The ship")) PendingFabricatorLevels[0] = 0;
            if (PendingFabricatorLevels[1] > 0)
            {
                foreach (PlayerControlledShip ship in UnityEngine.Object.FindObjectsOfType<PlayerControlledShip>())
                {
                    if (ship == null || ship == shipOne) continue;
                    if (SetFabricatorLevel(ship, PendingFabricatorLevels[1], "Ship 2")) PendingFabricatorLevels[1] = 0;
                    break;
                }
            }
            if (waited > 60f && (PendingFabricatorLevels[0] > 0 || PendingFabricatorLevels[1] > 0))
            {
                Log.Warn("Could not set the fabricator's CUSTOM level, so it starts at MK I.");
                PendingFabricatorLevels[0] = PendingFabricatorLevels[1] = 0;
            }
        }

        private static bool SetFabricatorLevel(PlayerControlledShip ship, int level, string which)
        {
            try
            {
                FabricatorModule fabricator = ship.GetModule<FabricatorModule>();
                if (fabricator == null || fabricator.ResolvedFabricatorTiers == null || fabricator.ResolvedFabricatorTiers.Count == 0) return false;
                // Some missions have fewer levels than MK III; the fabricator then starts at the highest they have.
                int tier = Math.Max(0, Math.Min(level, Math.Min(HullConfig.MaxFabricatorLevel, fabricator.ResolvedFabricatorTiers.Count - 1)));
                if (fabricator.CurrentTier != tier) fabricator.SetFabricatorTier(tier);
                Log.Info(which + "'s fabricator starts at " + FabricatorLevelName(tier) + (tier < level ? " (the highest this mission has)" : "") + ".");
                return true;
            }
            catch (Exception e)
            {
                Log.Verbose("The fabricator isn't ready for its CUSTOM level yet: " + e.Message);
                return false;
            }
        }

        public static string FabricatorLevelName(int level)
        {
            return level == 2 ? "MK III" : (level == 1 ? "MK II" : "MK I");
        }

        /// <summary>Back in the hub after a CUSTOM mission: choose that CUSTOM loadout again, as the game does for its own.</summary>
        public static void OnHubLoaded(HubShipManager manager)
        {
            _ship2CustomRun = false;
            PendingFabricatorLevels[0] = PendingFabricatorLevels[1] = 0;
            _shipSeenAt = -1f;
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
            ActiveShip2 = GUIDUnion.Empty();
            RemoteConfig2 = null;
            _ship2CustomRun = false;
            TerminalShip = 1;
            HostLayouts.Clear();
            HostLayouts2.Clear();
            LastChangeToast.Clear();
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
            string text = (names.Count == 0 ? "no modules" : string.Join(", ", names.ToArray())) + "; " + config.StoredCount + " items in storage";
            if (config.Alloys >= 0) text += "; " + config.Alloys + " alloys";
            if (config.FabricatorLevel > 0) text += "; fabricator " + FabricatorLevelName(config.FabricatorLevel);
            return text;
        }
    }
}
