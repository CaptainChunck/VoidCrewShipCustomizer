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
        }
    }
}
