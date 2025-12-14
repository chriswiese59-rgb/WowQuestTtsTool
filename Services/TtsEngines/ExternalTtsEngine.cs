using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services.TtsEngines
{
    /// <summary>
    /// ElevenLabs TTS Engine Implementation.
    /// Verwendet die bestehende ElevenLabs-Integration.
    /// </summary>
    public class ExternalTtsEngine : ITtsEngine, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly TtsEngineSettings _settings;
        private List<TtsVoiceInfo>? _cachedVoices;
        private bool _disposed;

        public string EngineId => "External";
        public string DisplayName => "ElevenLabs";

        public bool IsConfigured => _settings.External.IsConfigured;
        public bool IsAvailable => IsConfigured;

        public IReadOnlyList<TtsVoiceInfo> SupportedVoices => _cachedVoices ?? (IReadOnlyList<TtsVoiceInfo>)Array.Empty<TtsVoiceInfo>();

        private ExternalTtsSettings Settings => _settings.External;

        public ExternalTtsEngine() : this(TtsEngineSettings.Instance)
        {
        }

        public ExternalTtsEngine(TtsEngineSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        public async Task<TtsResult> GenerateAudioAsync(TtsRequest request, CancellationToken ct = default)
        {
            if (!IsAvailable)
            {
                return TtsResult.Failed(TtsErrorCode.EngineNotAvailable, "ElevenLabs ist nicht konfiguriert. Bitte API-Key und Voice-ID eingeben.");
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return TtsResult.Failed(TtsErrorCode.InvalidText, "Text darf nicht leer sein.");
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Voice-ID basierend auf Geschlecht waehlen
                var voiceId = request.VoiceGender?.ToLower() == "female"
                    ? Settings.FemaleVoiceId
                    : Settings.MaleVoiceId;

                if (!string.IsNullOrWhiteSpace(request.VoiceId))
                {
                    voiceId = request.VoiceId;
                }

                if (string.IsNullOrWhiteSpace(voiceId))
                {
                    return TtsResult.Failed(TtsErrorCode.InvalidVoice, "Keine Voice-ID konfiguriert.");
                }

                // Request-Body erstellen
                var requestBody = new
                {
                    text = request.Text,
                    model_id = Settings.ModelId,
                    voice_settings = new
                    {
                        stability = Settings.Stability,
                        similarity_boost = Settings.SimilarityBoost,
                        style = Settings.Style,
                        use_speaker_boost = Settings.UseSpeakerBoost
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // HTTP-Request vorbereiten
                var url = $"{Settings.BaseUrl}/text-to-speech/{voiceId}";
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = content
                };
                requestMessage.Headers.Add("xi-api-key", Settings.ApiKey);
                requestMessage.Headers.Add("Accept", "audio/mpeg");

                // Request senden
                var response = await _httpClient.SendAsync(requestMessage, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    var errorCode = MapHttpErrorCode(response.StatusCode, errorBody);
                    return TtsResult.Failed(errorCode, $"ElevenLabs API Fehler: {response.StatusCode} - {errorBody}");
                }

                // Audio-Daten lesen
                var audioData = await response.Content.ReadAsByteArrayAsync(ct);

                // Datei speichern
                var outputPath = request.OutputPath;
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    var tempDir = Path.GetTempPath();
                    outputPath = Path.Combine(tempDir, $"elevenlabs_tts_{request.QuestId}_{Guid.NewGuid():N}.mp3");
                }

                // Verzeichnis erstellen falls noetig
                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllBytesAsync(outputPath, audioData, ct);

                stopwatch.Stop();

                var charCount = request.Text.Length;
                var estimatedTokens = EstimateTokens(request.Text);

                Debug.WriteLine($"ElevenLabs TTS: {charCount} Zeichen, {stopwatch.ElapsedMilliseconds}ms");

                return TtsResult.Successful(outputPath, charCount, estimatedTokens, stopwatch.ElapsedMilliseconds);
            }
            catch (TaskCanceledException)
            {
                return TtsResult.Failed(TtsErrorCode.Timeout, "ElevenLabs TTS Request wurde abgebrochen (Timeout).");
            }
            catch (HttpRequestException ex)
            {
                return TtsResult.Failed(TtsErrorCode.NetworkError, $"Netzwerkfehler: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ElevenLabs TTS Fehler: {ex}");
                return TtsResult.Failed(TtsErrorCode.UnknownError, $"Unerwarteter Fehler: {ex.Message}");
            }
        }

        public async Task<TtsValidationResult> ValidateConfigurationAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return TtsValidationResult.Invalid("API-Key oder Voice-ID ist nicht konfiguriert.");
            }

            try
            {
                // User-Info abrufen um API-Key zu validieren
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{Settings.BaseUrl}/user");
                requestMessage.Headers.Add("xi-api-key", Settings.ApiKey);

                var response = await _httpClient.SendAsync(requestMessage, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    return TtsValidationResult.Invalid($"API-Key ungueltig: {errorBody}");
                }

                // Versuche verbleibendes Kontingent zu lesen
                var jsonResponse = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonResponse);

                long? remainingQuota = null;
                if (doc.RootElement.TryGetProperty("subscription", out var subscription))
                {
                    if (subscription.TryGetProperty("character_limit", out var limit) &&
                        subscription.TryGetProperty("character_count", out var count))
                    {
                        remainingQuota = limit.GetInt64() - count.GetInt64();
                    }
                }

                return TtsValidationResult.Valid(remainingQuota);
            }
            catch (Exception ex)
            {
                return TtsValidationResult.Invalid($"Validierung fehlgeschlagen: {ex.Message}");
            }
        }

        public async Task<IReadOnlyList<TtsVoiceInfo>> GetAvailableVoicesAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return Array.Empty<TtsVoiceInfo>();
            }

            try
            {
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{Settings.BaseUrl}/voices");
                requestMessage.Headers.Add("xi-api-key", Settings.ApiKey);

                var response = await _httpClient.SendAsync(requestMessage, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"ElevenLabs Voices abrufen fehlgeschlagen: {response.StatusCode}");
                    return Array.Empty<TtsVoiceInfo>();
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonResponse);

                var voices = new List<TtsVoiceInfo>();

                if (doc.RootElement.TryGetProperty("voices", out var voicesArray))
                {
                    foreach (var voice in voicesArray.EnumerateArray())
                    {
                        var voiceInfo = new TtsVoiceInfo
                        {
                            VoiceId = voice.GetProperty("voice_id").GetString() ?? "",
                            DisplayName = voice.GetProperty("name").GetString() ?? "",
                            Gender = "neutral",
                            LanguageCodes = new List<string> { "de-DE", "en-US" }
                        };

                        // Gender aus Labels extrahieren falls vorhanden
                        if (voice.TryGetProperty("labels", out var labels))
                        {
                            if (labels.TryGetProperty("gender", out var gender))
                            {
                                voiceInfo.Gender = gender.GetString()?.ToLower() ?? "neutral";
                            }
                        }

                        // Preview URL
                        if (voice.TryGetProperty("preview_url", out var previewUrl))
                        {
                            voiceInfo.PreviewUrl = previewUrl.GetString();
                        }

                        voices.Add(voiceInfo);
                    }
                }

                _cachedVoices = voices;
                return voices;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ElevenLabs Voices abrufen Fehler: {ex.Message}");
                return Array.Empty<TtsVoiceInfo>();
            }
        }

        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            // ElevenLabs rechnet nach Zeichen, nicht Tokens
            // Wir geben trotzdem eine Token-Schaetzung zurueck fuer Vergleichbarkeit
            return (int)Math.Ceiling(text.Length / 4.0);
        }

        private static TtsErrorCode MapHttpErrorCode(System.Net.HttpStatusCode statusCode, string errorBody)
        {
            return statusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => TtsErrorCode.AuthenticationError,
                System.Net.HttpStatusCode.Forbidden => TtsErrorCode.AuthenticationError,
                System.Net.HttpStatusCode.TooManyRequests => TtsErrorCode.RateLimitExceeded,
                System.Net.HttpStatusCode.BadRequest when errorBody.Contains("voice") => TtsErrorCode.InvalidVoice,
                System.Net.HttpStatusCode.BadRequest when errorBody.Contains("quota") => TtsErrorCode.QuotaExceeded,
                System.Net.HttpStatusCode.BadRequest => TtsErrorCode.InvalidText,
                System.Net.HttpStatusCode.InternalServerError => TtsErrorCode.ServerError,
                System.Net.HttpStatusCode.ServiceUnavailable => TtsErrorCode.ServerError,
                _ => TtsErrorCode.UnknownError
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }
}
