using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool
{
    public class Quest
    {
        [JsonPropertyName("quest_id")]
        public int QuestId { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("objectives")]
        public string? Objectives { get; set; }

        [JsonPropertyName("completion")]
        public string? Completion { get; set; }

        [JsonPropertyName("zone")]
        public string? Zone { get; set; }

        [JsonPropertyName("is_main_story")]
        public bool IsMainStory { get; set; }

        // Quest-Klassifizierung
        [JsonPropertyName("category")]
        public QuestCategory Category { get; set; } = QuestCategory.Unknown;

        /// <summary>
        /// Gibt an, ob die Quest eine Gruppe erfordert.
        /// </summary>
        [JsonPropertyName("is_group_quest")]
        public bool IsGroupQuest { get; set; }

        /// <summary>
        /// Empfohlene Gruppengröße (0 = Solo, 2-5 = Gruppe, 5+ = Raid).
        /// </summary>
        [JsonPropertyName("suggested_party_size")]
        public int SuggestedPartySize { get; set; }

        /// <summary>
        /// Empfohlenes Spielerlevel für die Quest.
        /// </summary>
        [JsonPropertyName("required_level")]
        public int RequiredLevel { get; set; }

        /// <summary>
        /// Quest-Typ von der Blizzard-API (z.B. "Group", "Dungeon", "Raid").
        /// </summary>
        [JsonPropertyName("quest_type")]
        public string? QuestType { get; set; }

        /// <summary>
        /// Anzeigename der Kategorie (für UI-Binding).
        /// </summary>
        [JsonIgnore]
        public string CategoryDisplayName => Category.ToDisplayName();

        /// <summary>
        /// Kurzname der Kategorie (für DataGrid).
        /// </summary>
        [JsonIgnore]
        public string CategoryShortName => Category.ToShortName();

        /// <summary>
        /// Aktualisiert die Kategorie basierend auf IsMainStory und anderen Feldern.
        /// </summary>
        public void UpdateCategoryFromFields()
        {
            // Wenn bereits eine spezifische Kategorie gesetzt ist, nicht überschreiben
            if (Category != QuestCategory.Unknown)
                return;

            // Aus IsMainStory ableiten (Legacy-Kompatibilität)
            if (IsMainStory)
            {
                Category = QuestCategory.Main;
                return;
            }

            // Aus QuestType ableiten
            if (!string.IsNullOrWhiteSpace(QuestType))
            {
                var type = QuestType.ToLowerInvariant();
                if (type.Contains("group"))
                {
                    Category = QuestCategory.Group;
                    IsGroupQuest = true;
                }
                else if (type.Contains("dungeon"))
                {
                    Category = QuestCategory.Dungeon;
                    IsGroupQuest = true;
                }
                else if (type.Contains("raid"))
                {
                    Category = QuestCategory.Raid;
                    IsGroupQuest = true;
                }
                else if (type.Contains("pvp"))
                {
                    Category = QuestCategory.PvP;
                }
                else if (type.Contains("daily"))
                {
                    Category = QuestCategory.Daily;
                }
                else if (type.Contains("weekly"))
                {
                    Category = QuestCategory.Weekly;
                }
                else if (type.Contains("world"))
                {
                    Category = QuestCategory.World;
                }
                else if (type.Contains("legendary"))
                {
                    Category = QuestCategory.Legendary;
                }
                return;
            }

            // Aus SuggestedPartySize ableiten
            if (SuggestedPartySize >= 10)
            {
                Category = QuestCategory.Raid;
                IsGroupQuest = true;
            }
            else if (SuggestedPartySize >= 2)
            {
                Category = QuestCategory.Group;
                IsGroupQuest = true;
            }
            else
            {
                // Default: Nebenquest
                Category = QuestCategory.Side;
            }
        }

        // === Lokalisierungs-Flags ===

        /// <summary>
        /// Gibt an, ob der Titel auf Deutsch vorhanden ist.
        /// </summary>
        [JsonPropertyName("has_title_de")]
        public bool HasTitleDe { get; set; }

        /// <summary>
        /// Gibt an, ob die Beschreibung auf Deutsch vorhanden ist.
        /// </summary>
        [JsonPropertyName("has_description_de")]
        public bool HasDescriptionDe { get; set; }

        /// <summary>
        /// Gibt an, ob die Ziele auf Deutsch vorhanden sind.
        /// </summary>
        [JsonPropertyName("has_objectives_de")]
        public bool HasObjectivesDe { get; set; }

        /// <summary>
        /// Gibt an, ob der Abschlusstext auf Deutsch vorhanden ist.
        /// </summary>
        [JsonPropertyName("has_completion_de")]
        public bool HasCompletionDe { get; set; }

        /// <summary>
        /// Lokalisierungsstatus der Quest (DE/EN/Mixed/Incomplete).
        /// </summary>
        [JsonPropertyName("localization_status")]
        public QuestLocalizationStatus LocalizationStatus { get; set; } = QuestLocalizationStatus.Incomplete;

        /// <summary>
        /// Gibt an, ob die Quest vollstaendig auf Deutsch lokalisiert ist.
        /// </summary>
        [JsonIgnore]
        public bool IsFullyLocalizedDe => LocalizationStatus == QuestLocalizationStatus.FullyGerman;

        /// <summary>
        /// Kurzname des Lokalisierungsstatus (fuer UI).
        /// </summary>
        [JsonIgnore]
        public string LocalizationStatusShortName => LocalizationStatus.ToShortName();

        /// <summary>
        /// Anzeigename des Lokalisierungsstatus (fuer UI).
        /// </summary>
        [JsonIgnore]
        public string LocalizationStatusDisplayName => LocalizationStatus.ToDisplayName();

        // Flag für TTS-Review
        [JsonPropertyName("tts_reviewed")]
        public bool TtsReviewed { get; set; }

        // Benutzerdefinierter TTS-Text (überschreibt automatisch generierten Text)
        [JsonPropertyName("custom_tts_text")]
        public string? CustomTtsText { get; set; }

        // Flag ob TTS-Audio bereits generiert wurde (Legacy, für Kompatibilität)
        [JsonPropertyName("has_tts_audio")]
        public bool HasTtsAudio { get; set; }

        // Pfad zur generierten TTS-Datei (optional, für Referenz)
        [JsonPropertyName("tts_audio_path")]
        public string? TtsAudioPath { get; set; }

        // TTS-Status für männliche Erzähler-Stimme
        [JsonPropertyName("has_male_tts")]
        public bool HasMaleTts { get; set; }

        // TTS-Status für weibliche Erzähler-Stimme
        [JsonPropertyName("has_female_tts")]
        public bool HasFemaleTts { get; set; }

        // Zeitstempel der letzten TTS-Generierung (optional)
        [JsonPropertyName("last_tts_generated_at")]
        public DateTime? LastTtsGeneratedAt { get; set; }

        // Letzter TTS-Fehler (falls vorhanden)
        [JsonPropertyName("last_tts_error")]
        public string? LastTtsError { get; set; }

        // Zeitstempel des letzten TTS-Fehlers
        [JsonPropertyName("last_tts_error_at")]
        public DateTime? LastTtsErrorAt { get; set; }

        // Anzahl der Fehlversuche
        [JsonPropertyName("tts_error_count")]
        public int TtsErrorCount { get; set; }

        /// <summary>
        /// Gibt an, ob die Quest einen TTS-Fehler hat.
        /// </summary>
        [JsonIgnore]
        public bool HasTtsError => !string.IsNullOrEmpty(LastTtsError);

        /// <summary>
        /// Setzt den TTS-Fehler zurück.
        /// </summary>
        public void ClearTtsError()
        {
            LastTtsError = null;
            LastTtsErrorAt = null;
            TtsErrorCount = 0;
        }

        /// <summary>
        /// Setzt einen TTS-Fehler.
        /// </summary>
        public void SetTtsError(string error)
        {
            LastTtsError = error;
            LastTtsErrorAt = DateTime.Now;
            TtsErrorCount++;
        }

        /// <summary>
        /// Aktualisiert die TTS-Flags basierend auf vorhandenen Dateien im Dateisystem.
        /// </summary>
        /// <param name="outputRootPath">Basis-Ausgabepfad</param>
        /// <param name="languageCode">Sprachcode (z.B. "deDE")</param>
        public void UpdateTtsFlagsFromFileSystem(string outputRootPath, string languageCode)
        {
            if (string.IsNullOrWhiteSpace(outputRootPath))
                return;

            var zone = SanitizeZoneName(Zone);
            var malePath = Path.Combine(outputRootPath, "audio", languageCode, "male", zone, $"quest_{QuestId}.mp3");
            var femalePath = Path.Combine(outputRootPath, "audio", languageCode, "female", zone, $"quest_{QuestId}.mp3");

            HasMaleTts = File.Exists(malePath);
            HasFemaleTts = File.Exists(femalePath);

            // Legacy-Flag aktualisieren (beide vorhanden = komplett)
            HasTtsAudio = HasMaleTts && HasFemaleTts;
        }

        /// <summary>
        /// Bereinigt den Zonennamen für die Verwendung als Ordnername.
        /// </summary>
        private static string SanitizeZoneName(string? zone)
        {
            if (string.IsNullOrWhiteSpace(zone))
                return "UnknownZone";

            var invalidChars = Path.GetInvalidFileNameChars();
            var result = zone;

            foreach (var c in invalidChars)
            {
                result = result.Replace(c, '_');
            }

            return result.Replace(' ', '_').Replace('.', '_').Trim('_');
        }

        // Hilfseigenschaft für TTS-Text (alle relevanten Textteile)
        /// <summary>
        /// Gibt den Text für TTS-Generierung zurück.
        /// Verwendet CustomTtsText falls vorhanden, sonst automatisch generierten Text.
        /// </summary>
        public string TtsText
        {
            get
            {
                // Benutzerdefinierter Text hat Vorrang
                if (!string.IsNullOrWhiteSpace(CustomTtsText))
                    return CustomTtsText;

                // Automatisch generierter Text
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(Title))
                    parts.Add(Title);

                if (!string.IsNullOrWhiteSpace(Description))
                    parts.Add(Description);

                if (!string.IsNullOrWhiteSpace(Objectives))
                    parts.Add($"Ziele: {Objectives}");

                if (!string.IsNullOrWhiteSpace(Completion))
                    parts.Add($"Abschluss: {Completion}");

                return string.Join(". ", parts);
            }
        }

        /// <summary>
        /// Gibt den automatisch generierten TTS-Text zurück (ohne Custom-Override).
        /// </summary>
        [JsonIgnore]
        public string AutoGeneratedTtsText
        {
            get
            {
                var parts = new List<string>();

                if (!string.IsNullOrWhiteSpace(Title))
                    parts.Add(Title);

                if (!string.IsNullOrWhiteSpace(Description))
                    parts.Add(Description);

                if (!string.IsNullOrWhiteSpace(Objectives))
                    parts.Add($"Ziele: {Objectives}");

                if (!string.IsNullOrWhiteSpace(Completion))
                    parts.Add($"Abschluss: {Completion}");

                return string.Join(". ", parts);
            }
        }

        /// <summary>
        /// Gibt an, ob ein benutzerdefinierter TTS-Text verwendet wird.
        /// </summary>
        [JsonIgnore]
        public bool HasCustomTtsText => !string.IsNullOrWhiteSpace(CustomTtsText);
    }
}
