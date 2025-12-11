using System;
using System.IO;
using System.Text.Json;
using WowQuestTtsTool.Models;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Repository für Quest-Daten-Snapshots.
    /// Speichert und lädt Snapshots als JSON-Dateien.
    /// </summary>
    public class QuestSourceSnapshotRepository
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private const string SnapshotFileName = "quest_source_snapshot.json";
        private const string MetadataFileName = "sync_metadata.json";

        /// <summary>
        /// Lädt den letzten Snapshot aus dem angegebenen Ordner.
        /// </summary>
        /// <param name="snapshotFolder">Ordner, in dem der Snapshot gespeichert ist</param>
        /// <returns>Der geladene Snapshot oder null, wenn keiner vorhanden ist</returns>
        public QuestSourceSnapshot? LoadLastSnapshot(string snapshotFolder)
        {
            if (string.IsNullOrWhiteSpace(snapshotFolder))
                return null;

            var snapshotPath = Path.Combine(snapshotFolder, SnapshotFileName);

            if (!File.Exists(snapshotPath))
                return null;

            try
            {
                var json = File.ReadAllText(snapshotPath, System.Text.Encoding.UTF8);
                var snapshot = JsonSerializer.Deserialize<QuestSourceSnapshot>(json, s_jsonOptions);
                return snapshot;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden des Snapshots: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Speichert einen Snapshot in den angegebenen Ordner.
        /// </summary>
        /// <param name="snapshotFolder">Zielordner für den Snapshot</param>
        /// <param name="snapshot">Der zu speichernde Snapshot</param>
        public void SaveSnapshot(string snapshotFolder, QuestSourceSnapshot snapshot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshotFolder, nameof(snapshotFolder));
            ArgumentNullException.ThrowIfNull(snapshot, nameof(snapshot));

            // Ordner erstellen falls nicht vorhanden
            if (!Directory.Exists(snapshotFolder))
            {
                Directory.CreateDirectory(snapshotFolder);
            }

            // Snapshot speichern
            var snapshotPath = Path.Combine(snapshotFolder, SnapshotFileName);
            var json = JsonSerializer.Serialize(snapshot, s_jsonOptions);
            File.WriteAllText(snapshotPath, json, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Erstellt eine Backup-Kopie des aktuellen Snapshots.
        /// </summary>
        /// <param name="snapshotFolder">Ordner mit dem Snapshot</param>
        /// <returns>Pfad zur Backup-Datei oder null wenn kein Snapshot vorhanden</returns>
        public string? BackupCurrentSnapshot(string snapshotFolder)
        {
            if (string.IsNullOrWhiteSpace(snapshotFolder))
                return null;

            var snapshotPath = Path.Combine(snapshotFolder, SnapshotFileName);

            if (!File.Exists(snapshotPath))
                return null;

            try
            {
                var backupFileName = $"quest_source_snapshot_backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                var backupPath = Path.Combine(snapshotFolder, "backups", backupFileName);

                var backupFolder = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrEmpty(backupFolder) && !Directory.Exists(backupFolder))
                {
                    Directory.CreateDirectory(backupFolder);
                }

                File.Copy(snapshotPath, backupPath, overwrite: false);
                return backupPath;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Backup: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Lädt die Sync-Metadaten aus dem angegebenen Ordner.
        /// </summary>
        /// <param name="snapshotFolder">Ordner mit den Metadaten</param>
        /// <returns>Die geladenen Metadaten oder neue Standardwerte</returns>
        public SyncMetadata LoadMetadata(string snapshotFolder)
        {
            if (string.IsNullOrWhiteSpace(snapshotFolder))
                return new SyncMetadata();

            var metadataPath = Path.Combine(snapshotFolder, MetadataFileName);

            if (!File.Exists(metadataPath))
                return new SyncMetadata();

            try
            {
                var json = File.ReadAllText(metadataPath, System.Text.Encoding.UTF8);
                var metadata = JsonSerializer.Deserialize<SyncMetadata>(json, s_jsonOptions);
                return metadata ?? new SyncMetadata();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der Metadaten: {ex.Message}");
                return new SyncMetadata();
            }
        }

        /// <summary>
        /// Speichert die Sync-Metadaten in den angegebenen Ordner.
        /// </summary>
        /// <param name="snapshotFolder">Zielordner für die Metadaten</param>
        /// <param name="metadata">Die zu speichernden Metadaten</param>
        public void SaveMetadata(string snapshotFolder, SyncMetadata metadata)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(snapshotFolder, nameof(snapshotFolder));
            ArgumentNullException.ThrowIfNull(metadata, nameof(metadata));

            // Ordner erstellen falls nicht vorhanden
            if (!Directory.Exists(snapshotFolder))
            {
                Directory.CreateDirectory(snapshotFolder);
            }

            var metadataPath = Path.Combine(snapshotFolder, MetadataFileName);
            var json = JsonSerializer.Serialize(metadata, s_jsonOptions);
            File.WriteAllText(metadataPath, json, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Löscht den aktuellen Snapshot (z.B. für kompletten Reset).
        /// </summary>
        /// <param name="snapshotFolder">Ordner mit dem Snapshot</param>
        /// <returns>True wenn erfolgreich gelöscht</returns>
        public bool DeleteSnapshot(string snapshotFolder)
        {
            if (string.IsNullOrWhiteSpace(snapshotFolder))
                return false;

            var snapshotPath = Path.Combine(snapshotFolder, SnapshotFileName);

            if (!File.Exists(snapshotPath))
                return false;

            try
            {
                File.Delete(snapshotPath);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Löschen des Snapshots: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Prüft ob ein Snapshot im angegebenen Ordner existiert.
        /// </summary>
        /// <param name="snapshotFolder">Ordner für die Prüfung</param>
        /// <returns>True wenn ein Snapshot vorhanden ist</returns>
        public bool SnapshotExists(string snapshotFolder)
        {
            if (string.IsNullOrWhiteSpace(snapshotFolder))
                return false;

            var snapshotPath = Path.Combine(snapshotFolder, SnapshotFileName);
            return File.Exists(snapshotPath);
        }

        /// <summary>
        /// Gibt den Standard-Snapshot-Ordner zurück (neben dem Audio-Output).
        /// </summary>
        /// <param name="audioOutputPath">Audio-Output-Pfad</param>
        /// <returns>Pfad zum Snapshot-Ordner</returns>
        public static string GetDefaultSnapshotFolder(string audioOutputPath)
        {
            if (string.IsNullOrWhiteSpace(audioOutputPath))
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "WowQuestTtsTool",
                    "snapshots");
            }

            return Path.Combine(audioOutputPath, "snapshots");
        }
    }
}
