namespace WowQuestTtsTool
{
    /// <summary>
    /// Lokalisierungsstatus einer Quest.
    /// Wird verwendet um Quests nach Sprachqualitaet zu filtern und sortieren.
    /// </summary>
    public enum QuestLocalizationStatus
    {
        /// <summary>
        /// Alle Texte sind auf Deutsch vorhanden.
        /// </summary>
        FullyGerman = 0,

        /// <summary>
        /// Mischung aus deutschen und englischen Texten.
        /// </summary>
        MixedGermanEnglish = 1,

        /// <summary>
        /// Nur englische Texte vorhanden.
        /// </summary>
        OnlyEnglish = 2,

        /// <summary>
        /// Unvollstaendig - wichtige Texte fehlen.
        /// </summary>
        Incomplete = 3
    }

    /// <summary>
    /// Erweiterungsmethoden fuer QuestLocalizationStatus.
    /// </summary>
    public static class QuestLocalizationStatusExtensions
    {
        /// <summary>
        /// Gibt den Anzeigenamen fuer den Status zurueck.
        /// </summary>
        public static string ToDisplayName(this QuestLocalizationStatus status)
        {
            return status switch
            {
                QuestLocalizationStatus.FullyGerman => "Vollstaendig Deutsch",
                QuestLocalizationStatus.MixedGermanEnglish => "Deutsch/Englisch gemischt",
                QuestLocalizationStatus.OnlyEnglish => "Nur Englisch",
                QuestLocalizationStatus.Incomplete => "Unvollstaendig",
                _ => "Unbekannt"
            };
        }

        /// <summary>
        /// Gibt einen Kurzcode fuer den Status zurueck.
        /// </summary>
        public static string ToShortName(this QuestLocalizationStatus status)
        {
            return status switch
            {
                QuestLocalizationStatus.FullyGerman => "DE",
                QuestLocalizationStatus.MixedGermanEnglish => "DE/EN",
                QuestLocalizationStatus.OnlyEnglish => "EN",
                QuestLocalizationStatus.Incomplete => "?",
                _ => "-"
            };
        }
    }
}
