using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services.Update
{
    /// <summary>
    /// GitHub Release API Response.
    /// </summary>
    public class GitHubReleaseInfo
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("body")]
        public string Body { get; set; } = "";

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("published_at")]
        public string PublishedAt { get; set; } = "";

        [JsonPropertyName("assets")]
        public GitHubAsset[] Assets { get; set; } = [];

        /// <summary>
        /// Extrahiert die Version aus dem Tag-Namen (z.B. "v1.2.3" -> "1.2.3").
        /// </summary>
        public string GetVersionString()
        {
            var tag = TagName ?? "";
            if (tag.StartsWith("v", System.StringComparison.OrdinalIgnoreCase))
            {
                return tag[1..];
            }
            return tag;
        }

        /// <summary>
        /// Konvertiert zu UpdateManifest.
        /// </summary>
        public UpdateManifest ToUpdateManifest()
        {
            var zipAsset = GetZipAsset();

            return new UpdateManifest
            {
                LatestVersion = GetVersionString(),
                DownloadUrl = zipAsset?.BrowserDownloadUrl ?? "",
                Changelog = Body ?? "",
                Mandatory = false,
                FileHash = null,
                ReleaseDate = PublishedAt,
                FileSize = zipAsset?.Size
            };
        }

        /// <summary>
        /// Findet das ZIP-Asset.
        /// </summary>
        public GitHubAsset? GetZipAsset()
        {
            if (Assets == null || Assets.Length == 0)
                return null;

            // Suche nach ZIP-Datei
            foreach (var asset in Assets)
            {
                if (asset.Name?.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase) == true)
                {
                    return asset;
                }
            }

            // Fallback: erstes Asset
            return Assets.Length > 0 ? Assets[0] : null;
        }
    }

    /// <summary>
    /// GitHub Release Asset.
    /// </summary>
    public class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";

        [JsonPropertyName("size")]
        public long Size { get; set; }

        [JsonPropertyName("content_type")]
        public string ContentType { get; set; } = "";
    }
}
