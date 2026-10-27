using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace CardVault.UnityLayer.UI
{
    /// <summary>Small factory for consistently styled UI Toolkit elements built in code.</summary>
    public static class Ui
    {
        public static VisualElement Column(params VisualElement[] children)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Column;
            foreach (var c in children) if (c != null) e.Add(c);
            return e;
        }

        public static VisualElement Row(params VisualElement[] children)
        {
            var e = new VisualElement();
            e.style.flexDirection = FlexDirection.Row;
            e.style.alignItems = Align.Center;
            foreach (var c in children) if (c != null) e.Add(c);
            return e;
        }

        public static VisualElement Spacer(float height = 0)
        {
            var e = new VisualElement();
            if (height > 0) e.style.height = height;
            else e.style.flexGrow = 1;
            return e;
        }

        public static Label Text(string text, float size = Theme.FontBody, Color? color = null, bool bold = false, bool wrap = true)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color ?? Theme.Text;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.whiteSpace = wrap ? WhiteSpace.Normal : WhiteSpace.NoWrap;
            l.style.marginTop = 0;
            l.style.marginBottom = 0;
            l.style.paddingLeft = 0;
            l.style.paddingRight = 0;
            return l;
        }

        public static Label Muted(string text, float size = Theme.FontSmall) => Text(text, size, Theme.TextMuted);

        public static Label Title(string text) => Text(text, Theme.FontTitle, bold: true);

        public static Label SectionHeader(string text)
        {
            var l = Text(text.ToUpperInvariant(), Theme.FontSmall, Theme.Accent, bold: true);
            l.style.marginTop = Theme.Gutter;
            l.style.marginBottom = 12;
            l.style.letterSpacing = 2;
            return l;
        }

        public enum ButtonKind { Primary, Secondary, Ghost, Danger }

        /// <summary>Every button gets a real action; disabled buttons explain why via the label.</summary>
        public static Button Button(string text, Action onClick, ButtonKind kind = ButtonKind.Primary, bool enabled = true)
        {
            var b = new Button(() => { if (enabled) onClick?.Invoke(); }) { text = text };
            b.style.minHeight = Theme.TouchTarget;
            b.style.fontSize = Theme.FontBody;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            Theme.Round(b, Theme.Radius);
            Theme.Padding(b, 8, 32);
            Theme.Margin(b, 8, 0);
            b.style.whiteSpace = WhiteSpace.Normal;
            Color bg, fg, border;
            switch (kind)
            {
                case ButtonKind.Secondary: bg = Theme.SurfaceRaised; fg = Theme.Text; border = Theme.Border; break;
                case ButtonKind.Ghost: bg = Color.clear; fg = Theme.Accent; border = Theme.Accent; break;
                case ButtonKind.Danger: bg = Theme.Negative; fg = Color.white; border = Theme.Negative; break;
                default: bg = Theme.Accent; fg = Theme.Background; border = Theme.Accent; break;
            }
            if (!enabled)
            {
                bg = Theme.Surface;
                fg = Theme.TextMuted;
                border = Theme.Border;
            }
            b.style.backgroundColor = bg;
            b.style.color = fg;
            Theme.BorderColor(b, border, 3);
            b.SetEnabled(enabled);
            return b;
        }

        public static VisualElement Panel(params VisualElement[] children)
        {
            var e = Column(children);
            e.style.backgroundColor = Theme.Surface;
            Theme.Round(e, Theme.Radius);
            Theme.BorderColor(e, Theme.Border, 2);
            Theme.Padding(e, 28, 32);
            e.style.marginBottom = 20;
            return e;
        }

        /// <summary>A tappable list row with a title, subtitle and optional trailing text.</summary>
        public static VisualElement ListRow(string title, string subtitle, string trailing, Action onTap, Color? accent = null)
        {
            var row = Row();
            row.style.minHeight = Theme.TouchTarget;
            row.style.backgroundColor = Theme.Surface;
            Theme.Round(row, Theme.Radius);
            Theme.BorderColor(row, Theme.Border, 2);
            Theme.Padding(row, 20, 28);
            row.style.marginBottom = 14;
            if (accent.HasValue)
            {
                row.style.borderLeftColor = accent.Value;
                row.style.borderLeftWidth = 10;
            }
            var text = Column(Text(title, Theme.FontBody, bold: true), subtitle != null ? Muted(subtitle) : null);
            text.style.flexGrow = 1;
            text.style.flexShrink = 1;
            row.Add(text);
            if (trailing != null)
            {
                var t = Text(trailing, Theme.FontSmall, Theme.Accent, bold: true, wrap: false);
                t.style.marginLeft = 16;
                row.Add(t);
            }
            if (onTap != null) row.RegisterCallback<ClickEvent>(_ => onTap());
            return row;
        }

        public static VisualElement ProgressBar(double fraction, Color? fill = null, float height = 24)
        {
            var track = new VisualElement();
            track.style.height = height;
            track.style.backgroundColor = Theme.SurfaceRaised;
            Theme.Round(track, height / 2);
            track.style.overflow = Overflow.Hidden;
            var bar = new VisualElement();
            bar.style.height = Length.Percent(100);
            bar.style.width = Length.Percent((float)Math.Max(0, Math.Min(1, fraction)) * 100f);
            bar.style.backgroundColor = fill ?? Theme.Accent;
            Theme.Round(bar, height / 2);
            track.Add(bar);
            return track;
        }

        public static VisualElement Badge(string text, Color color)
        {
            var l = Text(text, 26, Theme.Background, bold: true, wrap: false);
            l.style.backgroundColor = color;
            Theme.Round(l, 14);
            Theme.Padding(l, 4, 14);
            l.style.marginRight = 10;
            l.style.alignSelf = Align.FlexStart;
            return l;
        }

        /// <summary>Horizontal segmented control. onSelect receives the chosen index.</summary>
        public static VisualElement Tabs(IReadOnlyList<string> labels, int selected, Action<int> onSelect)
        {
            var row = Row();
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 16;
            for (int i = 0; i < labels.Count; i++)
            {
                int index = i;
                var b = Button(labels[i], () => onSelect(index), i == selected ? ButtonKind.Primary : ButtonKind.Secondary);
                b.style.marginRight = 12;
                b.style.minHeight = 96;
                b.style.fontSize = Theme.FontSmall;
                Theme.Padding(b, 4, 24);
                row.Add(b);
            }
            return row;
        }

        public static ScrollView Scroll()
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.style.flexGrow = 1;
            s.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            s.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            return s;
        }

        public static VisualElement Grid(float itemWidthPercent)
        {
            var grid = Row();
            grid.style.flexWrap = Wrap.Wrap;
            grid.style.alignItems = Align.FlexStart;
            grid.userData = itemWidthPercent;
            return grid;
        }

        public static void AddToGrid(VisualElement grid, VisualElement item)
        {
            float pct = grid.userData is float f ? f : 33.3f;
            var cell = new VisualElement();
            cell.style.width = Length.Percent(pct);
            Theme.Padding(cell, 8, 8);
            cell.Add(item);
            grid.Add(cell);
        }
    }
}
