using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services.TtsEngines
{
    /// <summary>
    /// OpenAI TTS Einstellungen.
    /// </summary>
    public class OpenAiTtsSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = "https://api.openai.com/v1";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "tts-1";

        [JsonPropertyName("maleVoice")]
        public string MaleVoice { get; set; } = "onyx";

        [JsonPropertyName("femaleVoice")]
        public string FemaleVoice { get; set; } = "nova";

        [JsonPropertyName("speed")]
        public double Speed { get; set; } = 1.0;

        [JsonPropertyName("responseFormat")]
        public string ResponseFormat { get; set; } = "mp3";

        [JsonIgnore]
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }

    /// <summary>
    /// Google Cloud TTS Einstellungen.
    /// Verwendet Google Cloud Text-to-Speech API (nicht Gemini).
    /// </summary>
    public class GeminiTtsSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("projectId")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("region")]
        public string Region { get; set; } = "us-central1";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("maleVoice")]
        public string MaleVoice { get; set; } = "de-DE-Neural2-B";

        [JsonPropertyName("femaleVoice")]
        public string FemaleVoice { get; set; } = "de-DE-Neural2-C";

        [JsonPropertyName("languageCode")]
        public string LanguageCode { get; set; } = "de-DE";

        [JsonPropertyName("speakingRate")]
        public double SpeakingRate { get; set; } = 1.0;

        [JsonPropertyName("pitch")]
        public double Pitch { get; set; } = 0.0;

        [JsonPropertyName("audioEncoding")]
        public string AudioEncoding { get; set; } = "MP3";

        /// <summary>
        /// Pfad zur Service Account JSON-Datei fuer OAuth2-Authentifizierung.
        /// </summary>
        [JsonPropertyName("serviceAccountJsonPath")]
        public string ServiceAccountJsonPath { get; set; } = "";

        [JsonIgnore]
        public bool IsConfigured =>
            !string.IsNullOrWhiteSpace(ApiKey) ||
            !string.IsNullOrWhiteSpace(ServiceAccountJsonPath);
    }

    /// <summary>
    /// Anthropic Claude TTS Einstellungen.
    /// </summary>
    public class ClaudeTtsSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = "https://api.anthropic.com";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("maleVoice")]
        public string MaleVoice { get; set; } = "";

        [JsonPropertyName("femaleVoice")]
        public string FemaleVoice { get; set; } = "";

        [JsonPropertyName("useForTextOptimization")]
        public bool UseForTextOptimization { get; set; } = false;

        [JsonPropertyName("textOptimizationPrompt")]
        public string TextOptimizationPrompt { get; set; } = "Optimiere den folgenden Quest-Text fuer Sprachausgabe. Entferne Formatierungen und ersetze Abkuerzungen.";

        [JsonIgnore]
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }

    /// <summary>
    /// Externe TTS Einstellungen (ElevenLabs).
    /// </summary>
    public class ExternalTtsSettings
    {
        [JsonPropertyName("apiKey")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; } = "https://api.elevenlabs.io/v1";

        [JsonPropertyName("maleVoiceId")]
        public string MaleVoiceId { get; set; } = "";

        [JsonPropertyName("femaleVoiceId")]
        public string FemaleVoiceId { get; set; } = "";

        [JsonPropertyName("modelId")]
        public string ModelId { get; set; } = "eleven_multilingual_v2";

        [JsonPropertyName("stability")]
        public double Stability { get; set; } = 0.5;

        [JsonPropertyName("similarityBoost")]
        public double SimilarityBoost { get; set; } = 0.75;

        [JsonPropertyName("style")]
        public double Style { get; set; } = 0.0;

        [JsonPropertyName("useSpeakerBoost")]
        public bool UseSpeakerBoost { get; set; } = true;

        [JsonIgnore]
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) &&
                                    !string.IsNullOrWhiteSpace(MaleVoiceId);
    }

    /// <summary>
    /// Hauptklasse fuer alle TTS-Engine Einstellungen.
    /// </summary>
    public class TtsEngineSettings
    {
        private static readonly string SettingsFilePath = Path.Combine(
            AppContext.BaseDirectory, "config", "tts_engine_settings.json");

        private static TtsEngineSettings? _instance;

        /// <summary>
        /// Singleton-Instanz.
        /// </summary>
        public static TtsEngineSettings Instance => _instance ??= Load();

        [JsonPropertyName("activeEngineId")]
        public string ActiveEngineId { get; set; } = "External";

        [JsonPropertyName("fallbackEngineId")]
        public string? FallbackEngineId { get; set; }

        [JsonPropertyName("autoFallbackEnabled")]
        public bool AutoFallbackEnabled { get; set; } = false;

        [JsonPropertyName("maxRetries")]
        public int MaxRetries { get; set; } = 2;

        [JsonPropertyName("retryDelayMs")]
        public int RetryDelayMs { get; set; } = 1000;

        [JsonPropertyName("usageTrackingEnabled")]
        public bool UsageTrackingEnabled { get; set; } = true;

        [JsonPropertyName("openAi")]
        public OpenAiTtsSettings OpenAi { get; set; } = new();

        [JsonPropertyName("gemini")]
        public GeminiTtsSettings Gemini { get; set; } = new();

        [JsonPropertyName("claude")]
        public ClaudeTtsSettings Claude { get; set; } = new();

        [JsonPropertyName("external")]
        public ExternalTtsSettings External { get; set; } = new();

        /// <summary>
        /// Laedt die Einstellungen aus der JSON-Datei.
        /// </summary>
        public static TtsEngineSettings Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<TtsEngineSettings>(json);
                    if (settings != null)
                    {
                        _instance = settings;
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der TTS-Engine-Einstellungen: {ex.Message}");
            }

            // Standardeinstellungen zurueckgeben
            var defaultSettings = new TtsEngineSettings();
            _instance = defaultSettings;

            // Versuche bestehende ElevenLabs-Einstellungen zu migrieren
            MigrateExistingSettings(defaultSettings);

            return defaultSettings;
        }

        /// <summary>
        /// Migriert bestehende ElevenLabs-Einstellungen aus tts_config.json.
        /// </summary>
        private static void MigrateExistingSettings(TtsEngineSettings settings)
        {
            try
            {
                var oldConfigPath = Path.Combine(AppContext.BaseDirectory, "config", "tts_config.json");
                if (File.Exists(oldConfigPath))
                {
                    var json = File.ReadAllText(oldConfigPath);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("apiKey", out var apiKey))
                        settings.External.ApiKey = apiKey.GetString() ?? "";

                    if (root.TryGetProperty("maleVoiceId", out var maleVoice))
                        settings.External.MaleVoiceId = maleVoice.GetString() ?? "";

                    if (root.TryGetProperty("femaleVoiceId", out var femaleVoice))
                        settings.External.FemaleVoiceId = femaleVoice.GetString() ?? "";

                    if (root.TryGetProperty("modelId", out var modelId))
                        settings.External.ModelId = modelId.GetString() ?? "eleven_multilingual_v2";

                    if (root.TryGetProperty("stability", out var stability))
                        settings.External.Stability = stability.GetDouble();

                    if (root.TryGetProperty("similarityBoost", out var similarity))
                        settings.External.SimilarityBoost = similarity.GetDouble();

                    System.Diagnostics.Debug.WriteLine("ElevenLabs-Einstellungen erfolgreich migriert.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Migration der ElevenLabs-Einstellungen fehlgeschlagen: {ex.Message}");
            }
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
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern der TTS-Engine-Einstellungen: {ex.Message}");
            }
        }

        /// <summary>
        /// Setzt die Singleton-Instanz zurueck (fuer Tests).
        /// </summary>
        public static void ResetInstance()
        {
            _instance = null;
        }
    }
}
