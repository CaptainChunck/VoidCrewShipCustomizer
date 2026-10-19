using System;
using Photon.Pun;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// The Void Crew modding rules say mods that work like cheats or a sandbox must turn progression off. A run started
    /// with a CUSTOM loadout therefore earns no XP, rewards, quest progress, achievements, tracked stats or daily and
    /// weekly quests for players with this mod, from the moment the mission starts until they are back in the hub. Players
    /// without the mod can't be covered, so CUSTOM is only used while everyone in the room has it, and the room is marked
    /// [mod_session] in the server list.
    ///
    /// The host knows when it starts a CUSTOM run. Everyone else (and a player who becomes the host mid-run) learns it from
    /// the room: SC_RunId holds the CUSTOM run's session id, which every player's mission shares, so a flag left over from
    /// an earlier mission never pauses a later one. SC_Run (true / false) is kept for players on 0.7 and older.
    /// </summary>
    internal static class ProgressionGuard
    {
        private static bool _hostRun;
        private static bool _hostRunAnnounced;
        private static bool _roomRun;
        private static string _rememberedRun;

        public static bool Active
        {
            get { return _hostRun || _roomRun; }
        }

        /// <summary>Host: the mission now starting does (or doesn't) use a CUSTOM loadout.</summary>
        public static void SetHostRun(bool customRun)
        {
            bool raised = customRun && !_hostRun;
            _hostRun = customRun;
            _hostRunAnnounced = false;
            _rememberedRun = null;
            RoomSync.SetRun(customRun, null);
            if (raised) Log.Info("Progression is paused for this run because it uses a CUSTOM loadout (no XP, rewards, quest progress, stats or achievements).");
        }

        /// <summary>Back in the hub: progression is on again. The host also clears the room's flag for the next mission.</summary>
        public static void OnHubLoaded()
        {
            if (Active) Log.Info("Back in the hub: progression is on again.");
            Reset();
            if (PhotonNetwork.IsMasterClient) RoomSync.SetRun(false, null);
        }

        /// <summary>Left the room (or joined another): this room's run is none of the next one's business.</summary>
        public static void OnRoomChanged()
        {
            if (Active) Log.Info("Left the CUSTOM run's room: progression is on again.");
            Reset();
        }

        private static void Reset()
        {
            _hostRun = false;
            _roomRun = false;
            _hostRunAnnounced = false;
            _rememberedRun = null;
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

            // Everyone, the host included: a host that left mid-run hands the run over with the room.
            if (!_roomRun && RoomSaysCustomRun(session.GameSessionID))
            {
                _roomRun = true;
                Log.Info("This mission was started with a CUSTOM loadout, so your progression is paused until you are back in the hub.");
            }
            if (!PhotonNetwork.IsMasterClient || !Active) return;

            // Remember the run (the host saves it, also after taking over from a host who left), so continuing it from a
            // save later keeps progression paused.
            if (!string.IsNullOrEmpty(session.GameSessionID) && session.GameSessionID != _rememberedRun)
            {
                _rememberedRun = session.GameSessionID;
                LoadoutStore.RememberCustomRun(session.GameSessionID);
            }
            // Keep the room's flags right for players who join during the mission.
            if (!_hostRunAnnounced && !string.IsNullOrEmpty(session.GameSessionID))
            {
                _hostRunAnnounced = true;
                RoomSync.SetRun(true, session.GameSessionID);
            }
        }

        /// <summary>The room's word that this mission (by its session id) is a CUSTOM run; an older host only sets SC_Run.</summary>
        private static bool RoomSaysCustomRun(string sessionId)
        {
            object id;
            if (RoomSync.TryGet(RoomSync.RunIdKey, out id) && id is string) return !string.IsNullOrEmpty(sessionId) && (string)id == sessionId;
            object run;
            return RoomSync.TryGet(RoomSync.RunKey, out run) && run is bool && (bool)run;
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
