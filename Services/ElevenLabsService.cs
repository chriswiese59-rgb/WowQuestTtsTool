using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    public class ElevenLabsService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://api.elevenlabs.io/v1";

        public ElevenLabsService(string apiKey)
        {
            _apiKey = apiKey;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_apiKey) &&
                                    _apiKey != "YOUR_ELEVENLABS_API_KEY_HERE";

        /// <summary>
        /// Generiert Audio aus Text via ElevenLabs API.
        /// </summary>
        public async Task<byte[]?> GenerateAudioAsync(
            string text,
            string voiceId,
            string modelId = "eleven_multilingual_v2",
            double stability = 0.5,
            double similarityBoost = 0.75)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("ElevenLabs API-Key nicht konfiguriert.");
            }

            var url = $"{BaseUrl}/text-to-speech/{voiceId}";

            var requestBody = new
            {
                text = text,
                model_id = modelId,
                voice_settings = new
                {
                    stability = stability,
                    similarity_boost = similarityBoost,
                    style = 0.0,
                    use_speaker_boost = true
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            try
            {
                var response = await _httpClient.PostAsync(url, content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"ElevenLabs API Fehler ({response.StatusCode}): {error}");
                }

                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"Fehler bei der TTS-Generierung: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Generiert Audio und speichert es als Datei.
        /// </summary>
        public async Task<string> GenerateAndSaveAsync(
            string text,
            string voiceId,
            string outputPath,
            string modelId = "eleven_multilingual_v2")
        {
            var audioData = await GenerateAudioAsync(text, voiceId, modelId);

            if (audioData == null || audioData.Length == 0)
            {
                throw new Exception("Keine Audio-Daten empfangen.");
            }

            // Verzeichnis erstellen falls nötig
            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(outputPath, audioData);
            return outputPath;
        }

        /// <summary>
        /// Ruft verfügbare Stimmen ab.
        /// </summary>
        public async Task<string> GetVoicesAsync()
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException("ElevenLabs API-Key nicht konfiguriert.");
            }

            var response = await _httpClient.GetAsync($"{BaseUrl}/voices");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
