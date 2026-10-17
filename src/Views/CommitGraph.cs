using System;
using System.Collections.Generic;
using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CommitDock.Views
{
    public class CommitGraph : Control, Models.IAvatarHost
    {
        public static readonly DirectProperty<CommitGraph, Models.CommitGraph> GraphProperty =
            AvaloniaProperty.RegisterDirect<CommitGraph, Models.CommitGraph>(
                nameof(Graph),
                static o => o.Graph,
                static (o, v) => o.Graph = v);

        public Models.CommitGraph Graph
        {
            get => _graph;
            set => SetAndRaise(GraphProperty, ref _graph, value);
        }

        public static readonly DirectProperty<CommitGraph, Models.CommitGraphLayout> LayoutProperty =
            AvaloniaProperty.RegisterDirect<CommitGraph, Models.CommitGraphLayout>(
                nameof(Layout),
                static o => o.Layout,
                static (o, v) => o.Layout = v);

        public Models.CommitGraphLayout Layout
        {
            get => _layout;
            set => SetAndRaise(LayoutProperty, ref _layout, value);
        }

        public static readonly StyledProperty<IBrush> DotBrushProperty =
            AvaloniaProperty.Register<CommitGraph, IBrush>(nameof(DotBrush), Brushes.Transparent);

        public IBrush DotBrush
        {
            get => GetValue(DotBrushProperty);
            set => SetValue(DotBrushProperty, value);
        }

        public bool ShowRefConnectors
        {
            get => _showRefConnectors;
            set
            {
                if (_showRefConnectors == value)
                    return;
                _showRefConnectors = value;
                InvalidateVisual();
            }
        }

        public CommitGraph()
        {
            RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.HighQuality);
        }

        public override void Render(DrawingContext context)
        {
            base.Render(context);

            if (_graph == null || _layout == null || Models.CommitGraph.Pens.Count == 0)
                return;

            var startY = _layout.StartY;
            var clipWidth = _layout.ClipWidth;
            var inset = _layout.TopInset;
            var clipHeight = Bounds.Height - inset;
            var rowHeight = _layout.RowHeight;
            var endY = startY + clipHeight + 28;

            using (context.PushClip(new Rect(_layout.StartX, inset, clipWidth, clipHeight)))
            using (context.PushTransform(Matrix.CreateTranslation(_layout.StartX, inset - startY)))
            {
                DrawTrails(context, _graph, startY, endY, rowHeight, clipWidth);
                DrawCurves(context, _graph, startY, endY, rowHeight);
                DrawWipConnector(context, _graph, startY, endY, rowHeight, inset);
                DrawAnchors(context, _graph, startY, endY, rowHeight);
            }

            if (inset > 0)
            {
                using (context.PushClip(new Rect(_layout.StartX, 0, clipWidth, inset)))
                using (context.PushTransform(Matrix.CreateTranslation(_layout.StartX, 0)))
                    DrawWipNode(context, _graph, inset);
            }
        }

        public void OnAvatarResourceChanged(string email, Bitmap image)
        {
            if (_avatars.ContainsKey(email))
            {
                _avatars[email] = image;
                InvalidateVisual();
            }
        }

        protected override void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            Models.AvatarManager.Instance.Subscribe(this);
        }

        protected override void OnUnloaded(RoutedEventArgs e)
        {
            base.OnUnloaded(e);
            Models.AvatarManager.Instance.Unsubscribe(this);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == GraphProperty ||
                change.Property == LayoutProperty ||
                change.Property == DotBrushProperty)
                InvalidateVisual();
        }

        private void DrawTrails(DrawingContext context, Models.CommitGraph graph, double top, double bottom, double rowHeight, double width)
        {
            // A soft band in the lane color runs from each commit to the edge of the graph column.
            foreach (var dot in graph.Dots)
            {
                var y = dot.Center.Y * rowHeight;
                if (y < top - rowHeight)
                    continue;
                if (y > bottom)
                    break;

                var color = ((ISolidColorBrush)Models.CommitGraph.Pens[dot.Color].Brush).Color;
                var brush = new SolidColorBrush(color, dot.IsHighlighted ? 0.13 : 0.05);
                context.FillRectangle(brush, new Rect(dot.Center.X, y - rowHeight / 2 + 2, Math.Max(0, width - dot.Center.X), rowHeight - 4));
            }
        }

        private static Models.CommitGraph.Dot FindHead(Models.CommitGraph graph)
        {
            foreach (var dot in graph.Dots)
            {
                if (dot.Type == Models.CommitGraph.DotType.Head)
                    return dot;
            }

            return null;
        }

        private static double GetWipX(Models.CommitGraph graph)
        {
            return FindHead(graph)?.Center.X ?? Models.CommitGraph.LaneWidth / 2 + 8;
        }

        private static IBrush GetWipBrush(Models.CommitGraph graph)
        {
            var head = FindHead(graph);
            return Models.CommitGraph.Pens[head?.Color ?? 0].Brush;
        }

        private void DrawWipNode(DrawingContext context, Models.CommitGraph graph, double inset)
        {
            // Uncommitted work: a dashed, hollow node sitting above the current HEAD's lane.
            var center = new Point(GetWipX(graph), inset / 2);
            var brush = GetWipBrush(graph);
            context.DrawEllipse(DotBrush, new Pen(brush, 2, new DashStyle([2, 1.5], 0)), center, AVATAR_RADIUS - 1, AVATAR_RADIUS - 1);
            context.DrawLine(new Pen(brush, 2, new DashStyle([0.1, 2.2], 0), PenLineCap.Round), new Point(center.X, center.Y + AVATAR_RADIUS + 1), new Point(center.X, inset + 2));
        }

        private void DrawWipConnector(DrawingContext context, Models.CommitGraph graph, double top, double bottom, double rowHeight, double inset)
        {
            if (inset <= 0)
                return;

            var head = FindHead(graph);
            if (head == null)
                return;

            // Dotted line from the top of the list down to the HEAD commit, while HEAD is below the top edge.
            var headY = head.Center.Y * rowHeight;
            if (headY <= top)
                return;

            var pen = new Pen(Models.CommitGraph.Pens[head.Color].Brush, 2, new DashStyle([0.1, 2.2], 0), PenLineCap.Round);
            context.DrawLine(pen, new Point(head.Center.X, top - 2), new Point(head.Center.X, Math.Min(headY, bottom)));
        }

        private void DrawCurves(DrawingContext context, Models.CommitGraph graph, double top, double bottom, double rowHeight)
        {
            var dimmed = new List<(StreamGeometry, int)>();
            var highlighted = new List<(StreamGeometry, int)>();

            foreach (var link in graph.Links)
            {
                var startY = link.Start.Y * rowHeight;
                var endY = link.End.Y * rowHeight;

                if (endY < top)
                    continue;
                if (startY > bottom)
                    break;

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(link.Start.X, startY), false);
                    ctx.QuadraticBezierTo(new Point(link.Control.X, link.Control.Y * rowHeight), new Point(link.End.X, endY));
                }

                (link.IsHighlighted ? highlighted : dimmed).Add((geo, link.Color));
            }

            foreach (var line in graph.Paths)
            {
                var last = new Point(line.Points[0].X, line.Points[0].Y * rowHeight);
                var size = line.Points.Count;
                var endY = line.Points[size - 1].Y * rowHeight;

                if (endY < top)
                    continue;
                if (last.Y > bottom)
                    break;

                var geo = new StreamGeometry();
                using (var ctx = geo.Open())
                {
                    var started = false;
                    var ended = false;
                    for (int i = 1; i < size; i++)
                    {
                        var cur = new Point(line.Points[i].X, line.Points[i].Y * rowHeight);
                        if (cur.Y < top)
                        {
                            last = cur;
                            continue;
                        }

                        if (!started)
                        {
                            ctx.BeginFigure(last, false);
                            started = true;
                        }

                        if (cur.Y > bottom)
                        {
                            cur = new Point(cur.X, bottom);
                            ended = true;
                        }

                        // Lane changes are drawn as smooth S-curves with vertical tangents at both ends.
                        if (cur.X != last.X)
                        {
                            var midY = (last.Y + cur.Y) / 2;
                            ctx.CubicBezierTo(new Point(last.X, midY), new Point(cur.X, midY), cur);
                        }
                        else
                        {
                            ctx.LineTo(cur);
                        }

                        if (ended)
                            break;
                        last = cur;
                    }
                }

                (line.IsHighlighted ? highlighted : dimmed).Add((geo, line.Color));
            }

            foreach (var (geo, color) in dimmed)
                context.DrawGeometry(null, Models.CommitGraph.DimPens[color], geo);

            foreach (var (geo, color) in highlighted)
                context.DrawGeometry(null, Models.CommitGraph.Pens[color], geo);
        }

        private void DrawAnchors(DrawingContext context, Models.CommitGraph graph, double top, double bottom, double rowHeight)
        {
            var halo = DotBrush;
            var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.Bold);

            foreach (var dot in graph.Dots)
            {
                var center = new Point(dot.Center.X, dot.Center.Y * rowHeight);

                if (center.Y < top)
                    continue;
                if (center.Y > bottom)
                    break;

                var pen = dot.IsHighlighted ? Models.CommitGraph.Pens[dot.Color] : Models.CommitGraph.DimPens[dot.Color];

                // Connect the branch/tag labels (drawn in the column on the left) to this commit.
                if (dot.HasRefs && _showRefConnectors)
                    context.DrawLine(new Pen(pen.Brush, 2), new Point(-4, center.Y), center);

                if (dot.Type == Models.CommitGraph.DotType.Merge)
                {
                    context.DrawEllipse(halo, null, center, 8, 8);
                    context.DrawEllipse(pen.Brush, null, center, 6, 6);
                    continue;
                }

                var isHead = dot.Type == Models.CommitGraph.DotType.Head;
                if (isHead && dot.IsHighlighted)
                    context.DrawEllipse(Models.CommitGraph.GlowPens[dot.Color].Brush, null, center, AVATAR_RADIUS + 6, AVATAR_RADIUS + 6);

                context.DrawEllipse(halo, null, center, AVATAR_RADIUS + 2, AVATAR_RADIUS + 2);

                using (context.PushOpacity(dot.IsHighlighted ? 1.0 : 0.4))
                    DrawAvatar(context, dot, center, typeface);

                context.DrawEllipse(null, new Pen(Models.CommitGraph.Pens[dot.Color].Brush, isHead ? 1.75 : 1.25), center, AVATAR_RADIUS, AVATAR_RADIUS);
            }
        }

        private void DrawAvatar(DrawingContext context, Models.CommitGraph.Dot dot, Point center, Typeface typeface)
        {
            var rect = new Rect(center.X - AVATAR_RADIUS, center.Y - AVATAR_RADIUS, AVATAR_RADIUS * 2, AVATAR_RADIUS * 2);
            var image = GetAvatar(dot.Author);
            if (image != null)
            {
                using (context.PushGeometryClip(new EllipseGeometry(rect)))
                    context.DrawImage(image, rect);
                return;
            }

            // No picture (yet): show the author's initials on a disc tinted with the lane color.
            var color = ((ISolidColorBrush)Models.CommitGraph.Pens[dot.Color].Brush).Color;
            var bg = new SolidColorBrush(Color.FromArgb(255, (byte)(color.R * 0.32), (byte)(color.G * 0.32), (byte)(color.B * 0.32)));
            context.DrawEllipse(bg, null, center, AVATAR_RADIUS, AVATAR_RADIUS);

            var initials = new FormattedText(
                GetInitials(dot.Author?.Name),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                8,
                new SolidColorBrush(color));
            context.DrawText(initials, new Point(center.X - initials.Width / 2, center.Y - initials.Height / 2));
        }

        private Bitmap GetAvatar(Models.User user)
        {
            if (user == null || string.IsNullOrEmpty(user.Email))
                return null;

            if (!_avatars.TryGetValue(user.Email, out var image))
            {
                image = Models.AvatarManager.Instance.Request(user.Email, false);
                _avatars[user.Email] = image;
            }

            return image;
        }

        private static string GetInitials(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "?";

            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && char.IsLetterOrDigit(parts[0][0]) && char.IsLetterOrDigit(parts[^1][0]))
                return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}";

            return char.ToUpperInvariant(name.Trim()[0]).ToString();
        }

        private const double AVATAR_RADIUS = 9.5;

        private Models.CommitGraph _graph = null;
        private Models.CommitGraphLayout _layout = null;
        private bool _showRefConnectors = true;
        private readonly Dictionary<string, Bitmap> _avatars = new Dictionary<string, Bitmap>();
    }
}
