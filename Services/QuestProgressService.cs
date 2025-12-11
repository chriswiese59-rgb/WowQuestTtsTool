using System;
using System.Collections.Generic;
using System.Linq;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Service fuer die Berechnung des Quest-Vertonungs-Fortschritts.
    /// Trennt die Berechnungslogik von der UI-Logik im ProgressWindow.
    /// </summary>
    public class QuestProgressService
    {
        /// <summary>
        /// Berechnet den Vertonungs-Fortschritt pro Zone.
        /// </summary>
        /// <param name="allQuests">Alle verfuegbaren Quests</param>
        /// <param name="audioLookup">Lookup-Dictionary aus dem Audio-Index</param>
        /// <param name="requireBothGenders">Wenn true, gilt eine Quest nur als vertont wenn beide Stimmen (M+W) vorhanden sind</param>
        /// <returns>Liste mit ZoneProgressInfo fuer jede Zone, sortiert nach Zonennamen</returns>
        public List<ZoneProgressInfo> CalculateZoneProgress(
            IEnumerable<Quest> allQuests,
            Dictionary<string, QuestAudioIndexEntry>? audioLookup,
            bool requireBothGenders = true)
        {
            if (allQuests == null)
                return new List<ZoneProgressInfo>();

            audioLookup ??= new Dictionary<string, QuestAudioIndexEntry>();

            // Quests nach Zone gruppieren
            var zoneGroups = allQuests
                .GroupBy(q => q.Zone ?? "Unbekannt")
                .OrderBy(g => g.Key);

            var result = new List<ZoneProgressInfo>();

            foreach (var zoneGroup in zoneGroups)
            {
                var zoneName = zoneGroup.Key;
                var questsInZone = zoneGroup.ToList();
                var totalInZone = questsInZone.Count;

                // Zaehle vertonte Quests in dieser Zone
                int voicedInZone = 0;
                int problemQuestsInZone = 0;
                int mainQuestsInZone = 0;
                int mainQuestsVoicedInZone = 0;

                foreach (var quest in questsInZone)
                {
                    // TTS-Status pruefen
                    bool hasMale = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "male");
                    bool hasFemale = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "female");

                    // Quest gilt als vertont basierend auf Konfiguration
                    bool isVoiced = requireBothGenders
                        ? (hasMale && hasFemale)
                        : (hasMale || hasFemale);

                    if (isVoiced)
                    {
                        voicedInZone++;
                    }

                    // Problem-Quests zaehlen (MixedGermanEnglish oder Incomplete)
                    if (quest.LocalizationStatus == QuestLocalizationStatus.MixedGermanEnglish ||
                        quest.LocalizationStatus == QuestLocalizationStatus.Incomplete)
                    {
                        problemQuestsInZone++;
                    }

                    // Hauptquest-Statistik
                    if (quest.IsMainStory || quest.Category == QuestCategory.Main)
                    {
                        mainQuestsInZone++;
                        if (isVoiced)
                        {
                            mainQuestsVoicedInZone++;
                        }
                    }
                }

                var missingInZone = totalInZone - voicedInZone;
                var progressPercent = totalInZone > 0 ? (voicedInZone * 100.0 / totalInZone) : 0;
                var mainProgressPercent = mainQuestsInZone > 0 ? (mainQuestsVoicedInZone * 100.0 / mainQuestsInZone) : 100;

                var zoneInfo = new ZoneProgressInfo
                {
                    ZoneName = zoneName,
                    TotalQuests = totalInZone,
                    VoicedQuests = voicedInZone,
                    MissingQuests = missingInZone,
                    ProgressPercent = progressPercent,
                    ProblemQuests = problemQuestsInZone,
                    MainQuests = mainQuestsInZone,
                    MainQuestsVoiced = mainQuestsVoicedInZone,
                    MainProgressPercent = mainProgressPercent
                };

                result.Add(zoneInfo);
            }

            return result;
        }

        /// <summary>
        /// Ermittelt fehlende/problematische Quests fuer eine Zone.
        /// </summary>
        /// <param name="zoneName">Name der Zone</param>
        /// <param name="allQuests">Alle verfuegbaren Quests</param>
        /// <param name="audioLookup">Lookup-Dictionary aus dem Audio-Index</param>
        /// <param name="filter">Art der zu filternden Quests</param>
        /// <param name="requireBothGenders">Wenn true, fehlt eine Quest wenn nicht beide Stimmen vorhanden sind</param>
        /// <returns>Gefilterte Quest-Liste, sortiert nach Wichtigkeit</returns>
        public List<Quest> GetFilteredQuestsForZone(
            string zoneName,
            IEnumerable<Quest> allQuests,
            Dictionary<string, QuestAudioIndexEntry>? audioLookup,
            QuestFilterMode filter = QuestFilterMode.MissingAudio,
            bool requireBothGenders = true)
        {
            if (string.IsNullOrEmpty(zoneName) || allQuests == null)
                return new List<Quest>();

            audioLookup ??= new Dictionary<string, QuestAudioIndexEntry>();

            // Alle Quests in dieser Zone
            var questsInZone = allQuests
                .Where(q => (q.Zone ?? "Unbekannt") == zoneName)
                .ToList();

            var result = new List<Quest>();

            foreach (var quest in questsInZone)
            {
                bool hasMale = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "male");
                bool hasFemale = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "female");

                // TTS-Flags fuer die Anzeige aktualisieren
                quest.HasMaleTts = hasMale;
                quest.HasFemaleTts = hasFemale;

                bool isVoiced = requireBothGenders
                    ? (hasMale && hasFemale)
                    : (hasMale || hasFemale);

                bool isProblem = quest.LocalizationStatus == QuestLocalizationStatus.MixedGermanEnglish ||
                                 quest.LocalizationStatus == QuestLocalizationStatus.Incomplete;

                bool includeQuest = filter switch
                {
                    QuestFilterMode.MissingAudio => !isVoiced,
                    QuestFilterMode.ProblemQuestsOnly => isProblem,
                    QuestFilterMode.MissingAndProblem => !isVoiced || isProblem,
                    QuestFilterMode.All => true,
                    _ => !isVoiced
                };

                if (includeQuest)
                {
                    result.Add(quest);
                }
            }

            // Sortierung: Hauptquests zuerst, dann Kategorie, dann ID
            return result
                .OrderByDescending(q => q.IsMainStory || q.Category == QuestCategory.Main)
                .ThenBy(q => q.Category)
                .ThenBy(q => q.LocalizationStatus)
                .ThenBy(q => q.QuestId)
                .ToList();
        }

        /// <summary>
        /// Berechnet Gesamtstatistiken ueber alle Quests.
        /// </summary>
        /// <param name="allQuests">Alle verfuegbaren Quests</param>
        /// <param name="audioLookup">Lookup-Dictionary aus dem Audio-Index</param>
        /// <param name="requireBothGenders">Wenn true, gilt eine Quest nur als vertont wenn beide Stimmen vorhanden sind</param>
        /// <returns>Gesamtstatistik</returns>
        public TotalProgressStats CalculateTotalStats(
            IEnumerable<Quest> allQuests,
            Dictionary<string, QuestAudioIndexEntry>? audioLookup,
            bool requireBothGenders = true)
        {
            if (allQuests == null)
                return new TotalProgressStats();

            audioLookup ??= new Dictionary<string, QuestAudioIndexEntry>();

            int total = 0;
            int voiced = 0;
            int problemQuests = 0;
            int mainQuests = 0;
            int mainQuestsVoiced = 0;

            foreach (var quest in allQuests)
            {
                total++;

                bool hasMale = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "male");
                bool hasFemale = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "female");

                bool isVoiced = requireBothGenders
                    ? (hasMale && hasFemale)
                    : (hasMale || hasFemale);

                if (isVoiced)
                {
                    voiced++;
                }

                if (quest.LocalizationStatus == QuestLocalizationStatus.MixedGermanEnglish ||
                    quest.LocalizationStatus == QuestLocalizationStatus.Incomplete)
                {
                    problemQuests++;
                }

                if (quest.IsMainStory || quest.Category == QuestCategory.Main)
                {
                    mainQuests++;
                    if (isVoiced)
                    {
                        mainQuestsVoiced++;
                    }
                }
            }

            return new TotalProgressStats
            {
                TotalQuests = total,
                VoicedQuests = voiced,
                MissingQuests = total - voiced,
                ProblemQuests = problemQuests,
                MainQuests = mainQuests,
                MainQuestsVoiced = mainQuestsVoiced,
                ProgressPercent = total > 0 ? (voiced * 100.0 / total) : 0,
                MainProgressPercent = mainQuests > 0 ? (mainQuestsVoiced * 100.0 / mainQuests) : 100
            };
        }
    }

    /// <summary>
    /// Filtermodus fuer die Detail-Quest-Anzeige.
    /// </summary>
    public enum QuestFilterMode
    {
        /// <summary>
        /// Nur Quests ohne Audio anzeigen.
        /// </summary>
        MissingAudio,

        /// <summary>
        /// Nur Problem-Quests anzeigen (MixedGermanEnglish oder Incomplete).
        /// </summary>
        ProblemQuestsOnly,

        /// <summary>
        /// Fehlende UND Problem-Quests anzeigen.
        /// </summary>
        MissingAndProblem,

        /// <summary>
        /// Alle Quests der Zone anzeigen.
        /// </summary>
        All
    }

    /// <summary>
    /// Gesamtstatistik ueber alle Quests.
    /// </summary>
    public class TotalProgressStats
    {
        public int TotalQuests { get; set; }
        public int VoicedQuests { get; set; }
        public int MissingQuests { get; set; }
        public int ProblemQuests { get; set; }
        public int MainQuests { get; set; }
        public int MainQuestsVoiced { get; set; }
        public double ProgressPercent { get; set; }
        public double MainProgressPercent { get; set; }
    }
}
