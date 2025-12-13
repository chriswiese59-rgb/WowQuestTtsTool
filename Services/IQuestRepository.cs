using System.Collections.Generic;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Interface fuer Quest-Datenzugriff.
    /// Ermoeglicht Austauschbarkeit zwischen MySQL und SQLite.
    /// </summary>
    public interface IQuestRepository
    {
        /// <summary>
        /// Laedt alle Quests.
        /// </summary>
        Task<List<Quest>> GetAllQuestsAsync();

        /// <summary>
        /// Laedt Quests nach Zone.
        /// </summary>
        Task<List<Quest>> GetQuestsByZoneAsync(string zone);

        /// <summary>
        /// Laedt eine einzelne Quest nach ID.
        /// </summary>
        Task<Quest?> GetQuestByIdAsync(int questId);

        /// <summary>
        /// Laedt alle verfuegbaren Zonen.
        /// </summary>
        Task<List<string>> GetAllZonesAsync();

        /// <summary>
        /// Prueft die Verbindung zur Datenquelle.
        /// </summary>
        Task<bool> TestConnectionAsync();

        /// <summary>
        /// Gibt die Anzahl der Quests zurueck.
        /// </summary>
        Task<int> GetQuestCountAsync();
    }
}
