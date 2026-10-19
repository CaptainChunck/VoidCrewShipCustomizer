using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CG;
using CG.Game;
using CG.Game.SpaceObjects.Controllers;
using CG.Network;
using CG.Objects;
using CG.Ship.Hull;
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

        /// <summary>
        /// Player ships: 1 is the game's own; 2 to 6 are Void Crew Fleet's (up to 3 in co-op, up to 3 a team in a Fleet
        /// Battle). Everything per ship below is indexed 1 to MaxShips.
        /// </summary>
        public const int MaxShips = 6;

        private static readonly Dictionary<GUIDUnion, Entry> ByShip = new Dictionary<GUIDUnion, Entry>();
        private static readonly Dictionary<GUIDUnion, Entry> ByGuid = new Dictionary<GUIDUnion, Entry>();
        private static readonly FieldInfo HeaderTextField = AccessTools.Field(typeof(ContextInfo), "headerTextDefaultable");
        private static readonly FieldInfo BodyTextField = AccessTools.Field(typeof(ContextInfo), "bodyTextDefaultable");

        /// <summary>True once every patch the CUSTOM loadout depends on is in place.</summary>
        public static bool Available;

        /// <summary>The ship type whose CUSTOM loadout each player ship uses, or empty while it uses a game loadout.</summary>
        private static readonly GUIDUnion[] ActiveShips = Enumerable.Repeat(GUIDUnion.Empty(), MaxShips + 1).ToArray();

        /// <summary>For players other than the host: the host's chosen CUSTOM loadout of each player ship, read from the room.</summary>
        private static readonly HullConfig[] RemoteConfigs = new HullConfig[MaxShips + 1];

        /// <summary>The ship whose CUSTOM loadout the last mission used, so it is chosen again back in the hub.</summary>
        public static GUIDUnion ResumeShip = GUIDUnion.Empty();

        private static int _terminalShip = 1;

        /// <summary>Host: one of Void Crew Fleet's ships started this mission with a CUSTOM loadout.</summary>
        private static bool _otherShipCustomRun;

        /// <summary>Host: the mission now starting continues a saved run.</summary>
        private static bool _missionIsSavedRun;

        /// <summary>
        /// For players other than a host that shares them (protocol 2): the host's CUSTOM loadouts by player ship and ship
        /// type. Ships 2 to 6 only have the ones they own (otherwise they follow ship 1's).
        /// </summary>
        private static readonly Dictionary<GUIDUnion, HullConfig>[] HostLayouts = PerShip();

        /// <summary>
        /// For players other than the host: the host's loadouts exactly as last read from the room (the ones shown can be
        /// ahead, with this player's own change on its way). A change is sent with what it was made against, from here.
        /// </summary>
        private static readonly Dictionary<GUIDUnion, HullConfig>[] ConfirmedLayouts = PerShip();

        private static Dictionary<GUIDUnion, HullConfig>[] PerShip()
        {
            return Enumerable.Range(0, MaxShips + 1).Select(i => new Dictionary<GUIDUnion, HullConfig>()).ToArray();
        }

        /// <summary>The ship type whose CUSTOM loadout the room's ship 1 uses, or empty.</summary>
        public static GUIDUnion ActiveShip
        {
            get { return ActiveShips[1]; }
        }

        /// <summary>The ship type whose CUSTOM loadout a player ship uses, or empty (also for a ship that doesn't exist).</summary>
        public static GUIDUnion ActiveShipOf(int ship)
        {
            return ValidShip(ship) ? ActiveShips[ship] : GUIDUnion.Empty();
        }

        public static bool ValidShip(int ship)
        {
            return ship >= 1 && ship <= MaxShips;
        }

        /// <summary>
        /// Which player ship the loadout terminal is setting up: 1, or the ship Void Crew Fleet switched it to while Fleet
        /// runs (here and on the host; otherwise it is always ship 1).
        /// </summary>
        public static int TerminalShip
        {
            get { return _terminalShip > 1 && !FleetLink.Running ? 1 : _terminalShip; }
        }

        /// <summary>For players other than the host: the room's choice for a player ship (empty and null for a game loadout).</summary>
        public static void SetRemoteChoice(int ship, GUIDUnion hullGuid, HullConfig config)
        {
            if (!ValidShip(ship)) return;
            ActiveShips[ship] = hullGuid;
            RemoteConfigs[ship] = config;
        }

        /// <summary>Who last changed each part of each loadout ("hull/ship/part"), for the messages when two players collide.</summary>
        private static readonly Dictionary<string, string> PartEditors = new Dictionary<string, string>();
        /// <summary>Who last changed each loadout ("hull/ship"), as seen by a player other than the host.</summary>
        private static readonly Dictionary<string, string> LayoutEditors = new Dictionary<string, string>();
        private static readonly Dictionary<string, float> LastChangeToast = new Dictionary<string, float>();

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
            if (HeaderTextField != null) HeaderTextField.SetValue(context, new DefaultableLocalizedString(Header, null));
            if (BodyTextField != null) BodyTextField.SetValue(context, new DefaultableLocalizedString(Description(hull, ConfigToShow(hull)), null));
            return context;
        }

        /// <summary>The CUSTOM loadout's text in the terminal's DESCRIPTION: how to change it, its power, and what the run starts with.</summary>
        private static string Description(HullInfo hull, HullConfig config)
        {
            var start = new List<string> { PowerLine(hull, config) };
            if (config.Alloys >= 0) start.Add(config.Alloys + " alloys");
            if (config.FabricatorLevel > 0) start.Add("fabricator " + FabricatorLevelName(config.FabricatorLevel));
            return "<b>Custom Loadout</b>\n<i>Click a hardpoint on the ship, or use HARDPOINTS, to fit any module or weapon.</i>\n" +
                   string.Join("  ·  ", start.ToArray()) + "\n" +
                   "Missions flown with it are marked modded and earn no XP or rewards.";
        }

        /// <summary>
        /// The power check: what everything fitted draws when it is all on, against what the ship and its generators make
        /// ("Power: 18 of 24 made"), as the game's base values (blessings, relics and upgrades can change them in a run).
        /// </summary>
        public static string PowerLine(HullInfo hull, HullConfig config)
        {
            int use = 0;
            int made = hull.BasePower;
            foreach (string code in config.Slots.Values)
            {
                Option option;
                if (!Catalog.TryGetOption(code, out option)) continue;
                use += option.PowerUse;
                made += option.PowerMade;
            }
            string line = "Power: " + use + " of " + made + " made";
            return use > made ? line + " (not everything can be on at once)" : line;
        }

        /// <summary>
        /// The loadout to show for a ship, for the ship the terminal is setting up. The host shows its own. Other players
        /// show the host's when it shares them (0.5.0 on); with an older host, the host's while the room uses it,
        /// otherwise their own.
        /// </summary>
        public static HullConfig ConfigToShow(HullInfo hull)
        {
            int ship = TerminalShip;
            if (!PhotonNetwork.IsMasterClient)
            {
                HullConfig shared = HostLayout(hull, ship);
                if (shared != null) return shared;
                HullConfig remote = RemoteConfigs[ship];
                if (remote != null && RoomUses(hull)) return remote;
            }
            return LoadoutStore.Get(hull, ship);
        }

        /// <summary>Whether this player may change what the terminal shows for this ship.</summary>
        public static bool CanEdit(HullInfo hull)
        {
            if (!TeamMayEdit() || !HostKnowsTerminalShip()) return false;
            return PhotonNetwork.IsMasterClient || EditsGoToHost(hull) || RemoteConfigs[TerminalShip] == null || !RoomUses(hull);
        }

        /// <summary>
        /// In a Void Crew Fleet battle, only a ship's own team may change it (odd ships are BLUE's, even ships RED's), the
        /// host included. Always true otherwise.
        /// </summary>
        public static bool TeamMayEdit()
        {
            Photon.Realtime.Player me = PhotonNetwork.LocalPlayer;
            return me == null || FleetLink.CanEditShip(me.ActorNumber, TerminalShip);
        }

        /// <summary>Ships 3 to 6 need a host whose Ship Customizer knows them (0.9.0 on): an older one couldn't save their changes.</summary>
        private static bool HostKnowsTerminalShip()
        {
            return TerminalShip < 3 || !PhotonNetwork.InRoom || RoomSync.HostKnowsManyShips();
        }

        /// <summary>Why this player can't change the CUSTOM loadout the terminal shows, in a few words for a message.</summary>
        public static string WhyNotEditable()
        {
            if (!TeamMayEdit())
            {
                string team = FleetLink.TeamNameOfShip(TerminalShip);
                return team != null ? "This is the " + team + " team's ship: only its players can change it." : "Only this ship's team can change it.";
            }
            if (!HostKnowsTerminalShip()) return "The host's Ship Customizer is older than 0.9.0, so ships 3 to 6 can't use CUSTOM.";
            return "This is the host's CUSTOM loadout, and the host's Ship Customizer is older than 0.5.0, so only the host can change it.";
        }

        /// <summary>A player other than the host whose changes go to the host's CUSTOM loadout (the host shares it).</summary>
        public static bool EditsGoToHost(HullInfo hull)
        {
            return !PhotonNetwork.IsMasterClient && HostLayouts[1].ContainsKey(hull.ShipGuid) && RoomSync.HostSharesLayouts();
        }

        /// <summary>
        /// The host's shared CUSTOM loadout of a ship type for a player ship, or null when it shares none. A ship 2 to 6
        /// without its own follows ship 1's, except in a Fleet Battle (see LoadoutStore.Get).
        /// </summary>
        public static HullConfig HostLayout(HullInfo hull, int ship)
        {
            HullConfig config;
            if (!ValidShip(ship)) return null;
            if (ship >= 2 && HostLayouts[ship].TryGetValue(hull.ShipGuid, out config)) return config;
            if (!HostLayouts[1].TryGetValue(hull.ShipGuid, out config)) return null;
            return ship >= 2 && FleetLink.IsTeamMatch ? LoadoutStore.Default(hull) : config;
        }

        /// <summary>A player ship's CUSTOM loadout as the terminal works with it: the host's when shared, otherwise this player's.</summary>
        public static HullConfig LayoutOf(HullInfo hull, int ship)
        {
            return EditsGoToHost(hull) ? HostLayout(hull, ship) : LoadoutStore.Get(hull, ship);
        }

        /// <summary>Whether ship 2 to 6 uses ship 1's CUSTOM loadout (rather than one of its own) for this ship type.</summary>
        public static bool FollowsShip1(HullInfo hull, int ship)
        {
            if (ship < 2 || !ValidShip(ship)) return false;
            return EditsGoToHost(hull) ? !HostLayouts[ship].ContainsKey(hull.ShipGuid) : !LoadoutStore.HasOwn(hull, ship);
        }

        /// <summary>Other players: the host's loadout exactly as the room has it now (null: none shared / the ship follows ship 1).</summary>
        public static void SetConfirmedLayout(int ship, GUIDUnion hullGuid, HullConfig config)
        {
            if (!ValidShip(ship)) return;
            if (config == null) ConfirmedLayouts[ship].Remove(hullGuid);
            else ConfirmedLayouts[ship][hullGuid] = Normalize(config.Clone());
        }

        /// <summary>The host's loadout as last read from the room, which this player's next change is measured against.</summary>
        private static HullConfig ConfirmedLayout(HullInfo hull, int ship)
        {
            HullConfig config;
            if (ship >= 2 && ConfirmedLayouts[ship].TryGetValue(hull.ShipGuid, out config)) return config;
            if (ship >= 2 && FleetLink.IsTeamMatch) return LoadoutStore.Default(hull);
            if (ConfirmedLayouts[1].TryGetValue(hull.ShipGuid, out config)) return config;
            return HostLayout(hull, ship);
        }

        public static void SetHostLayout(int ship, GUIDUnion hullGuid, HullConfig config)
        {
            if (!ValidShip(ship)) return;
            if (config == null) HostLayouts[ship].Remove(hullGuid);
            else HostLayouts[ship][hullGuid] = Normalize(config);
            HullInfo hull;
            if (ByShip.ContainsKey(hullGuid) && Catalog.TryGetHull(hullGuid, out hull)) Refresh(hull);
        }

        /// <summary>The host changed (or this player became the host): the old host's shared loadouts no longer apply.</summary>
        public static void ForgetHostLayouts()
        {
            foreach (Dictionary<GUIDUnion, HullConfig> layouts in HostLayouts) layouts.Clear();
            foreach (Dictionary<GUIDUnion, HullConfig> layouts in ConfirmedLayouts) layouts.Clear();
            LayoutEditors.Clear();
            RefreshAll();
        }

        /// <summary>
        /// A player other than the host changed the host's CUSTOM loadout: show it straight away and send it to the host,
        /// which saves it and shares it with everyone.
        /// </summary>
        public static bool SendEditToHost(HullInfo hull, int ship, HullConfig config, bool followShip1)
        {
            if (!ValidShip(ship)) return false;
            HullConfig seen = ConfirmedLayout(hull, ship);
            if (!RoomSync.SendEdit(hull, ship, config, followShip1, seen != null ? seen.Clone() : null)) return false;
            if (followShip1) HostLayouts[ship].Remove(hull.ShipGuid);
            else HostLayouts[ship][hull.ShipGuid] = config;
            Refresh(hull);
            return true;
        }

        /// <summary>
        /// Host: another player changed one of its CUSTOM loadouts. The change is limited to what the ship has, saved on
        /// this PC like the host's own changes, and shared with everyone.
        /// </summary>
        public static void ApplyEditAsHost(string who, int actor, RoomSync.RoomEdit edit)
        {
            if (edit == null || string.IsNullOrEmpty(edit.Hull) || !ValidShip(edit.Ship)) return;
            // Ships 2 to 6 only exist with Void Crew Fleet running.
            if (edit.Ship >= 2 && !FleetLink.Running) return;
            var hullGuid = new GUIDUnion(edit.Hull);
            HullInfo hull;
            // Only the game's ships: a made-up GUID must not make the catalog try to read a ship that isn't there.
            if (!Catalog.ShipGuids().Contains(hullGuid) || !Catalog.TryGetHull(hullGuid, out hull)) return;
            if (edit.FollowShip1)
            {
                if (edit.Ship < 2) return;
                LoadoutStore.Forget(hull, edit.Ship);
            }
            List<string> refused = null;
            if (!edit.FollowShip1)
            {
                if (edit.Config == null) return;
                HullConfig wanted = Sanitize(hull, edit.Config);
                HullConfig current = LoadoutStore.GetForEdit(hull, edit.Ship);
                var applied = new List<string>();
                var refusedParts = new List<string>();
                HullConfig result;
                if (edit.Base != null)
                {
                    // Only this player's own changes, part by part: what someone else changed since they last looked stays.
                    result = LayoutMerge.Merge(current, Sanitize(hull, edit.Base), wanted, hull.Sockets, RackSlots(hull), applied, refusedParts);
                }
                else
                {
                    // A player on Ship Customizer 0.7.0 or older sends the whole loadout, which replaces this one.
                    applied.AddRange(LayoutMerge.Changed(current, wanted, hull.Sockets, RackSlots(hull)));
                    result = wanted;
                }
                refused = refusedParts.Select(p => PartName(hull, p) + " (" + EditorOf(hull, edit.Ship, p) + ")").ToList();
                if (refusedParts.Count > 0) Log.Info("Kept what others changed first, instead of " + who + "'s change: " + string.Join(", ", refused.ToArray()) + ".");
                if (applied.Count == 0 && refusedParts.Count == 0 && edit.Base != null)
                {
                    // Nothing new (the same change again): only confirm it to the player.
                    OnEdited(hull, actor, edit.N, edit.Ship, refused);
                    return;
                }
                LoadoutStore.Set(hull, result, edit.Ship);
                NoteEditors(hull, edit.Ship, applied, who);
            }
            OnEdited(hull, actor, edit.N, edit.Ship, refused);
            TerminalUi.OnLoadoutChangedRemotely();
            string what = Whose(edit.Ship) + "CUSTOM " + hull.Name;
            Log.Info(who + " changed " + what + ".");
            ToastChange(who, what);
        }

        /// <summary>"" for ship 1 on its own, otherwise the ship's name and "'s " ("SHIP 2's ", "BLUE 1's "), for messages.</summary>
        public static string Whose(int ship)
        {
            return ship < 2 && !FleetLink.IsTeamMatch ? "" : FleetLink.ShipName(ship) + "'s ";
        }

        /// <summary>
        /// Host: a player's change that can't be made at all (their team doesn't own the ship). The player is told, and sees
        /// the loadout as it really is again.
        /// </summary>
        public static void RefuseEditAsHost(int actor, RoomSync.RoomEdit edit, string why)
        {
            if (edit == null || string.IsNullOrEmpty(edit.Hull) || !ValidShip(edit.Ship)) return;
            HullInfo hull;
            var hullGuid = new GUIDUnion(edit.Hull);
            if (!Catalog.ShipGuids().Contains(hullGuid) || !Catalog.TryGetHull(hullGuid, out hull)) return;
            RoomSync.PublishLayouts(hull, actor, edit.N, edit.Ship, new List<string> { "your change, because " + why });
        }

        /// <summary>The number of slots of each storage rack, by core system index.</summary>
        public static Dictionary<int, int> RackSlots(HullInfo hull)
        {
            return hull.Shelves.ToDictionary(s => s.CoreIndex, s => s.Slots.Count);
        }

        /// <summary>Host: remembers who changed which parts of a loadout, for the messages when two players collide.</summary>
        private static void NoteEditors(HullInfo hull, int ship, IEnumerable<string> parts, string who)
        {
            foreach (string part in parts) PartEditors[hull.ShipGuid.AsHex() + "/" + ship + "/" + part] = who;
        }

        /// <summary>Host: who last changed a part of a loadout, as far as this session knows.</summary>
        private static string EditorOf(HullInfo hull, int ship, string part)
        {
            string who;
            return PartEditors.TryGetValue(hull.ShipGuid.AsHex() + "/" + ship + "/" + part, out who) ? who : "someone";
        }

        /// <summary>The host changed one of its own CUSTOM loadouts: remember which parts, for the messages when players collide.</summary>
        public static void NoteHostChange(HullInfo hull, int ship, HullConfig before, HullConfig after)
        {
            if (before == null || after == null) return;
            string who = PhotonNetwork.LocalPlayer != null && !string.IsNullOrEmpty(PhotonNetwork.LocalPlayer.NickName) ? PhotonNetwork.LocalPlayer.NickName : "the host";
            NoteEditors(hull, ship, LayoutMerge.Changed(before, after, hull.Sockets, RackSlots(hull)), who);
        }

        /// <summary>
        /// Who last changed a part of the loadout the terminal shows (the host knows per part, other players per loadout),
        /// for the message when a change is refused because the part changed while a list was open.
        /// </summary>
        public static string LastEditorOf(HullInfo hull, string part)
        {
            if (PhotonNetwork.IsMasterClient) return EditorOf(hull, TerminalShip, part);
            string who;
            return LayoutEditors.TryGetValue(hull.ShipGuid.AsHex() + "/" + TerminalShip, out who) ? who : "someone";
        }

        /// <summary>Other players: the host shared a change someone else made to one of its loadouts.</summary>
        public static void OnChangedByOther(string who, int ship, GUIDUnion hullGuid)
        {
            LayoutEditors[hullGuid.AsHex() + "/" + ship] = who;
            HullInfo hull;
            if (!Catalog.TryGetHull(hullGuid, out hull)) return;
            ToastChange(who, Whose(ship) + "CUSTOM " + hull.Name);
        }

        /// <summary>"NAME changed CUSTOM METEM Frigate.", at most every few seconds per player.</summary>
        private static void ToastChange(string who, string what)
        {
            float last;
            if (LastChangeToast.TryGetValue(who, out last) && Time.unscaledTime - last <= 8f) return;
            LastChangeToast[who] = Time.unscaledTime;
            TerminalUi.ShowToast(who + " changed " + what + ".");
        }

        /// <summary>A part of a loadout in words, for messages: "HARDPOINT 3", "STORAGE RACK 1 slot 4", "the fabricator level".</summary>
        public static string PartName(HullInfo hull, string part)
        {
            if (part == LayoutMerge.Homunculus) return "the homunculus";
            if (part == LayoutMerge.Alloys) return "the starting alloys";
            if (part == LayoutMerge.Fabricator) return "the fabricator level";
            if (part.StartsWith("hp:")) return "HARDPOINT " + (int.Parse(part.Substring(3)) + 1);
            if (part.StartsWith("rack:"))
            {
                string[] bits = part.Split(':');
                int rack = int.Parse(bits[1]);
                ShelfInfo shelf = hull.Shelves.FirstOrDefault(s => s.CoreIndex == rack);
                return (shelf != null ? shelf.Name : "a storage rack") + " slot " + (int.Parse(bits[2]) + 1);
            }
            return part;
        }

        /// <summary>
        /// A loadout from another player or a share code, cut down to what this ship has and what this PC's settings allow
        /// (it is saved here): known modules and weapons that fit their hardpoint (any size with AllowAnySize), never two
        /// that overlap, one of each one-per-ship module, things that can lie on a rack (on any rack with AnyItemOnAnyRack,
        /// otherwise only where the rack takes them), and a homunculus that is one. Anything else is left out.
        /// </summary>
        public static HullConfig Sanitize(HullInfo hull, HullConfig config)
        {
            return CheckCodes(hull, Shape(hull, config));
        }

        /// <summary>
        /// The first half of <see cref="Sanitize"/>, which needs nothing from the game: only hardpoint positions and racks
        /// the ship has (no more items than a rack has slots, contents only where a module is), codes of a sane length, and
        /// the numbers in range.
        /// </summary>
        internal static HullConfig Shape(HullInfo hull, HullConfig config)
        {
            var shaped = new HullConfig
            {
                CopiedFrom = config.CopiedFrom != null && config.CopiedFrom.Length <= 100 ? config.CopiedFrom : "",
                Homunculus = Code(config.Homunculus),
                Alloys = config.Alloys,
                FabricatorLevel = config.FabricatorLevel
            };
            if (config.Slots != null)
            {
                foreach (KeyValuePair<int, string> slot in config.Slots)
                {
                    if (slot.Key >= 0 && slot.Key < hull.Sockets.Count && Code(slot.Value).Length > 0) shaped.Slots[slot.Key] = slot.Value;
                }
            }
            if (config.Storage != null)
            {
                foreach (ShelfInfo shelf in hull.Shelves)
                {
                    List<string> codes;
                    if (config.Storage.TryGetValue(shelf.CoreIndex, out codes) && codes != null) shaped.Storage[shelf.CoreIndex] = codes.Take(shelf.Slots.Count).Select(Code).ToList();
                }
            }
            if (config.Contents != null)
            {
                foreach (KeyValuePair<int, List<string>> inside in config.Contents)
                {
                    if (inside.Value != null && shaped.Slots.ContainsKey(inside.Key)) shaped.Contents[inside.Key] = inside.Value.Take(MaxContentSlots).Select(Code).ToList();
                }
            }
            return shaped.Normalized();
        }

        /// <summary>More item slots than any module has.</summary>
        private const int MaxContentSlots = 16;

        /// <summary>
        /// The second half of <see cref="Sanitize"/>, against the game's catalog: known modules and weapons that fit their
        /// hardpoint (any size with AllowAnySize), never two that overlap, one of each one-per-ship module, things that can
        /// lie on a rack (on any rack with AnyItemOnAnyRack, otherwise only where the rack takes them), a homunculus that is
        /// one, and contents each module slot takes.
        /// </summary>
        private static HullConfig CheckCodes(HullInfo hull, HullConfig shaped)
        {
            var clean = new HullConfig
            {
                CopiedFrom = shaped.CopiedFrom,
                Homunculus = shaped.Homunculus.Length > 0 && Catalog.IsHomunculus(shaped.Homunculus) ? shaped.Homunculus : "",
                Alloys = shaped.Alloys,
                FabricatorLevel = shaped.FabricatorLevel
            };
            var singles = new HashSet<Type>();
            foreach (KeyValuePair<int, string> slot in shaped.Slots)
            {
                Option option;
                if (!Catalog.TryGetHardpointOption(slot.Value, out option)) continue;
                SocketInfo socket = hull.Sockets[slot.Key];
                if (!Settings.AllowAnySize.Value && option.Size != socket.Size) continue;
                if (socket.Overlaps.Any(clean.Slots.ContainsKey)) continue;
                if (option.SingleInstance && option.ModuleType != null && !singles.Add(option.ModuleType)) continue;
                clean.Slots[slot.Key] = option.Code;
            }
            foreach (ShelfInfo shelf in hull.Shelves)
            {
                List<string> codes;
                if (shaped.Storage.TryGetValue(shelf.CoreIndex, out codes)) clean.Storage[shelf.CoreIndex] = codes.Select(code => RackCode(shelf, code)).ToList();
            }
            foreach (KeyValuePair<int, List<string>> inside in shaped.Contents)
            {
                string code;
                Option module;
                if (!clean.Slots.TryGetValue(inside.Key, out code) || !Catalog.TryGetHardpointOption(code, out module)) continue;
                List<ContentSlot> slots = Catalog.ContentSlotsOf(module);
                if (slots.Count == 0) continue;
                clean.Contents[inside.Key] = inside.Value.Take(slots.Count).Select((c, j) => ContentCode(slots[j], c)).ToList();
            }
            return clean.Normalized();
        }

        /// <summary>A module slot's code if it names an item that slot takes, otherwise "" (empty).</summary>
        private static string ContentCode(ContentSlot slot, string code)
        {
            Option item;
            if (code.Length == 0 || !Catalog.TryGetRackOption(code, out item) || !Catalog.ContentSlotAccepts(slot, item)) return "";
            return item.Code;
        }

        /// <summary>A rack slot's code if it names something that can lie there, otherwise "" (an empty slot).</summary>
        private static string RackCode(ShelfInfo shelf, string code)
        {
            Option option;
            if (code.Length == 0 || !Catalog.TryGetRackOption(code, out option)) return "";
            if (!Settings.AnyItemOnAnyRack.Value && !Catalog.ShelfAccepts(shelf, option)) return "";
            return option.Code;
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
            return ActiveShips[TerminalShip] == hull.ShipGuid;
        }

        /// <summary>
        /// Void Crew Fleet switched the ship the terminal sets up; every CUSTOM definition now shows that ship's layout.
        /// Without Fleet running (here and on the host) there are no other ships, so the terminal stays on ship 1.
        /// </summary>
        public static void SetTerminalShip(int ship)
        {
            if (!ValidShip(ship)) ship = 1;
            if (ship >= 2 && !FleetLink.Running)
            {
                Log.WarnOnce("ship " + ship + " without fleet", "Ignored a request to set up ship " + ship + ": Void Crew Fleet isn't running here and on the host.");
                ship = 1;
            }
            if (_terminalShip == ship) return;
            _terminalShip = ship;
            RefreshAll();
            TerminalUi.OnTerminalShipChanged();
        }

        /// <summary>The terminal closed: it sets up ship 1 again next time.</summary>
        public static void ResetTerminalShip()
        {
            if (_terminalShip == 1) return;
            _terminalShip = 1;
            RefreshAll();
        }

        /// <summary>
        /// Void Crew Fleet's features started or stopped applying (the host's word arrived, or a new host): the terminal
        /// shows ship 1 again without Fleet, and every CUSTOM loadout is drawn for what applies now.
        /// </summary>
        public static void OnFleetRunningChanged(bool running)
        {
            if (!running) _terminalShip = 1;
            RefreshAll();
            TerminalUi.OnTerminalShipChanged();
        }

        /// <summary>
        /// Void Crew Fleet's ships changed (how many, or a ship's forced type): the terminal is drawn again, on ship 1 if
        /// the ship it showed is gone. (Every ship's own loadouts are shared whatever the count, so nothing is sent.)
        /// </summary>
        public static void OnShipsChanged()
        {
            bool gone = _terminalShip > FleetLink.ShipCount;
            if (gone) _terminalShip = 1;
            RefreshAll();
            // Open lists stay open unless the ship they were for is gone.
            if (gone) TerminalUi.OnTerminalShipChanged();
            else TerminalUi.OnLoadoutChangedRemotely();
        }

        /// <summary>The name of a ship type ("METEM Destroyer"), for messages.</summary>
        public static string HullName(GUIDUnion shipGuid)
        {
            HullInfo hull;
            return !shipGuid.IsNull && Catalog.ShipGuids().Contains(shipGuid) && Catalog.TryGetHull(shipGuid, out hull) ? hull.Name : "different ship";
        }

        /// <summary>
        /// Why a player ship can't use this ship type's CUSTOM loadout, or null when it can: the host may make a ship use a
        /// given type (Void Crew Fleet 0.4.0).
        /// </summary>
        public static string WrongType(int ship, HullInfo hull)
        {
            GUIDUnion forced = FleetLink.HullOfShip(ship);
            if (forced.IsNull || forced == hull.ShipGuid) return null;
            return FleetLink.ShipName(ship) + " must be a " + HullName(forced) + " in this match, so it can't use the CUSTOM " + hull.Name + ".";
        }

        /// <summary>Rebuilds what the terminal shows for a ship after its loadout changed.</summary>
        public static void Refresh(HullInfo hull)
        {
            Entry entry;
            if (!ByShip.TryGetValue(hull.ShipGuid, out entry)) return;
            try
            {
                HullConfig config = ConfigToShow(hull);
                entry.Data.ShipLoadout = Build(hull, config);
                if (BodyTextField != null) BodyTextField.SetValue(entry.Def.LoadoutContextInfo, new DefaultableLocalizedString(Description(hull, config), null));
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
                if (!Catalog.TryGetHardpointOption(slot.Value, out option) || !Catalog.CanSpawn(option.PrefabGuid))
                {
                    Log.WarnOnce("unknown " + slot.Value, "The CUSTOM " + hull.Name + " has a module the game no longer has (" + slot.Value + "); that hardpoint is left empty.");
                    continue;
                }
                ShipSocketEntry entry = SocketEntryFor(option, slot.Key, config.Contents != null && config.Contents.ContainsKey(slot.Key));
                if (entry == null) continue;
                loadout.Sockets.Add(entry);
                filled.Add(slot.Key);
            }

            foreach (ShelfInfo shelf in hull.Shelves)
            {
                List<string> codes;
                config.Storage.TryGetValue(shelf.CoreIndex, out codes);
                // A fresh entry per rack, so nothing of the base loadout's racks is left over: the game fills each rack slot
                // from exactly one entry, and its module must be the rack's own (or the game skips the whole rack).
                loadout.CoreSystemSockets.RemoveAll(c => c != null && c.SocketId == shelf.CoreIndex);
                if (!Catalog.CanSpawn(shelf.ModuleGuid)) continue;
                var rack = new ShipSocketEntry
                {
                    SocketId = shelf.CoreIndex,
                    Module = new GameObjectRef(new ResourceAssetRef(shelf.ModuleGuid)),
                    BuildBox = new CarryableRef(),
                    Upgrades = new ShipAssetRef[0]
                };
                loadout.CoreSystemSockets.Add(rack);
                var items = new ShipAssetRef[shelf.Slots.Count];
                for (int i = 0; i < items.Length; i++)
                {
                    items[i] = ItemFor(codes != null && i < codes.Count ? codes[i] : "", hull);
                }
                rack.Upgrades = items;
            }

            loadout.HomunculusOverride = new CarryableRef();
            Option homunculus;
            if (!string.IsNullOrEmpty(config.Homunculus) && Catalog.TryGetOption(config.Homunculus, out homunculus) && Catalog.IsCarryable(homunculus.Guid) && Catalog.CanSpawn(homunculus.Guid))
            {
                loadout.HomunculusOverride = new CarryableRef(homunculus.Guid);
            }
            return loadout;
        }

        /// <param name="chosenContents">
        /// The loadout says what this module holds: the game starts it empty, and the host puts the chosen items in once
        /// the ship has spawned (see FillContents), into the module's real slots, each only where it fits. Otherwise the
        /// module starts with its usual contents from the game's loadouts.
        /// </param>
        private static ShipSocketEntry SocketEntryFor(Option option, int socketIndex, bool chosenContents)
        {
            ShipSocketEntry entry = null;
            JObject template;
            if (!chosenContents && Settings.CopyPresetModuleContents.Value && Catalog.TryGetPresetContents(option.Guid, out template))
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
            if (entry.Upgrades == null || chosenContents) entry.Upgrades = new ShipAssetRef[0];
            entry.Upgrades = entry.Upgrades.Select(u => u != null && !u.IsNull && Catalog.CanSpawn(u.AssetGuid) && Catalog.IsDefaultContent(u) && KeepsToMatchRules(u) ? u : new ShipAssetRef()).ToArray();
            if (option.Kind == OptionKind.Weapon)
            {
                entry.ExtraData = WeaponData(option.Guid);
            }
            return entry;
        }

        /// <summary>
        /// In a Fleet Battle, whether something a module starts with (ammo, a power cell, a weapon mod, copied from the game's
        /// loadouts) keeps to the match rules; anything else stays out, as on the racks.
        /// </summary>
        private static bool KeepsToMatchRules(ShipAssetRef content)
        {
            if (!MatchRules.InMatch) return true;
            Option option;
            return !Catalog.TryGetOption(Catalog.CodeFor(content), out option) || MatchRules.WhyRefused(option) == null;
        }

        private static ShipAssetRef ItemFor(string code, HullInfo hull)
        {
            if (string.IsNullOrEmpty(code)) return new ShipAssetRef();
            Option option;
            if (!Catalog.TryGetRackOption(code, out option))
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
            ActiveShips[1] = hull.ShipGuid;
            RemoteConfigs[1] = null;
            Refresh(hull);
            RoomSync.PublishCustom(1, hull, LoadoutStore.Get(hull));
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
            ActiveShips[1] = GUIDUnion.Empty();
            RoomSync.ClearCustom(1);
            Log.Info("The room's loadout is no longer the CUSTOM " + name + ".");
        }

        /// <summary>
        /// A CUSTOM loadout was changed and saved on this PC. On the host, tell the other players: the room's chosen
        /// loadouts, and the shared loadouts (<paramref name="ship"/> names the player ship that changed, 0 for all;
        /// <paramref name="by"/> and <paramref name="n"/> name the player's change, 0 for the host's own).
        /// </summary>
        public static void OnEdited(HullInfo hull, int by = 0, int n = 0, int ship = 0, List<string> refused = null)
        {
            Refresh(hull);
            if (!PhotonNetwork.IsMasterClient) return;
            // Every ship that uses this CUSTOM loadout: ships 2 to 6 follow ship 1's until they have their own.
            for (int s = 1; s <= MaxShips; s++)
            {
                if (ActiveShips[s] == hull.ShipGuid) RoomSync.PublishCustom(s, hull, LoadoutStore.Get(hull, s));
            }
            RoomSync.PublishLayouts(hull, by, n, ship, refused);
        }

        /// <summary>
        /// Host: the room's CUSTOM choices again, as they are now (a Fleet Battle starting or ending changes what ships 2 to
        /// 6 use while they have no loadout of their own). Unchanged ones aren't sent.
        /// </summary>
        public static void ShareChoicesAgain()
        {
            if (!PhotonNetwork.IsMasterClient) return;
            for (int s = 1; s <= MaxShips; s++)
            {
                HullInfo hull;
                if (!ActiveShips[s].IsNull && Catalog.TryGetHull(ActiveShips[s], out hull)) RoomSync.PublishCustom(s, hull, LoadoutStore.Get(hull, s));
            }
        }

        // ---------------------------------------------------------------- ships 2 to 6 (Void Crew Fleet)

        /// <summary>Host: one of Void Crew Fleet's ships now uses this ship type's CUSTOM loadout.</summary>
        public static void ActivateShipAsHost(int ship, HullInfo hull)
        {
            if (ship < 2 || !ValidShip(ship)) return;
            bool changed = ActiveShips[ship] != hull.ShipGuid;
            ActiveShips[ship] = hull.ShipGuid;
            RemoteConfigs[ship] = null;
            Refresh(hull);
            RoomSync.PublishCustom(ship, hull, LoadoutStore.Get(hull, ship));
            ModdingUtils.RegisterSessionMod();
            if (changed) Log.Info(FleetLink.ShipName(ship) + "'s loadout is now its CUSTOM " + hull.Name + " (" + Describe(LoadoutStore.Get(hull, ship)) + ").");
        }

        /// <summary>Host: one of Void Crew Fleet's ships uses a game loadout (or none).</summary>
        public static void DeactivateShipAsHost(int ship)
        {
            if (ship < 2 || !ValidShip(ship) || ActiveShips[ship].IsNull) return;
            HullInfo hull;
            string name = Catalog.TryGetHull(ActiveShips[ship], out hull) ? hull.Name : "ship";
            ActiveShips[ship] = GUIDUnion.Empty();
            RoomSync.ClearCustom(ship);
            Log.Info(FleetLink.ShipName(ship) + "'s loadout is no longer the CUSTOM " + name + ".");
        }

        /// <summary>
        /// Host, at mission start: a Void Crew Fleet ship's CUSTOM loadout built fresh, or null when CUSTOM can't be used
        /// (the host is told why, and the caller uses the stand-in). The same rules as ship 1, plus: ships 3 to 6 need
        /// everyone on Ship Customizer 0.9.0 or newer. A CUSTOM ship makes the run a CUSTOM run.
        /// </summary>
        public static ShipLoadout BuildShipForMission(int ship, GUIDUnion guid)
        {
            ShipLoadoutDataDef def;
            HullInfo hull;
            if (ship < 2 || !ValidShip(ship) || !PhotonNetwork.IsMasterClient || !TryGetDefOfCustomGuid(guid, out def) || !TryGetHullOfCustomGuid(guid, out hull)) return null;
            string name = FleetLink.ShipName(ship);
            string instead = name + " starts with " + LoadoutStore.PresetName(hull.BasePreset) + " instead of CUSTOM: ";
            string wrongType = WrongType(ship, hull);
            if (wrongType != null)
            {
                Notice.Show(wrongType);
                return null;
            }
            List<string> missing = RoomSync.PlayersWithoutMod(false);
            if (missing.Count > 0)
            {
                Notice.Show(instead + string.Join(", ", missing.ToArray()) + " doesn't have Ship Customizer, and nobody may earn progression in a CUSTOM run.");
                return null;
            }
            List<string> older = ship >= 3 ? RoomSync.PlayersWithoutManyShips(false) : new List<string>();
            if (older.Count > 0)
            {
                Notice.Show(instead + string.Join(", ", older.ToArray()) + " has a Ship Customizer older than 0.9.0, which ships 3 to 6 need.");
                return null;
            }
            if (!EnsureRoomMarkedModded())
            {
                Notice.Show(instead + "the room is not marked [mod_session] yet.");
                return null;
            }
            // In a Fleet Battle, without what the match rules don't allow (on a copy: the saved loadout keeps it).
            HullConfig config = MatchRules.ForMission(hull, LoadoutStore.Get(hull, ship), ship);
            ShipLoadout loadout = Build(hull, config);
            // Progression is paused and the room closed before the CUSTOM ship goes to the game. This only ever raises the
            // pause; ship 1's check at mission start keeps it raised (see OnMissionStarting).
            _otherShipCustomRun = true;
            ProgressionGuard.SetHostRun(true);
            ModdingUtils.RegisterSessionMod();
            RoomSync.CloseForCustomMission();
            RememberShip(hull, loadout, config, name);
            Log.Info(name + " starts with its CUSTOM " + hull.Name + ": " + Describe(config) + ".");
            return loadout;
        }

        /// <summary>
        /// Called just before the game leaves the hub for a mission. On the host, swaps in the CUSTOM ship when the room
        /// chose one, and decides whether this run pauses progression. Progression is paused (and the room closed) before
        /// the CUSTOM ship is handed to the game, so nothing that goes wrong later can start a CUSTOM ship with progression on.
        /// </summary>
        public static void OnMissionStarting(GameSession session, PreservedGameSession preserved)
        {
            if (!PhotonNetwork.IsMasterClient || session == null) return;
            _missionIsSavedRun = preserved != null;

            bool custom = false;
            HullInfo hull = null;
            HullConfig config = null;
            ShipLoadout built = null;
            if (preserved != null)
            {
                // Continuing a saved run: it is a CUSTOM run if it was started as one.
                custom = LoadoutStore.WasCustomRun(preserved.GameSessionID);
                if (custom)
                {
                    LoadoutStore.RememberCustomRun(preserved.GameSessionID);
                    Log.Info("Continuing a saved run that was started with a CUSTOM loadout; progression stays paused.");
                }
            }
            else if (IsActive && session.ToLoadShipData != null && Catalog.TryGetHull(ActiveShip, out hull) && hull.BasePreset != null &&
                     session.ToLoadShipGuid == hull.BasePreset.AssetGuid)
            {
                List<string> missing = RoomSync.PlayersWithoutMod(false);
                string wrongType = WrongType(1, hull);
                if (missing.Count > 0)
                {
                    Notice.Show("This mission starts with " + LoadoutStore.PresetName(hull.BasePreset) + " instead of CUSTOM: " + string.Join(", ", missing.ToArray()) +
                                " doesn't have Ship Customizer, and nobody may earn progression in a CUSTOM run.");
                }
                else if (wrongType != null)
                {
                    Notice.Show(wrongType);
                }
                else if (!EnsureRoomMarkedModded())
                {
                    Notice.Show("This mission starts with " + LoadoutStore.PresetName(hull.BasePreset) + " instead of CUSTOM: the room is not marked [mod_session] yet.");
                }
                else
                {
                    // In a Fleet Battle, without what the match rules don't allow (on a copy: the saved loadout keeps it).
                    // If building fails, nothing has changed yet and the stand-in starts.
                    config = MatchRules.ForMission(hull, LoadoutStore.Get(hull), 1);
                    built = Build(hull, config);
                    custom = true;
                }
            }

            if (custom && preserved != null)
            {
                List<string> missing = RoomSync.PlayersWithoutMod(false);
                if (missing.Count > 0) Log.Warn("Continuing a saved CUSTOM run while " + string.Join(", ", missing.ToArray()) + " doesn't have Ship Customizer; their progression can't be paused.");
            }

            bool shipOneCustom = custom;
            // Void Crew Fleet's ships can be CUSTOM too, whichever of the two mission-start hooks ran first.
            if (_otherShipCustomRun && preserved == null) custom = true;
            ResumeShip = shipOneCustom && preserved == null ? ActiveShip : GUIDUnion.Empty();
            ProgressionGuard.SetHostRun(custom);
            if (custom)
            {
                ModdingUtils.RegisterSessionMod();
                RoomSync.CloseForCustomMission();
            }
            if (built == null) return;

            session.ToLoadShipData = built;
            // The game's own starting amount for the run (-1 means its usual bonus from the players' ranks).
            if (config.Alloys >= 0) session.InitialAlloys = config.Alloys;
            RememberShip(hull, built, config, FleetLink.Running ? FleetLink.ShipName(1) : "The ship");
            Log.Info("Starting the mission with your CUSTOM " + hull.Name + ": " + Describe(config) + ".");
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

        // ---------------------------------------------------------------- the ships, checked after spawning

        /// <summary>A CUSTOM ship this mission spawns: its loadout, which core systems are its racks, and its fabricator level.</summary>
        private sealed class ShipPlan
        {
            public string Hull;
            public string Which;
            /// <summary>The loadout handed to the game, and the same as JSON, to recognise a copy of it (Void Crew Fleet's "as ship 1").</summary>
            public ShipLoadout Loadout;
            public string Json;
            public HashSet<int> Racks;
            public int FabricatorLevel;
            /// <summary>The items chosen for each hardpoint's module, to put in once it has spawned.</summary>
            public Dictionary<int, List<string>> Contents;
            /// <summary>How many ships spawned from it so far: Void Crew Fleet's co-op copies of ship 1 spawn from the very same loadout.</summary>
            public int Spawns;
        }

        /// <summary>A spawned CUSTOM ship that still needs its fabricator level and its modules' chosen contents.</summary>
        private sealed class PendingLevel
        {
            public PlayerControlledShip Ship;
            public int Level;
            public Dictionary<int, List<string>> Contents;
            public string Which;
            public float Since = -1f;
        }

        private static readonly List<ShipPlan> ShipPlans = new List<ShipPlan>();
        private static readonly List<PendingLevel> PendingLevels = new List<PendingLevel>();

        /// <summary>Host, at mission start: remembers a CUSTOM ship, to check its racks and set its fabricator once it has spawned.</summary>
        private static void RememberShip(HullInfo hull, ShipLoadout loadout, HullConfig config, string which)
        {
            var built = new HashSet<int>(loadout.Sockets.Where(s => s != null).Select(s => s.SocketId));
            ShipPlans.Add(new ShipPlan
            {
                Hull = hull.Name,
                Which = which,
                Loadout = loadout,
                Json = JsonOf(loadout),
                Racks = new HashSet<int>(hull.Shelves.Select(s => s.CoreIndex)),
                // Continuing a saved run, the game puts the fabricators back as they were.
                FabricatorLevel = _missionIsSavedRun ? 0 : config.FabricatorLevel,
                Contents = config.Contents == null
                    ? new Dictionary<int, List<string>>()
                    : config.Contents.Where(c => built.Contains(c.Key) && c.Value != null).ToDictionary(c => c.Key, c => c.Value.ToList())
            });
        }

        private static string JsonOf(ShipLoadout loadout)
        {
            try
            {
                return loadout.AsJObject().ToString(Newtonsoft.Json.Formatting.None);
            }
            catch (Exception e)
            {
                Log.Verbose("Could not read a loadout as JSON: " + e.Message);
                return null;
            }
        }

        /// <summary>
        /// Host, right after the game spawned a player ship. If it is one of this mission's CUSTOM ships, its racks are
        /// checked and its fabricator level and contents are queued for that ship object, so each ship gets its own
        /// whichever team the host is on and wherever the ship is in the game's list. A CUSTOM ship is the very loadout
        /// handed to the game; Void Crew Fleet 0.4.0 spawns its co-op copies of ship 1 from that same loadout too. An older
        /// Fleet spawns a copy of it, recognised by its contents.
        /// </summary>
        public static void OnShipSpawned(ShipLoadout loadout, PlayerControlledShip ship)
        {
            if (!PhotonNetwork.IsMasterClient || loadout == null || ship == null || ShipPlans.Count == 0) return;
            ShipPlan plan = ShipPlans.FirstOrDefault(p => ReferenceEquals(p.Loadout, loadout));
            string which = null;
            if (plan != null)
            {
                plan.Spawns++;
                which = plan.Spawns > 1 ? "A copy of " + plan.Which : plan.Which;
            }
            else
            {
                // Only for a Fleet without ships 3 to 6: matching by contents would also take a plain stand-in ship for a
                // copy of a CUSTOM ship that changes no modules.
                if (FleetLink.KnowsManyShips) return;
                string json = JsonOf(loadout);
                plan = json == null ? null : ShipPlans.FirstOrDefault(p => p.Json == json);
                if (plan == null) return;
                which = "The copy of the CUSTOM " + plan.Hull;
            }
            CheckRacks(plan, which, loadout, ship);
            if (plan.FabricatorLevel > 0 || plan.Contents.Count > 0)
            {
                PendingLevels.Add(new PendingLevel
                {
                    Ship = ship,
                    Level = plan.FabricatorLevel,
                    Contents = plan.Contents.ToDictionary(c => c.Key, c => c.Value.ToList()),
                    Which = which
                });
            }
        }

        /// <summary>
        /// Every rack slot of a spawned CUSTOM ship must hold what the CUSTOM loadout puts there. The game fills racks only
        /// from the loadout, but if a slot ends up with something else (or an empty slot with anything), that item is
        /// replaced by the chosen one and the log says so.
        /// </summary>
        private static void CheckRacks(ShipPlan plan, string which, ShipLoadout loadout, PlayerControlledShip ship)
        {
            if (ship.CoreSystems == null) return;
            int replaced = 0;
            foreach (ShipSocketEntry rack in loadout.CoreSystemSockets.Where(c => c != null && plan.Racks.Contains(c.SocketId) && c.Upgrades != null))
            {
                CellModule module = ship.CoreSystems.ElementAtOrDefault(rack.SocketId);
                if (module == null) continue;
                List<CarryablesSocket> sockets = module.CarryablesSockets.ToList();
                for (int k = 0; k < sockets.Count && k < rack.Upgrades.Length; k++)
                {
                    CarryablesSocket socket = sockets[k];
                    if (socket == null) continue;
                    ShipAssetRef wanted = rack.Upgrades[k];
                    GUIDUnion wantedGuid = wanted != null && !wanted.IsNull ? wanted.AssetRef.AssetGuid : GUIDUnion.Empty();
                    CarryableObject current = socket.Payload;
                    GUIDUnion currentGuid = current != null ? current.assetGuid : GUIDUnion.Empty();
                    if (currentGuid == wantedGuid) continue;
                    Log.Warn(which + "'s storage rack slot " + (k + 1) + " (core system " + rack.SocketId + ") held " +
                             (current != null ? NameOfItem(currentGuid) : "nothing") + " instead of " + (wantedGuid.IsNull ? "nothing" : NameOfItem(wantedGuid)) + "; fixing it.");
                    if (current != null) socket.DestroyStoredCarryable();
                    if (!wantedGuid.IsNull) PutInSlot(socket, wantedGuid, wanted.ExtraData);
                    replaced++;
                }
            }
            if (replaced > 0) Log.Warn("Put " + replaced + " storage rack slot(s) of " + which.ToLowerInvariant() + " back to its CUSTOM loadout.");
        }

        private static void PutInSlot(CarryablesSocket socket, GUIDUnion item, JObject extraData)
        {
            var data = new Dictionary<byte, object> { { 1, socket.photonView.ViewID } };
            if (extraData != null) data.Add(0, extraData.ToString(Newtonsoft.Json.Formatting.None));
            AbstractCloneStarObject spawned = ObjectFactory.InstantiateSpaceObjectByGUID(item, socket.WorldPosition, socket.WorldRotation, data);
            CarryableObject carryable = spawned != null ? spawned.GetComponent<CarryableObject>() : null;
            if (carryable != null) socket.TryInsertCarryable(carryable);
        }

        private static string NameOfItem(GUIDUnion guid)
        {
            Option option;
            return Catalog.TryGetOption("i:" + guid.AsHex(), out option) ? option.Name : guid.AsHex();
        }

        // ---------------------------------------------------------------- the fabricators' starting level

        /// <summary>
        /// Host, early in a fresh CUSTOM mission: each spawned CUSTOM ship's fabricator starts at its CUSTOM level, and its
        /// modules get the contents chosen for them, once the ship has had a moment to start up (the way the game itself puts
        /// a saved run's fabricator back). Called a few times a second; gives up on a ship after a minute.
        /// </summary>
        public static void ApplyFabricatorLevels()
        {
            if (PendingLevels.Count == 0 || !PhotonNetwork.IsMasterClient || GameSessionManager.InHub) return;
            for (int i = PendingLevels.Count - 1; i >= 0; i--)
            {
                PendingLevel pending = PendingLevels[i];
                if (pending.Ship == null)
                {
                    PendingLevels.RemoveAt(i);
                    continue;
                }
                if (pending.Since < 0f) pending.Since = Time.unscaledTime;
                float waited = Time.unscaledTime - pending.Since;
                // A moment for the ship's own start-up (its fabricator terminal and upgrade visuals listen for the level).
                if (waited < 1f) continue;
                if (pending.Level > 0 && SetFabricatorLevel(pending.Ship, pending.Level, pending.Which)) pending.Level = 0;
                if (pending.Contents.Count > 0) FillContents(pending.Ship, pending.Contents, pending.Which);
                if (pending.Level == 0 && pending.Contents.Count == 0)
                {
                    PendingLevels.RemoveAt(i);
                }
                else if (waited > 60f)
                {
                    if (pending.Level > 0) Log.Warn(pending.Which + "'s fabricator could not be set to its CUSTOM level, so it starts at MK I.");
                    if (pending.Contents.Count > 0) Log.Warn(pending.Which + ": " + pending.Contents.Count + " module(s) could not be loaded with their chosen contents.");
                    PendingLevels.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Host: puts the chosen items into the spawned modules, by hardpoint. Each item goes into its own slot when that
        /// slot takes it, otherwise into the first empty slot of the module that does, otherwise it is left out (logged).
        /// A module that isn't there yet is tried again next time.
        /// </summary>
        private static void FillContents(PlayerControlledShip ship, Dictionary<int, List<string>> contents, string which)
        {
            BuildSocketController controller = ship.GetComponent<BuildSocketController>();
            if (controller == null || controller.Sockets == null)
            {
                contents.Clear();
                return;
            }
            foreach (KeyValuePair<int, List<string>> pair in contents.ToList())
            {
                if (pair.Key < 0 || pair.Key >= controller.Sockets.Count || controller.Sockets[pair.Key] == null)
                {
                    contents.Remove(pair.Key);
                    continue;
                }
                CellModule module = controller.Sockets[pair.Key].InstalledModule;
                if (module == null) continue;
                List<CarryablesSocket> slots = module.CarryablesSockets.Where(s => s != null).ToList();
                for (int j = 0; j < pair.Value.Count; j++)
                {
                    Option item;
                    if (string.IsNullOrEmpty(pair.Value[j]) || !Catalog.TryGetRackOption(pair.Value[j], out item) || item.Kind != OptionKind.Item) continue;
                    CarryablesSocket target = j < slots.Count && slots[j].Payload == null && Catalog.SocketAccepts(slots[j], item, null, false, "a module slot")
                        ? slots[j]
                        : slots.FirstOrDefault(s => s.Payload == null && Catalog.SocketAccepts(s, item, null, false, "a module slot"));
                    if (target == null)
                    {
                        Log.Warn(which + ": the " + RichText.Plain(item.Name) + " chosen for hardpoint " + pair.Key + " has no free slot that takes it, so it was left out.");
                        continue;
                    }
                    PutInSlot(target, item.Guid, null);
                }
                contents.Remove(pair.Key);
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

        /// <summary>A short line for lists: "12 modules  ·  32 items  ·  500 alloys  ·  fabricator MK III".</summary>
        public static string Summary(HullConfig config)
        {
            var parts = new List<string>
            {
                config.FittedCount + (config.FittedCount == 1 ? " module" : " modules"),
                config.StoredCount + (config.StoredCount == 1 ? " item" : " items")
            };
            if (config.Alloys >= 0) parts.Add(config.Alloys + " alloys");
            if (config.FabricatorLevel > 0) parts.Add("fabricator " + FabricatorLevelName(config.FabricatorLevel));
            return string.Join("  ·  ", parts.ToArray());
        }

        public static string FabricatorLevelName(int level)
        {
            return level == 2 ? "MK III" : (level == 1 ? "MK II" : "MK I");
        }

        /// <summary>The last mission's ships, levels and notes are done with (back in the hub, or in another room).</summary>
        private static void ResetMissionState()
        {
            _otherShipCustomRun = false;
            ShipPlans.Clear();
            PendingLevels.Clear();
        }

        /// <summary>Back in the hub after a CUSTOM mission: choose that CUSTOM loadout again, as the game does for its own.</summary>
        public static void OnHubLoaded(HubShipManager manager)
        {
            ResetMissionState();
            MatchRules.OnHubLoaded();
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
            for (int ship = 0; ship <= MaxShips; ship++)
            {
                ActiveShips[ship] = GUIDUnion.Empty();
                RemoteConfigs[ship] = null;
                HostLayouts[ship].Clear();
                ConfirmedLayouts[ship].Clear();
            }
            ResumeShip = GUIDUnion.Empty();
            ResetMissionState();
            _terminalShip = 1;
            PartEditors.Clear();
            LayoutEditors.Clear();
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
