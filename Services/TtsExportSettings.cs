using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Einstellungen für den TTS-Export. Werden automatisch in einer JSON-Datei persistiert.
    /// </summary>
    public class TtsExportSettings : INotifyPropertyChanged
    {
        private static readonly string SettingsPath;
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
        private static TtsExportSettings? _instance;

        private string _outputRootPath = "";
        private string _languageCode = "deDE";
        private string _mainFolderName = "main";
        private string _sideFolderName = "side";
        private string _ttsVoiceId = "default_de_voice";
        private string _ttsProvider = "Dummy";

        // Male/Female Voice-IDs
        private string _maleVoiceId = "ErXwobaYiN019PkySvjV";    // ElevenLabs Antoni (Default)
        private string _femaleVoiceId = "EXAVITQu4vr4xnSDxMaL"; // ElevenLabs Bella (Default)

        // Kosten-Tracking Parameter
        private double _avgCharsPerToken = 4.0;
        private decimal _costPer1kTokens = 0.30m;
        private string _currencySymbol = "€";

        // SQLite Quest-Datenbank (AzerothCore)
        private string _sqliteDatabasePath = "";
        private bool _useSqliteDatabase = false;

        // Blizzard JSON-Datei
        private string _blizzardJsonPath = "";

        // Merged Repository aktivieren
        private bool _useMergedRepository = false;

        static TtsExportSettings()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            SettingsPath = Path.Combine(baseDir, "config", "tts_export_settings.json");
        }

        public static TtsExportSettings Instance => _instance ??= Load();

        /// <summary>
        /// Basis-Ordner für Audio-Ausgabe.
        /// </summary>
        [JsonPropertyName("output_root_path")]
        public string OutputRootPath
        {
            get => _outputRootPath;
            set
            {
                if (_outputRootPath != value)
                {
                    _outputRootPath = value;
                    OnPropertyChanged(nameof(OutputRootPath));
                }
            }
        }

        /// <summary>
        /// Sprachcode, z.B. "deDE", "enUS".
        /// </summary>
        [JsonPropertyName("language_code")]
        public string LanguageCode
        {
            get => _languageCode;
            set
            {
                if (_languageCode != value)
                {
                    _languageCode = value;
                    OnPropertyChanged(nameof(LanguageCode));
                }
            }
        }

        /// <summary>
        /// Ordnername für Hauptstory-Quests.
        /// </summary>
        [JsonPropertyName("main_folder_name")]
        public string MainFolderName
        {
            get => _mainFolderName;
            set
            {
                if (_mainFolderName != value)
                {
                    _mainFolderName = value;
                    OnPropertyChanged(nameof(MainFolderName));
                }
            }
        }

        /// <summary>
        /// Ordnername für Nebenquests.
        /// </summary>
        [JsonPropertyName("side_folder_name")]
        public string SideFolderName
        {
            get => _sideFolderName;
            set
            {
                if (_sideFolderName != value)
                {
                    _sideFolderName = value;
                    OnPropertyChanged(nameof(SideFolderName));
                }
            }
        }

        /// <summary>
        /// Voice-ID für TTS-Generierung.
        /// </summary>
        [JsonPropertyName("tts_voice_id")]
        public string TtsVoiceId
        {
            get => _ttsVoiceId;
            set
            {
                if (_ttsVoiceId != value)
                {
                    _ttsVoiceId = value;
                    OnPropertyChanged(nameof(TtsVoiceId));
                }
            }
        }

        /// <summary>
        /// Aktiver TTS-Provider (Dummy, ElevenLabs, Gemini, etc.).
        /// </summary>
        [JsonPropertyName("tts_provider")]
        public string TtsProvider
        {
            get => _ttsProvider;
            set
            {
                if (_ttsProvider != value)
                {
                    _ttsProvider = value;
                    OnPropertyChanged(nameof(TtsProvider));
                }
            }
        }

        /// <summary>
        /// Voice-ID für männliche Erzähler-Stimme.
        /// </summary>
        [JsonPropertyName("male_voice_id")]
        public string MaleVoiceId
        {
            get => _maleVoiceId;
            set
            {
                if (_maleVoiceId != value)
                {
                    _maleVoiceId = value;
                    OnPropertyChanged(nameof(MaleVoiceId));
                }
            }
        }

        /// <summary>
        /// Voice-ID für weibliche Erzähler-Stimme.
        /// </summary>
        [JsonPropertyName("female_voice_id")]
        public string FemaleVoiceId
        {
            get => _femaleVoiceId;
            set
            {
                if (_femaleVoiceId != value)
                {
                    _femaleVoiceId = value;
                    OnPropertyChanged(nameof(FemaleVoiceId));
                }
            }
        }

        /// <summary>
        /// Durchschnittliche Zeichen pro Token (für Kostenschätzung).
        /// </summary>
        [JsonPropertyName("avg_chars_per_token")]
        public double AvgCharsPerToken
        {
            get => _avgCharsPerToken;
            set
            {
                if (Math.Abs(_avgCharsPerToken - value) > 0.001)
                {
                    _avgCharsPerToken = value > 0 ? value : 4.0;
                    OnPropertyChanged(nameof(AvgCharsPerToken));
                }
            }
        }

        /// <summary>
        /// Kosten pro 1000 Tokens.
        /// </summary>
        [JsonPropertyName("cost_per_1k_tokens")]
        public decimal CostPer1kTokens
        {
            get => _costPer1kTokens;
            set
            {
                if (_costPer1kTokens != value)
                {
                    _costPer1kTokens = value >= 0 ? value : 0.30m;
                    OnPropertyChanged(nameof(CostPer1kTokens));
                }
            }
        }

        /// <summary>
        /// Währungssymbol für die Kostenanzeige.
        /// </summary>
        [JsonPropertyName("currency_symbol")]
        public string CurrencySymbol
        {
            get => _currencySymbol;
            set
            {
                if (_currencySymbol != value)
                {
                    _currencySymbol = value ?? "€";
                    OnPropertyChanged(nameof(CurrencySymbol));
                }
            }
        }

        /// <summary>
        /// Pfad zur SQLite Quest-Datenbank.
        /// </summary>
        [JsonPropertyName("sqlite_database_path")]
        public string SqliteDatabasePath
        {
            get => _sqliteDatabasePath;
            set
            {
                if (_sqliteDatabasePath != value)
                {
                    _sqliteDatabasePath = value ?? "";
                    OnPropertyChanged(nameof(SqliteDatabasePath));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob SQLite statt JSON/Blizzard verwendet werden soll.
        /// </summary>
        [JsonPropertyName("use_sqlite_database")]
        public bool UseSqliteDatabase
        {
            get => _useSqliteDatabase;
            set
            {
                if (_useSqliteDatabase != value)
                {
                    _useSqliteDatabase = value;
                    OnPropertyChanged(nameof(UseSqliteDatabase));
                }
            }
        }

        /// <summary>
        /// Pfad zur Blizzard JSON-Datei.
        /// </summary>
        [JsonPropertyName("blizzard_json_path")]
        public string BlizzardJsonPath
        {
            get => _blizzardJsonPath;
            set
            {
                if (_blizzardJsonPath != value)
                {
                    _blizzardJsonPath = value ?? "";
                    OnPropertyChanged(nameof(BlizzardJsonPath));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob das Merged Repository verwendet werden soll.
        /// Kombiniert Blizzard-Daten mit AzerothCore-Daten.
        /// </summary>
        [JsonPropertyName("use_merged_repository")]
        public bool UseMergedRepository
        {
            get => _useMergedRepository;
            set
            {
                if (_useMergedRepository != value)
                {
                    _useMergedRepository = value;
                    OnPropertyChanged(nameof(UseMergedRepository));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        /// <summary>
        /// Lädt die Einstellungen aus der JSON-Datei.
        /// </summary>
        public static TtsExportSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    var settings = JsonSerializer.Deserialize<TtsExportSettings>(json);
                    if (settings != null)
                    {
                        _instance = settings;
                        return settings;
                    }
                }
            }
            catch
            {
                // Bei Fehler: Default-Settings zurückgeben
            }

            return new TtsExportSettings();
        }

        /// <summary>
        /// Speichert die aktuellen Einstellungen in die JSON-Datei.
        /// </summary>
        public void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(this, s_jsonOptions);
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Stilles Fehlschlagen beim Speichern
            }
        }

        /// <summary>
        /// Ermittelt den vollständigen Ausgabepfad für eine Quest.
        /// NEUE Struktur: {Root}/{Language}/{Zone}/{Category}/Quest_{ID}_{Category}_{Gender}_{Title}.mp3
        /// </summary>
        public string GetOutputPath(Quest quest, string genderCode = "neutral")
        {
            return AudioPathHelper.GetAudioFilePath(OutputRootPath, LanguageCode, quest, genderCode);
        }

        /// <summary>
        /// Ermittelt den Ausgabepfad für die männliche Erzähler-Stimme.
        /// NEUE Struktur: {Root}/{Language}/{Zone}/{Category}/Quest_{ID}_{Category}_male_{Title}.mp3
        /// </summary>
        public string GetMaleOutputPath(Quest quest)
        {
            return AudioPathHelper.GetMaleAudioPath(OutputRootPath, LanguageCode, quest);
        }

        /// <summary>
        /// Ermittelt den Ausgabepfad für die weibliche Erzähler-Stimme.
        /// NEUE Struktur: {Root}/{Language}/{Zone}/{Category}/Quest_{ID}_{Category}_female_{Title}.mp3
        /// </summary>
        public string GetFemaleOutputPath(Quest quest)
        {
            return AudioPathHelper.GetFemaleAudioPath(OutputRootPath, LanguageCode, quest);
        }

        /// <summary>
        /// Berechnet die geschätzten Kosten für eine gegebene Zeichenanzahl.
        /// </summary>
        /// <param name="totalChars">Gesamtanzahl der Zeichen</param>
        /// <param name="voiceCount">Anzahl der Stimmen (default: 2 für male+female)</param>
        /// <returns>Tuple mit (effectiveChars, estimatedTokens, estimatedCost)</returns>
        public (long EffectiveChars, int EstimatedTokens, decimal EstimatedCost) CalculateCostEstimate(long totalChars, int voiceCount = 2)
        {
            var effectiveChars = totalChars * voiceCount;
            var estimatedTokens = (int)Math.Ceiling(effectiveChars / AvgCharsPerToken);
            var estimatedCost = (estimatedTokens / 1000m) * CostPer1kTokens;
            return (effectiveChars, estimatedTokens, estimatedCost);
        }

        /// <summary>
        /// Bereinigt einen String für die Verwendung als Dateiname.
        /// </summary>
        public static string SanitizeFileName(string? raw, int maxLength = 40)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Unknown";

            // Ungültige Zeichen entfernen
            var invalidChars = Path.GetInvalidFileNameChars();
            var result = raw;

            foreach (var c in invalidChars)
            {
                result = result.Replace(c, '_');
            }

            // Zusätzliche problematische Zeichen ersetzen
            result = result
                .Replace(' ', '_')
                .Replace('.', '_')
                .Replace(',', '_')
                .Replace(';', '_')
                .Replace(':', '_')
                .Replace('\'', '_')
                .Replace('"', '_')
                .Replace('/', '_')
                .Replace('\\', '_');

            // Mehrfache Unterstriche zu einem reduzieren
            while (result.Contains("__"))
            {
                result = result.Replace("__", "_");
            }

            // Führende/trailing Unterstriche entfernen
            result = result.Trim('_');

            // Auf maximale Länge kürzen
            if (result.Length > maxLength)
            {
                result = result[..maxLength].TrimEnd('_');
            }

            return string.IsNullOrEmpty(result) ? "Unknown" : result;
        }
    }
}
