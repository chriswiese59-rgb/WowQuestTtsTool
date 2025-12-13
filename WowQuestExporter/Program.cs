using Microsoft.Data.Sqlite;
using MySqlConnector;

namespace WowQuestExporter;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine("  WoW Quest Exporter - MySQL -> SQLite");
        Console.WriteLine("=================================================");
        Console.WriteLine();

        var settings = ExporterSettings.ParseArgs(args);

        Console.WriteLine($"MySQL Host:     {settings.MySqlHost}:{settings.MySqlPort}");
        Console.WriteLine($"MySQL Database: {settings.MySqlDatabase}");
        Console.WriteLine($"MySQL User:     {settings.MySqlUser}");
        Console.WriteLine($"SQLite Output:  {settings.SqliteOutputPath}");
        Console.WriteLine($"Locale:         {settings.Locale}");
        Console.WriteLine();

        try
        {
            // Schritt 1: SQLite-Datenbank erstellen/oeffnen
            Console.WriteLine("[1/4] Erstelle SQLite-Datenbank...");
            await CreateSqliteDatabase(settings.SqliteOutputPath);

            // Schritt 2: MySQL-Verbindung testen
            Console.WriteLine("[2/4] Verbinde mit MySQL...");
            await using var mysqlConnection = new MySqlConnection(settings.GetMySqlConnectionString());
            await mysqlConnection.OpenAsync();
            Console.WriteLine("      MySQL-Verbindung OK!");

            // Schritt 3: Quests exportieren
            Console.WriteLine("[3/4] Exportiere Quests...");
            var exportedCount = await ExportQuests(mysqlConnection, settings);
            Console.WriteLine($"      {exportedCount} Quests exportiert!");

            // Schritt 4: RewardText exportieren (quest_offer_reward_locale)
            Console.WriteLine("[4/5] Exportiere RewardText (quest_offer_reward_locale)...");
            var rewardCount = await ExportRewardTexts(mysqlConnection, settings);
            Console.WriteLine($"      {rewardCount} RewardTexts exportiert!");

            // Schritt 5: Zonen exportieren
            Console.WriteLine("[5/5] Exportiere Zonen...");
            var zonesCount = await ExportZones(mysqlConnection, settings);
            Console.WriteLine($"      {zonesCount} Zonen exportiert!");

            Console.WriteLine();
            Console.WriteLine("=================================================");
            Console.WriteLine($"  Export abgeschlossen!");
            Console.WriteLine($"  Datei: {Path.GetFullPath(settings.SqliteOutputPath)}");
            Console.WriteLine("=================================================");

            return 0;
        }
        catch (MySqlException ex)
        {
            Console.WriteLine();
            Console.WriteLine($"MySQL-FEHLER: {ex.Message}");
            Console.WriteLine();
            Console.WriteLine("Tipps:");
            Console.WriteLine("  - Ist der MySQL-Server gestartet?");
            Console.WriteLine("  - Stimmen Host, Port, User und Passwort?");
            Console.WriteLine("  - Existiert die Datenbank?");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"FEHLER: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Erstellt die SQLite-Datenbank mit dem Quest-Schema.
    /// </summary>
    static async Task CreateSqliteDatabase(string path)
    {
        // Bestehende Datei loeschen
        if (File.Exists(path))
        {
            File.Delete(path);
            Console.WriteLine($"      Bestehende Datei geloescht.");
        }

        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();

        // Quest-Tabelle erstellen
        var createQuestsTable = @"
            CREATE TABLE IF NOT EXISTS quests (
                quest_id INTEGER PRIMARY KEY,
                title TEXT,
                description TEXT,
                objectives TEXT,
                completion TEXT,
                zone TEXT,
                zone_id INTEGER,
                required_level INTEGER DEFAULT 0,
                quest_type TEXT,
                suggested_party_size INTEGER DEFAULT 0,
                is_main_story INTEGER DEFAULT 0,
                is_group_quest INTEGER DEFAULT 0,
                category INTEGER DEFAULT 0,
                has_title_de INTEGER DEFAULT 0,
                has_description_de INTEGER DEFAULT 0,
                has_objectives_de INTEGER DEFAULT 0,
                has_completion_de INTEGER DEFAULT 0,
                localization_status INTEGER DEFAULT 3,
                created_at TEXT DEFAULT CURRENT_TIMESTAMP,
                updated_at TEXT DEFAULT CURRENT_TIMESTAMP
            );

            CREATE INDEX IF NOT EXISTS idx_quests_zone ON quests(zone);
            CREATE INDEX IF NOT EXISTS idx_quests_zone_id ON quests(zone_id);
            CREATE INDEX IF NOT EXISTS idx_quests_category ON quests(category);
            CREATE INDEX IF NOT EXISTS idx_quests_localization ON quests(localization_status);
        ";

        await using var cmd = new SqliteCommand(createQuestsTable, connection);
        await cmd.ExecuteNonQueryAsync();

        // Zonen-Tabelle erstellen
        var createZonesTable = @"
            CREATE TABLE IF NOT EXISTS zones (
                zone_id INTEGER PRIMARY KEY,
                zone_name TEXT,
                locale TEXT DEFAULT 'deDE'
            );

            CREATE INDEX IF NOT EXISTS idx_zones_name ON zones(zone_name);
        ";

        await using var cmd2 = new SqliteCommand(createZonesTable, connection);
        await cmd2.ExecuteNonQueryAsync();

        // quest_offer_reward_locale Tabelle erstellen (RewardText bei Quest-Abgabe)
        var createRewardTable = @"
            CREATE TABLE IF NOT EXISTS quest_offer_reward_locale (
                ID INTEGER,
                locale TEXT,
                RewardText TEXT,
                VerifiedBuild INTEGER DEFAULT 0,
                PRIMARY KEY (ID, locale)
            );

            CREATE INDEX IF NOT EXISTS idx_reward_quest ON quest_offer_reward_locale(ID);
            CREATE INDEX IF NOT EXISTS idx_reward_locale ON quest_offer_reward_locale(locale);
        ";

        await using var cmd3 = new SqliteCommand(createRewardTable, connection);
        await cmd3.ExecuteNonQueryAsync();

        // Metadaten-Tabelle erstellen
        var createMetaTable = @"
            CREATE TABLE IF NOT EXISTS export_meta (
                key TEXT PRIMARY KEY,
                value TEXT
            );

            INSERT INTO export_meta (key, value) VALUES ('export_date', datetime('now'));
            INSERT INTO export_meta (key, value) VALUES ('exporter_version', '1.1.0');
        ";

        await using var cmd4 = new SqliteCommand(createMetaTable, connection);
        await cmd4.ExecuteNonQueryAsync();

        Console.WriteLine("      SQLite-Schema erstellt.");
    }

    /// <summary>
    /// Exportiert Quests von MySQL nach SQLite.
    /// </summary>
    static async Task<int> ExportQuests(MySqlConnection mysql, ExporterSettings settings)
    {
        // Versuche erst herauszufinden welche Zonen-Tabelle existiert
        string? zoneTableName = await FindZoneTableAsync(mysql);

        // Basis-Query fuer quest_template_locale (lokalisierte Texte)
        string query;
        if (zoneTableName != null)
        {
            // Mit Zonen-Tabelle
            query = $@"
                SELECT
                    qt.ID as quest_id,
                    COALESCE(qtl.Title, qt.LogTitle) as title,
                    COALESCE(qtl.Details, qt.LogDescription) as description,
                    COALESCE(qtl.Objectives, qt.QuestDescription) as objectives,
                    COALESCE(qtl.CompletedText, qt.QuestCompletionLog) as completion,
                    qt.QuestLevel as required_level,
                    qt.QuestType as quest_type,
                    qt.SuggestedGroupNum as suggested_party_size,
                    qt.AllowableRaces,
                    zt.name as zone_name,
                    qt.QuestSortID as zone_id,
                    CASE WHEN qtl.Title IS NOT NULL AND LENGTH(qtl.Title) > 0 THEN 1 ELSE 0 END as has_title_de,
                    CASE WHEN qtl.Details IS NOT NULL AND LENGTH(qtl.Details) > 0 THEN 1 ELSE 0 END as has_description_de,
                    CASE WHEN qtl.Objectives IS NOT NULL AND LENGTH(qtl.Objectives) > 0 THEN 1 ELSE 0 END as has_objectives_de,
                    CASE WHEN qtl.CompletedText IS NOT NULL AND LENGTH(qtl.CompletedText) > 0 THEN 1 ELSE 0 END as has_completion_de
                FROM quest_template qt
                LEFT JOIN quest_template_locale qtl ON qt.ID = qtl.ID AND qtl.locale = @locale
                LEFT JOIN {zoneTableName} zt ON ABS(qt.QuestSortID) = zt.ID
                WHERE 1=1
            ";
            Console.WriteLine($"      Verwende Zonen-Tabelle: {zoneTableName}");
        }
        else
        {
            // Ohne Zonen-Tabelle - verwende QuestSortID als Zone
            query = @"
                SELECT
                    qt.ID as quest_id,
                    COALESCE(qtl.Title, qt.LogTitle) as title,
                    COALESCE(qtl.Details, qt.LogDescription) as description,
                    COALESCE(qtl.Objectives, qt.QuestDescription) as objectives,
                    COALESCE(qtl.CompletedText, qt.QuestCompletionLog) as completion,
                    qt.QuestLevel as required_level,
                    qt.QuestType as quest_type,
                    qt.SuggestedGroupNum as suggested_party_size,
                    qt.AllowableRaces,
                    CONCAT('Zone_', ABS(qt.QuestSortID)) as zone_name,
                    qt.QuestSortID as zone_id,
                    CASE WHEN qtl.Title IS NOT NULL AND LENGTH(qtl.Title) > 0 THEN 1 ELSE 0 END as has_title_de,
                    CASE WHEN qtl.Details IS NOT NULL AND LENGTH(qtl.Details) > 0 THEN 1 ELSE 0 END as has_description_de,
                    CASE WHEN qtl.Objectives IS NOT NULL AND LENGTH(qtl.Objectives) > 0 THEN 1 ELSE 0 END as has_objectives_de,
                    CASE WHEN qtl.CompletedText IS NOT NULL AND LENGTH(qtl.CompletedText) > 0 THEN 1 ELSE 0 END as has_completion_de
                FROM quest_template qt
                LEFT JOIN quest_template_locale qtl ON qt.ID = qtl.ID AND qtl.locale = @locale
                WHERE 1=1
            ";
            Console.WriteLine("      Warnung: Keine Zonen-Tabelle gefunden, verwende Zone-IDs.");
        }

        // ID-Filter hinzufuegen
        if (settings.MinQuestId.HasValue)
            query += $" AND qt.ID >= {settings.MinQuestId.Value}";
        if (settings.MaxQuestId.HasValue)
            query += $" AND qt.ID <= {settings.MaxQuestId.Value}";

        query += " ORDER BY qt.ID";

        await using var mysqlCmd = new MySqlCommand(query, mysql);
        mysqlCmd.Parameters.AddWithValue("@locale", settings.Locale);

        // SQLite-Verbindung oeffnen
        await using var sqlite = new SqliteConnection($"Data Source={settings.SqliteOutputPath}");
        await sqlite.OpenAsync();

        // Transaktion fuer Performance
        await using var transaction = sqlite.BeginTransaction();

        var insertQuery = @"
            INSERT INTO quests (
                quest_id, title, description, objectives, completion,
                zone, zone_id, required_level, quest_type, suggested_party_size,
                is_main_story, is_group_quest, category,
                has_title_de, has_description_de, has_objectives_de, has_completion_de,
                localization_status
            ) VALUES (
                @quest_id, @title, @description, @objectives, @completion,
                @zone, @zone_id, @required_level, @quest_type, @suggested_party_size,
                @is_main_story, @is_group_quest, @category,
                @has_title_de, @has_description_de, @has_objectives_de, @has_completion_de,
                @localization_status
            )";

        int count = 0;
        await using var reader = await mysqlCmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var questId = reader.GetInt32("quest_id");
            var title = GetStringOrNull(reader, "title");
            var description = GetStringOrNull(reader, "description");
            var objectives = GetStringOrNull(reader, "objectives");
            var completion = GetStringOrNull(reader, "completion");
            var zoneName = GetStringOrNull(reader, "zone_name");
            var zoneId = reader.IsDBNull(reader.GetOrdinal("zone_id")) ? 0 : reader.GetInt32("zone_id");
            var requiredLevel = reader.IsDBNull(reader.GetOrdinal("required_level")) ? 0 : reader.GetInt32("required_level");
            var questType = reader.IsDBNull(reader.GetOrdinal("quest_type")) ? 0 : reader.GetInt32("quest_type");
            var suggestedPartySize = reader.IsDBNull(reader.GetOrdinal("suggested_party_size")) ? 0 : reader.GetInt32("suggested_party_size");

            // Lokalisierungsflags
            var hasTitleDe = reader.GetInt32("has_title_de") == 1;
            var hasDescriptionDe = reader.GetInt32("has_description_de") == 1;
            var hasObjectivesDe = reader.GetInt32("has_objectives_de") == 1;
            var hasCompletionDe = reader.GetInt32("has_completion_de") == 1;

            // Lokalisierungsstatus berechnen
            var locStatus = CalculateLocalizationStatus(hasTitleDe, hasDescriptionDe, hasObjectivesDe, hasCompletionDe);

            // Kategorie bestimmen
            var category = DetermineCategory(questType, suggestedPartySize);
            var isGroupQuest = suggestedPartySize >= 2 || questType == 81 || questType == 82; // 81=Dungeon, 82=Raid

            await using var insertCmd = new SqliteCommand(insertQuery, sqlite, transaction);
            insertCmd.Parameters.AddWithValue("@quest_id", questId);
            insertCmd.Parameters.AddWithValue("@title", (object?)title ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@description", (object?)description ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@objectives", (object?)objectives ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@completion", (object?)completion ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@zone", (object?)zoneName ?? DBNull.Value);
            insertCmd.Parameters.AddWithValue("@zone_id", zoneId);
            insertCmd.Parameters.AddWithValue("@required_level", requiredLevel);
            insertCmd.Parameters.AddWithValue("@quest_type", questType.ToString());
            insertCmd.Parameters.AddWithValue("@suggested_party_size", suggestedPartySize);
            insertCmd.Parameters.AddWithValue("@is_main_story", 0);
            insertCmd.Parameters.AddWithValue("@is_group_quest", isGroupQuest ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@category", (int)category);
            insertCmd.Parameters.AddWithValue("@has_title_de", hasTitleDe ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@has_description_de", hasDescriptionDe ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@has_objectives_de", hasObjectivesDe ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@has_completion_de", hasCompletionDe ? 1 : 0);
            insertCmd.Parameters.AddWithValue("@localization_status", (int)locStatus);

            await insertCmd.ExecuteNonQueryAsync();
            count++;

            // Fortschritt anzeigen
            if (count % 1000 == 0)
            {
                Console.WriteLine($"      {count} Quests verarbeitet...");
            }
        }

        await transaction.CommitAsync();

        // Locale in Metadaten speichern
        await using var metaCmd = new SqliteCommand(
            "INSERT OR REPLACE INTO export_meta (key, value) VALUES ('locale', @locale)", sqlite);
        metaCmd.Parameters.AddWithValue("@locale", settings.Locale);
        await metaCmd.ExecuteNonQueryAsync();

        return count;
    }

    /// <summary>
    /// Exportiert RewardText aus quest_offer_reward_locale.
    /// Das ist der Text der bei Quest-Abgabe angezeigt wird, bevor die Belohnung erhalten wird.
    /// </summary>
    static async Task<int> ExportRewardTexts(MySqlConnection mysql, ExporterSettings settings)
    {
        // Pruefen ob Tabelle existiert
        try
        {
            var checkQuery = "SELECT 1 FROM quest_offer_reward_locale LIMIT 1";
            await using var checkCmd = new MySqlCommand(checkQuery, mysql);
            await checkCmd.ExecuteScalarAsync();
        }
        catch (MySqlException)
        {
            Console.WriteLine("      Warnung: quest_offer_reward_locale Tabelle nicht gefunden, ueberspringe.");
            return 0;
        }

        // RewardText fuer die gewuenschte Locale laden
        var query = @"
            SELECT
                ID,
                locale,
                RewardText,
                VerifiedBuild
            FROM quest_offer_reward_locale
            WHERE locale = @locale
            AND RewardText IS NOT NULL
            AND TRIM(RewardText) != ''
            ORDER BY ID
        ";

        await using var mysqlCmd = new MySqlCommand(query, mysql);
        mysqlCmd.Parameters.AddWithValue("@locale", settings.Locale);

        // SQLite-Verbindung oeffnen
        await using var sqlite = new SqliteConnection($"Data Source={settings.SqliteOutputPath}");
        await sqlite.OpenAsync();

        await using var transaction = sqlite.BeginTransaction();

        var insertQuery = @"
            INSERT OR REPLACE INTO quest_offer_reward_locale (ID, locale, RewardText, VerifiedBuild)
            VALUES (@id, @locale, @reward_text, @verified_build)
        ";

        int count = 0;
        await using var reader = await mysqlCmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var id = reader.GetInt32("ID");
            var locale = reader.GetString("locale");
            var rewardText = GetStringOrNull(reader, "RewardText");
            var verifiedBuild = reader.IsDBNull(reader.GetOrdinal("VerifiedBuild")) ? 0 : reader.GetInt32("VerifiedBuild");

            if (string.IsNullOrWhiteSpace(rewardText))
                continue;

            await using var insertCmd = new SqliteCommand(insertQuery, sqlite, transaction);
            insertCmd.Parameters.AddWithValue("@id", id);
            insertCmd.Parameters.AddWithValue("@locale", locale);
            insertCmd.Parameters.AddWithValue("@reward_text", rewardText);
            insertCmd.Parameters.AddWithValue("@verified_build", verifiedBuild);

            await insertCmd.ExecuteNonQueryAsync();
            count++;

            if (count % 1000 == 0)
            {
                Console.WriteLine($"      {count} RewardTexts verarbeitet...");
            }
        }

        await transaction.CommitAsync();
        return count;
    }

    /// <summary>
    /// Sucht nach einer verfuegbaren Zonen-Tabelle in der Datenbank.
    /// </summary>
    static async Task<string?> FindZoneTableAsync(MySqlConnection mysql)
    {
        // Moegliche Tabellennamen fuer Zonen-Informationen in verschiedenen WoW-Emulatoren
        var possibleTables = new[]
        {
            ("areaortrigger_dbc", "AreaName_Lang"),      // AzerothCore DBC
            ("areatable_dbc", "AreaName_Lang"),          // Alternative DBC
            ("area_dbc", "name"),                        // Einfache Variante
            ("areas", "name"),                           // Generisch
        };

        foreach (var (tableName, columnName) in possibleTables)
        {
            try
            {
                var checkQuery = $"SELECT 1 FROM {tableName} LIMIT 1";
                await using var cmd = new MySqlCommand(checkQuery, mysql);
                await cmd.ExecuteScalarAsync();

                // Tabelle existiert - pruefe ob Spalte existiert
                var colCheckQuery = $"SELECT {columnName} FROM {tableName} LIMIT 1";
                await using var cmd2 = new MySqlCommand(colCheckQuery, mysql);
                await cmd2.ExecuteScalarAsync();

                return tableName;
            }
            catch
            {
                // Tabelle existiert nicht, naechste versuchen
            }
        }

        return null;
    }

    /// <summary>
    /// Exportiert Zonen-Informationen (optional, falls Tabelle existiert).
    /// </summary>
    static async Task<int> ExportZones(MySqlConnection mysql, ExporterSettings settings)
    {
        // Zonen aus den bereits exportierten Quests extrahieren
        await using var sqlite = new SqliteConnection($"Data Source={settings.SqliteOutputPath}");
        await sqlite.OpenAsync();

        // Eindeutige Zonen aus der Quest-Tabelle extrahieren und in Zonen-Tabelle einfuegen
        var extractQuery = @"
            INSERT OR REPLACE INTO zones (zone_id, zone_name, locale)
            SELECT DISTINCT zone_id, zone, @locale
            FROM quests
            WHERE zone IS NOT NULL AND zone != ''
        ";

        await using var cmd = new SqliteCommand(extractQuery, sqlite);
        cmd.Parameters.AddWithValue("@locale", settings.Locale);
        var count = await cmd.ExecuteNonQueryAsync();

        return count;
    }

    /// <summary>
    /// Exportiert Zonen-Informationen aus MySQL (veraltet, nicht mehr verwendet).
    /// </summary>
    static async Task<int> ExportZonesFromMySql_Legacy(MySqlConnection mysql, ExporterSettings settings)
    {
        string? zoneTableName = await FindZoneTableAsync(mysql);

        if (zoneTableName == null)
        {
            Console.WriteLine("      Warnung: Keine Zonen-Tabelle gefunden, ueberspringe Zonen-Export.");
            return 0;
        }

        var query = $@"
            SELECT DISTINCT
                ID as zone_id,
                name as zone_name
            FROM {zoneTableName}
            WHERE name IS NOT NULL AND name != ''
            ORDER BY ID
        ";

        await using var mysqlCmd = new MySqlCommand(query, mysql);

        // SQLite-Verbindung oeffnen
        await using var sqlite = new SqliteConnection($"Data Source={settings.SqliteOutputPath}");
        await sqlite.OpenAsync();

        await using var transaction = sqlite.BeginTransaction();

        var insertQuery = "INSERT OR REPLACE INTO zones (zone_id, zone_name, locale) VALUES (@zone_id, @zone_name, @locale)";

        int count = 0;
        try
        {
            await using var reader = await mysqlCmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var zoneId = reader.GetInt32("zone_id");
                var zoneName = GetStringOrNull(reader, "zone_name");

                if (string.IsNullOrEmpty(zoneName))
                    continue;

                await using var insertCmd = new SqliteCommand(insertQuery, sqlite, transaction);
                insertCmd.Parameters.AddWithValue("@zone_id", zoneId);
                insertCmd.Parameters.AddWithValue("@zone_name", zoneName);
                insertCmd.Parameters.AddWithValue("@locale", settings.Locale);

                await insertCmd.ExecuteNonQueryAsync();
                count++;
            }

            await transaction.CommitAsync();
        }
        catch (MySqlException ex)
        {
            Console.WriteLine($"      Warnung: Zonen-Export fehlgeschlagen: {ex.Message}");
            await transaction.RollbackAsync();
        }

        return count;
    }

    /// <summary>
    /// Liest einen String aus dem Reader oder gibt null zurueck.
    /// </summary>
    static string? GetStringOrNull(MySqlDataReader reader, string column)
    {
        var ordinal = reader.GetOrdinal(column);
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    /// <summary>
    /// Berechnet den Lokalisierungsstatus basierend auf vorhandenen Texten.
    /// </summary>
    static int CalculateLocalizationStatus(bool hasTitle, bool hasDesc, bool hasObj, bool hasCompletion)
    {
        // 0 = FullyGerman, 1 = MixedGermanEnglish, 2 = OnlyEnglish, 3 = Incomplete
        int count = (hasTitle ? 1 : 0) + (hasDesc ? 1 : 0) + (hasObj ? 1 : 0) + (hasCompletion ? 1 : 0);

        if (count == 4)
            return 0; // FullyGerman
        if (count == 0)
            return 2; // OnlyEnglish
        if (count >= 2)
            return 1; // MixedGermanEnglish
        return 3; // Incomplete
    }

    /// <summary>
    /// Bestimmt die Quest-Kategorie basierend auf Typ und Gruppengroesse.
    /// </summary>
    static int DetermineCategory(int questType, int partySize)
    {
        // Quest-Typen: 0=Normal, 1=Group, 21=Life, 41=PvP, 62=Raid, 81=Dungeon, 82=World Event
        return questType switch
        {
            1 => 3,   // Group
            41 => 8,  // PvP
            62 => 7,  // Raid
            81 => 6,  // Dungeon
            82 => 10, // Event
            _ when partySize >= 10 => 7, // Raid
            _ when partySize >= 2 => 3,  // Group
            _ => 2    // Side (default)
        };
    }
}
