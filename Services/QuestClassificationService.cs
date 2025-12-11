using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Meta-Informationen fuer eine Quest (aus DB/API).
    /// Wird fuer die automatische Klassifizierung verwendet.
    /// </summary>
    public class QuestMeta
    {
        /// <summary>
        /// Quest-ID.
        /// </summary>
        [JsonPropertyName("quest_id")]
        public int QuestId { get; set; }

        /// <summary>
        /// Empfohlene Spieleranzahl (0 = Solo, 2-5 = Gruppe, 5+ = Raid).
        /// Aus DB: quest_template.SuggestedPlayers oder API.
        /// </summary>
        [JsonPropertyName("suggested_players")]
        public int? SuggestedPlayers { get; set; }

        /// <summary>
        /// Quest-Typ-String aus DB/API (z.B. "Normal", "Group", "Dungeon", "Raid", "Elite").
        /// Aus DB: quest_template.QuestType oder quest_template.Flags.
        /// </summary>
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        /// <summary>
        /// Quest-Kategorie/Tag aus DB/API (z.B. "Storyline", "Main", "Campaign", "Class").
        /// </summary>
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        /// <summary>
        /// Quest-Info-ID aus DB (quest_template.QuestInfoID).
        /// WotLK Quest-Info-IDs:
        /// 1=Normal, 21=Life, 41=PvP, 62=Raid, 81=Dungeon, 82=World, 83=Heroic, etc.
        /// </summary>
        [JsonPropertyName("quest_info_id")]
        public int? QuestInfoId { get; set; }

        /// <summary>
        /// Quest-Flags aus DB (quest_template.Flags).
        /// Bestimmte Flags zeigen Gruppen/Raid-Quests an.
        /// </summary>
        [JsonPropertyName("flags")]
        public int? Flags { get; set; }

        /// <summary>
        /// Quest-Flags Extra aus DB (quest_template.FlagsExtra).
        /// </summary>
        [JsonPropertyName("flags_extra")]
        public int? FlagsExtra { get; set; }

        /// <summary>
        /// Minimales Level fuer die Quest.
        /// </summary>
        [JsonPropertyName("min_level")]
        public int? MinLevel { get; set; }

        /// <summary>
        /// Quest-Level (fuer Skalierung).
        /// </summary>
        [JsonPropertyName("quest_level")]
        public int? QuestLevel { get; set; }
    }

    /// <summary>
    /// Service fuer die automatische Klassifizierung von Quests.
    /// Analysiert Meta-Informationen und setzt IsMainStory, IsGroupQuest und Category.
    /// </summary>
    public static class QuestClassificationService
    {
        // WotLK Quest-Info-IDs (aus quest_template.QuestInfoID)
        private const int QuestInfoNormal = 1;
        private const int QuestInfoGroup = 21;      // "Group" - erfordert Gruppe
        private const int QuestInfoPvP = 41;
        private const int QuestInfoRaid = 62;
        private const int QuestInfoDungeon = 81;
        private const int QuestInfoWorldEvent = 82;
        private const int QuestInfoHeroic = 83;
        private const int QuestInfoRaidHeroic = 85;
        private const int QuestInfoEscort = 88;

        // Quest-Flags (Bitfield aus quest_template.Flags)
        [Flags]
        private enum QuestFlags
        {
            None = 0,
            StayAlive = 0x00000001,           // Quest muss am Leben bleiben
            PartyAccept = 0x00000002,         // Party akzeptiert Quest automatisch
            Exploration = 0x00000004,         // Erkundungsquest
            Sharable = 0x00000008,            // Quest kann geteilt werden
            HasCondition = 0x00000010,        // Hat Bedingung
            HideRewardPOI = 0x00000020,       // Versteckt Belohnungs-POI
            Raid = 0x00000040,                // RAID Quest!
            TBC = 0x00000080,                 // TBC Quest
            NoMoneyFromXP = 0x00000100,       // Kein Geld aus XP
            HiddenRewards = 0x00000200,       // Versteckte Belohnungen
            Tracking = 0x00000400,            // Tracking (Auto-Complete)
            DeprecateReputation = 0x00000800, // Veraltet - Ruf
            Daily = 0x00001000,               // Taegliche Quest
            FlagsUseX = 0x00002000,           // Flags PvP
            Unavailable = 0x00004000,         // Quest nicht verfuegbar
            Weekly = 0x00008000,              // Woechentliche Quest
            AutoComplete = 0x00010000,        // Auto-Complete
            DisplayItemInTracker = 0x00020000,// Item in Tracker anzeigen
            ObjText = 0x00040000,             // Objektiver Text
            AutoAccept = 0x00080000           // Auto-Accept
        }

        /// <summary>
        /// Klassifiziert eine Quest basierend auf Meta-Informationen.
        /// Setzt IsMainStory, IsGroupQuest und Category.
        /// </summary>
        /// <param name="quest">Quest-Objekt (wird modifiziert)</param>
        /// <param name="meta">Meta-Informationen (kann null sein)</param>
        public static void ClassifyQuest(Quest quest, QuestMeta? meta)
        {
            ArgumentNullException.ThrowIfNull(quest);

            // Defaults setzen falls noch nicht gesetzt
            if (quest.Category == QuestCategory.Unknown)
                quest.Category = QuestCategory.Side;

            if (meta == null)
            {
                // Ohne Meta: Heuristik basierend auf vorhandenen Quest-Daten
                ClassifyByQuestData(quest);
                return;
            }

            // ===== 1. Gruppenquest-Erkennung =====
            bool isGroup = false;

            // a) SuggestedPlayers >= 3 deutet auf Gruppenquest hin
            if (meta.SuggestedPlayers.HasValue && meta.SuggestedPlayers.Value >= 3)
            {
                isGroup = true;
            }

            // b) QuestInfoID auswerten
            if (meta.QuestInfoId.HasValue)
            {
                switch (meta.QuestInfoId.Value)
                {
                    case QuestInfoGroup:
                    case QuestInfoDungeon:
                    case QuestInfoHeroic:
                        quest.Category = QuestCategory.Dungeon;
                        isGroup = true;
                        break;

                    case QuestInfoRaid:
                    case QuestInfoRaidHeroic:
                        quest.Category = QuestCategory.Raid;
                        isGroup = true;
                        break;

                    case QuestInfoPvP:
                        quest.Category = QuestCategory.PvP;
                        break;

                    case QuestInfoWorldEvent:
                        quest.Category = QuestCategory.Event;
                        break;
                }
            }

            // c) Flags auswerten
            if (meta.Flags.HasValue)
            {
                var flags = (QuestFlags)meta.Flags.Value;

                if (flags.HasFlag(QuestFlags.Raid))
                {
                    quest.Category = QuestCategory.Raid;
                    isGroup = true;
                }

                if (flags.HasFlag(QuestFlags.Daily))
                {
                    quest.Category = QuestCategory.Daily;
                }

                if (flags.HasFlag(QuestFlags.Weekly))
                {
                    quest.Category = QuestCategory.Weekly;
                }
            }

            // d) Type-String auswerten
            if (!string.IsNullOrWhiteSpace(meta.Type))
            {
                var type = meta.Type.ToLowerInvariant();

                if (type.Contains("group") || type.Contains("elite"))
                {
                    isGroup = true;
                    if (quest.Category == QuestCategory.Side || quest.Category == QuestCategory.Unknown)
                        quest.Category = QuestCategory.Group;
                }
                else if (type.Contains("dungeon"))
                {
                    quest.Category = QuestCategory.Dungeon;
                    isGroup = true;
                }
                else if (type.Contains("raid"))
                {
                    quest.Category = QuestCategory.Raid;
                    isGroup = true;
                }
                else if (type.Contains("pvp"))
                {
                    quest.Category = QuestCategory.PvP;
                }
                else if (type.Contains("daily"))
                {
                    quest.Category = QuestCategory.Daily;
                }
                else if (type.Contains("weekly"))
                {
                    quest.Category = QuestCategory.Weekly;
                }
            }

            quest.IsGroupQuest = isGroup;

            // ===== 2. Hauptquest-Erkennung =====
            bool isMain = false;

            // a) Category-String auswerten
            if (!string.IsNullOrWhiteSpace(meta.Category))
            {
                var cat = meta.Category.ToLowerInvariant();

                if (cat.Contains("storyline") ||
                    cat.Contains("main") ||
                    cat.Contains("campaign") ||
                    cat.Contains("epic") ||
                    cat.Contains("legendary"))
                {
                    isMain = true;
                }

                if (cat.Contains("class") || cat.Contains("profession"))
                {
                    // Klassenquests sind oft wichtig aber nicht unbedingt "Main"
                    // Setze sie als Side mit Markierung
                    quest.Category = QuestCategory.Profession;
                }
            }

            // b) Heuristik: Hohe Quest-IDs in bestimmten Bereichen sind oft Storyline
            // (z.B. in WotLK: Nordend-Story-Quests haben oft bestimmte ID-Bereiche)
            // Dies ist eine sehr grobe Heuristik und kann angepasst werden

            quest.IsMainStory = isMain;

            // ===== 3. Finale Kategorie-Zuweisung =====
            if (isMain && quest.Category == QuestCategory.Side)
            {
                quest.Category = QuestCategory.Main;
            }

            if (isGroup && quest.Category == QuestCategory.Side)
            {
                quest.Category = QuestCategory.Group;
            }
        }

        /// <summary>
        /// Klassifiziert eine Quest basierend nur auf Quest-Daten (ohne Meta).
        /// Nutzt Heuristiken basierend auf Titel, Zone, etc.
        /// </summary>
        private static void ClassifyByQuestData(Quest quest)
        {
            // Heuristik 1: Quest-Titel enthaelt Hinweise
            var title = quest.Title?.ToLowerInvariant() ?? "";

            // Gruppenquest-Hinweise im Titel
            if (title.Contains("[gruppe]") ||
                title.Contains("[group]") ||
                title.Contains("[dungeon]") ||
                title.Contains("[raid]") ||
                title.Contains("[5]") ||
                title.Contains("[elite]"))
            {
                quest.IsGroupQuest = true;
                quest.Category = QuestCategory.Group;
            }

            // Hauptquest-Hinweise (selten im Titel, aber moeglich)
            if (title.Contains("[kampagne]") ||
                title.Contains("[campaign]") ||
                title.Contains("[story]") ||
                title.Contains("[hauptquest]"))
            {
                quest.IsMainStory = true;
                quest.Category = QuestCategory.Main;
            }

            // Heuristik 2: Quest-Typ aus vorhandenem Feld
            if (!string.IsNullOrWhiteSpace(quest.QuestType))
            {
                var type = quest.QuestType.ToLowerInvariant();

                if (type.Contains("group") || type.Contains("dungeon") || type.Contains("raid"))
                {
                    quest.IsGroupQuest = true;
                }
            }

            // Heuristik 3: SuggestedPartySize (falls schon gesetzt)
            if (quest.SuggestedPartySize >= 3)
            {
                quest.IsGroupQuest = true;
                if (quest.Category == QuestCategory.Side || quest.Category == QuestCategory.Unknown)
                {
                    quest.Category = quest.SuggestedPartySize >= 10 ? QuestCategory.Raid : QuestCategory.Group;
                }
            }
        }

        /// <summary>
        /// Klassifiziert alle Quests in einer Liste.
        /// </summary>
        /// <param name="quests">Liste der Quests</param>
        /// <param name="metaDict">Dictionary mit QuestId -> QuestMeta (optional)</param>
        /// <returns>Anzahl der klassifizierten Quests</returns>
        public static int ClassifyAll(
            IEnumerable<Quest> quests,
            IReadOnlyDictionary<int, QuestMeta>? metaDict = null)
        {
            int count = 0;

            foreach (var quest in quests)
            {
                QuestMeta? meta = null;
                metaDict?.TryGetValue(quest.QuestId, out meta);

                ClassifyQuest(quest, meta);
                count++;
            }

            return count;
        }

        /// <summary>
        /// Ermittelt die Ordner-Kategorie fuer die Audio-Dateistruktur.
        /// Gibt "Main", "Side" oder "Group" zurueck.
        /// </summary>
        public static string GetAudioFolderCategory(Quest quest)
        {
            // Hauptquests: Main
            if (quest.IsMainStory || quest.Category == QuestCategory.Main)
                return "Main";

            // Gruppenquests (inkl. Dungeon/Raid): Group
            if (quest.IsGroupQuest ||
                quest.Category == QuestCategory.Group ||
                quest.Category == QuestCategory.Dungeon ||
                quest.Category == QuestCategory.Raid)
                return "Group";

            // Alles andere: Side
            return "Side";
        }
    }
}
