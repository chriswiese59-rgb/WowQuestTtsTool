using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Repository das Blizzard- und AzerothCore-Daten zur Laufzeit zusammenfuehrt.
    ///
    /// MERGE-REGELN:
    /// 1. Blizzard ist IMMER die BASIS - nur Blizzard-Quests werden geladen
    /// 2. AzerothCore ist NUR Fallback/Ergaenzung fuer fehlende Felder
    /// 3. Felder wie Title, Description, Zone kommen bevorzugt von Blizzard
    /// 4. Objectives und Completion werden von AzerothCore ergaenzt wenn bei Blizzard leer
    /// 5. Quests die NUR in AzerothCore existieren werden NICHT hinzugefuegt
    /// 6. Blizzard-Daten werden NIE durch AzerothCore ersetzt
    /// </summary>
    public class MergedQuestRepository : IQuestRepository
    {
        private readonly IBlizzardQuestSource _blizzard;
        private readonly IAcoreQuestSource _acore;

        // Cache fuer gemergete Quests (optional, fuer Performance)
        private IReadOnlyList<Quest>? _cachedQuests;
        private DateTime _cacheTime;
        private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

        public MergedQuestRepository(IBlizzardQuestSource blizzard, IAcoreQuestSource acore)
        {
            _blizzard = blizzard ?? throw new ArgumentNullException(nameof(blizzard));
            _acore = acore ?? throw new ArgumentNullException(nameof(acore));
        }

        /// <summary>
        /// Laedt eine einzelne Quest nach ID (gemerged).
        /// </summary>
        public async Task<Quest?> GetQuestByIdAsync(int questId)
        {
            // Parallel von beiden Quellen laden
            var blizzTask = _blizzard.IsAvailable
                ? _blizzard.GetQuestByIdAsync(questId)
                : Task.FromResult<Quest?>(null);

            var acoreTask = _acore.IsAvailable
                ? _acore.GetQuestByIdAsync(questId)
                : Task.FromResult<Quest?>(null);

            await Task.WhenAll(blizzTask, acoreTask);

            return Merge(blizzTask.Result, acoreTask.Result);
        }

        /// <summary>
        /// Laedt alle Quests (gemerged).
        /// </summary>
        public async Task<List<Quest>> GetAllQuestsAsync()
        {
            // Cache pruefen
            if (_cachedQuests != null && DateTime.Now - _cacheTime < _cacheExpiry)
            {
                return _cachedQuests.ToList();
            }

            // Parallel von beiden Quellen laden
            var blizzTask = _blizzard.IsAvailable
                ? _blizzard.GetAllQuestsAsync()
                : Task.FromResult<IReadOnlyList<Quest>>(Array.Empty<Quest>());

            var acoreTask = _acore.IsAvailable
                ? _acore.GetAllQuestsAsync()
                : Task.FromResult<IReadOnlyList<Quest>>(Array.Empty<Quest>());

            await Task.WhenAll(blizzTask, acoreTask);

            var blizzList = blizzTask.Result;
            var acoreList = acoreTask.Result;

            // Dictionaries fuer schnellen Lookup
            var blizzDict = blizzList.ToDictionary(q => q.QuestId);
            var acoreDict = acoreList.ToDictionary(q => q.QuestId);

            // NUR Blizzard-IDs als Basis - AzerothCore-only Quests werden NICHT hinzugefuegt
            // Wenn 10 Quests von Blizzard geladen, bleiben es 10 Quests (mit ACore-Ergaenzungen)
            var blizzardIds = blizzDict.Keys;

            // Mergen - nur fuer Blizzard-Quests
            var result = new List<Quest>(blizzardIds.Count());

            foreach (var id in blizzardIds.OrderBy(x => x))
            {
                blizzDict.TryGetValue(id, out var blizz);
                acoreDict.TryGetValue(id, out var acore);

                var merged = Merge(blizz, acore);
                if (merged != null)
                {
                    result.Add(merged);
                }
            }

            // Cache aktualisieren
            _cachedQuests = result;
            _cacheTime = DateTime.Now;

            return result;
        }

        /// <summary>
        /// Laedt Quests nach Zone (gemerged).
        /// </summary>
        public async Task<List<Quest>> GetQuestsByZoneAsync(string zone)
        {
            var all = await GetAllQuestsAsync();
            return all
                .Where(q => string.Equals(q.Zone, zone, StringComparison.OrdinalIgnoreCase))
                .OrderBy(q => q.QuestId)
                .ToList();
        }

        /// <summary>
        /// Laedt alle verfuegbaren Zonen.
        /// </summary>
        public async Task<List<string>> GetAllZonesAsync()
        {
            var all = await GetAllQuestsAsync();
            return all
                .Where(q => !string.IsNullOrWhiteSpace(q.Zone))
                .Select(q => q.Zone!)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(z => z)
                .ToList();
        }

        /// <summary>
        /// Prueft die Verbindung (mindestens eine Quelle muss verfuegbar sein).
        /// </summary>
        public Task<bool> TestConnectionAsync()
        {
            return Task.FromResult(_blizzard.IsAvailable || _acore.IsAvailable);
        }

        /// <summary>
        /// Gibt die Anzahl der gemergeten Quests zurueck.
        /// </summary>
        public async Task<int> GetQuestCountAsync()
        {
            var all = await GetAllQuestsAsync();
            return all.Count;
        }

        /// <summary>
        /// Invalidiert den Cache (z.B. nach Datenaktualisierung).
        /// </summary>
        public void InvalidateCache()
        {
            _cachedQuests = null;
        }

        #region Merge-Logik

        /// <summary>
        /// Fuehrt zwei Quest-Objekte zusammen.
        ///
        /// REGELN:
        /// - Blizzard ist IMMER Basis - ohne Blizzard keine Quest
        /// - AzerothCore ergaenzt NUR fehlende Felder
        /// - Objectives/Completion: Blizzard bevorzugen, bei leer aus AzerothCore
        /// </summary>
        private Quest? Merge(Quest? blizz, Quest? acore)
        {
            // Beide null -> nichts
            if (blizz == null && acore == null)
                return null;

            // Nur Blizzard vorhanden - OK
            if (blizz != null && acore == null)
            {
                blizz.HasBlizzardSource = true;
                blizz.HasAcoreSource = false;
                return blizz;
            }

            // Nur AzerothCore vorhanden - NICHT hinzufuegen (Blizzard ist Pflicht)
            if (blizz == null && acore != null)
            {
                return null; // AzerothCore-only Quests werden NICHT hinzugefuegt
            }

            // BEIDE vorhanden -> Merge mit Blizzard als Primaerquelle
            var result = new Quest
            {
                QuestId = blizz!.QuestId,
                HasBlizzardSource = true,
                HasAcoreSource = true,

                // === PRIMAER-FELDER: Immer von Blizzard (mit Fallback auf ACore) ===
                Title = Choose(blizz.Title, acore!.Title),
                Description = Choose(blizz.Description, acore.Description),
                Zone = Choose(blizz.Zone, acore.Zone),

                // === ERGAENZUNGS-FELDER: Blizzard bevorzugen, ACore als Fallback ===
                // Diese Felder sind oft bei Blizzard leer, aber in AzerothCore vorhanden
                Objectives = ChooseWithFallback(blizz.Objectives, acore.Objectives),
                Completion = ChooseWithFallback(blizz.Completion, acore.Completion),

                // === METADATEN: Blizzard bevorzugen ===
                IsMainStory = blizz.IsMainStory || acore.IsMainStory,
                Category = blizz.Category != QuestCategory.Unknown ? blizz.Category : acore.Category,
                IsGroupQuest = blizz.IsGroupQuest || acore.IsGroupQuest,
                SuggestedPartySize = blizz.SuggestedPartySize > 0 ? blizz.SuggestedPartySize : acore.SuggestedPartySize,
                RequiredLevel = blizz.RequiredLevel > 0 ? blizz.RequiredLevel : acore.RequiredLevel,
                QuestType = Choose(blizz.QuestType, acore.QuestType),

                // === LOKALISIERUNGS-FLAGS: OR-Verknuepfung ===
                HasTitleDe = blizz.HasTitleDe || acore.HasTitleDe,
                HasDescriptionDe = blizz.HasDescriptionDe || acore.HasDescriptionDe,
                HasObjectivesDe = blizz.HasObjectivesDe || acore.HasObjectivesDe,
                HasCompletionDe = blizz.HasCompletionDe || acore.HasCompletionDe,

                // Lokalisierungsstatus neu berechnen
                LocalizationStatus = CalculateMergedLocalizationStatus(blizz, acore),

                // === TTS-FLAGS: Uebernehmen von existierenden Quests ===
                TtsReviewed = blizz.TtsReviewed || acore.TtsReviewed,
                CustomTtsText = Choose(blizz.CustomTtsText, acore.CustomTtsText),
                HasTtsAudio = blizz.HasTtsAudio || acore.HasTtsAudio,
                HasMaleTts = blizz.HasMaleTts || acore.HasMaleTts,
                HasFemaleTts = blizz.HasFemaleTts || acore.HasFemaleTts,
            };

            // === REWARD TEXT: AzerothCore als primaere Quelle (quest_offer_reward_locale) ===
            // Blizzard hat dieses Feld meistens nicht, AzerothCore hat die Abgabe-Texte
            MergeRewardText(result, blizz, acore);

            return result;
        }

        /// <summary>
        /// Waehlt den ersten nicht-leeren Wert (Primaer vor Sekundaer).
        /// </summary>
        private static string? Choose(string? primary, string? secondary)
        {
            return !string.IsNullOrWhiteSpace(primary) ? primary : secondary;
        }

        /// <summary>
        /// Waehlt Primaer wenn nicht leer, sonst Fallback.
        /// Speziell fuer Felder die bei Blizzard oft fehlen.
        /// </summary>
        private static string? ChooseWithFallback(string? primary, string? fallback)
        {
            // Primaer bevorzugen wenn NICHT leer
            if (!string.IsNullOrWhiteSpace(primary))
                return primary;

            // Sonst Fallback
            return !string.IsNullOrWhiteSpace(fallback) ? fallback : primary;
        }

        /// <summary>
        /// Berechnet den Lokalisierungsstatus fuer gemergete Quest.
        /// </summary>
        private static QuestLocalizationStatus CalculateMergedLocalizationStatus(Quest blizz, Quest acore)
        {
            // Zaehle wie viele Felder jetzt auf Deutsch sind
            int deCount = 0;

            if (blizz.HasTitleDe || acore.HasTitleDe) deCount++;
            if (blizz.HasDescriptionDe || acore.HasDescriptionDe) deCount++;
            if (blizz.HasObjectivesDe || acore.HasObjectivesDe) deCount++;
            if (blizz.HasCompletionDe || acore.HasCompletionDe) deCount++;

            return deCount switch
            {
                4 => QuestLocalizationStatus.FullyGerman,
                0 => QuestLocalizationStatus.OnlyEnglish,
                >= 2 => QuestLocalizationStatus.MixedGermanEnglish,
                _ => QuestLocalizationStatus.Incomplete
            };
        }

        /// <summary>
        /// Fuehrt RewardText von beiden Quellen zusammen.
        /// AzerothCore ist primaere Quelle fuer quest_offer_reward_locale Daten.
        /// </summary>
        private static void MergeRewardText(Quest result, Quest blizz, Quest acore)
        {
            // AzerothCore hat RewardText aus quest_offer_reward_locale
            if (acore.HasRewardText)
            {
                // ACore hat RewardText -> uebernehmen
                result.OriginalRewardText = acore.OriginalRewardText ?? acore.RewardText;
                result.RewardText = acore.RewardText;
                result.RewardTextSource = acore.RewardTextSource != QuestTextSource.None
                    ? acore.RewardTextSource
                    : QuestTextSource.AzerothCore;
                result.IsRewardTextOverridden = acore.IsRewardTextOverridden;
            }
            else if (blizz.HasRewardText)
            {
                // Fallback auf Blizzard wenn ACore leer
                result.OriginalRewardText = blizz.OriginalRewardText ?? blizz.RewardText;
                result.RewardText = blizz.RewardText;
                result.RewardTextSource = blizz.RewardTextSource != QuestTextSource.None
                    ? blizz.RewardTextSource
                    : QuestTextSource.Blizzard;
                result.IsRewardTextOverridden = blizz.IsRewardTextOverridden;
            }
            // Sonst: Kein RewardText vorhanden - Felder bleiben leer/default

            // RequestItemsText uebernehmen (falls vorhanden)
            if (!string.IsNullOrWhiteSpace(acore.RequestItemsText))
            {
                result.RequestItemsText = acore.RequestItemsText;
            }
            else if (!string.IsNullOrWhiteSpace(blizz.RequestItemsText))
            {
                result.RequestItemsText = blizz.RequestItemsText;
            }
        }

        #endregion
    }
}
