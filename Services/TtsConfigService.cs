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
        public Dictionary<string, VoiceProfile> VoiceProfiles { get; set; } = new();
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
        [JsonPropertyName("voice_id")]
        public string VoiceId { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";
    }

    public class TtsConfigService
    {
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

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(config, options);
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
                    ["neutral_male"] = new() { VoiceId = "pNInz6obpgDQGcFmaJgB", Description = "Adam - Neutral männlich" },
                    ["neutral_female"] = new() { VoiceId = "21m00Tcm4TlvDq8ikWAM", Description = "Rachel - Neutral weiblich" },
                    ["epic_narrator"] = new() { VoiceId = "VR6AewLTigWG4xSOukaG", Description = "Arnold - Epischer Erzähler" }
                }
            };
        }
    }
}
