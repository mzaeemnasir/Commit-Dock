using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json.Serialization;

namespace CommitDock.Models
{
    public class ReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string DownloadUrl { get; set; }

        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    public class Version
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("tag_name")]
        public string TagName { get; set; }

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; }

        [JsonPropertyName("published_at")]
        public DateTime PublishedAt { get; set; }

        [JsonPropertyName("body")]
        public string Body { get; set; }

        [JsonPropertyName("assets")]
        public List<ReleaseAsset> Assets { get; set; } = [];

        [JsonIgnore]
        public System.Version CurrentVersion { get; }

        [JsonIgnore]
        public string CurrentVersionStr => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{CurrentVersion.Build}";

        /// <summary>
        ///     The tag without its leading "v", e.g. "1.0.0". Release files are named after it.
        /// </summary>
        [JsonIgnore]
        public string VersionNumber => string.IsNullOrEmpty(TagName) ? string.Empty : TagName.TrimStart('v', 'V');

        [JsonIgnore]
        public bool IsNewVersion => System.Version.TryParse(VersionNumber, out var version) && CurrentVersion.CompareTo(version) < 0;

        [JsonIgnore]
        public string ReleaseDateStr => DateTimeFormat.Format(PublishedAt, true);

        [JsonIgnore]
        public string ReleasePageUrl => string.IsNullOrEmpty(HtmlUrl) ? SelfUpdater.ReleasesUrl : HtmlUrl;

        public Version()
        {
            var assembly = Assembly.GetExecutingAssembly().GetName();
            CurrentVersion = assembly.Version ?? new System.Version();
        }

        public ReleaseAsset FindAsset(string name)
        {
            foreach (var asset in Assets)
            {
                if (name.Equals(asset.Name, StringComparison.OrdinalIgnoreCase))
                    return asset;
            }

            return null;
        }
    }
}
