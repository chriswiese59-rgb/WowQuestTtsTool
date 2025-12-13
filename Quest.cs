using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Status einer Quest im Bearbeitungs-Workflow.
    /// </summary>
    public enum QuestWorkflowStatus
    {
        /// <summary>
        /// Quest ist offen und noch nicht bearbeitet.
        /// </summary>
        Open = 0,

        /// <summary>
        /// Quest wird gerade bearbeitet.
        /// </summary>
        InProgress = 1,

        /// <summary>
        /// Quest ist fertig bearbeitet und abgeschlossen.
        /// </summary>
        Completed = 2
    }

    /// <summary>
    /// Herkunft eines Quest-Textfeldes.
    /// </summary>
    public enum QuestTextSource
    {
        /// <summary>
        /// Keine Quelle/unbekannt.
        /// </summary>
        None = 0,

        /// <summary>
        /// Text stammt aus der Blizzard-API.
        /// </summary>
        Blizzard = 1,

        /// <summary>
        /// Text stammt aus AzerothCore-Datenbank.
        /// </summary>
        AzerothCore = 2,

        /// <summary>
        /// Text wurde manuell vom Benutzer eingegeben/ueberschrieben.
        /// </summary>
        Manual = 3,

        /// <summary>
        /// Text wurde von KI korrigiert.
        /// </summary>
        AiCorrected = 4
    }

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

        // === Datenquellen-Tracking ===

        /// <summary>
        /// Gibt an, ob diese Quest Daten aus der Blizzard-API hat.
        /// </summary>
        [JsonPropertyName("has_blizzard_source")]
        public bool HasBlizzardSource { get; set; }

        /// <summary>
        /// Gibt an, ob diese Quest Daten aus AzerothCore hat.
        /// </summary>
        [JsonPropertyName("has_acore_source")]
        public bool HasAcoreSource { get; set; }

        /// <summary>
        /// Kurzbezeichnung der Datenquelle(n) fuer UI.
        /// </summary>
        [JsonIgnore]
        public string SourceDisplayName
        {
            get
            {
                if (HasBlizzardSource && HasAcoreSource) return "Blizz+AC";
                if (HasBlizzardSource) return "Blizzard";
                if (HasAcoreSource) return "ACore";
                return "?";
            }
        }

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

        // === RewardText (Text bei Quest-Abgabe aus quest_offer_reward_locale) ===
        // HINWEIS: In AzerothCore heisst das Feld "RewardText", wird aber bei Quest-ABSCHLUSS angezeigt
        // (vor der Belohnung). Das ist NICHT der Belohnungstext, sondern der Abschluss-Dialog.

        /// <summary>
        /// Der aktive Belohnungstext fuer TTS (nach Korrektur/Ueberschreibung).
        /// Aus quest_offer_reward_locale.RewardText - Text bei Quest-Abgabe.
        /// </summary>
        [JsonPropertyName("reward_text")]
        public string? RewardText { get; set; }

        /// <summary>
        /// Original-Belohnungstext aus der Datenbank (unveraendert).
        /// </summary>
        [JsonPropertyName("original_reward_text")]
        public string? OriginalRewardText { get; set; }

        /// <summary>
        /// Herkunft des aktuellen RewardText.
        /// </summary>
        [JsonPropertyName("reward_text_source")]
        public QuestTextSource RewardTextSource { get; set; } = QuestTextSource.None;

        /// <summary>
        /// Gibt an, ob der RewardText manuell ueberschrieben wurde.
        /// </summary>
        [JsonPropertyName("is_reward_text_overridden")]
        public bool IsRewardTextOverridden { get; set; }

        /// <summary>
        /// Gibt an, ob ein RewardText vorhanden ist (Original oder ueberschrieben).
        /// </summary>
        [JsonIgnore]
        public bool HasRewardText => !string.IsNullOrWhiteSpace(RewardText) || !string.IsNullOrWhiteSpace(OriginalRewardText);

        /// <summary>
        /// Gibt den effektiven RewardText zurueck (ueberschrieben oder Original).
        /// </summary>
        [JsonIgnore]
        public string EffectiveRewardText => !string.IsNullOrWhiteSpace(RewardText) ? RewardText : (OriginalRewardText ?? string.Empty);

        /// <summary>
        /// Setzt den RewardText aus einer Datenquelle.
        /// </summary>
        /// <param name="text">Der Text</param>
        /// <param name="source">Die Quelle (Blizzard, AzerothCore)</param>
        public void SetRewardTextFromSource(string? text, QuestTextSource source)
        {
            OriginalRewardText = text;
            RewardTextSource = source;
            // RewardText nur setzen, wenn noch nicht manuell ueberschrieben
            if (!IsRewardTextOverridden)
            {
                RewardText = text;
            }
        }

        /// <summary>
        /// Ueberschreibt den RewardText manuell.
        /// </summary>
        /// <param name="text">Der neue Text</param>
        /// <param name="isAiCorrected">True wenn KI-korrigiert</param>
        public void OverrideRewardText(string? text, bool isAiCorrected = false)
        {
            RewardText = text;
            IsRewardTextOverridden = true;
            RewardTextSource = isAiCorrected ? QuestTextSource.AiCorrected : QuestTextSource.Manual;
        }

        /// <summary>
        /// Setzt den RewardText auf das Original zurueck.
        /// </summary>
        public void ResetRewardTextToOriginal()
        {
            RewardText = OriginalRewardText;
            IsRewardTextOverridden = false;
            // Source bleibt (zeigt Original-Quelle)
            if (!string.IsNullOrWhiteSpace(OriginalRewardText))
            {
                // Quelle basierend auf vorhandenen Daten bestimmen
                RewardTextSource = HasAcoreSource ? QuestTextSource.AzerothCore :
                                   HasBlizzardSource ? QuestTextSource.Blizzard :
                                   QuestTextSource.None;
            }
        }

        // === RequestItemsText (Text wenn Quest-Items fehlen aus quest_request_items_locale) ===

        /// <summary>
        /// Text der angezeigt wird wenn Quest-Items noch fehlen.
        /// Aus quest_request_items_locale.CompletionText.
        /// </summary>
        [JsonPropertyName("request_items_text")]
        public string? RequestItemsText { get; set; }

        /// <summary>
        /// Gibt an, ob ein RequestItemsText vorhanden ist.
        /// </summary>
        [JsonIgnore]
        public bool HasRequestItemsText => !string.IsNullOrWhiteSpace(RequestItemsText);

        // === Kompatibilitaets-Aliase fuer alten Code ===

        /// <summary>
        /// Alias fuer RewardText (Kompatibilitaet).
        /// </summary>
        [JsonIgnore]
        public string? CompletionText
        {
            get => RewardText;
            set => RewardText = value;
        }

        /// <summary>
        /// Alias fuer OriginalRewardText (Kompatibilitaet).
        /// </summary>
        [JsonIgnore]
        public string? OriginalCompletionText
        {
            get => OriginalRewardText;
            set => OriginalRewardText = value;
        }

        /// <summary>
        /// Alias fuer RewardTextSource (Kompatibilitaet).
        /// </summary>
        [JsonIgnore]
        public QuestTextSource CompletionTextSource
        {
            get => RewardTextSource;
            set => RewardTextSource = value;
        }

        /// <summary>
        /// Alias fuer IsRewardTextOverridden (Kompatibilitaet).
        /// </summary>
        [JsonIgnore]
        public bool IsCompletionTextOverridden
        {
            get => IsRewardTextOverridden;
            set => IsRewardTextOverridden = value;
        }

        /// <summary>
        /// Alias fuer HasRewardText (Kompatibilitaet).
        /// </summary>
        [JsonIgnore]
        public bool HasCompletionText => HasRewardText;

        /// <summary>
        /// Alias fuer EffectiveRewardText (Kompatibilitaet).
        /// </summary>
        [JsonIgnore]
        public string EffectiveCompletionText => EffectiveRewardText;

        /// <summary>
        /// Alias fuer SetRewardTextFromSource (Kompatibilitaet).
        /// </summary>
        public void SetCompletionTextFromSource(string? text, QuestTextSource source) => SetRewardTextFromSource(text, source);

        /// <summary>
        /// Alias fuer OverrideRewardText (Kompatibilitaet).
        /// </summary>
        public void OverrideCompletionText(string? text, bool isAiCorrected = false) => OverrideRewardText(text, isAiCorrected);

        /// <summary>
        /// Alias fuer ResetRewardTextToOriginal (Kompatibilitaet).
        /// </summary>
        public void ResetCompletionTextToOriginal() => ResetRewardTextToOriginal();

        // Flag für TTS-Review
        [JsonPropertyName("tts_reviewed")]
        public bool TtsReviewed { get; set; }

        // === Workflow-Status (Open/InProgress/Completed) ===

        /// <summary>
        /// Workflow-Status der Quest (Open, InProgress, Completed).
        /// </summary>
        [JsonPropertyName("workflow_status")]
        public QuestWorkflowStatus WorkflowStatus { get; set; } = QuestWorkflowStatus.Open;

        /// <summary>
        /// Gibt an, ob die Quest gesperrt ist.
        /// Gesperrte Quests koennen nicht mehr bearbeitet werden.
        /// </summary>
        [JsonPropertyName("is_locked")]
        public bool IsLocked { get; set; }

        /// <summary>
        /// Zeitpunkt wann die Quest als erledigt markiert wurde.
        /// </summary>
        [JsonPropertyName("completed_at")]
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Zeitpunkt wann die Quest in Bearbeitung genommen wurde.
        /// </summary>
        [JsonPropertyName("started_at")]
        public DateTime? StartedAt { get; set; }

        /// <summary>
        /// Gibt an, ob die Quest als erledigt markiert wurde (Completed).
        /// Kompatibilitaets-Property fuer bestehenden Code.
        /// </summary>
        [JsonIgnore]
        public bool IsCompleted => WorkflowStatus == QuestWorkflowStatus.Completed;

        /// <summary>
        /// Gibt an, ob die Quest offen ist (noch nicht bearbeitet).
        /// </summary>
        [JsonIgnore]
        public bool IsOpen => WorkflowStatus == QuestWorkflowStatus.Open;

        /// <summary>
        /// Gibt an, ob die Quest in Bearbeitung ist.
        /// </summary>
        [JsonIgnore]
        public bool IsInProgress => WorkflowStatus == QuestWorkflowStatus.InProgress;

        /// <summary>
        /// Gibt an, ob die Quest bearbeitbar ist (nicht Completed und nicht Locked).
        /// </summary>
        [JsonIgnore]
        public bool IsEditable => WorkflowStatus != QuestWorkflowStatus.Completed && !IsLocked;

        /// <summary>
        /// Setzt die Quest auf "In Bearbeitung".
        /// </summary>
        public void StartProgress()
        {
            if (WorkflowStatus == QuestWorkflowStatus.Completed && IsLocked)
                return; // Gesperrte Completed-Quests nicht aendern

            WorkflowStatus = QuestWorkflowStatus.InProgress;
            StartedAt ??= DateTime.Now;
        }

        /// <summary>
        /// Markiert die Quest als erledigt und sperrt sie.
        /// </summary>
        public void MarkAsCompleted()
        {
            WorkflowStatus = QuestWorkflowStatus.Completed;
            IsLocked = true;
            CompletedAt = DateTime.Now;
        }

        /// <summary>
        /// Entsperrt die Quest wieder fuer Bearbeitung.
        /// Setzt den Status auf InProgress zurueck.
        /// </summary>
        public void Unlock()
        {
            IsLocked = false;
            if (WorkflowStatus == QuestWorkflowStatus.Completed)
            {
                WorkflowStatus = QuestWorkflowStatus.InProgress;
            }
        }

        /// <summary>
        /// Setzt den Status komplett zurueck auf Open.
        /// </summary>
        public void ResetToOpen()
        {
            WorkflowStatus = QuestWorkflowStatus.Open;
            IsLocked = false;
            CompletedAt = null;
            StartedAt = null;
        }

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

                // ACHTUNG: DB-Felder sind vertauscht!
                // RewardText (quest_offer_reward_locale) enthaelt den ABSCHLUSS-Text
                // Completion (quest_template_locale) enthaelt den BELOHNUNGS-Text

                // Zuerst Abschluss (aus RewardText)
                if (!string.IsNullOrWhiteSpace(EffectiveRewardText))
                    parts.Add($"Abschluss: {EffectiveRewardText}");

                // Dann Belohnung (aus Completion)
                if (!string.IsNullOrWhiteSpace(Completion) && Completion != EffectiveRewardText)
                    parts.Add($"Belohnung: {Completion}");

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

                // ACHTUNG: DB-Felder sind vertauscht!
                // RewardText (quest_offer_reward_locale) enthaelt den ABSCHLUSS-Text
                // Completion (quest_template_locale) enthaelt den BELOHNUNGS-Text

                // Zuerst Abschluss (aus RewardText)
                if (!string.IsNullOrWhiteSpace(EffectiveRewardText))
                    parts.Add($"Abschluss: {EffectiveRewardText}");

                // Dann Belohnung (aus Completion)
                if (!string.IsNullOrWhiteSpace(Completion) && Completion != EffectiveRewardText)
                    parts.Add($"Belohnung: {Completion}");

                return string.Join(". ", parts);
            }
        }

        /// <summary>
        /// Gibt an, ob ein benutzerdefinierter TTS-Text verwendet wird.
        /// </summary>
        [JsonIgnore]
        public bool HasCustomTtsText => !string.IsNullOrWhiteSpace(CustomTtsText);

        // === Audio-Index Properties (vom AudioIndexService gesetzt) ===

        /// <summary>
        /// Gibt an, ob im Audio-Index mindestens eine Audio-Datei fuer diese Quest existiert.
        /// Wird vom AudioIndexService beim Scan gesetzt.
        /// </summary>
        [JsonIgnore]
        public bool HasAudioFromIndex { get; set; }

        /// <summary>
        /// Gibt an, ob im Audio-Index eine maennliche Audio-Datei existiert.
        /// </summary>
        [JsonIgnore]
        public bool HasMaleAudioFromIndex { get; set; }

        /// <summary>
        /// Gibt an, ob im Audio-Index eine weibliche Audio-Datei existiert.
        /// </summary>
        [JsonIgnore]
        public bool HasFemaleAudioFromIndex { get; set; }

        /// <summary>
        /// Pfad zur maennlichen Audio-Datei aus dem Index.
        /// </summary>
        [JsonIgnore]
        public string? MaleAudioPathFromIndex { get; set; }

        /// <summary>
        /// Pfad zur weiblichen Audio-Datei aus dem Index.
        /// </summary>
        [JsonIgnore]
        public string? FemaleAudioPathFromIndex { get; set; }

        /// <summary>
        /// Zeitstempel der letzten Audio-Aenderung aus dem Index.
        /// </summary>
        [JsonIgnore]
        public DateTime? AudioLastModifiedFromIndex { get; set; }

        /// <summary>
        /// Gibt an, ob die Quest als "offen" gilt (noch nicht vertont).
        /// Eine Quest ist offen, wenn sie keine Audio im Index hat.
        /// </summary>
        [JsonIgnore]
        public bool IsOpenForVoicing => !HasAudioFromIndex;

        /// <summary>
        /// Gibt an, ob die Quest vollstaendig vertont ist (Male + Female vorhanden).
        /// </summary>
        [JsonIgnore]
        public bool IsFullyVoiced => HasMaleAudioFromIndex && HasFemaleAudioFromIndex;

        /// <summary>
        /// Aktualisiert die Audio-Index-Properties basierend auf einem AudioIndexEntry.
        /// </summary>
        public void UpdateFromAudioIndex(WowQuestTtsTool.Services.AudioIndexEntry? entry)
        {
            if (entry == null)
            {
                HasAudioFromIndex = false;
                HasMaleAudioFromIndex = false;
                HasFemaleAudioFromIndex = false;
                MaleAudioPathFromIndex = null;
                FemaleAudioPathFromIndex = null;
                AudioLastModifiedFromIndex = null;
            }
            else
            {
                HasAudioFromIndex = entry.HasAnyAudio;
                HasMaleAudioFromIndex = entry.HasMaleAudio;
                HasFemaleAudioFromIndex = entry.HasFemaleAudio;
                MaleAudioPathFromIndex = entry.MaleAudioPath;
                FemaleAudioPathFromIndex = entry.FemaleAudioPath;
                AudioLastModifiedFromIndex = entry.LastModified;

                // Legacy-Flags synchronisieren fuer Kompatibilitaet
                HasMaleTts = entry.HasMaleAudio;
                HasFemaleTts = entry.HasFemaleAudio;
                HasTtsAudio = entry.HasMaleAudio && entry.HasFemaleAudio;
            }
        }
    }
}
