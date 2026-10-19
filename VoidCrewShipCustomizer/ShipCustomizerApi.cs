using System;
using System.Collections.Generic;
using Photon.Pun;
using ResourceAssets;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// For other plugins, reached through reflection only when this plugin is installed, so neither needs the other
    /// (Void Crew Fleet uses it for its second player ship). Ship 1 is the game's own ship selection; ship 2 is the second
    /// player ship Void Crew Fleet spawns, so the ship 2 members do nothing unless Fleet is installed and turned on.
    /// Every member can be called at any time and never throws.
    /// </summary>
    public static class ShipCustomizerApi
    {
        /// <summary>Raised when a member changes in a way older callers don't understand.</summary>
        public const int ApiVersion = 1;

        /// <summary>
        /// Host: raised when ship 2's CUSTOM loadout was switched off in the hub because someone without this plugin
        /// joined. The caller should give ship 2 the stand-in loadout (<see cref="GetStandIn"/>) again.
        /// </summary>
        public static event Action Ship2CustomCancelled;

        /// <summary>The CUSTOM definition behind a GUID (the object the terminal lists, created on first use), or null.</summary>
        public static ShipLoadoutDataDef GetDef(GUIDUnion loadout)
        {
            try
            {
                ShipLoadoutDataDef def;
                return CustomLoadouts.TryGetDefOfCustomGuid(loadout, out def) ? def : null;
            }
            catch (Exception e)
            {
                Log.Failure("Looking up a CUSTOM loadout for another plugin", e);
                return null;
            }
        }

        /// <summary>The game loadout a CUSTOM loadout is built on (shown as to players without this plugin), or null.</summary>
        public static ShipLoadoutDataDef GetStandIn(GUIDUnion loadout)
        {
            try
            {
                HullInfo hull;
                return GetDef(loadout) != null && CustomLoadouts.TryGetHullOfCustomGuid(loadout, out hull) ? hull.BasePreset : null;
            }
            catch (Exception e)
            {
                Log.Failure("Looking up a CUSTOM loadout's stand-in for another plugin", e);
                return null;
            }
        }

        /// <summary>Which ship (1 or 2) the loadout terminal is setting up: CUSTOM shows, edits and saves that ship's layout.</summary>
        public static void SetTerminalShip(int ship)
        {
            try
            {
                CustomLoadouts.SetTerminalShip(ship == 2 ? 2 : 1);
            }
            catch (Exception e)
            {
                Log.Failure("Switching the loadout terminal to ship " + ship, e);
            }
        }

        /// <summary>
        /// Host: null when this player may give ship 2 this CUSTOM loadout now, otherwise the reason (for that player).
        /// The rules are ship 1's: any player with this plugin may choose it (0.5.0 on; before, only the host), everyone
        /// in the room has this plugin, and the room is [mod_session].
        /// </summary>
        public static string CheckShip2(GUIDUnion loadout, int actorNumber)
        {
            try
            {
                if (!FleetLink.Running) return "Void Crew Fleet isn't running, so there is no ship 2.";
                if (GetDef(loadout) == null) return "That CUSTOM loadout isn't available.";
                if (!PhotonNetwork.IsMasterClient) return "Only the host can check this.";
                if (!RoomSync.PlayerHasMod(actorNumber)) return "You need Ship Customizer to choose CUSTOM.";
                List<string> missing = RoomSync.PlayersWithoutMod(false);
                if (missing.Count > 0)
                {
                    return "Everyone in the room needs Ship Customizer for CUSTOM. Not yet: " + string.Join(", ", missing.ToArray()) + ".";
                }
                if (!CustomLoadouts.EnsureRoomMarkedModded()) return "Marking the room [mod_session] first; choose CUSTOM again in a moment.";
                return null;
            }
            catch (Exception e)
            {
                Log.Failure("Checking CUSTOM for ship 2", e);
                return "CUSTOM could not be checked (see the BepInEx log).";
            }
        }

        /// <summary>Host: ship 2 now uses this loadout (any GUID; empty for none). Players with this plugin then see the host's ship 2 layout.</summary>
        public static void SetShip2(GUIDUnion loadout)
        {
            try
            {
                HullInfo hull;
                if (FleetLink.Running && GetDef(loadout) != null && CustomLoadouts.TryGetHullOfCustomGuid(loadout, out hull)) CustomLoadouts.ActivateShip2AsHost(hull);
                else CustomLoadouts.DeactivateShip2AsHost();
            }
            catch (Exception e)
            {
                Log.Failure("Choosing ship 2's loadout", e);
            }
        }

        /// <summary>
        /// Host, from a GSIngame.TransitionTo prefix that runs after this plugin's: a new ShipLoadout for ship 2 built from
        /// the host's ship 2 CUSTOM layout, or null when the GUID isn't CUSTOM or CUSTOM can't be used for this mission
        /// (the host is told why; use GetStandIn). A CUSTOM ship 2 makes the run a CUSTOM run (progression paused).
        /// </summary>
        public static ShipLoadout BuildShip2(GUIDUnion loadout)
        {
            try
            {
                return FleetLink.Running ? CustomLoadouts.BuildShip2ForMission(loadout) : null;
            }
            catch (Exception e)
            {
                Log.Failure("Building ship 2's CUSTOM loadout", e);
                return null;
            }
        }

        internal static void RaiseShip2CustomCancelled()
        {
            Action handlers = Ship2CustomCancelled;
            if (handlers == null) return;
            foreach (Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    ((Action)handler)();
                }
                catch (Exception e)
                {
                    Log.Failure("Telling another plugin that ship 2's CUSTOM was switched off", e);
                }
            }
        }
    }
}
