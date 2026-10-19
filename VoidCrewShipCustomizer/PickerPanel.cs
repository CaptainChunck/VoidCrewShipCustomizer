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
    /// A pop-up list drawn inside the terminal's own screen: a title, category tabs, and a scrolling list of choices with
    /// icons, rarity colours and the game's own tooltips. Clicking outside the window closes it.
    /// </summary>
    internal sealed class PickerPanel
    {
        public sealed class Entry
        {
            public string Name;
            public string Detail = "";
            public Sprite Icon;
            public Color Accent = Ui.TextDim;
            public string Tag = "";
            public Color TagColor = Ui.Gold;
            public bool Enabled = true;
            public bool Current;
            public string Category = "";
            /// <summary>Shown above the list whatever tab is open (e.g. "Empty this hardpoint").</summary>
            public bool Pinned;
            public ContextInfo Tooltip;
            public GUIDUnion TooltipGuid;
            public Action OnPick;
        }

        private const string AllTab = "ALL";

        private readonly VisualElement _host;
        private readonly VisualElement _backdrop;
        private readonly VisualElement _tabs;
        private readonly ScrollView _scroll;
        private readonly List<Entry> _entries;
        private readonly Dictionary<string, VisualElement> _tabButtons = new Dictionary<string, VisualElement>();
        private string _tab;

        public Action Closed;

        public bool IsOpen
        {
            get { return _backdrop.parent != null; }
        }

        /// <param name="frame">The window's frame colour (gold, or a Void Crew Fleet team's colour in a Fleet Battle).</param>
        public PickerPanel(VisualElement host, string title, string subtitle, List<Entry> entries, string initialTab, Color frame)
        {
            _host = host;
            _entries = entries;

            _backdrop = new VisualElement { name = "ShipCustomizerPicker" };
            Ui.Fill(_backdrop);
            _backdrop.style.backgroundColor = Ui.Backdrop;
            // Click outside the window to close; clicks inside stop at the window.
            _backdrop.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _backdrop) Close();
            });

            var window = new VisualElement();
            window.style.position = Position.Absolute;
            window.style.left = new Length(17, LengthUnit.Percent);
            window.style.right = new Length(17, LengthUnit.Percent);
            window.style.top = new Length(7, LengthUnit.Percent);
            window.style.bottom = new Length(7, LengthUnit.Percent);
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
            var titleLabel = Ui.Label(title, 30, Ui.Gold, true);
            titleLabel.style.flexGrow = 1;
            header.Add(titleLabel);
            header.Add(Ui.Button("CLOSE", Close, 20));
            window.Add(header);

            if (!string.IsNullOrEmpty(subtitle))
            {
                Label sub = Ui.Label(subtitle, 19, Ui.TextDim);
                sub.style.marginTop = 4;
                sub.style.flexShrink = 0;
                window.Add(sub);
            }

            _tabs = new VisualElement();
            _tabs.style.flexDirection = FlexDirection.Row;
            _tabs.style.flexWrap = Wrap.Wrap;
            _tabs.style.flexShrink = 0;
            _tabs.style.marginTop = 10;
            window.Add(_tabs);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.style.marginTop = 10;
            _scroll.mouseWheelScrollSize = 62f;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            window.Add(_scroll);

            List<string> categories = entries.Where(e => !e.Pinned && !string.IsNullOrEmpty(e.Category)).Select(e => e.Category).Distinct().ToList();
            if (categories.Count > 1) AddTab(AllTab);
            foreach (string category in categories) AddTab(category);
            _tab = categories.Count > 1 ? (categories.Contains(initialTab) ? initialTab : AllTab) : null;

            host.Add(_backdrop);
            Ui.Announce(_backdrop);
            Rebuild();
        }

        private void AddTab(string name)
        {
            var tab = new VisualElement();
            Ui.Padding(tab, 6, 14);
            Ui.Margin(tab, 0, 6, 6, 0);
            Ui.Radius(tab, 3);
            Ui.Border(tab, 1, new Color(1f, 1f, 1f, 0.18f));
            tab.Add(Ui.Label(name, 19, Ui.Text, true));
            tab.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (_tab != name) tab.style.backgroundColor = Ui.ButtonHover;
            });
            tab.RegisterCallback<PointerLeaveEvent>(e => StyleTab(name));
            Ui.Click(tab, () =>
            {
                _tab = name;
                foreach (string key in _tabButtons.Keys) StyleTab(key);
                Rebuild();
            });
            _tabButtons[name] = tab;
            _tabs.Add(tab);
            StyleTab(name);
        }

        private void StyleTab(string name)
        {
            VisualElement tab;
            if (!_tabButtons.TryGetValue(name, out tab)) return;
            bool selected = name == _tab;
            tab.style.backgroundColor = selected ? Ui.Gold : Ui.ButtonColor;
            var label = tab.Q<Label>();
            if (label != null) label.style.color = selected ? Color.black : Ui.Text;
        }

        private void Rebuild()
        {
            _scroll.Clear();
            foreach (Entry entry in _entries.Where(e => e.Pinned)) _scroll.Add(MakeRow(entry));
            IEnumerable<Entry> shown = _entries.Where(e => !e.Pinned && (_tab == null || _tab == AllTab || e.Category == _tab));
            string lastCategory = null;
            int count = 0;
            foreach (Entry entry in shown)
            {
                if ((_tab == null || _tab == AllTab) && entry.Category != lastCategory && !string.IsNullOrEmpty(entry.Category) && _tabButtons.Count > 0)
                {
                    Label heading = Ui.Label(entry.Category, 18, Ui.Gold, true);
                    Ui.Margin(heading, count == 0 ? 2 : 12, 0, 4, 2);
                    _scroll.Add(heading);
                }
                lastCategory = entry.Category;
                _scroll.Add(MakeRow(entry));
                count++;
            }
            if (count == 0 && !_entries.Any(e => e.Pinned))
            {
                _scroll.Add(Ui.Label("Nothing fits here.", 20, Ui.TextDim));
            }
            foreach (VisualElement child in _scroll.contentContainer.Children()) Ui.Announce(child);
        }

        private VisualElement MakeRow(Entry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 58;
            row.style.marginBottom = 4;
            Ui.Padding(row, 4, 10);
            Ui.Radius(row, 3);
            Color background = entry.Current ? new Color(0.24f, 0.2f, 0.1f, 1f) : Ui.Row;
            row.style.backgroundColor = background;
            row.style.borderLeftWidth = 6;
            row.style.borderLeftColor = entry.Accent;
            if (entry.Current)
            {
                row.style.borderRightWidth = 2;
                row.style.borderTopWidth = 2;
                row.style.borderBottomWidth = 2;
                row.style.borderRightColor = Ui.Gold;
                row.style.borderTopColor = Ui.Gold;
                row.style.borderBottomColor = Ui.Gold;
            }

            VisualElement icon = Ui.Icon(entry.Icon, 48);
            icon.style.marginRight = 12;
            row.Add(icon);

            var texts = new VisualElement();
            texts.pickingMode = PickingMode.Ignore;
            texts.style.flexGrow = 1;
            texts.style.flexShrink = 1;
            texts.Add(Ui.Label(entry.Name, 23, Ui.Text, true));
            if (!string.IsNullOrEmpty(entry.Detail)) texts.Add(Ui.Label(entry.Detail, 16, Ui.TextDim));
            row.Add(texts);

            if (!string.IsNullOrEmpty(entry.Tag))
            {
                Label tag = Ui.Label(entry.Tag, 17, entry.TagColor, true);
                tag.style.marginLeft = 10;
                tag.style.flexShrink = 0;
                tag.style.maxWidth = new Length(40, LengthUnit.Percent);
                tag.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(tag);
            }

            if (entry.Tooltip != null)
            {
                try
                {
                    row.AddManipulator(new TooltipManipulatorBase<ContextInfoTooltipVE, ContextHoverData>(new ContextHoverData(entry.Tooltip, entry.TooltipGuid)));
                }
                catch (Exception e)
                {
                    Log.Verbose("No tooltip for " + entry.Name + ": " + e.Message);
                }
            }

            if (!entry.Enabled || entry.OnPick == null)
            {
                row.style.opacity = 0.45f;
                return row;
            }
            Ui.Hover(row, background, Ui.RowHover);
            Ui.Click(row, () =>
            {
                Action pick = entry.OnPick;
                Close();
                pick();
            });
            return row;
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
