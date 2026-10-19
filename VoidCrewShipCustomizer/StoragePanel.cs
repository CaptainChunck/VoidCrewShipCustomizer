using System;
using System.Collections.Generic;
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
        private readonly bool _editable;
        private readonly string _viewOnlyReason;
        private readonly Action<ShelfInfo, int> _onSlotClicked;

        public Action<string> OnAction;
        public Action Closed;

        public bool IsOpen
        {
            get { return _backdrop.parent != null; }
        }

        /// <param name="viewOnlyReason">Why the racks can't be changed, shown when <paramref name="editable"/> is false.</param>
        /// <param name="frame">The window's frame colour (gold, or a Void Crew Fleet team's colour in a Fleet Battle).</param>
        public StoragePanel(VisualElement host, HullInfo hull, Func<HullConfig> config, bool editable, string viewOnlyReason, Color frame,
                            Action<ShelfInfo, int> onSlotClicked, Action<string> onAction)
        {
            _hull = hull;
            _config = config;
            _editable = editable;
            _viewOnlyReason = viewOnlyReason;
            _onSlotClicked = onSlotClicked;
            OnAction = onAction;

            _backdrop = new VisualElement { name = "ShipCustomizerStorage" };
            Ui.Fill(_backdrop);
            _backdrop.style.backgroundColor = Ui.Backdrop;
            _backdrop.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _backdrop) Close();
            });

            var window = new VisualElement();
            window.style.position = Position.Absolute;
            window.style.left = new Length(10, LengthUnit.Percent);
            window.style.right = new Length(10, LengthUnit.Percent);
            window.style.top = new Length(6, LengthUnit.Percent);
            window.style.bottom = new Length(6, LengthUnit.Percent);
            window.style.backgroundColor = Ui.Window;
            Ui.Border(window, 2, frame);
            Ui.Radius(window, 6);
            Ui.Padding(window, 14, 18);
            window.style.flexDirection = FlexDirection.Column;
            window.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            _backdrop.Add(window);

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.flexShrink = 0;
            Label title = Ui.Label("STORAGE RACKS · " + hull.Name.ToUpperInvariant(), 30, Ui.Gold, true);
            title.style.flexGrow = 1;
            header.Add(title);
            if (editable)
            {
                header.Add(Ui.Button("EMPTY ALL", () => Act("empty"), 20));
                header.Add(Ui.Button("COPY FROM LOADOUT", () => Act("copy"), 20));
            }
            header.Add(Ui.Button("CLOSE", Close, 20));
            window.Add(header);

            _summary = Ui.Label("", 19, Ui.TextDim);
            _summary.style.marginTop = 4;
            _summary.style.flexShrink = 0;
            window.Add(_summary);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.style.marginTop = 10;
            _scroll.mouseWheelScrollSize = 70f;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            window.Add(_scroll);

            host.Add(_backdrop);
            Ui.Announce(_backdrop);
            Refresh();
        }

        private void Act(string action)
        {
            if (OnAction != null) OnAction(action);
        }

        /// <summary>Redraws the tiles from the current loadout.</summary>
        public void Refresh()
        {
            _scroll.Clear();
            HullConfig config = _config();
            int slots = 0;
            foreach (ShelfInfo shelf in _hull.Shelves) slots += shelf.Slots.Count;
            _summary.text = _hull.Shelves.Count == 0
                ? "This ship has no storage racks the customizer can fill."
                : string.Format("{0} of {1} slots filled. {2}", config.StoredCount, slots,
                    _editable ? "Click a slot to choose what the ship starts with there." : _viewOnlyReason);

            foreach (ShelfInfo shelf in _hull.Shelves)
            {
                List<string> codes;
                config.Storage.TryGetValue(shelf.CoreIndex, out codes);
                Label heading = Ui.Label(shelf.Name + "  ·  " + shelf.Slots.Count + " SLOTS", 20, Ui.Gold, true);
                Ui.Margin(heading, 8, 0, 6, 2);
                _scroll.Add(heading);

                var grid = new VisualElement();
                grid.style.flexDirection = FlexDirection.Row;
                grid.style.flexWrap = Wrap.Wrap;
                for (int i = 0; i < shelf.Slots.Count; i++)
                {
                    string code = codes != null && i < codes.Count ? codes[i] : "";
                    grid.Add(MakeTile(shelf, i, code));
                }
                _scroll.Add(grid);
            }
            foreach (VisualElement child in _scroll.contentContainer.Children()) Ui.Announce(child);
        }

        private VisualElement MakeTile(ShelfInfo shelf, int index, string code)
        {
            Option option = null;
            bool filled = !string.IsNullOrEmpty(code) && Catalog.TryGetOption(code, out option);

            var tile = new VisualElement();
            tile.style.width = 132;
            tile.style.height = 132;
            Ui.Margin(tile, 0, 8, 8, 0);
            Ui.Padding(tile, 6, 6);
            Ui.Radius(tile, 4);
            tile.style.alignItems = Align.Center;
            tile.style.justifyContent = Justify.Center;
            Color background = filled ? Ui.Row : new Color(0.09f, 0.095f, 0.1f, 1f);
            tile.style.backgroundColor = background;
            Ui.Border(tile, 2, filled ? Ui.RarityColor(option.Rarity) : new Color(1f, 1f, 1f, 0.15f));

            Label number = Ui.Label((index + 1).ToString(), 15, Ui.TextDim);
            number.style.position = Position.Absolute;
            number.style.left = 6;
            number.style.top = 4;
            tile.Add(number);

            if (filled)
            {
                tile.Add(Ui.Icon(option.Icon, 64));
                Label name = Ui.Label(option.Name, 15, Ui.Text);
                name.style.unityTextAlign = TextAnchor.MiddleCenter;
                name.style.marginTop = 4;
                name.style.maxHeight = 40;
                name.style.overflow = Overflow.Hidden;
                tile.Add(name);
                if (option.Context != null)
                {
                    try
                    {
                        tile.AddManipulator(new TooltipManipulatorBase<ContextInfoTooltipVE, ContextHoverData>(new ContextHoverData(option.Context, option.Guid)));
                    }
                    catch
                    {
                        // No tooltip, no problem.
                    }
                }
            }
            else
            {
                tile.Add(Ui.Label(string.IsNullOrEmpty(code) ? "+" : "?", 44, new Color(1f, 1f, 1f, 0.35f), true));
                Label empty = Ui.Label(string.IsNullOrEmpty(code) ? "EMPTY" : "UNKNOWN ITEM", 14, Ui.TextDim);
                empty.style.unityTextAlign = TextAnchor.MiddleCenter;
                tile.Add(empty);
            }

            if (_editable)
            {
                Ui.Hover(tile, background, Ui.RowHover);
                Ui.Click(tile, () => _onSlotClicked(shelf, index));
            }
            return tile;
        }

        public void Close()
        {
            if (_backdrop.parent == null) return;
            try
            {
                ContextInfoController.SharedContextInfoVe?.Hide();
            }
            catch
            {
                // The tooltip may already be gone.
            }
            _backdrop.RemoveFromHierarchy();
            Action closed = Closed;
            Closed = null;
            if (closed != null) closed();
        }
    }
}
