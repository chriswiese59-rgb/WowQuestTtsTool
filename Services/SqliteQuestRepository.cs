using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Quest-Repository fuer SQLite-Datenbanken.
    /// Liest Quests aus einer exportierten SQLite-Datei.
    /// </summary>
    public class SqliteQuestRepository : IQuestRepository, IDisposable
    {
        private readonly string _connectionString;
        private SqliteConnection? _connection;
        private bool _disposed;

        /// <summary>
        /// Erstellt ein neues SqliteQuestRepository.
        /// </summary>
        /// <param name="databasePath">Pfad zur SQLite-Datenbankdatei</param>
        public SqliteQuestRepository(string databasePath)
        {
            if (string.IsNullOrWhiteSpace(databasePath))
                throw new ArgumentException("Datenbankpfad darf nicht leer sein.", nameof(databasePath));

            _connectionString = $"Data Source={databasePath};Mode=ReadOnly";
        }

        /// <summary>
        /// Oeffnet die Datenbankverbindung wenn noetig.
        /// </summary>
        private async Task<SqliteConnection> GetConnectionAsync()
        {
            if (_connection == null)
            {
                _connection = new SqliteConnection(_connectionString);
                await _connection.OpenAsync();
            }
            else if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }
            return _connection;
        }

        /// <summary>
        /// Laedt alle Quests aus der SQLite-Datenbank.
        /// </summary>
        public async Task<List<Quest>> GetAllQuestsAsync()
        {
            var quests = new List<Quest>();
            var connection = await GetConnectionAsync();

            var query = @"
                SELECT
                    quest_id, title, description, objectives, completion,
                    zone, zone_id, required_level, quest_type, suggested_party_size,
                    is_main_story, is_group_quest, category,
                    has_title_de, has_description_de, has_objectives_de, has_completion_de,
                    localization_status
                FROM quests
                ORDER BY quest_id";

            await using var cmd = new SqliteCommand(query, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                quests.Add(MapReaderToQuest(reader));
            }

            return quests;
        }

        /// <summary>
        /// Laedt Quests nach Zone.
        /// </summary>
        public async Task<List<Quest>> GetQuestsByZoneAsync(string zone)
        {
            var quests = new List<Quest>();
            var connection = await GetConnectionAsync();

            var query = @"
                SELECT
                    quest_id, title, description, objectives, completion,
                    zone, zone_id, required_level, quest_type, suggested_party_size,
                    is_main_story, is_group_quest, category,
                    has_title_de, has_description_de, has_objectives_de, has_completion_de,
                    localization_status
                FROM quests
                WHERE zone = @zone
                ORDER BY quest_id";

            await using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@zone", zone);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                quests.Add(MapReaderToQuest(reader));
            }

            return quests;
        }

        /// <summary>
        /// Laedt eine einzelne Quest nach ID.
        /// </summary>
        public async Task<Quest?> GetQuestByIdAsync(int questId)
        {
            var connection = await GetConnectionAsync();

            var query = @"
                SELECT
                    quest_id, title, description, objectives, completion,
                    zone, zone_id, required_level, quest_type, suggested_party_size,
                    is_main_story, is_group_quest, category,
                    has_title_de, has_description_de, has_objectives_de, has_completion_de,
                    localization_status
                FROM quests
                WHERE quest_id = @quest_id";

            await using var cmd = new SqliteCommand(query, connection);
            cmd.Parameters.AddWithValue("@quest_id", questId);
            await using var reader = await cmd.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                return MapReaderToQuest(reader);
            }

            return null;
        }

        /// <summary>
        /// Laedt alle verfuegbaren Zonen.
        /// </summary>
        public async Task<List<string>> GetAllZonesAsync()
        {
            var zones = new List<string>();
            var connection = await GetConnectionAsync();

            var query = @"
                SELECT DISTINCT zone
                FROM quests
                WHERE zone IS NOT NULL AND zone != ''
                ORDER BY zone";

            await using var cmd = new SqliteCommand(query, connection);
            await using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                zones.Add(reader.GetString(0));
            }

            return zones;
        }

        /// <summary>
        /// Prueft die Verbindung zur Datenbank.
        /// </summary>
        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var connection = await GetConnectionAsync();

                await using var cmd = new SqliteCommand("SELECT COUNT(*) FROM quests", connection);
                var result = await cmd.ExecuteScalarAsync();

                return result != null && Convert.ToInt32(result) >= 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gibt die Anzahl der Quests zurueck.
        /// </summary>
        public async Task<int> GetQuestCountAsync()
        {
            var connection = await GetConnectionAsync();

            await using var cmd = new SqliteCommand("SELECT COUNT(*) FROM quests", connection);
            var result = await cmd.ExecuteScalarAsync();

            return result != null ? Convert.ToInt32(result) : 0;
        }

        /// <summary>
        /// Mappt einen SqliteDataReader auf ein Quest-Objekt.
        /// </summary>
        private static Quest MapReaderToQuest(SqliteDataReader reader)
        {
            var quest = new Quest
            {
                QuestId = reader.GetInt32(reader.GetOrdinal("quest_id")),
                Title = GetStringOrNull(reader, "title"),
                Description = GetStringOrNull(reader, "description"),
                Objectives = GetStringOrNull(reader, "objectives"),
                Completion = GetStringOrNull(reader, "completion"),
                Zone = GetStringOrNull(reader, "zone"),
                RequiredLevel = GetIntOrDefault(reader, "required_level"),
                QuestType = GetStringOrNull(reader, "quest_type"),
                SuggestedPartySize = GetIntOrDefault(reader, "suggested_party_size"),
                IsMainStory = GetIntOrDefault(reader, "is_main_story") == 1,
                IsGroupQuest = GetIntOrDefault(reader, "is_group_quest") == 1,
                Category = (QuestCategory)GetIntOrDefault(reader, "category"),
                HasTitleDe = GetIntOrDefault(reader, "has_title_de") == 1,
                HasDescriptionDe = GetIntOrDefault(reader, "has_description_de") == 1,
                HasObjectivesDe = GetIntOrDefault(reader, "has_objectives_de") == 1,
                HasCompletionDe = GetIntOrDefault(reader, "has_completion_de") == 1,
                LocalizationStatus = (QuestLocalizationStatus)GetIntOrDefault(reader, "localization_status")
            };

            return quest;
        }

        /// <summary>
        /// Liest einen String oder gibt null zurueck.
        /// </summary>
        private static string? GetStringOrNull(SqliteDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        /// <summary>
        /// Liest einen Int oder gibt 0 zurueck.
        /// </summary>
        private static int GetIntOrDefault(SqliteDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }

        /// <summary>
        /// Schliesst die Datenbankverbindung.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _connection?.Dispose();
                    _connection = null;
                }
                _disposed = true;
            }
        }
    }
}
