using System.Collections.Generic;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Interface fuer Blizzard-Questdaten (Primaerquelle).
    /// Liest aus JSON-Datei oder Blizzard-API.
    /// </summary>
    public interface IBlizzardQuestSource
    {
        /// <summary>
        /// Laedt eine einzelne Quest nach ID.
        /// </summary>
        Task<Quest?> GetQuestByIdAsync(int questId);

        /// <summary>
        /// Laedt alle Quests.
        /// </summary>
        Task<IReadOnlyList<Quest>> GetAllQuestsAsync();

        /// <summary>
        /// Gibt an, ob die Quelle verfuegbar/konfiguriert ist.
        /// </summary>
        bool IsAvailable { get; }
    }
}
