using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Media;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Service fuer die Audio-Preview-Wiedergabe von Quest-Audio-Dateien.
    /// Verwendet System.Windows.Media.MediaPlayer fuer die Wiedergabe.
    /// </summary>
    public class AudioPreviewService : IDisposable
    {
        private readonly MediaPlayer _mediaPlayer;
        private bool _disposed;

        // Aktuelle Wiedergabe-Informationen
        private int _currentQuestId;
        private string _currentGender = "";
        private string _currentTitle = "";

        /// <summary>
        /// Event wird ausgeloest wenn sich der Wiedergabe-Status aendert.
        /// </summary>
        public event EventHandler<AudioPreviewStatusEventArgs>? StatusChanged;

        /// <summary>
        /// Event wird ausgeloest wenn die Wiedergabe beendet ist.
        /// </summary>
        public event EventHandler? PlaybackEnded;

        /// <summary>
        /// Gibt an, ob gerade eine Wiedergabe laeuft.
        /// </summary>
        public bool IsPlaying { get; private set; }

        /// <summary>
        /// Lautstaerke (0.0 bis 1.0).
        /// </summary>
        public double Volume
        {
            get => _mediaPlayer.Volume;
            set => _mediaPlayer.Volume = Math.Clamp(value, 0.0, 1.0);
        }

        /// <summary>
        /// Aktuelle Quest-ID die abgespielt wird (0 wenn keine).
        /// </summary>
        public int CurrentQuestId => _currentQuestId;

        /// <summary>
        /// Aktuelles Gender das abgespielt wird.
        /// </summary>
        public string CurrentGender => _currentGender;

        /// <summary>
        /// Aktueller Quest-Titel der abgespielt wird.
        /// </summary>
        public string CurrentTitle => _currentTitle;

        public AudioPreviewService()
        {
            _mediaPlayer = new MediaPlayer();
            _mediaPlayer.MediaEnded += OnMediaEnded;
            _mediaPlayer.MediaFailed += OnMediaFailed;

            // Standard-Lautstaerke
            _mediaPlayer.Volume = 0.8;
        }

        /// <summary>
        /// Spielt die Audio-Preview fuer eine Quest ab.
        /// </summary>
        /// <param name="quest">Die Quest fuer die Audio abgespielt werden soll</param>
        /// <param name="gender">Gender-Code ("male" oder "female")</param>
        /// <param name="rootFolder">TTS-Output-Root-Ordner</param>
        /// <param name="language">Sprachcode (Standard: "deDE")</param>
        /// <returns>PlayPreviewResult mit Erfolgsstatus und ggf. Fehlermeldung</returns>
        public PlayPreviewResult PlayPreview(Quest quest, string gender, string rootFolder, string language = "deDE")
        {
            if (quest == null)
            {
                return PlayPreviewResult.Error("Keine Quest ausgewaehlt.");
            }

            if (string.IsNullOrWhiteSpace(rootFolder))
            {
                return PlayPreviewResult.Error("Bitte zuerst einen Ausgabeordner fuer TTS konfigurieren.");
            }

            if (string.IsNullOrWhiteSpace(gender))
            {
                gender = "male"; // Standard-Fallback
            }

            gender = gender.ToLowerInvariant();

            // Audio-Index laden
            var audioIndex = AudioIndexWriter.LoadIndex(rootFolder, language);
            var lookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

            // Pruefen ob Eintrag im Index vorhanden
            var entry = AudioIndexWriter.GetEntry(lookup, quest.QuestId, gender);

            if (entry == null)
            {
                // Kein Eintrag gefunden - pruefen ob alternatives Gender verfuegbar
                var alternativeGender = gender == "male" ? "female" : "male";
                var alternativeEntry = AudioIndexWriter.GetEntry(lookup, quest.QuestId, alternativeGender);

                if (alternativeEntry != null)
                {
                    return PlayPreviewResult.Error(
                        $"Fuer Quest {quest.QuestId} existiert keine Audio-Datei fuer '{GetGenderDisplayName(gender)}'.\n" +
                        $"Verfuegbar: {GetGenderDisplayName(alternativeGender)}");
                }

                return PlayPreviewResult.Error(
                    $"Fuer Quest {quest.QuestId} existiert noch keine Audio-Datei.\n" +
                    "Bitte zuerst TTS generieren.");
            }

            // Vollstaendigen Pfad aus RelativePath und RootFolder ermitteln
            var fullPath = GetFullAudioPath(rootFolder, entry.RelativePath);

            // Pruefen ob Datei physisch existiert
            if (!File.Exists(fullPath))
            {
                return PlayPreviewResult.Error(
                    $"Audio-Datei im Index eingetragen, aber nicht gefunden:\n{fullPath}\n\n" +
                    "Bitte TTS neu generieren.");
            }

            // Eventuell laufende Wiedergabe stoppen
            StopPreview();

            // Neue Wiedergabe starten
            try
            {
                _currentQuestId = quest.QuestId;
                _currentGender = gender;
                _currentTitle = quest.Title ?? $"Quest {quest.QuestId}";

                _mediaPlayer.Open(new Uri(fullPath, UriKind.Absolute));
                _mediaPlayer.Play();
                IsPlaying = true;

                // Status-Event ausloesen
                OnStatusChanged(new AudioPreviewStatusEventArgs(
                    AudioPreviewStatus.Playing,
                    $"Spiele Quest {quest.QuestId} - {_currentTitle} ({GetGenderDisplayName(gender)})..."));

                return PlayPreviewResult.Success(fullPath, entry);
            }
            catch (Exception ex)
            {
                IsPlaying = false;
                _currentQuestId = 0;
                _currentGender = "";
                _currentTitle = "";

                return PlayPreviewResult.Error($"Fehler beim Abspielen:\n{ex.Message}");
            }
        }

        /// <summary>
        /// Stoppt die aktuelle Wiedergabe.
        /// </summary>
        public void StopPreview()
        {
            if (IsPlaying)
            {
                _mediaPlayer.Stop();
                _mediaPlayer.Close();
                IsPlaying = false;

                var stoppedQuestId = _currentQuestId;
                _currentQuestId = 0;
                _currentGender = "";
                _currentTitle = "";

                OnStatusChanged(new AudioPreviewStatusEventArgs(
                    AudioPreviewStatus.Stopped,
                    stoppedQuestId > 0 ? $"Preview gestoppt (Quest {stoppedQuestId})." : "Preview gestoppt."));
            }
        }

        /// <summary>
        /// Pausiert die aktuelle Wiedergabe.
        /// </summary>
        public void PausePreview()
        {
            if (IsPlaying)
            {
                _mediaPlayer.Pause();
                OnStatusChanged(new AudioPreviewStatusEventArgs(
                    AudioPreviewStatus.Paused,
                    $"Preview pausiert (Quest {_currentQuestId})..."));
            }
        }

        /// <summary>
        /// Setzt pausierte Wiedergabe fort.
        /// </summary>
        public void ResumePreview()
        {
            if (_currentQuestId > 0 && !IsPlaying)
            {
                _mediaPlayer.Play();
                IsPlaying = true;
                OnStatusChanged(new AudioPreviewStatusEventArgs(
                    AudioPreviewStatus.Playing,
                    $"Spiele Quest {_currentQuestId} - {_currentTitle} ({GetGenderDisplayName(_currentGender)})..."));
            }
        }

        /// <summary>
        /// Prueft ob fuer eine Quest Audio verfuegbar ist.
        /// </summary>
        /// <param name="quest">Die zu pruefende Quest</param>
        /// <param name="gender">Gender-Code</param>
        /// <param name="rootFolder">TTS-Output-Root-Ordner</param>
        /// <param name="language">Sprachcode</param>
        /// <returns>True wenn Audio verfuegbar und Datei existiert</returns>
        public static bool IsAudioAvailable(Quest quest, string gender, string rootFolder, string language = "deDE")
        {
            if (quest == null || string.IsNullOrWhiteSpace(rootFolder))
                return false;

            var audioIndex = AudioIndexWriter.LoadIndex(rootFolder, language);
            var lookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

            var entry = AudioIndexWriter.GetEntry(lookup, quest.QuestId, gender);
            if (entry == null)
                return false;

            var fullPath = GetFullAudioPath(rootFolder, entry.RelativePath);
            return File.Exists(fullPath);
        }

        /// <summary>
        /// Ermittelt verfuegbare Audio-Genders fuer eine Quest.
        /// </summary>
        public static List<string> GetAvailableGenders(Quest quest, string rootFolder, string language = "deDE")
        {
            var result = new List<string>();

            if (quest == null || string.IsNullOrWhiteSpace(rootFolder))
                return result;

            var audioIndex = AudioIndexWriter.LoadIndex(rootFolder, language);
            var lookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

            if (AudioIndexWriter.IsAlreadyVoiced(lookup, quest.QuestId, "male"))
            {
                var entry = AudioIndexWriter.GetEntry(lookup, quest.QuestId, "male");
                if (entry != null)
                {
                    var fullPath = GetFullAudioPath(rootFolder, entry.RelativePath);
                    if (File.Exists(fullPath))
                        result.Add("male");
                }
            }

            if (AudioIndexWriter.IsAlreadyVoiced(lookup, quest.QuestId, "female"))
            {
                var entry = AudioIndexWriter.GetEntry(lookup, quest.QuestId, "female");
                if (entry != null)
                {
                    var fullPath = GetFullAudioPath(rootFolder, entry.RelativePath);
                    if (File.Exists(fullPath))
                        result.Add("female");
                }
            }

            return result;
        }

        /// <summary>
        /// Ermittelt den vollstaendigen Pfad aus Root-Ordner und relativem Pfad.
        /// </summary>
        private static string GetFullAudioPath(string rootFolder, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return "";

            // RelativePath verwendet / als Separator, normalisieren fuer Windows
            var normalizedRelative = relativePath.Replace('/', Path.DirectorySeparatorChar);
            return Path.Combine(rootFolder, normalizedRelative);
        }

        /// <summary>
        /// Gibt den Anzeigenamen fuer ein Gender zurueck.
        /// </summary>
        public static string GetGenderDisplayName(string gender)
        {
            return gender?.ToLowerInvariant() switch
            {
                "male" => "Maennlich",
                "female" => "Weiblich",
                "neutral" => "Neutral",
                _ => gender ?? "Unbekannt"
            };
        }

        private void OnMediaEnded(object? sender, EventArgs e)
        {
            IsPlaying = false;
            var questId = _currentQuestId;
            var title = _currentTitle;

            _currentQuestId = 0;
            _currentGender = "";
            _currentTitle = "";

            OnStatusChanged(new AudioPreviewStatusEventArgs(
                AudioPreviewStatus.Ended,
                $"Wiedergabe beendet (Quest {questId})."));

            PlaybackEnded?.Invoke(this, EventArgs.Empty);
        }

        private void OnMediaFailed(object? sender, ExceptionEventArgs e)
        {
            IsPlaying = false;
            var questId = _currentQuestId;

            _currentQuestId = 0;
            _currentGender = "";
            _currentTitle = "";

            OnStatusChanged(new AudioPreviewStatusEventArgs(
                AudioPreviewStatus.Error,
                $"Wiedergabe-Fehler (Quest {questId}): {e.ErrorException?.Message ?? "Unbekannter Fehler"}"));
        }

        protected virtual void OnStatusChanged(AudioPreviewStatusEventArgs e)
        {
            StatusChanged?.Invoke(this, e);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                StopPreview();
                _mediaPlayer.Close();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Status der Audio-Preview.
    /// </summary>
    public enum AudioPreviewStatus
    {
        Idle,
        Playing,
        Paused,
        Stopped,
        Ended,
        Error
    }

    /// <summary>
    /// Event-Argumente fuer Audio-Preview-Status-Aenderungen.
    /// </summary>
    public class AudioPreviewStatusEventArgs : EventArgs
    {
        public AudioPreviewStatus Status { get; }
        public string Message { get; }

        public AudioPreviewStatusEventArgs(AudioPreviewStatus status, string message)
        {
            Status = status;
            Message = message;
        }
    }

    /// <summary>
    /// Ergebnis eines PlayPreview-Aufrufs.
    /// </summary>
    public class PlayPreviewResult
    {
        public bool IsSuccess { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string? FilePath { get; private set; }
        public QuestAudioIndexEntry? IndexEntry { get; private set; }

        private PlayPreviewResult() { }

        public static PlayPreviewResult Success(string filePath, QuestAudioIndexEntry entry)
        {
            return new PlayPreviewResult
            {
                IsSuccess = true,
                FilePath = filePath,
                IndexEntry = entry
            };
        }

        public static PlayPreviewResult Error(string message)
        {
            return new PlayPreviewResult
            {
                IsSuccess = false,
                ErrorMessage = message
            };
        }
    }
}
