namespace WowQuestTtsTool
{
    /// <summary>
    /// Kategorien für WoW-Quests.
    /// </summary>
    public enum QuestCategory
    {
        /// <summary>Unbekannt/Nicht klassifiziert</summary>
        Unknown = 0,

        /// <summary>Hauptstory-Quest (Campaign/Main Story)</summary>
        Main = 1,

        /// <summary>Nebenquest (Side Quest)</summary>
        Side = 2,

        /// <summary>Gruppenquest (erfordert Gruppe)</summary>
        Group = 3,

        /// <summary>Tägliche Quest (Daily)</summary>
        Daily = 4,

        /// <summary>Wöchentliche Quest (Weekly)</summary>
        Weekly = 5,

        /// <summary>Dungeon-Quest</summary>
        Dungeon = 6,

        /// <summary>Raid-Quest</summary>
        Raid = 7,

        /// <summary>PvP-Quest</summary>
        PvP = 8,

        /// <summary>Weltquest (World Quest)</summary>
        World = 9,

        /// <summary>Event-Quest (zeitlich begrenzt)</summary>
        Event = 10,

        /// <summary>Berufsquest (Profession)</summary>
        Profession = 11,

        /// <summary>Ruf-Quest (Reputation)</summary>
        Reputation = 12,

        /// <summary>Bonus-Objektiv</summary>
        BonusObjective = 13,

        /// <summary>Legendäre Quest</summary>
        Legendary = 14
    }

    /// <summary>
    /// Erweiterungsmethoden für QuestCategory.
    /// </summary>
    public static class QuestCategoryExtensions
    {
        /// <summary>
        /// Gibt den deutschen Anzeigenamen für die Kategorie zurück.
        /// </summary>
        public static string ToDisplayName(this QuestCategory category)
        {
            return category switch
            {
                QuestCategory.Unknown => "Unbekannt",
                QuestCategory.Main => "Hauptstory",
                QuestCategory.Side => "Nebenquest",
                QuestCategory.Group => "Gruppenquest",
                QuestCategory.Daily => "Täglich",
                QuestCategory.Weekly => "Wöchentlich",
                QuestCategory.Dungeon => "Dungeon",
                QuestCategory.Raid => "Raid",
                QuestCategory.PvP => "PvP",
                QuestCategory.World => "Weltquest",
                QuestCategory.Event => "Event",
                QuestCategory.Profession => "Beruf",
                QuestCategory.Reputation => "Ruf",
                QuestCategory.BonusObjective => "Bonus",
                QuestCategory.Legendary => "Legendär",
                _ => category.ToString()
            };
        }

        /// <summary>
        /// Gibt die Kurzbezeichnung für die Kategorie zurück (für DataGrid).
        /// </summary>
        public static string ToShortName(this QuestCategory category)
        {
            return category switch
            {
                QuestCategory.Unknown => "?",
                QuestCategory.Main => "Main",
                QuestCategory.Side => "Side",
                QuestCategory.Group => "Grp",
                QuestCategory.Daily => "Day",
                QuestCategory.Weekly => "Wk",
                QuestCategory.Dungeon => "Dng",
                QuestCategory.Raid => "Raid",
                QuestCategory.PvP => "PvP",
                QuestCategory.World => "Wld",
                QuestCategory.Event => "Evt",
                QuestCategory.Profession => "Prof",
                QuestCategory.Reputation => "Rep",
                QuestCategory.BonusObjective => "Bon",
                QuestCategory.Legendary => "Leg",
                _ => "?"
            };
        }

        /// <summary>
        /// Prüft ob die Kategorie typischerweise eine Gruppe erfordert.
        /// </summary>
        public static bool TypicallyRequiresGroup(this QuestCategory category)
        {
            return category switch
            {
                QuestCategory.Group => true,
                QuestCategory.Dungeon => true,
                QuestCategory.Raid => true,
                _ => false
            };
        }
    }
}
