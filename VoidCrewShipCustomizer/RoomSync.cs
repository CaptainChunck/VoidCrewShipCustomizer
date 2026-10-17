using System;
using ExitGames.Client.Photon;
using Newtonsoft.Json;
using Photon.Pun;
using ResourceAssets;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// Tells other players with the mod about the host's CUSTOM loadout through two Photon room properties. Players
    /// without the mod ignore them. The host writes; everyone else reads them a few times a second.
    /// </summary>
    internal static class RoomSync
    {
        /// <summary>JSON of the host's chosen CUSTOM loadout, absent while a game loadout is chosen.</summary>
        public const string CustomKey = "SC_Custom";

        /// <summary>True while the current mission was started with a CUSTOM loadout.</summary>
        public const string RunKey = "SC_Run";

        private sealed class RoomCustom
        {
            public string Ship;
            public HullConfig Config;
        }

        private static string _room;
        private static string _lastCustomJson;

        public static void PublishCustom(HullInfo hull, HullConfig config)
        {
            Set(CustomKey, JsonConvert.SerializeObject(new RoomCustom { Ship = hull.ShipGuid.AsHex(), Config = config }, Formatting.None));
        }

        public static void ClearCustom()
        {
            Set(CustomKey, null);
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

        /// <summary>Called a few times a second.</summary>
        public static void Poll()
        {
            string room = PhotonNetwork.InRoom && PhotonNetwork.CurrentRoom != null ? PhotonNetwork.CurrentRoom.Name : null;
            if (room != _room)
            {
                _room = room;
                _lastCustomJson = null;
                CustomLoadouts.OnRoomChanged();
            }
            if (room == null || PhotonNetwork.IsMasterClient) return;
            // Only the hub shows loadouts; a change seen during a mission is picked up on the way back.
            if (HubShipManager.Instance == null) return;

            object value;
            TryGet(CustomKey, out value);
            string json = value as string;
            if (json == _lastCustomJson) return;
            _lastCustomJson = json;
            ApplyHostCustom(json);
        }

        private static void ApplyHostCustom(string json)
        {
            CustomLoadouts.ActiveShip = GUIDUnion.Empty();
            CustomLoadouts.RemoteConfig = null;
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var custom = JsonConvert.DeserializeObject<RoomCustom>(json);
                    var ship = new GUIDUnion(custom.Ship);
                    HullInfo hull;
                    ShipLoadoutDataDef def;
                    if (Catalog.TryGetHull(ship, out hull) && CustomLoadouts.TryGetDef(ship, out def))
                    {
                        CustomLoadouts.ActiveShip = ship;
                        CustomLoadouts.RemoteConfig = custom.Config ?? new HullConfig();
                        Log.Info("The host chose a CUSTOM " + hull.Name + ": " + CustomLoadouts.Describe(CustomLoadouts.RemoteConfig) + ".");
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
