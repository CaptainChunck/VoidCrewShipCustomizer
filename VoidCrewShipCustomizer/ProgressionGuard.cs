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
        /// <summary>The mission the pause belongs to, once it has been seen running.</summary>
        private static GameSession _runSession;
        /// <summary>The room was left during the run: the pause ends once that mission is gone, not before.</summary>
        private static bool _leftRoom;
        /// <summary>Since the room was left, the run's mission has been seen gone (the game drops it as the room is left).</summary>
        private static bool _sessionGone;

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

        /// <summary>
        /// Left the room (or joined another). The game saves a mission's stats while it unloads the mission
        /// (PlayerMetricTracker.OnDisable), which happens after the room is gone when a player quits to the main menu,
        /// follows an invite or is disconnected. So the pause holds until the hub loads or another mission runs (see
        /// <see cref="Poll"/>); the main menu in between has no progression to pause.
        /// </summary>
        public static void OnRoomChanged()
        {
            if (!Active)
            {
                Reset();
                return;
            }
            _leftRoom = true;
        }

        private static void Reset()
        {
            _hostRun = false;
            _roomRun = false;
            _hostRunAnnounced = false;
            _rememberedRun = null;
            _runSession = null;
            _leftRoom = false;
            _sessionGone = false;
        }

        /// <summary>Called a few times a second.</summary>
        public static void Poll()
        {
            bool inHub;
            GameSession session;
            try
            {
                if (GameSessionManager.Instance == null)
                {
                    if (_leftRoom) _sessionGone = true;
                    return;
                }
                inHub = GameSessionManager.InHub;
                session = GameSessionManager.ActiveSession;
            }
            catch
            {
                return;
            }
            // The room was left mid-run: the run is over in the hub or in another mission. Not when the session is merely
            // gone: the game drops it as the room is left (PhotonService.OnLeftRoom), before it unloads the mission and
            // saves its stats. A mission that comes after that is another one, even if the run's was never seen.
            if (_leftRoom)
            {
                if (session == null) _sessionGone = true;
                bool otherMission = session != null && (_runSession != null ? !ReferenceEquals(session, _runSession) : _sessionGone);
                if (inHub || otherMission)
                {
                    Log.Info("The CUSTOM run's mission is over: progression is on again.");
                    Reset();
                }
            }
            if (inHub || session == null) return;
            if (Active && !_leftRoom && _runSession == null) _runSession = session;

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

        /// <summary>
        /// The room's word that this mission (by its session id) is a CUSTOM run. SC_RunId only counts under a host on
        /// 0.8.0 or newer, which writes and clears it; an older host only sets SC_Run, and an id an earlier host left behind
        /// would never match its runs.
        /// </summary>
        private static bool RoomSaysCustomRun(string sessionId)
        {
            object id;
            if (RoomSync.HostProtocol() >= RoomSync.RunIdProtocol && RoomSync.TryGet(RoomSync.RunIdKey, out id) && id is string)
            {
                return !string.IsNullOrEmpty(sessionId) && (string)id == sessionId;
            }
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
