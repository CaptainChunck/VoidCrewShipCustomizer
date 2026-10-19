using System;
using BepInEx;
using HarmonyLib;
using ResourceAssets;
using UnityEngine;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// BepInEx entry point. Applies the game patches and starts the small runtime object. Each start-up step is guarded:
    /// if one fails on a player's PC (a different BepInEx, Harmony or game version), the error is logged with what
    /// failed and the rest of the plugin still starts.
    /// </summary>
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.captainchunck.voidcrew.shipcustomizer";
        public const string Name = "Void Crew Ship Customizer";
        public const string Version = "0.7.0";

        // Fixed id the game's mod list (main menu) shows this plugin under.
        private static readonly GUIDUnion ModId = new GUIDUnion("5c0ffee55ea7c0de5c0ffee55ea7c0de");

        private GameObject _runtime;
        private bool _running;

        private void Awake()
        {
            Log.Init(Logger);
            try
            {
                Settings.Bind(Config);
            }
            catch (Exception e)
            {
                Log.Error(Name + " " + Version + " could not read its settings file (" + Config.ConfigFilePath + "), so it is not running. " +
                          "Delete that file and start the game again. Details: " + e);
                return;
            }

            if (!Settings.Enabled.Value)
            {
                Log.Info(Name + " " + Version + " is turned off in its config (General > Enabled).");
                return;
            }

            Log.Info(Name + " " + Version + " | Unity " + Application.unityVersion + " | game " + Application.version +
                     " | BepInEx " + typeof(BaseUnityPlugin).Assembly.GetName().Version + ", Harmony " + typeof(Harmony).Assembly.GetName().Version);

            Step("Applying the game patches", () => Patches.Apply(new Harmony(Guid)));
            Step("Adding the plugin to the game's mod list", RegisterWithGame);
            _running = Step("Starting the customizer", CreateRuntime);
            if (_running) Log.Info(Name + " " + Version + " is running. Your Custom loadouts are saved in " + LoadoutStore.Folder + ".");
        }

        private void Update()
        {
            if (_running && _runtime == null)
            {
                Log.Warn("The plugin's runtime object was destroyed by the game; recreating it.");
                _running = Step("Restarting the customizer", CreateRuntime);
            }
        }

        /// <summary>Runs one start-up step; a failure is logged in full and the next steps still run.</summary>
        private static bool Step(string what, Action step)
        {
            try
            {
                step();
                return true;
            }
            catch (Exception e)
            {
                Log.Error(what + " failed, so part of the plugin may not work. Please send BepInEx\\LogOutput.log with a bug report. Details: " + e);
                return false;
            }
        }

        /// <summary>
        /// The game's own register of mods, shown from the main menu. "None" means joining players are not required to
        /// have it; sessions that use a CUSTOM loadout are flagged separately when they start.
        /// </summary>
        private static void RegisterWithGame()
        {
            if (RuntimeAssetsRegister.Instance.GetRegisteredModIds().Contains(ModId)) return;
            RuntimeAssetsRegister.Instance.RegisterExternalMod(ModId, SessionModificationEffect.None, new RuntimeAssetInfo
            {
                Name = "VoidCrewShipCustomizer " + Version,
                DisplayName = "Ship Customizer"
            });
        }

        private void CreateRuntime()
        {
            _runtime = new GameObject("VoidCrewShipCustomizer");
            DontDestroyOnLoad(_runtime);
            _runtime.hideFlags = HideFlags.HideAndDontSave;
            _runtime.AddComponent<CustomizerRuntime>();
        }
    }
}
