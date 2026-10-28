using System;
using System.Collections.Generic;

using Avalonia.Media;
using Avalonia.Styling;

namespace CommitDock.Models
{
    /// <summary>
    ///     Built-in dark theme variants. Each one inherits from <see cref="ThemeVariant.Dark"/>, so any
    ///     resource it does not define falls back to the default dark palette.
    /// </summary>
    public static class ThemeVariants
    {
        public static readonly ThemeVariant Obsidian = new("Obsidian", ThemeVariant.Dark);
        public static readonly ThemeVariant Abyss = new("Abyss", ThemeVariant.Dark);
        public static readonly ThemeVariant Forest = new("Forest", ThemeVariant.Dark);
        public static readonly ThemeVariant Ember = new("Ember", ThemeVariant.Dark);
        public static readonly ThemeVariant Nebula = new("Nebula", ThemeVariant.Dark);
        public static readonly ThemeVariant Arctic = new("Arctic", ThemeVariant.Dark);
        public static readonly ThemeVariant Mono = new("Mono", ThemeVariant.Dark);

        public static readonly List<ThemeVariant> Selectable =
        [
            ThemeVariant.Default,
            ThemeVariant.Dark,
            Obsidian,
            Abyss,
            Forest,
            Ember,
            Nebula,
            Arctic,
            Mono,
            ThemeVariant.Light,
        ];

        public static ThemeVariant Parse(string name)
        {
            if (string.IsNullOrEmpty(name))
                return ThemeVariant.Default;

            foreach (var variant in Selectable)
            {
                if (name.Equals(variant.Key as string, StringComparison.OrdinalIgnoreCase))
                    return variant;
            }

            return ThemeVariant.Default;
        }

        public static List<Color> GetGraphPalette(ThemeVariant variant)
        {
            if (variant != null && s_graphPalettes.TryGetValue(variant, out var palette))
                return palette;

            return null;
        }

        private static List<Color> ParseColors(params string[] colors)
        {
            var list = new List<Color>(colors.Length);
            foreach (var c in colors)
                list.Add(Color.Parse(c));
            return list;
        }

        private static readonly Dictionary<ThemeVariant, List<Color>> s_graphPalettes = new()
        {
            [Obsidian] = ParseColors("#2DD4BF", "#F472B6", "#FBBF24", "#60A5FA", "#A3E635", "#F87171", "#C084FC", "#FB923C", "#34D399", "#E879F9", "#38BDF8", "#FACC15"),
            [Abyss] = ParseColors("#38BDF8", "#FB7185", "#FBBF24", "#34D399", "#A78BFA", "#F97316", "#22D3EE", "#E879F9", "#A3E635", "#60A5FA", "#F472B6", "#2DD4BF"),
            [Forest] = ParseColors("#34D399", "#FBBF24", "#60A5FA", "#F472B6", "#A3E635", "#FB923C", "#22D3EE", "#C084FC", "#F87171", "#5EEAD4", "#FDE047", "#93C5FD"),
            [Ember] = ParseColors("#F59E0B", "#38BDF8", "#F472B6", "#84CC16", "#FB7185", "#2DD4BF", "#C084FC", "#FACC15", "#60A5FA", "#F97316", "#34D399", "#E879F9"),
            [Nebula] = ParseColors("#F472B6", "#A78BFA", "#22D3EE", "#FBBF24", "#4ADE80", "#FB923C", "#60A5FA", "#E879F9", "#2DD4BF", "#F87171", "#A3E635", "#38BDF8"),
            [Arctic] = ParseColors("#88C0D0", "#BF616A", "#EBCB8B", "#A3BE8C", "#B48EAD", "#D08770", "#81A1C1", "#8FBCBB", "#5E81AC", "#E5A3AE", "#C6D6A6", "#D8C2E0"),
            // Mono has no tint of its own, so the lanes use saturated hues (ordered around the color wheel) that stay vivid on pure black.
            [Mono] = ParseColors("#22D3EE", "#3B82F6", "#A855F7", "#E879F9", "#FB7185", "#EF4444", "#F97316", "#FACC15", "#A3E635", "#2DD4BF"),
        };
    }
}
