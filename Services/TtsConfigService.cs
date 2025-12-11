using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services
{
    public class TtsConfig
    {
        [JsonPropertyName("elevenlabs")]
        public ElevenLabsConfig ElevenLabs { get; set; } = new();

        [JsonPropertyName("blizzard")]
        public BlizzardConfig Blizzard { get; set; } = new();

        [JsonPropertyName("tts_settings")]
        public TtsSettings TtsSettings { get; set; } = new();

        [JsonPropertyName("paths")]
        public PathsConfig Paths { get; set; } = new();

        [JsonPropertyName("voice_profiles")]
        public Dictionary<string, VoiceProfile> VoiceProfiles { get; set; } = [];

        /// <summary>
        /// LLM-Konfiguration fuer Text-Optimierung (Hoerbuch-Feeling).
        /// </summary>
        [JsonPropertyName("llm")]
        public LlmConfig Llm { get; set; } = new();
    }

    public class BlizzardConfig
    {
        [JsonPropertyName("client_id")]
        public string ClientId { get; set; } = "";

        [JsonPropertyName("client_secret")]
        public string ClientSecret { get; set; } = "";

        [JsonPropertyName("region")]
        public string Region { get; set; } = "eu";

        [JsonPropertyName("max_quests")]
        public int MaxQuests { get; set; } = 1000;
    }

    public class ElevenLabsConfig
    {
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = "";

        [JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = "";

        [JsonPropertyName("model_id")]
        public string ModelId { get; set; } = "eleven_multilingual_v2";

        [JsonPropertyName("voice_settings")]
        public VoiceSettings VoiceSettings { get; set; } = new();
    }

    public class VoiceSettings
    {
        [JsonPropertyName("stability")]
        public double Stability { get; set; } = 0.5;

        [JsonPropertyName("similarity_boost")]
        public double SimilarityBoost { get; set; } = 0.75;

        [JsonPropertyName("style")]
        public double Style { get; set; } = 0.0;

        [JsonPropertyName("use_speaker_boost")]
        public bool UseSpeakerBoost { get; set; } = true;
    }

    public class TtsSettings
    {
        [JsonPropertyName("language")]
        public string Language { get; set; } = "de-DE";

        [JsonPropertyName("include_title")]
        public bool IncludeTitle { get; set; } = true;

        [JsonPropertyName("output_format")]
        public string OutputFormat { get; set; } = "mp3_44100_128";
    }

    public class PathsConfig
    {
        [JsonPropertyName("quests_json")]
        public string QuestsJson { get; set; } = "data/quests_deDE.json";

        [JsonPropertyName("audio_output")]
        public string AudioOutput { get; set; } = "audio/deDE";

        [JsonPropertyName("batch_export")]
        public string BatchExport { get; set; } = "data/batch_export.json";
    }

    public class VoiceProfile
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = "";

        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "ElevenLabs";

        [JsonPropertyName("language")]
        public string Language { get; set; } = "de-DE";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("gender")]
        public string Gender { get; set; } = ""; // male, female, neutral

        [JsonPropertyName("style")]
        public string Style { get; set; } = ""; // narrator, character, epic, calm, etc.

        // ==================== TTS-Feintuning Einstellungen ====================

        /// <summary>
        /// Stabilitaet der Stimme (0.0 = variabel/expressiv, 1.0 = stabil/konsistent).
        /// Hoehere Werte = gleichmaessigere Stimme, niedrigere = mehr Variation.
        /// </summary>
        [JsonPropertyName("stability")]
        public double Stability { get; set; } = 0.5;

        /// <summary>
        /// Aehnlichkeitsverstaerkung (0.0 = niedrig, 1.0 = hoch).
        /// Hoehere Werte = mehr wie das Original-Stimmmodell.
        /// </summary>
        [JsonPropertyName("similarity_boost")]
        public double SimilarityBoost { get; set; } = 0.75;

        /// <summary>
        /// Stil-Intensitaet (0.0 = neutral, 1.0 = stark stilisiert).
        /// Fuer ElevenLabs: Wie stark der Stil der Stimme zum Ausdruck kommt.
        /// </summary>
        [JsonPropertyName("style_intensity")]
        public double StyleIntensity { get; set; } = 0.0;

        /// <summary>
        /// Speaker-Boost aktivieren (verbessert Stimmklarheit).
        /// </summary>
        [JsonPropertyName("use_speaker_boost")]
        public bool UseSpeakerBoost { get; set; } = true;

        /// <summary>
        /// Sprechgeschwindigkeit (0.5 = langsam, 1.0 = normal, 1.5 = schnell).
        /// Nicht alle Provider unterstuetzen dies direkt.
        /// </summary>
        [JsonPropertyName("speed")]
        public double Speed { get; set; } = 1.0;

        /// <summary>
        /// Pausen-Multiplikator fuer Satzenden (1.0 = normal, 1.5 = laengere Pausen).
        /// Wird per SSML oder Textmanipulation umgesetzt.
        /// </summary>
        [JsonPropertyName("pause_multiplier")]
        public double PauseMultiplier { get; set; } = 1.0;

        /// <summary>
        /// Pitch/Tonhoehe Anpassung (-1.0 = tiefer, 0 = normal, 1.0 = hoeher).
        /// Nicht alle Provider unterstuetzen dies.
        /// </summary>
        [JsonPropertyName("pitch")]
        public double Pitch { get; set; } = 0.0;

        // ==================== Erweiterte Prosodie-Einstellungen ====================

        /// <summary>
        /// Text-Vorverarbeitung: Fuegt Atempausen nach bestimmten Interpunktionen ein.
        /// </summary>
        [JsonPropertyName("add_breath_pauses")]
        public bool AddBreathPauses { get; set; } = false;

        /// <summary>
        /// Prosodie-Preset (none, calm, dramatic, epic, conversational).
        /// </summary>
        [JsonPropertyName("prosody_preset")]
        public string ProsodyPreset { get; set; } = "none";

        /// <summary>
        /// Anzeigename für UI (Name + Beschreibung)
        /// </summary>
        [JsonIgnore]
        public string DisplayName => string.IsNullOrEmpty(Description) ? Name : $"{Name} - {Description}";

        /// <summary>
        /// Kurzinfo fuer UI (Gender + Style).
        /// </summary>
        [JsonIgnore]
        public string ShortInfo => $"{GenderDisplayName}, {StyleDisplayName}";

        /// <summary>
        /// Gender-Anzeigename.
        /// </summary>
        [JsonIgnore]
        public string GenderDisplayName => Gender?.ToLowerInvariant() switch
        {
            "male" => "Maennlich",
            "female" => "Weiblich",
            "neutral" => "Neutral",
            _ => "Unbekannt"
        };

        /// <summary>
        /// Style-Anzeigename.
        /// </summary>
        [JsonIgnore]
        public string StyleDisplayName => Style?.ToLowerInvariant() switch
        {
            "narrator" => "Erzaehler",
            "epic" => "Episch",
            "calm" => "Ruhig",
            "dramatic" => "Dramatisch",
            "conversational" => "Gespraechig",
            "character" => "Charakter",
            _ => Style ?? "Standard"
        };

        /// <summary>
        /// Erstellt ElevenLabs VoiceSettings aus diesem Profil.
        /// </summary>
        public VoiceSettings ToVoiceSettings()
        {
            return new VoiceSettings
            {
                Stability = Stability,
                SimilarityBoost = SimilarityBoost,
                Style = StyleIntensity,
                UseSpeakerBoost = UseSpeakerBoost
            };
        }

        /// <summary>
        /// Erstellt eine Kopie des Profils
        /// </summary>
        public VoiceProfile Clone() => new()
        {
            Name = Name,
            VoiceId = VoiceId,
            Provider = Provider,
            Language = Language,
            Description = Description,
            Gender = Gender,
            Style = Style,
            Stability = Stability,
            SimilarityBoost = SimilarityBoost,
            StyleIntensity = StyleIntensity,
            UseSpeakerBoost = UseSpeakerBoost,
            Speed = Speed,
            PauseMultiplier = PauseMultiplier,
            Pitch = Pitch,
            AddBreathPauses = AddBreathPauses,
            ProsodyPreset = ProsodyPreset
        };
    }

    public class TtsConfigService
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };
        private readonly string _configPath;
        private TtsConfig? _config;

        public TtsConfigService()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _configPath = Path.Combine(baseDir, "config", "tts_config.json");
        }

        public TtsConfigService(string configPath)
        {
            _configPath = configPath;
        }

        public TtsConfig Config => _config ??= LoadConfig();

        public TtsConfig LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                // Erstelle Default-Config
                var defaultConfig = CreateDefaultConfig();
                SaveConfig(defaultConfig);
                return defaultConfig;
            }

            try
            {
                var json = File.ReadAllText(_configPath);
                return JsonSerializer.Deserialize<TtsConfig>(json) ?? CreateDefaultConfig();
            }
            catch
            {
                return CreateDefaultConfig();
            }
        }

        public void SaveConfig(TtsConfig config)
        {
            var directory = Path.GetDirectoryName(_configPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(config, s_jsonOptions);
            File.WriteAllText(_configPath, json);
            _config = config;
        }

        public string GetVoiceId(string profileName)
        {
            if (Config.VoiceProfiles.TryGetValue(profileName, out var profile))
            {
                return profile.VoiceId;
            }
            return Config.ElevenLabs.VoiceId;
        }

        /// <summary>
        /// Gibt ein VoiceProfile anhand des Namens zurück.
        /// </summary>
        public VoiceProfile? GetVoiceProfile(string profileName)
        {
            if (Config.VoiceProfiles.TryGetValue(profileName, out var profile))
            {
                return profile;
            }
            return null;
        }

        /// <summary>
        /// Gibt alle verfügbaren Voice-Profile zurück.
        /// </summary>
        public IEnumerable<KeyValuePair<string, VoiceProfile>> GetAllVoiceProfiles()
        {
            return Config.VoiceProfiles;
        }

        /// <summary>
        /// Fügt ein neues Voice-Profil hinzu oder aktualisiert ein bestehendes.
        /// </summary>
        public void SaveVoiceProfile(string key, VoiceProfile profile)
        {
            Config.VoiceProfiles[key] = profile;
            SaveConfig(Config);
        }

        /// <summary>
        /// Entfernt ein Voice-Profil.
        /// </summary>
        public bool RemoveVoiceProfile(string key)
        {
            if (Config.VoiceProfiles.Remove(key))
            {
                SaveConfig(Config);
                return true;
            }
            return false;
        }

        public string GetAudioOutputPath(int questId)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var audioDir = Path.Combine(baseDir, Config.Paths.AudioOutput);
            return Path.Combine(audioDir, $"quest_{questId}.mp3");
        }

        private static TtsConfig CreateDefaultConfig()
        {
            return new TtsConfig
            {
                ElevenLabs = new ElevenLabsConfig
                {
                    ApiKey = "YOUR_ELEVENLABS_API_KEY_HERE",
                    VoiceId = "pNInz6obpgDQGcFmaJgB",
                    ModelId = "eleven_multilingual_v2"
                },
                VoiceProfiles = new Dictionary<string, VoiceProfile>
                {
                    ["male_narrator"] = new()
                    {
                        Name = "Male Narrator",
                        VoiceId = "ErXwobaYiN019PkySvjV",
                        Provider = "ElevenLabs",
                        Language = "de-DE",
                        Description = "Antoni - Männlicher Erzähler",
                        Gender = "male",
                        Style = "narrator"
                    },
                    ["female_narrator"] = new()
                    {
                        Name = "Female Narrator",
                        VoiceId = "EXAVITQu4vr4xnSDxMaL",
                        Provider = "ElevenLabs",
                        Language = "de-DE",
                        Description = "Bella - Weibliche Erzählerin",
                        Gender = "female",
                        Style = "narrator"
                    },
                    ["epic_narrator"] = new()
                    {
                        Name = "Epic Narrator",
                        VoiceId = "VR6AewLTigWG4xSOukaG",
                        Provider = "ElevenLabs",
                        Language = "de-DE",
                        Description = "Arnold - Epischer Erzähler",
                        Gender = "male",
                        Style = "epic"
                    }
                }
            };
        }
    }
}
