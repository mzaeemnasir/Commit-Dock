using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CommitDock.Views
{
    /// <summary>
    ///     The branch/tag label of a commit in the graph's label column. It shows the most relevant ref as a pill that is
    ///     cut short with an ellipsis when it does not fit, a "+N" badge for the commit's other refs, and a thin guide that
    ///     the graph continues up to the commit's node. Hidden or shortened names are listed in the tooltip.
    /// </summary>
    public class GraphRefsPresenter : Control
    {
        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            TextBlock.FontFamilyProperty.AddOwner<GraphRefsPresenter>();

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly StyledProperty<double> FontSizeProperty =
            TextBlock.FontSizeProperty.AddOwner<GraphRefsPresenter>();

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly StyledProperty<IBrush> BackgroundProperty =
            AvaloniaProperty.Register<GraphRefsPresenter, IBrush>(nameof(Background), Brushes.Transparent);

        public IBrush Background
        {
            get => GetValue(BackgroundProperty);
            set => SetValue(BackgroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<GraphRefsPresenter, IBrush>(nameof(Foreground), Brushes.White);

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> SecondaryForegroundProperty =
            AvaloniaProperty.Register<GraphRefsPresenter, IBrush>(nameof(SecondaryForeground), Brushes.Gray);

        public IBrush SecondaryForeground
        {
            get => GetValue(SecondaryForegroundProperty);
            set => SetValue(SecondaryForegroundProperty, value);
        }

        public static readonly StyledProperty<bool> UseGraphColorProperty =
            AvaloniaProperty.Register<GraphRefsPresenter, bool>(nameof(UseGraphColor));

        public bool UseGraphColor
        {
            get => GetValue(UseGraphColorProperty);
            set => SetValue(UseGraphColorProperty, value);
        }

        public static readonly StyledProperty<bool> UseCompactBranchNamesProperty =
            AvaloniaProperty.Register<GraphRefsPresenter, bool>(nameof(UseCompactBranchNames));

        public bool UseCompactBranchNames
        {
            get => GetValue(UseCompactBranchNamesProperty);
            set => SetValue(UseCompactBranchNamesProperty, value);
        }

        public static readonly StyledProperty<bool> HasSingleRemoteProperty =
            AvaloniaProperty.Register<GraphRefsPresenter, bool>(nameof(HasSingleRemote));

        public bool HasSingleRemote
        {
            get => GetValue(HasSingleRemoteProperty);
            set => SetValue(HasSingleRemoteProperty, value);
        }

        public static readonly StyledProperty<bool> ShowTagsProperty =
            AvaloniaProperty.Register<GraphRefsPresenter, bool>(nameof(ShowTags), true);

        public bool ShowTags
        {
            get => GetValue(ShowTagsProperty);
            set => SetValue(ShowTagsProperty, value);
        }

        /// <summary>
        ///     The ref whose pill is under the given point, or null.
        /// </summary>
        public Models.Decorator DecoratorAt(Point point)
        {
            return _labels.Count > 0 && _pill.Contains(point) ? _labels[0].Decorator : null;
        }

        public override void Render(DrawingContext context)
        {
            if (_labels.Count == 0 || DataContext is not Models.Commit commit)
                return;

            var label = _labels[0];
            var midY = Bounds.Height * 0.5;
            var color = GetBaseColor(label, commit);
            var isHead = IsHead(label.Decorator);
            var pill = new RoundedRect(_pill, PILL_RADIUS);
            IBrush fg;

            if (isHead)
            {
                context.DrawRectangle(new SolidColorBrush(color), null, pill);
                fg = GetContrastBrush(color);
            }
            else
            {
                var isDark = IsDark(Background);
                context.DrawRectangle(new SolidColorBrush(color, isDark ? 0.2 : 0.12), new Pen(new SolidColorBrush(color, 0.55)), pill.Deflate(0.5, 0.5));
                fg = Foreground;
            }

            var iconBrush = isHead ? fg : new SolidColorBrush(color);
            var x = _pill.X + PILL_PADDING;
            DrawIcon(context, CommitRefsIconCache.Instance.GetIcon(label.Decorator.Type), iconBrush, x, midY);

            x += ICON_SIZE + ICON_GAP;
            var text = CreateText(label.Decorator.Name, isHead, fg);
            if (_isNameTrimmed)
                text.MaxTextWidth = Math.Max(1, _textWidth);
            context.DrawText(text, new Point(x, midY - text.Height * 0.5));

            if (_showRemotes)
            {
                x += _textWidth;
                var remoteIcon = CommitRefsIconCache.Instance.GetIcon(Models.DecoratorType.RemoteBranchHead);
                if (HasSingleRemote)
                {
                    DrawIcon(context, remoteIcon, iconBrush, x + ICON_GAP, midY);
                }
                else
                {
                    foreach (var remote in label.Remotes)
                    {
                        x += ICON_GAP;
                        DrawIcon(context, remoteIcon, iconBrush, x, midY);
                        x += ICON_SIZE + 3;

                        var name = CreateText(remote, false, fg, FontStyle.Italic);
                        context.DrawText(name, new Point(x, midY - name.Height * 0.5));
                        x += name.WidthIncludingTrailingWhitespace;
                    }
                }
            }

            var end = _pill.Right;
            if (_badge.Width > 0)
            {
                context.DrawRectangle(new SolidColorBrush(GetColor(SecondaryForeground), 0.18), null, new RoundedRect(_badge, _badge.Height * 0.5));

                var badge = CreateText($"+{_labels.Count - 1}", false, SecondaryForeground, FontStyle.Normal, FontSize - 1.5, FontWeight.SemiBold);
                context.DrawText(badge, new Point(_badge.X + (_badge.Width - badge.Width) * 0.5, midY - badge.Height * 0.5));
                end = _badge.Right;
            }

            // Guide toward the commit, continued by the graph up to the node.
            if (end + GUIDE_GAP < Bounds.Width)
            {
                var pen = CommitGraph.GetRefConnectorPen(commit.Color, UseGraphColor);
                context.DrawLine(pen, new Point(end + GUIDE_GAP, midY + 0.5), new Point(Bounds.Width, midY + 0.5));
            }
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == FontFamilyProperty ||
                change.Property == FontSizeProperty ||
                change.Property == UseCompactBranchNamesProperty ||
                change.Property == HasSingleRemoteProperty ||
                change.Property == ShowTagsProperty)
            {
                CollectLabels();
                InvalidateArrange();
                InvalidateVisual();
            }
            else if (change.Property == BackgroundProperty ||
                     change.Property == ForegroundProperty ||
                     change.Property == SecondaryForegroundProperty ||
                     change.Property == UseGraphColorProperty)
            {
                InvalidateVisual();
            }
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            CollectLabels();
            InvalidateArrange();
            InvalidateVisual();
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            return new Size(0, PILL_HEIGHT);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            LayoutLabels(finalSize.Width, finalSize.Height);
            return finalSize;
        }

        private void CollectLabels()
        {
            _labels.Clear();

            if (DataContext is not Models.Commit commit)
                return;

            var refs = commit.Decorators;
            var showTags = ShowTags;
            var useCompactBranchNames = UseCompactBranchNames;
            var merged = new HashSet<int>();

            for (var i = 0; i < refs.Count; i++)
            {
                if (merged.Contains(i))
                    continue;

                var decorator = refs[i];
                if (!showTags && decorator.Type == Models.DecoratorType.Tag)
                    continue;

                var label = new Label() { Decorator = decorator };
                _labels.Add(label);

                // A local branch and its remote counterpart share one label.
                if (!useCompactBranchNames || decorator.Type is not (Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.LocalBranchHead))
                    continue;

                for (var j = i + 1; j < refs.Count; j++)
                {
                    var test = refs[j];
                    if (test.Type != Models.DecoratorType.RemoteBranchHead)
                        continue;

                    var idx = test.Name.IndexOf('/');
                    if (idx < 1 || idx == test.Name.Length - 1)
                        continue;

                    if (decorator.Name.Equals(test.Name.Substring(idx + 1), StringComparison.Ordinal))
                    {
                        label.Remotes.Add(test.Name.Substring(0, idx));
                        merged.Add(j);
                    }
                }
            }
        }

        private void LayoutLabels(double width, double height)
        {
            _pill = default;
            _badge = default;
            _textWidth = 0;
            _isNameTrimmed = false;
            _showRemotes = false;

            var isTrimmed = false;
            if (_labels.Count > 0)
            {
                var label = _labels[0];
                var midY = height * 0.5;
                var available = width - PADDING_LEFT - MIN_GUIDE;

                var badgeWidth = 0.0;
                if (_labels.Count > 1)
                {
                    var badge = CreateText($"+{_labels.Count - 1}", false, SecondaryForeground, FontStyle.Normal, FontSize - 1.5, FontWeight.SemiBold);
                    badgeWidth = badge.Width + 10;
                }

                var remotesWidth = 0.0;
                if (label.Remotes.Count > 0)
                {
                    if (HasSingleRemote)
                    {
                        remotesWidth = ICON_GAP + ICON_SIZE;
                    }
                    else
                    {
                        foreach (var remote in label.Remotes)
                            remotesWidth += ICON_GAP + ICON_SIZE + 3 + CreateText(remote, false, Foreground, FontStyle.Italic).WidthIncludingTrailingWhitespace;
                    }
                }

                var chrome = PILL_PADDING * 2 + ICON_SIZE + ICON_GAP;
                var textWidth = CreateText(label.Decorator.Name, IsHead(label.Decorator), Foreground).WidthIncludingTrailingWhitespace;
                var badgeSpace = badgeWidth > 0 ? BADGE_GAP + badgeWidth : 0;

                // Give up the remote markers first, then the badge, before the name gets too short to read.
                var showRemotes = remotesWidth > 0;
                var room = available - chrome - badgeSpace - (showRemotes ? remotesWidth : 0);
                if (room < textWidth && showRemotes && room < MIN_TEXT)
                {
                    showRemotes = false;
                    room = available - chrome - badgeSpace;
                }

                if (room < textWidth && badgeSpace > 0 && room < MIN_TEXT)
                {
                    badgeSpace = 0;
                    room = available - chrome - (showRemotes ? remotesWidth : 0);
                }

                _isNameTrimmed = room < textWidth;
                isTrimmed = _isNameTrimmed || (remotesWidth > 0 && !showRemotes) || (badgeWidth > 0 && badgeSpace == 0);
                _textWidth = Math.Max(0, Math.Min(textWidth, room));
                _showRemotes = showRemotes;

                var pillWidth = chrome + _textWidth + (showRemotes ? remotesWidth : 0);
                _pill = new Rect(PADDING_LEFT, midY - PILL_HEIGHT * 0.5, pillWidth, PILL_HEIGHT);

                if (badgeSpace > 0)
                    _badge = new Rect(_pill.Right + BADGE_GAP, midY - BADGE_HEIGHT * 0.5, badgeWidth, BADGE_HEIGHT);
            }

            // Explain shortened or hidden refs on hover.
            ToolTip.SetTip(this, isTrimmed || _labels.Count > 1 ? DescribeLabels() : null);
        }

        private string DescribeLabels()
        {
            var builder = new StringBuilder();
            foreach (var label in _labels)
            {
                if (builder.Length > 0)
                    builder.AppendLine();

                builder.Append(label.Decorator.Name);
                foreach (var remote in label.Remotes)
                    builder.AppendLine().Append(remote).Append('/').Append(label.Decorator.Name);
            }

            return builder.ToString();
        }

        private FormattedText CreateText(string text, bool bold, IBrush brush, FontStyle style = FontStyle.Normal, double size = 0, FontWeight weight = FontWeight.Normal)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(FontFamily, style, bold ? FontWeight.Bold : weight),
                size > 0 ? size : FontSize,
                brush);

            formatted.MaxLineCount = 1;
            formatted.Trimming = TextTrimming.CharacterEllipsis;
            return formatted;
        }

        private Color GetBaseColor(Label label, Models.Commit commit)
        {
            if (label.Decorator.Type == Models.DecoratorType.Tag)
                return IsDark(Background) ? TAG_COLOR_DARK : TAG_COLOR_LIGHT;

            return UseGraphColor ? Models.CommitGraph.GetColor(commit.Color) : GetColor(SecondaryForeground);
        }

        private static void DrawIcon(DrawingContext context, Geometry icon, IBrush brush, double x, double midY)
        {
            if (icon == null)
                return;

            using (context.PushTransform(Matrix.CreateTranslation(x, midY - ICON_SIZE * 0.5)))
                context.DrawGeometry(brush, null, icon);
        }

        private static bool IsHead(Models.Decorator decorator)
        {
            return decorator.Type is Models.DecoratorType.CurrentBranchHead or Models.DecoratorType.CurrentCommitHead;
        }

        private static Color GetColor(IBrush brush)
        {
            return brush is ISolidColorBrush solid ? solid.Color : Colors.Gray;
        }

        private static bool IsDark(IBrush background)
        {
            var c = GetColor(background);
            return c.R * 0.299 + c.G * 0.587 + c.B * 0.114 < 128;
        }

        private static IBrush GetContrastBrush(Color fill)
        {
            // White text unless it would be too faint on a light lane color.
            return Contrast(fill, Colors.White) >= 3.0 ? Brushes.White : DARK_TEXT;
        }

        private static double Contrast(Color a, Color b)
        {
            var la = Luminance(a);
            var lb = Luminance(b);
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        private static double Luminance(Color c)
        {
            static double Channel(byte v)
            {
                var s = v / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
        }

        private class Label
        {
            public Models.Decorator Decorator { get; set; }
            public List<string> Remotes { get; } = [];
        }

        private const double PILL_HEIGHT = 20;
        private const double PILL_RADIUS = 4;
        private const double PILL_PADDING = 6;
        private const double PADDING_LEFT = 6;
        private const double ICON_SIZE = 10;
        private const double ICON_GAP = 5;
        private const double BADGE_HEIGHT = 16;
        private const double BADGE_GAP = 4;
        private const double GUIDE_GAP = 3;
        private const double MIN_GUIDE = 10;
        private const double MIN_TEXT = 36;

        private static readonly Color TAG_COLOR_DARK = Color.FromRgb(0xD4, 0xA2, 0x3A);
        private static readonly Color TAG_COLOR_LIGHT = Color.FromRgb(0xA6, 0x76, 0x12);
        private static readonly IBrush DARK_TEXT = new SolidColorBrush(Color.FromRgb(0x14, 0x13, 0x1C));

        private readonly List<Label> _labels = [];
        private Rect _pill;
        private Rect _badge;
        private double _textWidth;
        private bool _isNameTrimmed;
        private bool _showRemotes;
    }
}
