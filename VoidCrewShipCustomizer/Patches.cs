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
            core &= Patch("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "Display", nameof(DisplayPrefix), nameof(DisplayPostfix));
            // Progression must pause during CUSTOM runs (Void Crew modding rules), so this one is required too.
            core &= Patch("CG.Profile.PlayerRewardsCollection", "AddItem", nameof(BlockRewardsPrefix), null);

            // Shows CUSTOM (rather than the loadout standing in for it) as the room's choice everywhere in the hub. Required:
            // without it the game sends a CUSTOM id on to every player each time a quest is picked.
            core &= PatchSelectionNotice();
            // Back in the hub: the only place a CUSTOM run's pause, the closed room and the mission's state are reset, so
            // CUSTOM can't be offered without it.
            core &= Patch("HubShipManager", "Start", null, nameof(HubStartPostfix));
            Patch("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "OnDestroy", nameof(TerminalDestroyPrefix), null);
            Patch("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "OnLeaveUI", null, nameof(TerminalLeavePostfix));
            // Every player's terminal shows the room's choice when it changes, and when they start using the terminal, so
            // players setting up a ship together look at the same loadout (the game's terminal only marks a new choice).
            Patch("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "ShowSelection", null, nameof(ShowSelectionPostfix));
            Patch("UI.ShipLoadoutTerminal.ShipLoadoutTerminal", "OnEnterUI", null, nameof(TerminalEnterPostfix));
            // During a CUSTOM run the storage racks take any item, as the CUSTOM editor lets them start with any item.
            Patch("CG.Ship.Hull.CarryablesSocket", "DoesAccept", nameof(RackAcceptsPrefix), null);
            // A CUSTOM ship's racks hold exactly what was chosen, checked right after the ship spawns.
            Patch("ShipLoadout", "InitializeShip", null, nameof(InitializeShipPostfix));

            // The rest of progression, by kind. CUSTOM is only offered while every kind can be paused, so a game update that
            // takes away all of one kind's hooks turns CUSTOM off instead of letting that progression through (see Covered).
            // The game hands most of these calls along a chain of wrappers around the profile (Steam's, the cloud's, the
            // profile's own), so a hook anywhere in the chain stops the whole call.
            // Rewards: the required hook above stops them; the cloud's wrapper only passes them on to it.
            Patch("CG.Cloud.CloudRewardsProfileSync", "AddItem", nameof(BlockRewardsPrefix), null);
            bool progression = Covered("pausing achievements",
                Patch("CG.Profile.PlayerAchievements", "SetAchievementComplete", nameof(BlockAchievementsPrefix), null),
                Patch("SteamPlayerAchievementsSync", "SetAchievementComplete", nameof(BlockAchievementsPrefix), null),
                Patch("CG.Cloud.CloudPlayerAchievementsSync", "SetAchievementComplete", nameof(BlockAchievementsPrefix), null));
            progression &= Covered("pausing quest progress",
                Patch("CG.Profile.PlayerProfileData", "AddQuestProgress", nameof(BlockQuestProgressPrefix), null),
                Patch("CG.Cloud.CloudPlayerProfileDataSync", "AddQuestProgress", nameof(BlockQuestProgressPrefix), null),
                Patch("CG.Profile.PhotonPlayerDataSync", "AddQuestProgress", nameof(BlockQuestProgressPrefix), null));
            // Tracked stats (the profile's lifetime counters, Steam stats, and the achievements built on them). During a
            // mission every change goes through TrackedMetric, which also sets its Steam stat itself. As the mission ends every
            // stat goes to the chain's outer wrapper, Steam's, which sets the Steam stat before passing it on: the cloud's and
            // the profile's own hooks only keep the counters.
            progression &= Covered("pausing tracked stats and Steam stats during a mission",
                Patch("TrackedMetric", "OnMetricProgressChange", nameof(BlockStatsPrefix), null));
            progression &= Covered("pausing tracked stats and Steam stats as a mission ends",
                Patch("SteamPlayerTrackedMetrics", "SubmitProgress", nameof(BlockStatsPrefix), null));
            Patch("CG.Profile.PlayerTrackedMetrics", "SubmitProgress", nameof(BlockStatsPrefix), null);
            Patch("CloudPlayerTrackedMetricSync", "SubmitProgress", nameof(BlockStatsPrefix), null);
            // The daily and weekly quests count on their own and are saved when the mission ends.
            progression &= Covered("pausing daily and weekly quests",
                Patch("Metaquest", "OnMetricProgress", nameof(BlockDailyQuestsPrefix), null));
            // A saved CUSTOM run doesn't start while someone in the room is still without the mod (they join faster than
            // the room is checked: the start countdown is 30 s at most). Choosing it is refused too, which only says so sooner.
            progression &= Covered("holding a saved CUSTOM run until everyone has the mod",
                Patch("Gameplay.Hub.HubQuestManager", "StartQuest", nameof(StartQuestPrefix), null));
            Patch("Gameplay.Hub.HubQuestManager", "SelectPreservedSessionQuest", nameof(SelectSavedRunPrefix), null);

            CustomLoadouts.Available = core && progression;
            if (core && progression) Log.Info("All game patches are in; the CUSTOM loadout is available at the ship loadout terminal.");
            else if (core) Log.Error("Not all of progression can be paused during CUSTOM runs (see above), so the CUSTOM loadout is turned off: Void Crew's modding rules say such a run must not earn progression. The game itself is unaffected.");
            else Log.Error("Some patches the CUSTOM loadout needs could not be applied (see above), so it is turned off. The game itself is unaffected.");
        }

        /// <summary>
        /// One kind of progression, by whether its hooks went in: any one of them is enough (they sit in one chain of calls),
        /// none turns CUSTOM off. Every hook is applied before this is called, so the others stay in as a back-up.
        /// </summary>
        private static bool Covered(string what, params bool[] applied)
        {
            if (Array.IndexOf(applied, true) >= 0) return true;
            Log.Error("No game hook for " + what + " could be applied (the game may have changed).");
            return false;
        }

        /// <summary>HubShipManager.NotifyShipSelected, with the two private fields its replacement needs.</summary>
        private static bool PatchSelectionNotice()
        {
            try
            {
                // FieldRefAccess throws (rather than returning null) when the field isn't there.
                _currentShip = AccessTools.FieldRefAccess<HubShipManager, ShipLoadoutDataDef>("_currentShipSelected");
                _selectionChanged = AccessTools.Field(typeof(HubShipManager), "ShipSelectionChanged");
            }
            catch (Exception e)
            {
                Log.Error("Could not find HubShipManager's selection fields (the game may have changed): " + e.Message);
                return false;
            }
            if (_currentShip == null || _selectionChanged == null)
            {
                Log.Error("Could not find HubShipManager's selection fields (the game may have changed).");
                return false;
            }
            return Patch("HubShipManager", "NotifyShipSelected", nameof(NotifyShipSelectedPrefix), null);
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
        private static bool SelectShipPrefix(HubShipManager __instance, ref int[] guid, bool __runOriginal)
        {
            // Another plugin's prefix already refused this request (Void Crew Fleet: a ship 1 pick from a player of the
            // other team). HarmonyX still runs the later prefixes; CUSTOM must not be switched on or off for it.
            if (!__runOriginal) return false;
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
                        TerminalUi.OnSelectRefused();
                        return false;
                    }
                    // Void Crew Fleet 0.4.0: the host may make ship 1 a given type.
                    string wrongType = CustomLoadouts.WrongType(1, hull);
                    if (wrongType != null)
                    {
                        TerminalUi.Toast(wrongType);
                        TerminalUi.OnSelectRefused();
                        return false;
                    }
                    if (!PhotonNetwork.IsMasterClient)
                    {
                        // The game sends this choice to the host, whose Ship Customizer checks it again and selects the
                        // loadout CUSTOM stands in for. A host older than 0.5.0 keeps CUSTOM to itself.
                        if (RoomSync.HostSharesLayouts()) return true;
                        TerminalUi.Toast("Only the host can choose CUSTOM here: the host's Ship Customizer is older than 0.5.0.");
                        TerminalUi.OnSelectRefused();
                        return false;
                    }
                    if (!CustomLoadouts.EnsureRoomMarkedModded())
                    {
                        TerminalUi.Toast("Marking the room [mod_session] first; choose CUSTOM again in a moment.");
                        TerminalUi.OnSelectRefused();
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
                    TerminalUi.OnSelectRefused();
                    return false;
                }
                if (PhotonNetwork.IsMasterClient) CustomLoadouts.DeactivateAsHost();
            }
            catch (Exception e)
            {
                Log.Failure("Choosing a ship loadout", e);
                // Never let a CUSTOM id through to the game (and the other players) because something went wrong here.
                if (CustomLoadouts.LooksCustom(new GUIDUnion(guid)))
                {
                    TerminalUi.OnSelectRefused();
                    return false;
                }
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
                // Host: the players' last changes are on the ships (it never throws).
                RoomSync.ApplyPendingEdits();
                LoadoutStore.Flush(true);
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

        private static float _lastStartRefusal = -100f;

        /// <summary>
        /// Host: a saved CUSTOM run doesn't start while anyone in the room is still without the mod (a player who joined a
        /// moment ago and hasn't loaded yet counts too). The game tries again each frame, so it starts as soon as everyone
        /// is ready; until then the countdown stays at zero and the chat says why, every few seconds.
        /// </summary>
        private static bool StartQuestPrefix(Gameplay.Hub.HubQuestManager.SessionStartType startType)
        {
            try
            {
                if (!PhotonNetwork.IsMasterClient || startType != Gameplay.Hub.HubQuestManager.SessionStartType.Preserved) return true;
                PreservedGameSession saved = PlayerProfile.Instance.PreservedSession.PreservedSession;
                if (saved == null || !LoadoutStore.WasCustomRun(saved.GameSessionID)) return true;
                List<string> missing = RoomSync.PlayersWithoutMod(false);
                if (missing.Count == 0) return true;
                if (UnityEngine.Time.unscaledTime - _lastStartRefusal > 10f)
                {
                    _lastStartRefusal = UnityEngine.Time.unscaledTime;
                    Notice.Show("The saved CUSTOM run waits for everyone to have Ship Customizer. Not yet: " + string.Join(", ", missing.ToArray()) + ".");
                }
                return false;
            }
            catch (Exception e)
            {
                Log.Failure("Checking the saved CUSTOM run before it starts", e);
                return true;
            }
        }

        private static void HubStartPostfix(HubShipManager __instance)
        {
            CustomizerRuntime.GameReady = true;
            // Each on its own: one going wrong must not keep the others from resetting.
            try
            {
                ProgressionGuard.OnHubLoaded();
            }
            catch (Exception e)
            {
                Log.Failure("Turning progression back on in the hub", e);
            }
            try
            {
                RoomSync.ReopenAfterCustomMission();
            }
            catch (Exception e)
            {
                Log.Failure("Opening the room again after a CUSTOM mission", e);
            }
            try
            {
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
                // Void Crew Fleet 0.4.0: when the host makes the ship being set up a given type, only that type's CUSTOM.
                GUIDUnion forced = FleetLink.HullOfShip(CustomLoadouts.TerminalShip);
                if (!forced.IsNull && forced != loadouts[0].ShipGuid) return;
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

        /// <summary>A CUSTOM loadout is built just before the terminal shows it, when it changed since it was last shown.</summary>
        private static void DisplayPrefix(ShipLoadoutDataDef def)
        {
            try
            {
                CustomLoadouts.EnsureBuilt(def);
            }
            catch (Exception e)
            {
                Log.Failure("Building the CUSTOM loadout to show", e);
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

        /// <summary>The game told the terminal about ship 1's new choice (it only marks it in the list).</summary>
        private static void ShowSelectionPostfix(LoadoutTerminal __instance)
        {
            try
            {
                TerminalUi.OnShipOneChoiceShown(__instance);
            }
            catch (Exception e)
            {
                Log.Failure("Showing the room's choice on the loadout terminal", e);
            }
        }

        private static void TerminalEnterPostfix(LoadoutTerminal __instance)
        {
            try
            {
                TerminalUi.OnEnteredTerminal(__instance);
            }
            catch (Exception e)
            {
                Log.Failure("Showing the room's choice as the terminal is used", e);
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
                CustomLoadouts.OnShipSpawned(__instance, __result);
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

        private static bool BlockStatsPrefix()
        {
            return !ProgressionGuard.Block("tracked stats");
        }

        private static bool BlockDailyQuestsPrefix()
        {
            return !ProgressionGuard.Block("daily and weekly quest progress");
        }
    }
}
