using System;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Dummy-Implementierung von ITtsService für Entwicklung und Tests.
    /// Gibt Fake-Daten zurück, bis eine echte TTS-API implementiert wird.
    /// </summary>
    public class DummyTtsService : ITtsService
    {
        public bool IsConfigured => true;

        public string ProviderName => "Dummy (Test)";

        public Task<byte[]> GenerateMp3Async(string text, string languageCode, string voiceId)
        {
            // Simuliere eine kurze Verzögerung wie bei einem echten API-Call
            return Task.Run(async () =>
            {
                await Task.Delay(500); // 500ms simulierte Latenz

                // Erzeuge ein minimales "Fake" MP3 (nur Header-Bytes)
                // In der echten Implementierung kommt hier das Audio vom TTS-Provider
                var fakeHeader = new byte[]
                {
                    // ID3v2 Tag Header (simuliert)
                    0x49, 0x44, 0x33, // "ID3"
                    0x04, 0x00,       // Version
                    0x00,             // Flags
                    0x00, 0x00, 0x00, 0x00, // Size

                    // MP3 Frame Header (simuliert)
                    0xFF, 0xFB, 0x90, 0x00
                };

                // Füge etwas "Inhalt" hinzu basierend auf Textlänge
                var contentLength = Math.Min(text.Length * 10, 1000);
                var result = new byte[fakeHeader.Length + contentLength];
                Array.Copy(fakeHeader, result, fakeHeader.Length);

                // Fülle den Rest mit Dummy-Daten
                var random = new Random(text.GetHashCode());
                for (int i = fakeHeader.Length; i < result.Length; i++)
                {
                    result[i] = (byte)random.Next(256);
                }

                return result;
            });
        }
    }
}
