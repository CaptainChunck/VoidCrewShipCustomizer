using System;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using UnityEngine;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// The optional link to Void Crew Fleet, which adds a second player ship. Ship Customizer never needs it: everything
    /// for ship 2 (the terminal's SHIP 2, ship 2's CUSTOM loadout and its room properties, ship 2's fabricator level, the
    /// Fleet Battle team rules) only works while Fleet is installed and turned on, and stays off otherwise.
    ///
    /// Fleet's team rules come from its public FleetApi, reached through reflection like Fleet reaches ShipCustomizerApi:
    /// in Fleet Battle, ship 1 belongs to the BLUE team and ship 2 to the RED team, and only a ship's own team may change
    /// its loadout. Without Fleet, or with a Fleet without that API, every player may change every ship.
    /// </summary>
    internal static class FleetLink
    {
        public const string PluginGuid = "com.captainchunck.voidcrew.fleet";
        private const string ApiTypeName = "VoidCrewFleet.FleetApi";
        private const int ApiVersion = 1;

        private static bool _looked;
        private static bool _running;
        private static MethodInfo _isTeamMatch, _teamOfShip, _canEditShip, _shipColor;

        /// <summary>Whether Void Crew Fleet is installed and turned on. Until the hub has loaded, false.</summary>
        public static bool Running
        {
            get
            {
                Look();
                return _running;
            }
        }

        /// <summary>
        /// Looked up once, the first time it is needed after the hub has loaded: Fleet loads after this plugin, so it
        /// isn't there yet during start-up.
        /// </summary>
        private static void Look()
        {
            if (_looked || !CustomizerRuntime.GameReady) return;
            _looked = true;
            try
            {
                BepInEx.PluginInfo info;
                if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out info) || info == null)
                {
                    Log.Verbose("Void Crew Fleet is not installed, so everything for a second ship stays off.");
                    return;
                }
                if (!FleetTurnedOn(info))
                {
                    Log.Info("Void Crew Fleet is turned off in its config, so everything for a second ship stays off.");
                    return;
                }
                _running = true;
                LinkApi(info);
            }
            catch (Exception e)
            {
                _isTeamMatch = null;
                Log.Failure("Looking for Void Crew Fleet", e);
            }
        }

        /// <summary>Fleet's own General > Enabled setting (when it is off, Fleet does nothing).</summary>
        private static bool FleetTurnedOn(BepInEx.PluginInfo info)
        {
            if (ReferenceEquals(info.Instance, null)) return true;
            ConfigEntry<bool> enabled;
            return !info.Instance.Config.TryGetEntry("General", "Enabled", out enabled) || enabled.Value;
        }

        private static void LinkApi(BepInEx.PluginInfo info)
        {
            Type api = FindApi(info);
            FieldInfo version = api != null ? api.GetField("ApiVersion", BindingFlags.Public | BindingFlags.Static) : null;
            if (version == null || (int)version.GetValue(null) < ApiVersion)
            {
                Log.Info("Void Crew Fleet is running without its team rules (an older Fleet): every player may change both ships' CUSTOM loadouts.");
                return;
            }
            PropertyInfo isTeamMatch = api.GetProperty("IsTeamMatch", BindingFlags.Public | BindingFlags.Static);
            MethodInfo teamOfShip = api.GetMethod("TeamOfShip", new[] { typeof(int) });
            MethodInfo canEditShip = api.GetMethod("CanEditShip", new[] { typeof(int), typeof(int) });
            MethodInfo shipColor = api.GetMethod("ShipColor", new[] { typeof(int) });
            if (isTeamMatch == null || isTeamMatch.GetGetMethod() == null || teamOfShip == null || canEditShip == null || shipColor == null ||
                canEditShip.ReturnType != typeof(bool) || shipColor.ReturnType != typeof(Color))
            {
                Log.Warn("Void Crew Fleet's team rules are missing members, so they don't apply to CUSTOM.");
                return;
            }
            _teamOfShip = teamOfShip;
            _canEditShip = canEditShip;
            _shipColor = shipColor;
            _isTeamMatch = isTeamMatch.GetGetMethod();
            // Who may change which ship can change at any moment in the hub: redraw the terminal when it does.
            EventInfo changed = api.GetEvent("TeamsChanged", BindingFlags.Public | BindingFlags.Static);
            if (changed != null) changed.AddEventHandler(null, (Action)OnTeamsChanged);
            Log.Info("Void Crew Fleet found: in a Fleet Battle, only a ship's own team can change its CUSTOM loadout.");
        }

        /// <summary>Fleet's API type, from Fleet's own assembly (or any loaded assembly, if its plugin object is gone).</summary>
        private static Type FindApi(BepInEx.PluginInfo info)
        {
            if (!ReferenceEquals(info.Instance, null))
            {
                Type type = info.Instance.GetType().Assembly.GetType(ApiTypeName, false);
                if (type != null) return type;
            }
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type;
                try
                {
                    type = assembly.GetType(ApiTypeName, false);
                }
                catch
                {
                    continue;
                }
                if (type != null) return type;
            }
            return null;
        }

        private static void OnTeamsChanged()
        {
            try
            {
                TerminalUi.OnLoadoutChangedRemotely();
            }
            catch (Exception e)
            {
                Log.Failure("Redrawing the terminal after the teams changed", e);
            }
        }

        /// <summary>True during a Fleet Battle (chosen in the hub, or running).</summary>
        public static bool IsTeamMatch
        {
            get
            {
                Look();
                return _isTeamMatch != null && Call(_isTeamMatch, false);
            }
        }

        /// <summary>Whether this player may change ship 1's or ship 2's loadout: always, outside a Fleet Battle.</summary>
        public static bool CanEditShip(int actorNumber, int ship)
        {
            Look();
            return _canEditShip == null || Call(_canEditShip, true, actorNumber, ship);
        }

        /// <summary>"BLUE" or "RED": the team a ship belongs to in a Fleet Battle; null otherwise.</summary>
        public static string TeamNameOfShip(int ship)
        {
            Look();
            if (_teamOfShip == null) return null;
            int team = Call(_teamOfShip, -1, ship);
            return team == 0 ? "BLUE" : (team == 1 ? "RED" : null);
        }

        /// <summary>A ship's team colour in a Fleet Battle; null otherwise.</summary>
        public static Color? ShipColor(int ship)
        {
            Look();
            if (_shipColor == null) return null;
            Color color = Call(_shipColor, Color.clear, ship);
            return color.a > 0f ? color : (Color?)null;
        }

        private static T Call<T>(MethodInfo method, T fallback, params object[] args)
        {
            try
            {
                object result = method.Invoke(null, args);
                return result is T ? (T)result : fallback;
            }
            catch (Exception e)
            {
                Log.Verbose("Void Crew Fleet's " + method.Name + " failed: " + e.Message);
                return fallback;
            }
        }
    }
}
