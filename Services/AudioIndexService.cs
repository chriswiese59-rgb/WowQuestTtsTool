using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Interner Index-Eintrag fuer den AudioIndexService.
    /// Speichert Informationen ueber vorhandene Audio-Dateien fuer eine Quest-ID.
    /// </summary>
    public class AudioIndexEntry
    {
        /// <summary>
        /// Die eindeutige Quest-ID.
        /// </summary>
        public int QuestId { get; set; }

        /// <summary>
        /// Gibt an, ob eine Audio-Datei mit maennlicher Stimme existiert.
        /// </summary>
        public bool HasMaleAudio { get; set; }

        /// <summary>
        /// Gibt an, ob eine Audio-Datei mit weiblicher Stimme existiert.
        /// </summary>
        public bool HasFemaleAudio { get; set; }

        /// <summary>
        /// Gibt an, ob eine Audio-Datei mit neutraler Stimme existiert.
        /// </summary>
        public bool HasNeutralAudio { get; set; }

        /// <summary>
        /// Vollstaendiger Pfad zur maennlichen Audio-Datei (null wenn nicht vorhanden).
        /// </summary>
        public string? MaleAudioPath { get; set; }

        /// <summary>
        /// Vollstaendiger Pfad zur weiblichen Audio-Datei (null wenn nicht vorhanden).
        /// </summary>
        public string? FemaleAudioPath { get; set; }

        /// <summary>
        /// Vollstaendiger Pfad zur neutralen Audio-Datei (null wenn nicht vorhanden).
        /// </summary>
        public string? NeutralAudioPath { get; set; }

        /// <summary>
        /// Zeitstempel der neuesten Audio-Datei dieser Quest.
        /// </summary>
        public DateTime LastModified { get; set; }

        /// <summary>
        /// Extrahierter Zonenordner (fuer Statistiken/Debugging).
        /// </summary>
        public string? Zone { get; set; }

        /// <summary>
        /// Kategorie-Ordner (Main/Side/Group).
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Gibt an, ob mindestens eine Audio-Variante vorhanden ist.
        /// </summary>
        public bool HasAnyAudio => HasMaleAudio || HasFemaleAudio || HasNeutralAudio;

        /// <summary>
        /// Anzahl der vorhandenen Audio-Varianten (0-3).
        /// </summary>
        public int AudioVariantCount
        {
            get
            {
                int count = 0;
                if (HasMaleAudio) count++;
                if (HasFemaleAudio) count++;
                if (HasNeutralAudio) count++;
                return count;
            }
        }

        /// <summary>
        /// Aktualisiert den Eintrag mit einer gefundenen Audio-Datei.
        /// </summary>
        public void AddAudioFile(string audioPath, string gender, string? zone, string? category, DateTime lastModified)
        {
            switch (gender.ToLowerInvariant())
            {
                case "male":
                    HasMaleAudio = true;
                    MaleAudioPath = audioPath;
                    break;
                case "female":
                    HasFemaleAudio = true;
                    FemaleAudioPath = audioPath;
                    break;
                default:
                    HasNeutralAudio = true;
                    NeutralAudioPath = audioPath;
                    break;
            }

            // Zone und Category nur setzen wenn noch nicht gesetzt
            if (string.IsNullOrEmpty(Zone))
                Zone = zone;
            if (string.IsNullOrEmpty(Category))
                Category = category;

            // Neuesten Zeitstempel behalten
            if (lastModified > LastModified)
                LastModified = lastModified;
        }
    }

    /// <summary>
    /// Zentraler Service fuer die Verwaltung des Audio-Index.
    /// Scannt den Audio-Ordner und verwaltet einen Index aller vorhandenen Quest-Audio-Dateien.
    /// Singleton-Pattern analog zu TtsExportSettings und ProjectService.
    /// </summary>
    public class AudioIndexService
    {
        private static AudioIndexService? _instance;
        private static readonly object s_lock = new();

        /// <summary>
        /// Singleton-Instanz des AudioIndexService.
        /// </summary>
        public static AudioIndexService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (s_lock)
                    {
                        _instance ??= new AudioIndexService();
                    }
                }
                return _instance;
            }
        }

        // Interner Index: QuestId -> AudioIndexEntry
        private readonly ConcurrentDictionary<int, AudioIndexEntry> _audioIndex = new();

        /// <summary>
        /// Event das ausgeloest wird, wenn der Index aktualisiert wurde.
        /// </summary>
        public event EventHandler? IndexUpdated;

        /// <summary>
        /// Event das ausgeloest wird waehrend des Scans mit Fortschrittsinformationen.
        /// Parameter: (int filesScanned, int questsFound)
        /// </summary>
        public event EventHandler<(int FilesScanned, int QuestsFound)>? ScanProgressChanged;

        /// <summary>
        /// Gibt an, ob gerade ein Scan laeuft.
        /// </summary>
        public bool IsScanning { get; private set; }

        /// <summary>
        /// Zeitpunkt des letzten erfolgreichen Scans.
        /// </summary>
        public DateTime? LastScanTime { get; private set; }

        /// <summary>
        /// Anzahl der Quests im Index.
        /// </summary>
        public int TotalIndexedQuests => _audioIndex.Count;

        /// <summary>
        /// Gesamtzahl der indizierten Audio-Dateien (alle Varianten).
        /// </summary>
        public int TotalIndexedAudioFiles => _audioIndex.Values.Sum(e => e.AudioVariantCount);

        /// <summary>
        /// Der aktuell verwendete Basis-Pfad.
        /// </summary>
        public string AudioRootPath => TtsExportSettings.Instance.OutputRootPath;

        private AudioIndexService()
        {
        }

        /// <summary>
        /// Fuehrt einen vollstaendigen Scan des Audio-Ordners durch.
        /// Loescht den bestehenden Index und baut ihn neu auf.
        /// </summary>
        /// <returns>Anzahl der gefundenen Quest-Audios</returns>
        public async Task<int> ScanAudioFolderAsync()
        {
            if (IsScanning)
            {
                System.Diagnostics.Debug.WriteLine("AudioIndexService: Scan bereits aktiv, uebersprungen.");
                return TotalIndexedQuests;
            }

            IsScanning = true;
            var rootPath = TtsExportSettings.Instance.OutputRootPath;

            try
            {
                // Index leeren
                _audioIndex.Clear();

                if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
                {
                    System.Diagnostics.Debug.WriteLine($"AudioIndexService: Ordner nicht gefunden: {rootPath}");
                    return 0;
                }

                // Alle MP3-Dateien finden
                var audioFiles = await Task.Run(() =>
                {
                    try
                    {
                        return Directory.EnumerateFiles(rootPath, "*.mp3", SearchOption.AllDirectories).ToList();
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"AudioIndexService: Fehler beim Scannen: {ex.Message}");
                        return new List<string>();
                    }
                });

                int filesScanned = 0;

                int skippedFiles = 0;
                foreach (var filePath in audioFiles)
                {
                    filesScanned++;

                    // Quest-ID aus Dateinamen extrahieren
                    var fileName = Path.GetFileName(filePath);
                    var questId = AudioPathHelper.ExtractQuestIdFromFileName(fileName);

                    if (!questId.HasValue)
                    {
                        // Datei entspricht nicht dem Namensschema, ueberspringen
                        skippedFiles++;
                        if (skippedFiles <= 5)
                        {
                            System.Diagnostics.Debug.WriteLine($"AudioIndexService: Uebersprungen (kein Quest_-Format): {fileName}");
                        }
                        continue;
                    }

                    // Relative Pfadinfos parsen
                    var relativePath = GetRelativePath(rootPath, filePath);
                    var (zone, category, gender) = AudioPathHelper.ParsePathInfo(relativePath);

                    // Datei-Zeitstempel
                    var fileInfo = new FileInfo(filePath);
                    var lastModified = fileInfo.LastWriteTime;

                    // Eintrag erstellen oder aktualisieren
                    var entry = _audioIndex.GetOrAdd(questId.Value, _ => new AudioIndexEntry { QuestId = questId.Value });
                    entry.AddAudioFile(filePath, gender, zone, category, lastModified);

                    // Fortschritt melden (alle 100 Dateien)
                    if (filesScanned % 100 == 0)
                    {
                        ScanProgressChanged?.Invoke(this, (filesScanned, _audioIndex.Count));
                    }
                }

                LastScanTime = DateTime.Now;
                System.Diagnostics.Debug.WriteLine($"AudioIndexService: Scan abgeschlossen. {_audioIndex.Count} Quests, {TotalIndexedAudioFiles} Audio-Dateien gefunden. ({skippedFiles} Dateien uebersprungen)");

                // Event ausloesen
                IndexUpdated?.Invoke(this, EventArgs.Empty);

                return _audioIndex.Count;
            }
            finally
            {
                IsScanning = false;
            }
        }

        /// <summary>
        /// Fuegt einen einzelnen Eintrag zum Index hinzu oder aktualisiert ihn.
        /// Wird nach erfolgreichem TTS-Export aufgerufen.
        /// </summary>
        /// <param name="questId">Quest-ID</param>
        /// <param name="audioFilePath">Vollstaendiger Pfad zur Audio-Datei</param>
        public void AddOrUpdateEntry(int questId, string audioFilePath)
        {
            if (!File.Exists(audioFilePath))
                return;

            var rootPath = TtsExportSettings.Instance.OutputRootPath;
            var relativePath = GetRelativePath(rootPath, audioFilePath);
            var (zone, category, gender) = AudioPathHelper.ParsePathInfo(relativePath);

            var fileInfo = new FileInfo(audioFilePath);
            var lastModified = fileInfo.LastWriteTime;

            var entry = _audioIndex.GetOrAdd(questId, _ => new AudioIndexEntry { QuestId = questId });
            entry.AddAudioFile(audioFilePath, gender, zone, category, lastModified);

            System.Diagnostics.Debug.WriteLine($"AudioIndexService: Quest {questId} aktualisiert ({gender})");

            // Event ausloesen
            IndexUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Prueft, ob fuer eine Quest mindestens eine Audio-Datei existiert.
        /// </summary>
        /// <param name="questId">Quest-ID</param>
        /// <returns>True wenn Audio vorhanden</returns>
        public bool HasAudioForQuest(int questId)
        {
            return _audioIndex.TryGetValue(questId, out var entry) && entry.HasAnyAudio;
        }

        /// <summary>
        /// Prueft, ob fuer eine Quest eine maennliche Audio-Datei existiert.
        /// </summary>
        public bool HasMaleAudioForQuest(int questId)
        {
            return _audioIndex.TryGetValue(questId, out var entry) && entry.HasMaleAudio;
        }

        /// <summary>
        /// Prueft, ob fuer eine Quest eine weibliche Audio-Datei existiert.
        /// </summary>
        public bool HasFemaleAudioForQuest(int questId)
        {
            return _audioIndex.TryGetValue(questId, out var entry) && entry.HasFemaleAudio;
        }

        /// <summary>
        /// Gibt den vollstaendigen AudioIndexEntry zurueck.
        /// </summary>
        /// <param name="questId">Quest-ID</param>
        /// <returns>Index-Eintrag oder null wenn nicht vorhanden</returns>
        public AudioIndexEntry? GetAudioEntry(int questId)
        {
            return _audioIndex.TryGetValue(questId, out var entry) ? entry : null;
        }

        /// <summary>
        /// Gibt eine Liste aller Quest-IDs mit Audio zurueck.
        /// </summary>
        public IReadOnlyList<int> GetAllIndexedQuestIds()
        {
            return _audioIndex.Keys.ToList();
        }

        /// <summary>
        /// Gibt alle Index-Eintraege zurueck.
        /// </summary>
        public IReadOnlyCollection<AudioIndexEntry> GetAllEntries()
        {
            return _audioIndex.Values.ToList();
        }

        /// <summary>
        /// Entfernt einen Eintrag aus dem Index.
        /// </summary>
        /// <param name="questId">Quest-ID</param>
        public void RemoveEntry(int questId)
        {
            if (_audioIndex.TryRemove(questId, out _))
            {
                IndexUpdated?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Leert den gesamten Index.
        /// </summary>
        public void ClearIndex()
        {
            _audioIndex.Clear();
            LastScanTime = null;
            IndexUpdated?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Gibt Statistiken ueber den Index zurueck.
        /// </summary>
        public (int TotalQuests, int WithMale, int WithFemale, int WithBoth, int TotalFiles) GetStatistics()
        {
            int totalQuests = _audioIndex.Count;
            int withMale = _audioIndex.Values.Count(e => e.HasMaleAudio);
            int withFemale = _audioIndex.Values.Count(e => e.HasFemaleAudio);
            int withBoth = _audioIndex.Values.Count(e => e.HasMaleAudio && e.HasFemaleAudio);
            int totalFiles = TotalIndexedAudioFiles;

            return (totalQuests, withMale, withFemale, withBoth, totalFiles);
        }

        /// <summary>
        /// Berechnet den relativen Pfad von einem vollstaendigen Pfad.
        /// </summary>
        private static string GetRelativePath(string rootPath, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootPath) || string.IsNullOrWhiteSpace(fullPath))
                return fullPath;

            var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedFull = Path.GetFullPath(fullPath);

            if (normalizedFull.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedFull[(normalizedRoot.Length + 1)..];
            }

            return fullPath;
        }
    }
}
