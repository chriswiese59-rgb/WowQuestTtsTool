namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Enthält die aus der AzerothCore-Datenbank importierten Quest-Texte.
    /// Speichert sowohl deutsche als auch englische Versionen für Fallback-Logik.
    /// </summary>
    public class PrivateQuestText
    {
        /// <summary>
        /// Quest-ID (entspricht Quest.QuestId).
        /// </summary>
        public int QuestId { get; set; }

        /// <summary>
        /// Deutsche Ziele/Objectives (aus quest_template_locale, locale='deDE').
        /// </summary>
        public string? ObjectivesDe { get; set; }

        /// <summary>
        /// Deutscher Abschlusstext/Completion (aus quest_offer_reward_locale oder quest_template_locale).
        /// </summary>
        public string? CompletionDe { get; set; }

        /// <summary>
        /// Englische Ziele/Objectives (aus quest_template, Fallback wenn DE leer).
        /// </summary>
        public string? ObjectivesEn { get; set; }

        /// <summary>
        /// Englischer Abschlusstext/Completion (Fallback wenn DE leer).
        /// </summary>
        public string? CompletionEn { get; set; }

        /// <summary>
        /// Gibt die Objectives mit Fallback-Logik zurueck (DE -> EN).
        /// </summary>
        public string? GetObjectives()
        {
            if (!string.IsNullOrWhiteSpace(ObjectivesDe))
                return ObjectivesDe;
            return ObjectivesEn;
        }

        /// <summary>
        /// Gibt die Completion mit Fallback-Logik zurueck (DE -> EN).
        /// </summary>
        public string? GetCompletion()
        {
            if (!string.IsNullOrWhiteSpace(CompletionDe))
                return CompletionDe;
            return CompletionEn;
        }
    }
}
