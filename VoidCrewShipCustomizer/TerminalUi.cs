using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CG.Ship.Hull;
using HarmonyLib;
using Photon.Pun;
using ResourceAssets;
using UI.PowerTerminal;
using UI.ShipLoadoutTerminal;
using UnityEngine;
using UnityEngine.UIElements;
using LoadoutTerminal = UI.ShipLoadoutTerminal.ShipLoadoutTerminal;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// Everything the customizer adds to the hub's ship loadout terminal: the CUSTOM list entry, clickable hardpoints on
    /// the ship picture, a toolbar, and the pop-up pickers. The terminal's own code still draws the ship, the module
    /// icons, the systems list and the resources list for the CUSTOM loadout; this only adds to it.
    /// </summary>
    internal static class TerminalUi
    {
        private sealed class SlotHook
        {
            public Clickable Click;
            public EventCallback<PointerEnterEvent> Sound;
            public EventCallback<PointerEnterEvent> Enter;
            public EventCallback<PointerLeaveEvent> Leave;
            public PickingMode OriginalPicking;
        }

        private const string PlaceholderName = "ShipCustomizerEmptyHardpoint";
        private const string LockedClass = "select-displayer__locked";
        private static readonly Color Orange = new Color(1f, 0.6f, 0.2f, 1f);

        private static readonly MethodInfo DisplayMethod = AccessTools.Method(typeof(LoadoutTerminal), "Display");
        private static readonly Dictionary<SlotVE, SlotHook> Hooks = new Dictionary<SlotVE, SlotHook>();

        private static LoadoutTerminal _terminal;
        private static VisualElement _root;
        private static List<SlotVE> _slots;
        private static Button _selectButton;
        private static VisualElement _anchor;
        private static ShipLoadoutDataDef _shown;
        private static HullInfo _shownHull;
        private static VisualElement _toolbar;
        private static bool _selectLocked;
        private static PickerPanel _picker;
        /// <summary>The loadout as it was when the open list was made (which ship type and which ship), to catch a part that changed meanwhile.</summary>
        private static HullConfig _pickerSeen;
        private static GUIDUnion _pickerSeenHull;
        private static int _pickerSeenShip;
        private static StoragePanel _storage;
        private static VisualElement _toast;
        private static Label _toastLabel;
        private static int _toastVersion;
        private static bool _reportWritten;
        /// <summary>A redraw is asked for and comes on the next frame (see <see cref="RequestRedisplay"/>).</summary>
        private static bool _redisplayAsked;

        // ---------------------------------------------------------------- hooks called from Patches

        /// <summary>The terminal showed a loadout (after the ship picture, resources and systems were drawn).</summary>
        public static void OnTerminalDisplayed(LoadoutTerminal terminal, ShipLoadoutDataDef def, VisualElement root)
        {
            _terminal = terminal;
            if (terminal != null && terminal.ListEntryPrefab != null) Ui.ListEntryTemplate = terminal.ListEntryPrefab;
            if (root != null) _root = root;
            _shown = def;
            HullInfo hull;
            _shownHull = CustomLoadouts.TryGetHull(def, out hull) ? hull : null;
            if (_shownHull == null) CloseOverlays();
            else if (_storage != null && _storage.IsOpen) _storage.Refresh();
            UpdateToolbar();
            WriteReportOnce();
        }

        /// <summary>The ship picture was drawn for a loadout.</summary>
        public static void OnLayoutShown(ShipLoadout loadout, ShipLoadoutDataDef data, List<SlotVE> slots, Button selectButton, VisualElement anchor)
        {
            _slots = slots;
            _selectButton = selectButton;
            _anchor = anchor;
            foreach (SlotVE stale in Hooks.Keys.Where(s => slots == null || !slots.Contains(s)).ToList()) Hooks.Remove(stale);
            if (slots == null) return;

            HullInfo hull;
            if (CustomLoadouts.TryGetHull(data, out hull))
            {
                _shown = data;
                _shownHull = hull;
                DecorateSlots(hull, loadout);
                bool roomUsesIt = CustomLoadouts.RoomUses(hull);
                // A host on 0.5.0 or newer lets every player with the mod choose CUSTOM; older hosts keep it to themselves.
                bool hostOnly = !PhotonNetwork.IsMasterClient && !RoomSync.HostSharesLayouts();
                LockSelect(hostOnly, roomUsesIt ? null : "HOST ONLY", !roomUsesIt);
            }
            else
            {
                UndecorateSlots();
                LockSelect(false, null, false);
            }
            UpdateToolbar();
        }

        public static void OnTerminalDestroyed(LoadoutTerminal terminal)
        {
            if (terminal != _terminal) return;
            CloseOverlays();
            PickerPanel.ClearPool();
            _terminal = null;
            _root = null;
            _slots = null;
            _selectButton = null;
            _anchor = null;
            _shown = null;
            _shownHull = null;
            _toolbar = null;
            _toast = null;
            _redisplayAsked = false;
            Hooks.Clear();
            CustomLoadouts.ResetTerminalShip();
        }

        /// <summary>Another plugin switched the ship the terminal sets up: close pop-ups made for the other ship and redraw.</summary>
        public static void OnTerminalShipChanged()
        {
            CloseOverlays();
            if (_shownHull != null) RequestRedisplay();
            else UpdateToolbar();
        }

        /// <summary>
        /// Draws the terminal again once, on the next frame, however many changes ask for it at once: each draw runs the
        /// game's whole Display, which makes every module icon again (and the open storage window is refreshed by it).
        /// </summary>
        private static void RequestRedisplay()
        {
            if (_redisplayAsked || _root == null || _shownHull == null) return;
            _redisplayAsked = true;
            _root.schedule.Execute(() =>
            {
                _redisplayAsked = false;
                try
                {
                    if (_shownHull != null) Redisplay();
                }
                catch (Exception e)
                {
                    Log.Failure("Refreshing the loadout terminal", e);
                }
            });
        }

        /// <summary>The player stepped away from the terminal.</summary>
        public static void OnLeftTerminal()
        {
            CloseOverlays();
        }

        /// <summary>Draws the CUSTOM row of the loadout list (instead of the game's binding, which expects a real unlock).</summary>
        public static void BindListEntry(VisualElement item, ShipLoadoutDataDef def)
        {
            item.AddToClassList("metem-list-entry");
            item.SetEnabled(true);
            item.focusable = true;
            item.name = CustomLoadouts.Header;
            var label = item.Q<Label>("Title");
            if (label != null) label.text = CustomLoadouts.Header;
        }

        /// <summary>A CUSTOM loadout changed on another player's terminal (or the host changed): redraw what shows it.</summary>
        public static void OnLoadoutChangedRemotely()
        {
            RequestRedisplay();
        }

        /// <summary>The host's CUSTOM choice for ship 1 changed (seen through the room); redraw what depends on it.</summary>
        public static void OnSelectionChangedRemotely()
        {
            try
            {
                HubShipManager manager = HubShipManager.Instance;
                if (manager != null) manager.NotifyShipSelected(manager.SelectedShipGuid.AsIntArray());
            }
            catch (Exception e)
            {
                Log.Failure("Showing the room's choice", e);
            }
            RequestRedisplay();
        }

        // ---------------------------------------------------------------- the hardpoints on the ship picture

        private static void DecorateSlots(HullInfo hull, ShipLoadout loadout)
        {
            if (_slots.Count < hull.Sockets.Count)
            {
                Log.WarnOnce("slot count " + hull.ShipGuid, string.Format("The terminal draws {0} hardpoint positions for the {1}, but the ship has {2}; only the ones it draws can be changed.",
                    _slots.Count, hull.Name, hull.Sockets.Count));
            }

            // What the terminal actually drew a module icon for (entries the game could not spawn were left out).
            var fitted = new HashSet<int>(loadout.Sockets.Where(s => s != null && s.SocketId >= 0 && s.SocketId < hull.Sockets.Count).Select(s => s.SocketId));
            HashSet<int> offered = Hardpoints.OfferedEmpty(hull.Sockets, fitted, _slots.Count);

            for (int i = 0; i < _slots.Count; i++)
            {
                SlotVE slot = _slots[i];
                if (slot == null) continue;
                RemovePlaceholder(slot);
                bool isFitted = fitted.Contains(i);
                bool isOffered = offered.Contains(i);
                slot.style.display = isFitted || isOffered ? DisplayStyle.Flex : DisplayStyle.None;
                if (isOffered)
                {
                    VisualElement frame = slot.parent;
                    bool frameShowsModule = frame != null && frame.Children().OfType<SlotVE>().Any(s => fitted.Contains(_slots.IndexOf(s)));
                    if (frame != null && !frameShowsModule)
                    {
                        frame.RemoveFromClassList("slot-color__red");
                        frame.RemoveFromClassList("slot-color__green");
                    }
                    slot.Add(MakePlaceholder());
                }
                Hook(slot, i);
            }
        }

        private static void UndecorateSlots()
        {
            foreach (SlotVE slot in _slots)
            {
                if (slot == null) continue;
                RemovePlaceholder(slot);
                Unhook(slot);
                // The game only ever shows positions; after a CUSTOM loadout, hide the ones this loadout leaves empty.
                if (slot.childCount == 0) slot.style.display = DisplayStyle.None;
            }
        }

        private static VisualElement MakePlaceholder()
        {
            var placeholder = new VisualElement { name = PlaceholderName };
            placeholder.style.flexGrow = 1;
            placeholder.style.width = new Length(100, LengthUnit.Percent);
            placeholder.style.height = new Length(100, LengthUnit.Percent);
            placeholder.style.minWidth = 30;
            placeholder.style.minHeight = 30;
            placeholder.style.alignItems = Align.Center;
            placeholder.style.justifyContent = Justify.Center;
            placeholder.style.backgroundColor = new Color(0.03f, 0.03f, 0.03f, 0.6f);
            Ui.Border(placeholder, 2, new Color(1f, 1f, 1f, 0.6f));
            Ui.Radius(placeholder, 4);
            placeholder.Add(Ui.Label("+", 26, new Color(1f, 1f, 1f, 0.9f), true));
            return placeholder;
        }

        private static void RemovePlaceholder(SlotVE slot)
        {
            VisualElement placeholder = slot.Q(PlaceholderName);
            if (placeholder != null) placeholder.RemoveFromHierarchy();
        }

        private static void Hook(SlotVE slot, int index)
        {
            if (Hooks.ContainsKey(slot)) return;
            var hook = new SlotHook { OriginalPicking = slot.pickingMode };
            slot.pickingMode = PickingMode.Position;
            hook.Click = Ui.ClickOnly(slot, () => OnSlotClicked(_shownHull, index, false));
            hook.Sound = Ui.HoverSound(slot, "list");
            hook.Enter = e => slot.style.scale = new Scale(new Vector3(1.2f, 1.2f, 1f));
            hook.Leave = e => slot.style.scale = new Scale(Vector3.one);
            slot.RegisterCallback(hook.Enter);
            slot.RegisterCallback(hook.Leave);
            Hooks[slot] = hook;
        }

        private static void Unhook(SlotVE slot)
        {
            SlotHook hook;
            if (!Hooks.TryGetValue(slot, out hook)) return;
            slot.RemoveManipulator(hook.Click);
            slot.UnregisterCallback(hook.Sound);
            slot.UnregisterCallback(hook.Enter);
            slot.UnregisterCallback(hook.Leave);
            slot.pickingMode = hook.OriginalPicking;
            slot.style.scale = StyleKeyword.Null;
            Hooks.Remove(slot);
        }

        /// <param name="hull">The ship type the click was meant for: the shown one for the ship picture, or the one a list was made for.</param>
        /// <param name="fromList">Opened from the HARDPOINTS list: a pick goes back to the list, where it was.</param>
        private static void OnSlotClicked(HullInfo hull, int index, bool fromList)
        {
            if (hull == null || hull != _shownHull || index < 0 || index >= hull.Sockets.Count) return;
            if (!CustomLoadouts.CanEdit(hull))
            {
                Toast(CustomLoadouts.WhyNotEditable());
                return;
            }
            if (fromList && _picker != null) _hardpointListScroll = _picker.ScrollOffset;
            OpenSlotPicker(hull, hull.Sockets[index], fromList);
        }

        /// <summary>How far the HARDPOINTS list was scrolled when a hardpoint was picked from it.</summary>
        private static Vector2 _hardpointListScroll;

        private static void OpenSlotPicker(HullInfo hull, SocketInfo socket, bool fromList)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            string currentCode;
            config.Slots.TryGetValue(socket.Index, out currentCode);
            Option current = null;
            if (currentCode != null) Catalog.TryGetOption(currentCode, out current);

            var entries = new List<PickerPanel.Entry>();
            if (currentCode != null)
            {
                entries.Add(new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "EMPTY THIS HARDPOINT",
                    Detail = "Take out the " + (current != null ? current.Name : "module") + " and leave the hardpoint free.",
                    OnPick = () => Edit(hull, c =>
                    {
                        c.Slots.Remove(socket.Index);
                        c.Contents.Remove(socket.Index);
                    }, LayoutMerge.HardpointPart(socket.Group))
                });
            }

            foreach (Option option in Catalog.SlotOptions)
            {
                // A Fleet Battle never offers some things (Placeholder weapons, extra fabricators): they aren't listed.
                if (MatchRules.Hidden(option)) continue;
                SocketInfo target = TargetFor(option, socket, hull.Sockets, config);
                if (target == null) continue;
                List<int> displaced = Hardpoints.Displaced(target, config.Slots.Keys);
                bool isCurrent = target.Index == socket.Index && currentCode == option.Code;
                bool refused = MatchRules.WhyRefused(option) != null;

                var entry = new PickerPanel.Entry
                {
                    Name = option.Name,
                    Detail = DetailFor(option, socket, target),
                    Icon = option.Icon,
                    Accent = Ui.RarityColor(option.Rarity),
                    Category = option.Category,
                    Current = isCurrent,
                    Tooltip = option.Context,
                    TooltipGuid = option.Guid
                };
                string blocker = SingleInstanceBlocker(option, config, displaced);
                if (isCurrent)
                {
                    entry.Tag = refused ? "FITTED · " + MatchRules.NotAllowedTag : "FITTED";
                    if (refused) entry.TagColor = Ui.Warning;
                    entry.OnPick = () => { };
                }
                else if (refused)
                {
                    // The host's match rules refuse it: shown greyed out, so players see why it can't be picked.
                    entry.Enabled = false;
                    entry.Tag = MatchRules.NotAllowedTag;
                    entry.TagColor = Ui.Warning;
                }
                else if (blocker != null)
                {
                    entry.Enabled = false;
                    entry.Tag = blocker;
                    entry.TagColor = Ui.Warning;
                }
                else
                {
                    List<string> replaced = displaced.Where(k => k != socket.Index).Select(k => NameOf(config.Slots[k])).ToList();
                    if (replaced.Count > 0)
                    {
                        entry.Tag = "REPLACES " + string.Join(", ", replaced.ToArray()).ToUpperInvariant();
                        entry.TagColor = Orange;
                    }
                    Option chosen = option;
                    SocketInfo chosenTarget = target;
                    entry.OnPick = () => Fit(hull, chosen, chosenTarget);
                }
                entries.Add(entry);
            }

            if (fromList)
            {
                // After a pick, back to the HARDPOINTS list, scrolled where it was, for the next hardpoint.
                foreach (PickerPanel.Entry entry in entries.Where(e => e.OnPick != null))
                {
                    Action pick = entry.OnPick;
                    entry.OnPick = () =>
                    {
                        pick();
                        OpenHardpointList(hull);
                        if (_picker != null) _picker.ScrollTo(_hardpointListScroll);
                    };
                }
            }
            if (current != null && Catalog.ContentSlotsOf(current).Count > 0)
            {
                // Not wrapped above: it opens the module's slots rather than changing anything.
                entries.Insert(Math.Min(1, entries.Count), new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "CONTENTS",
                    Detail = "What the " + current.Name + " starts loaded with" + ContentsNote(hull, config, socket.Index, current) + ".",
                    Tag = "CHOOSE",
                    OnPick = () => OpenContentsPicker(hull, socket)
                });
            }
            string title = SizeTitle(socket.Size);
            string subtitle = (current != null ? "Fitted: " + current.Name + ". Pick a replacement, or empty it." : "Empty. Pick what to fit here.") +
                              " " + CustomLoadouts.PowerLine(hull, config) + ".";
            subtitle += BreaksRulesNote(current);
            if (socket.Overlaps.Any(i => hull.Sockets[i].Size < socket.Size)) subtitle += " A smaller module takes one part of this hardpoint.";
            if (Settings.AllowAnySize.Value) subtitle += " (Any size allowed: see the AllowAnySize setting.)";
            OpenPicker(title, subtitle, entries, current != null ? current.Category : null);
        }

        /// <summary>For a picker's subtitle: why the current choice breaks this match's rules, or "".</summary>
        private static string BreaksRulesNote(Option current)
        {
            string why = current != null ? MatchRules.WhyRefused(current) : null;
            return why == null ? "" : " Not allowed in this match (" + why.TrimEnd('.') + "), so it comes off when the mission starts.";
        }

        /// <summary>For lists: what a fitted module starts loaded with ("  ·  LOADED: ..."), or "" when that is nothing as usual.</summary>
        private static string ContentsNote(HullInfo hull, HullConfig config, int position, Option module)
        {
            if (module == null) return "";
            List<string> codes = null;
            bool chosen = config.Contents != null && config.Contents.TryGetValue(position, out codes) && codes != null;
            if (!chosen) codes = Catalog.UsualContents(module);
            var names = new List<string>();
            foreach (string code in codes)
            {
                Option item;
                if (!string.IsNullOrEmpty(code) && Catalog.TryGetOption(code, out item)) names.Add(RichText.Plain(item.Name));
            }
            if (names.Count == 0) return chosen ? "  ·  LOADED: nothing" : "";
            return "  ·  LOADED: " + (names.Count <= 2 ? string.Join(", ", names.ToArray()) : names[0] + ", " + names[1] + " +" + (names.Count - 2));
        }

        /// <summary>
        /// What a fitted module or weapon starts loaded with: each of its item slots (ammo or batteries, weapon mods, power
        /// cells, payloads) set to something that slot takes, or all of them to the module's usual contents.
        /// </summary>
        private static void OpenContentsPicker(HullInfo hull, SocketInfo socket)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            string code;
            Option module;
            if (!config.Slots.TryGetValue(socket.Index, out code) || !Catalog.TryGetHardpointOption(code, out module)) return;
            List<ContentSlot> slots = Catalog.ContentSlotsOf(module);
            List<string> chosen;
            bool own = config.Contents.TryGetValue(socket.Index, out chosen) && chosen != null;
            List<string> usual = Catalog.UsualContents(module);
            List<string> shown = own ? chosen : usual;
            string part = LayoutMerge.HardpointPart(socket.Group);
            string usualNote = ContentsNote(hull, new HullConfig(), socket.Index, module);

            var entries = new List<PickerPanel.Entry>
            {
                new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "USUAL CONTENTS",
                    Detail = usualNote.Length > 0 ? "As the game's own loadouts load it" + usualNote + "." : "As the game's own loadouts load it: nothing.",
                    Current = !own,
                    Tag = own ? "" : "NOW",
                    OnPick = () => ChangeContents(hull, socket, c => c.Contents.Remove(socket.Index), part)
                },
                new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "EMPTY",
                    Detail = "Start with nothing loaded.",
                    Current = own && chosen.All(string.IsNullOrEmpty),
                    OnPick = () => ChangeContents(hull, socket, c => c.Contents[socket.Index] = Enumerable.Repeat("", slots.Count).ToList(), part)
                }
            };
            for (int j = 0; j < slots.Count; j++)
            {
                int index = j;
                Option item = null;
                bool filled = j < shown.Count && !string.IsNullOrEmpty(shown[j]) && Catalog.TryGetOption(shown[j], out item);
                string why = filled ? MatchRules.WhyRefused(item) : null;
                entries.Add(new PickerPanel.Entry
                {
                    Name = "SLOT " + (j + 1) + "  ·  " + Catalog.KindOf(slots[j]),
                    Detail = filled ? item.Name + (why != null ? "  ·  not allowed in this match" : "") : "Empty",
                    Icon = filled ? item.Icon : null,
                    Accent = filled ? Ui.RarityColor(item.Rarity) : Ui.TextDim,
                    Tag = why != null ? "NOT ALLOWED" : "CHANGE",
                    TagColor = why != null ? Ui.Warning : Ui.Gold,
                    Tooltip = filled ? item.Context : null,
                    TooltipGuid = filled ? item.Guid : GUIDUnion.Empty(),
                    OnPick = () => OpenContentItemPicker(hull, socket, index)
                });
            }
            if (slots.Count == 0)
            {
                entries.Add(new PickerPanel.Entry { Name = "NO ITEM SLOTS", Detail = "The " + module.Name + " doesn't hold any items.", Info = true });
            }
            string subtitle = "What the " + module.Name + " starts loaded with. Each slot only takes what fits it" +
                              (own ? "." : "; now its usual contents.") + (MatchRules.InMatch ? " In a Fleet Battle the match rules apply too." : "");
            OpenPicker("CONTENTS · " + RichText.Plain(module.Name).ToUpperInvariant(), subtitle, entries, null);
        }

        /// <summary>Every item one slot of a fitted module takes (what fits it, as the game decides when you load it by hand).</summary>
        private static void OpenContentItemPicker(HullInfo hull, SocketInfo socket, int slotIndex)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            string code;
            Option module;
            if (!config.Slots.TryGetValue(socket.Index, out code) || !Catalog.TryGetHardpointOption(code, out module)) return;
            List<ContentSlot> slots = Catalog.ContentSlotsOf(module);
            if (slotIndex >= slots.Count) return;
            ContentSlot slot = slots[slotIndex];
            List<string> chosen;
            List<string> shown = config.Contents.TryGetValue(socket.Index, out chosen) && chosen != null ? chosen : Catalog.UsualContents(module);
            string currentCode = slotIndex < shown.Count ? shown[slotIndex] : "";
            string part = LayoutMerge.HardpointPart(socket.Group);

            var entries = new List<PickerPanel.Entry>
            {
                new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "EMPTY SLOT",
                    Detail = "Nothing in this slot.",
                    Current = string.IsNullOrEmpty(currentCode),
                    OnPick = () => SetContent(hull, socket, slotIndex, "", part)
                }
            };
            foreach (Option item in Catalog.ItemOptions.Where(o => o.Kind == OptionKind.Item && !MatchRules.Hidden(o) && Catalog.ContentSlotAccepts(slot, o)))
            {
                bool isCurrent = item.Code == currentCode;
                bool refused = MatchRules.WhyRefused(item) != null;
                string itemCode = item.Code;
                var entry = new PickerPanel.Entry
                {
                    Name = item.Name,
                    Detail = Ui.RarityName(item.Rarity),
                    Icon = item.Icon,
                    Accent = Ui.RarityColor(item.Rarity),
                    Category = item.Category,
                    Current = isCurrent,
                    Tag = isCurrent ? (refused ? "IN THIS SLOT · " + MatchRules.NotAllowedTag : "IN THIS SLOT") : (refused ? MatchRules.NotAllowedTag : ""),
                    TagColor = refused ? Ui.Warning : Ui.Gold,
                    Tooltip = item.Context,
                    TooltipGuid = item.Guid,
                    OnPick = () => SetContent(hull, socket, slotIndex, itemCode, part)
                };
                if (refused && !isCurrent) entry.Enabled = false;
                entries.Add(entry);
            }
            Option currentItem;
            string tab = !string.IsNullOrEmpty(currentCode) && Catalog.TryGetOption(currentCode, out currentItem) ? currentItem.Category : null;
            OpenPicker("SLOT " + (slotIndex + 1) + "  ·  " + Catalog.KindOf(slot) + "  ·  " + RichText.Plain(module.Name).ToUpperInvariant(),
                "What this slot of the " + module.Name + " starts with: only what fits it is listed.", entries, tab);
        }

        /// <summary>Sets one slot of a module's contents (the first change keeps the module's other slots as they usually are).</summary>
        private static void SetContent(HullInfo hull, SocketInfo socket, int slotIndex, string code, string part)
        {
            ChangeContents(hull, socket, c =>
            {
                string moduleCode;
                Option module;
                if (!c.Slots.TryGetValue(socket.Index, out moduleCode) || !Catalog.TryGetHardpointOption(moduleCode, out module)) return;
                int count = Catalog.ContentSlotsOf(module).Count;
                List<string> inside;
                if (!c.Contents.TryGetValue(socket.Index, out inside) || inside == null)
                {
                    inside = Catalog.UsualContents(module).Take(count).ToList();
                    c.Contents[socket.Index] = inside;
                }
                while (inside.Count < count) inside.Add("");
                if (slotIndex < inside.Count) inside[slotIndex] = code;
            }, part);
        }

        /// <summary>Changes a module's contents, then shows its slots again.</summary>
        private static void ChangeContents(HullInfo hull, SocketInfo socket, Action<HullConfig> change, string part)
        {
            Edit(hull, change, part);
            OpenContentsPicker(hull, socket);
        }

        /// <summary>Which position a module would go in when picked from this hardpoint, or null if it doesn't fit.</summary>
        private static SocketInfo TargetFor(Option option, SocketInfo clicked, List<SocketInfo> sockets, HullConfig config)
        {
            if (Settings.AllowAnySize.Value) return clicked;
            if (!option.HasSize) return null;
            return Hardpoints.TargetFor(option.Size, clicked, sockets, config.Slots.Keys, _slots != null ? _slots.Count : sockets.Count);
        }

        /// <summary>The game allows only one of some modules per ship (BuildingConstraints.AllowOnlySingleInstance).</summary>
        private static string SingleInstanceBlocker(Option option, HullConfig config, List<int> displaced)
        {
            if (!option.SingleInstance || option.ModuleType == null) return null;
            foreach (KeyValuePair<int, string> slot in config.Slots)
            {
                if (displaced.Contains(slot.Key)) continue;
                Option other;
                if (Catalog.TryGetOption(slot.Value, out other) && other.ModuleType == option.ModuleType) return "ONE PER SHIP";
            }
            return null;
        }

        private static string DetailFor(Option option, SocketInfo clicked, SocketInfo target)
        {
            var parts = new List<string>();
            if (option.HasSize) parts.Add(Catalog.SizeName(option.Size));
            string rarity = Ui.RarityName(option.Rarity);
            if (rarity.Length > 0) parts.Add(rarity);
            if (option.PowerMade > 0) parts.Add("MAKES " + option.PowerMade + " POWER");
            if (option.PowerUse > 0) parts.Add("POWER " + option.PowerUse);
            if (target != clicked) parts.Add(target.Size < clicked.Size ? "takes part of this hardpoint" : "takes the whole hardpoint");
            return string.Join("  ·  ", parts.ToArray());
        }

        private static void Fit(HullInfo hull, Option option, SocketInfo target)
        {
            Edit(hull, c =>
            {
                foreach (int key in Hardpoints.Displaced(target, c.Slots.Keys))
                {
                    c.Slots.Remove(key);
                    c.Contents.Remove(key);
                }
                c.Slots[target.Index] = option.Code;
                // A new module starts with its own usual contents.
                c.Contents.Remove(target.Index);
            }, LayoutMerge.HardpointPart(target.Group));
        }

        private static string SizeTitle(BuildSize size)
        {
            switch (size)
            {
                case BuildSize.Small: return "SMALL HARDPOINT (1x1)";
                case BuildSize.Medium: return "MEDIUM HARDPOINT (2x1)";
                default: return "LARGE HARDPOINT (2x2)";
            }
        }

        private static string NameOf(string code)
        {
            Option option;
            return Catalog.TryGetOption(code, out option) ? option.Name : "module";
        }

        // ---------------------------------------------------------------- toolbar

        /// <summary>
        /// The CUSTOM buttons sit in the empty space above the SELECT button, two to a row. They are added last to the
        /// terminal's right-hand panel, so they are drawn (and clicked) on top of the ship picture and the panels.
        /// </summary>
        private static void UpdateToolbar()
        {
            VisualElement host = _anchor != null ? _anchor.parent : null;
            if (_shownHull == null || host == null)
            {
                if (_toolbar != null) _toolbar.RemoveFromHierarchy();
                return;
            }
            if (_toolbar == null)
            {
                _toolbar = new VisualElement { name = "ShipCustomizerToolbar" };
                _toolbar.style.position = Position.Absolute;
                _toolbar.style.right = ToolbarRight;
                _toolbar.style.width = ToolbarWidth;
                _toolbar.style.flexDirection = FlexDirection.Row;
                _toolbar.style.flexWrap = Wrap.Wrap;
                _toolbar.style.justifyContent = Justify.SpaceBetween;
                _toolbar.pickingMode = PickingMode.Ignore;
            }
            _toolbar.Clear();
            HullInfo hull = _shownHull;
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            bool editable = CustomLoadouts.CanEdit(hull);
            // In a Fleet Battle the MATCH RULES button goes above the section, so its header stays level with the terminal's.
            bool match = MatchRules.InMatch;
            _toolbar.style.top = ToolbarTop - (match ? ToolbarButtonHeight + 5f : 0f);
            if (match)
            {
                int broken = MatchRules.Check(hull, config, false).Count;
                Button rules = AddToolbarButton(broken > 0 ? "BREAKS MATCH RULES (" + broken + ")" : "MATCH RULES", () => OpenRulesPicker(hull));
                rules.style.width = ToolbarWidth;
                if (broken > 0) rules.style.color = Ui.Warning;
            }
            var notes = new List<string>();
            // Void Crew Fleet's ships by name ("SHIP 2", "BLUE 1"); nothing of the kind without Fleet.
            int ship = CustomLoadouts.TerminalShip;
            if (ship >= 2 || FleetLink.IsTeamMatch) notes.Add(FleetLink.ShipName(ship));
            if (CustomLoadouts.WrongType(ship, hull) != null) notes.Add("WRONG SHIP TYPE");
            if (!editable) notes.Add("VIEW ONLY");
            else if (CustomLoadouts.EditsGoToHost(hull) || RoomSync.SharingLayouts) notes.Add("SHARED");
            AddToolbarHeader("CUSTOM", string.Join(" · ", notes.ToArray()), TeamColor() ?? Ui.Gold);
            AddToolbarButton("HARDPOINTS", () => OpenHardpointList(hull));
            AddToolbarButton("STORAGE (" + config.StoredCount + ")", () => OpenStorage(hull));
            if (editable)
            {
                AddToolbarButton("HOMUNCULUS", () => OpenHomunculusPicker(hull));
                AddToolbarButton("FABRICATOR", () => OpenFabricatorPicker(hull));
            }
            // Saving keeps a copy on this PC, so it works on a loadout this player may only look at, too.
            AddToolbarButton("SAVE LOADOUT", () => OpenSavePicker(hull));
            if (editable) AddToolbarButton("LOAD LOADOUT", () => OpenLoadPicker(hull, false));
            if (_toolbar.parent != host)
            {
                _toolbar.RemoveFromHierarchy();
                host.Add(_toolbar);
            }
            _toolbar.BringToFront();
            foreach (VisualElement child in _toolbar.Children()) Ui.Announce(child);
        }

        // The section sits above SELECT, its header level with the terminal's DESCRIPTION and HOMUNCULUS headers
        // (y 745 on the 1920 x 1080 screen; the right-hand panel starts at y 50). Its height is fixed so the last row of
        // buttons always ends just above SELECT (y 921).
        private const float ToolbarRight = 29f;
        private const float ToolbarTop = 695f;
        private const float ToolbarWidth = 362f;
        private const float ToolbarHeaderHeight = 52f;
        private const float ToolbarButtonHeight = 30f;

        /// <summary>In a Void Crew Fleet battle, the team colour of the ship the terminal is setting up; otherwise null.</summary>
        private static Color? TeamColor()
        {
            return FleetLink.ShipColor(CustomLoadouts.TerminalShip);
        }

        /// <summary>The section header, styled like the terminal's own (its classes), with the game's decorative line under it.</summary>
        private static void AddToolbarHeader(string title, string note, Color noteColor)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.style.width = new Length(100, LengthUnit.Percent);
            row.style.height = ToolbarHeaderHeight;
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.justifyContent = Justify.SpaceBetween;
            Label header = Ui.GameLabel(title, "ui-text-size__large", "ui-text-color__white", "loadout-panel-header", "ui-text-dropshadow");
            header.style.flexShrink = 0;
            row.Add(header);
            if (!string.IsNullOrEmpty(note))
            {
                Label noteLabel = Ui.Label(note, 17, noteColor, true);
                noteLabel.style.flexShrink = 1;
                noteLabel.style.marginLeft = 10;
                noteLabel.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(noteLabel);
            }
            _toolbar.Add(row);

            var line = new VisualElement { pickingMode = PickingMode.Ignore };
            line.AddToClassList("metem-decorative__box-border");
            line.style.width = new Length(100, LengthUnit.Percent);
            line.style.height = 10;
            line.style.marginBottom = 4;
            _toolbar.Add(line);
        }

        /// <summary>The game's small button (the small version of SELECT), sized to fit two to a row.</summary>
        private static Button AddToolbarButton(string text, Action onClick)
        {
            Button button = Ui.GameButton(text, onClick, "metem-button-size__small");
            button.style.width = (ToolbarWidth - 6f) / 2f;
            button.style.height = ToolbarButtonHeight;
            button.style.minWidth = 0;
            button.style.minHeight = 0;
            Ui.Margin(button, 0, 0, 5, 0);
            button.style.paddingLeft = 4;
            button.style.paddingRight = 4;
            button.style.paddingTop = 0;
            button.style.paddingBottom = 0;
            button.style.fontSize = 17;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            _toolbar.Add(button);
            return button;
        }

        /// <summary>Opens the place a match-rules problem is: its hardpoint, its rack slot, the homunculus or the fabricator.</summary>
        private static void GoTo(HullInfo hull, MatchRules.Issue issue)
        {
            if (issue.Hardpoint >= 0 && issue.Hardpoint < hull.Sockets.Count)
            {
                if (issue.Where.EndsWith("LOADED")) OpenContentsPicker(hull, hull.Sockets[issue.Hardpoint]);
                else OnSlotClicked(hull, issue.Hardpoint, false);
                return;
            }
            ShelfInfo shelf = hull.Shelves.FirstOrDefault(s => s.CoreIndex == issue.Rack);
            if (shelf != null && issue.Slot >= 0)
            {
                OpenStorage(hull);
                OpenItemPicker(hull, shelf, issue.Slot);
                return;
            }
            if (issue.Part == LayoutMerge.Homunculus) OpenHomunculusPicker(hull);
            else OpenFabricatorPicker(hull);
        }

        /// <summary>
        /// In a Fleet Battle: what the shown CUSTOM loadout has that this match doesn't allow (the host takes it off when
        /// the mission starts; a player who may change the loadout can take it off now), and the match's rules.
        /// </summary>
        private static void OpenRulesPicker(HullInfo hull)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            List<MatchRules.Issue> issues = MatchRules.Check(hull, config, false);
            bool editable = CustomLoadouts.CanEdit(hull);
            var entries = new List<PickerPanel.Entry>();
            if (issues.Count > 0 && editable)
            {
                entries.Add(new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "TAKE OFF WHAT BREAKS THE RULES",
                    Detail = "Now, instead of when the mission starts. Your saved loadouts keep everything.",
                    Tag = issues.Count + (issues.Count == 1 ? " THING" : " THINGS"),
                    TagColor = Ui.Warning,
                    OnPick = () => Edit(hull, c => MatchRules.Check(hull, c, true))
                });
            }
            foreach (MatchRules.Issue issue in issues)
            {
                MatchRules.Issue shown = issue;
                entries.Add(new PickerPanel.Entry
                {
                    Name = issue.Name,
                    Detail = issue.Where + "  ·  " + issue.Why,
                    Icon = issue.Option != null ? issue.Option.Icon : null,
                    Accent = Ui.Warning,
                    Category = "BREAKS THE RULES",
                    Tag = editable ? "GO THERE" : "COMES OFF",
                    TagColor = Ui.Warning,
                    // Someone who may change the loadout can go straight to that hardpoint, slot or setting.
                    Info = !editable,
                    OnPick = () => GoTo(hull, shown)
                });
            }
            foreach (KeyValuePair<string, string> rule in MatchRules.Describe())
            {
                entries.Add(new PickerPanel.Entry { Name = rule.Key, Detail = rule.Value, DetailBright = true, Category = "THE RULES", Info = true });
            }
            string subtitle = issues.Count == 0
                ? "This CUSTOM loadout keeps to the rules of this Fleet Battle."
                : "When the mission starts, the host takes off what breaks this Fleet Battle's rules" + (editable ? ". You can also take it off now." : ".");
            OpenPicker("MATCH RULES", subtitle, entries, issues.Count > 0 ? "BREAKS THE RULES" : "THE RULES");
        }

        /// <summary>
        /// Every hardpoint as a big row, with what is fitted there: the same choices as clicking the ship picture,
        /// without having to hit a small icon (hard with a head-aimed pointer in VR).
        /// </summary>
        private static void OpenHardpointList(HullInfo hull)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            int drawn = _slots != null ? Math.Min(_slots.Count, hull.Sockets.Count) : hull.Sockets.Count;
            var fitted = new HashSet<int>(config.Slots.Keys.Where(k => k >= 0 && k < drawn));
            HashSet<int> offered = Hardpoints.OfferedEmpty(hull.Sockets, fitted, drawn);
            bool editable = CustomLoadouts.CanEdit(hull);

            var entries = new List<PickerPanel.Entry>();
            foreach (SocketInfo socket in hull.Sockets.Where(s => fitted.Contains(s.Index) || offered.Contains(s.Index))
                         .OrderBy(s => s.Group).ThenByDescending(s => (int)s.Size).ThenBy(s => s.Index))
            {
                Option option = null;
                string code;
                if (config.Slots.TryGetValue(socket.Index, out code)) Catalog.TryGetOption(code, out option);
                string rarity = option != null ? Ui.RarityName(option.Rarity) : "";
                bool refused = option != null && MatchRules.WhyRefused(option) != null;
                SocketInfo chosen = socket;
                entries.Add(new PickerPanel.Entry
                {
                    Name = option != null ? option.Name : "EMPTY",
                    // Numbered, because several hardpoints of a ship can share a position name.
                    Detail = "HARDPOINT " + (socket.Group + 1) + "  ·  " + PositionName(hull, socket) + "  ·  " + Catalog.SizeName(socket.Size) +
                             (rarity.Length > 0 ? "  ·  " + rarity : "") + ContentsNote(hull, config, socket.Index, option),
                    Icon = option != null ? option.Icon : null,
                    Accent = option != null ? Ui.RarityColor(option.Rarity) : Ui.TextDim,
                    Tag = refused ? "NOT ALLOWED" : (!editable ? "" : (option == null ? "+ FIT" : "CHANGE")),
                    TagColor = refused ? Ui.Warning : Ui.Gold,
                    Tooltip = option != null ? option.Context : null,
                    TooltipGuid = option != null ? option.Guid : GUIDUnion.Empty(),
                    // Someone who may only look gets a list to read, not buttons that lead nowhere.
                    Info = !editable,
                    OnPick = () => OnSlotClicked(hull, chosen.Index, true)
                });
            }
            OpenPicker("HARDPOINTS · " + hull.Name.ToUpperInvariant(),
                (editable ? "Pick a hardpoint to change what is fitted there. Port is the ship's left, starboard its right. " : CustomLoadouts.WhyNotEditable() + " ") +
                CustomLoadouts.PowerLine(hull, config) + ".",
                entries, null);
        }

        /// <summary>Where a hardpoint is on the ship, from its position in the ship prefab (x is starboard, z is forward).</summary>
        private static string PositionName(HullInfo hull, SocketInfo socket)
        {
            if (socket.Cells.Length == 0) return "HARDPOINT " + (socket.Group + 1);
            Vector3 centre = Vector3.zero;
            foreach (Vector3 cell in socket.Cells) centre += cell;
            centre /= socket.Cells.Length;
            float minZ = hull.Sockets.SelectMany(s => s.Cells).Min(c => c.z);
            float maxZ = hull.Sockets.SelectMany(s => s.Cells).Max(c => c.z);
            float along = maxZ - minZ > 0.1f ? (centre.z - minZ) / (maxZ - minZ) : 0.5f;
            string side = centre.x < -1f ? "PORT" : (centre.x > 1f ? "STARBOARD" : "CENTRE");
            string length = along > 0.8f ? "BOW" : (along > 0.55f ? "FORWARD" : (along > 0.3f ? "MIDSHIPS" : "AFT"));
            return side + ", " + length;
        }

        private static void OpenHomunculusPicker(HullInfo hull)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            var entries = new List<PickerPanel.Entry>
            {
                new PickerPanel.Entry
                {
                    Name = "STANDARD HOMUNCULUS",
                    Detail = "The plain homunculus, with no loadout blessing.",
                    Current = string.IsNullOrEmpty(config.Homunculus),
                    Tag = string.IsNullOrEmpty(config.Homunculus) ? "CHOSEN" : "",
                    OnPick = () => Edit(hull, c => c.Homunculus = "", LayoutMerge.Homunculus)
                }
            };
            foreach (Option option in Catalog.HomunculusOptions)
            {
                bool chosen = config.Homunculus == option.Code;
                string code = option.Code;
                bool refused = MatchRules.WhyRefused(option) != null;
                // What its blessing does, as the game's tooltip says it, so there is no need to hover over each one.
                string blessing = Catalog.BlessingOf(option);
                var entry = new PickerPanel.Entry
                {
                    Name = option.Name,
                    Detail = blessing ?? "From the game's loadouts; each blessing suits the loadout it came with.",
                    DetailBright = blessing != null,
                    Icon = option.Icon,
                    Accent = Ui.RarityColor(option.Rarity),
                    Current = chosen,
                    Tag = chosen ? (refused ? "CHOSEN · " + MatchRules.NotAllowedTag : "CHOSEN") : "",
                    TagColor = refused ? Ui.Warning : Ui.Gold,
                    Tooltip = option.Context,
                    TooltipGuid = option.Guid,
                    OnPick = () => Edit(hull, c => c.Homunculus = code, LayoutMerge.Homunculus)
                };
                if (refused && !chosen)
                {
                    entry.Enabled = false;
                    entry.Tag = MatchRules.NotAllowedTag;
                }
                entries.Add(entry);
            }
            Option current;
            string subtitle = "The homunculus your ship starts with in the Central Computer Cradle, with what each blessing does.";
            if (!string.IsNullOrEmpty(config.Homunculus) && Catalog.TryGetOption(config.Homunculus, out current)) subtitle += BreaksRulesNote(current);
            OpenPicker("HOMUNCULUS", subtitle, entries, null);
        }

        /// <summary>Starting alloys a run can be given, besides the game's own amount.</summary>
        private static readonly int[] AlloyChoices = { 0, 50, 100, 200, 250, 300, 500, 750, 1000, 1500, 2000, 3000, 5000, 10000 };

        /// <summary>The usual choices, plus the current amount and a Fleet Battle's limit, so both can always be seen and picked.</summary>
        private static IEnumerable<int> AlloyAmounts(int current)
        {
            var amounts = new SortedSet<int>(AlloyChoices);
            if (current >= 0) amounts.Add(current);
            if (FleetLink.ShipRulesActive)
            {
                int max = FleetLink.MaxStartAlloys;
                if (max >= 0 && max <= HullConfig.MaxAlloys) amounts.Add(max);
            }
            return amounts;
        }

        /// <summary>What the ship's fabricator starts with: the run's alloys, and the fabricator's level (MK I to MK III).</summary>
        private static void OpenFabricatorPicker(HullInfo hull)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            int ship = CustomLoadouts.TerminalShip;
            // The other ships' alloys are kept by Void Crew Fleet: offered only when Fleet takes them from their CUSTOM
            // loadouts (ship 2 with ReadsShip2Alloys, every ship with a Fleet that knows ships 3 to 6).
            bool alloys = ship < 2 || FleetLink.ReadsShip2Alloys || FleetLink.KnowsManyShips;
            var entries = new List<PickerPanel.Entry>();
            if (alloys)
            {
                entries.Add(AlloyEntry(hull, -1, config.Alloys, "GAME DEFAULT", "The game's own starting alloys: a bonus from the players' ranks."));
                foreach (int amount in AlloyAmounts(config.Alloys))
                {
                    entries.Add(AlloyEntry(hull, amount, config.Alloys, amount + " ALLOYS", amount == 0 ? "Start with none: recycle to get some." : "Spend them at the fabricator: printing and upgrades."));
                }
            }
            string[] details =
            {
                "As every run starts.",
                "Upgraded once: more to print, and it prints faster.",
                "Fully upgraded: everything it can print, at its fastest."
            };
            for (int level = 0; level <= HullConfig.MaxFabricatorLevel; level++)
            {
                int chosen = level;
                bool current = config.FabricatorLevel == level;
                entries.Add(Limited(new PickerPanel.Entry
                {
                    Name = "FABRICATOR " + CustomLoadouts.FabricatorLevelName(level),
                    Detail = details[level],
                    Category = "LEVEL",
                    Current = current,
                    Tag = current ? "CHOSEN" : "",
                    OnPick = () => Edit(hull, c => c.FabricatorLevel = chosen, LayoutMerge.Fabricator)
                }, MatchRules.FabricatorLevelAllowed(level)));
            }
            string subtitle = !alloys
                ? FleetLink.ShipName(ship) + "'s fabricator level. Its own starting alloys need a newer Void Crew Fleet; until then both ships start with ship 1's."
                : (ship >= 2 ? "What " + FleetLink.ShipName(ship) + "'s fabricator starts with" : "What the ship's fabricator starts with") +
                  ": alloys to print and upgrade with, and its level. Saved runs keep what they had.";
            if (FleetLink.ShipRulesActive)
            {
                int max = FleetLink.MaxStartAlloys;
                string alloyRule = max < 0 ? "the game's own alloys only" : (max == int.MaxValue ? "any alloys" : "up to " + max + " alloys");
                subtitle += " This match allows " + alloyRule + " and a fabricator up to " +
                            CustomLoadouts.FabricatorLevelName(MatchRules.AllowedFabricatorLevel(HullConfig.MaxFabricatorLevel)) + ".";
            }
            OpenPicker("FABRICATOR", subtitle, entries, null);
        }

        private static PickerPanel.Entry AlloyEntry(HullInfo hull, int amount, int currentAmount, string name, string detail)
        {
            bool current = currentAmount == amount;
            return Limited(new PickerPanel.Entry
            {
                Name = name,
                Detail = detail,
                Category = "ALLOYS",
                Current = current,
                Tag = current ? "CHOSEN" : "",
                OnPick = () => Edit(hull, c => c.Alloys = amount, LayoutMerge.Alloys)
            }, MatchRules.AlloysAllowed(amount));
        }

        /// <summary>A FABRICATOR choice the match rules don't allow: greyed out, or, when it is the current one, marked.</summary>
        private static PickerPanel.Entry Limited(PickerPanel.Entry entry, bool allowed)
        {
            if (allowed) return entry;
            entry.TagColor = Ui.Warning;
            if (entry.Current)
            {
                entry.Tag = "CHOSEN · " + MatchRules.NotAllowedTag;
                return entry;
            }
            entry.Enabled = false;
            entry.Tag = MatchRules.NotAllowedTag;
            return entry;
        }

        /// <summary>
        /// Saves the CUSTOM loadout the terminal shows in one of this player's numbered slots for this ship type, to load
        /// again in a later match. Slots replace what they held (marked REPLACE); the CLEAR tab empties one.
        /// </summary>
        private static void OpenSavePicker(HullInfo hull, string tab = "SAVE")
        {
            var entries = new List<PickerPanel.Entry>();
            for (int slot = 1; slot <= SavedLoadouts.SlotCount; slot++)
            {
                int chosen = slot;
                SavedLoadouts.Saved saved = SavedLoadouts.Get(hull, slot);
                bool asking = saved != null && Asking(slot, "save");
                entries.Add(new PickerPanel.Entry
                {
                    Name = "SLOT " + slot,
                    Detail = saved != null ? SavedDetail(saved) : "Empty",
                    Category = "SAVE",
                    Tag = saved == null ? "SAVE HERE" : (asking ? "CLICK AGAIN TO REPLACE" : "REPLACE"),
                    TagColor = saved == null ? Ui.Gold : (asking ? Ui.Warning : Orange),
                    OnPick = () =>
                    {
                        // A saved loadout is only replaced on a second click (a VR pointer can land on the wrong row).
                        if (saved != null && !Confirmed(chosen, "save"))
                        {
                            OpenSavePicker(hull);
                            return;
                        }
                        SaveToSlot(hull, chosen);
                    }
                });
            }
            foreach (SavedLoadouts.Saved saved in SavedLoadouts.All(hull))
            {
                int chosen = saved.Slot;
                bool asking = Asking(chosen, "clear");
                entries.Add(new PickerPanel.Entry
                {
                    Name = "CLEAR SLOT " + saved.Slot,
                    Detail = SavedDetail(saved),
                    Category = "CLEAR",
                    Tag = asking ? "CLICK AGAIN TO CLEAR" : "",
                    TagColor = Ui.Warning,
                    OnPick = () =>
                    {
                        if (!Confirmed(chosen, "clear"))
                        {
                            OpenSavePicker(hull, "CLEAR");
                            if (_picker != null) _picker.ScrollTo(new Vector2(0f, 0f));
                            return;
                        }
                        SavedLoadouts.Clear(hull, chosen);
                        Toast("Slot " + chosen + " is empty again.");
                    }
                });
            }
            entries.Insert(0, new PickerPanel.Entry
            {
                Pinned = true,
                Name = "COPY AS A SHARE CODE",
                Detail = "A line of text with this whole loadout, to paste in a chat or on a forum. Anyone with Ship Customizer brings it in with LOAD LOADOUT.",
                Tag = "COPY",
                OnPick = () => CopyShareCode(hull)
            });
            OpenPicker("SAVE LOADOUT · " + hull.Name.ToUpperInvariant(),
                "Keep this CUSTOM loadout on your PC, then bring it back in any later match with LOAD LOADOUT (on a " + hull.Name + "), or share it as a code.",
                entries, tab);
        }

        /// <summary>Puts the shown CUSTOM loadout on the clipboard as a share code.</summary>
        private static void CopyShareCode(HullInfo hull)
        {
            string code = ShareCodes.Make(hull.ShipGuid.AsHex(), CustomLoadouts.ConfigToShow(hull));
            GUIUtility.systemCopyBuffer = code;
            Toast("Share code copied (" + code.Length + " characters). Paste it anywhere; LOAD LOADOUT > PASTE A SHARE CODE brings it in on a " + hull.Name + ".");
        }

        /// <summary>
        /// Brings in the share code on the clipboard (only for this ship type), checked like a change from another player:
        /// what this ship doesn't have, or doesn't fit, is left out.
        /// </summary>
        private static void PasteShareCode(HullInfo hull, bool storageOnly)
        {
            if (!CustomLoadouts.CanEdit(hull))
            {
                Toast(CustomLoadouts.WhyNotEditable());
                return;
            }
            string hullHex;
            HullConfig shared;
            string why;
            if (!ShareCodes.TryRead(GUIUtility.systemCopyBuffer, out hullHex, out shared, out why))
            {
                Toast("Nothing was loaded: " + why + ".");
                return;
            }
            if (hullHex != hull.ShipGuid.AsHex())
            {
                var otherGuid = new GUIDUnion(hullHex);
                HullInfo other;
                bool known = Catalog.ShipGuids().Contains(otherGuid) && Catalog.TryGetHull(otherGuid, out other);
                Toast("That share code is for " + (known ? "the " + CustomLoadouts.HullName(otherGuid) + ": pick that ship first." : "a ship this game doesn't have."));
                return;
            }
            HullConfig clean = CustomLoadouts.Sanitize(hull, shared);
            if (!Edit(hull, c => CopyInto(c, clean.Clone(), storageOnly, "a share code"))) return;
            Toast(storageOnly ? "Loaded the storage racks of the share code." : "Loaded the share code: " + CustomLoadouts.Summary(clean) + ".");
        }

        /// <summary>The saved slot (and what for) that waits for a second click, and since when.</summary>
        private static int _askSlot;
        private static string _askAction;
        private static float _askSince;

        /// <summary>Whether a saved slot waits for a second click for this.</summary>
        private static bool Asking(int slot, string action)
        {
            return _askSlot == slot && _askAction == action && Time.unscaledTime - _askSince < 8f;
        }

        /// <summary>True on the second click (within a few seconds); the first one only asks.</summary>
        private static bool Confirmed(int slot, string action)
        {
            if (Asking(slot, action))
            {
                _askSlot = 0;
                return true;
            }
            _askSlot = slot;
            _askAction = action;
            _askSince = Time.unscaledTime;
            return false;
        }

        private static void SaveToSlot(HullInfo hull, int slot)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            if (!SavedLoadouts.Save(hull, slot, config))
            {
                Toast("Not saved: your saved loadouts file couldn't be read when the game started (see the BepInEx log).");
                return;
            }
            Toast("Saved to slot " + slot + ": " + CustomLoadouts.Summary(config) + ".");
        }

        private static string SavedDetail(SavedLoadouts.Saved saved)
        {
            return "Saved " + saved.SavedAt + "  ·  " + CustomLoadouts.Summary(saved.Config);
        }

        /// <summary>
        /// Replaces the CUSTOM loadout (or only its storage racks) with one this player saved, one of the game's loadouts,
        /// or an empty ship; with Void Crew Fleet, also another player ship's (only the same team's in a Fleet Battle).
        /// </summary>
        private static void OpenLoadPicker(HullInfo hull, bool storageOnly)
        {
            var entries = new List<PickerPanel.Entry>
            {
                new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "PASTE A SHARE CODE",
                    Detail = storageOnly ? "The storage racks of a share code you copied." : "A loadout someone shared: copy their code, then pick this. Only what fits this ship comes in.",
                    Tag = "PASTE",
                    OnPick = () => PasteShareCode(hull, storageOnly)
                }
            };
            int ship = CustomLoadouts.TerminalShip;
            // In a Fleet Battle no ship follows another: each team keeps its choices to itself.
            if (ship >= 2 && !FleetLink.IsTeamMatch)
            {
                bool following = CustomLoadouts.FollowsShip1(hull, ship);
                entries.Add(new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "SAME AS SHIP 1",
                    Detail = FleetLink.ShipName(ship) + " uses ship 1's CUSTOM loadout and follows its changes.",
                    Current = following,
                    Tag = following ? "NOW" : "",
                    OnPick = following ? (Action)(() => { }) : () => FollowShip1(hull)
                });
            }
            for (int other = 1; other <= FleetLink.ShipCount; other++)
            {
                if (other == ship || FleetLink.TeamNameOfShip(other) != FleetLink.TeamNameOfShip(ship)) continue;
                int from = other;
                string name = FleetLink.ShipName(from);
                entries.Add(new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = name + "'S CUSTOM",
                    Detail = storageOnly ? "Copy " + name + "'s storage racks once." : "Copy " + name + "'s CUSTOM loadout once, then change what you like.",
                    OnPick = () =>
                    {
                        HullConfig source = CustomLoadouts.LayoutOf(hull, from);
                        if (source != null) Edit(hull, c => CopyInto(c, source.Clone(), storageOnly, name + "'s CUSTOM"));
                    }
                });
            }
            if (CustomLoadouts.EditsGoToHost(hull))
            {
                // In another player's room the terminal shows the host's CUSTOM loadouts; this player's own stay on their PC.
                entries.Add(new PickerPanel.Entry
                {
                    Pinned = true,
                    Name = "YOUR OWN CUSTOM",
                    Detail = storageOnly ? "Copy the storage racks of the CUSTOM loadout saved on your PC." : "Copy the CUSTOM loadout saved on your PC into the room's.",
                    OnPick = () => Edit(hull, c => CopyInto(c, LoadoutStore.Get(hull, CustomLoadouts.TerminalShip).Clone(), storageOnly, "your own CUSTOM"))
                });
            }
            List<SavedLoadouts.Saved> savedLoadouts = SavedLoadouts.All(hull);
            foreach (SavedLoadouts.Saved saved in savedLoadouts)
            {
                SavedLoadouts.Saved chosen = saved;
                entries.Add(new PickerPanel.Entry
                {
                    Name = "SLOT " + saved.Slot,
                    Detail = SavedDetail(saved),
                    Category = "SAVED",
                    OnPick = () => Edit(hull, c => CopyInto(c, chosen.Config.Clone(), storageOnly, "saved loadout " + chosen.Slot))
                });
            }
            if (savedLoadouts.Count == 0)
            {
                entries.Add(new PickerPanel.Entry
                {
                    Name = "NOTHING SAVED YET",
                    Detail = "Use SAVE LOADOUT to keep a CUSTOM loadout of this ship on your PC.",
                    Category = "SAVED",
                    Enabled = false
                });
            }
            foreach (ShipLoadoutDataDef preset in hull.Presets)
            {
                ShipLoadoutDataDef def = preset;
                bool listed = hull.RegularPresets.Contains(preset);
                string detail;
                try
                {
                    ShipLoadout loadout = preset.Asset.ShipLoadout;
                    detail = loadout.Sockets.Count(e => e != null && e.Module != null && !e.Module.IsNull) + " modules  ·  " + loadout.GetAllResourceInfos(true).Count + " stored items";
                }
                catch
                {
                    detail = "";
                }
                entries.Add(new PickerPanel.Entry
                {
                    Name = LoadoutStore.PresetName(preset).ToUpperInvariant(),
                    Detail = detail,
                    Category = listed ? "TERMINAL LOADOUTS" : "CHALLENGE AND OTHER",
                    OnPick = () => Edit(hull, c =>
                    {
                        HullConfig copy = LoadoutStore.FromGameLoadout(hull, def);
                        c.Storage = copy.Storage;
                        if (storageOnly) return;
                        c.Slots = copy.Slots;
                        c.Contents = copy.Contents;
                        c.Homunculus = copy.Homunculus;
                        c.CopiedFrom = copy.CopiedFrom;
                    })
                });
            }
            if (!storageOnly)
            {
                entries.Add(new PickerPanel.Entry
                {
                    Name = "EMPTY ALL HARDPOINTS",
                    Detail = "Take every module and weapon off the ship.",
                    Category = "EMPTY",
                    OnPick = () => Edit(hull, c =>
                    {
                        c.Slots.Clear();
                        c.Contents.Clear();
                    })
                });
                entries.Add(new PickerPanel.Entry { Name = "EMPTY ALL STORAGE RACKS", Detail = "Start with nothing on the racks.", Category = "EMPTY", OnPick = () => Edit(hull, EmptyStorage) });
                entries.Add(new PickerPanel.Entry
                {
                    Name = "EMPTY EVERYTHING",
                    Detail = "No modules, nothing in storage, standard homunculus.",
                    Category = "EMPTY",
                    OnPick = () => Edit(hull, c =>
                    {
                        c.Slots.Clear();
                        c.Contents.Clear();
                        EmptyStorage(c);
                        c.Homunculus = "";
                    })
                });
            }
            OpenPicker(storageOnly ? "LOAD STORAGE RACKS" : "LOAD LOADOUT",
                storageOnly ? "Fill the racks from one of your saved loadouts or one of the game's loadouts."
                            : "Replace your CUSTOM loadout with one you saved, one of the game's loadouts, or an empty ship, then change what you like.",
                entries, savedLoadouts.Count > 0 ? "SAVED" : null);
        }

        /// <summary>Copies a whole CUSTOM loadout (fabricator included), or only its storage racks, into the one being changed.</summary>
        private static void CopyInto(HullConfig target, HullConfig source, bool storageOnly, string name)
        {
            target.Storage = source.Storage;
            if (storageOnly) return;
            target.Slots = source.Slots;
            target.Contents = source.Contents ?? new SortedDictionary<int, List<string>>();
            target.Homunculus = source.Homunculus;
            target.Alloys = source.Alloys;
            target.FabricatorLevel = source.FabricatorLevel;
            target.CopiedFrom = name;
        }

        private static void EmptyStorage(HullConfig config)
        {
            foreach (List<string> rack in config.Storage.Values)
            {
                if (rack == null) continue;
                for (int i = 0; i < rack.Count; i++) rack[i] = "";
            }
        }

        // ---------------------------------------------------------------- storage racks

        private static void OpenStorage(HullInfo hull)
        {
            VisualElement host = OverlayHost();
            if (host == null) return;
            CloseOverlays();
            _storage = new StoragePanel(host, hull, () => CustomLoadouts.ConfigToShow(hull), () => CustomLoadouts.CanEdit(hull), CustomLoadouts.WhyNotEditable, TeamColor(), NotAllowedInMatch,
                (shelf, index) => OpenItemPicker(hull, shelf, index),
                (from, fromIndex, to, toIndex, seen) => CopyStorage(hull, from, fromIndex, to, toIndex, seen),
                action =>
                {
                    if (action == "empty") Edit(hull, EmptyStorage);
                    else if (action == "copy") OpenLoadPicker(hull, true);
                });
            KeepToastOnTop();
        }

        /// <summary>Whether a storage item breaks this match's rules (always false outside a Fleet Battle).</summary>
        private static bool NotAllowedInMatch(string code)
        {
            Option option;
            return !string.IsNullOrEmpty(code) && Catalog.TryGetOption(code, out option) && MatchRules.WhyRefused(option) != null;
        }

        private static void OpenItemPicker(HullInfo hull, ShelfInfo shelf, int index)
        {
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            List<string> rack;
            string currentCode = config.Storage.TryGetValue(shelf.CoreIndex, out rack) && index < rack.Count ? rack[index] : "";

            var entries = new List<PickerPanel.Entry>();
            if (!string.IsNullOrEmpty(currentCode))
            {
                entries.Add(new PickerPanel.Entry { Pinned = true, Name = "EMPTY SLOT", Detail = "Leave this rack slot empty.", OnPick = () => SetStorage(hull, shelf, index, "") });
            }
            int refused = 0;
            bool anyItem = Settings.AnyItemOnAnyRack.Value;
            foreach (Option option in Catalog.ItemOptions)
            {
                // A Fleet Battle never offers relics (so no RELICS tab), the Armory Exchange Key, or Placeholder crates and mods.
                if (MatchRules.Hidden(option)) continue;
                bool fits = anyItem || Catalog.ShelfAccepts(shelf, option);
                bool isCurrent = option.Code == currentCode;
                bool notAllowed = MatchRules.WhyRefused(option) != null;
                string rarity = Ui.RarityName(option.Rarity);
                // A Blessed Homunculus on a rack shows its blessing, as in the HOMUNCULUS menu.
                string blessing = Catalog.IsHomunculus(option.Code) ? Catalog.BlessingOf(option) : null;
                var entry = new PickerPanel.Entry
                {
                    Name = option.Name,
                    Detail = blessing ?? (option.Kind == OptionKind.WeaponCrate ? "Inscribed crate: build this weapon on any free hardpoint" + (rarity.Length > 0 ? "  ·  " + rarity : "") : rarity),
                    DetailBright = blessing != null,
                    Icon = option.Icon,
                    Accent = Ui.RarityColor(option.Rarity),
                    Category = option.Category,
                    Current = isCurrent,
                    Tooltip = option.Context,
                    TooltipGuid = option.Guid
                };
                if (isCurrent)
                {
                    entry.Tag = notAllowed ? "IN THIS SLOT · " + MatchRules.NotAllowedTag : "IN THIS SLOT";
                    if (notAllowed) entry.TagColor = Ui.Warning;
                    entry.OnPick = () => { };
                }
                else if (notAllowed)
                {
                    entry.Enabled = false;
                    entry.Tag = MatchRules.NotAllowedTag;
                    entry.TagColor = Ui.Warning;
                }
                else if (!fits)
                {
                    entry.Enabled = false;
                    entry.Tag = StoragePanel.DoesntFitTag;
                    entry.TagColor = Ui.Warning;
                    refused++;
                }
                else
                {
                    string code = option.Code;
                    entry.OnPick = () => SetStorage(hull, shelf, index, code);
                }
                entries.Add(entry);
            }

            Option current;
            string subtitle = !string.IsNullOrEmpty(currentCode) && Catalog.TryGetOption(currentCode, out current) ? "Now: " + current.Name + "." + BreaksRulesNote(current) : "Empty.";
            if (anyItem) subtitle += " Any item fits any rack slot; big items can stick out of small slots.";
            else if (refused > 0) subtitle += " Greyed-out items are ones this rack normally refuses (see the AnyItemOnAnyRack setting).";
            Option currentOption;
            string tab = !string.IsNullOrEmpty(currentCode) && Catalog.TryGetOption(currentCode, out currentOption) ? currentOption.Category : null;
            OpenPicker(shelf.Name + "  ·  SLOT " + (index + 1), subtitle, entries, tab, false);
        }

        private static void SetStorage(HullInfo hull, ShelfInfo shelf, int index, string code)
        {
            Edit(hull, c => c.SetRackSlot(shelf.CoreIndex, shelf.Slots.Count, index, code), LayoutMerge.RackPart(shelf.CoreIndex, index));
        }

        /// <summary>
        /// A filled rack slot was dragged onto another slot (of the same rack or another one): its item goes there too,
        /// replacing what was there. The slot is checked as its list checks a pick (the match rules, what the rack takes,
        /// whether this player may change the loadout), and changed the same way as a pick, against the window as it was
        /// when the drag began: a slot someone else changed meanwhile keeps their change. So other players, Void Crew
        /// Fleet's ships and the save see a copy just like a pick.
        /// </summary>
        private static void CopyStorage(HullInfo hull, ShelfInfo from, int fromIndex, ShelfInfo to, int toIndex, HullConfig seen)
        {
            if (hull == null || hull != _shownHull || from == null || to == null || seen == null || toIndex < 0 || toIndex >= to.Slots.Count) return;
            if (!CustomLoadouts.CanEdit(hull))
            {
                Toast(CustomLoadouts.WhyNotEditable());
                return;
            }
            string code = LayoutMerge.StateOf(seen, hull.Sockets, LayoutMerge.RackPart(from.CoreIndex, fromIndex));
            string part = LayoutMerge.RackPart(to.CoreIndex, toIndex);
            Option item;
            if (code.Length == 0 || !Catalog.TryGetRackOption(code, out item)) return;
            string name = RichText.Plain(item.Name);
            string slot = CustomLoadouts.PartName(hull, part);
            string why = MatchRules.WhyRefused(item);
            if (why != null)
            {
                Toast(name + " is " + MatchRules.NotAllowedTag + " (" + why.TrimEnd('.') + "), so it wasn't copied to " + slot + ".");
                return;
            }
            if (!Settings.AnyItemOnAnyRack.Value && !Catalog.ShelfAccepts(to, item))
            {
                Toast(name + " wasn't copied to " + slot + ": " + StoragePanel.DoesntFitTag + " (the rack normally refuses it; see the AnyItemOnAnyRack setting).");
                return;
            }
            // Already there: nothing to change.
            if (LayoutMerge.StateOf(seen, hull.Sockets, part) == code) return;
            Edit(hull, c => c.SetRackSlot(to.CoreIndex, to.Slots.Count, toIndex, code), seen, part);
        }

        // ---------------------------------------------------------------- shared

        /// <summary>
        /// Changes the CUSTOM loadout the terminal shows. The host (or anyone with an older host, for their own loadouts)
        /// saves it on this PC; in the room of a host on 0.5.0 or newer, the change goes to the host, who saves and shares it.
        /// <paramref name="parts"/> are the parts a pick from a list changes (a hardpoint, a rack slot, the homunculus ...):
        /// if one of them changed while the list was open (another player got there first), nothing is changed and the
        /// player is told, so neither pick silently replaces the other.
        /// </summary>
        private static bool Edit(HullInfo hull, Action<HullConfig> change, params string[] parts)
        {
            return Edit(hull, change, null, parts);
        }

        /// <param name="dragged">For a copy dragged in the storage window: the loadout as the window showed it when the drag
        /// began, which <paramref name="parts"/> are checked against instead of the open list (null for a pick or a button).
        /// The window closes when the terminal switches ship, so a drag needs no check for that.</param>
        private static bool Edit(HullInfo hull, Action<HullConfig> change, HullConfig dragged, params string[] parts)
        {
            if (!CustomLoadouts.CanEdit(hull))
            {
                Toast(CustomLoadouts.WhyNotEditable());
                return false;
            }
            int ship = CustomLoadouts.TerminalShip;
            if (dragged == null && _pickerSeen != null && (_pickerSeenHull != hull.ShipGuid || _pickerSeenShip != ship))
            {
                // The terminal moved to another ship while the list was open (its lists are closed a moment later).
                Toast("The terminal switched to another ship while this list was open, so your pick wasn't made. Pick again.");
                return false;
            }
            HullConfig seen = dragged ?? _pickerSeen;
            if (parts.Length > 0 && seen != null)
            {
                HullConfig now = CustomLoadouts.ConfigToShow(hull);
                string moved = parts.FirstOrDefault(p => LayoutMerge.StateOf(seen, hull.Sockets, p) != LayoutMerge.StateOf(now, hull.Sockets, p));
                if (moved != null)
                {
                    Toast(CustomLoadouts.LastEditorOf(hull, moved) + " changed " + CustomLoadouts.PartName(hull, moved) +
                          (dragged != null
                              ? " while you were dragging, so your copy wasn't made. Have a look and drag again."
                              : " while this list was open, so your pick wasn't made. Have a look and pick again."));
                    if (_storage != null && _storage.IsOpen) _storage.Refresh();
                    return false;
                }
            }
            if (CustomLoadouts.EditsGoToHost(hull))
            {
                HullConfig shared = CustomLoadouts.ConfigToShow(hull).Clone();
                change(shared);
                if (!CustomLoadouts.SendEditToHost(hull, ship, shared, false))
                {
                    Toast("Your change could not be sent to the host. Try again in a moment.");
                    return false;
                }
            }
            else
            {
                HullConfig config = LoadoutStore.GetForEdit(hull, ship);
                HullConfig before = config.Clone();
                change(config);
                config.Normalized();
                LoadoutStore.Set(hull, config, ship);
                CustomLoadouts.NoteHostChange(hull, ship, before, config);
                CustomLoadouts.OnEdited(hull, 0, 0, ship);
            }
            // The redraw refreshes an open storage window too.
            Redisplay();
            return true;
        }

        /// <summary>Ship 2 to 6 goes back to using ship 1's CUSTOM loadout.</summary>
        private static void FollowShip1(HullInfo hull)
        {
            int ship = CustomLoadouts.TerminalShip;
            if (ship < 2) return;
            if (!CustomLoadouts.CanEdit(hull))
            {
                Toast(CustomLoadouts.WhyNotEditable());
                return;
            }
            if (CustomLoadouts.EditsGoToHost(hull))
            {
                if (!CustomLoadouts.SendEditToHost(hull, ship, null, true)) Toast("Your change could not be sent to the host. Try again in a moment.");
            }
            else
            {
                CustomLoadouts.FollowShip1AsHost(hull, ship);
                CustomLoadouts.OnEdited(hull, 0, 0, ship);
            }
            // The redraw refreshes an open storage window too.
            Redisplay();
        }

        /// <summary>
        /// CUSTOM was just refused. The game's SELECT button marks itself SELECTED right after asking, whatever the answer, so
        /// the terminal is drawn again a moment later to show what the room really has chosen.
        /// </summary>
        public static void OnSelectRefused()
        {
            VisualElement host = OverlayHost();
            if (host != null) host.schedule.Execute(Redisplay).ExecuteLater(50);
        }

        /// <summary>Asks the terminal to draw the shown loadout again (ship picture, systems and resources).</summary>
        private static void Redisplay()
        {
            if (_terminal == null || _shown == null || DisplayMethod == null) return;
            try
            {
                DisplayMethod.Invoke(_terminal, new object[] { _shown });
            }
            catch (Exception e)
            {
                Log.Failure("Redrawing the loadout terminal", e);
            }
        }

        /// <summary>
        /// With a host older than 0.5.0, players other than the host can't choose CUSTOM: the button stops reacting and
        /// reads HOST ONLY, unless the room already uses this CUSTOM loadout (then it keeps the game's own SELECTED look).
        /// </summary>
        private static void LockSelect(bool locked, string text, bool lockedLook)
        {
            if (_selectButton == null) return;
            if (locked)
            {
                if (text != null) _selectButton.text = text;
                _selectButton.pickingMode = PickingMode.Ignore;
                _selectButton.EnableInClassList(LockedClass, lockedLook);
                _selectLocked = true;
            }
            else if (_selectLocked)
            {
                _selectButton.pickingMode = PickingMode.Position;
                _selectButton.RemoveFromClassList(LockedClass);
                _selectLocked = false;
            }
        }

        private static void OpenPicker(string title, string subtitle, List<PickerPanel.Entry> entries, string tab, bool closeOthers = true)
        {
            VisualElement host = OverlayHost();
            if (host == null) return;
            if (closeOthers) CloseOverlays();
            else if (_picker != null) _picker.Close();
            // What the list was made from: a pick is checked against it (see Edit).
            _pickerSeen = _shownHull != null ? CustomLoadouts.ConfigToShow(_shownHull).Clone() : null;
            _pickerSeenHull = _shownHull != null ? _shownHull.ShipGuid : GUIDUnion.Empty();
            _pickerSeenShip = CustomLoadouts.TerminalShip;
            _picker = new PickerPanel(host, title, subtitle, entries, tab, TeamColor());
            KeepToastOnTop();
        }

        private static void CloseOverlays()
        {
            if (_picker != null) _picker.Close();
            if (_storage != null) _storage.Close();
            _picker = null;
            _storage = null;
            _pickerSeen = null;
        }

        /// <summary>A message shown just before a window opened (a refused pick, say) stays readable above it.</summary>
        private static void KeepToastOnTop()
        {
            if (_toast != null && _toast.parent != null) _toast.BringToFront();
        }

        private static VisualElement OverlayHost()
        {
            VisualElement host = _root;
            if (host == null && _anchor != null)
            {
                host = _anchor;
                while (host.parent != null) host = host.parent;
            }
            return host;
        }

        /// <summary>A short message across the top of the terminal screen, also written to the log.</summary>
        public static void Toast(string message)
        {
            Log.Info(message);
            ShowToast(message);
        }

        /// <summary>The message on the terminal screen only (when the terminal is there).</summary>
        public static void ShowToast(string message)
        {
            VisualElement host = OverlayHost();
            if (host == null) return;
            if (_toast == null)
            {
                _toast = new VisualElement { name = "ShipCustomizerToast" };
                _toast.pickingMode = PickingMode.Ignore;
                _toast.style.position = Position.Absolute;
                _toast.style.left = new Length(22, LengthUnit.Percent);
                _toast.style.right = new Length(22, LengthUnit.Percent);
                _toast.style.top = new Length(3, LengthUnit.Percent);
                // The game's pop-up colours: dark grey with the light-blue outline.
                _toast.style.backgroundColor = new Color(0.188f, 0.188f, 0.188f, 0.96f);
                Ui.Border(_toast, 2, new Color(0.741f, 0.906f, 0.984f, 1f));
                Ui.Padding(_toast, 10, 16);
                _toastLabel = Ui.GameText("", "ui-text-size__medium");
                _toastLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                _toast.Add(_toastLabel);
            }
            _toastLabel.text = message;
            if (_toast.parent != host) host.Add(_toast);
            _toast.BringToFront();
            int version = ++_toastVersion;
            _toast.schedule.Execute(() =>
            {
                if (version == _toastVersion && _toast != null) _toast.RemoveFromHierarchy();
            }).ExecuteLater(3500);
        }

        private static void WriteReportOnce()
        {
            if (_reportWritten || Settings.WriteGameDataReport == null || !Settings.WriteGameDataReport.Value) return;
            _reportWritten = true;
            // Only after an update of the mod or the game: otherwise it would say the same again.
            if (GameDataReport.UpToDate()) return;
            VisualElement root = OverlayHost();
            // Wait a moment so the terminal's layout has been calculated for the screen-layout part of the report.
            if (root != null) root.schedule.Execute(() => GameDataReport.Write(root)).ExecuteLater(1500);
            else GameDataReport.Write(null);
        }
    }
}
