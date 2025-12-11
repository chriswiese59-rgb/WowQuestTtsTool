// NuGet-Paket installieren:
// Projekt öffnen -> NuGet-Pakete verwalten -> nach "MySqlConnector" suchen -> installieren
// Oder via Package Manager Console: Install-Package MySqlConnector

using System.ComponentModel;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Konfiguration für die MySQL-Datenbankverbindung zur AzerothCore wow_world Datenbank.
    /// </summary>
    public class QuestDbConfig : INotifyPropertyChanged
    {
        private string _host = "127.0.0.1";
        private int _port = 3306;
        private string _database = "wow_world";
        private string _user = "root";
        private string _password = "";

        /// <summary>
        /// MySQL-Server Host (Standard: 127.0.0.1 für lokale Installation).
        /// </summary>
        public string Host
        {
            get => _host;
            set
            {
                if (_host != value)
                {
                    _host = value;
                    OnPropertyChanged(nameof(Host));
                }
            }
        }

        /// <summary>
        /// MySQL-Server Port (Standard: 3306).
        /// </summary>
        public int Port
        {
            get => _port;
            set
            {
                if (_port != value)
                {
                    _port = value;
                    OnPropertyChanged(nameof(Port));
                }
            }
        }

        /// <summary>
        /// Datenbankname (Standard: wow_world für AzerothCore).
        /// </summary>
        public string Database
        {
            get => _database;
            set
            {
                if (_database != value)
                {
                    _database = value;
                    OnPropertyChanged(nameof(Database));
                }
            }
        }

        /// <summary>
        /// MySQL-Benutzername (Standard: root).
        /// </summary>
        public string User
        {
            get => _user;
            set
            {
                if (_user != value)
                {
                    _user = value;
                    OnPropertyChanged(nameof(User));
                }
            }
        }

        /// <summary>
        /// MySQL-Passwort.
        /// </summary>
        public string Password
        {
            get => _password;
            set
            {
                if (_password != value)
                {
                    _password = value;
                    OnPropertyChanged(nameof(Password));
                }
            }
        }

        // Tabellennamen (anpassbar für andere DB-Strukturen)

        /// <summary>
        /// Tabellenname für quest_template (englische Basis-Daten).
        /// </summary>
        public string QuestTemplateTable { get; set; } = "quest_template";

        /// <summary>
        /// Tabellenname für quest_template_locale (lokalisierte Quest-Texte).
        /// </summary>
        public string QuestTemplateLocaleTable { get; set; } = "quest_template_locale";

        /// <summary>
        /// Tabellenname für quest_offer_reward_locale (NPC-Belohnungstext beim Abgeben).
        /// </summary>
        public string QuestOfferRewardLocaleTable { get; set; } = "quest_offer_reward_locale";

        /// <summary>
        /// Tabellenname für quest_request_items_locale (NPC-Anforderungstext).
        /// </summary>
        public string QuestRequestItemsLocaleTable { get; set; } = "quest_request_items_locale";

        /// <summary>
        /// Erstellt den MySQL Connection String.
        /// </summary>
        public string ConnectionString =>
            $"Server={Host};Port={Port};Database={Database};User={User};Password={Password};" +
            "SslMode=None;AllowPublicKeyRetrieval=True;CharacterSet=utf8mb4;";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
