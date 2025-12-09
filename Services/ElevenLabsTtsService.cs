using System;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// ITtsService-Implementierung für ElevenLabs.
    /// Wrapppt den bestehenden ElevenLabsService für die einheitliche TTS-Schnittstelle.
    /// </summary>
    public class ElevenLabsTtsService : ITtsService
    {
        private readonly ElevenLabsService _elevenLabsService;
        private readonly TtsConfigService _configService;

        public ElevenLabsTtsService(ElevenLabsService elevenLabsService, TtsConfigService configService)
        {
            _elevenLabsService = elevenLabsService ?? throw new ArgumentNullException(nameof(elevenLabsService));
            _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        }

        public bool IsConfigured => _elevenLabsService.IsConfigured;

        public string ProviderName => "ElevenLabs";

        /// <summary>
        /// Generiert MP3-Audio über die ElevenLabs API.
        /// </summary>
        /// <param name="text">Der zu sprechende Text.</param>
        /// <param name="languageCode">Sprachcode (wird für Model-Auswahl verwendet).</param>
        /// <param name="voiceId">Voice-ID oder Profilname (z.B. "neutral_male").</param>
        /// <returns>MP3-Audiodaten als Byte-Array.</returns>
        public async Task<byte[]> GenerateMp3Async(string text, string languageCode, string voiceId)
        {
            if (!IsConfigured)
            {
                throw new InvalidOperationException(
                    "ElevenLabs API ist nicht konfiguriert. " +
                    "Bitte API-Key in den Einstellungen hinterlegen.");
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Text darf nicht leer sein.", nameof(text));
            }

            // Voice-ID aus Profil-Name auflösen (falls es ein Profilname ist)
            var resolvedVoiceId = ResolveVoiceId(voiceId);

            // Config-Einstellungen holen
            var config = _configService.Config;
            var modelId = config.ElevenLabs.ModelId;
            var stability = config.ElevenLabs.VoiceSettings.Stability;
            var similarityBoost = config.ElevenLabs.VoiceSettings.SimilarityBoost;

            // Audio generieren
            var audioData = await _elevenLabsService.GenerateAudioAsync(
                text,
                resolvedVoiceId,
                modelId,
                stability,
                similarityBoost);

            if (audioData == null || audioData.Length == 0)
            {
                throw new Exception("Keine Audio-Daten von ElevenLabs empfangen.");
            }

            return audioData;
        }

        /// <summary>
        /// Löst einen Voice-Profilnamen zu einer echten Voice-ID auf.
        /// Falls bereits eine Voice-ID übergeben wird, wird diese zurückgegeben.
        /// </summary>
        private string ResolveVoiceId(string voiceIdOrProfile)
        {
            if (string.IsNullOrWhiteSpace(voiceIdOrProfile))
            {
                // Fallback auf Default-Voice aus Config
                return _configService.Config.ElevenLabs.VoiceId;
            }

            // Versuche als Profilname aufzulösen
            var resolvedId = _configService.GetVoiceId(voiceIdOrProfile);

            // Falls GetVoiceId den gleichen Wert zurückgibt (kein Profil gefunden),
            // prüfen ob es eine gültige Voice-ID sein könnte (ElevenLabs IDs sind 20+ Zeichen)
            if (resolvedId == voiceIdOrProfile && voiceIdOrProfile.Length < 15)
            {
                // Wahrscheinlich ein unbekannter Profilname, Fallback auf Default
                return _configService.Config.ElevenLabs.VoiceId;
            }

            return resolvedId;
        }
    }
}
