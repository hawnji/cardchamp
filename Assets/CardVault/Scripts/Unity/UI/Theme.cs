using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI
{
    /// <summary>Design tokens. Sizes are in panel pixels at the 1080-wide reference resolution.</summary>
    public static class Theme
    {
        public static readonly Color Background = Hex("#0F1115");
        public static readonly Color Surface = Hex("#1A1D24");
        public static readonly Color SurfaceRaised = Hex("#242833");
        public static readonly Color Border = Hex("#2F3440");
        public static readonly Color Text = Hex("#F2F2F2");
        public static readonly Color TextMuted = Hex("#9BA3B4");
        public static readonly Color Accent = Hex("#E3B341");
        public static readonly Color AccentDark = Hex("#7A5C12");
        public static readonly Color Positive = Hex("#3FB950");
        public static readonly Color Negative = Hex("#F85149");
        public static readonly Color Info = Hex("#58A6FF");
        public static readonly Color Overlay = new Color(0, 0, 0, 0.72f);

        public const float FontSmall = 30;
        public const float FontBody = 36;
        public const float FontTitle = 48;
        public const float FontHero = 64;
        public const float Gutter = 32;
        public const float Radius = 20;
        /// <summary>Minimum touch target (about 48dp on a typical phone).</summary>
        public const float TouchTarget = 120;

        public static Color Hex(string hex, Color? fallback = null)
            => !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c) ? c : fallback ?? Color.gray;

        public static void Round(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        public static void BorderColor(VisualElement e, Color c, float width)
        {
            e.style.borderTopColor = c;
            e.style.borderBottomColor = c;
            e.style.borderLeftColor = c;
            e.style.borderRightColor = c;
            e.style.borderTopWidth = width;
            e.style.borderBottomWidth = width;
            e.style.borderLeftWidth = width;
            e.style.borderRightWidth = width;
        }

        public static void Padding(VisualElement e, float v, float h)
        {
            e.style.paddingTop = v;
            e.style.paddingBottom = v;
            e.style.paddingLeft = h;
            e.style.paddingRight = h;
        }

        public static void Margin(VisualElement e, float v, float h)
        {
            e.style.marginTop = v;
            e.style.marginBottom = v;
            e.style.marginLeft = h;
            e.style.marginRight = h;
        }
    }
}
