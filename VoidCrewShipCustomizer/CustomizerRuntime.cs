using System;
using UnityEngine;

namespace VoidCrewShipCustomizer
{
    /// <summary>Keeps the room information and the progression pause up to date a few times a second.</summary>
    internal sealed class CustomizerRuntime : MonoBehaviour
    {
        private const float PollSeconds = 0.25f;
        private float _nextPoll;

        /// <summary>
        /// Set once the hub has loaded. Until then nothing here runs, so the plugin never wakes up Photon (or the game's
        /// managers) earlier than the game itself does.
        /// </summary>
        public static bool GameReady;

        /// <summary>
        /// The modding guidelines: a mod that changes the session marks itself [mod_session] before any room it hosts is
        /// listed. The game then tags every room this player hosts as [mod_session] (players opt in to join those).
        /// </summary>
        private void Start()
        {
            if (!CustomLoadouts.Available) return;
            try
            {
                ModdingUtils.RegisterSessionMod();
                Log.Info("Rooms you host are marked [mod_session] (the game's tag for mods that change the session), as Void Crew's modding guidelines ask.");
            }
            catch (Exception e)
            {
                Log.Failure("Marking hosted rooms as [mod_session]", e);
            }
        }

        private void Update()
        {
            if (!GameReady || Time.unscaledTime < _nextPoll) return;
            _nextPoll = Time.unscaledTime + PollSeconds;

            try
            {
                RoomSync.Poll();
            }
            catch (Exception e)
            {
                Log.Failure("Reading the room's ship customizer info", e);
            }

            try
            {
                ProgressionGuard.Poll();
            }
            catch (Exception e)
            {
                Log.Failure("Updating the progression pause", e);
            }

            try
            {
                CustomLoadouts.ApplyFabricatorLevels();
            }
            catch (Exception e)
            {
                Log.Failure("Setting the fabricator's CUSTOM level", e);
            }

            try
            {
                TerminalUi.CatchUp();
            }
            catch (Exception e)
            {
                Log.Failure("Showing the room's choice on the loadout terminal", e);
            }

            try
            {
                LoadoutStore.Flush(false);
            }
            catch (Exception e)
            {
                Log.Failure("Saving the CUSTOM loadouts", e);
            }
        }

        /// <summary>Changes still waiting to be written are saved before the game closes.</summary>
        private void OnApplicationQuit()
        {
            try
            {
                LoadoutStore.Flush(true);
            }
            catch (Exception e)
            {
                Log.Failure("Saving the CUSTOM loadouts as the game closes", e);
            }
        }
    }
}
