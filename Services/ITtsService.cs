using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Interface für Text-to-Speech Services.
    /// </summary>
    public interface ITtsService
    {
        /// <summary>
        /// Generiert eine TTS-Audiodatei für den gegebenen Text.
        /// </summary>
        /// <param name="text">Der zu sprechende Text.</param>
        /// <param name="languageCode">Sprachcode, z.B. "deDE".</param>
        /// <param name="voiceId">ID oder Name der zu verwendenden Stimme.</param>
        /// <returns>Byte-Array mit MP3-Daten.</returns>
        Task<byte[]> GenerateMp3Async(string text, string languageCode, string voiceId);

        /// <summary>
        /// Prüft, ob der Service konfiguriert und einsatzbereit ist.
        /// </summary>
        bool IsConfigured { get; }

        /// <summary>
        /// Name des TTS-Providers (z.B. "ElevenLabs", "Gemini", "Dummy").
        /// </summary>
        string ProviderName { get; }
    }
}
