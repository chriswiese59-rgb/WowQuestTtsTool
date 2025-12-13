using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Dialog zur Auswahl eines Projekts und Optionen zum Laden.
    /// </summary>
    public partial class ProjectSelectionDialog : Window
    {
        /// <summary>
        /// Informationen ueber ein verfuegbares Projekt.
        /// </summary>
        public class ProjectInfo
        {
            public string Id { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Description { get; set; } = "";
            public string FilePath { get; set; } = "";
            public DateTime LastModified { get; set; }
            public bool IsBackup { get; set; }
        }

        /// <summary>
        /// Das ausgewaehlte Projekt.
        /// </summary>
        public ProjectInfo? SelectedProject { get; private set; }

        /// <summary>
        /// Ob nur vertonte Quests geladen werden sollen.
        /// </summary>
        public bool LoadOnlyVoiced => LoadOnlyVoicedCheckBox.IsChecked == true;

        /// <summary>
        /// Ob der Dialog mit OK bestaetigt wurde.
        /// </summary>
        public bool DialogConfirmed { get; private set; }

        private readonly List<ProjectInfo> _availableProjects = new();

        public ProjectSelectionDialog()
        {
            InitializeComponent();
            LoadAvailableProjects();
        }

        /// <summary>
        /// Laedt alle verfuegbaren Projekte (aktuelles + Backups).
        /// </summary>
        private void LoadAvailableProjects()
        {
            _availableProjects.Clear();

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dataDir = Path.Combine(baseDir, "data");
            var projectDir = Path.Combine(baseDir, "project");

            // 1. Aktueller Quest-Cache (Hauptprojekt)
            var questCachePath = Path.Combine(dataDir, "quests_cache.json");
            if (File.Exists(questCachePath))
            {
                var fileInfo = new FileInfo(questCachePath);
                _availableProjects.Add(new ProjectInfo
                {
                    Id = "current",
                    DisplayName = "Aktuelles Projekt",
                    Description = $"Zuletzt geaendert: {fileInfo.LastWriteTime:dd.MM.yyyy HH:mm}",
                    FilePath = questCachePath,
                    LastModified = fileInfo.LastWriteTime,
                    IsBackup = false
                });
            }

            // 2. Blizzard-Cache (falls vorhanden und anders als Quest-Cache)
            var blizzardCachePath = Path.Combine(dataDir, "blizzard_quests_cache.json");
            if (File.Exists(blizzardCachePath))
            {
                var fileInfo = new FileInfo(blizzardCachePath);
                _availableProjects.Add(new ProjectInfo
                {
                    Id = "blizzard",
                    DisplayName = "Blizzard Quest-Cache",
                    Description = $"Zuletzt geaendert: {fileInfo.LastWriteTime:dd.MM.yyyy HH:mm}",
                    FilePath = blizzardCachePath,
                    LastModified = fileInfo.LastWriteTime,
                    IsBackup = false
                });
            }

            // 3. Projekt-Backups
            var backupDir = Path.Combine(projectDir, "backups");
            if (Directory.Exists(backupDir))
            {
                var backupFiles = Directory.GetFiles(backupDir, "*.json")
                    .OrderByDescending(f => new FileInfo(f).LastWriteTime)
                    .Take(5); // Nur die letzten 5 Backups anzeigen

                foreach (var backupPath in backupFiles)
                {
                    var fileInfo = new FileInfo(backupPath);
                    var fileName = Path.GetFileNameWithoutExtension(backupPath);

                    _availableProjects.Add(new ProjectInfo
                    {
                        Id = $"backup_{fileName}",
                        DisplayName = $"Backup: {fileName}",
                        Description = $"Erstellt: {fileInfo.LastWriteTime:dd.MM.yyyy HH:mm}",
                        FilePath = backupPath,
                        LastModified = fileInfo.LastWriteTime,
                        IsBackup = true
                    });
                }
            }

            // Liste an UI binden
            ProjectListBox.ItemsSource = _availableProjects;

            // Erstes Element auswaehlen wenn vorhanden
            if (_availableProjects.Count > 0)
            {
                ProjectListBox.SelectedIndex = 0;
            }
        }

        private void ProjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            LoadButton.IsEnabled = ProjectListBox.SelectedItem != null;
        }

        private void OnLoadClick(object sender, RoutedEventArgs e)
        {
            SelectedProject = ProjectListBox.SelectedItem as ProjectInfo;
            DialogConfirmed = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogConfirmed = false;
            Close();
        }
    }
}
