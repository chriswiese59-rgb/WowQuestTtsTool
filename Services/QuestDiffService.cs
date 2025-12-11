using System;
using System.Collections.Generic;
using System.Linq;
using WowQuestTtsTool.Models;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Service zur Berechnung von Diffs zwischen Quest-Datenständen.
    /// Vergleicht aktuellen Stand mit gespeichertem Snapshot.
    /// </summary>
    public class QuestDiffService
    {
        /// <summary>
        /// Erstellt einen Diff zwischen aktuellen Quests und einem alten Snapshot.
        /// </summary>
        /// <param name="currentQuests">Aktuelle Quest-Daten aus der Quelle</param>
        /// <param name="oldSnapshot">Alter Snapshot (kann null sein für Initial-Scan)</param>
        /// <param name="languageCode">Sprachcode für den Vergleich</param>
        /// <returns>Diff-Ergebnis mit allen Änderungen</returns>
        public QuestDiffResult CreateDiff(
            IEnumerable<Quest> currentQuests,
            QuestSourceSnapshot? oldSnapshot,
            string languageCode = "deDE")
        {
            var result = new QuestDiffResult
            {
                ComputedAtUtc = DateTime.UtcNow,
                OldDataVersion = oldSnapshot?.DataVersion,
                NewDataVersion = QuestSourceSnapshot.GenerateDataVersion()
            };

            // Dictionary für schnellen Lookup des alten Snapshots
            var oldEntriesById = new Dictionary<int, QuestSnapshotEntry>();
            if (oldSnapshot?.Entries != null)
            {
                foreach (var entry in oldSnapshot.Entries)
                {
                    oldEntriesById[entry.QuestId] = entry;
                }
            }

            // HashSet für Tracking welche alten Quests bereits verarbeitet wurden
            var processedOldIds = new HashSet<int>();

            // Alle aktuellen Quests durchgehen
            foreach (var quest in currentQuests)
            {
                // Neuen Snapshot-Eintrag erstellen (mit Hash-Berechnung)
                var currentEntry = QuestSnapshotEntry.FromQuest(quest, languageCode);

                // Prüfen ob Quest im alten Snapshot existiert
                if (oldEntriesById.TryGetValue(quest.QuestId, out var oldEntry))
                {
                    processedOldIds.Add(quest.QuestId);

                    // Hash vergleichen
                    if (oldEntry.ContentHash != currentEntry.ContentHash)
                    {
                        // Quest hat sich geändert
                        result.AllEntries.Add(new QuestDiffEntry
                        {
                            QuestId = quest.QuestId,
                            Title = quest.Title ?? "",
                            Zone = quest.Zone ?? "",
                            Category = quest.Category,
                            DiffType = QuestDiffType.Changed,
                            OldHash = oldEntry.ContentHash,
                            NewHash = currentEntry.ContentHash,
                            IsMainStory = quest.IsMainStory,
                            IsGroupQuest = quest.IsGroupQuest
                        });
                    }
                    else
                    {
                        // Quest unverändert
                        result.AllEntries.Add(new QuestDiffEntry
                        {
                            QuestId = quest.QuestId,
                            Title = quest.Title ?? "",
                            Zone = quest.Zone ?? "",
                            Category = quest.Category,
                            DiffType = QuestDiffType.Unchanged,
                            OldHash = oldEntry.ContentHash,
                            NewHash = currentEntry.ContentHash,
                            IsMainStory = quest.IsMainStory,
                            IsGroupQuest = quest.IsGroupQuest
                        });
                    }
                }
                else
                {
                    // Neue Quest (nicht im alten Snapshot)
                    result.AllEntries.Add(new QuestDiffEntry
                    {
                        QuestId = quest.QuestId,
                        Title = quest.Title ?? "",
                        Zone = quest.Zone ?? "",
                        Category = quest.Category,
                        DiffType = QuestDiffType.New,
                        OldHash = null,
                        NewHash = currentEntry.ContentHash,
                        IsMainStory = quest.IsMainStory,
                        IsGroupQuest = quest.IsGroupQuest
                    });
                }
            }

            // Entfernte Quests finden (im alten Snapshot, aber nicht mehr in aktuellen Daten)
            if (oldSnapshot?.Entries != null)
            {
                foreach (var oldEntry in oldSnapshot.Entries)
                {
                    if (!processedOldIds.Contains(oldEntry.QuestId))
                    {
                        result.AllEntries.Add(new QuestDiffEntry
                        {
                            QuestId = oldEntry.QuestId,
                            Title = oldEntry.Title,
                            Zone = oldEntry.Zone,
                            Category = oldEntry.Category,
                            DiffType = QuestDiffType.Removed,
                            OldHash = oldEntry.ContentHash,
                            NewHash = null,
                            IsMainStory = oldEntry.IsMainStory,
                            IsGroupQuest = oldEntry.IsGroupQuest
                        });
                    }
                }
            }

            // Sortieren: Neue zuerst, dann Geänderte, dann Entfernte, dann Unveränderte
            result.AllEntries = result.AllEntries
                .OrderBy(e => e.DiffType switch
                {
                    QuestDiffType.New => 0,
                    QuestDiffType.Changed => 1,
                    QuestDiffType.Removed => 2,
                    QuestDiffType.Unchanged => 3,
                    _ => 4
                })
                .ThenBy(e => e.Zone)
                .ThenBy(e => e.QuestId)
                .ToList();

            return result;
        }

        /// <summary>
        /// Erstellt einen neuen Snapshot aus den aktuellen Quests.
        /// </summary>
        /// <param name="quests">Aktuelle Quest-Daten</param>
        /// <param name="languageCode">Sprachcode</param>
        /// <param name="wowBuild">WoW-Build-Version (optional)</param>
        /// <returns>Neuer Snapshot</returns>
        public QuestSourceSnapshot CreateSnapshot(
            IEnumerable<Quest> quests,
            string languageCode = "deDE",
            string? wowBuild = null)
        {
            var snapshot = new QuestSourceSnapshot
            {
                DataVersion = QuestSourceSnapshot.GenerateDataVersion(),
                WowBuild = wowBuild,
                CreatedAtUtc = DateTime.UtcNow,
                LanguageCode = languageCode
            };

            foreach (var quest in quests)
            {
                var entry = QuestSnapshotEntry.FromQuest(quest, languageCode);
                snapshot.Entries.Add(entry);
            }

            return snapshot;
        }

        /// <summary>
        /// Filtert die Diff-Einträge nach bestimmten Kriterien.
        /// </summary>
        /// <param name="diff">Das Diff-Ergebnis</param>
        /// <param name="includeNew">Neue Quests einbeziehen</param>
        /// <param name="includeChanged">Geänderte Quests einbeziehen</param>
        /// <param name="includeRemoved">Entfernte Quests einbeziehen</param>
        /// <param name="includeUnchanged">Unveränderte Quests einbeziehen</param>
        /// <param name="onlyMainQuests">Nur Hauptquests</param>
        /// <param name="zones">Nur bestimmte Zonen (null = alle)</param>
        /// <returns>Gefilterte Liste der Diff-Einträge</returns>
        public IEnumerable<QuestDiffEntry> FilterDiff(
            QuestDiffResult diff,
            bool includeNew = true,
            bool includeChanged = true,
            bool includeRemoved = false,
            bool includeUnchanged = false,
            bool onlyMainQuests = false,
            IEnumerable<string>? zones = null)
        {
            var result = diff.AllEntries.AsEnumerable();

            // Nach DiffType filtern
            result = result.Where(e =>
                (includeNew && e.DiffType == QuestDiffType.New) ||
                (includeChanged && e.DiffType == QuestDiffType.Changed) ||
                (includeRemoved && e.DiffType == QuestDiffType.Removed) ||
                (includeUnchanged && e.DiffType == QuestDiffType.Unchanged));

            // Nur Hauptquests
            if (onlyMainQuests)
            {
                result = result.Where(e => e.IsMainStory);
            }

            // Nach Zonen filtern
            if (zones != null)
            {
                var zoneSet = zones.ToHashSet(StringComparer.OrdinalIgnoreCase);
                result = result.Where(e => zoneSet.Contains(e.Zone));
            }

            return result;
        }

        /// <summary>
        /// Gibt eine Liste aller Zonen aus dem Diff zurück.
        /// </summary>
        /// <param name="diff">Das Diff-Ergebnis</param>
        /// <returns>Sortierte Liste der Zonen</returns>
        public IEnumerable<string> GetZonesFromDiff(QuestDiffResult diff)
        {
            return diff.AllEntries
                .Select(e => e.Zone)
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(z => z);
        }

        /// <summary>
        /// Berechnet Statistiken pro Zone.
        /// </summary>
        /// <param name="diff">Das Diff-Ergebnis</param>
        /// <returns>Dictionary mit Zone -> (Neu, Geändert, Entfernt)</returns>
        public Dictionary<string, (int New, int Changed, int Removed)> GetZoneStatistics(QuestDiffResult diff)
        {
            var stats = new Dictionary<string, (int New, int Changed, int Removed)>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in diff.AllEntries.GroupBy(e => e.Zone ?? "Unknown"))
            {
                var newCount = group.Count(e => e.DiffType == QuestDiffType.New);
                var changedCount = group.Count(e => e.DiffType == QuestDiffType.Changed);
                var removedCount = group.Count(e => e.DiffType == QuestDiffType.Removed);

                stats[group.Key] = (newCount, changedCount, removedCount);
            }

            return stats;
        }
    }
}
