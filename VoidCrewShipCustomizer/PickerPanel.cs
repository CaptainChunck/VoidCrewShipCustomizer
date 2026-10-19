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
            /// <summary>The detail at full brightness (it matters as much as the name, e.g. a Blessed Homunculus's blessing).</summary>
            public bool DetailBright;
            /// <summary>A line to read, not a choice: it can't be clicked, but isn't greyed out either.</summary>
            public bool Info;
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

        /// <param name="team">A Void Crew Fleet team's colour in a Fleet Battle (the frame and bars take it), otherwise null.</param>
        public PickerPanel(VisualElement host, string title, string subtitle, List<Entry> entries, string initialTab, Color? team)
        {
            _host = host;
            _entries = entries;

            _backdrop = new VisualElement { name = "ShipCustomizerPicker" };
            // Click outside the window to close; clicks inside stop at the window.
            _backdrop.RegisterCallback<PointerDownEvent>(e =>
            {
                if (e.target == _backdrop) Close();
            });
            VisualElement body = Ui.GameWindow(_backdrop, title, Close, team, 17, 6);

            if (!string.IsNullOrEmpty(subtitle))
            {
                Label sub = Ui.GameText(subtitle, "ui-text-size__medium");
                sub.style.unityTextAlign = TextAnchor.MiddleCenter;
                sub.style.flexShrink = 0;
                body.Add(sub);
            }

            _tabs = new VisualElement();
            _tabs.style.flexDirection = FlexDirection.Row;
            _tabs.style.flexWrap = Wrap.Wrap;
            _tabs.style.justifyContent = Justify.Center;
            _tabs.style.flexShrink = 0;
            _tabs.style.marginTop = 8;
            body.Add(_tabs);

            _scroll = new ScrollView(ScrollViewMode.Vertical);
            _scroll.style.flexGrow = 1;
            _scroll.style.marginTop = 6;
            _scroll.mouseWheelScrollSize = 62f;
            _scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            body.Add(_scroll);

            List<string> categories = entries.Where(e => !e.Pinned && !string.IsNullOrEmpty(e.Category)).Select(e => e.Category).Distinct().ToList();
            if (categories.Count > 1) AddTab(AllTab);
            foreach (string category in categories) AddTab(category);
            _tab = categories.Count > 1 ? (categories.Contains(initialTab) ? initialTab : AllTab) : null;

            host.Add(_backdrop);
            Ui.Announce(_backdrop);
            Rebuild();
        }

        /// <summary>A tab is one of the game's small buttons; the open one is the game's selected (orange) button.</summary>
        private void AddTab(string name)
        {
            Button tab = Ui.SmallGameButton(name, () =>
            {
                _tab = name;
                foreach (string key in _tabButtons.Keys) StyleTab(key);
                Rebuild();
            });
            tab.style.marginBottom = 6;
            _tabButtons[name] = tab;
            _tabs.Add(tab);
            StyleTab(name);
        }

        private void StyleTab(string name)
        {
            VisualElement tab;
            if (_tabButtons.TryGetValue(name, out tab)) tab.EnableInClassList("metem-button-selected", name == _tab);
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
                    Label heading = Ui.GameText(entry.Category, "ui-text-size__medium");
                    Ui.Margin(heading, count == 0 ? 2 : 14, 0, 4, 2);
                    _scroll.Add(heading);
                }
                lastCategory = entry.Category;
                _scroll.Add(MakeRow(entry));
                count++;
            }
            if (count == 0 && !_entries.Any(e => e.Pinned))
            {
                Label nothing = Ui.GameText("Nothing fits here.", "ui-text-size__medium");
                nothing.style.opacity = 0.7f;
                _scroll.Add(nothing);
            }
            foreach (VisualElement child in _scroll.contentContainer.Children()) Ui.Announce(child);
        }

        private VisualElement MakeRow(Entry entry)
        {
            VisualElement row = null;
            try
            {
                row = MakeGameRow(entry);
            }
            catch (Exception e)
            {
                Log.WarnOnce("game row", "Could not build a list row from the terminal's own template, so the lists use the customizer's own look: " + e.Message);
            }
            return row ?? MakeOwnRow(entry);
        }

        /// <summary>
        /// A row made from the terminal's LOADOUTS list template (its look, hover, and the gold marking of the current
        /// choice), with the icon, a detail line and a tag added. Null when the template isn't there or has changed.
        /// </summary>
        private VisualElement MakeGameRow(Entry entry)
        {
            if (Ui.ListEntryTemplate == null) return null;
            TemplateContainer item = Ui.ListEntryTemplate.Instantiate();
            Label title = item.Q<Label>("Title");
            VisualElement row = title != null ? title.parent : null;
            if (row == null) return null;
            item.AddToClassList("metem-list-entry");
            // The template is made for the narrow LOADOUTS column and one line of text; here it fills the window and grows.
            item.style.width = new Length(100, LengthUnit.Percent);
            item.style.height = StyleKeyword.Auto;
            row.style.width = StyleKeyword.Auto;
            row.style.flexGrow = 1;
            row.style.height = StyleKeyword.Auto;
            row.style.minHeight = 46;
            row.style.alignItems = Align.Center;
            row.style.paddingTop = 3;
            row.style.paddingBottom = 3;
            VisualElement lockIcon = item.Q("LockIcon");
            if (lockIcon != null) lockIcon.style.display = DisplayStyle.None;
            VisualElement marker = item.Q("RarityMarker");
            // The current choice keeps the game's own (gold) marking; others show their rarity.
            if (marker != null && !entry.Current) marker.style.backgroundColor = entry.Accent;

            int at = row.IndexOf(title);
            title.RemoveFromHierarchy();
            title.text = entry.Name;
            title.style.width = StyleKeyword.Auto;
            title.style.height = StyleKeyword.Auto;
            title.style.whiteSpace = WhiteSpace.Normal;
            var texts = new VisualElement { pickingMode = PickingMode.Ignore };
            texts.style.flexGrow = 1;
            texts.style.flexShrink = 1;
            texts.style.justifyContent = Justify.Center;
            texts.Add(title);
            if (!string.IsNullOrEmpty(entry.Detail))
            {
                Label detail = Ui.GameText(entry.Detail, "ui-text-size__small");
                detail.style.opacity = entry.DetailBright ? 1f : 0.75f;
                texts.Add(detail);
            }
            row.Insert(at, texts);
            if (entry.Icon != null)
            {
                VisualElement icon = Ui.Icon(entry.Icon, 40);
                Ui.Margin(icon, 0, 10, 0, 4);
                row.Insert(at, icon);
            }
            if (!string.IsNullOrEmpty(entry.Tag))
            {
                Label tag = Ui.Label(entry.Tag, 17, entry.TagColor, true);
                Ui.Margin(tag, 0, 12, 0, 10);
                tag.style.flexShrink = 0;
                tag.style.maxWidth = new Length(40, LengthUnit.Percent);
                tag.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Add(tag);
            }
            if (entry.Current) item.AddToClassList("list-item__active");
            if (entry.Info)
            {
                // Nothing to point at: no hover highlight, no click.
                IgnorePointer(item);
                return item;
            }
            AddTooltip(item, entry);

            if (!entry.Enabled || entry.OnPick == null)
            {
                item.SetEnabled(false);
                return item;
            }
            Ui.Click(item, () =>
            {
                Action pick = entry.OnPick;
                Close();
                pick();
            });
            return item;
        }

        private static void IgnorePointer(VisualElement element)
        {
            element.pickingMode = PickingMode.Ignore;
            foreach (VisualElement child in element.Children()) IgnorePointer(child);
        }

        private static void AddTooltip(VisualElement element, Entry entry)
        {
            if (entry.Tooltip == null) return;
            try
            {
                element.AddManipulator(new TooltipManipulatorBase<ContextInfoTooltipVE, ContextHoverData>(new ContextHoverData(entry.Tooltip, entry.TooltipGuid)));
            }
            catch (Exception e)
            {
                Log.Verbose("No tooltip for " + entry.Name + ": " + e.Message);
            }
        }

        /// <summary>The customizer's own row, for when the game's template can't be used.</summary>
        private VisualElement MakeOwnRow(Entry entry)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.minHeight = 58;
            row.style.marginBottom = 4;
            Ui.Padding(row, 4, 10);
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
            if (!string.IsNullOrEmpty(entry.Detail)) texts.Add(Ui.Label(entry.Detail, 16, entry.DetailBright ? Ui.Text : Ui.TextDim));
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

            if (entry.Info) return row;
            AddTooltip(row, entry);

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
