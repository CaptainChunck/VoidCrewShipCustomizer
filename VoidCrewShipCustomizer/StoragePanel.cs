using System;
using System.Collections.Generic;
using System.Linq;
using Client.Player.Interactions;
using UI.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// The storage-rack editor: every rack of the ship with one tile per slot. Clicking a tile opens a
    /// <see cref="PickerPanel"/> of items for that slot.
    /// </summary>
    internal sealed class StoragePanel
    {
        private readonly VisualElement _backdrop;
        private readonly ScrollView _scroll;
        private readonly Label _summary;
        private readonly HullInfo _hull;
        private readonly Func<HullConfig> _config;
        private readonly Func<bool> _editable;
        private readonly Func<string> _viewOnlyReason;
        private readonly VisualElement _emptyAll;
        private readonly VisualElement _loadRacks;
        private readonly Func<string, bool> _notAllowed;
        private readonly Action<ShelfInfo, int> _onSlotClicked;

        public Action<string> OnAction;
        public Action Closed;

        public bool IsOpen
        {
            get { return _backdrop.parent != null; }
        }

        /// <param name="editable">Whether this player may change the racks now (asked again each time the window redraws:
        /// a Void Crew Fleet team change can give or take the right while it is open).</param>
        /// <param name="viewOnlyReason">Why the racks can't be changed, shown when <paramref name="editable"/> says no.</param>
        /// <param name="team">A Void Crew Fleet team's colour in a Fleet Battle (the frame and bars take it), otherwise null.</param>
        /// <param name="notAllowed">Whether an item's code breaks a Fleet Battle's match rules (its tile is marked).</param>
        public StoragePanel(VisualElement host, HullInfo hull, Func<HullConfig> config, Func<bool> editable, Func<string> viewOnlyReason, Color? team,
                            Func<string, bool> notAllowed, Action<ShelfInfo, int> onSlotClicked, Action<string> onAction)
        {
            _hull = hull;
            _config = config;
            _editable = editable;
            _viewOnlyReason = viewOnlyReason;
            _notAllowed = notAllowed;
            _onSlotClicked = onSlotClicked;
            OnAction = onAction;

            _backdrop = new VisualElement { name = "ShipCustomizerStorage" };
            _backdrop.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _backdrop) Close();
            });
            _emptyAll = Ui.SmallGameButton("EMPTY ALL", () => Act("empty"));
            _loadRacks = Ui.SmallGameButton("LOAD RACKS", () => Act("copy"));
            VisualElement body = Ui.GameWindow(_backdrop, "STORAGE RACKS · " + hull.Name.ToUpperInvariant(), Close, team, 10, 5, _emptyAll, _loadRacks);

            _summary = Ui.GameText("", "ui-text-size__medium");
            _summary.style.unityTextAlign = TextAnchor.MiddleCenter;
            _summary.style.flexShrink = 0;
            body.Add(_summary);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.style.marginTop = 6;
            _scroll.mouseWheelScrollSize = 70f;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(_scroll);

            host.Add(_backdrop);
            Ui.Announce(_backdrop);
            Refresh();
        }

        private void Act(string action)
        {
            if (OnAction != null && _editable()) OnAction(action);
        }

        /// <summary>Redraws the tiles from the current loadout.</summary>
        public void Refresh()
        {
            _scroll.Clear();
            HullConfig config = _config();
            bool editable = _editable();
            _emptyAll.style.display = editable ? DisplayStyle.Flex : DisplayStyle.None;
            _loadRacks.style.display = editable ? DisplayStyle.Flex : DisplayStyle.None;
            int slots = 0;
            foreach (ShelfInfo shelf in _hull.Shelves) slots += shelf.Slots.Count;
            int notAllowed = _notAllowed == null ? 0 : config.Storage.Values.Where(rack => rack != null).Sum(rack => rack.Count(_notAllowed));
            _summary.text = _hull.Shelves.Count == 0
                ? "This ship has no storage racks the customizer can fill."
                : string.Format("{0} of {1} slots filled. {2}", config.StoredCount, slots,
                    editable ? "Click a slot to choose what the ship starts with there." : _viewOnlyReason()) +
                  (notAllowed > 0 ? " " + notAllowed + " marked NOT ALLOWED break this match's rules and come off when the mission starts." : "");

            foreach (ShelfInfo shelf in _hull.Shelves)
            {
                List<string> codes;
                config.Storage.TryGetValue(shelf.CoreIndex, out codes);
                Label heading = Ui.GameText(shelf.Name + "  ·  " + shelf.Slots.Count + " SLOTS", "ui-text-size__medium");
                Ui.Margin(heading, 8, 0, 2, 2);
                _scroll.Add(heading);
                VisualElement line = Ui.Decoration("metem-decorative__box-border", 8);
                line.style.marginBottom = 6;
                _scroll.Add(line);

                var grid = new VisualElement();
                grid.style.flexDirection = FlexDirection.Row;
                grid.style.flexWrap = Wrap.Wrap;
                for (int i = 0; i < shelf.Slots.Count; i++)
                {
                    string code = codes != null && i < codes.Count ? codes[i] : "";
                    grid.Add(MakeTile(shelf, i, code, editable));
                }
                _scroll.Add(grid);
            }
        }

        private VisualElement MakeTile(ShelfInfo shelf, int index, string code, bool editable)
        {
            Option option = null;
            bool filled = !string.IsNullOrEmpty(code) && Catalog.TryGetOption(code, out option);
            bool notAllowed = filled && _notAllowed != null && _notAllowed(code);

            // A slot in the style of the terminal's module slots: square, dark, framed in the item's rarity colour. The
            // top strip is kept for the slot number (and the NOT ALLOWED mark), the rest for the icon and the name.
            var tile = new VisualElement();
            tile.style.width = 132;
            tile.style.height = 132;
            Ui.Margin(tile, 0, 8, 8, 0);
            Ui.Padding(tile, 4, 6);
            tile.style.paddingTop = 18;
            tile.style.alignItems = Align.Center;
            tile.style.justifyContent = Justify.Center;
            Color background = filled ? new Color(0.1f, 0.105f, 0.11f, 0.95f) : new Color(0.05f, 0.055f, 0.06f, 0.9f);
            tile.style.backgroundColor = background;
            Ui.Border(tile, 2, notAllowed ? Ui.Warning : (filled ? Ui.RarityColor(option.Rarity) : new Color(1f, 1f, 1f, 0.12f)));

            if (filled)
            {
                tile.Add(Ui.Icon(option.Icon, 52));
                // Long names get smaller text, and show from their first line, so they fit under the icon.
                Label name = Ui.GameText(option.Name, "ui-text-size__small");
                name.style.fontSize = NameFontSize(RichText.Plain(option.Name));
                name.style.unityTextAlign = TextAnchor.UpperCenter;
                name.style.marginTop = 4;
                name.style.marginLeft = 0;
                name.style.marginRight = 0;
                name.style.maxHeight = 48;
                name.style.overflow = Overflow.Hidden;
                name.style.flexShrink = 0;
                tile.Add(name);
                Ui.Tooltip(tile, option.Context, option.Guid);
            }
            else
            {
                tile.Add(Ui.Label(string.IsNullOrEmpty(code) ? "+" : "?", 44, new Color(1f, 1f, 1f, 0.35f), true));
                Label empty = Ui.GameText(string.IsNullOrEmpty(code) ? "EMPTY" : "UNKNOWN ITEM", "ui-text-size__small");
                empty.style.opacity = 0.6f;
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                tile.Add(empty);
            }

            // The slot number and, when a Fleet Battle's match rules don't allow the item, NOT ALLOWED (the host takes it
            // off when the mission starts): one row, so they never run into each other, drawn over the icon.
            var top = new VisualElement { pickingMode = PickingMode.Ignore };
            top.style.position = Position.Absolute;
            top.style.left = 6;
            top.style.right = 5;
            top.style.top = 1;
            top.style.flexDirection = FlexDirection.Row;
            top.style.justifyContent = Justify.SpaceBetween;
            top.style.alignItems = Align.Center;
            Label number = Ui.GameText((index + 1).ToString(), "ui-text-size__small");
            number.style.opacity = 0.6f;
            number.style.flexShrink = 0;
            number.style.marginLeft = 0;
            top.Add(number);
            if (notAllowed)
            {
                Label mark = Ui.Label("NOT ALLOWED", 11, Ui.Warning, true);
                mark.style.marginLeft = 4;
                mark.style.whiteSpace = WhiteSpace.NoWrap;
                mark.style.overflow = Overflow.Hidden;
                mark.style.flexShrink = 1;
                top.Add(mark);
            }
            tile.Add(top);

            if (editable)
            {
                Ui.Hover(tile, background, Ui.RowHover);
                Ui.Click(tile, () => _onSlotClicked(shelf, index));
            }
            return tile;
        }

        /// <summary>
        /// The text size for an item's name on a tile: the game's small text (18) for short names, smaller for longer
        /// ones, so even "Payload Battery: Cluster Missiles" fits in three lines under the icon.
        /// </summary>
        internal static int NameFontSize(string plainName)
        {
            int length = plainName == null ? 0 : plainName.Length;
            if (length <= 16) return 18;
            return length <= 24 ? 15 : 13;
        }

        public void Close()
        {
            if (_backdrop.parent == null) return;
            Ui.HideTooltip();
            _backdrop.RemoveFromHierarchy();
            Action closed = Closed;
            Closed = null;
            if (closed != null) closed();
        }
    }
}
