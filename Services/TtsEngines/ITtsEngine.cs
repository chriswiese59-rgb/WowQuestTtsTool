using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services.TtsEngines
{
    /// <summary>
    /// Fehlertypen fuer TTS-Operationen.
    /// </summary>
    public enum TtsErrorCode
    {
        None = 0,
        AuthenticationError = 1,
        RateLimitExceeded = 2,
        QuotaExceeded = 3,
        NetworkError = 4,
        Timeout = 5,
        InvalidVoice = 6,
        InvalidText = 7,
        ServerError = 8,
        EngineNotAvailable = 9,
        UnknownError = 99
    }

    /// <summary>
    /// Informationen zu einer TTS-Stimme.
    /// </summary>
    public class TtsVoiceInfo
    {
        public string VoiceId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Gender { get; set; } = "neutral"; // male, female, neutral
        public List<string> LanguageCodes { get; set; } = new();
        public string? PreviewUrl { get; set; }

        public override string ToString() => $"{DisplayName} ({Gender})";
    }

    /// <summary>
    /// Anfrage fuer eine TTS-Generierung.
    /// </summary>
    public class TtsRequest
    {
        public int QuestId { get; set; }
        public string Text { get; set; } = "";
        public string VoiceId { get; set; } = "";
        public string VoiceGender { get; set; } = "male"; // male, female
        public string LanguageCode { get; set; } = "de-DE";
        public string OutputPath { get; set; } = "";
        public string OutputFormat { get; set; } = "mp3";
        public Dictionary<string, object> AdditionalSettings { get; set; } = new();
    }

    /// <summary>
    /// Ergebnis einer TTS-Generierung.
    /// </summary>
    public class TtsResult
    {
        public bool Success { get; set; }
        public string? AudioFilePath { get; set; }
        public byte[]? AudioData { get; set; }
        public int CharacterCount { get; set; }
        public int EstimatedTokens { get; set; }
        public long DurationMs { get; set; }
        public long AudioDurationMs { get; set; }
        public string? ErrorMessage { get; set; }
        public TtsErrorCode ErrorCode { get; set; } = TtsErrorCode.None;

        public static TtsResult Successful(string audioPath, int chars, int tokens, long durationMs, long audioDurationMs = 0)
        {
            return new TtsResult
            {
                Success = true,
                AudioFilePath = audioPath,
                CharacterCount = chars,
                EstimatedTokens = tokens,
                DurationMs = durationMs,
                AudioDurationMs = audioDurationMs,
                ErrorCode = TtsErrorCode.None
            };
        }

        public static TtsResult Failed(TtsErrorCode code, string message)
        {
            return new TtsResult
            {
                Success = false,
                ErrorCode = code,
                ErrorMessage = message
            };
        }
    }

    /// <summary>
    /// Ergebnis der Konfigurationspruefung.
    /// </summary>
    public class TtsValidationResult
    {
        public bool IsValid { get; set; }
        public string? ErrorMessage { get; set; }
        public long? RemainingQuota { get; set; }

        public static TtsValidationResult Valid(long? quota = null) => new() { IsValid = true, RemainingQuota = quota };
        public static TtsValidationResult Invalid(string message) => new() { IsValid = false, ErrorMessage = message };
    }

    /// <summary>
    /// Interface fuer alle TTS-Engine Implementierungen.
    /// </summary>
    public interface ITtsEngine
    {
        /// <summary>
        /// Eindeutiger Identifier der Engine (z.B. "OpenAI", "Gemini", "Claude", "External").
        /// </summary>
        string EngineId { get; }

        /// <summary>
        /// Anzeigename fuer das UI.
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// Gibt an, ob alle notwendigen Konfigurationsdaten vorhanden sind.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Gibt an, ob die Engine aktuell nutzbar ist.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Liste der verfuegbaren Stimmen.
        /// </summary>
        IReadOnlyList<TtsVoiceInfo> SupportedVoices { get; }

        /// <summary>
        /// Generiert Audio aus Text.
        /// </summary>
        Task<TtsResult> GenerateAudioAsync(TtsRequest request, CancellationToken ct = default);

        /// <summary>
        /// Prueft ob die Konfiguration gueltig ist.
        /// </summary>
        Task<TtsValidationResult> ValidateConfigurationAsync(CancellationToken ct = default);

        /// <summary>
        /// Laedt die verfuegbaren Stimmen vom Anbieter.
        /// </summary>
        Task<IReadOnlyList<TtsVoiceInfo>> GetAvailableVoicesAsync(CancellationToken ct = default);

        /// <summary>
        /// Berechnet die geschaetzten Tokens fuer einen Text.
        /// </summary>
        int EstimateTokens(string text);
    }
}
