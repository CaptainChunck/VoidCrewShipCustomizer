using System;
using System.Collections.Generic;
using System.Linq;
using Client.Player.Interactions;
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
            /// <summary>The code of the module, weapon or item the row stands for, if any (see <see cref="TellApart"/>).</summary>
            public string Code;
            public Action OnPick;
        }

        /// <summary>
        /// A list row, made once and used again for other entries and other lists. A row made from the game's LOADOUTS
        /// template brings the game's text helper, which registers itself with the game until the hub unloads, so a new row
        /// for every entry of every list would pile up; reused rows don't.
        /// </summary>
        private sealed class Row
        {
            public VisualElement Root;
            public Label Title;
            public Label Detail;
            public VisualElement Icon;
            public Label Tag;
            /// <summary>The template's coloured bar on the left (game rows only).</summary>
            public VisualElement Marker;
            public PickerPanel Owner;
            public Entry Entry;
            public Color Background;
        }

        private const string AllTab = "ALL";
        /// <summary>More rows than this open on the first tab instead of ALL.</summary>
        private const int ManyRows = 100;
        /// <summary>How long after opening a click in the window is still taken for the click that opened it.</summary>
        private const float PickDelaySeconds = 0.35f;
        /// <summary>
        /// How long after the list was filled in again (another player's change) a click isn't taken for a pick: a click
        /// that started on a row before could end on the same row showing another entry.
        /// </summary>
        private const float RefreshPickDelaySeconds = 0.2f;

        private static readonly Stack<Row> Pool = new Stack<Row>();
        /// <summary>The template the pooled rows were made from (a new terminal can bring a new one).</summary>
        private static VisualTreeAsset _poolTemplate;

        private readonly VisualElement _host;
        private readonly VisualElement _backdrop;
        private readonly Label _subtitle;
        private readonly VisualElement _tabs;
        private readonly ScrollView _scroll;
        private List<Entry> _entries;
        private readonly Dictionary<string, VisualElement> _tabButtons = new Dictionary<string, VisualElement>();
        private List<string> _categories = new List<string>();
        private readonly List<Row> _rows = new List<Row>();
        private string _tab;
        private float _openedAt;
        private float _refreshedAt = -100f;

        public Action Closed;

        public bool IsOpen
        {
            get { return _backdrop.parent != null; }
        }

        /// <summary>How far the list is scrolled.</summary>
        public Vector2 ScrollOffset
        {
            get { return _scroll.scrollOffset; }
        }

        /// <summary>Scrolls the list to where it was (once the new list has been laid out).</summary>
        public void ScrollTo(Vector2 offset)
        {
            _scroll.schedule.Execute(() => _scroll.scrollOffset = offset).ExecuteLater(1);
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

            // Always there (hidden while empty), so the list can be filled in again with another subtitle (see Refresh).
            _subtitle = Ui.GameText(subtitle ?? "", "ui-text-size__medium");
            _subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _subtitle.style.flexShrink = 0;
            _subtitle.style.display = string.IsNullOrEmpty(subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
            body.Add(_subtitle);

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

            MakeTabs(Categories(entries), initialTab);
            _openedAt = Time.unscaledTime;

            host.Add(_backdrop);
            Ui.Announce(_backdrop);
            Rebuild();
        }

        /// <summary>The tabs a list has: one per category of its (not pinned) entries, in their order.</summary>
        private static List<string> Categories(List<Entry> entries)
        {
            return entries.Where(e => !e.Pinned && !string.IsNullOrEmpty(e.Category)).Select(e => e.Category).Distinct().ToList();
        }

        /// <summary>The category tabs (ALL first when there are several), opening on <paramref name="wantedTab"/> when it is one of them.</summary>
        private void MakeTabs(List<string> categories, string wantedTab)
        {
            _tabs.Clear();
            _tabButtons.Clear();
            _categories = categories;
            if (categories.Count > 1) AddTab(AllTab);
            foreach (string category in categories) AddTab(category);
            // A long list (every item for a rack slot) opens on its first tab rather than ALL: hundreds of rows at once make
            // the window slow to open, in VR above all.
            string opening = _entries.Count(e => !e.Pinned) > ManyRows ? categories.FirstOrDefault() : AllTab;
            _tab = categories.Count > 1 ? (wantedTab == AllTab || categories.Contains(wantedTab) ? wantedTab : opening) : null;
            // The tab the window opens on is highlighted from the start, not only once another one is clicked.
            foreach (string key in _tabButtons.Keys) StyleTab(key);
        }

        /// <summary>
        /// Fills the open list in again with new entries, after another player changed the loadout it shows: its tab and how
        /// far it is scrolled stay (the tabs are made again only if the categories changed), and a click that was on its
        /// way while the rows changed isn't taken for a pick.
        /// </summary>
        public void Refresh(string subtitle, List<Entry> entries)
        {
            if (!IsOpen) return;
            Vector2 offset = _scroll.scrollOffset;
            // Nothing to see changed (the host's answer to this player's own pick, say): the rows show the same entries in the
            // same places, so clicks aren't held back.
            bool looksSame = (subtitle ?? "") == _subtitle.text && SameLook(_entries, entries);
            _subtitle.text = subtitle ?? "";
            _subtitle.style.display = string.IsNullOrEmpty(subtitle) ? DisplayStyle.None : DisplayStyle.Flex;
            _entries = entries;
            List<string> categories = Categories(entries);
            if (!categories.SequenceEqual(_categories)) MakeTabs(categories, _tab);
            if (!looksSame) _refreshedAt = Time.unscaledTime;
            Rebuild();
            ScrollTo(offset);
        }

        /// <summary>Whether two lists of entries look the same, row for row (what they do when picked aside).</summary>
        internal static bool SameLook(List<Entry> a, List<Entry> b)
        {
            if (a == null || b == null || a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                Entry x = a[i];
                Entry y = b[i];
                if (x.Name != y.Name || x.Detail != y.Detail || x.DetailBright != y.DetailBright || x.Info != y.Info || !ReferenceEquals(x.Icon, y.Icon) || x.Accent != y.Accent ||
                    x.Tag != y.Tag || x.TagColor != y.TagColor || x.Enabled != y.Enabled || x.Current != y.Current || x.Category != y.Category ||
                    x.Pinned != y.Pinned || x.Code != y.Code || (x.OnPick == null) != (y.OnPick == null)) return false;
            }
            return true;
        }

        /// <summary>
        /// Different things can share a name: the game has two of several payload batteries (one of each in two
        /// categories), and mods add their own items. List rows that would read exactly the same (name and detail line)
        /// each get what tells them apart (<paramref name="noteOf"/>: where the item comes from) at the end of their
        /// detail line, numbered when even that is the same, instead of one of them being hidden.
        /// </summary>
        internal static void TellApart(IList<Entry> entries, Func<Entry, string> noteOf)
        {
            foreach (IGrouping<string, Entry> same in entries.Where(e => !e.Pinned && !e.Info).GroupBy(e => (e.Name ?? "") + "\n" + (e.Detail ?? "")).Where(g => g.Count() > 1).ToList())
            {
                List<Entry> group = same.ToList();
                List<string> notes = group.Select(e => noteOf(e) ?? "").ToList();
                for (int i = 0; i < group.Count; i++)
                {
                    string note = notes[i];
                    if (note.Length == 0) note = "variant " + (i + 1);
                    else if (notes.Count(n => n == notes[i]) > 1) note += " (" + (i + 1) + ")";
                    group[i].Detail = string.IsNullOrEmpty(group[i].Detail) ? note : group[i].Detail + "  ·  " + note;
                }
            }
        }

        /// <summary>The terminal is gone: its rows belong to its screen, so none are used again.</summary>
        public static void ClearPool()
        {
            Pool.Clear();
            _poolTemplate = null;
        }

        /// <summary>A tab is one of the game's small buttons; the open one is the game's selected (orange) button.</summary>
        private void AddTab(string name)
        {
            Button tab = Ui.SmallGameButton(name, () =>
            {
                _tab = name;
                foreach (string key in _tabButtons.Keys) StyleTab(key);
                Rebuild();
                _scroll.scrollOffset = Vector2.zero;
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
            ReleaseRows();
            _scroll.Clear();
            foreach (Entry entry in _entries.Where(e => e.Pinned)) _scroll.Add(RowFor(entry));
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
                _scroll.Add(RowFor(entry));
                count++;
            }
            if (count == 0 && !_entries.Any(e => e.Pinned))
            {
                Label nothing = Ui.GameText("Nothing fits here.", "ui-text-size__medium");
                nothing.style.opacity = 0.7f;
                _scroll.Add(nothing);
            }
        }

        /// <summary>A row showing this entry: one from the pool when there is one, otherwise a new one.</summary>
        private VisualElement RowFor(Entry entry)
        {
            if (_poolTemplate != Ui.ListEntryTemplate)
            {
                Pool.Clear();
                _poolTemplate = Ui.ListEntryTemplate;
            }
            Row row = Pool.Count > 0 ? Pool.Pop() : MakeRow();
            row.Owner = this;
            row.Entry = entry;
            Fill(row, entry);
            _rows.Add(row);
            return row.Root;
        }

        private void ReleaseRows()
        {
            foreach (Row row in _rows)
            {
                row.Owner = null;
                row.Entry = null;
                row.Root.RemoveFromHierarchy();
                Pool.Push(row);
            }
            _rows.Clear();
        }

        private Row MakeRow()
        {
            Row row = null;
            try
            {
                row = MakeGameRow();
            }
            catch (Exception e)
            {
                Log.WarnOnce("game row", "Could not build a list row from the terminal's own template, so the lists use the customizer's own look: " + e.Message);
            }
            row = row ?? MakeOwnRow();
            Row made = row;
            // Wired once; the entry the row shows decides what a click does and which tooltip appears.
            Ui.Click(made.Root, () =>
            {
                PickerPanel owner = made.Owner;
                Entry entry = made.Entry;
                if (owner != null && entry != null) owner.Pick(entry);
            });
            Ui.Tooltip(made.Root, () => made.Entry != null && made.Entry.Tooltip != null ? new ContextHoverData(made.Entry.Tooltip, made.Entry.TooltipGuid) : null);
            return made;
        }

        private void Pick(Entry entry)
        {
            Action pick = entry.OnPick;
            if (pick == null || !entry.Enabled || entry.Info) return;
            // The second click of a double-click (or a bouncing VR trigger) that opened this window isn't a pick in it.
            if (Time.unscaledTime - _openedAt < PickDelaySeconds) return;
            if (Time.unscaledTime - _refreshedAt < RefreshPickDelaySeconds) return;
            Close();
            pick();
        }

        /// <summary>
        /// A row made from the terminal's LOADOUTS list template (its look, hover, and the gold marking of the current
        /// choice), with an icon, a detail line and a tag added. Null when the template isn't there or has changed.
        /// </summary>
        private static Row MakeGameRow()
        {
            if (Ui.ListEntryTemplate == null) return null;
            TemplateContainer item = Ui.ListEntryTemplate.Instantiate();
            Label title = item.Q<Label>("Title");
            VisualElement line = title != null ? title.parent : null;
            if (line == null) return null;
            item.AddToClassList("metem-list-entry");
            // The template is made for the narrow LOADOUTS column and one line of text; here it fills the window and grows.
            item.style.width = new Length(100, LengthUnit.Percent);
            item.style.height = StyleKeyword.Auto;
            line.style.width = StyleKeyword.Auto;
            line.style.flexGrow = 1;
            line.style.height = StyleKeyword.Auto;
            line.style.minHeight = 46;
            line.style.alignItems = Align.Center;
            line.style.paddingTop = 3;
            line.style.paddingBottom = 3;
            VisualElement lockIcon = item.Q("LockIcon");
            if (lockIcon != null) lockIcon.style.display = DisplayStyle.None;

            int at = line.IndexOf(title);
            title.RemoveFromHierarchy();
            title.style.width = StyleKeyword.Auto;
            title.style.height = StyleKeyword.Auto;
            title.style.whiteSpace = WhiteSpace.Normal;
            var texts = new VisualElement();
            texts.style.flexGrow = 1;
            texts.style.flexShrink = 1;
            texts.style.justifyContent = Justify.Center;
            texts.Add(title);
            Label detail = Ui.GameText("", "ui-text-size__small");
            texts.Add(detail);
            line.Insert(at, texts);
            VisualElement icon = Ui.Icon(null, 40);
            Ui.Margin(icon, 0, 10, 0, 4);
            line.Insert(at, icon);
            Label tag = Ui.Label("", 17, Ui.Gold, true);
            Ui.Margin(tag, 0, 12, 0, 10);
            tag.style.flexShrink = 0;
            tag.style.maxWidth = new Length(40, LengthUnit.Percent);
            tag.style.unityTextAlign = TextAnchor.MiddleRight;
            line.Add(tag);
            IgnorePointer(item);
            return new Row { Root = item, Title = title, Detail = detail, Icon = icon, Tag = tag, Marker = item.Q("RarityMarker") };
        }

        /// <summary>The customizer's own row, for when the game's template can't be used.</summary>
        private static Row MakeOwnRow()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.minHeight = 58;
            root.style.marginBottom = 4;
            Ui.Padding(root, 4, 10);
            root.style.borderLeftWidth = 6;

            VisualElement icon = Ui.Icon(null, 48);
            icon.style.marginRight = 12;
            root.Add(icon);
            var texts = new VisualElement();
            texts.style.flexGrow = 1;
            texts.style.flexShrink = 1;
            Label title = Ui.Label("", 23, Ui.Text, true);
            Label detail = Ui.Label("", 16, Ui.TextDim);
            texts.Add(title);
            texts.Add(detail);
            root.Add(texts);
            Label tag = Ui.Label("", 17, Ui.Gold, true);
            tag.style.marginLeft = 10;
            tag.style.flexShrink = 0;
            tag.style.maxWidth = new Length(40, LengthUnit.Percent);
            tag.style.unityTextAlign = TextAnchor.MiddleRight;
            root.Add(tag);
            IgnorePointer(root);
            var row = new Row { Root = root, Title = title, Detail = detail, Icon = icon, Tag = tag };
            root.RegisterCallback<PointerEnterEvent>(e =>
            {
                if (root.enabledInHierarchy && root.pickingMode == PickingMode.Position) root.style.backgroundColor = Ui.RowHover;
            });
            root.RegisterCallback<PointerLeaveEvent>(e => root.style.backgroundColor = row.Background);
            return row;
        }

        /// <summary>Only the row itself takes the pointer (its hover, click and tooltip); its parts don't.</summary>
        private static void IgnorePointer(VisualElement row)
        {
            foreach (VisualElement child in row.Query<VisualElement>().ToList())
            {
                if (child != row) child.pickingMode = PickingMode.Ignore;
            }
        }

        /// <summary>Shows an entry in a row (which may have shown another entry before).</summary>
        private static void Fill(Row row, Entry entry)
        {
            row.Title.text = entry.Name ?? "";
            bool hasDetail = !string.IsNullOrEmpty(entry.Detail);
            row.Detail.text = hasDetail ? entry.Detail : "";
            row.Detail.style.display = hasDetail ? DisplayStyle.Flex : DisplayStyle.None;
            if (row.Marker != null)
            {
                // Game rows: the detail at the game's dimmer white, the current choice in the template's own gold.
                row.Detail.style.opacity = entry.DetailBright ? 1f : 0.75f;
                if (entry.Current) row.Marker.style.backgroundColor = StyleKeyword.Null;
                else row.Marker.style.backgroundColor = entry.Accent;
                row.Root.EnableInClassList("list-item__active", entry.Current);
            }
            else
            {
                row.Detail.style.color = entry.DetailBright ? Ui.Text : Ui.TextDim;
                row.Background = entry.Current ? new Color(0.24f, 0.2f, 0.1f, 1f) : Ui.Row;
                row.Root.style.backgroundColor = row.Background;
                row.Root.style.borderLeftColor = entry.Accent;
                Ui.Border(row.Root, entry.Current ? 2 : 0, Ui.Gold);
                row.Root.style.borderLeftWidth = 6;
                row.Root.style.borderLeftColor = entry.Accent;
            }
            row.Icon.style.display = entry.Icon != null ? DisplayStyle.Flex : DisplayStyle.None;
            row.Icon.style.backgroundImage = entry.Icon != null ? new StyleBackground(entry.Icon) : new StyleBackground(StyleKeyword.None);
            bool hasTag = !string.IsNullOrEmpty(entry.Tag);
            row.Tag.text = hasTag ? entry.Tag : "";
            row.Tag.style.color = entry.TagColor;
            row.Tag.style.display = hasTag ? DisplayStyle.Flex : DisplayStyle.None;

            // A line to read takes no pointer at all (no hover, no click) but isn't greyed out; a choice that can't be
            // picked now is greyed out the game's way.
            row.Root.pickingMode = entry.Info ? PickingMode.Ignore : PickingMode.Position;
            bool usable = entry.Info || (entry.Enabled && entry.OnPick != null);
            row.Root.SetEnabled(usable);
            if (row.Marker == null) row.Root.style.opacity = usable ? 1f : 0.45f;
        }

        public void Close()
        {
            if (_backdrop.parent == null) return;
            Ui.HideTooltip();
            ReleaseRows();
            _backdrop.RemoveFromHierarchy();
            Action closed = Closed;
            Closed = null;
            if (closed != null) closed();
        }
    }
}
