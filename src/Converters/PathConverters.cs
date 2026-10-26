using System.IO;
using Avalonia.Data.Converters;

namespace CommitDock.Converters
{
    public static class PathConverters
    {
        public static readonly FuncValueConverter<string, string> PureFileName =
            new(v => Path.GetFileName(v) ?? "");

        public static readonly FuncValueConverter<string, string> PureDirectoryName =
            new(v => Path.GetDirectoryName(v) ?? "");

        /// <summary>
        ///     The folder part of a git path including the trailing slash ("src/Views/"), or an empty string for a
        ///     file at the repository root. Git paths always use forward slashes.
        /// </summary>
        public static readonly FuncValueConverter<string, string> DirectoryPrefix =
            new(v =>
            {
                if (string.IsNullOrEmpty(v))
                    return string.Empty;

                var idx = v.LastIndexOf('/');
                return idx < 0 ? string.Empty : v.Substring(0, idx + 1);
            });

        public static readonly FuncValueConverter<string, string> RelativeToHome =
            new(Native.OS.GetRelativePathToHome);
    }
}
