using System;
using System.Collections.Generic;

using Avalonia;
using Avalonia.Media;

namespace CommitDock.Models
{
    public record CommitGraphLayout(double StartX, double StartY, double ClipWidth, double RowHeight, double TopInset = 0);

    public enum CommitGraphHighlighting
    {
        All = 0,
        CurrentBranchOnly,
        SelectedCommitsOnly,
        CurrentBranchAndSelectedCommits,
        SelectedCommitsOnlyFirstParent,
    }

    /// <summary>
    ///     Lays commits out on fixed lanes: a branch keeps its column from its newest commit down to the commit it
    ///     forked from, so lanes are straight vertical lines. Lines only move sideways at merges (out of a node) and
    ///     fork points (into a node), and every lane is colored by its column.
    ///     X coordinates are in pixels, Y coordinates are in rows (row N is centered at N + 0.5).
    /// </summary>
    public class CommitGraph
    {
        public const double LaneWidth = 24;
        public const double LaneMargin = 8;

        public static List<Pen> Pens { get; } = [];
        public static List<Pen> DimPens { get; } = [];
        public static List<Pen> GlowPens { get; } = [];

        public static void SetDefaultPens(bool isDark, double thickness = 2)
        {
            SetPens(isDark ? s_darkPenColors : s_lightPenColors, thickness);
        }

        public static void SetPens(List<Color> colors, double thickness)
        {
            Pens.Clear();
            DimPens.Clear();
            GlowPens.Clear();

            foreach (var c in colors)
            {
                Pens.Add(new Pen(new SolidColorBrush(c), thickness, null, PenLineCap.Round, PenLineJoin.Round));
                DimPens.Add(new Pen(new SolidColorBrush(c, 0.28), thickness, null, PenLineCap.Round, PenLineJoin.Round));
                GlowPens.Add(new Pen(new SolidColorBrush(c, 0.16), thickness + 6, null, PenLineCap.Round, PenLineJoin.Round));
            }
        }

        /// <summary>
        ///     Color indexes are lane columns, so they are wrapped here instead of when the graph is generated.
        ///     That keeps them valid when the palette changes.
        /// </summary>
        public static Pen GetPen(int color, bool isHighlighted = true)
        {
            var pens = isHighlighted ? Pens : DimPens;
            return pens.Count > 0 ? pens[color % pens.Count] : s_fallbackPen;
        }

        public static Color GetColor(int color)
        {
            return ((ISolidColorBrush)GetPen(color).Brush).Color;
        }

        public static double GetLaneX(int column)
        {
            return LaneMargin + LaneWidth * column + LaneWidth * 0.5;
        }

        /// <summary>
        ///     One lane (or one highlighted/dimmed part of it). All points share the lane's X except the first one,
        ///     which is the merge commit when the lane was opened by a merge, and the last one, which is the commit
        ///     the lane forks from when it does not end in its own column.
        /// </summary>
        public class Path(int color, bool isHighlighted, bool isWip = false)
        {
            public List<Point> Points { get; } = [];
            public int Color { get; } = color;
            public bool IsHighlighted { get; } = isHighlighted;
            public bool IsWip { get; } = isWip;
        }

        /// <summary>
        ///     A merge into a lane that already exists: leaves the merge commit horizontally and turns down into the lane.
        /// </summary>
        public class Link
        {
            public Point Start;
            public Point End;
            public int Color;
            public bool IsHighlighted;
        }

        public enum DotType
        {
            Default,
            Merge,
        }

        public class Dot
        {
            public string SHA;
            public DotType Type;
            public Point Center;
            public int Color;
            public bool IsHead;
            public bool IsForkPoint;
            public bool IsHighlighted;
            public bool HasRefs;
            public User Author;
        }

        public List<Path> Paths { get; } = [];
        public List<Link> Links { get; } = [];
        public List<Dot> Dots { get; } = [];
        public double Width { get; private set; } = 0;

        public static CommitGraph Generate(List<Commit> commits, bool firstParentOnlyEnabled, CommitGraphHighlighting highlighting, HashSet<string> highlightExtraCommits, bool hasLocalChanges = false)
        {
            var graph = new CommitGraph();
            var lanes = new List<Lane>();
            var defHighlighting = highlighting == CommitGraphHighlighting.All;
            var columns = 1;

            // Local changes behave like a commit on top of HEAD: they open the first lane, and the dotted line from them
            // runs straight down that lane to HEAD without crossing any other commit.
            if (hasLocalChanges)
            {
                var head = commits.Find(x => x.IsCurrentHead);
                if (head != null)
                {
                    var wip = new Lane(head.SHA, 0, true, new Point(GetLaneX(0), -0.5), true);
                    lanes.Add(wip);
                    graph.Paths.Add(wip.Path);
                }
            }

            for (var row = 0; row < commits.Count; row++)
            {
                var commit = commits[row];
                var y = row + 0.5;
                var isHighlighted = defHighlighting;

                // Every lane waiting for this commit ends here. The leftmost one carries on as this commit's lane and
                // the others fork into it.
                var col = -1;
                var isForkPoint = false;
                Lane major = null;
                for (var i = 0; i < lanes.Count; i++)
                {
                    var lane = lanes[i];
                    if (lane == null || !lane.Next.Equals(commit.SHA, StringComparison.Ordinal))
                        continue;

                    if (major == null)
                    {
                        col = i;
                        major = lane;
                        if (!lane.IsWip)
                            isHighlighted = lane.IsHighlighted;
                        continue;
                    }

                    if (!isHighlighted && lane.IsHighlighted)
                        isHighlighted = true;

                    // The column is free again right away: a merged branch of this commit may take it.
                    lane.ForkInto(new Point(GetLaneX(col), y));
                    lanes[i] = null;
                    isForkPoint = true;
                }

                if (major == null)
                    col = FindFreeColumn(lanes, -1);

                var x = GetLaneX(col);
                var position = new Point(x, y);

                // Calculate highlighted state
                if (!isHighlighted)
                {
                    switch (highlighting)
                    {
                        case CommitGraphHighlighting.CurrentBranchOnly:
                            isHighlighted = commit.IsMerged;
                            break;
                        case CommitGraphHighlighting.SelectedCommitsOnly:
                        case CommitGraphHighlighting.SelectedCommitsOnlyFirstParent:
                            if (highlightExtraCommits.Remove(commit.SHA))
                            {
                                isHighlighted = true;
                                // Highlight first parent, other parents are dealt with later
                                if (commit.Parents.Count > 0)
                                    highlightExtraCommits.Add(commit.Parents[0]);
                            }
                            break;
                        default: // CommitGraphHighlighting.CurrentBranchAndSelectedCommits
                            if (commit.IsMerged)
                            {
                                isHighlighted = true;
                            }
                            else if (highlightExtraCommits.Remove(commit.SHA))
                            {
                                isHighlighted = true;
                                // Highlight first parent, other parents are dealt with later
                                if (commit.Parents.Count > 0)
                                    highlightExtraCommits.Add(commit.Parents[0]);
                            }
                            break;
                    }
                }
                commit.IsHighlightedInGraph = isHighlighted;

                // The dotted local-changes lane stops at HEAD; HEAD's own history continues below it as a normal lane.
                if (major is { IsWip: true })
                {
                    major.End(position);
                    lanes[col] = null;
                    major = null;
                }

                if (commit.Parents.Count > 0)
                {
                    if (major == null)
                    {
                        major = new Lane(commit.Parents[0], col, isHighlighted, position);
                        SetLane(lanes, col, major);
                        graph.Paths.Add(major.Path);
                    }
                    else
                    {
                        if (isHighlighted && !major.IsHighlighted)
                            graph.Paths.Add(major.Highlight(y));

                        major.Next = commit.Parents[0];
                    }
                }
                else if (major != null)
                {
                    major.End(position);
                    lanes[col] = null;
                }

                graph.Dots.Add(new Dot()
                {
                    SHA = commit.SHA,
                    Type = commit.Parents.Count > 1 ? DotType.Merge : DotType.Default,
                    Center = position,
                    Color = col,
                    IsHead = commit.IsCurrentHead,
                    IsForkPoint = isForkPoint,
                    IsHighlighted = isHighlighted,
                    HasRefs = commit.HasDecorators,
                    Author = commit.Author,
                });

                // Deal with other parents (the first parent has been processed)
                if (!firstParentOnlyEnabled)
                {
                    if (highlighting == CommitGraphHighlighting.SelectedCommitsOnlyFirstParent)
                        isHighlighted = false;

                    for (var j = 1; j < commit.Parents.Count; j++)
                    {
                        var parentHash = commit.Parents[j];
                        if (parentHash.Equals(commit.Parents[0], StringComparison.Ordinal))
                            continue;

                        var target = -1;
                        for (var i = 0; i < lanes.Count; i++)
                        {
                            if (lanes[i] is { IsWip: false } l && l.Next.Equals(parentHash, StringComparison.Ordinal))
                            {
                                target = i;
                                break;
                            }
                        }

                        if (target >= 0)
                        {
                            var lane = lanes[target];
                            if (isHighlighted && !lane.IsHighlighted)
                                graph.Paths.Add(lane.Highlight(y + 0.5));

                            graph.Links.Add(new Link()
                            {
                                Start = position,
                                End = new Point(GetLaneX(target), y + 0.5),
                                Color = target,
                                IsHighlighted = isHighlighted,
                            });
                        }
                        else
                        {
                            // A merged branch gets the free column closest to the merge, so the merge line stays short.
                            var column = FindFreeColumn(lanes, col);
                            var lane = new Lane(parentHash, column, isHighlighted, position, new Point(GetLaneX(column), y + 0.5));
                            SetLane(lanes, column, lane);
                            graph.Paths.Add(lane.Path);
                        }
                    }
                }

                var used = col + 1;
                for (var i = lanes.Count - 1; i >= used; i--)
                {
                    if (lanes[i] != null)
                    {
                        used = i + 1;
                        break;
                    }
                }

                columns = Math.Max(columns, used);
                commit.Color = col;
                commit.GraphX = x;
            }

            // Lanes whose parents are not loaded run off the bottom of the list.
            var bottom = (double)commits.Count;
            foreach (var lane in lanes)
                lane?.End(new Point(lane.X, bottom));

            graph.Width = LaneMargin * 1.5 + columns * LaneWidth;
            return graph;
        }

        private static int FindFreeColumn(List<Lane> lanes, int near)
        {
            bool IsFree(int i) => i >= lanes.Count || lanes[i] == null;

            // New branches take the leftmost free column.
            if (near < 0)
            {
                for (var i = 0; i < lanes.Count; i++)
                {
                    if (IsFree(i))
                        return i;
                }

                return lanes.Count;
            }

            // Merged branches take the nearest free column, preferring the right side.
            for (var d = 1; ; d++)
            {
                if (IsFree(near + d))
                    return near + d;

                var left = near - d;
                if (left >= 0 && IsFree(left))
                    return left;
            }
        }

        private static void SetLane(List<Lane> lanes, int column, Lane lane)
        {
            while (lanes.Count <= column)
                lanes.Add(null);

            lanes[column] = lane;
        }

        private class Lane
        {
            public string Next { get; set; }
            public Path Path { get; private set; }
            public double X { get; }
            public bool IsWip => Path.IsWip;
            public bool IsHighlighted => Path.IsHighlighted;

            public Lane(string next, int column, bool isHighlighted, Point start, bool isWip = false)
            {
                Next = next;
                X = GetLaneX(column);
                Path = new Path(column, isHighlighted, isWip);
                Path.Points.Add(start);
            }

            public Lane(string next, int column, bool isHighlighted, Point from, Point start)
                : this(next, column, isHighlighted, from)
            {
                Add(start);
            }

            /// <summary>
            ///     Ends this lane in its own column.
            /// </summary>
            public void End(Point at)
            {
                Add(new Point(X, at.Y));
            }

            /// <summary>
            ///     Ends this lane in the commit it forked from, which sits in another column of the same row.
            /// </summary>
            public void ForkInto(Point node)
            {
                Add(new Point(X, node.Y - 0.5));
                Add(node);
            }

            /// <summary>
            ///     Highlights this lane from the given row position on, and returns the new highlighted part.
            /// </summary>
            public Path Highlight(double y)
            {
                var at = new Point(X, y);
                Add(at);

                Path = new Path(Path.Color, true);
                Path.Points.Add(at);
                return Path;
            }

            private void Add(Point p)
            {
                var last = Path.Points[^1];
                if (p.Y > last.Y || (p.Y == last.Y && p.X != last.X))
                    Path.Points.Add(p);
            }
        }

        private static readonly Pen s_fallbackPen = new Pen(Brushes.Gray, 2);

        // Ordered around the color wheel, so neighbouring lanes always get clearly different colors.
        private static readonly List<Color> s_darkPenColors = [
            Color.Parse("#18A7CC"), // cyan
            Color.Parse("#2E74F5"), // blue
            Color.Parse("#9C40E8"), // violet
            Color.Parse("#CF35C0"), // magenta
            Color.Parse("#E92A80"), // pink
            Color.Parse("#E8433F"), // red
            Color.Parse("#F26B32"), // orange
            Color.Parse("#F2C437"), // yellow
            Color.Parse("#7DD33F"), // green
            Color.Parse("#2FCB9D"), // teal
        ];

        // The same hues, deep enough to stay readable as thin lines on a white background.
        private static readonly List<Color> s_lightPenColors = [
            Color.Parse("#0B8AAD"), // cyan
            Color.Parse("#1E5FD6"), // blue
            Color.Parse("#8331C9"), // violet
            Color.Parse("#B0249F"), // magenta
            Color.Parse("#CC1F6A"), // pink
            Color.Parse("#C93030"), // red
            Color.Parse("#D2541A"), // orange
            Color.Parse("#B08600"), // yellow
            Color.Parse("#4C9A1C"), // green
            Color.Parse("#0F9A78"), // teal
        ];
    }
}
