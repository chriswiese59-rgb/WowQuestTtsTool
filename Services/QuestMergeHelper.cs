using System;
using System.Collections.Generic;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Hilfsklasse zum Zusammenfuehren der aus der DB importierten Texte
    /// mit den bestehenden Quest-Objekten.
    /// Setzt auch die Lokalisierungs-Flags und den Status.
    /// </summary>
    public static class QuestMergeHelper
    {
        /// <summary>
        /// Fuegt die importierten Texte (Objectives, Completion) in die Quest-Objekte ein.
        /// Verwendet Fallback-Logik: DE -> EN -> unveraendert.
        /// Setzt ausserdem die Lokalisierungs-Flags und den Status.
        /// </summary>
        /// <param name="quests">Die bestehenden Quest-Objekte (aus Blizzard-API/JSON).</param>
        /// <param name="texts">Die aus der DB importierten Texte (Key = QuestId).</param>
        /// <returns>Anzahl der aktualisierten Quests.</returns>
        public static int MergePrivateTextsIntoQuests(
            IEnumerable<Quest> quests,
            IReadOnlyDictionary<int, PrivateQuestText> texts)
        {
            ArgumentNullException.ThrowIfNull(quests, nameof(quests));
            ArgumentNullException.ThrowIfNull(texts, nameof(texts));

            int updatedCount = 0;

            foreach (var quest in quests)
            {
                // Reset Flags
                quest.HasObjectivesDe = false;
                quest.HasCompletionDe = false;

                // Titel und Description kommen aus Blizzard-API (deDE)
                // Wir nehmen an, dass sie deutsch sind wenn vorhanden
                quest.HasTitleDe = !string.IsNullOrWhiteSpace(quest.Title);
                quest.HasDescriptionDe = !string.IsNullOrWhiteSpace(quest.Description);

                if (!texts.TryGetValue(quest.QuestId, out var t))
                {
                    // Kein Eintrag in der DB - Status aktualisieren
                    UpdateLocalizationStatus(quest);
                    continue;
                }

                bool updated = false;

                // OBJECTIVES: DE bevorzugt, EN Fallback
                if (!string.IsNullOrWhiteSpace(t.ObjectivesDe))
                {
                    quest.Objectives = t.ObjectivesDe;
                    quest.HasObjectivesDe = true;
                    updated = true;
                }
                else if (!string.IsNullOrWhiteSpace(t.ObjectivesEn))
                {
                    quest.Objectives = t.ObjectivesEn;
                    quest.HasObjectivesDe = false;
                    updated = true;
                }

                // COMPLETION: DE bevorzugt, EN Fallback
                if (!string.IsNullOrWhiteSpace(t.CompletionDe))
                {
                    quest.Completion = t.CompletionDe;
                    quest.HasCompletionDe = true;
                    updated = true;
                }
                else if (!string.IsNullOrWhiteSpace(t.CompletionEn))
                {
                    quest.Completion = t.CompletionEn;
                    quest.HasCompletionDe = false;
                    updated = true;
                }

                // Lokalisierungsstatus aktualisieren
                UpdateLocalizationStatus(quest);

                if (updated)
                    updatedCount++;
            }

            return updatedCount;
        }

        /// <summary>
        /// Aktualisiert den Lokalisierungsstatus einer Quest basierend auf den Flags.
        /// </summary>
        public static void UpdateLocalizationStatus(Quest quest)
        {
            ArgumentNullException.ThrowIfNull(quest, nameof(quest));

            // Pruefen ob ueberhaupt Texte vorhanden sind
            var hasAnyText =
                !string.IsNullOrWhiteSpace(quest.Title) ||
                !string.IsNullOrWhiteSpace(quest.Description) ||
                !string.IsNullOrWhiteSpace(quest.Objectives) ||
                !string.IsNullOrWhiteSpace(quest.Completion);

            if (!hasAnyText)
            {
                quest.LocalizationStatus = QuestLocalizationStatus.Incomplete;
                return;
            }

            // Pruefen ob alle vorhandenen Texte deutsch sind
            var allDe =
                quest.HasTitleDe &&
                quest.HasDescriptionDe &&
                (string.IsNullOrWhiteSpace(quest.Objectives) || quest.HasObjectivesDe) &&
                (string.IsNullOrWhiteSpace(quest.Completion) || quest.HasCompletionDe);

            if (allDe)
            {
                quest.LocalizationStatus = QuestLocalizationStatus.FullyGerman;
                return;
            }

            // Pruefen ob wir ueberhaupt irgendwas deutsches haben
            var anyDe =
                quest.HasTitleDe ||
                quest.HasDescriptionDe ||
                quest.HasObjectivesDe ||
                quest.HasCompletionDe;

            if (anyDe)
            {
                quest.LocalizationStatus = QuestLocalizationStatus.MixedGermanEnglish;
                return;
            }

            // Sonst: Nur Englisch
            quest.LocalizationStatus = QuestLocalizationStatus.OnlyEnglish;
        }

        /// <summary>
        /// Aktualisiert den Lokalisierungsstatus fuer alle Quests.
        /// Nuetzlich wenn Quests bereits geladen sind aber Status fehlt.
        /// </summary>
        public static void UpdateAllLocalizationStatuses(IEnumerable<Quest> quests)
        {
            ArgumentNullException.ThrowIfNull(quests, nameof(quests));

            foreach (var quest in quests)
            {
                // Falls Flags noch nicht gesetzt, aus vorhandenen Texten ableiten
                if (!quest.HasTitleDe && !string.IsNullOrWhiteSpace(quest.Title))
                    quest.HasTitleDe = true;

                if (!quest.HasDescriptionDe && !string.IsNullOrWhiteSpace(quest.Description))
                    quest.HasDescriptionDe = true;

                UpdateLocalizationStatus(quest);
            }
        }

        /// <summary>
        /// Zaehlt wie viele Quests durch den Merge aktualisiert werden koennten.
        /// </summary>
        public static (int totalMatches, int withObjectives, int withCompletion) CountPotentialUpdates(
            IEnumerable<Quest> quests,
            IReadOnlyDictionary<int, PrivateQuestText> texts)
        {
            int totalMatches = 0;
            int withObjectives = 0;
            int withCompletion = 0;

            foreach (var quest in quests)
            {
                if (!texts.TryGetValue(quest.QuestId, out var t))
                    continue;

                totalMatches++;

                if (!string.IsNullOrWhiteSpace(t.ObjectivesDe) ||
                    !string.IsNullOrWhiteSpace(t.ObjectivesEn))
                {
                    withObjectives++;
                }

                if (!string.IsNullOrWhiteSpace(t.CompletionDe) ||
                    !string.IsNullOrWhiteSpace(t.CompletionEn))
                {
                    withCompletion++;
                }
            }

            return (totalMatches, withObjectives, withCompletion);
        }

        /// <summary>
        /// Zaehlt Quests nach Lokalisierungsstatus.
        /// </summary>
        public static (int fullyGerman, int mixed, int onlyEnglish, int incomplete) CountByLocalizationStatus(
            IEnumerable<Quest> quests)
        {
            int fullyGerman = 0;
            int mixed = 0;
            int onlyEnglish = 0;
            int incomplete = 0;

            foreach (var quest in quests)
            {
                switch (quest.LocalizationStatus)
                {
                    case QuestLocalizationStatus.FullyGerman:
                        fullyGerman++;
                        break;
                    case QuestLocalizationStatus.MixedGermanEnglish:
                        mixed++;
                        break;
                    case QuestLocalizationStatus.OnlyEnglish:
                        onlyEnglish++;
                        break;
                    case QuestLocalizationStatus.Incomplete:
                        incomplete++;
                        break;
                }
            }

            return (fullyGerman, mixed, onlyEnglish, incomplete);
        }
    }
}
