using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services.TtsEngines
{
    /// <summary>
    /// Einfache Statistik-Struktur fuer Session-Daten.
    /// </summary>
    public class TtsUsageStats
    {
        public long TotalCharacters { get; set; }
        public long TotalTokensEstimate { get; set; }
        public int TotalRequests { get; set; }
    }

    /// <summary>
    /// Nutzungsdaten fuer eine Engine.
    /// </summary>
    public class TtsUsageEntry
    {
        [JsonPropertyName("engineId")]
        public string EngineId { get; set; } = "";

        [JsonPropertyName("totalCharacters")]
        public long TotalCharacters { get; set; }

        [JsonPropertyName("totalTokensEstimate")]
        public long TotalTokensEstimate { get; set; }

        [JsonPropertyName("totalRequests")]
        public int TotalRequests { get; set; }

        [JsonPropertyName("totalAudioDurationMs")]
        public long TotalAudioDurationMs { get; set; }

        [JsonPropertyName("lastUsedAt")]
        public DateTime? LastUsedAt { get; set; }

        // Session-Daten (nicht persistiert)
        [JsonIgnore]
        public long SessionCharacters { get; set; }

        [JsonIgnore]
        public long SessionTokens { get; set; }

        [JsonIgnore]
        public int SessionRequests { get; set; }

        [JsonIgnore]
        public long SessionDurationMs { get; set; }
    }

    /// <summary>
    /// Session-Nutzungsdaten (alle Engines zusammen).
    /// </summary>
    public class TtsSessionUsage
    {
        public long TotalCharacters { get; set; }
        public long TotalTokens { get; set; }
        public int TotalRequests { get; set; }
        public long TotalDurationMs { get; set; }
        public DateTime SessionStartTime { get; set; }

        public string FormattedDuration
        {
            get
            {
                var span = TimeSpan.FromMilliseconds(TotalDurationMs);
                if (span.TotalHours >= 1)
                    return $"{(int)span.TotalHours}h {span.Minutes}m";
                if (span.TotalMinutes >= 1)
                    return $"{span.Minutes}m {span.Seconds}s";
                return $"{span.Seconds}s";
            }
        }
    }

    /// <summary>
    /// Persistiertes Format der Usage-Daten.
    /// </summary>
    public class TtsUsageData
    {
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonPropertyName("engines")]
        public Dictionary<string, TtsUsageEntry> Engines { get; set; } = new();
    }

    /// <summary>
    /// Tracker fuer TTS-Nutzung pro Engine.
    /// </summary>
    public class TtsUsageTracker
    {
        private static readonly string UsageFilePath = Path.Combine(
            AppContext.BaseDirectory, "data", "tts_usage.json");

        private readonly Dictionary<string, TtsUsageEntry> _usageData = new();
        private DateTime _sessionStartTime;
        private bool _isDirty;

        private static TtsUsageTracker? _instance;
        public static TtsUsageTracker Instance => _instance ??= new TtsUsageTracker();

        public TtsUsageTracker()
        {
            Load();
            _sessionStartTime = DateTime.Now;
        }

        /// <summary>
        /// Zeichnet Nutzung auf.
        /// </summary>
        public void RecordUsage(string engineId, int characters, int estimatedTokens, long durationMs)
        {
            if (string.IsNullOrWhiteSpace(engineId))
                return;

            if (!_usageData.TryGetValue(engineId, out var entry))
            {
                entry = new TtsUsageEntry { EngineId = engineId };
                _usageData[engineId] = entry;
            }

            // Gesamt-Daten aktualisieren
            entry.TotalCharacters += characters;
            entry.TotalTokensEstimate += estimatedTokens;
            entry.TotalRequests++;
            entry.TotalAudioDurationMs += durationMs;
            entry.LastUsedAt = DateTime.Now;

            // Session-Daten aktualisieren
            entry.SessionCharacters += characters;
            entry.SessionTokens += estimatedTokens;
            entry.SessionRequests++;
            entry.SessionDurationMs += durationMs;

            _isDirty = true;

            // Automatisch speichern (debounced koennte hier implementiert werden)
            Save();
        }

        /// <summary>
        /// Holt Nutzungsdaten fuer eine Engine.
        /// </summary>
        public TtsUsageEntry? GetUsageForEngine(string engineId)
        {
            return _usageData.TryGetValue(engineId, out var entry) ? entry : null;
        }

        /// <summary>
        /// Holt alle Nutzungsdaten.
        /// </summary>
        public IReadOnlyList<TtsUsageEntry> GetAllUsage()
        {
            return _usageData.Values.ToList();
        }

        /// <summary>
        /// Holt Session-Nutzungsdaten.
        /// </summary>
        public TtsSessionUsage GetSessionUsage()
        {
            return new TtsSessionUsage
            {
                TotalCharacters = _usageData.Values.Sum(e => e.SessionCharacters),
                TotalTokens = _usageData.Values.Sum(e => e.SessionTokens),
                TotalRequests = _usageData.Values.Sum(e => e.SessionRequests),
                TotalDurationMs = _usageData.Values.Sum(e => e.SessionDurationMs),
                SessionStartTime = _sessionStartTime
            };
        }

        /// <summary>
        /// Setzt Session-Zaehler zurueck.
        /// </summary>
        public void ResetSessionUsage()
        {
            foreach (var entry in _usageData.Values)
            {
                entry.SessionCharacters = 0;
                entry.SessionTokens = 0;
                entry.SessionRequests = 0;
                entry.SessionDurationMs = 0;
            }
            _sessionStartTime = DateTime.Now;
        }

        /// <summary>
        /// Alias fuer ResetSessionUsage.
        /// </summary>
        public void ResetSession() => ResetSessionUsage();

        /// <summary>
        /// Session-Startzeit.
        /// </summary>
        public DateTime SessionStartTime => _sessionStartTime;

        /// <summary>
        /// Holt Session-Statistiken pro Engine.
        /// </summary>
        public Dictionary<string, TtsUsageStats> GetSessionStats()
        {
            var result = new Dictionary<string, TtsUsageStats>();
            foreach (var kvp in _usageData)
            {
                result[kvp.Key] = new TtsUsageStats
                {
                    TotalCharacters = kvp.Value.SessionCharacters,
                    TotalTokensEstimate = kvp.Value.SessionTokens,
                    TotalRequests = kvp.Value.SessionRequests
                };
            }
            return result;
        }

        /// <summary>
        /// Exportiert Session-Daten als CSV.
        /// </summary>
        public string ExportToCsv()
        {
            return ExportUsageReportCsv();
        }

        /// <summary>
        /// Setzt alle Zaehler zurueck.
        /// </summary>
        public void ResetAllUsage()
        {
            _usageData.Clear();
            _isDirty = true;
            Save();
            _sessionStartTime = DateTime.Now;
        }

        /// <summary>
        /// Exportiert Nutzungsdaten als CSV.
        /// </summary>
        public string ExportUsageReportCsv()
        {
            var lines = new List<string>
            {
                "Engine,Zeichen (Gesamt),Tokens (Schaetzung),Requests,Audio-Dauer (min),Zuletzt verwendet"
            };

            foreach (var entry in _usageData.Values.OrderByDescending(e => e.TotalCharacters))
            {
                var audioDurationMin = Math.Round(entry.TotalAudioDurationMs / 60000.0, 1);
                var lastUsed = entry.LastUsedAt?.ToString("yyyy-MM-dd HH:mm") ?? "-";
                lines.Add($"{entry.EngineId},{entry.TotalCharacters},{entry.TotalTokensEstimate},{entry.TotalRequests},{audioDurationMin},{lastUsed}");
            }

            return string.Join(Environment.NewLine, lines);
        }

        /// <summary>
        /// Exportiert Nutzungsdaten als JSON.
        /// </summary>
        public string ExportUsageReportJson()
        {
            var data = new TtsUsageData
            {
                LastUpdated = DateTime.Now,
                Engines = _usageData
            };

            return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        }

        /// <summary>
        /// Laedt Nutzungsdaten aus Datei.
        /// </summary>
        private void Load()
        {
            try
            {
                if (File.Exists(UsageFilePath))
                {
                    var json = File.ReadAllText(UsageFilePath);
                    var data = JsonSerializer.Deserialize<TtsUsageData>(json);

                    if (data?.Engines != null)
                    {
                        foreach (var kvp in data.Engines)
                        {
                            _usageData[kvp.Key] = kvp.Value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der TTS-Nutzungsdaten: {ex.Message}");
            }
        }

        /// <summary>
        /// Speichert Nutzungsdaten in Datei.
        /// </summary>
        public void Save()
        {
            if (!_isDirty)
                return;

            try
            {
                var directory = Path.GetDirectoryName(UsageFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var data = new TtsUsageData
                {
                    LastUpdated = DateTime.Now,
                    Engines = _usageData
                };

                var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(UsageFilePath, json);

                _isDirty = false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern der TTS-Nutzungsdaten: {ex.Message}");
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
