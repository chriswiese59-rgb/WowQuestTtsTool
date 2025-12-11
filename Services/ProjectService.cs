using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Projekt-Datenmodell - speichert alle Quest-Metadaten und Einstellungen.
    /// </summary>
    public class ProjectData
    {
        [JsonPropertyName("version")]
        public string Version { get; set; } = "1.0";

        [JsonPropertyName("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("last_modified")]
        public DateTime LastModified { get; set; } = DateTime.Now;

        [JsonPropertyName("quest_count")]
        public int QuestCount { get; set; }

        [JsonPropertyName("quests")]
        public Dictionary<int, QuestMetadata> Quests { get; set; } = [];

        [JsonPropertyName("settings")]
        public ProjectSettings Settings { get; set; } = new();

        [JsonPropertyName("statistics")]
        public ProjectStatistics Statistics { get; set; } = new();
    }

    /// <summary>
    /// Quest-spezifische Metadaten, die im Projekt gespeichert werden.
    /// </summary>
    public class QuestMetadata
    {
        [JsonPropertyName("quest_id")]
        public int QuestId { get; set; }

        [JsonPropertyName("category")]
        public QuestCategory Category { get; set; }

        [JsonPropertyName("has_male_tts")]
        public bool HasMaleTts { get; set; }

        [JsonPropertyName("has_female_tts")]
        public bool HasFemaleTts { get; set; }

        [JsonPropertyName("tts_reviewed")]
        public bool TtsReviewed { get; set; }

        [JsonPropertyName("last_tts_generated_at")]
        public DateTime? LastTtsGeneratedAt { get; set; }

        [JsonPropertyName("last_tts_error")]
        public string? LastTtsError { get; set; }

        [JsonPropertyName("last_tts_error_at")]
        public DateTime? LastTtsErrorAt { get; set; }

        [JsonPropertyName("tts_error_count")]
        public int TtsErrorCount { get; set; }

        [JsonPropertyName("custom_tts_text")]
        public string? CustomTtsText { get; set; }

        [JsonPropertyName("notes")]
        public string? Notes { get; set; }

        [JsonPropertyName("is_excluded")]
        public bool IsExcluded { get; set; }

        /// <summary>
        /// Erstellt Metadaten aus einem Quest-Objekt.
        /// </summary>
        public static QuestMetadata FromQuest(Quest quest)
        {
            return new QuestMetadata
            {
                QuestId = quest.QuestId,
                Category = quest.Category,
                HasMaleTts = quest.HasMaleTts,
                HasFemaleTts = quest.HasFemaleTts,
                TtsReviewed = quest.TtsReviewed,
                LastTtsGeneratedAt = quest.LastTtsGeneratedAt,
                LastTtsError = quest.LastTtsError,
                LastTtsErrorAt = quest.LastTtsErrorAt,
                TtsErrorCount = quest.TtsErrorCount,
                CustomTtsText = quest.CustomTtsText,
                Notes = null,
                IsExcluded = false
            };
        }

        /// <summary>
        /// Wendet die Metadaten auf ein Quest-Objekt an.
        /// </summary>
        public void ApplyToQuest(Quest quest)
        {
            quest.Category = Category;
            quest.HasMaleTts = HasMaleTts;
            quest.HasFemaleTts = HasFemaleTts;
            quest.TtsReviewed = TtsReviewed;
            quest.LastTtsGeneratedAt = LastTtsGeneratedAt;
            quest.LastTtsError = LastTtsError;
            quest.LastTtsErrorAt = LastTtsErrorAt;
            quest.TtsErrorCount = TtsErrorCount;
            if (!string.IsNullOrEmpty(CustomTtsText))
            {
                quest.CustomTtsText = CustomTtsText;
            }
        }
    }

    /// <summary>
    /// Projekt-Einstellungen.
    /// </summary>
    public class ProjectSettings
    {
        [JsonPropertyName("output_root_path")]
        public string? OutputRootPath { get; set; }

        [JsonPropertyName("language_code")]
        public string LanguageCode { get; set; } = "deDE";

        [JsonPropertyName("male_voice_id")]
        public string? MaleVoiceId { get; set; }

        [JsonPropertyName("female_voice_id")]
        public string? FemaleVoiceId { get; set; }

        [JsonPropertyName("avg_chars_per_token")]
        public double AvgCharsPerToken { get; set; } = 4.0;

        [JsonPropertyName("cost_per_1k_tokens")]
        public decimal CostPer1kTokens { get; set; } = 0.30m;

        [JsonPropertyName("currency_symbol")]
        public string CurrencySymbol { get; set; } = "€";
    }

    /// <summary>
    /// Projekt-Statistiken.
    /// </summary>
    public class ProjectStatistics
    {
        [JsonPropertyName("total_quests")]
        public int TotalQuests { get; set; }

        [JsonPropertyName("quests_with_male_tts")]
        public int QuestsWithMaleTts { get; set; }

        [JsonPropertyName("quests_with_female_tts")]
        public int QuestsWithFemaleTts { get; set; }

        [JsonPropertyName("quests_complete")]
        public int QuestsComplete { get; set; }

        [JsonPropertyName("quests_reviewed")]
        public int QuestsReviewed { get; set; }

        [JsonPropertyName("quests_with_errors")]
        public int QuestsWithErrors { get; set; }

        [JsonPropertyName("total_characters_processed")]
        public long TotalCharactersProcessed { get; set; }

        [JsonPropertyName("estimated_cost")]
        public decimal EstimatedCost { get; set; }

        [JsonPropertyName("last_batch_date")]
        public DateTime? LastBatchDate { get; set; }
    }

    /// <summary>
    /// Service für Projekt-Persistenz.
    /// </summary>
    public class ProjectService
    {
        private static ProjectService? _instance;
        private static readonly object _lock = new();
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

        private readonly string _projectDirectory;
        private readonly string _projectFilePath;
        private readonly string _backupDirectory;
        private ProjectData _currentProject;

        public static ProjectService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ProjectService();
                    }
                }
                return _instance;
            }
        }

        private ProjectService()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _projectDirectory = Path.Combine(baseDir, "project");
            _backupDirectory = Path.Combine(_projectDirectory, "backups");
            _projectFilePath = Path.Combine(_projectDirectory, "project.json");

            EnsureDirectories();
            _currentProject = LoadOrCreateProject();
        }

        private void EnsureDirectories()
        {
            if (!Directory.Exists(_projectDirectory))
                Directory.CreateDirectory(_projectDirectory);

            if (!Directory.Exists(_backupDirectory))
                Directory.CreateDirectory(_backupDirectory);
        }

        /// <summary>
        /// Lädt das Projekt oder erstellt ein neues.
        /// </summary>
        private ProjectData LoadOrCreateProject()
        {
            if (File.Exists(_projectFilePath))
            {
                try
                {
                    var json = File.ReadAllText(_projectFilePath);
                    var project = JsonSerializer.Deserialize<ProjectData>(json);
                    if (project != null)
                    {
                        return project;
                    }
                }
                catch
                {
                    // Bei Ladefehler: Backup erstellen und neues Projekt anlegen
                    CreateBackup("load_error");
                }
            }

            return new ProjectData();
        }

        /// <summary>
        /// Erstellt ein Backup der Projektdatei.
        /// </summary>
        public void CreateBackup(string? suffix = null)
        {
            if (!File.Exists(_projectFilePath))
                return;

            try
            {
                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                var backupName = string.IsNullOrEmpty(suffix)
                    ? $"project_{timestamp}.json"
                    : $"project_{timestamp}_{suffix}.json";

                var backupPath = Path.Combine(_backupDirectory, backupName);
                File.Copy(_projectFilePath, backupPath, overwrite: true);

                // Alte Backups aufräumen (nur die letzten 10 behalten)
                CleanupOldBackups(10);
            }
            catch
            {
                // Backup-Fehler ignorieren
            }
        }

        private void CleanupOldBackups(int keepCount)
        {
            try
            {
                var backups = Directory.GetFiles(_backupDirectory, "project_*.json")
                    .OrderByDescending(f => f)
                    .Skip(keepCount)
                    .ToList();

                foreach (var backup in backups)
                {
                    File.Delete(backup);
                }
            }
            catch
            {
                // Aufräum-Fehler ignorieren
            }
        }

        /// <summary>
        /// Speichert das aktuelle Projekt.
        /// </summary>
        public void Save()
        {
            try
            {
                _currentProject.LastModified = DateTime.Now;
                _currentProject.QuestCount = _currentProject.Quests.Count;

                var json = JsonSerializer.Serialize(_currentProject, s_jsonOptions);
                File.WriteAllText(_projectFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern des Projekts: {ex.Message}");
            }
        }

        /// <summary>
        /// Speichert das Projekt mit vorherigem Backup.
        /// </summary>
        public void SaveWithBackup()
        {
            CreateBackup();
            Save();
        }

        /// <summary>
        /// Aktualisiert die Quest-Metadaten im Projekt.
        /// </summary>
        public void UpdateQuestMetadata(Quest quest)
        {
            _currentProject.Quests[quest.QuestId] = QuestMetadata.FromQuest(quest);
        }

        /// <summary>
        /// Aktualisiert mehrere Quests auf einmal.
        /// </summary>
        public void UpdateQuests(IEnumerable<Quest> quests)
        {
            foreach (var quest in quests)
            {
                _currentProject.Quests[quest.QuestId] = QuestMetadata.FromQuest(quest);
            }
        }

        /// <summary>
        /// Merged Projekt-Daten auf Quest-Liste.
        /// Blizzard-Daten bleiben erhalten, TTS-Status wird aus dem Projekt übernommen.
        /// </summary>
        public void MergeWithQuests(IList<Quest> quests)
        {
            foreach (var quest in quests)
            {
                if (_currentProject.Quests.TryGetValue(quest.QuestId, out var metadata))
                {
                    metadata.ApplyToQuest(quest);
                }
            }
        }

        /// <summary>
        /// Aktualisiert die Projekt-Statistiken.
        /// </summary>
        public void UpdateStatistics(IEnumerable<Quest> quests)
        {
            var questList = quests.ToList();

            _currentProject.Statistics = new ProjectStatistics
            {
                TotalQuests = questList.Count,
                QuestsWithMaleTts = questList.Count(q => q.HasMaleTts),
                QuestsWithFemaleTts = questList.Count(q => q.HasFemaleTts),
                QuestsComplete = questList.Count(q => q.HasMaleTts && q.HasFemaleTts),
                QuestsReviewed = questList.Count(q => q.TtsReviewed),
                QuestsWithErrors = questList.Count(q => q.HasTtsError),
                TotalCharactersProcessed = questList.Sum(q => (long)(q.TtsText?.Length ?? 0)),
                LastBatchDate = DateTime.Now
            };
        }

        /// <summary>
        /// Aktualisiert die Projekt-Einstellungen aus TtsExportSettings.
        /// </summary>
        public void UpdateSettingsFromExport(TtsExportSettings settings)
        {
            _currentProject.Settings.OutputRootPath = settings.OutputRootPath;
            _currentProject.Settings.LanguageCode = settings.LanguageCode;
            _currentProject.Settings.MaleVoiceId = settings.MaleVoiceId;
            _currentProject.Settings.FemaleVoiceId = settings.FemaleVoiceId;
            _currentProject.Settings.AvgCharsPerToken = settings.AvgCharsPerToken;
            _currentProject.Settings.CostPer1kTokens = settings.CostPer1kTokens;
            _currentProject.Settings.CurrencySymbol = settings.CurrencySymbol;
        }

        /// <summary>
        /// Wendet gespeicherte Einstellungen auf TtsExportSettings an.
        /// </summary>
        public void ApplySettingsToExport(TtsExportSettings settings)
        {
            var projectSettings = _currentProject.Settings;

            if (!string.IsNullOrEmpty(projectSettings.OutputRootPath))
                settings.OutputRootPath = projectSettings.OutputRootPath;

            if (!string.IsNullOrEmpty(projectSettings.LanguageCode))
                settings.LanguageCode = projectSettings.LanguageCode;

            if (!string.IsNullOrEmpty(projectSettings.MaleVoiceId))
                settings.MaleVoiceId = projectSettings.MaleVoiceId;

            if (!string.IsNullOrEmpty(projectSettings.FemaleVoiceId))
                settings.FemaleVoiceId = projectSettings.FemaleVoiceId;

            settings.AvgCharsPerToken = projectSettings.AvgCharsPerToken;
            settings.CostPer1kTokens = projectSettings.CostPer1kTokens;

            if (!string.IsNullOrEmpty(projectSettings.CurrencySymbol))
                settings.CurrencySymbol = projectSettings.CurrencySymbol;
        }

        /// <summary>
        /// Gibt die aktuellen Statistiken zurück.
        /// </summary>
        public ProjectStatistics GetStatistics() => _currentProject.Statistics;

        /// <summary>
        /// Gibt das Projektverzeichnis zurück.
        /// </summary>
        public string ProjectDirectory => _projectDirectory;

        /// <summary>
        /// Gibt den Pfad zur Projektdatei zurück.
        /// </summary>
        public string ProjectFilePath => _projectFilePath;

        /// <summary>
        /// Gibt an, ob ein Projekt existiert.
        /// </summary>
        public bool HasExistingProject => File.Exists(_projectFilePath) && _currentProject.Quests.Count > 0;

        /// <summary>
        /// Gibt die Anzahl der gespeicherten Quest-Metadaten zurück.
        /// </summary>
        public int SavedQuestCount => _currentProject.Quests.Count;

        /// <summary>
        /// Löscht alle Projektdaten (mit Backup).
        /// </summary>
        public void ResetProject()
        {
            CreateBackup("reset");
            _currentProject = new ProjectData();
            Save();
        }
    }
}
