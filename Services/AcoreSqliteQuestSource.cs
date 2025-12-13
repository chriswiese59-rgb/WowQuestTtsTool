using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Liest AzerothCore-Quests aus einer SQLite-Datenbank.
    /// Die SQLite wurde vorher mit dem WowQuestExporter aus MySQL erstellt.
    /// </summary>
    public class AcoreSqliteQuestSource : IAcoreQuestSource, IDisposable
    {
        private readonly string _databasePath;
        private SqliteConnection? _connection;
        private List<Quest>? _cachedQuests;
        private Dictionary<int, Quest>? _questLookup;
        private bool _disposed;

        public AcoreSqliteQuestSource(string databasePath)
        {
            _databasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        }

        /// <summary>
        /// Gibt an, ob die SQLite-Datei existiert.
        /// </summary>
        public bool IsAvailable => File.Exists(_databasePath);

        /// <summary>
        /// Laedt eine einzelne Quest nach ID.
        /// </summary>
        public async Task<Quest?> GetQuestByIdAsync(int questId)
        {
            await EnsureLoadedAsync();

            if (_questLookup == null)
                return null;

            _questLookup.TryGetValue(questId, out var quest);
            return quest;
        }

        /// <summary>
        /// Laedt alle Quests aus der SQLite-Datenbank.
        /// </summary>
        public async Task<IReadOnlyList<Quest>> GetAllQuestsAsync()
        {
            await EnsureLoadedAsync();
            return _cachedQuests ?? (IReadOnlyList<Quest>)Array.Empty<Quest>();
        }

        /// <summary>
        /// Oeffnet die Datenbankverbindung.
        /// </summary>
        private async Task<SqliteConnection> GetConnectionAsync()
        {
            if (_connection == null)
            {
                _connection = new SqliteConnection($"Data Source={_databasePath};Mode=ReadOnly");
                await _connection.OpenAsync();
            }
            else if (_connection.State != System.Data.ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }
            return _connection;
        }

        /// <summary>
        /// Laedt alle Quests wenn noch nicht geschehen.
        /// </summary>
        private async Task EnsureLoadedAsync()
        {
            if (_cachedQuests != null)
                return;

            if (!IsAvailable)
            {
                _cachedQuests = new List<Quest>();
                _questLookup = new Dictionary<int, Quest>();
                return;
            }

            try
            {
                var connection = await GetConnectionAsync();

                // Zuerst normale Quest-Daten laden
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

                var quests = new List<Quest>();

                while (await reader.ReadAsync())
                {
                    var quest = MapReaderToQuest(reader);
                    quest.HasBlizzardSource = false;
                    quest.HasAcoreSource = true;
                    quests.Add(quest);
                }

                _cachedQuests = quests;
                _questLookup = _cachedQuests.ToDictionary(q => q.QuestId);

                // RewardText aus quest_offer_reward_locale laden (falls Tabelle existiert)
                await LoadRewardTextsAsync(connection);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der AzerothCore-SQLite: {ex.Message}");
                _cachedQuests = new List<Quest>();
                _questLookup = new Dictionary<int, Quest>();
            }
        }

        /// <summary>
        /// Laedt RewardText aus der quest_offer_reward_locale Tabelle.
        /// RewardText = Text bei Quest-Abgabe (vor der Belohnung).
        /// </summary>
        private async Task LoadRewardTextsAsync(SqliteConnection connection)
        {
            if (_questLookup == null || _questLookup.Count == 0)
                return;

            try
            {
                // Pruefen ob die Tabelle existiert
                var checkTableQuery = @"
                    SELECT name FROM sqlite_master
                    WHERE type='table' AND name='quest_offer_reward_locale'";

                await using var checkCmd = new SqliteCommand(checkTableQuery, connection);
                var tableName = await checkCmd.ExecuteScalarAsync();

                if (tableName == null)
                {
                    System.Diagnostics.Debug.WriteLine("quest_offer_reward_locale Tabelle nicht gefunden - RewardText wird uebersprungen.");
                    return;
                }

                // RewardText laden (Text bei Quest-Abgabe, vor Belohnung)
                // Prioritaet: deDE > enUS (falls vorhanden)
                var query = @"
                    SELECT
                        ID as quest_id,
                        locale,
                        RewardText as reward_text
                    FROM quest_offer_reward_locale
                    WHERE locale IN ('deDE', 'enUS')
                    AND RewardText IS NOT NULL
                    AND TRIM(RewardText) != ''
                    ORDER BY ID, CASE locale WHEN 'deDE' THEN 0 ELSE 1 END";

                await using var cmd = new SqliteCommand(query, connection);
                await using var reader = await cmd.ExecuteReaderAsync();

                var processedQuests = new HashSet<int>();

                while (await reader.ReadAsync())
                {
                    var questId = reader.GetInt32(reader.GetOrdinal("quest_id"));
                    var locale = reader.GetString(reader.GetOrdinal("locale"));
                    var rewardText = GetStringOrNull(reader, "reward_text");

                    // Nur den ersten Treffer pro Quest nehmen (deDE hat Prioritaet)
                    if (processedQuests.Contains(questId))
                        continue;

                    if (_questLookup.TryGetValue(questId, out var quest) && !string.IsNullOrWhiteSpace(rewardText))
                    {
                        quest.SetRewardTextFromSource(rewardText, QuestTextSource.AzerothCore);
                        processedQuests.Add(questId);
                    }
                }

                System.Diagnostics.Debug.WriteLine($"RewardText fuer {processedQuests.Count} Quests geladen.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden von RewardText: {ex.Message}");
                // Kein Fehler werfen - RewardText ist optional
            }
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

        private static string? GetStringOrNull(SqliteDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        private static int GetIntOrDefault(SqliteDataReader reader, string column)
        {
            var ordinal = reader.GetOrdinal(column);
            return reader.IsDBNull(ordinal) ? 0 : reader.GetInt32(ordinal);
        }

        /// <summary>
        /// Invalidiert den Cache.
        /// </summary>
        public void InvalidateCache()
        {
            _cachedQuests = null;
            _questLookup = null;
        }

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
