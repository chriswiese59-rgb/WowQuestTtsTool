using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// JSON-Index fuer generierte Quest-Audio-Dateien.
    /// Wird vom WoW-Addon verwendet, um Audio-Dateien zu finden.
    /// </summary>
    public class QuestAudioIndex
    {
        /// <summary>
        /// Sprachcode (z.B. "deDE", "enUS").
        /// </summary>
        [JsonPropertyName("language")]
        public string Language { get; set; } = "deDE";

        /// <summary>
        /// Zeitpunkt der Generierung (UTC).
        /// </summary>
        [JsonPropertyName("generated_at")]
        public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Anzahl der Eintraege (wird beim Serialisieren automatisch berechnet).
        /// </summary>
        [JsonPropertyName("total_count")]
        public int TotalCount => Entries?.Count ?? 0;

        /// <summary>
        /// Liste aller Audio-Eintraege.
        /// </summary>
        [JsonPropertyName("entries")]
        public List<QuestAudioIndexEntry> Entries { get; set; } = [];
    }

    /// <summary>
    /// Einzelner Eintrag im Quest-Audio-Index.
    /// Beschreibt eine generierte Audio-Datei.
    /// </summary>
    public class QuestAudioIndexEntry
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
        /// Ob die Quest zur Hauptstory gehoert.
        /// </summary>
        [JsonPropertyName("is_main_story")]
        public bool IsMainStory { get; set; }

        /// <summary>
        /// Stimm-Gender ("male", "female", "neutral").
        /// </summary>
        [JsonPropertyName("gender")]
        public string Gender { get; set; } = "neutral";

        /// <summary>
        /// Relativer Pfad zur Audio-Datei (mit / als Separator).
        /// Beispiel: "deDE/ElwynnForest/Main/Quest_176_male.mp3"
        /// </summary>
        [JsonPropertyName("relative_path")]
        public string RelativePath { get; set; } = "";

        /// <summary>
        /// Quest-Titel (fuer Anzeige/Debugging).
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        /// <summary>
        /// Kategorie der Quest (Main, Side, Group, etc.).
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; } = "";

        /// <summary>
        /// Dauer der Audio-Datei in Sekunden (optional, kann spaeter befuellt werden).
        /// </summary>
        [JsonPropertyName("duration_seconds")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public double DurationSeconds { get; set; }

        /// <summary>
        /// Dateigroesse in Bytes (optional).
        /// </summary>
        [JsonPropertyName("file_size_bytes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public long FileSizeBytes { get; set; }
    }
}
