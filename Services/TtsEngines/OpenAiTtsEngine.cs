using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services.TtsEngines
{
    /// <summary>
    /// OpenAI TTS Engine Implementation.
    /// Unterstuetzt tts-1 und tts-1-hd Modelle.
    /// </summary>
    public class OpenAiTtsEngine : ITtsEngine, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly TtsEngineSettings _settings;
        private List<TtsVoiceInfo>? _cachedVoices;
        private bool _disposed;

        // OpenAI TTS verfuegbare Stimmen
        private static readonly List<TtsVoiceInfo> OpenAiVoices = new()
        {
            new TtsVoiceInfo { VoiceId = "alloy", DisplayName = "Alloy", Gender = "neutral", LanguageCodes = new() { "de-DE", "en-US" } },
            new TtsVoiceInfo { VoiceId = "echo", DisplayName = "Echo", Gender = "male", LanguageCodes = new() { "de-DE", "en-US" } },
            new TtsVoiceInfo { VoiceId = "fable", DisplayName = "Fable", Gender = "neutral", LanguageCodes = new() { "de-DE", "en-US" } },
            new TtsVoiceInfo { VoiceId = "onyx", DisplayName = "Onyx", Gender = "male", LanguageCodes = new() { "de-DE", "en-US" } },
            new TtsVoiceInfo { VoiceId = "nova", DisplayName = "Nova", Gender = "female", LanguageCodes = new() { "de-DE", "en-US" } },
            new TtsVoiceInfo { VoiceId = "shimmer", DisplayName = "Shimmer", Gender = "female", LanguageCodes = new() { "de-DE", "en-US" } }
        };

        public string EngineId => "OpenAI";
        public string DisplayName => "OpenAI TTS";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.OpenAi.ApiKey);
        public bool IsAvailable => IsConfigured;

        public IReadOnlyList<TtsVoiceInfo> SupportedVoices => _cachedVoices ?? OpenAiVoices;

        private OpenAiTtsSettings Settings => _settings.OpenAi;

        public OpenAiTtsEngine() : this(TtsEngineSettings.Instance)
        {
        }

        public OpenAiTtsEngine(TtsEngineSettings settings)
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
                return TtsResult.Failed(TtsErrorCode.EngineNotAvailable, "OpenAI TTS ist nicht konfiguriert. Bitte API-Key eingeben.");
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return TtsResult.Failed(TtsErrorCode.InvalidText, "Text darf nicht leer sein.");
            }

            // OpenAI hat ein Limit von 4096 Zeichen
            if (request.Text.Length > 4096)
            {
                return TtsResult.Failed(TtsErrorCode.InvalidText, $"Text ist zu lang ({request.Text.Length} Zeichen). Maximum: 4096 Zeichen.");
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Stimme basierend auf Geschlecht waehlen
                var voice = request.VoiceGender?.ToLower() == "female"
                    ? Settings.FemaleVoice
                    : Settings.MaleVoice;

                if (!string.IsNullOrWhiteSpace(request.VoiceId))
                {
                    voice = request.VoiceId;
                }

                // Request-Body erstellen
                var requestBody = new
                {
                    model = Settings.Model,
                    input = request.Text,
                    voice = voice,
                    response_format = Settings.ResponseFormat,
                    speed = Settings.Speed
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // HTTP-Request vorbereiten
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{Settings.BaseUrl}/audio/speech")
                {
                    Content = content
                };
                requestMessage.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Settings.ApiKey);

                // Request senden
                var response = await _httpClient.SendAsync(requestMessage, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    var errorCode = MapHttpErrorCode(response.StatusCode, errorBody);
                    return TtsResult.Failed(errorCode, $"OpenAI API Fehler: {response.StatusCode} - {errorBody}");
                }

                // Audio-Daten lesen
                var audioData = await response.Content.ReadAsByteArrayAsync(ct);

                // Datei speichern
                var outputPath = request.OutputPath;
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    var tempDir = Path.GetTempPath();
                    outputPath = Path.Combine(tempDir, $"openai_tts_{request.QuestId}_{Guid.NewGuid():N}.{Settings.ResponseFormat}");
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

                Debug.WriteLine($"OpenAI TTS: {charCount} Zeichen, {estimatedTokens} Tokens, {stopwatch.ElapsedMilliseconds}ms");

                return TtsResult.Successful(outputPath, charCount, estimatedTokens, stopwatch.ElapsedMilliseconds);
            }
            catch (TaskCanceledException)
            {
                return TtsResult.Failed(TtsErrorCode.Timeout, "OpenAI TTS Request wurde abgebrochen (Timeout).");
            }
            catch (HttpRequestException ex)
            {
                return TtsResult.Failed(TtsErrorCode.NetworkError, $"Netzwerkfehler: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OpenAI TTS Fehler: {ex}");
                return TtsResult.Failed(TtsErrorCode.UnknownError, $"Unerwarteter Fehler: {ex.Message}");
            }
        }

        public async Task<TtsValidationResult> ValidateConfigurationAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return TtsValidationResult.Invalid("API-Key ist nicht konfiguriert.");
            }

            try
            {
                // Einfachen Test-Request senden
                var testRequest = new TtsRequest
                {
                    Text = "Test",
                    VoiceGender = "male",
                    OutputPath = Path.Combine(Path.GetTempPath(), $"openai_test_{Guid.NewGuid():N}.mp3")
                };

                var result = await GenerateAudioAsync(testRequest, ct);

                // Temp-Datei loeschen
                if (File.Exists(testRequest.OutputPath))
                {
                    File.Delete(testRequest.OutputPath);
                }

                if (result.Success)
                {
                    return TtsValidationResult.Valid();
                }

                return TtsValidationResult.Invalid(result.ErrorMessage ?? "Unbekannter Fehler");
            }
            catch (Exception ex)
            {
                return TtsValidationResult.Invalid($"Validierung fehlgeschlagen: {ex.Message}");
            }
        }

        public Task<IReadOnlyList<TtsVoiceInfo>> GetAvailableVoicesAsync(CancellationToken ct = default)
        {
            // OpenAI hat feste Stimmen, keine API zum Abrufen
            _cachedVoices = new List<TtsVoiceInfo>(OpenAiVoices);
            return Task.FromResult<IReadOnlyList<TtsVoiceInfo>>(_cachedVoices);
        }

        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            // Approximation: ~4 Zeichen pro Token fuer Deutsch
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
