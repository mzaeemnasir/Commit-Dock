using System;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CommitDock.Views
{
    /// <summary>
    ///     Draws a repository path as a dimmed folder part followed by the file name. When the path does not fit,
    ///     whole folders are dropped from the front ("…/Prefabs/Scene.prefab"), so the file name always stays visible.
    ///     (Avalonia's own leading-ellipsis trimming cannot be combined with differently colored runs.)
    /// </summary>
    public class ChangePathPresenter : Control
    {
        public static readonly StyledProperty<FontFamily> FontFamilyProperty =
            AvaloniaProperty.Register<ChangePathPresenter, FontFamily>(nameof(FontFamily));

        public FontFamily FontFamily
        {
            get => GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }

        public static readonly StyledProperty<double> FontSizeProperty =
            AvaloniaProperty.Register<ChangePathPresenter, double>(nameof(FontSize), 13);

        public double FontSize
        {
            get => GetValue(FontSizeProperty);
            set => SetValue(FontSizeProperty, value);
        }

        public static readonly StyledProperty<IBrush> ForegroundProperty =
            AvaloniaProperty.Register<ChangePathPresenter, IBrush>(nameof(Foreground), Brushes.White);

        public IBrush Foreground
        {
            get => GetValue(ForegroundProperty);
            set => SetValue(ForegroundProperty, value);
        }

        public static readonly StyledProperty<IBrush> DirectoryForegroundProperty =
            AvaloniaProperty.Register<ChangePathPresenter, IBrush>(nameof(DirectoryForeground), Brushes.Gray);

        public IBrush DirectoryForeground
        {
            get => GetValue(DirectoryForegroundProperty);
            set => SetValue(DirectoryForegroundProperty, value);
        }

        public static readonly DirectProperty<ChangePathPresenter, string> PathProperty =
            AvaloniaProperty.RegisterDirect<ChangePathPresenter, string>(
                nameof(Path),
                static o => o.Path,
                static (o, v) => o.Path = v);

        public string Path
        {
            get => _path;
            set => SetAndRaise(PathProperty, ref _path, value);
        }

        public ChangePathPresenter()
        {
            ClipToBounds = true;
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            var h = Bounds.Height;
            var x = 0.0;

            if (_directory != null)
            {
                context.DrawText(_directory, new Point(0, (h - _directory.Height) * 0.5));
                x = _directory.WidthIncludingTrailingWhitespace;
            }

            if (_file != null)
                context.DrawText(_file, new Point(x, (h - _file.Height) * 0.5));
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == PathProperty ||
                change.Property == FontFamilyProperty ||
                change.Property == FontSizeProperty ||
                change.Property == ForegroundProperty ||
                change.Property == DirectoryForegroundProperty)
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            Split(out var directory, out var file);

            var fileText = Format(file, Foreground);
            var width = fileText.WidthIncludingTrailingWhitespace;
            var height = Math.Max(18, fileText.Height);

            if (directory.Length > 0)
                width += Format(directory, DirectoryForeground).WidthIncludingTrailingWhitespace;

            if (!double.IsInfinity(availableSize.Width))
                width = Math.Min(width, availableSize.Width);

            return new Size(width, height);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Split(out var directory, out var file);

            _file = Format(file, Foreground);
            _directory = null;

            if (directory.Length > 0)
            {
                var room = finalSize.Width - _file.WidthIncludingTrailingWhitespace;
                var candidate = Format(directory, DirectoryForeground);

                if (candidate.WidthIncludingTrailingWhitespace > room)
                {
                    // Drop folders from the front until the rest fits.
                    var segments = directory.TrimEnd('/').Split('/');
                    candidate = null;

                    for (var i = 1; i < segments.Length; i++)
                    {
                        var shorter = Format("…/" + string.Join('/', segments, i, segments.Length - i) + "/", DirectoryForeground);
                        if (shorter.WidthIncludingTrailingWhitespace <= room)
                        {
                            candidate = shorter;
                            break;
                        }
                    }

                    if (candidate == null)
                    {
                        var ellipsis = Format("…/", DirectoryForeground);
                        if (ellipsis.WidthIncludingTrailingWhitespace <= room)
                            candidate = ellipsis;
                    }
                }

                _directory = candidate;
            }

            InvalidateVisual();
            return finalSize;
        }

        private void Split(out string directory, out string file)
        {
            var path = _path ?? string.Empty;
            var idx = path.LastIndexOf('/');
            if (idx < 0)
            {
                directory = string.Empty;
                file = path;
            }
            else
            {
                directory = path.Substring(0, idx + 1);
                file = path.Substring(idx + 1);
            }
        }

        private FormattedText Format(string text, IBrush brush)
        {
            return new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface(FontFamily), FontSize, brush);
        }

        private string _path = string.Empty;
        private FormattedText _directory = null;
        private FormattedText _file = null;
    }
}
