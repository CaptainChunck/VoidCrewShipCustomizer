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
        private static StoragePanel _storage;
        private static VisualElement _toast;
        private static Label _toastLabel;
        private static int _toastVersion;
        private static bool _reportWritten;

        // ---------------------------------------------------------------- hooks called from Patches

        /// <summary>The terminal showed a loadout (after the ship picture, resources and systems were drawn).</summary>
        public static void OnTerminalDisplayed(LoadoutTerminal terminal, ShipLoadoutDataDef def, VisualElement root)
        {
            _terminal = terminal;
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
                bool roomUsesIt = CustomLoadouts.ActiveShip == hull.ShipGuid;
                LockSelect(!PhotonNetwork.IsMasterClient, roomUsesIt ? null : "HOST ONLY", !roomUsesIt);
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
            _terminal = null;
            _root = null;
            _slots = null;
            _selectButton = null;
            _anchor = null;
            _shown = null;
            _shownHull = null;
            _toolbar = null;
            _toast = null;
            Hooks.Clear();
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

        /// <summary>The host's CUSTOM choice changed (seen through the room); redraw what depends on it.</summary>
        public static void OnSelectionChangedRemotely()
        {
            try
            {
                HubShipManager manager = HubShipManager.Instance;
                if (manager != null) manager.NotifyShipSelected(manager.SelectedShipGuid.AsIntArray());
                if (_shownHull != null) Redisplay();
            }
            catch (Exception e)
            {
                Log.Failure("Refreshing the loadout terminal", e);
            }
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
            hook.Click = Ui.Click(slot, () => OnSlotClicked(index));
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
            slot.UnregisterCallback(hook.Enter);
            slot.UnregisterCallback(hook.Leave);
            slot.pickingMode = hook.OriginalPicking;
            slot.style.scale = StyleKeyword.Null;
            Hooks.Remove(slot);
        }

        private static void OnSlotClicked(int index)
        {
            HullInfo hull = _shownHull;
            if (hull == null || index < 0 || index >= hull.Sockets.Count) return;
            if (!CustomLoadouts.CanEdit(hull))
            {
                Toast("This is the host's CUSTOM loadout. Only the host can change it.");
                return;
            }
            OpenSlotPicker(hull, hull.Sockets[index]);
        }

        private static void OpenSlotPicker(HullInfo hull, SocketInfo socket)
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
                    OnPick = () => Edit(hull, c => c.Slots.Remove(socket.Index))
                });
            }

            foreach (Option option in Catalog.SlotOptions)
            {
                SocketInfo target = TargetFor(option, socket, hull.Sockets, config);
                if (target == null) continue;
                List<int> displaced = Hardpoints.Displaced(target, config.Slots.Keys);
                bool isCurrent = target.Index == socket.Index && currentCode == option.Code;

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
                    entry.Tag = "FITTED";
                    entry.OnPick = () => { };
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

            string title = SizeTitle(socket.Size);
            string subtitle = current != null ? "Fitted: " + current.Name + ". Pick a replacement, or empty it." : "Empty. Pick what to fit here.";
            if (socket.Overlaps.Any(i => hull.Sockets[i].Size < socket.Size)) subtitle += " A smaller module takes one part of this hardpoint.";
            if (Settings.AllowAnySize.Value) subtitle += " (Any size allowed: see the AllowAnySize setting.)";
            OpenPicker(title, subtitle, entries, current != null ? current.Category : null);
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
            if (target != clicked) parts.Add(target.Size < clicked.Size ? "takes part of this hardpoint" : "takes the whole hardpoint");
            return string.Join("  ·  ", parts.ToArray());
        }

        private static void Fit(HullInfo hull, Option option, SocketInfo target)
        {
            Edit(hull, c =>
            {
                foreach (int key in Hardpoints.Displaced(target, c.Slots.Keys)) c.Slots.Remove(key);
                c.Slots[target.Index] = option.Code;
            });
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
                _toolbar.style.bottom = ToolbarBottom;
                _toolbar.style.width = ToolbarWidth;
                _toolbar.style.flexDirection = FlexDirection.Row;
                _toolbar.style.flexWrap = Wrap.Wrap;
                _toolbar.style.justifyContent = Justify.SpaceBetween;
            }
            _toolbar.Clear();
            HullInfo hull = _shownHull;
            HullConfig config = CustomLoadouts.ConfigToShow(hull);
            bool editable = CustomLoadouts.CanEdit(hull);
            if (!editable)
            {
                Label viewOnly = Ui.Label("HOST'S CUSTOM LOADOUT (VIEW ONLY)", 17, Ui.Gold, true);
                viewOnly.style.width = new Length(100, LengthUnit.Percent);
                viewOnly.style.marginBottom = 6;
                _toolbar.Add(viewOnly);
            }
            AddToolbarButton("HARDPOINTS", () => OpenHardpointList(hull));
            AddToolbarButton("STORAGE (" + config.StoredCount + ")", () => OpenStorage(hull));
            if (editable)
            {
                AddToolbarButton("HOMUNCULUS", () => OpenHomunculusPicker(hull));
                AddToolbarButton("COPY A LOADOUT", () => OpenCopyPicker(hull, false));
                AddToolbarButton("EMPTY", () => OpenEmptyPicker(hull));
            }
            if (_toolbar.parent != host)
            {
                _toolbar.RemoveFromHierarchy();
                host.Add(_toolbar);
            }
            _toolbar.BringToFront();
            foreach (VisualElement child in _toolbar.Children()) Ui.Announce(child);
        }

        private const float ToolbarRight = 29f;
        private const float ToolbarBottom = 122f;
        private const float ToolbarWidth = 362f;

        private static void AddToolbarButton(string text, Action onClick)
        {
            VisualElement button = Ui.Button(text, onClick, 17);
            button.style.width = (ToolbarWidth - 6f) / 2f;
            button.style.height = 40;
            button.style.marginRight = 0;
            button.style.marginBottom = 6;
            button.style.backgroundColor = new Color(0.08f, 0.085f, 0.09f, 0.92f);
            _toolbar.Add(button);
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
                SocketInfo chosen = socket;
                entries.Add(new PickerPanel.Entry
                {
                    Name = option != null ? option.Name : "EMPTY",
                    Detail = PositionName(hull, socket) + "  ·  " + Catalog.SizeName(socket.Size) + (rarity.Length > 0 ? "  ·  " + rarity : ""),
                    Icon = option != null ? option.Icon : null,
                    Accent = option != null ? Ui.RarityColor(option.Rarity) : Ui.TextDim,
                    Tag = option == null ? "+ FIT" : (editable ? "CHANGE" : ""),
                    Tooltip = option != null ? option.Context : null,
                    TooltipGuid = option != null ? option.Guid : GUIDUnion.Empty(),
                    OnPick = () => OnSlotClicked(chosen.Index)
                });
            }
            OpenPicker("HARDPOINTS · " + hull.Name.ToUpperInvariant(),
                editable ? "Pick a hardpoint to change what is fitted there. Port is the ship's left, starboard its right." : "The host's CUSTOM loadout; only the host can change it.",
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
                    OnPick = () => Edit(hull, c => c.Homunculus = "")
                }
            };
            foreach (Option option in Catalog.HomunculusOptions)
            {
                bool chosen = config.Homunculus == option.Code;
                string code = option.Code;
                entries.Add(new PickerPanel.Entry
                {
                    Name = option.Name,
                    Detail = "From the game's loadouts; each blessing suits the loadout it came with.",
                    Icon = option.Icon,
                    Accent = Ui.RarityColor(option.Rarity),
                    Current = chosen,
                    Tag = chosen ? "CHOSEN" : "",
                    Tooltip = option.Context,
                    TooltipGuid = option.Guid,
                    OnPick = () => Edit(hull, c => c.Homunculus = code)
                });
            }
            OpenPicker("HOMUNCULUS", "The homunculus your ship starts with in the Central Computer Cradle.", entries, null);
        }

        private static void OpenCopyPicker(HullInfo hull, bool storageOnly)
        {
            var entries = new List<PickerPanel.Entry>();
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
                        c.Homunculus = copy.Homunculus;
                        c.CopiedFrom = copy.CopiedFrom;
                    })
                });
            }
            OpenPicker(storageOnly ? "COPY STORAGE RACKS" : "COPY A LOADOUT",
                storageOnly ? "Start the racks with what one of the game's loadouts carries." : "Replace your CUSTOM loadout with a copy of one of the game's loadouts, then change what you like.",
                entries, null);
        }

        private static void OpenEmptyPicker(HullInfo hull)
        {
            var entries = new List<PickerPanel.Entry>
            {
                new PickerPanel.Entry { Name = "EMPTY ALL HARDPOINTS", Detail = "Take every module and weapon off the ship.", OnPick = () => Edit(hull, c => c.Slots.Clear()) },
                new PickerPanel.Entry { Name = "EMPTY ALL STORAGE RACKS", Detail = "Start with nothing on the racks.", OnPick = () => Edit(hull, EmptyStorage) },
                new PickerPanel.Entry
                {
                    Name = "EMPTY EVERYTHING",
                    Detail = "No modules, nothing in storage, standard homunculus.",
                    OnPick = () => Edit(hull, c =>
                    {
                        c.Slots.Clear();
                        EmptyStorage(c);
                        c.Homunculus = "";
                    })
                }
            };
            OpenPicker("EMPTY", "Clear part of your CUSTOM loadout.", entries, null);
        }

        private static void EmptyStorage(HullConfig config)
        {
            foreach (List<string> rack in config.Storage.Values)
            {
                for (int i = 0; i < rack.Count; i++) rack[i] = "";
            }
        }

        // ---------------------------------------------------------------- storage racks

        private static void OpenStorage(HullInfo hull)
        {
            VisualElement host = OverlayHost();
            if (host == null) return;
            CloseOverlays();
            bool editable = CustomLoadouts.CanEdit(hull);
            _storage = new StoragePanel(host, hull, () => CustomLoadouts.ConfigToShow(hull), editable,
                (shelf, index) => OpenItemPicker(hull, shelf, index),
                action =>
                {
                    if (action == "empty") Edit(hull, EmptyStorage);
                    else if (action == "copy") OpenCopyPicker(hull, true);
                });
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
            foreach (Option option in Catalog.ItemOptions)
            {
                bool fits = Catalog.ShelfAccepts(shelf, option);
                bool isCurrent = option.Code == currentCode;
                string rarity = Ui.RarityName(option.Rarity);
                var entry = new PickerPanel.Entry
                {
                    Name = option.Name,
                    Detail = option.Kind == OptionKind.WeaponCrate ? "Inscribed crate: build this weapon on any free hardpoint" + (rarity.Length > 0 ? "  ·  " + rarity : "") : rarity,
                    Icon = option.Icon,
                    Accent = Ui.RarityColor(option.Rarity),
                    Category = option.Category,
                    Current = isCurrent,
                    Tooltip = option.Context,
                    TooltipGuid = option.Guid
                };
                if (isCurrent)
                {
                    entry.Tag = "IN THIS SLOT";
                    entry.OnPick = () => { };
                }
                else if (!fits)
                {
                    entry.Enabled = false;
                    entry.Tag = "DOESN'T FIT THIS RACK";
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
            string subtitle = !string.IsNullOrEmpty(currentCode) && Catalog.TryGetOption(currentCode, out current) ? "Now: " + current.Name + "." : "Empty.";
            if (refused > 0) subtitle += " Greyed-out items are ones this rack refuses; they would end up on the floor.";
            Option currentOption;
            string tab = !string.IsNullOrEmpty(currentCode) && Catalog.TryGetOption(currentCode, out currentOption) ? currentOption.Category : null;
            OpenPicker(shelf.Name + "  ·  SLOT " + (index + 1), subtitle, entries, tab, false);
        }

        private static void SetStorage(HullInfo hull, ShelfInfo shelf, int index, string code)
        {
            Edit(hull, c =>
            {
                List<string> rack;
                if (!c.Storage.TryGetValue(shelf.CoreIndex, out rack) || rack == null)
                {
                    rack = new List<string>();
                    c.Storage[shelf.CoreIndex] = rack;
                }
                while (rack.Count < shelf.Slots.Count) rack.Add("");
                rack[index] = code;
            });
        }

        // ---------------------------------------------------------------- shared

        private static void Edit(HullInfo hull, Action<HullConfig> change)
        {
            if (!CustomLoadouts.CanEdit(hull))
            {
                Toast("Only the host can change the CUSTOM loadout the room is using.");
                return;
            }
            HullConfig config = LoadoutStore.Get(hull);
            change(config);
            LoadoutStore.Set(hull, config);
            CustomLoadouts.OnEdited(hull);
            Redisplay();
            if (_storage != null && _storage.IsOpen) _storage.Refresh();
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
        /// Players other than the host can't choose CUSTOM: the button stops reacting and reads HOST ONLY, unless the room
        /// already uses this CUSTOM loadout (then it keeps the game's own SELECTED look).
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
            _picker = new PickerPanel(host, title, subtitle, entries, tab);
        }

        private static void CloseOverlays()
        {
            if (_picker != null) _picker.Close();
            if (_storage != null) _storage.Close();
            _picker = null;
            _storage = null;
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

        /// <summary>A short message across the top of the terminal screen.</summary>
        public static void Toast(string message)
        {
            Log.Info(message);
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
                _toast.style.backgroundColor = new Color(0f, 0f, 0f, 0.88f);
                Ui.Border(_toast, 2, Ui.Gold);
                Ui.Radius(_toast, 6);
                Ui.Padding(_toast, 10, 16);
                _toastLabel = Ui.Label("", 22, Ui.Text, true);
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
            VisualElement root = OverlayHost();
            // Wait a moment so the terminal's layout has been calculated for the screen-layout part of the report.
            if (root != null) root.schedule.Execute(() => GameDataReport.Write(root)).ExecuteLater(1500);
            else GameDataReport.Write(null);
        }
    }
}
