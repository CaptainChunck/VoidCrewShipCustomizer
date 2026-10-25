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
    /// <see cref="PickerPanel"/> of items for that slot. Pressing on a filled tile and dragging it onto another slot (on the
    /// same rack or another one) copies its item there.
    /// </summary>
    internal sealed class StoragePanel
    {
        /// <summary>A slot's tile as it is drawn: which slot, what it holds, and its frame colour (put back after a drag lit it up).</summary>
        private sealed class Tile
        {
            public VisualElement Root;
            public ShelfInfo Shelf;
            public int Index;
            /// <summary>The item in the slot; null when the slot is empty or holds something the game no longer has.</summary>
            public Option Item;
            public Color Frame;
        }

        /// <summary>
        /// A press on a filled tile. It becomes a drag once the pointer has moved far enough; until then it is still a click,
        /// which the tile's own click handles as before.
        /// </summary>
        private sealed class Drag
        {
            public Tile From;
            public int PointerId;
            public Vector2 Start;
            /// <summary>Where the pointer is now, in the terminal screen's coordinates.</summary>
            public Vector2 Pointer;
            public bool Started;
            /// <summary>The drag took the pointer itself (the tile's click didn't hold it), so it lets it go again.</summary>
            public bool OwnsCapture;
            /// <summary>The loadout as the window showed it when the press began: the copy is checked against it.</summary>
            public HullConfig Seen;
            public VisualElement Ghost;
            public Label Why;
            /// <summary>The slot under the pointer that is lit up, if any.</summary>
            public Tile Over;
            public IVisualElementScheduledItem Scroller;
        }

        /// <summary>
        /// How far a press must move (on the terminal's 1920-wide screen) before it is a drag. Letting go on the slot the
        /// press started on is a click however far it moved, so a shaky VR pointer still opens the list.
        /// </summary>
        internal const float DragThreshold = 16f;

        /// <summary>The picker's tag for an item a rack normally refuses (the AnyItemOnAnyRack setting off).</summary>
        internal const string DoesntFitTag = "DOESN'T FIT THIS RACK";

        private const float TileSize = 132f;
        /// <summary>
        /// The frame of the slot a dragged item would go in: the game's light blue (its pop-up outline), which no rarity
        /// colour is close to (gold would read as a Legendary item's frame).
        /// </summary>
        private static readonly Color DropHere = new Color(0.741f, 0.906f, 0.984f, 1f);
        /// <summary>The dragged copy is a little smaller than a tile, so the frame of the slot under it shows its highlight.</summary>
        private const float GhostScale = 0.76f;
        /// <summary>Near the top or bottom of the rack list (this far in), a drag scrolls it.</summary>
        internal const float EdgeZone = 40f;
        internal const float MaxScrollStep = 16f;
        private const long ScrollTickMs = 30;

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
        private readonly Action<ShelfInfo, int, ShelfInfo, int, HullConfig> _onSlotCopied;
        private readonly List<Tile> _tiles = new List<Tile>();
        /// <summary>The loadout the tiles were drawn from: what the player sees.</summary>
        private HullConfig _drawn;
        private Drag _drag;
        /// <summary>The window was asked to redraw during a drag; it does once the drag is over.</summary>
        private bool _refreshWaiting;

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
        /// <param name="onSlotCopied">A filled slot (rack, slot) was dragged onto another slot (rack, slot), with the
        /// loadout as the window showed it when the drag began.</param>
        public StoragePanel(VisualElement host, HullInfo hull, Func<HullConfig> config, Func<bool> editable, Func<string> viewOnlyReason, Color? team,
                            Func<string, bool> notAllowed, Action<ShelfInfo, int> onSlotClicked, Action<ShelfInfo, int, ShelfInfo, int, HullConfig> onSlotCopied,
                            Action<string> onAction)
        {
            _hull = hull;
            _config = config;
            _editable = editable;
            _viewOnlyReason = viewOnlyReason;
            _notAllowed = notAllowed;
            _onSlotClicked = onSlotClicked;
            _onSlotCopied = onSlotCopied;
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
            // A dragged tile holds the pointer, so the tiles stay as they are until the drag is over, and the window is drawn
            // again then. A copy dropped on a slot someone changed meanwhile is refused, as a pick from a list is.
            if (_drag != null && _drag.Started)
            {
                _refreshWaiting = true;
                return;
            }
            _refreshWaiting = false;
            _scroll.Clear();
            _tiles.Clear();
            HullConfig config = _config();
            _drawn = config.Clone();
            bool editable = _editable();
            _emptyAll.style.display = editable ? DisplayStyle.Flex : DisplayStyle.None;
            _loadRacks.style.display = editable ? DisplayStyle.Flex : DisplayStyle.None;
            int slots = 0;
            foreach (ShelfInfo shelf in _hull.Shelves) slots += shelf.Slots.Count;
            int notAllowed = _notAllowed == null ? 0 : config.Storage.Values.Where(rack => rack != null).Sum(rack => rack.Count(_notAllowed));
            _summary.text = _hull.Shelves.Count == 0
                ? "This ship has no storage racks the customizer can fill."
                : string.Format("{0} of {1} slots filled. {2}", config.StoredCount, slots,
                    editable ? "Click a slot to choose what the ship starts with there, or drag an item onto another slot to copy it." : _viewOnlyReason()) +
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
            VisualElement tile = Box(filled);
            Color frame = notAllowed ? Ui.Warning : (filled ? Ui.RarityColor(option.Rarity) : new Color(1f, 1f, 1f, 0.12f));
            Ui.Border(tile, 2, frame);
            var info = new Tile { Root = tile, Shelf = shelf, Index = index, Item = filled ? option : null, Frame = frame };
            _tiles.Add(info);

            if (filled)
            {
                AddItem(tile, option);
                if (option.Context != null)
                {
                    // No item card pops up while an item is dragged over the tiles.
                    var card = new ContextHoverData(option.Context, option.Guid);
                    Ui.Tooltip(tile, () => _drag != null && _drag.Started ? null : card);
                }
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
                Ui.Hover(tile, Background(filled), Ui.RowHover);
                Ui.Click(tile, () => _onSlotClicked(shelf, index));
                if (filled && _onSlotCopied != null) WatchForDrag(info);
            }
            return tile;
        }

        private static Color Background(bool filled)
        {
            return filled ? new Color(0.1f, 0.105f, 0.11f, 0.95f) : new Color(0.05f, 0.055f, 0.06f, 0.9f);
        }

        /// <summary>A slot's square, without its frame colour or what it holds.</summary>
        private static VisualElement Box(bool filled)
        {
            var box = new VisualElement();
            box.style.width = TileSize;
            box.style.height = TileSize;
            Ui.Margin(box, 0, 8, 8, 0);
            Ui.Padding(box, 4, 6);
            box.style.paddingTop = 18;
            box.style.alignItems = Align.Center;
            box.style.justifyContent = Justify.Center;
            box.style.backgroundColor = Background(filled);
            return box;
        }

        /// <summary>An item's icon and name on a tile.</summary>
        private static void AddItem(VisualElement box, Option item)
        {
            box.Add(Ui.Icon(item.Icon, 52));
            // Long names get smaller text, and show from their first line, so they fit under the icon.
            Label name = Ui.GameText(item.Name, "ui-text-size__small");
            name.style.fontSize = NameFontSize(RichText.Plain(item.Name));
            name.style.unityTextAlign = TextAnchor.UpperCenter;
            name.style.marginTop = 4;
            name.style.marginLeft = 0;
            name.style.marginRight = 0;
            name.style.maxHeight = 48;
            name.style.overflow = Overflow.Hidden;
            name.style.flexShrink = 0;
            box.Add(name);
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

        // ---------------------------------------------------------------- dragging a slot onto another to copy it

        /// <summary>
        /// Lets a filled tile be dragged onto another slot. The tile's own click still opens the list: a press only becomes
        /// a drag once the pointer has moved past <see cref="DragThreshold"/>, and letting go on the same tile is a click.
        /// These are the standard pointer events with pointer capture (the tile's click already holds the pointer from the
        /// press on), so a VR laser that the VR UI Fix turns into pointer events drags like a mouse. They run before the
        /// tile's click sees the event.
        /// </summary>
        private void WatchForDrag(Tile tile)
        {
            VisualElement root = tile.Root;
            root.RegisterCallback<PointerDownEvent>(e => OnPress(tile, e), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerMoveEvent>(e => OnMove(tile, e), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerUpEvent>(e => OnRelease(tile, e), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCancelEvent>(e => OnLost(tile, e.pointerId), TrickleDown.TrickleDown);
            root.RegisterCallback<PointerCaptureOutEvent>(e => OnLost(tile, e.pointerId), TrickleDown.TrickleDown);
        }

        private void OnPress(Tile tile, PointerDownEvent evt)
        {
            if (evt.button != 0) return;
            try
            {
                if (_drag != null)
                {
                    // Another pointer is dragging: one drag at a time. A drag whose release never came is over.
                    if (_drag.Started && _drag.PointerId != evt.pointerId) return;
                    StopDrag();
                }
                _drag = new Drag { From = tile, PointerId = evt.pointerId, Start = evt.position, Pointer = evt.position, Seen = _drawn };
            }
            catch (Exception e)
            {
                DragFailed(e);
            }
        }

        private void OnMove(Tile tile, PointerMoveEvent evt)
        {
            Drag drag = _drag;
            if (drag == null || drag.From != tile || evt.pointerId != drag.PointerId) return;
            try
            {
                drag.Pointer = evt.position;
                if (!drag.Started)
                {
                    if (!PastDragThreshold(drag.Pointer.x - drag.Start.x, drag.Pointer.y - drag.Start.y)) return;
                    StartDrag(drag);
                }
                Follow(drag);
            }
            catch (Exception e)
            {
                DragFailed(e);
            }
        }

        private void OnRelease(Tile tile, PointerUpEvent evt)
        {
            Drag drag = _drag;
            if (drag == null || drag.From != tile || evt.pointerId != drag.PointerId) return;
            try
            {
                drag.Pointer = evt.position;
                Tile target = drag.Started ? TileAt(drag.Pointer) : null;
                StopDrag();
                // Let go on another slot: the item is copied there. On its own slot the tile's click opens the list, as any
                // click does; anywhere else nothing happens.
                AfterDrag(target != null && target != drag.From ? drag : null, target);
            }
            catch (Exception e)
            {
                DragFailed(e);
            }
        }

        /// <summary>The pointer was taken away or cancelled mid-drag (or mid-press): nothing is copied.</summary>
        private void OnLost(Tile tile, int pointerId)
        {
            Drag drag = _drag;
            if (drag == null || drag.From != tile || pointerId != drag.PointerId) return;
            try
            {
                StopDrag();
                AfterDrag(null, null);
            }
            catch (Exception e)
            {
                DragFailed(e);
            }
        }

        /// <summary>Something went wrong mid-drag: it is dropped (nothing is copied) rather than left on screen.</summary>
        private void DragFailed(Exception e)
        {
            Log.Failure("Dragging a storage rack slot", e);
            try
            {
                StopDrag();
                AfterDrag(null, null);
            }
            catch (Exception again)
            {
                Log.Verbose("Could not tidy up after a failed drag: " + again.Message);
            }
        }

        private void StartDrag(Drag drag)
        {
            drag.Started = true;
            VisualElement source = drag.From.Root;
            // The tile's click took the pointer when it was pressed, so the drag keeps getting it outside the tile. If it
            // didn't (an unusual button combination), the drag takes it, and lets it go at the end.
            if (!source.HasPointerCapture(drag.PointerId))
            {
                source.CapturePointer(drag.PointerId);
                drag.OwnsCapture = true;
            }
            Ui.HideTooltip();
            drag.Ghost = MakeGhost(drag.From, out drag.Why);
            _backdrop.Add(drag.Ghost);
            drag.Scroller = _backdrop.schedule.Execute(() => KeepScrolling(drag)).Every(ScrollTickMs);
        }

        /// <summary>
        /// The copy of the tile that follows the pointer while dragging: the tile's own look, a little smaller and see-through
        /// so the slot under it shows its highlight, and under it why a slot won't take the item, as its list would say.
        /// </summary>
        private static VisualElement MakeGhost(Tile from, out Label why)
        {
            var ghost = new VisualElement { name = "ShipCustomizerDraggedItem", pickingMode = PickingMode.Ignore };
            ghost.style.position = Position.Absolute;
            ghost.style.width = TileSize;
            ghost.style.alignItems = Align.Center;

            VisualElement face = Box(true);
            face.pickingMode = PickingMode.Ignore;
            Ui.Margin(face, 0, 0, 0, 0);
            Ui.Border(face, 2, from.Frame);
            AddItem(face, from.Item);
            face.style.opacity = 0.9f;
            face.style.scale = new Scale(new Vector3(GhostScale, GhostScale, 1f));
            ghost.Add(face);

            why = Ui.GameText("", "ui-text-size__small");
            why.style.color = Ui.Warning;
            why.style.unityFontStyleAndWeight = FontStyle.Bold;
            why.style.whiteSpace = WhiteSpace.NoWrap;
            why.style.flexShrink = 0;
            why.style.marginTop = -TileSize * (1f - GhostScale) / 2f + 2f;
            // The game's pop-up grey behind it, so it reads over any tile.
            why.style.backgroundColor = new Color(0.188f, 0.188f, 0.188f, 0.96f);
            Ui.Padding(why, 2, 8);
            why.style.display = DisplayStyle.None;
            ghost.Add(why);
            return ghost;
        }

        /// <summary>Moves the dragged copy to the pointer and lights up the slot under it.</summary>
        private void Follow(Drag drag)
        {
            if (drag.Ghost == null) return;
            Vector2 local = _backdrop.WorldToLocal(drag.Pointer);
            drag.Ghost.style.left = local.x - TileSize / 2f;
            drag.Ghost.style.top = local.y - TileSize / 2f;
            SetOver(drag, TileAt(drag.Pointer));
        }

        /// <summary>
        /// Lights up the slot under the dragged item: light blue when it takes the item, red with the reason (the picker's
        /// own tag) when it doesn't. The slot the item came from isn't lit: letting go there is a click.
        /// </summary>
        private void SetOver(Drag drag, Tile over)
        {
            if (over == drag.From) over = null;
            if (over == drag.Over) return;
            Unlight(drag.Over);
            drag.Over = over;
            string why = over != null ? WhyNot(drag.From.Item, over.Shelf) : null;
            if (over != null)
            {
                Ui.Border(over.Root, 2, why == null ? DropHere : Ui.Warning);
                over.Root.style.backgroundColor = Ui.RowHover;
            }
            if (drag.Why == null) return;
            drag.Why.text = why ?? "";
            drag.Why.style.display = why != null ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>A slot that was lit up as a drop target looks as it was drawn again.</summary>
        private static void Unlight(Tile tile)
        {
            if (tile == null) return;
            Ui.Border(tile.Root, 2, tile.Frame);
            tile.Root.style.backgroundColor = Background(tile.Item != null);
        }

        /// <summary>Why a rack won't take an item, as that slot's list would show it; null when it takes it.</summary>
        private string WhyNot(Option item, ShelfInfo shelf)
        {
            bool notAllowed = _notAllowed != null && _notAllowed(item.Code);
            bool fits = Settings.AnyItemOnAnyRack.Value || Catalog.ShelfAccepts(shelf, item);
            return RefusalTag(notAllowed, fits);
        }

        /// <summary>
        /// The tag a slot's list shows on an item it won't take: the match rules first (NOT ALLOWED IN THIS MATCH), then the
        /// rack's own rules (DOESN'T FIT THIS RACK); null when the slot takes it.
        /// </summary>
        internal static string RefusalTag(bool notAllowed, bool fits)
        {
            if (notAllowed) return MatchRules.NotAllowedTag;
            return fits ? null : DoesntFitTag;
        }

        /// <summary>Whether a press has moved far enough (by dx, dy on the terminal's screen) to be a drag rather than a click.</summary>
        internal static bool PastDragThreshold(float dx, float dy)
        {
            return dx * dx + dy * dy >= DragThreshold * DragThreshold;
        }

        /// <summary>The tile under a point of the terminal's screen, where the rack list shows it (not scrolled out of view); null between tiles.</summary>
        private Tile TileAt(Vector2 point)
        {
            if (!_scroll.contentViewport.worldBound.Contains(point)) return null;
            foreach (Tile tile in _tiles)
            {
                if (tile.Root.panel != null && tile.Root.worldBound.Contains(point)) return tile;
            }
            return null;
        }

        /// <summary>
        /// While a drag is near the top or bottom of the rack list, the list scrolls, so a slot further down can be reached.
        /// The slot under the pointer is looked up again each time, as the mouse wheel scrolls the list during a drag too.
        /// </summary>
        private void KeepScrolling(Drag drag)
        {
            if (_drag != drag || !IsOpen) return;
            try
            {
                Rect view = _scroll.contentViewport.worldBound;
                if (drag.Pointer.x >= view.xMin && drag.Pointer.x <= view.xMax)
                {
                    float step = EdgeScrollStep(drag.Pointer.y, view.yMin, view.yMax);
                    if (step != 0f)
                    {
                        Scroller bar = _scroll.verticalScroller;
                        float low = Math.Min(bar.lowValue, bar.highValue);
                        float high = Math.Max(bar.lowValue, bar.highValue);
                        Vector2 offset = _scroll.scrollOffset;
                        _scroll.scrollOffset = new Vector2(offset.x, Math.Max(low, Math.Min(high, offset.y + step)));
                    }
                }
                SetOver(drag, TileAt(drag.Pointer));
            }
            catch (Exception e)
            {
                DragFailed(e);
            }
        }

        /// <summary>
        /// How far the rack list scrolls on one tick of a drag at height <paramref name="y"/>: up (negative) within
        /// <see cref="EdgeZone"/> of its top or above it, down near or below its bottom, faster towards the edge (at most
        /// <see cref="MaxScrollStep"/>); not at all in between.
        /// </summary>
        internal static float EdgeScrollStep(float y, float top, float bottom)
        {
            if (y < top + EdgeZone) return -MaxScrollStep * Math.Min(1f, (top + EdgeZone - y) / EdgeZone);
            if (y > bottom - EdgeZone) return MaxScrollStep * Math.Min(1f, (y - (bottom - EdgeZone)) / EdgeZone);
            return 0f;
        }

        /// <summary>Ends a press or drag: the dragged copy and the highlight go, and a pointer the drag took is let go.</summary>
        private void StopDrag()
        {
            Drag drag = _drag;
            if (drag == null) return;
            _drag = null;
            if (drag.Scroller != null) drag.Scroller.Pause();
            Unlight(drag.Over);
            drag.Over = null;
            if (drag.Ghost != null) drag.Ghost.RemoveFromHierarchy();
            if (drag.OwnsCapture && drag.From.Root.HasPointerCapture(drag.PointerId)) drag.From.Root.ReleasePointer(drag.PointerId);
        }

        /// <summary>
        /// Once the pointer's event is over (the tile's click has had its turn, and the tiles may be drawn again): the copy,
        /// if any, goes to the terminal, which changes the loadout as a pick would; then the window shows what changed
        /// during the drag.
        /// </summary>
        private void AfterDrag(Drag copied, Tile target)
        {
            if (copied == null && !_refreshWaiting) return;
            _backdrop.schedule.Execute(() =>
            {
                if (!IsOpen) return;
                if (copied != null && target != null)
                {
                    try
                    {
                        _onSlotCopied(copied.From.Shelf, copied.From.Index, target.Shelf, target.Index, copied.Seen);
                    }
                    catch (Exception e)
                    {
                        Log.Failure("Copying a storage rack slot", e);
                    }
                }
                if (_refreshWaiting && (_drag == null || !_drag.Started)) Refresh();
            });
        }

        public void Close()
        {
            if (_backdrop.parent == null) return;
            StopDrag();
            _refreshWaiting = false;
            Ui.HideTooltip();
            _backdrop.RemoveFromHierarchy();
            Action closed = Closed;
            Closed = null;
            if (closed != null) closed();
        }
    }
}
