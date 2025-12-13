using System.Collections.Generic;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Interface fuer AzerothCore-Questdaten (Fallback/Ergaenzung).
    /// Liest aus SQLite-Datenbank (exportiert aus wow_world).
    /// </summary>
    public interface IAcoreQuestSource
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
