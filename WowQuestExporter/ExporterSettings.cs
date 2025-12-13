namespace WowQuestExporter;

/// <summary>
/// Einstellungen fuer den Quest-Exporter.
/// </summary>
public class ExporterSettings
{
    // MySQL-Verbindungseinstellungen
    public string MySqlHost { get; set; } = "localhost";
    public int MySqlPort { get; set; } = 3306;
    public string MySqlDatabase { get; set; } = "acore_world";
    public string MySqlUser { get; set; } = "root";
    public string MySqlPassword { get; set; } = "sam2888.";

    // SQLite-Ausgabe
    public string SqliteOutputPath { get; set; } = "quests_deDE.db";

    // Export-Optionen
    public string Locale { get; set; } = "deDE";
    public int? MinQuestId { get; set; } = null;
    public int? MaxQuestId { get; set; } = null;

    /// <summary>
    /// Gibt den MySQL Connection String zurueck.
    /// </summary>
    public string GetMySqlConnectionString()
    {
        return $"Server={MySqlHost};Port={MySqlPort};Database={MySqlDatabase};User={MySqlUser};Password={MySqlPassword};";
    }

    /// <summary>
    /// Parst Kommandozeilenargumente.
    /// </summary>
    public static ExporterSettings ParseArgs(string[] args)
    {
        var settings = new ExporterSettings();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i].ToLower();

            switch (arg)
            {
                case "--host":
                case "-h":
                    if (i + 1 < args.Length)
                        settings.MySqlHost = args[++i];
                    break;

                case "--port":
                case "-p":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int port))
                        settings.MySqlPort = port;
                    break;

                case "--database":
                case "-d":
                    if (i + 1 < args.Length)
                        settings.MySqlDatabase = args[++i];
                    break;

                case "--user":
                case "-u":
                    if (i + 1 < args.Length)
                        settings.MySqlUser = args[++i];
                    break;

                case "--password":
                case "--pass":
                    if (i + 1 < args.Length)
                        settings.MySqlPassword = args[++i];
                    break;

                case "--output":
                case "-o":
                    if (i + 1 < args.Length)
                        settings.SqliteOutputPath = args[++i];
                    break;

                case "--locale":
                case "-l":
                    if (i + 1 < args.Length)
                        settings.Locale = args[++i];
                    break;

                case "--min-id":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int minId))
                        settings.MinQuestId = minId;
                    break;

                case "--max-id":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int maxId))
                        settings.MaxQuestId = maxId;
                    break;

                case "--help":
                    PrintHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        return settings;
    }

    /// <summary>
    /// Zeigt die Hilfe an.
    /// </summary>
    public static void PrintHelp()
    {
        Console.WriteLine(@"
WowQuestExporter - Exportiert WoW-Quests von MySQL nach SQLite

VERWENDUNG:
  WowQuestExporter [Optionen]

OPTIONEN:
  --host, -h       MySQL Host (root)
  --port, -p       MySQL Port (3306)
  --database, -d   MySQL Datenbank (wow_world)
  --user, -u       MySQL Benutzer (root)
  --password       MySQL Passwort (sam2888.)
  --output, -o     SQLite-Ausgabedatei (Standard: quests_deDE.db)
  --locale, -l     Sprache/Locale (Standard: deDE)
  --min-id         Minimale Quest-ID (optional)
  --max-id         Maximale Quest-ID (optional)
  --help           Zeigt diese Hilfe an

BEISPIELE:
  WowQuestExporter
  WowQuestExporter -o C:\WoW\quests.db
  WowQuestExporter --host 192.168.1.100 --user root --pass secret
  WowQuestExporter --min-id 1 --max-id 10000
");
    }
}
