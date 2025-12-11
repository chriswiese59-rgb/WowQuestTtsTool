using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Models
{
    /// <summary>
    /// Snapshot der Quest-Datenquelle zu einem bestimmten Zeitpunkt.
    /// Wird verwendet, um Änderungen zwischen verschiedenen Datenständen zu erkennen.
    /// </summary>
    public class QuestSourceSnapshot
    {
        /// <summary>
        /// Eindeutige Versions-ID des Snapshots (z.B. "2025-12-11_14-30").
        /// </summary>
        [JsonPropertyName("data_version")]
        public string DataVersion { get; set; } = "";

        /// <summary>
        /// WoW-Build-Version (optional, z.B. "11.2.7").
        /// </summary>
        [JsonPropertyName("wow_build")]
        public string? WowBuild { get; set; }

        /// <summary>
        /// Zeitpunkt der Snapshot-Erstellung (UTC).
        /// </summary>
        [JsonPropertyName("created_at_utc")]
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Sprachcode des Snapshots (z.B. "deDE").
        /// </summary>
        [JsonPropertyName("language_code")]
        public string LanguageCode { get; set; } = "deDE";

        /// <summary>
        /// Anzahl der Quests im Snapshot.
        /// </summary>
        [JsonPropertyName("quest_count")]
        public int QuestCount => Entries?.Count ?? 0;

        /// <summary>
        /// Liste aller Quest-Einträge im Snapshot.
        /// </summary>
        [JsonPropertyName("entries")]
        public List<QuestSnapshotEntry> Entries { get; set; } = [];

        /// <summary>
        /// Optionale Metadaten (z.B. Quell-Datenbank, API-Version).
        /// </summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, string> Metadata { get; set; } = [];

        /// <summary>
        /// Erstellt eine neue DataVersion basierend auf dem aktuellen Zeitstempel.
        /// </summary>
        public static string GenerateDataVersion()
        {
            return DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        }
    }

    /// <summary>
    /// Einzelner Quest-Eintrag im Snapshot.
    /// Enthält alle relevanten Daten für den Diff-Vergleich.
    /// </summary>
    public class QuestSnapshotEntry
    {
        /// <summary>
        /// Quest-ID.
        /// </summary>
        [JsonPropertyName("quest_id")]
        public int QuestId { get; set; }

        /// <summary>
        /// Zone der Quest.
        /// </summary>
        [JsonPropertyName("zone")]
        public string Zone { get; set; } = "";

        /// <summary>
        /// Quest-Kategorie (Main, Side, Group, etc.).
        /// </summary>
        [JsonPropertyName("category")]
        public QuestCategory Category { get; set; } = QuestCategory.Unknown;

        /// <summary>
        /// Quest-Titel.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        /// <summary>
        /// Quest-Beschreibung.
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        /// <summary>
        /// Quest-Ziele (Objectives).
        /// </summary>
        [JsonPropertyName("objectives")]
        public string Objectives { get; set; } = "";

        /// <summary>
        /// Quest-Abschlusstext (Completion).
        /// </summary>
        [JsonPropertyName("completion")]
        public string Completion { get; set; } = "";

        /// <summary>
        /// Lokalisierungs-Key (z.B. "deDE").
        /// </summary>
        [JsonPropertyName("localization_key")]
        public string LocalizationKey { get; set; } = "deDE";

        /// <summary>
        /// SHA256-Hash über alle Text-Inhalte.
        /// Wird verwendet, um Änderungen zu erkennen.
        /// </summary>
        [JsonPropertyName("content_hash")]
        public string ContentHash { get; set; } = "";

        /// <summary>
        /// Ob die Quest eine Hauptquest ist.
        /// </summary>
        [JsonPropertyName("is_main_story")]
        public bool IsMainStory { get; set; }

        /// <summary>
        /// Ob die Quest eine Gruppenquest ist.
        /// </summary>
        [JsonPropertyName("is_group_quest")]
        public bool IsGroupQuest { get; set; }

        /// <summary>
        /// Berechnet den ContentHash aus den Textfeldern.
        /// Der Hash ist normalisiert (Whitespace getrimmt, lowercase).
        /// </summary>
        public void ComputeContentHash()
        {
            ContentHash = ComputeHash(Title, Description, Objectives, Completion);
        }

        /// <summary>
        /// Berechnet einen SHA256-Hash über die gegebenen Texte.
        /// </summary>
        public static string ComputeHash(string? title, string? description, string? objectives, string? completion)
        {
            // Normalisiere alle Texte: Trim, Lowercase
            var normalized = new StringBuilder();
            normalized.Append(NormalizeText(title));
            normalized.Append("|");
            normalized.Append(NormalizeText(description));
            normalized.Append("|");
            normalized.Append(NormalizeText(objectives));
            normalized.Append("|");
            normalized.Append(NormalizeText(completion));

            // SHA256 berechnen
            var bytes = Encoding.UTF8.GetBytes(normalized.ToString());
            var hashBytes = SHA256.HashData(bytes);

            // Als Hex-String zurückgeben (verkürzt auf 16 Zeichen für Lesbarkeit)
            return Convert.ToHexString(hashBytes)[..16];
        }

        /// <summary>
        /// Normalisiert einen Text für konsistenten Hash-Vergleich.
        /// </summary>
        private static string NormalizeText(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";

            // Whitespace normalisieren, alles lowercase
            return text.Trim().ToLowerInvariant();
        }

        /// <summary>
        /// Erstellt einen QuestSnapshotEntry aus einem Quest-Objekt.
        /// </summary>
        public static QuestSnapshotEntry FromQuest(Quest quest, string languageCode = "deDE")
        {
            var entry = new QuestSnapshotEntry
            {
                QuestId = quest.QuestId,
                Zone = quest.Zone ?? "Unknown",
                Category = quest.Category,
                Title = quest.Title ?? "",
                Description = quest.Description ?? "",
                Objectives = quest.Objectives ?? "",
                Completion = quest.Completion ?? "",
                LocalizationKey = languageCode,
                IsMainStory = quest.IsMainStory,
                IsGroupQuest = quest.IsGroupQuest
            };

            entry.ComputeContentHash();
            return entry;
        }
    }
}
