using System;
using System.Collections.Generic;
using System.Linq;
using CG.Game;
using CG.Profile;
using ExitGames.Client.Photon;
using Gameplay.Hub;
using Newtonsoft.Json;
using Photon.Pun;
using ResourceAssets;
using UnityEngine;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// Everything the plugin shares through Photon:
    /// - room properties telling other players with the mod about the host's CUSTOM loadout and CUSTOM missions;
    /// - room properties holding the host's CUSTOM loadouts, so every player with the mod can see and change them;
    /// - a player property saying "I have the Ship Customizer", which the host checks before using CUSTOM;
    /// - a player property with a player's change to one of the host's CUSTOM loadouts, which the host saves and shares.
    /// Players without the mod ignore all of it. The host writes the room properties; everyone else reads them.
    /// </summary>
    internal static class RoomSync
    {
        /// <summary>
        /// JSON of the CUSTOM loadout ship 1 uses, absent while a game loadout is chosen. Ships 2 to 6 (Void Crew Fleet) have
        /// their own: SC_Custom2 to SC_Custom6 (see <see cref="CustomKeyOf"/>).
        /// </summary>
        public const string CustomKey = "SC_Custom";

        /// <summary>
        /// 0.9.0 on: whether the host runs Void Crew Fleet. Players with Fleet only use its features (ships 2 to 6, team and
        /// match rules) while the host runs it too.
        /// </summary>
        public const string FleetKey = "SC_Fleet";

        /// <summary>True while the current mission was started with a CUSTOM loadout.</summary>
        public const string RunKey = "SC_Run";

        /// <summary>
        /// The session id of the CUSTOM run under way (0.8.0 on), absent otherwise. Every player's mission has the same id,
        /// so a flag left behind by a host who left can't pause a later mission.
        /// </summary>
        public const string RunIdKey = "SC_RunId";

        /// <summary>True while the room is closed to new players because a CUSTOM mission is running.</summary>
        public const string ClosedKey = "SC_Closed";

        /// <summary>Player property: this player runs the Ship Customizer (the value is <see cref="Protocol"/>).</summary>
        public const string PlayerKey = "SC_Player";

        /// <summary>
        /// A message from the host for every player with the mod (what a Fleet Battle's match rules took off the CUSTOM
        /// ships), shown in the chat once the mission has loaded: JSON of a <see cref="RoomNotice"/>.
        /// </summary>
        public const string NoticeKey = "SC_Notice";

        /// <summary>How old a notice may be and still be shown (a mission can take a while to load).</summary>
        private const int NoticeMaxAgeMs = 5 * 60 * 1000;

        /// <summary>
        /// Raised when what players exchange changes. 1: 0.3.0 and 0.4.0. 2: 0.5.0, where every player with the mod can
        /// choose and change the host's CUSTOM loadouts. 3: 0.8.0, where a change carries the loadout it was made against
        /// and the host merges it part by part (older players' whole-loadout changes still work). 4: 0.9.0, with ships 3
        /// to 6 (Void Crew Fleet 0.4.0) and the host's word on whether it runs Fleet.
        /// </summary>
        public const int Protocol = 4;

        /// <summary>The oldest protocol that still pauses its player's progression in a CUSTOM run, so counts as having the mod.</summary>
        private const int OldestProtocol = 1;

        /// <summary>From this host protocol on, every player with the mod can choose and change the CUSTOM loadouts.</summary>
        private const int SharedProtocol = 2;

        /// <summary>
        /// From this protocol on, a player knows ships 3 to 6: CUSTOM on those ships needs everyone in the room on it (an
        /// older player could neither see nor change their loadouts).
        /// </summary>
        private const int ManyShipsProtocol = 4;

        /// <summary>
        /// Room properties (protocol 2): the host's CUSTOM loadout for one ship type, as this prefix, the player ship (1, or
        /// 2 to 6 for Void Crew Fleet's), "_" and the ship type's GUID. Ships 2 to 6 have none while they follow ship 1's.
        /// </summary>
        private const string LayoutKeyStart = "SC_Layout";

        /// <summary>
        /// Player properties (protocol 2): this player's latest change to one of the host's CUSTOM loadouts, one per ship
        /// type and player ship (named like the room's <see cref="LayoutKeyStart"/> ones), so changes to different
        /// loadouts never replace each other before the host has read them.
        /// </summary>
        private const string EditKeyStart = "SC_Edit";

        /// <summary>The longest change the host reads; a whole CUSTOM loadout is a few KB.</summary>
        private const int MaxEditLength = 32768;

        /// <summary>
        /// How long a player who just joined may take to load into the hub before the host counts them as not having
        /// the mod (their "I have it" property is only sent once they are in the hub).
        /// </summary>
        private const float JoinGraceSeconds = 90f;

        private sealed class RoomCustom
        {
            public string Ship;
            public HullConfig Config;
        }

        /// <summary>One of the host's CUSTOM loadouts, as shared with the room.</summary>
        internal sealed class RoomLayout
        {
            public HullConfig Config;
            /// <summary>The player whose change this is (0 for the host's own), and that player's change number.</summary>
            public int By;
            public int N;
            /// <summary>
            /// 0.8.0 on: the parts of that player's change that were not made, because someone else changed them after the
            /// player last saw them (described for the player, with who it was).
            /// </summary>
            public List<string> Refused;
        }

        /// <summary>A player's change to one of the host's CUSTOM loadouts.</summary>
        internal sealed class RoomEdit
        {
            /// <summary>
            /// The room and host it is meant for. Photon keeps a player's properties when they move to another room, where
            /// actor numbers start again from 1, and after a host change the new host must not take the old host's changes.
            /// </summary>
            public string Room;
            public int To;
            /// <summary>Counts up with each change the player sends.</summary>
            public int N;
            /// <summary>The ship type's GUID.</summary>
            public string Hull;
            /// <summary>The player ship: 1, or 2 to 6 for Void Crew Fleet's.</summary>
            public int Ship = 1;
            /// <summary>The whole changed loadout (empty when <see cref="FollowShip1"/>).</summary>
            public HullConfig Config;
            /// <summary>Ship 2 to 6 goes back to following ship 1's loadout.</summary>
            public bool FollowShip1;
            /// <summary>
            /// 0.8.0 on: the loadout as the player last saw it from the host, so the host makes only the player's own
            /// changes. Without it (0.7.0 and older) the whole loadout replaces the host's.
            /// </summary>
            public HullConfig Base;
        }

        /// <summary>A message from the host: its number, when it was sent (Photon's server time, in ms) and its lines.</summary>
        internal sealed class RoomNotice
        {
            public int N;
            public int T;
            public string Text;
            /// <summary>Shown straight away (in the hub), rather than once the mission has loaded.</summary>
            public bool Now;
        }

        private static readonly Dictionary<int, float> FirstSeen = new Dictionary<int, float>();
        private static int _noticeNumber;
        private static string _lastNoticeJson;
        private static string _pendingNotice;
        private static string _room;
        /// <summary>Each player ship's CUSTOM choice as last read from the room (index 1 to CustomLoadouts.MaxShips).</summary>
        private static readonly string[] LastCustomJson = new string[CustomLoadouts.MaxShips + 1];
        /// <summary>Host: each player ship's CUSTOM choice as this host last sent it to the room.</summary>
        private static readonly string[] PublishedCustom = new string[CustomLoadouts.MaxShips + 1];
        private static string _taggedRoom;
        private static float _taggedAt;
        private static bool _wasHost;
        private static int _hostActor = -1;
        private static bool _fleetWasRunning;
        private static float _fleetFlagSentAt = -100f;

        // The host: the room it shares its CUSTOM loadouts with, and the changes it has read, by player and property.
        private static string _sharedRoom;
        private static readonly Dictionary<string, string> SeenEdits = new Dictionary<string, string>();
        /// <summary>Ship types whose loadouts are still to be shared with the room (see <see cref="SendNextShare"/>).</summary>
        private static readonly Queue<GUIDUnion> SharesToSend = new Queue<GUIDUnion>();
        private static readonly Dictionary<string, int> AppliedEdits = new Dictionary<string, int>();

        // Other players: the host's shared loadouts as last read, and the number of this player's latest change to each.
        private static readonly Dictionary<string, string> SeenLayouts = new Dictionary<string, string>();
        private static readonly Dictionary<string, int> SentEdits = new Dictionary<string, int>();
        private static int _editNumber;

        /// <summary>The room property with a player ship's CUSTOM choice: SC_Custom for ship 1, SC_Custom2 to SC_Custom6 for Fleet's.</summary>
        public static string CustomKeyOf(int ship)
        {
            return ship <= 1 ? CustomKey : CustomKey + ship;
        }

        /// <summary>Host: a player ship now uses this CUSTOM loadout (not sent again when it is what this host last sent).</summary>
        public static void PublishCustom(int ship, HullInfo hull, HullConfig config)
        {
            if (!CustomLoadouts.ValidShip(ship) || !PhotonNetwork.IsMasterClient) return;
            string json = JsonConvert.SerializeObject(new RoomCustom { Ship = hull.ShipGuid.AsHex(), Config = config }, Formatting.None);
            if (PublishedCustom[ship] == json) return;
            PublishedCustom[ship] = json;
            Set(CustomKeyOf(ship), json);
        }

        /// <summary>Host: a player ship uses a game loadout (or none).</summary>
        public static void ClearCustom(int ship)
        {
            if (!CustomLoadouts.ValidShip(ship)) return;
            PublishedCustom[ship] = null;
            Set(CustomKeyOf(ship), null);
        }

        /// <summary>Host: whether the mission is a CUSTOM run, and (once known) its session id.</summary>
        public static void SetRun(bool customRun, string sessionId)
        {
            Set(new Hashtable { { RunKey, customRun }, { RunIdKey, customRun && !string.IsNullOrEmpty(sessionId) ? sessionId : null } });
        }

        /// <summary>Host: a message (one or more lines) for every player with the mod, the host included, once in the mission.</summary>
        public static void PublishNotice(string text, bool now = false)
        {
            var notice = new RoomNotice { N = ++_noticeNumber, T = PhotonNetwork.ServerTimestamp, Text = text, Now = now };
            Set(NoticeKey, JsonConvert.SerializeObject(notice, Formatting.None));
        }

        /// <summary>Host: the last message is done with.</summary>
        public static void ClearNotice()
        {
            object value;
            if (PhotonNetwork.IsMasterClient && TryGet(NoticeKey, out value) && value != null) Set(NoticeKey, null);
        }

        /// <summary>
        /// Everyone: a new message from the host is kept until this player is in the mission with the ship spawned (not
        /// in the hub, not while loading), then shown in the chat. Old ones (from before this player joined) are skipped.
        /// </summary>
        private static void ReadNotice()
        {
            object value;
            TryGet(NoticeKey, out value);
            string json = value as string;
            if (json != _lastNoticeJson)
            {
                _lastNoticeJson = json;
                _pendingNotice = null;
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        var notice = JsonConvert.DeserializeObject<RoomNotice>(json);
                        if (notice != null && !string.IsNullOrEmpty(notice.Text) && unchecked(PhotonNetwork.ServerTimestamp - notice.T) < NoticeMaxAgeMs)
                        {
                            if (notice.Now)
                            {
                                foreach (string line in notice.Text.Split('\n'))
                                {
                                    if (line.Length > 0) Notice.Show(line);
                                }
                            }
                            else
                            {
                                _pendingNotice = notice.Text;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Failure("Reading the host's message", e);
                    }
                }
            }
            if (_pendingNotice == null || GameSessionManager.InHub || ClientGame.Current == null || ClientGame.Current.PlayerShip == null) return;
            string text = _pendingNotice;
            _pendingNotice = null;
            foreach (string line in text.Split('\n'))
            {
                if (line.Length > 0) Notice.Show(line);
            }
        }

        private static void Set(string key, object value)
        {
            Set(new Hashtable { { key, value } });
        }

        private static void Set(Hashtable properties)
        {
            try
            {
                if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return;
                PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
            }
            catch (Exception e)
            {
                Log.Failure("Updating the room's ship customizer info", e);
            }
        }

        // ---------------------------------------------------------------- the host's CUSTOM loadouts, shared

        public static string LayoutKey(int ship, GUIDUnion hullGuid)
        {
            return LayoutKeyStart + ship + "_" + hullGuid.AsHex();
        }

        /// <summary>Host: whether its CUSTOM loadouts are shared with this room (once a player who can change them is in it).</summary>
        public static bool SharingLayouts
        {
            get { return _sharedRoom != null && _sharedRoom == _room && PhotonNetwork.IsMasterClient; }
        }

        /// <summary>
        /// Host: shares its CUSTOM loadouts of one ship type: only <paramref name="ship"/>'s when it names the one that
        /// changed, otherwise every player ship's (ships 2 to 6 have none while they follow ship 1's). <paramref name="by"/>
        /// and <paramref name="n"/> name the player's change this comes from (0 for the host's own).
        /// </summary>
        public static void PublishLayouts(HullInfo hull, int by, int n, int ship = 0, List<string> refused = null)
        {
            if (!SharingLayouts) return;
            var properties = new Hashtable();
            for (int s = 1; s <= CustomLoadouts.MaxShips; s++)
            {
                if (ship > 0 && s != ship) continue;
                // Ships 2 to 6 only exist with Void Crew Fleet running.
                if (s >= 2 && !FleetLink.Running) break;
                HullConfig layout = s == 1 || LoadoutStore.HasOwn(hull, s) ? LoadoutStore.Get(hull, s) : null;
                List<string> refusedHere = s == ship && refused != null && refused.Count > 0 ? refused : null;
                // A ship that follows ship 1 has no layout to share, but the answer to a player's change must still reach
                // them: then the layout is shared empty, with the change's number and any refusal (their terminal stops
                // showing the change it sent and follows ship 1 again).
                properties[LayoutKey(s, hull.ShipGuid)] = layout != null || (s == ship && by != 0) ? LayoutJson(layout, by, n, refusedHere) : null;
            }
            if (properties.Count > 0) Set(properties);
        }

        private static string LayoutJson(HullConfig config, int by, int n, List<string> refused)
        {
            var layout = new RoomLayout { Config = config, By = by, N = n, Refused = refused != null && refused.Count > 0 ? refused : null };
            return JsonConvert.SerializeObject(layout, Formatting.None);
        }

        /// <summary>
        /// Host: the first time a player who can change the CUSTOM loadouts is in the room (protocol 2), share every ship
        /// type's. Until then nothing is shared, so a host playing alone or with older versions sends nothing extra.
        /// </summary>
        private static void ShareLayoutsWhenNeeded(string room)
        {
            if (_sharedRoom == room || !CustomLoadouts.Available) return;
            bool someoneCanEdit = false;
            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerListOthers)
            {
                if (ProtocolOf(player) >= SharedProtocol) someoneCanEdit = true;
            }
            if (!someoneCanEdit) return;
            _sharedRoom = room;
            SharesToSend.Clear();
            foreach (GUIDUnion guid in Catalog.ShipGuids()) SharesToSend.Enqueue(guid);
            Log.Info("Other players with Ship Customizer can now choose and change your CUSTOM loadouts; their changes are saved on your PC.");
        }

        /// <summary>
        /// Host: shares the next ship type's loadouts, one per poll, so reading each ship (the first time) is spread out
        /// instead of stalling one frame.
        /// </summary>
        private static void SendNextShare()
        {
            if (SharesToSend.Count == 0 || !SharingLayouts) return;
            HullInfo hull;
            if (Catalog.TryGetHull(SharesToSend.Dequeue(), out hull)) PublishLayouts(hull, 0, 0);
        }

        /// <summary>Host: reads the players' latest changes and saves the new ones, in the order each player made them.</summary>
        private static void ApplyEdits(string room)
        {
            Photon.Realtime.Player me = PhotonNetwork.LocalPlayer;
            if (me == null || !CustomLoadouts.Available) return;
            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerListOthers)
            {
                Hashtable properties = player.CustomProperties;
                if (properties == null) continue;
                var edits = new List<KeyValuePair<string, RoomEdit>>();
                foreach (object rawKey in properties.Keys.ToList())
                {
                    var key = rawKey as string;
                    var json = properties[rawKey] as string;
                    if (key == null || string.IsNullOrEmpty(json) || !key.StartsWith(EditKeyStart, StringComparison.Ordinal)) continue;
                    string seenKey = player.ActorNumber + "/" + key;
                    string seen;
                    if (SeenEdits.TryGetValue(seenKey, out seen) && seen == json) continue;
                    SeenEdits[seenKey] = json;
                    if (json.Length > MaxEditLength)
                    {
                        Log.WarnOnce("edit size " + seenKey, "Ignored a change from " + NameOf(player) + ": it is too big to be a CUSTOM loadout.");
                        continue;
                    }
                    try
                    {
                        var edit = JsonConvert.DeserializeObject<RoomEdit>(json);
                        int applied;
                        if (edit == null || edit.Room != room || edit.To != me.ActorNumber) continue;
                        if (AppliedEdits.TryGetValue(seenKey, out applied) && edit.N <= applied) continue;
                        // Void Crew Fleet battle: only a ship's own team may change it (odd ships BLUE, even ships RED).
                        if (!FleetLink.CanEditShip(player.ActorNumber, edit.Ship))
                        {
                            Log.Info("Ignored " + NameOf(player) + "'s change to ship " + edit.Ship + "'s CUSTOM loadout: it belongs to the other team.");
                            // Tell the player (their terminal already shows the change), and show them the loadout as it is.
                            AppliedEdits[seenKey] = edit.N;
                            CustomLoadouts.RefuseEditAsHost(player.ActorNumber, edit, "that ship belongs to the other team");
                            continue;
                        }
                        edits.Add(new KeyValuePair<string, RoomEdit>(seenKey, edit));
                    }
                    catch (Exception e)
                    {
                        Log.Failure("Reading a CUSTOM loadout change from " + NameOf(player), e);
                    }
                }
                foreach (KeyValuePair<string, RoomEdit> edit in edits.OrderBy(e => e.Value.N))
                {
                    AppliedEdits[edit.Key] = edit.Value.N;
                    try
                    {
                        CustomLoadouts.ApplyEditAsHost(NameOf(player), player.ActorNumber, edit.Value);
                    }
                    catch (Exception e)
                    {
                        Log.Failure("Saving a CUSTOM loadout change from " + NameOf(player), e);
                    }
                }
            }
        }

        /// <summary>
        /// A player other than the host: asks the host to save this change to its CUSTOM loadout and share it. The change
        /// is the whole loadout, so if two players change the same one at once, the later change wins.
        /// </summary>
        public static bool SendEdit(HullInfo hull, int ship, HullConfig config, bool followShip1, HullConfig seen)
        {
            Photon.Realtime.Player host = PhotonNetwork.MasterClient;
            Photon.Realtime.Player me = PhotonNetwork.LocalPlayer;
            if (host == null || me == null || host.IsLocal || PhotonNetwork.CurrentRoom == null) return false;
            var edit = new RoomEdit
            {
                Room = PhotonNetwork.CurrentRoom.Name,
                To = host.ActorNumber,
                N = ++_editNumber,
                Hull = hull.ShipGuid.AsHex(),
                Ship = ship,
                Config = followShip1 ? null : config,
                FollowShip1 = followShip1,
                Base = followShip1 ? null : seen
            };
            try
            {
                string target = LayoutKey(ship, hull.ShipGuid);
                me.SetCustomProperties(new Hashtable { { EditKeyStart + target.Substring(LayoutKeyStart.Length), JsonConvert.SerializeObject(edit, Formatting.None) } });
                SentEdits[target] = edit.N;
                return true;
            }
            catch (Exception e)
            {
                Log.Failure("Sending your change to the host", e);
                return false;
            }
        }

        /// <summary>This player's change to one of the host's loadouts (named like its room property) is done with.</summary>
        private static void ClearOwnEdit(string layoutKey)
        {
            SentEdits.Remove(layoutKey);
            Photon.Realtime.Player me = PhotonNetwork.LocalPlayer;
            if (me == null) return;
            try
            {
                me.SetCustomProperties(new Hashtable { { EditKeyStart + layoutKey.Substring(LayoutKeyStart.Length), null } });
            }
            catch (Exception e)
            {
                Log.Verbose("Could not clear your answered CUSTOM change: " + e.Message);
            }
        }

        private static void ForgetShared()
        {
            _sharedRoom = null;
            SharesToSend.Clear();
            SeenEdits.Clear();
            AppliedEdits.Clear();
            SeenLayouts.Clear();
            SentEdits.Clear();
        }

        /// <summary>Clears this player's changes meant for the room it just left (Photon would carry them into the next one).</summary>
        private static void ClearOwnEdits()
        {
            Photon.Realtime.Player me = PhotonNetwork.LocalPlayer;
            if (me == null || me.CustomProperties == null) return;
            var cleared = new Hashtable();
            foreach (object key in me.CustomProperties.Keys)
            {
                var name = key as string;
                if (name != null && name.StartsWith(EditKeyStart, StringComparison.Ordinal)) cleared[name] = null;
            }
            if (cleared.Count == 0) return;
            try
            {
                me.SetCustomProperties(cleared);
            }
            catch (Exception e)
            {
                Log.Verbose("Could not clear your old CUSTOM loadout changes: " + e.Message);
            }
        }

        /// <summary>
        /// Other players: reads the host's shared CUSTOM loadouts when they change. A loadout that still lacks this
        /// player's own later change is skipped, so the terminal doesn't jump back while that change is on its way.
        /// </summary>
        private static void ReadHostLayouts()
        {
            Hashtable properties = PhotonNetwork.CurrentRoom.CustomProperties;
            if (properties == null) return;
            int me = PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : -1;
            bool fleet = FleetLink.Running;
            bool changed = false;
            var present = new HashSet<string>();
            foreach (object rawKey in properties.Keys.ToList())
            {
                var key = rawKey as string;
                int ship;
                GUIDUnion hullGuid;
                // Ships 2 to 6 are only for Void Crew Fleet; without it running (here and on the host) they are ignored.
                if (!TryParseLayoutKey(key, out ship, out hullGuid) || (ship >= 2 && !fleet)) continue;
                present.Add(key);
                var json = properties[rawKey] as string;
                string seen;
                if (SeenLayouts.TryGetValue(key, out seen) && seen == json) continue;
                SeenLayouts[key] = json;
                try
                {
                    RoomLayout layout = string.IsNullOrEmpty(json) ? null : JsonConvert.DeserializeObject<RoomLayout>(json);
                    // What the host has now is what this player's next change is measured against, shown or not.
                    CustomLoadouts.SetConfirmedLayout(ship, hullGuid, layout != null ? layout.Config : null);
                    int sent;
                    bool mine = layout != null && layout.By == me;
                    if (mine && SentEdits.TryGetValue(key, out sent))
                    {
                        if (layout.N < sent) continue;
                        // The host has answered this player's latest change here: it needn't be sent to everyone who joins.
                        ClearOwnEdit(key);
                    }
                    CustomLoadouts.SetHostLayout(ship, hullGuid, layout != null ? layout.Config : null);
                    changed = true;
                    if (layout == null) continue;
                    if (mine && layout.Refused != null && layout.Refused.Count > 0) TerminalUi.Toast(RefusedMessage(layout.Refused));
                    // (An empty layout only carries a refusal for its player: nothing changed.)
                    else if (!mine && layout.Config != null) CustomLoadouts.OnChangedByOther(NameOfActor(layout.By), ship, hullGuid);
                }
                catch (Exception e)
                {
                    Log.Failure("Reading the host's CUSTOM loadouts", e);
                }
            }
            foreach (string key in SeenLayouts.Keys.Where(k => !present.Contains(k)).ToList())
            {
                SeenLayouts.Remove(key);
                int ship;
                GUIDUnion hullGuid;
                if (!TryParseLayoutKey(key, out ship, out hullGuid)) continue;
                // Gone from the room (the ship follows ship 1 again): the next change is measured against that.
                CustomLoadouts.SetConfirmedLayout(ship, hullGuid, null);
                CustomLoadouts.SetHostLayout(ship, hullGuid, null);
                changed = true;
            }
            if (changed) TerminalUi.OnLoadoutChangedRemotely();
        }

        private static bool TryParseLayoutKey(string key, out int ship, out GUIDUnion hullGuid)
        {
            ship = 0;
            hullGuid = GUIDUnion.Empty();
            if (key == null || key.Length < LayoutKeyStart.Length + 3 || !key.StartsWith(LayoutKeyStart, StringComparison.Ordinal)) return false;
            ship = key[LayoutKeyStart.Length] - '0';
            if (ship < 1 || ship > CustomLoadouts.MaxShips || key[LayoutKeyStart.Length + 1] != '_') return false;
            try
            {
                hullGuid = new GUIDUnion(key.Substring(LayoutKeyStart.Length + 2));
            }
            catch
            {
                return false;
            }
            return !hullGuid.IsNull;
        }

        public static bool TryGet(string key, out object value)
        {
            value = null;
            return PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null && PhotonNetwork.CurrentRoom.CustomProperties != null &&
                   PhotonNetwork.CurrentRoom.CustomProperties.TryGetValue(key, out value);
        }

        // ---------------------------------------------------------------- who has the mod

        /// <summary>
        /// The other players in the room without this mod (or with an older one). With <paramref name="giveJoinersTime"/>,
        /// players who joined in the last <see cref="JoinGraceSeconds"/> seconds are not counted yet.
        /// </summary>
        public static List<string> PlayersWithoutMod(bool giveJoinersTime)
        {
            return PlayersOlderThan(OldestProtocol, giveJoinersTime);
        }

        /// <summary>
        /// The other players whose Ship Customizer doesn't know ships 3 to 6 (older than 0.9.0, or none): CUSTOM on those
        /// ships needs everyone to know them.
        /// </summary>
        public static List<string> PlayersWithoutManyShips(bool giveJoinersTime)
        {
            return PlayersOlderThan(ManyShipsProtocol, giveJoinersTime);
        }

        private static List<string> PlayersOlderThan(int protocol, bool giveJoinersTime)
        {
            var missing = new List<string>();
            if (!PhotonNetwork.InRoom) return missing;
            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerList)
            {
                if (player == null || player.IsLocal) continue;
                if (ProtocolOf(player) >= protocol) continue;
                float seen;
                if (giveJoinersTime && FirstSeen.TryGetValue(player.ActorNumber, out seen) && Time.unscaledTime - seen < JoinGraceSeconds) continue;
                missing.Add(NameOf(player));
            }
            return missing;
        }

        /// <summary>A player other than the host: whether the host's Ship Customizer knows ships 3 to 6 (0.9.0 on).</summary>
        public static bool HostKnowsManyShips()
        {
            return PhotonNetwork.InRoom && (PhotonNetwork.IsMasterClient || ProtocolOf(PhotonNetwork.MasterClient) >= ManyShipsProtocol);
        }

        /// <summary>
        /// Whether the room's host runs Void Crew Fleet, so Fleet's features may be used here: always true for the host itself
        /// and outside a room. A host on Ship Customizer 0.9.0 or newer says so in the room (false until it has); an older
        /// host (or one without Ship Customizer) can't, and then Fleet's own checks decide, as before.
        /// </summary>
        public static bool HostRunsFleet()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient) return true;
            if (ProtocolOf(PhotonNetwork.MasterClient) < ManyShipsProtocol) return true;
            object value;
            return TryGet(FleetKey, out value) && value is bool && (bool)value;
        }

        /// <summary>Whether the player with this actor number has the mod (this player always does).</summary>
        public static bool PlayerHasMod(int actorNumber)
        {
            if (!PhotonNetwork.InRoom) return false;
            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerList)
            {
                if (player != null && player.ActorNumber == actorNumber) return player.IsLocal || ProtocolOf(player) >= OldestProtocol;
            }
            return false;
        }

        /// <summary>
        /// A player other than the host: whether the host lets every player with the mod choose and change the CUSTOM
        /// loadouts (protocol 2, Ship Customizer 0.5.0 or newer). Older hosts keep CUSTOM to themselves.
        /// </summary>
        public static bool HostSharesLayouts()
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.IsMasterClient) return false;
            return ProtocolOf(PhotonNetwork.MasterClient) >= SharedProtocol;
        }

        /// <summary>From this host protocol on (0.8.0), the host writes and clears <see cref="RunIdKey"/>.</summary>
        public const int RunIdProtocol = 3;

        /// <summary>The host's protocol (0 without the mod, or outside a room); the host itself is always on this version's.</summary>
        public static int HostProtocol()
        {
            if (!PhotonNetwork.InRoom) return 0;
            return PhotonNetwork.IsMasterClient ? Protocol : ProtocolOf(PhotonNetwork.MasterClient);
        }

        /// <summary>A player's protocol from their "I have the Ship Customizer" property; 0 without the mod.</summary>
        private static int ProtocolOf(Photon.Realtime.Player player)
        {
            object value;
            return player != null && player.CustomProperties != null && player.CustomProperties.TryGetValue(PlayerKey, out value) && value is int ? (int)value : 0;
        }

        /// <summary>A message for a player whose change was partly or wholly not made.</summary>
        private static string RefusedMessage(List<string> refused)
        {
            return "Not changed, because someone else changed it just before you: " + string.Join(", ", refused.ToArray()) + ". You see their choice now.";
        }

        /// <summary>A player's name by actor number; the host's own changes are made under 0.</summary>
        public static string NameOfActor(int actor)
        {
            if (!PhotonNetwork.InRoom) return "someone";
            Photon.Realtime.Player player = actor == 0 ? PhotonNetwork.MasterClient : PhotonNetwork.PlayerList.FirstOrDefault(p => p != null && p.ActorNumber == actor);
            return player != null ? NameOf(player) : "someone";
        }

        private static string NameOf(Photon.Realtime.Player player)
        {
            return string.IsNullOrEmpty(player.NickName) ? "player " + player.ActorNumber : player.NickName;
        }

        private static void TrackPlayers()
        {
            var present = new HashSet<int>();
            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerList)
            {
                if (player == null) continue;
                present.Add(player.ActorNumber);
                if (!FirstSeen.ContainsKey(player.ActorNumber)) FirstSeen[player.ActorNumber] = Time.unscaledTime;
            }
            var gone = new List<int>();
            foreach (int actor in FirstSeen.Keys)
            {
                if (!present.Contains(actor)) gone.Add(actor);
            }
            foreach (int actor in gone) FirstSeen.Remove(actor);
        }

        /// <summary>Tells the room this player has the mod (again after a few seconds if it didn't stick).</summary>
        private static void TagLocalPlayer(string room)
        {
            Photon.Realtime.Player me = PhotonNetwork.LocalPlayer;
            if (me == null) return;
            object value;
            bool tagged = me.CustomProperties != null && me.CustomProperties.TryGetValue(PlayerKey, out value) && value is int && (int)value == Protocol;
            if (tagged || (_taggedRoom == room && Time.unscaledTime - _taggedAt < 5f)) return;
            _taggedRoom = room;
            _taggedAt = Time.unscaledTime;
            me.SetCustomProperties(new Hashtable { { PlayerKey, Protocol } });
        }

        // ---------------------------------------------------------------- closing the room during CUSTOM missions

        /// <summary>
        /// Host: nobody can join a CUSTOM mission, because a player without the mod would earn progression in it.
        /// The game itself never closes rooms, so the room is simply opened again back in the hub.
        /// </summary>
        public static void CloseForCustomMission()
        {
            if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return;
            bool wasOpen = PhotonNetwork.CurrentRoom.IsOpen;
            PhotonNetwork.CurrentRoom.IsOpen = false;
            Set(ClosedKey, true);
            if (wasOpen) Log.Info("The room is closed to new players until the CUSTOM mission is over.");
        }

        /// <summary>Host, back in the hub: open the room again if a CUSTOM mission closed it.</summary>
        public static void ReopenAfterCustomMission()
        {
            object closed;
            if (!PhotonNetwork.IsMasterClient || !TryGet(ClosedKey, out closed) || !(closed is bool) || !(bool)closed) return;
            PhotonNetwork.CurrentRoom.IsOpen = true;
            Set(ClosedKey, null);
            Log.Info("The CUSTOM mission is over; the room is open to new players again.");
        }

        // ---------------------------------------------------------------- polling

        /// <summary>Called a few times a second once the hub has loaded.</summary>
        public static void Poll()
        {
            string room = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : null;
            if (room != _room)
            {
                ClearOwnEdits();
                _room = room;
                Array.Clear(LastCustomJson, 0, LastCustomJson.Length);
                Array.Clear(PublishedCustom, 0, PublishedCustom.Length);
                _lastNoticeJson = null;
                _pendingNotice = null;
                FirstSeen.Clear();
                ForgetShared();
                _hostActor = -1;
                ProgressionGuard.OnRoomChanged();
                CustomLoadouts.OnRoomChanged();
            }
            if (room == null) return;

            TrackPlayers();
            // The host's messages are for the mission, so they are read there too.
            ReadNotice();
            // Only once in the hub: the host checks for the tag as soon as players are able to send it.
            if (HubShipManager.Instance == null) return;
            // A player whose CUSTOM patches failed can't pause their progression, so they don't count as having the mod.
            if (CustomLoadouts.Available) TagLocalPlayer(room);

            bool host = PhotonNetwork.IsMasterClient;
            int hostActor = PhotonNetwork.MasterClient != null ? PhotonNetwork.MasterClient.ActorNumber : -1;
            if (host != _wasHost || hostActor != _hostActor)
            {
                // A new host (this player, or another one) shares its own CUSTOM loadouts and choices; whatever was shared,
                // sent or read before is forgotten and read again from the new host.
                _wasHost = host;
                _hostActor = hostActor;
                ForgetShared();
                Array.Clear(LastCustomJson, 0, LastCustomJson.Length);
                Array.Clear(PublishedCustom, 0, PublishedCustom.Length);
                _fleetFlagSentAt = -100f;
                CustomLoadouts.ForgetHostLayouts();
                TerminalUi.OnLoadoutChangedRemotely();
            }

            if (host) PublishFleetFlag();
            // Void Crew Fleet's features come and go with the host (a new host, or its word arriving): show what applies now.
            bool fleet = FleetLink.Running;
            if (fleet != _fleetWasRunning)
            {
                _fleetWasRunning = fleet;
                CustomLoadouts.OnFleetRunningChanged(fleet);
            }

            if (host)
            {
                ShareLayoutsWhenNeeded(room);
                SendNextShare();
                ApplyEdits(room);
                EnforceEveryoneHasMod();
                return;
            }

            // A host without the mod (or older than 0.5.0) shares nothing: what an earlier host left in the room is stale.
            bool hostHasMod = ProtocolOf(PhotonNetwork.MasterClient) >= OldestProtocol;
            if (HostSharesLayouts()) ReadHostLayouts();
            for (int ship = 1; ship <= CustomLoadouts.MaxShips; ship++)
            {
                // Ships 2 to 6 (Void Crew Fleet): only while Fleet runs here and on the host; otherwise they read as none.
                object value;
                TryGet(CustomKeyOf(ship), out value);
                string json = hostHasMod && (ship == 1 || fleet) ? value as string : null;
                if (json == LastCustomJson[ship]) continue;
                LastCustomJson[ship] = json;
                ApplyHostCustom(json, ship);
            }
        }

        /// <summary>
        /// Host: tells the room whether it runs Void Crew Fleet (players with Fleet only use it while it does), again after a
        /// few seconds if it didn't stick (the room only shows a change once the server has it).
        /// </summary>
        private static void PublishFleetFlag()
        {
            bool fleet = FleetLink.RunningHere;
            object value;
            if (TryGet(FleetKey, out value) && value is bool && (bool)value == fleet) return;
            if (Time.unscaledTime - _fleetFlagSentAt < 5f) return;
            _fleetFlagSentAt = Time.unscaledTime;
            Set(FleetKey, fleet);
        }

        /// <summary>
        /// Host, in the hub: CUSTOM and saved CUSTOM runs are only allowed while everyone in the room has the mod. If
        /// someone without it joins, the room goes back to the stand-in loadout (no one is kicked).
        /// </summary>
        private static void EnforceEveryoneHasMod()
        {
            List<int> otherShips = Enumerable.Range(2, CustomLoadouts.MaxShips - 1).Where(s => !CustomLoadouts.ActiveShipOf(s).IsNull).ToList();
            if (!CustomLoadouts.IsActive && otherShips.Count == 0 && !SavedCustomRunSelected()) return;
            List<string> missing = PlayersWithoutMod(true);
            if (missing.Count == 0)
            {
                // Ships 3 to 6 also need everyone on 0.9.0 or newer (older players can't see their CUSTOM loadouts).
                List<int> manyShips = otherShips.Where(s => s >= 3).ToList();
                if (manyShips.Count == 0) return;
                List<string> older = PlayersWithoutManyShips(true);
                if (older.Count == 0) return;
                CancelShips(manyShips);
                PublishNotice("CUSTOM was switched off on " + ShipNames(manyShips) + ": " + string.Join(", ", older.ToArray()) +
                              " joined with a Ship Customizer older than 0.9.0, and ships 3 to 6 need it.", true);
                return;
            }
            string who = string.Join(", ", missing.ToArray());
            // One message with every line: each new message replaces the one before.
            var notices = new List<string>();

            if (CustomLoadouts.IsActive)
            {
                HullInfo hull;
                if (Catalog.TryGetHull(CustomLoadouts.ActiveShip, out hull) && HubShipManager.Instance != null)
                {
                    HubShipManager.Instance.SelectShip(hull.BasePreset.AssetGuid.AsIntArray());
                }
                else
                {
                    CustomLoadouts.DeactivateAsHost();
                }
                notices.Add("CUSTOM was switched off: " + who + " joined without Ship Customizer. Everyone needs it for CUSTOM.");
            }
            if (otherShips.Count > 0)
            {
                CancelShips(otherShips);
                notices.Add("CUSTOM was switched off on " + ShipNames(otherShips) + ": " + who + " joined without Ship Customizer.");
            }
            if (SavedCustomRunSelected() && HubQuestManager.Instance != null)
            {
                HubQuestManager.Instance.SelectQuest(GUIDUnion.Empty());
                notices.Add("The saved CUSTOM run was unselected: " + who + " joined without Ship Customizer.");
            }
            if (notices.Count > 0) PublishNotice(string.Join("\n", notices.ToArray()), true);
        }

        /// <summary>Host: Void Crew Fleet's ships lose their CUSTOM loadout (Fleet gives them CUSTOM's stand-in).</summary>
        private static void CancelShips(IEnumerable<int> ships)
        {
            foreach (int ship in ships)
            {
                CustomLoadouts.DeactivateShipAsHost(ship);
                ShipCustomizerApi.RaiseShipCustomCancelled(ship);
            }
        }

        /// <summary>"SHIP 2, SHIP 3" or "BLUE 2, RED 2".</summary>
        private static string ShipNames(IEnumerable<int> ships)
        {
            return string.Join(", ", ships.Select(FleetLink.ShipName).ToArray());
        }

        /// <summary>Whether the room is set to continue a saved run that was started with a CUSTOM loadout.</summary>
        public static bool SavedCustomRunSelected()
        {
            try
            {
                if (HubQuestManager.Instance == null || HubQuestManager.Instance.QuestStartType != HubQuestManager.SessionStartType.Preserved) return false;
                PreservedGameSession saved = PlayerProfile.Instance.PreservedSession.PreservedSession;
                return saved != null && LoadoutStore.WasCustomRun(saved.GameSessionID);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Other players: a ship's CUSTOM choice as the host shares it. The host shares it again with every change to that
        /// loadout, so when only the loadout changed, only that ship type is drawn again; the room's choice is only told
        /// around the hub again (which rebuilds the hub's ship hologram) when ship 1's choice itself changed.
        /// </summary>
        private static void ApplyHostCustom(string json, int ship)
        {
            GUIDUnion before = CustomLoadouts.ActiveShipOf(ship);
            GUIDUnion hullGuid = GUIDUnion.Empty();
            HullConfig config = null;
            HullInfo hull = null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var custom = JsonConvert.DeserializeObject<RoomCustom>(json);
                    var guid = new GUIDUnion(custom.Ship);
                    ShipLoadoutDataDef def;
                    // Only the game's ships: a made-up GUID must not make the catalog try to read a ship that isn't there.
                    if (Catalog.ShipGuids().Contains(guid) && Catalog.TryGetHull(guid, out hull) && CustomLoadouts.TryGetDef(guid, out def))
                    {
                        hullGuid = guid;
                        config = custom.Config ?? new HullConfig();
                    }
                    else
                    {
                        hull = null;
                    }
                }
                catch (Exception e)
                {
                    hull = null;
                    Log.Failure("Reading the host's CUSTOM loadout", e);
                }
            }
            CustomLoadouts.SetRemoteChoice(ship, hullGuid, config);
            if (hull != null) CustomLoadouts.Refresh(hull);
            if (hullGuid == before)
            {
                TerminalUi.OnLoadoutChangedRemotely();
                return;
            }
            HullInfo previous;
            if (!before.IsNull && Catalog.TryGetHull(before, out previous)) CustomLoadouts.Refresh(previous);
            if (hull != null) Log.Info("The host chose a CUSTOM " + hull.Name + (ship >= 2 ? " for " + FleetLink.ShipName(ship) : "") + ": " + CustomLoadouts.Describe(config) + ".");
            if (ship == 1) TerminalUi.OnSelectionChangedRemotely();
            else TerminalUi.OnLoadoutChangedRemotely();
        }
    }
}
