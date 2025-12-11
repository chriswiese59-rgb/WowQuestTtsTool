using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WowQuestTtsTool.Models;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Fortschritts-Information für Update-Operationen.
    /// </summary>
    public class UpdateSyncProgress
    {
        public string Phase { get; set; } = "";
        public string Message { get; set; } = "";
        public int Current { get; set; }
        public int Total { get; set; }
        public double Percentage => Total > 0 ? (Current * 100.0 / Total) : 0;

        public UpdateSyncProgress() { }

        public UpdateSyncProgress(string phase, string message, int current = 0, int total = 0)
        {
            Phase = phase;
            Message = message;
            Current = current;
            Total = total;
        }
    }

    /// <summary>
    /// Service zur Orchestrierung von Quest-Updates und Synchronisation.
    /// Koordiniert Scan, TTS-Generierung und Addon-Export.
    /// </summary>
    public class UpdateSyncService
    {
        private readonly QuestSourceSnapshotRepository _snapshotRepo;
        private readonly QuestDiffService _diffService;
        private readonly string _snapshotFolder;
        private readonly string _audioOutputFolder;
        private readonly string _languageCode;

        // Delegates für TTS-Generierung und Addon-Export (werden vom MainWindow injiziert)
        public Func<Quest, string, CancellationToken, Task<bool>>? GenerateTtsForQuestAsync { get; set; }
        public Func<CancellationToken, Task<bool>>? ExportAddonAsync { get; set; }

        /// <summary>
        /// Erstellt eine neue Instanz des UpdateSyncService.
        /// </summary>
        /// <param name="snapshotFolder">Ordner für Snapshots</param>
        /// <param name="audioOutputFolder">Ordner für Audio-Dateien</param>
        /// <param name="languageCode">Sprachcode</param>
        public UpdateSyncService(
            string snapshotFolder,
            string audioOutputFolder,
            string languageCode = "deDE")
        {
            _snapshotRepo = new QuestSourceSnapshotRepository();
            _diffService = new QuestDiffService();
            _snapshotFolder = snapshotFolder;
            _audioOutputFolder = audioOutputFolder;
            _languageCode = languageCode;
        }

        /// <summary>
        /// Führt einen Scan durch: Lädt aktuelle Daten, vergleicht mit Snapshot, erstellt Diff.
        /// </summary>
        /// <param name="questSource">Quelle für aktuelle Quest-Daten</param>
        /// <param name="progress">Fortschritts-Callback</param>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <returns>Scan-Ergebnis mit Diff und neuem Snapshot</returns>
        public async Task<UpdateScanResult> ScanAsync(
            IQuestSource questSource,
            IProgress<UpdateSyncProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new UpdateScanResult();
            var startTime = DateTime.Now;

            try
            {
                // Phase 1: Alten Snapshot laden
                progress?.Report(new UpdateSyncProgress("Snapshot laden", "Lade letzten Quest-Snapshot...", 0, 3));

                var oldSnapshot = _snapshotRepo.LoadLastSnapshot(_snapshotFolder);
                result.OldSnapshot = oldSnapshot;

                if (oldSnapshot != null)
                {
                    progress?.Report(new UpdateSyncProgress("Snapshot laden",
                        $"Snapshot gefunden: {oldSnapshot.DataVersion} ({oldSnapshot.QuestCount} Quests)", 1, 3));
                }
                else
                {
                    progress?.Report(new UpdateSyncProgress("Snapshot laden",
                        "Kein vorheriger Snapshot gefunden. Alle Quests werden als 'Neu' markiert.", 1, 3));
                }

                cancellationToken.ThrowIfCancellationRequested();

                // Phase 2: Aktuelle Quests laden
                progress?.Report(new UpdateSyncProgress("Quests laden", "Lade aktuelle Quest-Daten...", 1, 3));

                var quests = await questSource.LoadAllQuestsAsync(_languageCode, cancellationToken);
                var questList = quests.ToList();

                // Quest-Lookup erstellen für späteren Zugriff
                foreach (var quest in questList)
                {
                    result.QuestLookup[quest.QuestId] = quest;
                }

                progress?.Report(new UpdateSyncProgress("Quests laden",
                    $"{questList.Count} Quests geladen.", 2, 3));

                cancellationToken.ThrowIfCancellationRequested();

                // Phase 3: Diff berechnen
                progress?.Report(new UpdateSyncProgress("Diff berechnen", "Berechne Unterschiede...", 2, 3));

                result.Diff = _diffService.CreateDiff(questList, oldSnapshot, _languageCode);
                result.NewSnapshot = _diffService.CreateSnapshot(questList, _languageCode);

                progress?.Report(new UpdateSyncProgress("Diff berechnen",
                    $"Diff berechnet: {result.Diff.Summary}", 3, 3));

                result.Success = true;
                result.Duration = DateTime.Now - startTime;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Scan wurde abgebrochen.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Scan fehlgeschlagen: {ex.Message}";
            }

            result.Duration = DateTime.Now - startTime;
            return result;
        }

        /// <summary>
        /// Führt einen Scan mit einer In-Memory Quest-Sammlung durch.
        /// </summary>
        public async Task<UpdateScanResult> ScanWithQuestsAsync(
            IEnumerable<Quest> quests,
            IProgress<UpdateSyncProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var questSource = new InMemoryQuestSource(quests);
            return await ScanAsync(questSource, progress, cancellationToken);
        }

        /// <summary>
        /// Wendet den Diff an: Generiert TTS für neue/geänderte Quests, aktualisiert Index, exportiert Addon.
        /// </summary>
        /// <param name="scanResult">Ergebnis des vorherigen Scans</param>
        /// <param name="onlyNewAndChanged">Nur neue und geänderte Quests vertonen</param>
        /// <param name="autoExportAddon">Addon automatisch neu exportieren</param>
        /// <param name="progress">Fortschritts-Callback</param>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <returns>Ergebnis der Anwendung</returns>
        public async Task<UpdateApplyResult> ApplyTtsForDiffAsync(
            UpdateScanResult scanResult,
            bool onlyNewAndChanged,
            bool autoExportAddon,
            IProgress<UpdateSyncProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new UpdateApplyResult();
            var startTime = DateTime.Now;

            if (GenerateTtsForQuestAsync == null)
            {
                result.Success = false;
                result.ErrorMessage = "TTS-Generierung nicht konfiguriert.";
                return result;
            }

            try
            {
                // Quests zum Vertonen auswählen
                var questsToVoice = onlyNewAndChanged
                    ? scanResult.Diff.NewAndChangedEntries.ToList()
                    : scanResult.Diff.AllEntries
                        .Where(e => e.DiffType != QuestDiffType.Removed)
                        .ToList();

                if (questsToVoice.Count == 0)
                {
                    progress?.Report(new UpdateSyncProgress("TTS", "Keine Quests zum Vertonen.", 0, 0));
                    result.Success = true;
                    result.QuestsSkipped = scanResult.Diff.UnchangedCount;
                    return result;
                }

                var totalQuests = questsToVoice.Count;
                var processed = 0;

                progress?.Report(new UpdateSyncProgress("TTS",
                    $"Vertone {totalQuests} Quests...", 0, totalQuests));

                // TTS für jede Quest generieren
                foreach (var diffEntry in questsToVoice)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Quest aus Lookup holen
                    if (!scanResult.QuestLookup.TryGetValue(diffEntry.QuestId, out var quest))
                    {
                        result.QuestsSkipped++;
                        continue;
                    }

                    progress?.Report(new UpdateSyncProgress("TTS",
                        $"[{processed + 1}/{totalQuests}] Quest {quest.QuestId}: {quest.Title}",
                        processed, totalQuests));

                    try
                    {
                        // TTS generieren (Male + Female wird vom Delegate gehandhabt)
                        var success = await GenerateTtsForQuestAsync(quest, _languageCode, cancellationToken);

                        if (success)
                        {
                            result.QuestsVoiced++;
                        }
                        else
                        {
                            result.QuestsFailed++;
                            result.FailedQuests.Add((quest.QuestId, "TTS-Generierung fehlgeschlagen"));
                        }
                    }
                    catch (Exception ex)
                    {
                        result.QuestsFailed++;
                        result.FailedQuests.Add((quest.QuestId, ex.Message));
                    }

                    processed++;
                }

                progress?.Report(new UpdateSyncProgress("TTS",
                    $"TTS abgeschlossen: {result.QuestsVoiced} erfolgreich, {result.QuestsFailed} Fehler",
                    totalQuests, totalQuests));

                cancellationToken.ThrowIfCancellationRequested();

                // Snapshot speichern
                progress?.Report(new UpdateSyncProgress("Snapshot speichern",
                    "Speichere neuen Quest-Snapshot...", totalQuests, totalQuests));

                try
                {
                    // Backup des alten Snapshots
                    _snapshotRepo.BackupCurrentSnapshot(_snapshotFolder);

                    // Neuen Snapshot speichern
                    _snapshotRepo.SaveSnapshot(_snapshotFolder, scanResult.NewSnapshot);

                    // Metadaten aktualisieren
                    var metadata = _snapshotRepo.LoadMetadata(_snapshotFolder);
                    metadata.LastDataVersion = scanResult.NewSnapshot.DataVersion;
                    metadata.LastSyncAtUtc = DateTime.UtcNow;
                    metadata.TotalQuestsVoiced = result.QuestsVoiced;
                    metadata.LanguageCode = _languageCode;
                    _snapshotRepo.SaveMetadata(_snapshotFolder, metadata);

                    result.SnapshotSaved = true;
                    result.SavedDataVersion = scanResult.NewSnapshot.DataVersion;
                }
                catch (Exception ex)
                {
                    // Snapshot-Fehler ist nicht kritisch, TTS wurde trotzdem generiert
                    System.Diagnostics.Debug.WriteLine($"Snapshot speichern fehlgeschlagen: {ex.Message}");
                }

                // Addon exportieren (falls gewünscht)
                if (autoExportAddon && ExportAddonAsync != null)
                {
                    progress?.Report(new UpdateSyncProgress("Addon Export",
                        "Exportiere WoW-Addon...", totalQuests, totalQuests));

                    try
                    {
                        var exportSuccess = await ExportAddonAsync(cancellationToken);
                        result.AddonExported = exportSuccess;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Addon-Export fehlgeschlagen: {ex.Message}");
                        result.AddonExported = false;
                    }
                }

                result.Success = true;
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Vertonung wurde abgebrochen.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Vertonung fehlgeschlagen: {ex.Message}";
            }

            result.Duration = DateTime.Now - startTime;
            return result;
        }

        /// <summary>
        /// Lädt die aktuellen Sync-Metadaten.
        /// </summary>
        public SyncMetadata LoadMetadata()
        {
            return _snapshotRepo.LoadMetadata(_snapshotFolder);
        }

        /// <summary>
        /// Speichert aktualisierte Sync-Metadaten.
        /// </summary>
        public void SaveMetadata(SyncMetadata metadata)
        {
            _snapshotRepo.SaveMetadata(_snapshotFolder, metadata);
        }

        /// <summary>
        /// Prüft ob ein Snapshot existiert.
        /// </summary>
        public bool HasSnapshot()
        {
            return _snapshotRepo.SnapshotExists(_snapshotFolder);
        }

        /// <summary>
        /// Lädt den letzten Snapshot.
        /// </summary>
        public QuestSourceSnapshot? LoadLastSnapshot()
        {
            return _snapshotRepo.LoadLastSnapshot(_snapshotFolder);
        }

        /// <summary>
        /// Erstellt einen Initial-Snapshot ohne Diff (für erste Einrichtung).
        /// </summary>
        public void CreateInitialSnapshot(IEnumerable<Quest> quests, string? wowBuild = null)
        {
            var snapshot = _diffService.CreateSnapshot(quests, _languageCode, wowBuild);
            _snapshotRepo.SaveSnapshot(_snapshotFolder, snapshot);

            var metadata = new SyncMetadata
            {
                LastDataVersion = snapshot.DataVersion,
                LastSyncAtUtc = DateTime.UtcNow,
                LastWowBuild = wowBuild,
                LanguageCode = _languageCode,
                TotalQuestsVoiced = 0
            };
            _snapshotRepo.SaveMetadata(_snapshotFolder, metadata);
        }

        /// <summary>
        /// Setzt den Snapshot zurück (löscht alle Snapshot-Daten).
        /// </summary>
        public bool ResetSnapshot()
        {
            return _snapshotRepo.DeleteSnapshot(_snapshotFolder);
        }
    }
}
