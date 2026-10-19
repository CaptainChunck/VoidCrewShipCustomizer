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
        private static bool _readsShip2Alloys;
        private static MethodInfo _isTeamMatch, _teamOfShip, _canEditShip, _shipColor;
        // Fleet Battle's rules for CUSTOM ships (Void Crew Fleet 0.3.0 on); each one optional.
        private static MethodInfo _rulesActive, _tierAllowed, _categoryAllowed, _itemAllowed, _fabricatorMaxLevel, _maxStartAlloys;

        /// <summary>
        /// Whether Void Crew Fleet gives ship 2 the starting alloys of its own CUSTOM loadout (it calls
        /// ShipCustomizerApi.GetShip2Alloys, and says so with FleetApi.ReadsShip2Alloys). Older Fleets start both ships
        /// with ship 1's, so the terminal only offers ship 2's alloys when this is true.
        /// </summary>
        public static bool ReadsShip2Alloys
        {
            get
            {
                Look();
                return _readsShip2Alloys;
            }
        }

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
            _readsShip2Alloys = Flag(api, "ReadsShip2Alloys");
            LinkRules(api);
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

        /// <summary>
        /// Fleet Battle's rules for CUSTOM ships: which tiers, categories and items CUSTOM may use, the highest fabricator
        /// level and the most starting alloys. All of them or none: a Fleet with only some leaves CUSTOM unrestricted.
        /// </summary>
        private static void LinkRules(Type api)
        {
            MethodInfo active = Getter(api, "ShipRulesActive", typeof(bool));
            MethodInfo tier = api.GetMethod("IsTierAllowed", new[] { typeof(int) });
            MethodInfo category = api.GetMethod("IsCategoryAllowed", new[] { typeof(string) });
            MethodInfo item = api.GetMethod("IsItemAllowed", new[] { typeof(string) });
            MethodInfo fabricator = Getter(api, "FabricatorMaxLevel", typeof(int));
            MethodInfo alloys = Getter(api, "MaxStartAlloys", typeof(int));
            if (active == null || tier == null || category == null || item == null || fabricator == null || alloys == null) return;
            if (tier.ReturnType != typeof(bool) || category.ReturnType != typeof(bool) || item.ReturnType != typeof(bool)) return;
            _tierAllowed = tier;
            _categoryAllowed = category;
            _itemAllowed = item;
            _fabricatorMaxLevel = fabricator;
            _maxStartAlloys = alloys;
            _rulesActive = active;
            EventInfo changed = api.GetEvent("ShipRulesChanged", BindingFlags.Public | BindingFlags.Static);
            if (changed != null) changed.AddEventHandler(null, (Action)OnRulesChanged);
            Log.Info("Void Crew Fleet's match rules are linked: in a Fleet Battle, CUSTOM ships keep to them.");
        }

        /// <summary>A static property's getter (or, failing that, nothing) of the given type.</summary>
        private static MethodInfo Getter(Type api, string name, Type type)
        {
            PropertyInfo property = api.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            MethodInfo getter = property != null ? property.GetGetMethod() : null;
            return getter != null && getter.ReturnType == type ? getter : null;
        }

        /// <summary>An optional true/false of Fleet's API (a static property or field); false when it isn't there.</summary>
        private static bool Flag(Type api, string name)
        {
            PropertyInfo property = api.GetProperty(name, BindingFlags.Public | BindingFlags.Static);
            if (property != null && property.PropertyType == typeof(bool)) return (bool)property.GetValue(null, null);
            FieldInfo field = api.GetField(name, BindingFlags.Public | BindingFlags.Static);
            return field != null && field.FieldType == typeof(bool) && (bool)field.GetValue(null);
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
                // A Fleet Battle starting or ending changes what CUSTOM ships may start with (the match rules).
                CustomLoadouts.RefreshAll();
                TerminalUi.OnLoadoutChangedRemotely();
            }
            catch (Exception e)
            {
                Log.Failure("Redrawing the terminal after the teams changed", e);
            }
        }

        private static void OnRulesChanged()
        {
            try
            {
                CustomLoadouts.RefreshAll();
                TerminalUi.OnLoadoutChangedRemotely();
            }
            catch (Exception e)
            {
                Log.Failure("Redrawing the terminal after the match rules changed", e);
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

        /// <summary>Whether a Fleet Battle has rules for CUSTOM ships that restrict something.</summary>
        public static bool ShipRulesActive
        {
            get
            {
                Look();
                return _rulesActive != null && Call(_rulesActive, false);
            }
        }

        public static bool IsTierAllowed(int tier)
        {
            return _tierAllowed == null || Call(_tierAllowed, true, tier);
        }

        public static bool IsCategoryAllowed(string category)
        {
            return _categoryAllowed == null || Call(_categoryAllowed, true, category);
        }

        public static bool IsItemAllowed(string code)
        {
            return _itemAllowed == null || Call(_itemAllowed, true, code);
        }

        /// <summary>The highest fabricator level the rules allow (0 is MK I).</summary>
        public static int FabricatorMaxLevel
        {
            get { return _fabricatorMaxLevel == null ? HullConfig.MaxFabricatorLevel : Call(_fabricatorMaxLevel, HullConfig.MaxFabricatorLevel); }
        }

        /// <summary>The most starting alloys the rules allow; -1 for the game's own amount only, int.MaxValue for no limit.</summary>
        public static int MaxStartAlloys
        {
            get { return _maxStartAlloys == null ? int.MaxValue : Call(_maxStartAlloys, int.MaxValue); }
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
