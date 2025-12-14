using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services.TtsEngines
{
    /// <summary>
    /// Google Cloud Text-to-Speech Engine Implementation.
    /// Unterstuetzt API-Key und Service Account (OAuth2) Authentifizierung.
    /// </summary>
    public class GoogleCloudTtsEngine : ITtsEngine, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly TtsEngineSettings _settings;
        private List<TtsVoiceInfo>? _cachedVoices;
        private string? _cachedAccessToken;
        private DateTime _tokenExpiresAt;
        private bool _disposed;

        private const string TtsApiBaseUrl = "https://texttospeech.googleapis.com/v1";

        public string EngineId => "Gemini"; // Behalte ID fuer Kompatibilitaet
        public string DisplayName => "Google Cloud TTS";

        public bool IsConfigured => Settings.IsConfigured;
        public bool IsAvailable => IsConfigured;

        public IReadOnlyList<TtsVoiceInfo> SupportedVoices =>
            _cachedVoices ?? (IReadOnlyList<TtsVoiceInfo>)Array.Empty<TtsVoiceInfo>();

        private GeminiTtsSettings Settings => _settings.Gemini;

        public GoogleCloudTtsEngine() : this(TtsEngineSettings.Instance)
        {
        }

        public GoogleCloudTtsEngine(TtsEngineSettings settings)
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
                return TtsResult.Failed(TtsErrorCode.EngineNotAvailable,
                    "Google Cloud TTS ist nicht konfiguriert. Bitte API-Key oder Service Account JSON-Datei angeben.");
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return TtsResult.Failed(TtsErrorCode.InvalidText, "Text darf nicht leer sein.");
            }

            var stopwatch = Stopwatch.StartNew();

            try
            {
                // Voice-Name basierend auf Geschlecht waehlen
                var voiceName = request.VoiceGender?.ToLower() == "female"
                    ? Settings.FemaleVoice
                    : Settings.MaleVoice;

                if (!string.IsNullOrWhiteSpace(request.VoiceId))
                {
                    voiceName = request.VoiceId;
                }

                if (string.IsNullOrWhiteSpace(voiceName))
                {
                    return TtsResult.Failed(TtsErrorCode.InvalidVoice, "Keine Stimme konfiguriert.");
                }

                // Request-Body erstellen
                var requestBody = new
                {
                    input = new
                    {
                        text = request.Text
                    },
                    voice = new
                    {
                        languageCode = Settings.LanguageCode,
                        name = voiceName
                    },
                    audioConfig = new
                    {
                        audioEncoding = Settings.AudioEncoding,
                        speakingRate = Settings.SpeakingRate,
                        pitch = Settings.Pitch
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // HTTP-Request vorbereiten
                var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{TtsApiBaseUrl}/text:synthesize")
                {
                    Content = content
                };

                // Authentifizierung hinzufuegen
                await AddAuthenticationAsync(requestMessage, ct);

                // Request senden
                var response = await _httpClient.SendAsync(requestMessage, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    var errorCode = MapHttpErrorCode(response.StatusCode, errorBody);
                    return TtsResult.Failed(errorCode, $"Google Cloud TTS API Fehler: {response.StatusCode} - {errorBody}");
                }

                // Response parsen
                var jsonResponse = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonResponse);

                if (!doc.RootElement.TryGetProperty("audioContent", out var audioContentElement))
                {
                    return TtsResult.Failed(TtsErrorCode.UnknownError, "Keine Audio-Daten in der Antwort.");
                }

                // Base64 dekodieren
                var audioBase64 = audioContentElement.GetString();
                if (string.IsNullOrEmpty(audioBase64))
                {
                    return TtsResult.Failed(TtsErrorCode.UnknownError, "Audio-Inhalt ist leer.");
                }

                var audioData = Convert.FromBase64String(audioBase64);

                // Datei speichern
                var outputPath = request.OutputPath;
                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    var extension = Settings.AudioEncoding.ToLower() switch
                    {
                        "mp3" => ".mp3",
                        "ogg_opus" => ".ogg",
                        "linear16" => ".wav",
                        _ => ".mp3"
                    };
                    var tempDir = Path.GetTempPath();
                    outputPath = Path.Combine(tempDir, $"google_tts_{request.QuestId}_{Guid.NewGuid():N}{extension}");
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

                Debug.WriteLine($"Google Cloud TTS: {charCount} Zeichen, {stopwatch.ElapsedMilliseconds}ms");

                return TtsResult.Successful(outputPath, charCount, estimatedTokens, stopwatch.ElapsedMilliseconds);
            }
            catch (TaskCanceledException)
            {
                return TtsResult.Failed(TtsErrorCode.Timeout, "Google Cloud TTS Request wurde abgebrochen (Timeout).");
            }
            catch (HttpRequestException ex)
            {
                return TtsResult.Failed(TtsErrorCode.NetworkError, $"Netzwerkfehler: {ex.Message}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Google Cloud TTS Fehler: {ex}");
                return TtsResult.Failed(TtsErrorCode.UnknownError, $"Unerwarteter Fehler: {ex.Message}");
            }
        }

        /// <summary>
        /// Fuegt die Authentifizierung zum Request hinzu.
        /// </summary>
        private async Task AddAuthenticationAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Option 1: API-Key (einfacher, aber eingeschraenkt)
            if (!string.IsNullOrWhiteSpace(Settings.ApiKey))
            {
                // API-Key als Query-Parameter
                var uriBuilder = new UriBuilder(request.RequestUri!);
                var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                query["key"] = Settings.ApiKey;
                uriBuilder.Query = query.ToString();
                request.RequestUri = uriBuilder.Uri;
                return;
            }

            // Option 2: Service Account (OAuth2)
            if (!string.IsNullOrWhiteSpace(Settings.ServiceAccountJsonPath))
            {
                var accessToken = await GetAccessTokenAsync(ct);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

                // Project-ID Header hinzufuegen wenn vorhanden
                if (!string.IsNullOrWhiteSpace(Settings.ProjectId))
                {
                    request.Headers.Add("x-goog-user-project", Settings.ProjectId);
                }
            }
        }

        /// <summary>
        /// Holt oder erneuert den OAuth2 Access Token.
        /// </summary>
        private async Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            // Cached Token verwenden wenn noch gueltig
            if (!string.IsNullOrEmpty(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiresAt.AddMinutes(-5))
            {
                return _cachedAccessToken;
            }

            // Service Account JSON laden
            if (!File.Exists(Settings.ServiceAccountJsonPath))
            {
                throw new FileNotFoundException($"Service Account JSON nicht gefunden: {Settings.ServiceAccountJsonPath}");
            }

            var serviceAccountJson = await File.ReadAllTextAsync(Settings.ServiceAccountJsonPath, ct);
            using var doc = JsonDocument.Parse(serviceAccountJson);
            var root = doc.RootElement;

            var clientEmail = root.GetProperty("client_email").GetString()
                ?? throw new InvalidOperationException("client_email nicht in Service Account JSON gefunden.");
            var privateKeyPem = root.GetProperty("private_key").GetString()
                ?? throw new InvalidOperationException("private_key nicht in Service Account JSON gefunden.");

            // JWT erstellen
            var jwt = CreateJwt(clientEmail, privateKeyPem);

            // Token anfordern
            var tokenRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "urn:ietf:params:oauth:grant-type:jwt-bearer"),
                new KeyValuePair<string, string>("assertion", jwt)
            });

            var tokenResponse = await _httpClient.PostAsync("https://oauth2.googleapis.com/token", tokenRequest, ct);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var errorBody = await tokenResponse.Content.ReadAsStringAsync(ct);
                throw new InvalidOperationException($"Token-Anforderung fehlgeschlagen: {errorBody}");
            }

            var tokenJson = await tokenResponse.Content.ReadAsStringAsync(ct);
            using var tokenDoc = JsonDocument.Parse(tokenJson);

            _cachedAccessToken = tokenDoc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("access_token nicht in Antwort gefunden.");

            var expiresIn = tokenDoc.RootElement.TryGetProperty("expires_in", out var expiresInElement)
                ? expiresInElement.GetInt32()
                : 3600;

            _tokenExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn);

            return _cachedAccessToken;
        }

        /// <summary>
        /// Erstellt ein JWT fuer die Service Account Authentifizierung.
        /// </summary>
        private static string CreateJwt(string clientEmail, string privateKeyPem)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var expiry = now + 3600; // 1 Stunde

            var header = new
            {
                alg = "RS256",
                typ = "JWT"
            };

            var payload = new
            {
                iss = clientEmail,
                scope = "https://www.googleapis.com/auth/cloud-platform",
                aud = "https://oauth2.googleapis.com/token",
                iat = now,
                exp = expiry
            };

            var headerJson = JsonSerializer.Serialize(header);
            var payloadJson = JsonSerializer.Serialize(payload);

            var headerBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
            var payloadBase64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));

            var dataToSign = $"{headerBase64}.{payloadBase64}";

            // RSA-Signatur erstellen
            var signatureBytes = SignWithRsa(dataToSign, privateKeyPem);
            var signatureBase64 = Base64UrlEncode(signatureBytes);

            return $"{dataToSign}.{signatureBase64}";
        }

        /// <summary>
        /// Signiert Daten mit RSA.
        /// </summary>
        private static byte[] SignWithRsa(string data, string privateKeyPem)
        {
            // PEM zu RSA konvertieren
            var privateKey = privateKeyPem
                .Replace("-----BEGIN PRIVATE KEY-----", "")
                .Replace("-----END PRIVATE KEY-----", "")
                .Replace("-----BEGIN RSA PRIVATE KEY-----", "")
                .Replace("-----END RSA PRIVATE KEY-----", "")
                .Replace("\n", "")
                .Replace("\r", "")
                .Trim();

            var keyBytes = Convert.FromBase64String(privateKey);

            using var rsa = RSA.Create();
            rsa.ImportPkcs8PrivateKey(keyBytes, out _);

            return rsa.SignData(Encoding.UTF8.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }

        /// <summary>
        /// Base64 URL-safe Encoding.
        /// </summary>
        private static string Base64UrlEncode(byte[] data)
        {
            return Convert.ToBase64String(data)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        public async Task<TtsValidationResult> ValidateConfigurationAsync(CancellationToken ct = default)
        {
            if (!IsConfigured)
            {
                return TtsValidationResult.Invalid("Weder API-Key noch Service Account JSON ist konfiguriert.");
            }

            try
            {
                // Stimmen abrufen als Validierungstest
                var voices = await GetAvailableVoicesAsync(ct);
                if (voices.Count == 0)
                {
                    return TtsValidationResult.Invalid("Keine Stimmen verfuegbar. Bitte Konfiguration pruefen.");
                }

                return TtsValidationResult.Valid();
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
                var requestMessage = new HttpRequestMessage(HttpMethod.Get, $"{TtsApiBaseUrl}/voices");
                await AddAuthenticationAsync(requestMessage, ct);

                // Language-Filter hinzufuegen
                var uriBuilder = new UriBuilder(requestMessage.RequestUri!);
                var query = System.Web.HttpUtility.ParseQueryString(uriBuilder.Query);
                query["languageCode"] = Settings.LanguageCode;
                uriBuilder.Query = query.ToString();
                requestMessage.RequestUri = uriBuilder.Uri;

                var response = await _httpClient.SendAsync(requestMessage, ct);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"Google Cloud TTS Voices abrufen fehlgeschlagen: {response.StatusCode}");
                    return Array.Empty<TtsVoiceInfo>();
                }

                var jsonResponse = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(jsonResponse);

                var voices = new List<TtsVoiceInfo>();

                if (doc.RootElement.TryGetProperty("voices", out var voicesArray))
                {
                    foreach (var voice in voicesArray.EnumerateArray())
                    {
                        var name = voice.GetProperty("name").GetString() ?? "";

                        // SSML-Gender bestimmen
                        var ssmlGender = "neutral";
                        if (voice.TryGetProperty("ssmlGender", out var genderElement))
                        {
                            ssmlGender = genderElement.GetString()?.ToLower() ?? "neutral";
                        }

                        // Language Codes sammeln
                        var languageCodes = new List<string>();
                        if (voice.TryGetProperty("languageCodes", out var langCodesArray))
                        {
                            foreach (var langCode in langCodesArray.EnumerateArray())
                            {
                                var code = langCode.GetString();
                                if (!string.IsNullOrEmpty(code))
                                    languageCodes.Add(code);
                            }
                        }

                        // Natural Sample Rate fuer Qualitaetsinfo
                        var sampleRate = 0;
                        if (voice.TryGetProperty("naturalSampleRateHertz", out var sampleRateElement))
                        {
                            sampleRate = sampleRateElement.GetInt32();
                        }

                        var voiceInfo = new TtsVoiceInfo
                        {
                            VoiceId = name,
                            DisplayName = $"{name} ({sampleRate / 1000}kHz)",
                            Gender = ssmlGender,
                            LanguageCodes = languageCodes
                        };

                        voices.Add(voiceInfo);
                    }
                }

                _cachedVoices = voices;
                return voices;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Google Cloud TTS Voices abrufen Fehler: {ex.Message}");
                return Array.Empty<TtsVoiceInfo>();
            }
        }

        public int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;

            // Google Cloud TTS berechnet nach Zeichen
            // Wir geben eine Token-Schaetzung fuer Vergleichbarkeit
            return (int)Math.Ceiling(text.Length / 4.0);
        }

        private static TtsErrorCode MapHttpErrorCode(System.Net.HttpStatusCode statusCode, string errorBody)
        {
            return statusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized => TtsErrorCode.AuthenticationError,
                System.Net.HttpStatusCode.Forbidden => TtsErrorCode.AuthenticationError,
                System.Net.HttpStatusCode.TooManyRequests => TtsErrorCode.RateLimitExceeded,
                System.Net.HttpStatusCode.BadRequest when errorBody.Contains("INVALID_ARGUMENT") => TtsErrorCode.InvalidVoice,
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
