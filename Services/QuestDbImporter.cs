using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Importiert Quest-Texte (Objectives, Completion) aus einer AzerothCore MySQL-Datenbank.
    /// Verwendet MySqlConnector fuer die Datenbankverbindung.
    /// </summary>
    public class QuestDbImporter(QuestDbConfig config)
    {
        private readonly QuestDbConfig _config = config ?? throw new ArgumentNullException(nameof(config));

        /// <summary>
        /// Laedt alle Quest-Texte aus der Datenbank und gibt sie als Dictionary zurueck.
        /// Key = QuestId, Value = PrivateQuestText mit deutschen/englischen Texten.
        /// </summary>
        public async Task<Dictionary<int, PrivateQuestText>> LoadQuestTextsAsync(
            CancellationToken cancellationToken = default)
        {
            var result = new Dictionary<int, PrivateQuestText>();

            // SQL-Abfrage fuer AzerothCore WotLK:
            //
            // Tabellenstruktur:
            // - quest_template / quest_template_locale -> Titel, Details, Objectives
            // - quest_offer_reward / quest_offer_reward_locale -> Belohnungstexte (RewardText)
            // - quest_request_items / quest_request_items_locale -> Anforderungstexte
            //
            // Wir holen:
            // - Objectives: DE aus qtl, EN-Fallback aus qt.LogDescription
            // - Completion/RewardText: DE aus qorl, EN-Fallback aus qor
            var sql = $@"
SELECT
    qt.ID AS quest_id,
    qt.LogDescription AS objectives_en,
    qor.RewardText AS completion_en,
    qtl.Objectives AS objectives_de,
    qorl.RewardText AS completion_de
FROM {_config.QuestTemplateTable} qt
LEFT JOIN {_config.QuestTemplateLocaleTable} qtl
    ON qtl.ID = qt.ID AND qtl.locale = 'deDE'
LEFT JOIN quest_offer_reward qor
    ON qor.ID = qt.ID
LEFT JOIN {_config.QuestOfferRewardLocaleTable} qorl
    ON qorl.ID = qt.ID AND qorl.locale = 'deDE'
";

            await using var connection = new MySqlConnection(_config.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            await using var command = new MySqlCommand(sql, connection);
            command.CommandTimeout = 120; // 2 Minuten Timeout fuer grosse DBs

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                var questId = reader.GetInt32(reader.GetOrdinal("quest_id"));

                var text = new PrivateQuestText
                {
                    QuestId = questId,
                    ObjectivesEn = reader.IsDBNull(reader.GetOrdinal("objectives_en"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("objectives_en")),
                    CompletionEn = reader.IsDBNull(reader.GetOrdinal("completion_en"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("completion_en")),
                    ObjectivesDe = reader.IsDBNull(reader.GetOrdinal("objectives_de"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("objectives_de")),
                    CompletionDe = reader.IsDBNull(reader.GetOrdinal("completion_de"))
                        ? null
                        : reader.GetString(reader.GetOrdinal("completion_de"))
                };

                // Nur hinzufuegen wenn mindestens ein Text vorhanden ist
                if (!string.IsNullOrWhiteSpace(text.ObjectivesDe) ||
                    !string.IsNullOrWhiteSpace(text.ObjectivesEn) ||
                    !string.IsNullOrWhiteSpace(text.CompletionDe) ||
                    !string.IsNullOrWhiteSpace(text.CompletionEn))
                {
                    result[questId] = text;
                }
            }

            return result;
        }

        /// <summary>
        /// Testet die Datenbankverbindung.
        /// </summary>
        public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await using var connection = new MySqlConnection(_config.ConnectionString);
                await connection.OpenAsync(cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gibt die Anzahl der Quests in der Datenbank zurueck.
        /// </summary>
        public async Task<int> GetQuestCountAsync(CancellationToken cancellationToken = default)
        {
            await using var connection = new MySqlConnection(_config.ConnectionString);
            await connection.OpenAsync(cancellationToken);

            var sql = $"SELECT COUNT(*) FROM {_config.QuestTemplateTable}";
            await using var command = new MySqlCommand(sql, connection);
            var count = await command.ExecuteScalarAsync(cancellationToken);
            return Convert.ToInt32(count);
        }
    }
}
