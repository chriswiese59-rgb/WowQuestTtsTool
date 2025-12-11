using System;
using System.IO;
using System.Text;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Service für TTS-Fehlerprotokollierung.
    /// </summary>
    public class TtsErrorLogger
    {
        private static TtsErrorLogger? _instance;
        private static readonly object _lock = new();

        private readonly string _logDirectory;
        private readonly string _currentLogFile;

        public static TtsErrorLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new TtsErrorLogger();
                    }
                }
                return _instance;
            }
        }

        private TtsErrorLogger()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _logDirectory = Path.Combine(baseDir, "logs");

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            _currentLogFile = Path.Combine(_logDirectory, $"tts_errors_{DateTime.Now:yyyy-MM-dd}.txt");
        }

        /// <summary>
        /// Protokolliert einen TTS-Fehler.
        /// </summary>
        public void LogError(int questId, string questTitle, string error, string? voiceType = null)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
                sb.AppendLine($"  Quest: {questId} - {questTitle}");
                if (!string.IsNullOrEmpty(voiceType))
                {
                    sb.AppendLine($"  Voice: {voiceType}");
                }
                sb.AppendLine($"  Error: {error}");
                sb.AppendLine();

                File.AppendAllText(_currentLogFile, sb.ToString());
            }
            catch
            {
                // Fehler beim Logging ignorieren
            }
        }

        /// <summary>
        /// Protokolliert einen Batch-Start.
        /// </summary>
        public void LogBatchStart(int questCount)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(new string('=', 60));
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BATCH START");
                sb.AppendLine($"  Quests to process: {questCount}");
                sb.AppendLine(new string('=', 60));
                sb.AppendLine();

                File.AppendAllText(_currentLogFile, sb.ToString());
            }
            catch
            {
                // Fehler beim Logging ignorieren
            }
        }

        /// <summary>
        /// Protokolliert ein Batch-Ende.
        /// </summary>
        public void LogBatchEnd(int successful, int failed, int skipped)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(new string('-', 60));
                sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] BATCH END");
                sb.AppendLine($"  Successful: {successful}");
                sb.AppendLine($"  Failed: {failed}");
                sb.AppendLine($"  Skipped: {skipped}");
                sb.AppendLine(new string('-', 60));
                sb.AppendLine();

                File.AppendAllText(_currentLogFile, sb.ToString());
            }
            catch
            {
                // Fehler beim Logging ignorieren
            }
        }

        /// <summary>
        /// Gibt den Pfad zur aktuellen Log-Datei zurück.
        /// </summary>
        public string CurrentLogFile => _currentLogFile;

        /// <summary>
        /// Gibt den Pfad zum Log-Verzeichnis zurück.
        /// </summary>
        public string LogDirectory => _logDirectory;

        /// <summary>
        /// Prüft, ob heute bereits Fehler protokolliert wurden.
        /// </summary>
        public bool HasTodaysErrors => File.Exists(_currentLogFile);

        /// <summary>
        /// Liest die heutigen Fehler.
        /// </summary>
        public string GetTodaysErrors()
        {
            if (!File.Exists(_currentLogFile))
                return string.Empty;

            try
            {
                return File.ReadAllText(_currentLogFile);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
