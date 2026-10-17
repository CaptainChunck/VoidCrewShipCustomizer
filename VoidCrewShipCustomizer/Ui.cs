using System;
using CG.Client;
using CG.Ship.Object;
using UI;
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

        /// <summary>Makes any element clickable with the left mouse button, the same way the game's lists are.</summary>
        public static Clickable Click(VisualElement element, Action onClick)
        {
            var clickable = new Clickable(() =>
            {
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
            try
            {
                UIAudioProvider.AddSounds(element);
            }
            catch
            {
                // Sounds are a nicety.
            }
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
