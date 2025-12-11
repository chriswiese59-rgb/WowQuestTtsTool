using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Models
{
    /// <summary>
    /// Typ der Änderung im Quest-Diff.
    /// </summary>
    public enum QuestDiffType
    {
        /// <summary>
        /// Neue Quest (war nicht im alten Snapshot).
        /// </summary>
        New,

        /// <summary>
        /// Geänderte Quest (ContentHash unterschiedlich).
        /// </summary>
        Changed,

        /// <summary>
        /// Entfernte Quest (war im alten Snapshot, fehlt jetzt).
        /// </summary>
        Removed,

        /// <summary>
        /// Unveränderte Quest (nur für Statistik).
        /// </summary>
        Unchanged
    }

    /// <summary>
    /// Einzelner Diff-Eintrag für eine Quest.
    /// </summary>
    public class QuestDiffEntry
    {
        /// <summary>
        /// Quest-ID.
        /// </summary>
        [JsonPropertyName("quest_id")]
        public int QuestId { get; set; }

        /// <summary>
        /// Quest-Titel.
        /// </summary>
        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        /// <summary>
        /// Zone der Quest.
        /// </summary>
        [JsonPropertyName("zone")]
        public string Zone { get; set; } = "";

        /// <summary>
        /// Quest-Kategorie.
        /// </summary>
        [JsonPropertyName("category")]
        public QuestCategory Category { get; set; } = QuestCategory.Unknown;

        /// <summary>
        /// Typ der Änderung (New, Changed, Removed).
        /// </summary>
        [JsonPropertyName("diff_type")]
        public QuestDiffType DiffType { get; set; }

        /// <summary>
        /// Hash des alten Inhalts (falls vorhanden).
        /// </summary>
        [JsonPropertyName("old_hash")]
        public string? OldHash { get; set; }

        /// <summary>
        /// Hash des neuen Inhalts (falls vorhanden).
        /// </summary>
        [JsonPropertyName("new_hash")]
        public string? NewHash { get; set; }

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
        /// Kurze Beschreibung der Änderung (z.B. "Text geändert", "Neu hinzugefügt").
        /// </summary>
        [JsonIgnore]
        public string DiffDescription => DiffType switch
        {
            QuestDiffType.New => "Neu hinzugefügt",
            QuestDiffType.Changed => "Text geändert",
            QuestDiffType.Removed => "Entfernt",
            QuestDiffType.Unchanged => "Unverändert",
            _ => "Unbekannt"
        };

        /// <summary>
        /// Kategorie-Kurzname für Anzeige.
        /// </summary>
        [JsonIgnore]
        public string CategoryShortName => Category.ToShortName();

        /// <summary>
        /// Diff-Typ als kurzer String für Anzeige.
        /// </summary>
        [JsonIgnore]
        public string DiffTypeShortName => DiffType switch
        {
            QuestDiffType.New => "NEU",
            QuestDiffType.Changed => "GEÄNDERT",
            QuestDiffType.Removed => "ENTFERNT",
            QuestDiffType.Unchanged => "-",
            _ => "?"
        };
    }

    /// <summary>
    /// Ergebnis eines Quest-Daten-Diffs.
    /// Enthält alle Änderungen zwischen zwei Datenständen.
    /// </summary>
    public class QuestDiffResult
    {
        /// <summary>
        /// Alle Diff-Einträge.
        /// </summary>
        [JsonPropertyName("all_entries")]
        public List<QuestDiffEntry> AllEntries { get; set; } = [];

        /// <summary>
        /// Zeitpunkt der Diff-Berechnung.
        /// </summary>
        [JsonPropertyName("computed_at_utc")]
        public DateTime ComputedAtUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// DataVersion des alten Snapshots (falls vorhanden).
        /// </summary>
        [JsonPropertyName("old_data_version")]
        public string? OldDataVersion { get; set; }

        /// <summary>
        /// DataVersion des neuen Snapshots.
        /// </summary>
        [JsonPropertyName("new_data_version")]
        public string? NewDataVersion { get; set; }

        // === Gefilterte Listen ===

        /// <summary>
        /// Neue Quests.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<QuestDiffEntry> NewEntries =>
            AllEntries.Where(e => e.DiffType == QuestDiffType.New);

        /// <summary>
        /// Geänderte Quests.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<QuestDiffEntry> ChangedEntries =>
            AllEntries.Where(e => e.DiffType == QuestDiffType.Changed);

        /// <summary>
        /// Entfernte Quests.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<QuestDiffEntry> RemovedEntries =>
            AllEntries.Where(e => e.DiffType == QuestDiffType.Removed);

        /// <summary>
        /// Unveränderte Quests.
        /// </summary>
        [JsonIgnore]
        public IEnumerable<QuestDiffEntry> UnchangedEntries =>
            AllEntries.Where(e => e.DiffType == QuestDiffType.Unchanged);

        /// <summary>
        /// Neue und geänderte Quests (zum Vertonen).
        /// </summary>
        [JsonIgnore]
        public IEnumerable<QuestDiffEntry> NewAndChangedEntries =>
            AllEntries.Where(e => e.DiffType == QuestDiffType.New || e.DiffType == QuestDiffType.Changed);

        // === Statistiken ===

        /// <summary>
        /// Anzahl neuer Quests.
        /// </summary>
        [JsonPropertyName("new_count")]
        public int NewCount => AllEntries.Count(e => e.DiffType == QuestDiffType.New);

        /// <summary>
        /// Anzahl geänderter Quests.
        /// </summary>
        [JsonPropertyName("changed_count")]
        public int ChangedCount => AllEntries.Count(e => e.DiffType == QuestDiffType.Changed);

        /// <summary>
        /// Anzahl entfernter Quests.
        /// </summary>
        [JsonPropertyName("removed_count")]
        public int RemovedCount => AllEntries.Count(e => e.DiffType == QuestDiffType.Removed);

        /// <summary>
        /// Anzahl unveränderter Quests.
        /// </summary>
        [JsonPropertyName("unchanged_count")]
        public int UnchangedCount => AllEntries.Count(e => e.DiffType == QuestDiffType.Unchanged);

        /// <summary>
        /// Ob es überhaupt Änderungen gibt.
        /// </summary>
        [JsonIgnore]
        public bool HasChanges => NewCount > 0 || ChangedCount > 0 || RemovedCount > 0;

        /// <summary>
        /// Anzahl der Quests, die neu vertont werden müssen.
        /// </summary>
        [JsonIgnore]
        public int ToVoiceCount => NewCount + ChangedCount;

        /// <summary>
        /// Zusammenfassung als String für Anzeige.
        /// </summary>
        [JsonIgnore]
        public string Summary =>
            $"Neu: {NewCount}, Geändert: {ChangedCount}, Entfernt: {RemovedCount}, Unverändert: {UnchangedCount}";
    }

    /// <summary>
    /// Ergebnis eines Update-Scans.
    /// Enthält den Diff sowie den neuen Snapshot.
    /// </summary>
    public class UpdateScanResult
    {
        /// <summary>
        /// Der berechnete Diff.
        /// </summary>
        public QuestDiffResult Diff { get; set; } = new();

        /// <summary>
        /// Der neue Snapshot (basierend auf aktuellen Daten).
        /// </summary>
        public QuestSourceSnapshot NewSnapshot { get; set; } = new();

        /// <summary>
        /// Der alte Snapshot (falls vorhanden).
        /// </summary>
        public QuestSourceSnapshot? OldSnapshot { get; set; }

        /// <summary>
        /// Dictionary für schnellen Zugriff auf Quests nach ID.
        /// </summary>
        public Dictionary<int, Quest> QuestLookup { get; set; } = [];

        /// <summary>
        /// Ob der Scan erfolgreich war.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Fehlermeldung (falls nicht erfolgreich).
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Dauer des Scans.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Anzahl geladener Quests.
        /// </summary>
        public int TotalQuestsLoaded => QuestLookup.Count;
    }

    /// <summary>
    /// Ergebnis der TTS-Generierung für den Diff.
    /// </summary>
    public class UpdateApplyResult
    {
        /// <summary>
        /// Ob die Anwendung erfolgreich war.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Fehlermeldung (falls nicht erfolgreich).
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Anzahl erfolgreich vertonter Quests.
        /// </summary>
        public int QuestsVoiced { get; set; }

        /// <summary>
        /// Anzahl übersprungener Quests.
        /// </summary>
        public int QuestsSkipped { get; set; }

        /// <summary>
        /// Anzahl fehlgeschlagener Quests.
        /// </summary>
        public int QuestsFailed { get; set; }

        /// <summary>
        /// Liste der fehlgeschlagenen Quest-IDs mit Fehlermeldungen.
        /// </summary>
        public List<(int QuestId, string Error)> FailedQuests { get; set; } = [];

        /// <summary>
        /// Ob der Addon-Export durchgeführt wurde.
        /// </summary>
        public bool AddonExported { get; set; }

        /// <summary>
        /// Pfad zum exportierten Addon (falls exportiert).
        /// </summary>
        public string? AddonPath { get; set; }

        /// <summary>
        /// Ob der Snapshot gespeichert wurde.
        /// </summary>
        public bool SnapshotSaved { get; set; }

        /// <summary>
        /// Dauer der Anwendung.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// DataVersion des gespeicherten Snapshots.
        /// </summary>
        public string? SavedDataVersion { get; set; }

        /// <summary>
        /// Zusammenfassung als String.
        /// </summary>
        public string Summary =>
            $"Vertont: {QuestsVoiced}, Übersprungen: {QuestsSkipped}, Fehler: {QuestsFailed}";
    }

    /// <summary>
    /// Meta-Informationen zum aktuellen Datenstand.
    /// Wird im Update & Sync Tab angezeigt und im Addon exportiert.
    /// </summary>
    public class SyncMetadata
    {
        /// <summary>
        /// Letzte DataVersion.
        /// </summary>
        [JsonPropertyName("last_data_version")]
        public string LastDataVersion { get; set; } = "";

        /// <summary>
        /// Zeitpunkt des letzten Syncs.
        /// </summary>
        [JsonPropertyName("last_sync_at_utc")]
        public DateTime? LastSyncAtUtc { get; set; }

        /// <summary>
        /// Letzte WoW-Build-Version.
        /// </summary>
        [JsonPropertyName("last_wow_build")]
        public string? LastWowBuild { get; set; }

        /// <summary>
        /// Version des Audio-Packs.
        /// </summary>
        [JsonPropertyName("audio_pack_version")]
        public string AudioPackVersion { get; set; } = "1.0.0";

        /// <summary>
        /// Anzahl der vertonten Quests.
        /// </summary>
        [JsonPropertyName("total_quests_voiced")]
        public int TotalQuestsVoiced { get; set; }

        /// <summary>
        /// Sprachcode.
        /// </summary>
        [JsonPropertyName("language_code")]
        public string LanguageCode { get; set; } = "deDE";

        /// <summary>
        /// Formatierte Anzeige des letzten Syncs.
        /// </summary>
        [JsonIgnore]
        public string LastSyncDisplay => LastSyncAtUtc.HasValue
            ? LastSyncAtUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
            : "Noch nie";

        /// <summary>
        /// Formatierte Anzeige der DataVersion.
        /// </summary>
        [JsonIgnore]
        public string DataVersionDisplay => string.IsNullOrEmpty(LastDataVersion)
            ? "Kein Snapshot vorhanden"
            : LastDataVersion;
    }
}
