using System;
using System.IO;
using System.Windows;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Startup-Dialog zur Projektauswahl.
    /// WICHTIG: Kein automatischer Fallback - Benutzer MUSS explizit waehlen.
    /// </summary>
    public partial class StartupDialog : Window
    {
        /// <summary>
        /// Ergebnis der Benutzerauswahl.
        /// </summary>
        public enum StartupChoice
        {
            Cancel,
            ContinueLastProject,
            LoadOtherProject,
            NewProject
        }

        /// <summary>
        /// Die vom Benutzer getroffene Auswahl.
        /// </summary>
        public StartupChoice UserChoice { get; private set; } = StartupChoice.Cancel;

        /// <summary>
        /// Pfad zum ausgewaehlten Projekt (bei LoadOtherProject).
        /// </summary>
        public string? SelectedProjectPath { get; private set; }

        /// <summary>
        /// Ob nur vertonte Quests geladen werden sollen.
        /// </summary>
        public bool LoadOnlyVoiced { get; private set; }

        /// <summary>
        /// Pfad zum letzten gueltigen Projekt.
        /// </summary>
        private string? _lastValidProjectPath;

        public StartupDialog()
        {
            InitializeComponent();
            this.Loaded += (s, e) => CheckProjectStatus();
        }

        /// <summary>
        /// Prueft den Projektstatus und konfiguriert die UI entsprechend.
        /// </summary>
        private void CheckProjectStatus()
        {
            try
            {
                // Letzten Projektpfad aus Settings laden
                var lastProjectPath = GetLastProjectPath();

                if (!string.IsNullOrEmpty(lastProjectPath) && IsValidProject(lastProjectPath))
                {
                    // Gueltiges letztes Projekt gefunden
                    _lastValidProjectPath = lastProjectPath;
                    ShowContinueOption(lastProjectPath);
                }
                else
                {
                    // Kein gueltiges letztes Projekt
                    HideContinueOption();

                    // Pruefen ob ueberhaupt Projekte existieren
                    if (!HasAnyValidProject())
                    {
                        ShowNoProjectHint();
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"StartupDialog: Fehler beim Pruefen des Projektstatus: {ex.Message}");
                HideContinueOption();
                ShowNoProjectHint();
            }
        }

        /// <summary>
        /// Gibt den Pfad zum letzten Projekt zurueck.
        /// </summary>
        private string? GetLastProjectPath()
        {
            // Aus TtsExportSettings laden
            var settings = TtsExportSettings.Instance;
            if (!string.IsNullOrEmpty(settings.OutputRootPath))
            {
                return settings.OutputRootPath;
            }

            // Fallback: Standard-Projektverzeichnis
            var defaultPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project");
            if (Directory.Exists(defaultPath))
            {
                return defaultPath;
            }

            return null;
        }

        /// <summary>
        /// Prueft ob ein Projektpfad gueltig ist (project.json existiert).
        /// </summary>
        private bool IsValidProject(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
                return false;

            // Pruefen ob project.json existiert
            var projectFile = Path.Combine(projectPath, "project.json");
            if (File.Exists(projectFile))
                return true;

            // Alternativ: Pruefen ob im uebergeordneten project-Ordner
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var standardProjectFile = Path.Combine(baseDir, "project", "project.json");
            if (File.Exists(standardProjectFile))
                return true;

            // Pruefen ob Quest-Cache existiert (als Fallback)
            var cacheFile = Path.Combine(baseDir, "data", "quests_cache.json");
            return File.Exists(cacheFile);
        }

        /// <summary>
        /// Prueft ob ueberhaupt ein gueltiges Projekt existiert.
        /// </summary>
        private bool HasAnyValidProject()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // Standard-Projektdatei pruefen
            var projectFile = Path.Combine(baseDir, "project", "project.json");
            if (File.Exists(projectFile))
                return true;

            // Quest-Cache pruefen
            var cacheFile = Path.Combine(baseDir, "data", "quests_cache.json");
            return File.Exists(cacheFile);
        }

        /// <summary>
        /// Zeigt die "Weiterarbeiten"-Option an.
        /// </summary>
        private void ShowContinueOption(string projectPath)
        {
            ContinueLastProjectButton.Visibility = Visibility.Visible;

            // Projektinfo anzeigen
            try
            {
                var projectFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "project", "project.json");
                if (File.Exists(projectFile))
                {
                    var fileInfo = new FileInfo(projectFile);
                    LastProjectInfo.Text = $"Zuletzt: {fileInfo.LastWriteTime:dd.MM.yyyy HH:mm}";
                }
                else
                {
                    var cacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "quests_cache.json");
                    if (File.Exists(cacheFile))
                    {
                        var fileInfo = new FileInfo(cacheFile);
                        LastProjectInfo.Text = $"Quest-Cache: {fileInfo.LastWriteTime:dd.MM.yyyy HH:mm}";
                    }
                    else
                    {
                        LastProjectInfo.Text = "Letztes Projekt fortsetzen";
                    }
                }

                // Statistiken falls verfuegbar
                var stats = ProjectService.Instance.GetStatistics();
                if (stats.TotalQuests > 0)
                {
                    LastProjectInfo.Text = $"{stats.TotalQuests} Quests - {LastProjectInfo.Text}";
                }
            }
            catch
            {
                LastProjectInfo.Text = "Letztes Projekt fortsetzen";
            }

            HeaderSubtitle.Text = "Willkommen zurueck!";
            NoProjectHint.Visibility = Visibility.Collapsed;
        }

        /// <summary>
        /// Versteckt die "Weiterarbeiten"-Option.
        /// </summary>
        private void HideContinueOption()
        {
            ContinueLastProjectButton.Visibility = Visibility.Collapsed;
            _lastValidProjectPath = null;
        }

        /// <summary>
        /// Zeigt den Hinweis dass kein Projekt gefunden wurde.
        /// </summary>
        private void ShowNoProjectHint()
        {
            NoProjectHint.Visibility = Visibility.Visible;
            HeaderSubtitle.Text = "Kein Projekt gefunden";
        }

        /// <summary>
        /// Letztes Projekt fortsetzen.
        /// </summary>
        private void OnContinueLastProjectClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_lastValidProjectPath))
            {
                MessageBox.Show(
                    "Das letzte Projekt ist nicht mehr gueltig.\nBitte waehlen Sie ein anderes Projekt.",
                    "Projekt ungueltig",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                HideContinueOption();
                return;
            }

            SelectedProjectPath = _lastValidProjectPath;
            UserChoice = StartupChoice.ContinueLastProject;
            Close();
        }

        /// <summary>
        /// Anderes Projekt laden.
        /// </summary>
        private void OnLoadOtherProjectClick(object sender, RoutedEventArgs e)
        {
            // Projektauswahl-Dialog oeffnen
            var projectDialog = new ProjectSelectionDialog();
            projectDialog.Owner = this;
            projectDialog.ShowDialog();

            if (projectDialog.DialogConfirmed && projectDialog.SelectedProject != null)
            {
                // Pruefen ob ausgewaehltes Projekt gueltig ist
                if (!IsValidProjectFile(projectDialog.SelectedProject.FilePath))
                {
                    MessageBox.Show(
                        "Das ausgewaehlte Projekt ist ungueltig oder beschaedigt.\n" +
                        "Es konnte keine gueltige project.json gefunden werden.",
                        "Projekt ungueltig",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                SelectedProjectPath = projectDialog.SelectedProject.FilePath;
                LoadOnlyVoiced = projectDialog.LoadOnlyVoiced;
                UserChoice = StartupChoice.LoadOtherProject;
                Close();
            }
            // Wenn abgebrochen, bleibt der StartupDialog offen
        }

        /// <summary>
        /// Prueft ob eine Projektdatei gueltig ist.
        /// </summary>
        private bool IsValidProjectFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return false;

            // Datei muss existieren
            if (!File.Exists(filePath))
                return false;

            // Bei JSON-Dateien: Pruefen ob lesbar
            try
            {
                var content = File.ReadAllText(filePath);
                return !string.IsNullOrWhiteSpace(content);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Neues Projekt erstellen.
        /// </summary>
        private void OnNewProjectClick(object sender, RoutedEventArgs e)
        {
            // Warnung wenn bestehendes Projekt vorhanden
            if (HasAnyValidProject())
            {
                var result = MessageBox.Show(
                    "Es existiert bereits ein Projekt.\n\n" +
                    "Wenn Sie ein neues Projekt erstellen, werden die bisherigen Fortschrittsdaten zurueckgesetzt.\n" +
                    "(Die Audio-Dateien bleiben erhalten.)\n\n" +
                    "Fortfahren?",
                    "Neues Projekt erstellen",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            UserChoice = StartupChoice.NewProject;
            Close();
        }

        /// <summary>
        /// Beenden.
        /// </summary>
        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            UserChoice = StartupChoice.Cancel;
            Close();
        }
    }
}
