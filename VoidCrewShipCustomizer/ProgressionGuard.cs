using System;
using Photon.Pun;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// The Void Crew modding rules say mods that work like cheats or a sandbox must turn progression off. A run started
    /// with a CUSTOM loadout therefore earns no XP, rewards, quest progress or achievements for players with this mod,
    /// from the moment the mission starts until they are back in the hub. Players without the mod can't be covered, but
    /// the room is marked [mod_session] in the server list so they know before joining.
    /// </summary>
    internal static class ProgressionGuard
    {
        private static bool _hostRun;
        private static bool _hostRunAnnounced;
        private static bool _clientRun;
        private static string _rememberedRun;

        public static bool Active
        {
            get { return _hostRun || _clientRun; }
        }

        /// <summary>Host: the mission now starting does (or doesn't) use a CUSTOM loadout.</summary>
        public static void SetHostRun(bool customRun)
        {
            _hostRun = customRun;
            _hostRunAnnounced = false;
            _rememberedRun = null;
            RoomSync.SetRun(customRun);
            if (customRun) Log.Info("Progression is paused for this run because it uses a CUSTOM loadout (no XP, rewards, quest progress or achievements).");
        }

        /// <summary>Back in the hub: progression is on again.</summary>
        public static void OnHubLoaded()
        {
            if (Active) Log.Info("Back in the hub: progression is on again.");
            _hostRun = false;
            _clientRun = false;
        }

        /// <summary>Called a few times a second.</summary>
        public static void Poll()
        {
            bool inHub;
            GameSession session;
            try
            {
                if (GameSessionManager.Instance == null) return;
                inHub = GameSessionManager.InHub;
                session = GameSessionManager.ActiveSession;
            }
            catch
            {
                return;
            }
            if (inHub || session == null) return;

            if (PhotonNetwork.IsMasterClient)
            {
                // Remember the run, so continuing it from a save later keeps progression paused.
                if (_hostRun && !string.IsNullOrEmpty(session.GameSessionID) && session.GameSessionID != _rememberedRun)
                {
                    _rememberedRun = session.GameSessionID;
                    LoadoutStore.RememberCustomRun(session.GameSessionID);
                }
                // Keep the room flag right for players who join during the mission.
                if (_hostRun && !_hostRunAnnounced)
                {
                    _hostRunAnnounced = true;
                    RoomSync.SetRun(true);
                }
                return;
            }

            object value;
            if (!_clientRun && RoomSync.TryGet(RoomSync.RunKey, out value) && value is bool && (bool)value)
            {
                _clientRun = true;
                Log.Info("The host started this mission with a CUSTOM loadout, so your progression is paused until you are back in the hub.");
            }
        }

        /// <summary>Patch helper: true when something that counts as progression must be skipped.</summary>
        public static bool Block(string what)
        {
            if (!Active) return false;
            Log.InfoOnce("blocked " + what, "Skipped " + what + " because this run uses a CUSTOM loadout.");
            return true;
        }
    }
}
