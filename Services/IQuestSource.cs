using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Interface für Quest-Datenquellen.
    /// Ermöglicht das Laden von Quests aus verschiedenen Quellen (API, DB, JSON, etc.).
    /// </summary>
    public interface IQuestSource
    {
        /// <summary>
        /// Name der Datenquelle (für Anzeige).
        /// </summary>
        string SourceName { get; }

        /// <summary>
        /// Ob die Datenquelle aktuell verfügbar ist.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>
        /// Lädt alle Quests aus der Datenquelle.
        /// </summary>
        /// <param name="languageCode">Sprachcode (z.B. "deDE")</param>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <returns>Liste aller geladenen Quests</returns>
        Task<IEnumerable<Quest>> LoadAllQuestsAsync(
            string languageCode = "deDE",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lädt Quests für eine bestimmte Zone.
        /// </summary>
        /// <param name="zone">Zone zum Filtern</param>
        /// <param name="languageCode">Sprachcode</param>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <returns>Liste der Quests in der Zone</returns>
        Task<IEnumerable<Quest>> LoadQuestsByZoneAsync(
            string zone,
            string languageCode = "deDE",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Lädt eine einzelne Quest nach ID.
        /// </summary>
        /// <param name="questId">Quest-ID</param>
        /// <param name="languageCode">Sprachcode</param>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <returns>Die Quest oder null wenn nicht gefunden</returns>
        Task<Quest?> LoadQuestByIdAsync(
            int questId,
            string languageCode = "deDE",
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gibt eine Liste aller verfügbaren Zonen zurück.
        /// </summary>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <returns>Liste der Zonen</returns>
        Task<IEnumerable<string>> GetAvailableZonesAsync(
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Einfache Wrapper-Implementierung, die eine Sammlung von Quests als Quelle verwendet.
    /// Nützlich für das Laden aus dem MainWindow-ViewModel.
    /// </summary>
    public class InMemoryQuestSource : IQuestSource
    {
        private readonly IEnumerable<Quest> _quests;

        public string SourceName => "In-Memory";
        public bool IsAvailable => true;

        public InMemoryQuestSource(IEnumerable<Quest> quests)
        {
            _quests = quests ?? [];
        }

        public Task<IEnumerable<Quest>> LoadAllQuestsAsync(
            string languageCode = "deDE",
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_quests);
        }

        public Task<IEnumerable<Quest>> LoadQuestsByZoneAsync(
            string zone,
            string languageCode = "deDE",
            CancellationToken cancellationToken = default)
        {
            var result = new List<Quest>();
            foreach (var quest in _quests)
            {
                if (string.Equals(quest.Zone, zone, System.StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(quest);
                }
            }
            return Task.FromResult<IEnumerable<Quest>>(result);
        }

        public Task<Quest?> LoadQuestByIdAsync(
            int questId,
            string languageCode = "deDE",
            CancellationToken cancellationToken = default)
        {
            foreach (var quest in _quests)
            {
                if (quest.QuestId == questId)
                {
                    return Task.FromResult<Quest?>(quest);
                }
            }
            return Task.FromResult<Quest?>(null);
        }

        public Task<IEnumerable<string>> GetAvailableZonesAsync(
            CancellationToken cancellationToken = default)
        {
            var zones = new HashSet<string>();
            foreach (var quest in _quests)
            {
                if (!string.IsNullOrWhiteSpace(quest.Zone))
                {
                    zones.Add(quest.Zone);
                }
            }
            return Task.FromResult<IEnumerable<string>>(zones.OrderBy(z => z));
        }
    }
}
