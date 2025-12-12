using System;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services.Update
{
    /// <summary>
    /// Repräsentiert das Update-Manifest vom Server.
    /// Dieses JSON wird von einem HTTP-Endpoint abgerufen (z.B. GitHub Releases, eigener Server).
    /// </summary>
    public class UpdateManifest
    {
        /// <summary>
        /// Die neueste verfügbare Version (z.B. "1.2.3").
        /// </summary>
        [JsonPropertyName("latestVersion")]
        public string LatestVersion { get; set; } = "";

        /// <summary>
        /// Download-URL für das Update-Paket (ZIP).
        /// </summary>
        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = "";

        /// <summary>
        /// Changelog/Release-Notes für diese Version.
        /// </summary>
        [JsonPropertyName("changelog")]
        public string Changelog { get; set; } = "";

        /// <summary>
        /// Ob dieses Update zwingend erforderlich ist.
        /// </summary>
        [JsonPropertyName("mandatory")]
        public bool Mandatory { get; set; }

        /// <summary>
        /// SHA256-Hash des ZIP-Archivs für Integritätsprüfung (optional).
        /// </summary>
        [JsonPropertyName("fileHash")]
        public string? FileHash { get; set; }

        /// <summary>
        /// Minimale Version, ab der dieses Update angewendet werden kann (optional).
        /// </summary>
        [JsonPropertyName("minVersion")]
        public string? MinVersion { get; set; }

        /// <summary>
        /// Veröffentlichungsdatum (optional).
        /// </summary>
        [JsonPropertyName("releaseDate")]
        public string? ReleaseDate { get; set; }

        /// <summary>
        /// Größe des Downloads in Bytes (optional).
        /// </summary>
        [JsonPropertyName("fileSize")]
        public long? FileSize { get; set; }

        /// <summary>
        /// Parst die LatestVersion als Version-Objekt.
        /// </summary>
        public Version? GetVersion()
        {
            if (Version.TryParse(LatestVersion, out var version))
            {
                return version;
            }
            return null;
        }
    }
}
