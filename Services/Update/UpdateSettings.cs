using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services.Update
{
    /// <summary>
    /// Einstellungen für das Auto-Update-System.
    /// </summary>
    public class UpdateSettings
    {
        private static readonly string SettingsFilePath = Path.Combine(
            AppContext.BaseDirectory, "config", "update_settings.json");

        private static UpdateSettings? _instance;

        /// <summary>
        /// Singleton-Instanz der Update-Einstellungen.
        /// </summary>
        public static UpdateSettings Instance => _instance ??= Load();

        /// <summary>
        /// URL zum Update-Manifest (JSON).
        /// Beispiele:
        /// - GitHub Releases: https://api.github.com/repos/USER/REPO/releases/latest
        /// - Eigener Server: https://example.com/wowquesttts/updates.json
        /// </summary>
        [JsonPropertyName("updateManifestUrl")]
        public string UpdateManifestUrl { get; set; } = "https://example.com/wowquesttts/updates.json";

        /// <summary>
        /// Basis-Verzeichnis der Installation.
        /// Standard: Verzeichnis, in dem die EXE liegt.
        /// </summary>
        [JsonPropertyName("installBaseDirectory")]
        public string InstallBaseDirectory
        {
            get => string.IsNullOrEmpty(_installBaseDirectory) ? AppContext.BaseDirectory : _installBaseDirectory;
            set => _installBaseDirectory = value;
        }
        private string _installBaseDirectory = "";

        /// <summary>
        /// Name der Updater-EXE.
        /// </summary>
        [JsonPropertyName("updaterExeName")]
        public string UpdaterExeName { get; set; } = "WowQuestTtsUpdater.exe";

        /// <summary>
        /// Name der Haupt-EXE (wird nach Update gestartet).
        /// </summary>
        [JsonPropertyName("mainExeName")]
        public string MainExeName { get; set; } = "WowQuestTtsTool.exe";

        /// <summary>
        /// Automatisch beim Start nach Updates suchen.
        /// </summary>
        [JsonPropertyName("checkUpdatesOnStartup")]
        public bool CheckUpdatesOnStartup { get; set; } = true;

        /// <summary>
        /// Zeitpunkt der letzten Update-Prüfung.
        /// </summary>
        [JsonPropertyName("lastUpdateCheck")]
        public DateTime? LastUpdateCheck { get; set; }

        /// <summary>
        /// Minimale Zeit zwischen automatischen Update-Prüfungen (in Stunden).
        /// </summary>
        [JsonPropertyName("updateCheckIntervalHours")]
        public int UpdateCheckIntervalHours { get; set; } = 24;

        /// <summary>
        /// Übersprungene Version (Benutzer hat "Später" geklickt).
        /// </summary>
        [JsonPropertyName("skippedVersion")]
        public string? SkippedVersion { get; set; }

        /// <summary>
        /// Temporäres Verzeichnis für Downloads.
        /// </summary>
        [JsonIgnore]
        public string TempUpdateDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WowQuestTtsTool", "Updates");

        /// <summary>
        /// Pfad zur Updater-EXE.
        /// </summary>
        [JsonIgnore]
        public string UpdaterExePath => Path.Combine(InstallBaseDirectory, UpdaterExeName);

        /// <summary>
        /// Pfad zur Haupt-EXE.
        /// </summary>
        [JsonIgnore]
        public string MainExePath => Path.Combine(InstallBaseDirectory, MainExeName);

        /// <summary>
        /// Prüft, ob eine automatische Update-Prüfung durchgeführt werden soll.
        /// </summary>
        public bool ShouldCheckForUpdates()
        {
            if (!CheckUpdatesOnStartup)
                return false;

            if (LastUpdateCheck == null)
                return true;

            var timeSinceLastCheck = DateTime.Now - LastUpdateCheck.Value;
            return timeSinceLastCheck.TotalHours >= UpdateCheckIntervalHours;
        }

        /// <summary>
        /// Lädt die Einstellungen aus der JSON-Datei.
        /// </summary>
        public static UpdateSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<UpdateSettings>(json);
                    if (settings != null)
                    {
                        _instance = settings;
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der Update-Einstellungen: {ex.Message}");
            }

            // Standardeinstellungen zurückgeben
            var defaultSettings = new UpdateSettings();
            _instance = defaultSettings;
            return defaultSettings;
        }

        /// <summary>
        /// Speichert die Einstellungen in die JSON-Datei.
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions
                {
                    WriteIndented = true
                };
                var json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern der Update-Einstellungen: {ex.Message}");
            }
        }

        /// <summary>
        /// Aktualisiert den Zeitpunkt der letzten Prüfung und speichert.
        /// </summary>
        public void MarkUpdateChecked()
        {
            LastUpdateCheck = DateTime.Now;
            Save();
        }
    }
}
