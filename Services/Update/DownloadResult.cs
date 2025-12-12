namespace WowQuestTtsTool.Services.Update
{
    /// <summary>
    /// Ergebnis eines Update-Downloads.
    /// </summary>
    public class DownloadResult
    {
        /// <summary>
        /// Ob der Download erfolgreich war.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Pfad zur heruntergeladenen ZIP-Datei.
        /// </summary>
        public string ZipFilePath { get; set; } = "";

        /// <summary>
        /// Verzeichnis, in das das ZIP entpackt wurde.
        /// </summary>
        public string ExtractedDirectory { get; set; } = "";

        /// <summary>
        /// Fehlermeldung, falls der Download fehlgeschlagen ist.
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Größe der heruntergeladenen Datei in Bytes.
        /// </summary>
        public long DownloadedBytes { get; set; }

        /// <summary>
        /// Ob der Hash verifiziert wurde (falls vorhanden).
        /// </summary>
        public bool HashVerified { get; set; }

        /// <summary>
        /// Erstellt ein erfolgreiches Ergebnis.
        /// </summary>
        public static DownloadResult Successful(string zipPath, string extractedDir, long bytes, bool hashVerified = false)
        {
            return new DownloadResult
            {
                Success = true,
                ZipFilePath = zipPath,
                ExtractedDirectory = extractedDir,
                DownloadedBytes = bytes,
                HashVerified = hashVerified
            };
        }

        /// <summary>
        /// Erstellt ein fehlgeschlagenes Ergebnis.
        /// </summary>
        public static DownloadResult Failed(string errorMessage)
        {
            return new DownloadResult
            {
                Success = false,
                ErrorMessage = errorMessage
            };
        }
    }
}
