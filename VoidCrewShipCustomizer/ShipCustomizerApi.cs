using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using ResourceAssets;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// For other plugins, reached through reflection only when this plugin is installed, so neither needs the other
    /// (Void Crew Fleet uses it for its extra player ships). Ship 1 is the game's own ship selection; ships 2 to 6 are the
    /// player ships Void Crew Fleet spawns (up to 3 in co-op, up to 3 a team in a Fleet Battle: odd ships BLUE, even ships
    /// RED), so their members do nothing unless Fleet runs here and on the host. The ship 2 members from 0.4.0 on stay, and
    /// are the same as the per-ship ones for ship 2. Every member can be called at any time and never throws.
    /// </summary>
    public static class ShipCustomizerApi
    {
        /// <summary>Raised when a member changes in a way older callers don't understand.</summary>
        public const int ApiVersion = 1;

        /// <summary>
        /// Host: raised when ship 2's CUSTOM loadout was switched off in the hub because someone without this plugin
        /// joined. The caller should give ship 2 the stand-in loadout (<see cref="GetStandIn"/>) again. Raised together with
        /// <see cref="ShipCustomCancelled"/> for ship 2, for plugins that only know this one.
        /// </summary>
        public static event Action Ship2CustomCancelled;

        /// <summary>
        /// Host, 0.9.0 on: raised with the player ship (2 to 6) whose CUSTOM loadout was switched off in the hub, because
        /// someone without this plugin joined (or, for ships 3 to 6, someone with a version older than 0.9.0). The caller
        /// should give that ship the stand-in loadout (<see cref="GetStandIn"/>) again.
        /// </summary>
        public static event Action<int> ShipCustomCancelled;

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

        /// <summary>
        /// 0.9.0 on: a ship type's default game loadout, or null for a type the loadout terminal doesn't list. It is the one
        /// CUSTOM stands in with (<see cref="GetStandIn"/>): the game's default when it is for that type (ROOKIE), otherwise
        /// the lowest-rank loadout the terminal lists, never a challenge or test loadout. Needs no CUSTOM.
        /// </summary>
        public static ShipLoadoutDataDef GetDefaultLoadout(GUIDUnion shipGuid)
        {
            try
            {
                HullInfo hull;
                return !shipGuid.IsNull && Catalog.ShipGuids().Contains(shipGuid) && Catalog.TryGetHull(shipGuid, out hull) ? hull.BasePreset : null;
            }
            catch (Exception e)
            {
                Log.Failure("Looking up a ship type's default loadout for another plugin", e);
                return null;
            }
        }

        /// <summary>
        /// Which player ship (1 to 6) the loadout terminal is setting up: CUSTOM shows, edits and saves that ship's layout.
        /// Ships 2 to 6 need Void Crew Fleet running here and on the host; otherwise the terminal stays on ship 1.
        /// </summary>
        public static void SetTerminalShip(int ship)
        {
            try
            {
                CustomLoadouts.SetTerminalShip(ship);
            }
            catch (Exception e)
            {
                Log.Failure("Switching the loadout terminal to ship " + ship, e);
            }
        }

        /// <summary>The same as <see cref="CheckShip"/> for ship 2.</summary>
        public static string CheckShip2(GUIDUnion loadout, int actorNumber)
        {
            return CheckShip(2, loadout, actorNumber);
        }

        /// <summary>
        /// Host, 0.9.0 on: null when this player may give a player ship (2 to 6) this CUSTOM loadout now, otherwise the
        /// reason (for that player). The rules are ship 1's: any player with this plugin may choose it, everyone in the room
        /// has this plugin, and the room is [mod_session]. Also: the ship must be of the type the host makes it use, if any,
        /// and ships 3 to 6 need everyone on Ship Customizer 0.9.0 or newer.
        /// </summary>
        public static string CheckShip(int ship, GUIDUnion loadout, int actorNumber)
        {
            try
            {
                if (!FleetLink.Running) return "Void Crew Fleet isn't running, so there is no ship " + ship + ".";
                if (ship < 2 || ship > CustomLoadouts.MaxShips) return "There is no ship " + ship + " that CUSTOM can be chosen for.";
                HullInfo hull;
                if (GetDef(loadout) == null || !CustomLoadouts.TryGetHullOfCustomGuid(loadout, out hull)) return "That CUSTOM loadout isn't available.";
                string wrongType = CustomLoadouts.WrongType(ship, hull);
                if (wrongType != null) return wrongType;
                if (!PhotonNetwork.IsMasterClient) return "Only the host can check this.";
                if (!RoomSync.PlayerHasMod(actorNumber)) return "You need Ship Customizer to choose CUSTOM.";
                List<string> missing = RoomSync.PlayersWithoutMod(false);
                if (missing.Count > 0)
                {
                    return "Everyone in the room needs Ship Customizer for CUSTOM. Not yet: " + string.Join(", ", missing.ToArray()) + ".";
                }
                if (ship >= 3)
                {
                    List<string> older = RoomSync.PlayersWithoutManyShips(false);
                    if (older.Count > 0)
                    {
                        return "CUSTOM on ships 3 to 6 needs everyone on Ship Customizer 0.9.0 or newer. Not yet: " + string.Join(", ", older.ToArray()) + ".";
                    }
                }
                if (!CustomLoadouts.EnsureRoomMarkedModded()) return "Marking the room [mod_session] first; choose CUSTOM again in a moment.";
                return null;
            }
            catch (Exception e)
            {
                Log.Failure("Checking CUSTOM for ship " + ship, e);
                return "CUSTOM could not be checked (see the BepInEx log).";
            }
        }

        /// <summary>The same as <see cref="SetShip"/> for ship 2.</summary>
        public static void SetShip2(GUIDUnion loadout)
        {
            SetShip(2, loadout);
        }

        /// <summary>
        /// Host, 0.9.0 on: a player ship (2 to 6) now uses this loadout (any GUID: one that isn't CUSTOM, or empty, means no
        /// CUSTOM). Players with this plugin then see the host's layout for that ship.
        /// </summary>
        public static void SetShip(int ship, GUIDUnion loadout)
        {
            try
            {
                if (ship < 2 || ship > CustomLoadouts.MaxShips) return;
                HullInfo hull;
                if (FleetLink.Running && GetDef(loadout) != null && CustomLoadouts.TryGetHullOfCustomGuid(loadout, out hull)) CustomLoadouts.ActivateShipAsHost(ship, hull);
                else CustomLoadouts.DeactivateShipAsHost(ship);
            }
            catch (Exception e)
            {
                Log.Failure("Choosing ship " + ship + "'s loadout", e);
            }
        }

        /// <summary>The same as <see cref="BuildShip"/> for ship 2.</summary>
        public static ShipLoadout BuildShip2(GUIDUnion loadout)
        {
            return BuildShip(2, loadout);
        }

        /// <summary>
        /// Host, 0.9.0 on, from a GSIngame.TransitionTo prefix that runs after this plugin's: a new ShipLoadout for a player
        /// ship (2 to 6) built from the host's CUSTOM layout for that ship, or null when the GUID isn't CUSTOM or CUSTOM can't
        /// be used for this mission (the host is told why; use GetStandIn). A CUSTOM ship makes the run a CUSTOM run
        /// (progression paused). Fleet Battle match rules apply.
        /// </summary>
        public static ShipLoadout BuildShip(int ship, GUIDUnion loadout)
        {
            try
            {
                return FleetLink.Running ? CustomLoadouts.BuildShipForMission(ship, loadout) : null;
            }
            catch (Exception e)
            {
                Log.Failure("Building ship " + ship + "'s CUSTOM loadout", e);
                return null;
            }
        }

        /// <summary>The same as <see cref="GetShipAlloys"/> for ship 2. Added in 0.5.1.</summary>
        public static int GetShip2Alloys(GUIDUnion loadout)
        {
            return GetShipAlloys(2, loadout);
        }

        /// <summary>
        /// Host, 0.9.0 on, at mission start: the alloys a player ship's (2 to 6) CUSTOM loadout starts with, or -1 for the
        /// game's own amount (also when the GUID isn't CUSTOM, or Void Crew Fleet isn't running). For a plugin that keeps an
        /// alloy count per ship; ship 1's goes through the game's own starting amount, so it is always -1 here. In a Fleet
        /// Battle, no more than the match rules allow.
        /// </summary>
        public static int GetShipAlloys(int ship, GUIDUnion loadout)
        {
            try
            {
                HullInfo hull;
                if (!FleetLink.Running || ship < 2 || ship > CustomLoadouts.MaxShips || GetDef(loadout) == null || !CustomLoadouts.TryGetHullOfCustomGuid(loadout, out hull)) return -1;
                return MatchRules.AllowedAlloys(LoadoutStore.Get(hull, ship).Alloys);
            }
            catch (Exception e)
            {
                Log.Failure("Reading ship " + ship + "'s starting alloys", e);
                return -1;
            }
        }

        /// <summary>
        /// Everything a Fleet Battle's match rules can allow or refuse on a CUSTOM ship (Void Crew Fleet's SHIP RULES tab),
        /// one line each: "code|category|tier|name".
        /// - code: what FleetApi.IsItemAllowed is asked: "m:" module, "w:" weapon, "i:" item or "c:" weapon crate, then the
        ///   GUID in lower-case hex. Each code is listed once. Banning a weapon ("w:") bans its weapon crate too.
        /// - category: what FleetApi.IsCategoryAllowed is asked, one of WEAPONS, DEFENSE, SUPPORT MODULES, AMMO &amp; POWER,
        ///   SUPPORT ITEMS, MATERIALS, ANIMUS CRATES, WEAPON CRATES, WEAPON MODS, SPECIAL, OTHER, HOMUNCULUS (in that order).
        /// - tier: 1 to 3 (MK I to MK III, or Common, Rare, and Epic or Legendary), or 0 for none (Special items, no
        ///   rarity), which no tier rule refuses.
        /// - name: plain text; a Blessed Homunculus's has its blessing after a colon.
        /// What a Fleet Battle never allows isn't listed (relics, the Armory Exchange Key, extra Fabricator modules,
        /// Placeholder weapons). Empty until the hub has loaded. Added in 0.7.0, ApiVersion still 1.
        /// </summary>
        public static string[] GetRuleOptions()
        {
            try
            {
                return MatchRules.RuleOptions();
            }
            catch (Exception e)
            {
                Log.Failure("Listing the options for Void Crew Fleet's match rules", e);
                return new string[0];
            }
        }

        /// <summary>Tells other plugins a player ship's CUSTOM was switched off (ship 2 on both events, for older plugins).</summary>
        internal static void RaiseShipCustomCancelled(int ship)
        {
            Action<int> perShip = ShipCustomCancelled;
            if (perShip != null)
            {
                foreach (Action<int> handler in perShip.GetInvocationList().Cast<Action<int>>()) Notify(() => handler(ship), ship);
            }
            if (ship != 2) return;
            Action shipTwo = Ship2CustomCancelled;
            if (shipTwo == null) return;
            foreach (Action handler in shipTwo.GetInvocationList().Cast<Action>()) Notify(handler, ship);
        }

        private static void Notify(Action handler, int ship)
        {
            try
            {
                handler();
            }
            catch (Exception e)
            {
                Log.Failure("Telling another plugin that ship " + ship + "'s CUSTOM was switched off", e);
            }
        }
    }
}
