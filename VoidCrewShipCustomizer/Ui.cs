using System;
using System.Linq;
using CG.Client;
using CG.Ship.Object;
using Client.Player.Interactions;
using UI;
using UI.Core;
using UI.Core.Audio;
using UnityEngine;
using UnityEngine.UIElements;

namespace VoidCrewShipCustomizer
{
    /// <summary>
    /// Small helpers for building UI Toolkit elements in code. Everything is styled inline, so it looks the same whatever
    /// style sheets the terminal has loaded, and clicks use the game's own click filters so they behave like its buttons.
    /// </summary>
    internal static class Ui
    {
        public static readonly Color Gold = new Color(0.96f, 0.71f, 0.22f, 1f);
        public static readonly Color Window = new Color(0.06f, 0.065f, 0.07f, 0.97f);
        public static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.6f);
        public static readonly Color Row = new Color(0.13f, 0.135f, 0.14f, 1f);
        public static readonly Color RowHover = new Color(0.22f, 0.225f, 0.23f, 1f);
        public static readonly Color ButtonColor = new Color(0.17f, 0.175f, 0.18f, 0.95f);
        public static readonly Color ButtonHover = new Color(0.3f, 0.3f, 0.31f, 0.95f);
        public static readonly Color Text = new Color(0.93f, 0.93f, 0.93f, 1f);
        public static readonly Color TextDim = new Color(0.62f, 0.63f, 0.65f, 1f);
        public static readonly Color Warning = new Color(1f, 0.45f, 0.35f, 1f);

        public static Label Label(string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.pickingMode = PickingMode.Ignore;
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.marginLeft = 0;
            label.style.marginRight = 0;
            label.style.marginTop = 0;
            label.style.marginBottom = 0;
            label.style.paddingLeft = 0;
            label.style.paddingRight = 0;
            label.style.paddingTop = 0;
            label.style.paddingBottom = 0;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>A flat button: a box with a label, highlighted on hover.</summary>
        public static VisualElement Button(string text, Action onClick, int size = 20)
        {
            var button = new VisualElement();
            button.style.flexDirection = FlexDirection.Row;
            button.style.alignItems = Align.Center;
            button.style.justifyContent = Justify.Center;
            Padding(button, 6, 14);
            Margin(button, 0, 6, 0, 0);
            Radius(button, 3);
            Border(button, 1, new Color(1f, 1f, 1f, 0.18f));
            button.style.backgroundColor = ButtonColor;
            button.Add(Label(text, size, Text, true));
            Hover(button, ButtonColor, ButtonHover);
            Click(button, onClick);
            return button;
        }

        /// <summary>
        /// One of the game's own buttons: its style classes (for example metem-button-size__small, the small version of
        /// SELECT) with its hover and click sounds, and clicks wired the way the game wires its buttons.
        /// </summary>
        public static Button GameButton(string text, Action onClick, params string[] classes)
        {
            var button = new Button { text = text };
            foreach (string name in classes) button.AddToClassList(name);
            button.AddClickedEvent(() =>
            {
                PlaySound(button, UIAudioEventRef.ESoundType.Click, "button");
                try
                {
                    onClick();
                }
                catch (Exception e)
                {
                    Log.Failure("Handling a click in the Ship Customizer", e);
                }
            });
            HoverSound(button, "button");
            return button;
        }

        /// <summary>
        /// One of the game's interface sounds, played once through its own sound channel. The game's way of attaching sounds
        /// to an element keeps the element until the hub unloads, which adds up for lists that are built again and again.
        /// </summary>
        public static void PlaySound(VisualElement element, UIAudioEventRef.ESoundType type, string key)
        {
            try
            {
                if (element.panel != null && ViewEventBus.Instance != null) ViewEventBus.Instance.UIOneShot.Publish(type, key, element.panel);
            }
            catch
            {
                // Sounds are a nicety.
            }
        }

        /// <summary>The game's hover sound when the pointer comes onto an element that can be used.</summary>
        public static EventCallback<PointerEnterEvent> HoverSound(VisualElement element, string key)
        {
            EventCallback<PointerEnterEvent> callback = e =>
            {
                if (element.enabledInHierarchy && element.pickingMode == PickingMode.Position) PlaySound(element, UIAudioEventRef.ESoundType.Hover, key);
            };
            element.RegisterCallback(callback);
            return callback;
        }

        /// <summary>
        /// The game's tooltip (the context card it shows for items and modules) while the pointer is over an element, for
        /// what <paramref name="data"/> returns at that moment (null: none). Shown through the game's shared tooltip, with
        /// plain callbacks that go when the element goes.
        /// </summary>
        public static void Tooltip(VisualElement element, Func<ContextHoverData> data)
        {
            bool showing = false;
            element.RegisterCallback<MouseEnterEvent>(e =>
            {
                showing = false;
                ContextHoverData shown = data();
                TooltipBaseVE tip = ContextInfoController.SharedContextInfoVe;
                if (shown == null || tip == null) return;
                try
                {
                    tip.Show(element, shown, null);
                    showing = true;
                }
                catch (Exception ex)
                {
                    Log.Verbose("No tooltip: " + ex.Message);
                }
            });
            element.RegisterCallback<MouseMoveEvent>(e =>
            {
                TooltipBaseVE tip = ContextInfoController.SharedContextInfoVe;
                if (!showing || tip == null) return;
                try
                {
                    tip.Move(e);
                }
                catch
                {
                    // Only a nicety.
                }
            });
            element.RegisterCallback<MouseLeaveEvent>(e =>
            {
                showing = false;
                HideTooltip();
            });
        }

        public static void Tooltip(VisualElement element, ContextInfo context, GUIDUnion guid)
        {
            if (context == null) return;
            var data = new ContextHoverData(context, guid);
            Tooltip(element, () => data);
        }

        public static void HideTooltip()
        {
            try
            {
                ContextInfoController.SharedContextInfoVe?.Hide();
            }
            catch
            {
                // The tooltip may already be gone.
            }
        }

        /// <summary>A label in the game's own text style (its style classes, for example the panel headers').</summary>
        public static Label GameLabel(string text, params string[] classes)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (string name in classes) label.AddToClassList(name);
            return label;
        }

        /// <summary>A header like the terminal's own (SHIP SYSTEMS, DESCRIPTION): large white text with the game's shadow.</summary>
        public static Label GameHeader(string text)
        {
            return GameLabel(text, "ui-text-size__large", "ui-text-color__white", "loadout-panel-header", "ui-text-dropshadow");
        }

        /// <summary>Body text in the game's style: <paramref name="sizeClass"/> is ui-text-size__small, __medium or __mediumPlus.</summary>
        public static Label GameText(string text, string sizeClass)
        {
            Label label = GameLabel(text, sizeClass, "ui-text-color__white", "ui-text-dropshadow");
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        /// <summary>
        /// One of the terminal's decorations, full width: "metem-decorative-bar-repeating" (the patterned bar along the top
        /// and bottom of the screen) or "metem-decorative__box-border" (the line under each panel header). The tint
        /// recolours it (a Void Crew Fleet team's colour, for example).
        /// </summary>
        public static VisualElement Decoration(string className, float height, Color? tint = null)
        {
            var decoration = new VisualElement { pickingMode = PickingMode.Ignore };
            decoration.AddToClassList(className);
            decoration.style.width = new Length(100, LengthUnit.Percent);
            decoration.style.height = height;
            decoration.style.flexShrink = 0;
            if (tint.HasValue) decoration.style.unityBackgroundImageTintColor = tint.Value;
            return decoration;
        }

        /// <summary>
        /// The row template of the terminal's LOADOUTS list (ShipLoadoutTerminal.ListEntryPrefab), set while a terminal is
        /// on screen. Pop-up lists build their rows from it, so they look and react like the game's own lists.
        /// </summary>
        public static VisualTreeAsset ListEntryTemplate;

        // The game's colours (SharedUIColorTheme), for when its pop-up style sheet isn't loaded.
        private static readonly Color PopupFill = new Color(0.188f, 0.188f, 0.188f, 1f);
        private static readonly Color PopupOutline = new Color(0.741f, 0.906f, 0.984f, 1f);
        private static readonly Color DecoBrown = new Color(0.369f, 0.329f, 0.29f, 1f);

        private static StyleSheet _popupStyle;

        /// <summary>
        /// The game's own pop-up style sheet (PopupBaseStyle: .popup-background, .popup-border, .deco-color). It is loaded in
        /// the hub but not attached to the loadout terminal, so the pop-ups attach it themselves. Null if it isn't loaded.
        /// </summary>
        private static StyleSheet PopupStyle()
        {
            if (_popupStyle != null) return _popupStyle;
            try
            {
                _popupStyle = Resources.FindObjectsOfTypeAll<StyleSheet>().FirstOrDefault(s => s != null && s.name == "PopupBaseStyle");
            }
            catch (Exception e)
            {
                Log.Verbose("Could not look for the game's pop-up style: " + e.Message);
            }
            return _popupStyle;
        }

        /// <summary>
        /// A pop-up window built like the game's own (its ConfirmPopup): the screen darkened, a dark-grey window with the
        /// light-blue outline, a centred header over the brown separator, the content, a row of the game's small buttons
        /// (CLOSE last), and the wings ornament. In a Void Crew Fleet battle the outline takes the team's colour. Returns
        /// the window's body for the content.
        /// </summary>
        public static VisualElement GameWindow(VisualElement backdrop, string title, Action close, Color? team, float sideInset, float topInset,
                                               params VisualElement[] buttons)
        {
            Fill(backdrop);
            backdrop.style.backgroundColor = new Color(0f, 0f, 0f, 0.92f);
            StyleSheet popupStyle = PopupStyle();
            if (popupStyle != null && !backdrop.styleSheets.Contains(popupStyle)) backdrop.styleSheets.Add(popupStyle);

            var background = new VisualElement { name = "Background" };
            background.AddToClassList("popup-background");
            background.style.position = Position.Absolute;
            background.style.left = new Length(sideInset, LengthUnit.Percent);
            background.style.right = new Length(sideInset, LengthUnit.Percent);
            background.style.top = new Length(topInset, LengthUnit.Percent);
            background.style.bottom = new Length(topInset, LengthUnit.Percent);
            background.style.flexDirection = FlexDirection.Column;
            if (popupStyle == null) background.style.backgroundColor = PopupFill;
            background.RegisterCallback<PointerDownEvent>(e => e.StopPropagation());
            backdrop.Add(background);

            var border = new VisualElement { name = "Border" };
            border.AddToClassList("popup-border");
            border.style.flexGrow = 1;
            border.style.flexDirection = FlexDirection.Column;
            if (popupStyle == null)
            {
                Border(border, 2, team.HasValue ? team.Value : PopupOutline);
                border.style.paddingTop = 20;
                border.style.paddingRight = 25;
                border.style.paddingBottom = 5;
                border.style.paddingLeft = 25;
            }
            else if (team.HasValue)
            {
                border.style.unityBackgroundImageTintColor = team.Value;
            }
            background.Add(border);

            Label header = GameLabel(title, "ui-text-size__large", "ui-text-color__white", "ui-text-dropshadow");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.unityTextAlign = TextAnchor.UpperCenter;
            header.style.flexShrink = 0;
            border.Add(header);
            VisualElement separator = Decoration("metem-decorative__box-border", 10);
            separator.AddToClassList("deco-color");
            if (popupStyle == null) SetBorderColor(separator, DecoBrown);
            border.Add(separator);

            var body = new VisualElement();
            body.style.flexGrow = 1;
            body.style.flexShrink = 1;
            body.style.flexDirection = FlexDirection.Column;
            body.style.marginTop = 6;
            border.Add(body);

            var buttonRow = new VisualElement();
            buttonRow.style.flexDirection = FlexDirection.Row;
            buttonRow.style.justifyContent = Justify.Center;
            buttonRow.style.flexShrink = 0;
            buttonRow.style.marginTop = 8;
            foreach (VisualElement button in buttons) buttonRow.Add(button);
            buttonRow.Add(SmallGameButton("CLOSE", close));
            border.Add(buttonRow);

            var wings = new VisualElement { name = "WingsDressup", pickingMode = PickingMode.Ignore };
            wings.AddToClassList("metem-decorative__wings");
            wings.AddToClassList("deco-color");
            wings.style.alignSelf = Align.Center;
            wings.style.flexShrink = 0;
            if (popupStyle == null) wings.style.unityBackgroundImageTintColor = DecoBrown;
            border.Add(wings);
            return body;
        }

        private static void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        /// <summary>The game's small button (the small version of SELECT) at its own size, 42 px high.</summary>
        public static Button SmallGameButton(string text, Action onClick)
        {
            Button button = GameButton(text, onClick, "metem-button-size__small");
            Margin(button, 0, 6, 0, 6);
            button.style.paddingLeft = 24;
            button.style.paddingRight = 24;
            button.style.whiteSpace = WhiteSpace.NoWrap;
            button.style.flexShrink = 0;
            return button;
        }

        /// <summary>Makes any element clickable with the left mouse button, the same way the game's lists are, with its list sounds.</summary>
        public static Clickable Click(VisualElement element, Action onClick)
        {
            Clickable clickable = ClickOnly(element, onClick);
            HoverSound(element, "list");
            return clickable;
        }

        /// <summary>
        /// <see cref="Click"/> without the hover sound, for an element whose callbacks are taken off again later (the hover
        /// sound is then registered with <see cref="HoverSound"/> and removed with the rest).
        /// </summary>
        public static Clickable ClickOnly(VisualElement element, Action onClick)
        {
            var clickable = new Clickable(() =>
            {
                PlaySound(element, UIAudioEventRef.ESoundType.Click, "list");
                try
                {
                    onClick();
                }
                catch (Exception e)
                {
                    Log.Failure("Handling a click in the Ship Customizer", e);
                }
            });
            element.AddUnfilteredClickable(clickable);
            return clickable;
        }

        public static void Hover(VisualElement element, Color normal, Color hover)
        {
            element.RegisterCallback<PointerEnterEvent>(e => element.style.backgroundColor = hover);
            element.RegisterCallback<PointerLeaveEvent>(e => element.style.backgroundColor = normal);
        }

        /// <summary>Tells the game a new element is on screen, as it does for its own (sounds, navigation).</summary>
        public static void Announce(VisualElement element)
        {
            try
            {
                if (element.panel != null && ViewEventBus.Instance != null) ViewEventBus.Instance.VEAddedtoPanel.Publish(element, element.panel);
            }
            catch
            {
                // Only a nicety.
            }
        }

        public static void Border(VisualElement element, float width, Color color)
        {
            element.style.borderLeftWidth = width;
            element.style.borderRightWidth = width;
            element.style.borderTopWidth = width;
            element.style.borderBottomWidth = width;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
        }

        public static void Radius(VisualElement element, float radius)
        {
            element.style.borderTopLeftRadius = radius;
            element.style.borderTopRightRadius = radius;
            element.style.borderBottomLeftRadius = radius;
            element.style.borderBottomRightRadius = radius;
        }

        public static void Padding(VisualElement element, float vertical, float horizontal)
        {
            element.style.paddingTop = vertical;
            element.style.paddingBottom = vertical;
            element.style.paddingLeft = horizontal;
            element.style.paddingRight = horizontal;
        }

        public static void Margin(VisualElement element, float top, float right, float bottom, float left)
        {
            element.style.marginTop = top;
            element.style.marginRight = right;
            element.style.marginBottom = bottom;
            element.style.marginLeft = left;
        }

        public static void Fill(VisualElement element)
        {
            element.style.position = Position.Absolute;
            element.style.left = 0;
            element.style.top = 0;
            element.style.right = 0;
            element.style.bottom = 0;
        }

        public static VisualElement Icon(Sprite sprite, float size)
        {
            var icon = new VisualElement();
            icon.pickingMode = PickingMode.Ignore;
            icon.style.width = size;
            icon.style.height = size;
            icon.style.flexShrink = 0;
            if (sprite != null) icon.style.backgroundImage = new StyleBackground(sprite);
            icon.style.backgroundSize = new BackgroundSize(BackgroundSizeType.Contain);
            icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            icon.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
            icon.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
            return icon;
        }

        public static Color RarityColor(RarityType rarity)
        {
            switch (rarity)
            {
                case RarityType.Common: return new Color(0.75f, 0.75f, 0.75f, 1f);
                case RarityType.Rare: return new Color(0.25f, 0.62f, 1f, 1f);
                case RarityType.Epic: return new Color(0.72f, 0.4f, 1f, 1f);
                case RarityType.Legendary: return new Color(1f, 0.66f, 0.12f, 1f);
                case RarityType.Special: return new Color(1f, 0.33f, 0.33f, 1f);
                default: return new Color(0.4f, 0.4f, 0.42f, 1f);
            }
        }

        public static string RarityName(RarityType rarity)
        {
            return rarity == RarityType.None ? "" : rarity.ToString().ToUpperInvariant();
        }
    }
}
