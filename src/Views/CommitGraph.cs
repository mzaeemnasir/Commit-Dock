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

        public static readonly DirectProperty<CommitGraph, List<Models.Commit>> SelectedCommitsProperty =
            AvaloniaProperty.RegisterDirect<CommitGraph, List<Models.Commit>>(
                nameof(SelectedCommits),
                static o => o.SelectedCommits,
                static (o, v) => o.SelectedCommits = v);

        public List<Models.Commit> SelectedCommits
        {
            get => _selectedCommits;
            set => SetAndRaise(SelectedCommitsProperty, ref _selectedCommits, value);
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

            var top = _layout.StartY;
            var clipWidth = _layout.ClipWidth;
            var inset = _layout.TopInset;
            var clipHeight = Bounds.Height - inset;
            var rowHeight = _layout.RowHeight;
            var bottom = top + clipHeight + rowHeight;

            using (context.PushClip(new Rect(_layout.StartX, inset, clipWidth, clipHeight)))
            using (context.PushTransform(Matrix.CreateTranslation(_layout.StartX, inset - top)))
            {
                DrawBands(context, top, bottom, rowHeight, clipWidth);

                if (_showRefConnectors)
                    DrawRefConnectors(context, top, bottom, rowHeight);

                DrawLanes(context, top, bottom, rowHeight);
                DrawNodes(context, top, bottom, rowHeight);
            }

            if (inset > 0)
            {
                using (context.PushClip(new Rect(_layout.StartX, 0, clipWidth, inset)))
                using (context.PushTransform(Matrix.CreateTranslation(_layout.StartX, 0)))
                    DrawWipNode(context, inset);
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

            if (change.Property == SelectedCommitsProperty)
            {
                _selected.Clear();
                if (_selectedCommits != null)
                {
                    foreach (var c in _selectedCommits)
                        _selected.Add(c.SHA);
                }

                InvalidateVisual();
            }
            else if (change.Property == GraphProperty ||
                     change.Property == LayoutProperty ||
                     change.Property == DotBrushProperty)
            {
                InvalidateVisual();
            }
        }

        private void DrawBands(DrawingContext context, double top, double bottom, double rowHeight, double width)
        {
            // A soft band in the lane color runs from each commit to the edge of the graph column, and lights up
            // when the commit is selected.
            foreach (var dot in _graph.Dots)
            {
                var y = dot.Center.Y * rowHeight;
                if (y < top - rowHeight)
                    continue;
                if (y > bottom)
                    break;

                var opacity = _selected.Contains(dot.SHA) ? 0.34 : (dot.IsHighlighted ? 0.13 : 0.05);
                var brush = new SolidColorBrush(Models.CommitGraph.GetColor(dot.Color), opacity);
                context.FillRectangle(brush, new Rect(dot.Center.X, y - rowHeight / 2 + 2, Math.Max(0, width - dot.Center.X), rowHeight - 4));
            }
        }

        private void DrawRefConnectors(DrawingContext context, double top, double bottom, double rowHeight)
        {
            // Thin guides from the branch/tag labels (in the column on the left) to their commits. They are drawn
            // under the lanes so they never read as part of the history.
            foreach (var dot in _graph.Dots)
            {
                var y = dot.Center.Y * rowHeight;
                if (y < top - rowHeight)
                    continue;
                if (y > bottom)
                    break;

                if (!dot.HasRefs)
                    continue;

                var radius = dot.Type == Models.CommitGraph.DotType.Merge ? MERGE_RADIUS : AVATAR_RADIUS;
                var pen = GetRefConnectorPen(dot.Color, dot.IsHighlighted);
                context.DrawLine(pen, new Point(0, y + 0.5), new Point(dot.Center.X - radius, y + 0.5));
            }
        }

        private void DrawLanes(DrawingContext context, double top, double bottom, double rowHeight)
        {
            var dimmed = new List<(StreamGeometry, int)>();
            var highlighted = new List<(StreamGeometry, int)>();
            var dimmedJunctions = new List<(StreamGeometry, int, double)>();
            var highlightedJunctions = new List<(StreamGeometry, int, double)>();
            StreamGeometry wip = null;

            foreach (var link in _graph.Links)
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
                    var start = new Point(link.Start.X, startY);
                    ctx.BeginFigure(start, false);
                    MergeOut(ctx, start, new Point(link.End.X, endY), rowHeight, IsCrossingForks(link.Start, link.End));
                    ctx.EndFigure(false);
                }

                (link.IsHighlighted ? highlighted : dimmed).Add((geo, link.Color));
            }

            foreach (var path in _graph.Paths)
            {
                var points = path.Points;
                var startY = points[0].Y * rowHeight;
                var endY = points[^1].Y * rowHeight;

                // Paths are created row by row, so once one starts below the visible area all others do too.
                if (startY > bottom + rowHeight)
                    break;
                if (endY < top || points.Count < 2)
                    continue;

                var geo = new StreamGeometry();
                var hasSegments = false;
                using (var ctx = geo.Open())
                {
                    var cursor = new Point(points[0].X, startY);
                    ctx.BeginFigure(cursor, false);

                    for (var i = 1; i < points.Count; i++)
                    {
                        var from = points[i - 1];
                        var to = new Point(points[i].X, points[i].Y * rowHeight);

                        if (Math.Abs(points[i].X - from.X) < 0.01)
                        {
                            if (to.Y > cursor.Y)
                            {
                                ctx.LineTo(to);
                                cursor = to;
                                hasSegments = true;
                            }
                        }
                        else if (IsRowCenter(from.Y))
                        {
                            // Out of a merge commit, into the merged branch's lane.
                            cursor = MergeOut(ctx, cursor, to, rowHeight, IsCrossingForks(from, points[i]));
                            hasSegments = true;
                        }
                        else
                        {
                            // Down a branch's lane and into the side of the commit it started from. Drawn on its own, so
                            // where several branches start from one commit the nearer ones can be layered on top.
                            var junction = new StreamGeometry();
                            using (var jctx = junction.Open())
                            {
                                jctx.BeginFigure(cursor, false);
                                DownThenHorizontal(jctx, cursor, to);
                                jctx.EndFigure(false);
                            }

                            (path.IsHighlighted ? highlightedJunctions : dimmedJunctions).Add((junction, path.Color, Math.Abs(to.X - cursor.X)));
                            cursor = to;
                        }
                    }

                    ctx.EndFigure(false);
                }

                if (!hasSegments)
                    continue;

                if (path.IsWip)
                    wip = geo;
                else
                    (path.IsHighlighted ? highlighted : dimmed).Add((geo, path.Color));
            }

            dimmedJunctions.Sort((l, r) => r.Item3.CompareTo(l.Item3));
            highlightedJunctions.Sort((l, r) => r.Item3.CompareTo(l.Item3));

            foreach (var (geo, color) in dimmed)
                context.DrawGeometry(null, Models.CommitGraph.GetPen(color, false), geo);

            foreach (var (geo, color, _) in dimmedJunctions)
                context.DrawGeometry(null, Models.CommitGraph.GetPen(color, false), geo);

            foreach (var (geo, color) in highlighted)
                context.DrawGeometry(null, Models.CommitGraph.GetPen(color), geo);

            foreach (var (geo, color, _) in highlightedJunctions)
                context.DrawGeometry(null, Models.CommitGraph.GetPen(color), geo);

            if (wip != null)
                context.DrawGeometry(null, GetWipPen(), wip);
        }

        private void DrawNodes(DrawingContext context, double top, double bottom, double rowHeight)
        {
            var background = DotBrush;
            var isDark = IsDark(background);
            var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyle.Normal, FontWeight.Bold);

            foreach (var dot in _graph.Dots)
            {
                var center = new Point(dot.Center.X, dot.Center.Y * rowHeight);
                if (center.Y < top - rowHeight)
                    continue;
                if (center.Y > bottom)
                    break;

                var pen = Models.CommitGraph.GetPen(dot.Color, dot.IsHighlighted);
                var isMerge = dot.Type == Models.CommitGraph.DotType.Merge;
                var radius = isMerge ? MERGE_RADIUS : AVATAR_RADIUS;

                // HEAD gets a soft halo in its lane color.
                if (dot.IsHead && dot.IsHighlighted)
                    context.DrawEllipse(Models.CommitGraph.GlowPens[dot.Color % Models.CommitGraph.GlowPens.Count].Brush, null, center, radius + 5, radius + 5);

                // An opaque base keeps lanes from showing through dimmed or transparent nodes.
                context.DrawEllipse(background, null, center, radius, radius);

                if (isMerge)
                {
                    context.DrawEllipse(pen.Brush, null, center, radius, radius);
                    continue;
                }

                using (context.PushOpacity(dot.IsHighlighted ? 1.0 : 0.4))
                    DrawAvatar(context, dot, center, typeface, isDark);

                context.DrawEllipse(null, new Pen(pen.Brush, dot.IsHead ? 2.5 : 2), center, AVATAR_RADIUS, AVATAR_RADIUS);
            }
        }

        private void DrawAvatar(DrawingContext context, Models.CommitGraph.Dot dot, Point center, Typeface typeface, bool isDark)
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
            var color = Models.CommitGraph.GetColor(dot.Color);
            context.DrawEllipse(new SolidColorBrush(color, isDark ? 0.3 : 0.16), null, center, AVATAR_RADIUS, AVATAR_RADIUS);

            var initials = new FormattedText(
                GetInitials(dot.Author?.Name),
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                8.5,
                isDark ? INITIALS_ON_DARK : new SolidColorBrush(color));
            context.DrawText(initials, new Point(center.X - initials.Width / 2, center.Y - initials.Height / 2));
        }

        private void DrawWipNode(DrawingContext context, double inset)
        {
            // Uncommitted work: a dashed, hollow node above HEAD, whose dotted lane continues in the rows below.
            var center = new Point(Models.CommitGraph.GetLaneX(0), inset / 2);
            var brush = Models.CommitGraph.GetPen(0).Brush;
            context.DrawEllipse(DotBrush, new Pen(brush, 2, new DashStyle([2, 1.5], 0)), center, AVATAR_RADIUS - 1, AVATAR_RADIUS - 1);

            if (_graph.Paths.Count > 0 && _graph.Paths[0].IsWip)
                context.DrawLine(GetWipPen(), new Point(center.X, center.Y + AVATAR_RADIUS + 1), new Point(center.X, inset + 2));
        }

        private static void HorizontalThenDown(StreamGeometryContext ctx, Point from, Point to)
        {
            var dir = Math.Sign(to.X - from.X);
            var r = Math.Min(CORNER_RADIUS, Math.Min(Math.Abs(to.X - from.X), to.Y - from.Y));
            var c0 = new Point(to.X - dir * r, from.Y);
            var c1 = new Point(to.X, from.Y + r);

            ctx.LineTo(c0);
            ctx.CubicBezierTo(new Point(c0.X + dir * r * KAPPA, c0.Y), new Point(c1.X, c1.Y - r * KAPPA), c1);
            ctx.LineTo(to);
        }

        private static void DownThenHorizontal(StreamGeometryContext ctx, Point from, Point to)
        {
            var dir = Math.Sign(to.X - from.X);
            var r = Math.Min(CORNER_RADIUS, Math.Min(Math.Abs(to.X - from.X), to.Y - from.Y));
            var c0 = new Point(from.X, to.Y - r);
            var c1 = new Point(from.X + dir * r, to.Y);

            ctx.LineTo(c0);
            ctx.CubicBezierTo(new Point(c0.X, c0.Y + r * KAPPA), new Point(c1.X - dir * r * KAPPA, c1.Y), c1);
            ctx.LineTo(to);
        }

        /// <summary>
        ///     Draws a merge line from a merge commit into the merged branch's lane and returns where it ends.
        /// </summary>
        private static Point MergeOut(StreamGeometryContext ctx, Point from, Point to, double rowHeight, bool fromBelow)
        {
            if (!fromBelow)
            {
                HorizontalThenDown(ctx, from, to);
                return to;
            }

            // Like "|\" in `git log --graph`: drop out of the bottom of the merge commit, run along the row boundary
            // below it, then turn down into the merged branch's lane.
            var boundary = from.Y + rowHeight * 0.5;
            var dir = Math.Sign(to.X - from.X);
            var r = Math.Min(CORNER_RADIUS, Math.Abs(to.X - from.X) * 0.5);

            ctx.LineTo(new Point(from.X, boundary - r));
            var c0 = new Point(from.X + dir * r, boundary);
            ctx.CubicBezierTo(new Point(from.X, boundary - r + r * KAPPA), new Point(c0.X - dir * r * KAPPA, boundary), c0);

            var c1 = new Point(to.X - dir * r, boundary);
            var c2 = new Point(to.X, boundary + r);
            ctx.LineTo(c1);
            ctx.CubicBezierTo(new Point(c1.X + dir * r * KAPPA, boundary), new Point(to.X, c2.Y - r * KAPPA), c2);
            return c2;
        }

        /// <summary>
        ///     Branches that start from a commit come into the right side of its node. A merge line heading right from the
        ///     same commit would run over them, so it leaves from below the node instead.
        /// </summary>
        private bool IsCrossingForks(Point mergeCommit, Point target)
        {
            if (target.X <= mergeCommit.X)
                return false;

            var row = (int)Math.Floor(mergeCommit.Y);
            return row >= 0 && row < _graph.Dots.Count && _graph.Dots[row].IsForkPoint;
        }

        private static bool IsRowCenter(double y)
        {
            return Math.Abs(y - Math.Floor(y) - 0.5) < 0.01;
        }

        private static bool IsDark(IBrush background)
        {
            if (background is not ISolidColorBrush solid)
                return true;

            var c = solid.Color;
            return c.R * 0.299 + c.G * 0.587 + c.B * 0.114 < 128;
        }

        private static Pen GetWipPen()
        {
            return new Pen(Models.CommitGraph.GetPen(0).Brush, 2, new DashStyle([0.1, 2.2], 0), PenLineCap.Round);
        }

        public static Pen GetRefConnectorPen(int color, bool isHighlighted)
        {
            return new Pen(new SolidColorBrush(Models.CommitGraph.GetColor(color), isHighlighted ? 0.55 : 0.2), 1);
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

        /// <summary>
        ///     Radius of a commit's avatar node, also used to place the hover area for its author's name.
        /// </summary>
        public const double AVATAR_RADIUS = 10;

        private const double MERGE_RADIUS = 5.5;
        private const double CORNER_RADIUS = 10;
        private const double KAPPA = 0.5523;
        private static readonly IBrush INITIALS_ON_DARK = new SolidColorBrush(Color.FromRgb(0xF4, 0xF3, 0xFA));

        private Models.CommitGraph _graph = null;
        private Models.CommitGraphLayout _layout = null;
        private List<Models.Commit> _selectedCommits = null;
        private readonly HashSet<string> _selected = new HashSet<string>();
        private bool _showRefConnectors = true;
        private readonly Dictionary<string, Bitmap> _avatars = new Dictionary<string, Bitmap>();
    }
}
