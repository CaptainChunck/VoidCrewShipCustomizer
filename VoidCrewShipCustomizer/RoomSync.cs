using System;
using System.Collections.Generic;
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
    /// - a player property saying "I have the Ship Customizer", which the host checks before using CUSTOM.
    /// Players without the mod ignore all of it. The host writes the room properties; everyone else reads them.
    /// </summary>
    internal static class RoomSync
    {
        /// <summary>JSON of the host's chosen CUSTOM loadout, absent while a game loadout is chosen.</summary>
        public const string CustomKey = "SC_Custom";

        /// <summary>JSON of the host's ship 2 CUSTOM loadout (Void Crew Fleet), absent otherwise.</summary>
        public const string CustomKey2 = "SC_Custom2";

        /// <summary>True while the current mission was started with a CUSTOM loadout.</summary>
        public const string RunKey = "SC_Run";

        /// <summary>True while the room is closed to new players because a CUSTOM mission is running.</summary>
        public const string ClosedKey = "SC_Closed";

        /// <summary>Player property: this player runs the Ship Customizer (the value is <see cref="Protocol"/>).</summary>
        public const string PlayerKey = "SC_Player";

        /// <summary>Raised when what players exchange changes in a way older versions don't understand.</summary>
        public const int Protocol = 1;

        /// <summary>
        /// How long a player who just joined may take to load into the hub before the host counts them as not having
        /// the mod (their "I have it" property is only sent once they are in the hub).
        /// </summary>
        private const float JoinGraceSeconds = 45f;

        private sealed class RoomCustom
        {
            public string Ship;
            public HullConfig Config;
        }

        private static readonly Dictionary<int, float> FirstSeen = new Dictionary<int, float>();
        private static string _room;
        private static string _lastCustomJson;
        private static string _lastCustomJson2;
        private static string _taggedRoom;
        private static float _taggedAt;

        public static void PublishCustom(HullInfo hull, HullConfig config)
        {
            Set(CustomKey, JsonConvert.SerializeObject(new RoomCustom { Ship = hull.ShipGuid.AsHex(), Config = config }, Formatting.None));
        }

        public static void ClearCustom()
        {
            Set(CustomKey, null);
        }

        public static void PublishCustom2(HullInfo hull, HullConfig config)
        {
            Set(CustomKey2, JsonConvert.SerializeObject(new RoomCustom { Ship = hull.ShipGuid.AsHex(), Config = config }, Formatting.None));
        }

        public static void ClearCustom2()
        {
            Set(CustomKey2, null);
        }

        public static void SetRun(bool customRun)
        {
            Set(RunKey, customRun);
        }

        private static void Set(string key, object value)
        {
            try
            {
                if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null) return;
                PhotonNetwork.CurrentRoom.SetCustomProperties(new Hashtable { { key, value } });
            }
            catch (Exception e)
            {
                Log.Failure("Updating the room's ship customizer info", e);
            }
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
            var missing = new List<string>();
            if (!PhotonNetwork.InRoom) return missing;
            foreach (Photon.Realtime.Player player in PhotonNetwork.PlayerList)
            {
                if (player == null || player.IsLocal) continue;
                object value;
                if (player.CustomProperties != null && player.CustomProperties.TryGetValue(PlayerKey, out value) && value is int && (int)value >= Protocol) continue;
                float seen;
                if (giveJoinersTime && FirstSeen.TryGetValue(player.ActorNumber, out seen) && Time.unscaledTime - seen < JoinGraceSeconds) continue;
                missing.Add(string.IsNullOrEmpty(player.NickName) ? "player " + player.ActorNumber : player.NickName);
            }
            return missing;
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
                _room = room;
                _lastCustomJson = null;
                _lastCustomJson2 = null;
                FirstSeen.Clear();
                CustomLoadouts.OnRoomChanged();
            }
            if (room == null) return;

            TrackPlayers();
            // Only once in the hub: the host checks for the tag as soon as players are able to send it.
            if (HubShipManager.Instance == null) return;
            TagLocalPlayer(room);

            if (PhotonNetwork.IsMasterClient)
            {
                EnforceEveryoneHasMod();
                return;
            }

            object value;
            TryGet(CustomKey, out value);
            string json = value as string;
            if (json != _lastCustomJson)
            {
                _lastCustomJson = json;
                ApplyHostCustom(json, 1);
            }
            TryGet(CustomKey2, out value);
            json = value as string;
            if (json != _lastCustomJson2)
            {
                _lastCustomJson2 = json;
                ApplyHostCustom(json, 2);
            }
        }

        /// <summary>
        /// Host, in the hub: CUSTOM and saved CUSTOM runs are only allowed while everyone in the room has the mod. If
        /// someone without it joins, the room goes back to the stand-in loadout (no one is kicked).
        /// </summary>
        private static void EnforceEveryoneHasMod()
        {
            if (!CustomLoadouts.IsActive && CustomLoadouts.ActiveShip2.IsNull && !SavedCustomRunSelected()) return;
            List<string> missing = PlayersWithoutMod(true);
            if (missing.Count == 0) return;
            string who = string.Join(", ", missing.ToArray());

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
                TerminalUi.Toast("CUSTOM was switched off: " + who + " joined without Ship Customizer. Everyone needs it for CUSTOM.");
            }
            if (!CustomLoadouts.ActiveShip2.IsNull)
            {
                CustomLoadouts.DeactivateShip2AsHost();
                ShipCustomizerApi.RaiseShip2CustomCancelled();
                TerminalUi.Toast("Ship 2's CUSTOM was switched off: " + who + " joined without Ship Customizer.");
            }
            if (SavedCustomRunSelected() && HubQuestManager.Instance != null)
            {
                HubQuestManager.Instance.SelectQuest(GUIDUnion.Empty());
                TerminalUi.Toast("The saved CUSTOM run was unselected: " + who + " joined without Ship Customizer.");
            }
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

        private static void ApplyHostCustom(string json, int ship)
        {
            if (ship == 2)
            {
                CustomLoadouts.ActiveShip2 = GUIDUnion.Empty();
                CustomLoadouts.RemoteConfig2 = null;
            }
            else
            {
                CustomLoadouts.ActiveShip = GUIDUnion.Empty();
                CustomLoadouts.RemoteConfig = null;
            }
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var custom = JsonConvert.DeserializeObject<RoomCustom>(json);
                    var hullGuid = new GUIDUnion(custom.Ship);
                    HullInfo hull;
                    ShipLoadoutDataDef def;
                    if (Catalog.TryGetHull(hullGuid, out hull) && CustomLoadouts.TryGetDef(hullGuid, out def))
                    {
                        HullConfig config = custom.Config ?? new HullConfig();
                        if (ship == 2)
                        {
                            CustomLoadouts.ActiveShip2 = hullGuid;
                            CustomLoadouts.RemoteConfig2 = config;
                        }
                        else
                        {
                            CustomLoadouts.ActiveShip = hullGuid;
                            CustomLoadouts.RemoteConfig = config;
                        }
                        Log.Info("The host chose a CUSTOM " + hull.Name + (ship == 2 ? " for ship 2" : "") + ": " + CustomLoadouts.Describe(config) + ".");
                    }
                }
                catch (Exception e)
                {
                    Log.Failure("Reading the host's CUSTOM loadout", e);
                }
            }
            CustomLoadouts.RefreshAll();
            TerminalUi.OnSelectionChangedRemotely();
        }
    }
}
