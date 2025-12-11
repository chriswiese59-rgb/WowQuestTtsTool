using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Service fuer die Erkennung neuer/geaenderter Quests zwischen WoW-Patches.
    /// Vergleicht Quest-Datenbanken und identifiziert Aenderungen.
    /// </summary>
    public class QuestPatchDiffService
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// Vergleicht zwei Quest-Listen und identifiziert Aenderungen.
        /// </summary>
        /// <param name="previousQuests">Quests aus dem vorherigen Patch/Snapshot</param>
        /// <param name="currentQuests">Aktuelle Quests</param>
        /// <returns>Diff-Ergebnis mit neuen, geaenderten und entfernten Quests</returns>
        public QuestPatchDiffResult CompareQuests(
            IEnumerable<Quest> previousQuests,
            IEnumerable<Quest> currentQuests)
        {
            var result = new QuestPatchDiffResult();

            var prevDict = previousQuests?.ToDictionary(q => q.QuestId) ?? [];
            var currDict = currentQuests?.ToDictionary(q => q.QuestId) ?? [];

            var prevIds = prevDict.Keys.ToHashSet();
            var currIds = currDict.Keys.ToHashSet();

            // Neue Quests (in aktuell, aber nicht in vorherig)
            var newIds = currIds.Except(prevIds);
            foreach (var id in newIds)
            {
                result.NewQuests.Add(currDict[id]);
            }

            // Entfernte Quests (in vorherig, aber nicht in aktuell)
            var removedIds = prevIds.Except(currIds);
            foreach (var id in removedIds)
            {
                result.RemovedQuests.Add(prevDict[id]);
            }

            // Potentiell geaenderte Quests (in beiden vorhanden)
            var commonIds = prevIds.Intersect(currIds);
            foreach (var id in commonIds)
            {
                var prev = prevDict[id];
                var curr = currDict[id];

                var changeType = DetectQuestChanges(prev, curr);
                if (changeType != QuestChangeType.None)
                {
                    result.ModifiedQuests.Add(new QuestModification
                    {
                        Quest = curr,
                        PreviousQuest = prev,
                        ChangeType = changeType
                    });
                }
                else
                {
                    result.UnchangedQuestIds.Add(id);
                }
            }

            return result;
        }

        /// <summary>
        /// Ermittelt welche Aspekte einer Quest sich geaendert haben.
        /// </summary>
        private static QuestChangeType DetectQuestChanges(Quest previous, Quest current)
        {
            var changes = QuestChangeType.None;

            // Titel geaendert
            if (!string.Equals(previous.Title, current.Title, StringComparison.Ordinal))
            {
                changes |= QuestChangeType.TitleChanged;
            }

            // Beschreibung geaendert
            if (!string.Equals(previous.Description, current.Description, StringComparison.Ordinal))
            {
                changes |= QuestChangeType.DescriptionChanged;
            }

            // Ziele geaendert
            if (!string.Equals(previous.Objectives, current.Objectives, StringComparison.Ordinal))
            {
                changes |= QuestChangeType.ObjectiveChanged;
            }

            // Abschluss-Text geaendert
            if (!string.Equals(previous.Completion, current.Completion, StringComparison.Ordinal))
            {
                changes |= QuestChangeType.CompletionChanged;
            }

            // Zone geaendert
            if (!string.Equals(previous.Zone, current.Zone, StringComparison.Ordinal))
            {
                changes |= QuestChangeType.ZoneChanged;
            }

            return changes;
        }

        /// <summary>
        /// Filtert Quests die neu vertont werden muessen (neue + textlich geaenderte).
        /// </summary>
        public List<Quest> GetQuestsRequiringVoicing(
            QuestPatchDiffResult diff,
            Dictionary<string, QuestAudioIndexEntry>? existingAudioLookup = null)
        {
            var result = new List<Quest>();

            // Alle neuen Quests brauchen Vertonung
            result.AddRange(diff.NewQuests);

            // Geaenderte Quests mit Text-Aenderungen brauchen neue Vertonung
            foreach (var mod in diff.ModifiedQuests)
            {
                // Nur Text-Aenderungen erfordern neue Vertonung
                var textChanges = mod.ChangeType & (
                    QuestChangeType.TitleChanged |
                    QuestChangeType.DescriptionChanged |
                    QuestChangeType.ObjectiveChanged |
                    QuestChangeType.CompletionChanged);

                if (textChanges != QuestChangeType.None)
                {
                    result.Add(mod.Quest);
                }
            }

            // Optional: Quests ohne existierendes Audio hinzufuegen
            if (existingAudioLookup != null)
            {
                foreach (var id in diff.UnchangedQuestIds)
                {
                    var maleKey = AudioIndexWriter.GetLookupKey(id, "male");
                    var femaleKey = AudioIndexWriter.GetLookupKey(id, "female");

                    // Quest hat noch kein Audio (weder male noch female)
                    if (!existingAudioLookup.ContainsKey(maleKey) && !existingAudioLookup.ContainsKey(femaleKey))
                    {
                        // Quest aus currentQuests holen wuerde hier benoetigt
                        // Diese Methode sollte mit der aktuellen Quest-Liste aufgerufen werden
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Speichert einen Quest-Snapshot fuer spaeteren Vergleich.
        /// </summary>
        public void SaveQuestSnapshot(
            IEnumerable<Quest> quests,
            string snapshotPath,
            string? patchVersion = null)
        {
            var snapshot = new QuestSnapshot
            {
                CreatedAtUtc = DateTime.UtcNow,
                PatchVersion = patchVersion ?? "unknown",
                TotalQuests = quests.Count(),
                Quests = quests.Select(q => new PatchQuestSnapshotEntry
                {
                    QuestId = q.QuestId,
                    Title = q.Title ?? "",
                    Description = q.Description ?? "",
                    Objective = q.Objectives ?? "",
                    Completion = q.Completion ?? "",
                    Zone = q.Zone ?? "",
                    Category = q.CategoryShortName,
                    IsMainStory = q.IsMainStory,
                    TitleHash = ComputeSimpleHash(q.Title),
                    ContentHash = ComputeContentHash(q)
                }).ToList()
            };

            var directory = Path.GetDirectoryName(snapshotPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            File.WriteAllText(snapshotPath, json, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Laedt einen Quest-Snapshot.
        /// </summary>
        public QuestSnapshot? LoadQuestSnapshot(string snapshotPath)
        {
            if (!File.Exists(snapshotPath))
                return null;

            try
            {
                var json = File.ReadAllText(snapshotPath);
                return JsonSerializer.Deserialize<QuestSnapshot>(json);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Konvertiert Snapshot-Eintraege zurueck zu Quest-Objekten (fuer Vergleich).
        /// </summary>
        public List<Quest> SnapshotToQuests(QuestSnapshot snapshot)
        {
            if (snapshot?.Quests == null)
                return [];

            return snapshot.Quests.Select(e => new Quest
            {
                QuestId = e.QuestId,
                Title = e.Title,
                Description = e.Description,
                Objectives = e.Objective,
                Completion = e.Completion,
                Zone = e.Zone,
                IsMainStory = e.IsMainStory
            }).ToList();
        }

        /// <summary>
        /// Berechnet einen einfachen Hash fuer einen String.
        /// </summary>
        private static string ComputeSimpleHash(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            // Einfacher Hash basierend auf String-Inhalt
            var hash = 0;
            foreach (var c in input)
            {
                hash = ((hash << 5) - hash) + c;
                hash = hash & hash; // Convert to 32bit integer
            }

            return hash.ToString("X8");
        }

        /// <summary>
        /// Berechnet einen Hash ueber den gesamten Quest-Inhalt.
        /// </summary>
        private static string ComputeContentHash(Quest quest)
        {
            var content = string.Join("|",
                quest.Title ?? "",
                quest.Description ?? "",
                quest.Objectives ?? "",
                quest.Completion ?? "");

            return ComputeSimpleHash(content);
        }
    }

    #region Data Classes

    /// <summary>
    /// Ergebnis eines Quest-Patch-Vergleichs.
    /// </summary>
    public class QuestPatchDiffResult
    {
        /// <summary>
        /// Komplett neue Quests.
        /// </summary>
        public List<Quest> NewQuests { get; set; } = [];

        /// <summary>
        /// Entfernte Quests (nicht mehr vorhanden).
        /// </summary>
        public List<Quest> RemovedQuests { get; set; } = [];

        /// <summary>
        /// Geaenderte Quests mit Details zur Aenderung.
        /// </summary>
        public List<QuestModification> ModifiedQuests { get; set; } = [];

        /// <summary>
        /// IDs von ungeaenderten Quests.
        /// </summary>
        public List<int> UnchangedQuestIds { get; set; } = [];

        /// <summary>
        /// Anzahl neuer Quests.
        /// </summary>
        public int NewCount => NewQuests.Count;

        /// <summary>
        /// Anzahl entfernter Quests.
        /// </summary>
        public int RemovedCount => RemovedQuests.Count;

        /// <summary>
        /// Anzahl geaenderter Quests.
        /// </summary>
        public int ModifiedCount => ModifiedQuests.Count;

        /// <summary>
        /// Anzahl ungeaenderter Quests.
        /// </summary>
        public int UnchangedCount => UnchangedQuestIds.Count;

        /// <summary>
        /// Ob es ueberhaupt Aenderungen gibt.
        /// </summary>
        public bool HasChanges => NewCount > 0 || RemovedCount > 0 || ModifiedCount > 0;

        /// <summary>
        /// Zusammenfassung als String.
        /// </summary>
        public string Summary => $"Neu: {NewCount}, Geaendert: {ModifiedCount}, Entfernt: {RemovedCount}, Unveraendert: {UnchangedCount}";
    }

    /// <summary>
    /// Details zu einer Quest-Modifikation.
    /// </summary>
    public class QuestModification
    {
        /// <summary>
        /// Die aktuelle (neue) Version der Quest.
        /// </summary>
        public Quest Quest { get; set; } = new();

        /// <summary>
        /// Die vorherige Version der Quest.
        /// </summary>
        public Quest PreviousQuest { get; set; } = new();

        /// <summary>
        /// Art der Aenderung(en).
        /// </summary>
        public QuestChangeType ChangeType { get; set; }
    }

    /// <summary>
    /// Arten von Quest-Aenderungen (Flags).
    /// </summary>
    [Flags]
    public enum QuestChangeType
    {
        None = 0,
        TitleChanged = 1,
        DescriptionChanged = 2,
        ObjectiveChanged = 4,
        ProgressChanged = 8,
        CompletionChanged = 16,
        ZoneChanged = 32,
        CategoryChanged = 64
    }

    /// <summary>
    /// Snapshot einer Quest-Datenbank fuer Patch-Vergleiche.
    /// </summary>
    public class QuestSnapshot
    {
        [System.Text.Json.Serialization.JsonPropertyName("created_at_utc")]
        public DateTime CreatedAtUtc { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("patch_version")]
        public string PatchVersion { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("total_quests")]
        public int TotalQuests { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("quests")]
        public List<PatchQuestSnapshotEntry> Quests { get; set; } = [];
    }

    /// <summary>
    /// Einzelner Quest-Eintrag im Patch-Diff-Snapshot.
    /// </summary>
    public class PatchQuestSnapshotEntry
    {
        [System.Text.Json.Serialization.JsonPropertyName("quest_id")]
        public int QuestId { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("objectives")]
        public string Objective { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("completion")]
        public string Completion { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("zone")]
        public string Zone { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("category")]
        public string Category { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("is_main_story")]
        public bool IsMainStory { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("title_hash")]
        public string TitleHash { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("content_hash")]
        public string ContentHash { get; set; } = "";
    }

    #endregion
}
