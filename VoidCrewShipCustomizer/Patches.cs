using System;
using System.Collections.Generic;
using System.Reflection;
using CG.Objects;
using CG.Profile;
using CG.Ship.Hull;
using CG.Space;
using HarmonyLib;
using Photon.Pun;
using ResourceAssets;
using UI.PowerTerminal;
using UI.ShipLoadoutTerminal;
using UnityEngine.UIElements;
using LoadoutTerminal = UI.ShipLoadoutTerminal.ShipLoadoutTerminal;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// The Harmony patches. Each target is looked up by name and patched on its own, so if a game update renames
    /// something only that part stops working. The CUSTOM loadout is only offered when every patch it depends on is in.
    /// PatchProcessor is used because it behaves the same in every Harmony 2 version BepInEx installs ship.
    /// </summary>
    internal static class Patches
    {
        private static Harmony _harmony;
        private static AccessTools.FieldRef<HubShipManager, ShipLoadoutDataDef> _currentShip;
        private static FieldInfo _selectionChanged;

        public static void Apply(Harmony harmony)
        {
            _harmony = harmony;

            // Without these the CUSTOM loadout can't work, so it is only offered when all of them are in.
            bool core = Patch("HubShipManager", "SelectShip", nameof(SelectShipPrefix), null);
            core &= Patch("HubShipManager", "get_CurrentShipSelected", null, nameof(CurrentShipSelectedPostfix));
            core &= Patch("CG.GameLoopStateMachine.GameStates.GSIngame", "TransitionTo", nameof(TransitionToPrefix), null);
            core &= Patch("UI.ShipLoadoutTerminal.LoadoutList", "SetSource", nameof(SetSourcePrefix), null);
            core &= Patch("UI.ShipLoadoutTerminal.LoadoutList", "Sort", nameof(SortPrefix), null);
            core &= Patch("UI.ShipLoadoutTerminal.LoadoutList", "BindItem", nameof(BindItemPrefix), null);
            core &= Patch("UI.ShipLoadoutTerminal.ModuleLayout", "Show", null, nameof(ModuleLayoutShowPostfix));
            core &= Patch("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "Display", null, nameof(DisplayPostfix));
            // Progression must pause during CUSTOM runs (Void Crew modding rules), so this one is required too.
            core &= Patch("CG.Profile.PlayerRewardsCollection", "AddItem", nameof(BlockRewardsPrefix), null);

            // Shows CUSTOM (rather than the loadout standing in for it) as the room's choice everywhere in the hub.
            _currentShip = AccessTools.FieldRefAccess<HubShipManager, ShipLoadoutDataDef>("_currentShipSelected");
            _selectionChanged = AccessTools.Field(typeof(HubShipManager), "ShipSelectionChanged");
            if (_currentShip != null && _selectionChanged != null) Patch("HubShipManager", "NotifyShipSelected", nameof(NotifyShipSelectedPrefix), null);
            else Log.Warn("Could not find HubShipManager's selection fields; the terminal will show the stand-in loadout as chosen instead of CUSTOM.");

            Patch("HubShipManager", "Start", null, nameof(HubStartPostfix));
            Patch("Gameplay.Hub.HubQuestManager", "SelectPreservedSessionQuest", nameof(SelectSavedRunPrefix), null);
            Patch("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "OnDestroy", nameof(TerminalDestroyPrefix), null);
            Patch("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "OnLeaveUI", null, nameof(TerminalLeavePostfix));
            // During a CUSTOM run the storage racks take any item, as the CUSTOM editor lets them start with any item.
            Patch("CG.Ship.Hull.CarryablesSocket", "DoesAccept", nameof(RackAcceptsPrefix), null);
            // A CUSTOM ship's racks hold exactly what was chosen, checked right after the ship spawns.
            Patch("ShipLoadout", "InitializeShip", null, nameof(InitializeShipPostfix));

            // The rest of progression: best effort.
            Patch("CG.Cloud.CloudRewardsProfileSync", "AddItem", nameof(BlockRewardsPrefix), null);
            Patch("CG.Profile.PlayerAchievements", "SetAchievementComplete", nameof(BlockAchievementsPrefix), null);
            Patch("SteamPlayerAchievementsSync", "SetAchievementComplete", nameof(BlockAchievementsPrefix), null);
            Patch("CG.Cloud.CloudPlayerAchievementsSync", "SetAchievementComplete", nameof(BlockAchievementsPrefix), null);
            Patch("CG.Profile.PlayerProfileData", "AddQuestProgress", nameof(BlockQuestProgressPrefix), null);
            Patch("CG.Cloud.CloudPlayerProfileDataSync", "AddQuestProgress", nameof(BlockQuestProgressPrefix), null);
            Patch("CG.Profile.PhotonPlayerDataSync", "AddQuestProgress", nameof(BlockQuestProgressPrefix), null);

            CustomLoadouts.Available = core;
            if (core) Log.Info("All game patches are in; the CUSTOM loadout is available at the ship loadout terminal.");
            else Log.Error("Some patches the CUSTOM loadout needs could not be applied (see above), so it is turned off. The game itself is unaffected.");
        }

        private static bool Patch(string typeName, string methodName, string prefix, string postfix)
        {
            try
            {
                Type type = AccessTools.TypeByName(typeName);
                if (type == null) throw new MissingMemberException("type " + typeName + " not found");
                MethodBase target = AccessTools.Method(type, methodName);
                if (target == null) throw new MissingMethodException(typeName, methodName);
                PatchProcessor processor = _harmony.CreateProcessor(target);
                if (prefix != null) processor.AddPrefix(new HarmonyMethod(AccessTools.Method(typeof(Patches), prefix)));
                if (postfix != null) processor.AddPostfix(new HarmonyMethod(AccessTools.Method(typeof(Patches), postfix)));
                processor.Patch();
                Log.Verbose("Patched " + typeName + "." + methodName + ".");
                return true;
            }
            catch (Exception e)
            {
                Log.Error("Could not patch " + typeName + "." + methodName + " (the game may have changed): " + e.Message);
                return false;
            }
        }

        // ---------------------------------------------------------------- choosing the loadout

        /// <summary>
        /// CUSTOM is picked: on the host, remember it and let the game pick the ship's first loadout in its place, so no
        /// player ever receives a loadout id they don't know. Any other pick on the host turns CUSTOM off.
        /// </summary>
        private static bool SelectShipPrefix(HubShipManager __instance, ref int[] guid)
        {
            try
            {
                var selected = new GUIDUnion(guid);
                HullInfo hull;
                ShipLoadoutDataDef custom;
                // Created here if needed: another player may choose a ship's CUSTOM before the host has looked at it.
                if (CustomLoadouts.TryGetDefOfCustomGuid(selected, out custom) && CustomLoadouts.TryGetHullOfCustomGuid(selected, out hull))
                {
                    // Modding guidelines: nobody may earn progression in a sandbox run, so everyone needs the mod (which
                    // pauses their progression), and the room must be marked [mod_session] first.
                    List<string> missing = RoomSync.PlayersWithoutMod(false);
                    if (missing.Count > 0)
                    {
                        Notice.Show("Everyone in the room needs Ship Customizer for CUSTOM. Not yet: " + string.Join(", ", missing.ToArray()) +
                                    ". (Just joined? Try again in a few seconds.)");
                        return false;
                    }
                    if (!PhotonNetwork.IsMasterClient)
                    {
                        // The game sends this choice to the host, whose Ship Customizer checks it again and selects the
                        // loadout CUSTOM stands in for. A host older than 0.5.0 keeps CUSTOM to itself.
                        if (RoomSync.HostSharesLayouts()) return true;
                        TerminalUi.Toast("Only the host can choose CUSTOM here: the host's Ship Customizer is older than 0.5.0.");
                        return false;
                    }
                    if (!CustomLoadouts.EnsureRoomMarkedModded())
                    {
                        TerminalUi.Toast("Marking the room [mod_session] first; choose CUSTOM again in a moment.");
                        return false;
                    }
                    CustomLoadouts.ActivateAsHost(hull);
                    guid = hull.BasePreset.AssetGuid.AsIntArray();
                    return true;
                }
                if (CustomLoadouts.LooksCustom(selected))
                {
                    // A CUSTOM id this player can't use (CUSTOM is off here): never let the game pass it on to players
                    // who don't know it.
                    Log.Warn("Ignored a choice of a CUSTOM loadout, because CUSTOM is not available here.");
                    return false;
                }
                if (PhotonNetwork.IsMasterClient) CustomLoadouts.DeactivateAsHost();
            }
            catch (Exception e)
            {
                Log.Failure("Choosing a ship loadout", e);
            }
            return true;
        }

        /// <summary>
        /// The game's NotifyShipSelected, with two changes: a CUSTOM id is turned into its stand-in, and listeners (the
        /// terminal, the hub display, the hologram) are told about CUSTOM when it stands behind the chosen loadout.
        /// </summary>
        private static bool NotifyShipSelectedPrefix(HubShipManager __instance, int[] guid)
        {
            ShipLoadoutDataDef shown;
            try
            {
                var selected = new GUIDUnion(guid);
                HullInfo hull;
                if (CustomLoadouts.TryGetHullOfCustomGuid(selected, out hull)) selected = hull.BasePreset.AssetGuid;
                __instance.SelectedShipGuid = selected;
                ShipLoadoutDataDef def = selected.IsNull ? null : ResourceAssetContainer<ShipLoadoutDataContainer, ShipLoadoutData, ShipLoadoutDataDef>.Instance.GetAssetDefById(selected);
                _currentShip(__instance) = def;
                ShipLoadoutDataDef custom;
                shown = CustomLoadouts.TryMapToCustom(def, out custom) ? custom : def;
            }
            catch (Exception e)
            {
                Log.Failure("Showing the chosen ship loadout", e);
                return true;
            }
            var listeners = _selectionChanged.GetValue(__instance) as Action<ShipLoadoutDataDef>;
            if (listeners != null) listeners(shown);
            return false;
        }

        private static void CurrentShipSelectedPostfix(ref ShipLoadoutDataDef __result)
        {
            try
            {
                ShipLoadoutDataDef custom;
                if (__result != null && CustomLoadouts.TryMapToCustom(__result, out custom)) __result = custom;
            }
            catch (Exception e)
            {
                Log.Failure("Reading the chosen ship loadout", e);
            }
        }

        private static void TransitionToPrefix(GameSession gameSession, PreservedGameSession preservedGameSession)
        {
            try
            {
                CustomizerRuntime.GameReady = true;
                CustomLoadouts.OnMissionStarting(gameSession, preservedGameSession);
            }
            catch (Exception e)
            {
                Log.Failure("Swapping in the CUSTOM ship at mission start (the mission starts with the stand-in loadout instead)", e);
            }
        }

        /// <summary>A saved CUSTOM run can only be continued while everyone in the room has the mod.</summary>
        private static bool SelectSavedRunPrefix()
        {
            try
            {
                if (!PhotonNetwork.IsMasterClient) return true;
                PreservedGameSession saved = PlayerProfile.Instance.PreservedSession.PreservedSession;
                if (saved == null || !LoadoutStore.WasCustomRun(saved.GameSessionID)) return true;
                List<string> missing = RoomSync.PlayersWithoutMod(false);
                if (missing.Count == 0) return true;
                Notice.Show("Your saved run was started with CUSTOM, so everyone needs Ship Customizer to continue it. Not yet: " + string.Join(", ", missing.ToArray()) + ".");
                return false;
            }
            catch (Exception e)
            {
                Log.Failure("Checking the saved run", e);
                return true;
            }
        }

        private static void HubStartPostfix(HubShipManager __instance)
        {
            try
            {
                CustomizerRuntime.GameReady = true;
                ProgressionGuard.OnHubLoaded();
                RoomSync.ReopenAfterCustomMission();
                CustomLoadouts.OnHubLoaded(__instance);
            }
            catch (Exception e)
            {
                Log.Failure("Choosing the CUSTOM loadout again in the hub", e);
            }
        }

        // ---------------------------------------------------------------- the loadout list

        private static void SetSourcePrefix(LoadoutList __instance, List<ShipLoadoutDataDef> loadouts)
        {
            try
            {
                // Challenge quests and saved runs restrict the list (IgnoreRankRequirements); CUSTOM is not offered then.
                if (!CustomLoadouts.Available || loadouts == null || loadouts.Count == 0 || __instance.IgnoreRankRequirements) return;
                if (loadouts.Exists(CustomLoadouts.IsCustom)) return;
                ShipLoadoutDataDef custom;
                if (CustomLoadouts.TryGetDef(loadouts[0].ShipGuid, out custom)) loadouts.Add(custom);
            }
            catch (Exception e)
            {
                Log.Failure("Adding CUSTOM to the loadout list", e);
            }
        }

        /// <summary>The game sorts by rank requirement, which CUSTOM doesn't have; it goes last.</summary>
        private static bool SortPrefix(ShipLoadoutDataDef a, ShipLoadoutDataDef b, ref int __result)
        {
            bool customA = CustomLoadouts.IsCustom(a);
            bool customB = CustomLoadouts.IsCustom(b);
            if (!customA && !customB) return true;
            __result = customA == customB ? 0 : (customA ? 1 : -1);
            return false;
        }

        private static bool BindItemPrefix(VisualElement item, ShipLoadoutDataDef loadout)
        {
            if (!CustomLoadouts.IsCustom(loadout)) return true;
            try
            {
                TerminalUi.BindListEntry(item, loadout);
            }
            catch (Exception e)
            {
                Log.Failure("Drawing the CUSTOM list entry", e);
            }
            return false;
        }

        // ---------------------------------------------------------------- the terminal screen

        private static void ModuleLayoutShowPostfix(ShipLoadout loadout, ShipLoadoutDataDef data, List<SlotVE> ___slots, Button ___selectButton, VisualElement ___moduleLayoutAnchor)
        {
            try
            {
                TerminalUi.OnLayoutShown(loadout, data, ___slots, ___selectButton, ___moduleLayoutAnchor);
            }
            catch (Exception e)
            {
                Log.Failure("Drawing the CUSTOM hardpoints", e);
            }
        }

        private static void DisplayPostfix(LoadoutTerminal __instance, ShipLoadoutDataDef def, VisualElement ___root)
        {
            try
            {
                CustomizerRuntime.GameReady = true;
                TerminalUi.OnTerminalDisplayed(__instance, def, ___root);
            }
            catch (Exception e)
            {
                Log.Failure("Updating the loadout terminal", e);
            }
        }

        private static void TerminalDestroyPrefix(LoadoutTerminal __instance)
        {
            try
            {
                TerminalUi.OnTerminalDestroyed(__instance);
            }
            catch (Exception e)
            {
                Log.Failure("Cleaning up the loadout terminal", e);
            }
        }

        private static void TerminalLeavePostfix()
        {
            try
            {
                TerminalUi.OnLeftTerminal();
            }
            catch (Exception e)
            {
                Log.Failure("Closing the Ship Customizer pop-ups", e);
            }
        }

        // ---------------------------------------------------------------- storage racks during a CUSTOM run

        /// <summary>
        /// A storage rack slot takes any item during a CUSTOM run (setting AnyItemOnAnyRack), so items that started on an
        /// unusual rack can go back after being taken off. The game checks a slot's accepted / rejected items only on the
        /// player who puts the item in, never on the host, so every player with the mod gets this.
        /// </summary>
        private static bool RackAcceptsPrefix(CarryablesSocket __instance, CarryableObject carryable, ref bool __result)
        {
            try
            {
                if (!ProgressionGuard.Active || carryable == null || !Settings.AnyItemOnAnyRack.Value || !(__instance.Module is CarryablesShelf)) return true;
                // The rest of the game's own check: the slot's interaction side must be there and willing.
                __result = __instance.CarryablesSocketActor != null && __instance.CarryablesSocketActor.DoesAccept(carryable);
                return false;
            }
            catch
            {
                return true;
            }
        }

        private static void InitializeShipPostfix(ShipLoadout __instance, PlayerControlledShip __result)
        {
            try
            {
                CustomLoadouts.CheckRacksAfterSpawn(__instance, __result);
            }
            catch (Exception e)
            {
                Log.Failure("Checking the CUSTOM ship's storage racks", e);
            }
        }

        // ---------------------------------------------------------------- progression pause

        private static bool BlockRewardsPrefix()
        {
            return !ProgressionGuard.Block("mission rewards and XP");
        }

        private static bool BlockAchievementsPrefix()
        {
            return !ProgressionGuard.Block("achievements");
        }

        private static bool BlockQuestProgressPrefix()
        {
            return !ProgressionGuard.Block("quest progress");
        }
    }
}
