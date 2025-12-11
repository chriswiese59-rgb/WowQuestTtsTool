using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using WowQuestTtsTool.Models;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Interaktionslogik für UpdateSyncWindow.xaml
    /// Fenster für Quest-Update-Erkennung und selektive TTS-Generierung.
    /// </summary>
    public partial class UpdateSyncWindow : Window
    {
        private readonly UpdateSyncService _updateSyncService;
        private readonly IEnumerable<Quest> _quests;
        private readonly string _languageCode;
        private readonly TtsExportSettings _ttsSettings;

        private UpdateScanResult? _currentScanResult;
        private CancellationTokenSource? _cancellationTokenSource;
        private ObservableCollection<QuestDiffEntry> _diffEntries = [];
        private List<QuestDiffEntry> _allDiffEntries = [];

        /// <summary>
        /// Delegate für TTS-Generierung (wird vom MainWindow gesetzt).
        /// Parameter: Quest, LanguageCode, CancellationToken
        /// Return: true wenn erfolgreich
        /// </summary>
        public Func<Quest, string, CancellationToken, Task<bool>>? GenerateTtsForQuestAsync { get; set; }

        /// <summary>
        /// Delegate für Addon-Export (wird vom MainWindow gesetzt).
        /// </summary>
        public Func<CancellationToken, Task<bool>>? ExportAddonAsync { get; set; }

        /// <summary>
        /// Erstellt ein neues UpdateSyncWindow.
        /// </summary>
        /// <param name="quests">Aktuelle Quest-Sammlung</param>
        /// <param name="ttsSettings">TTS-Einstellungen</param>
        public UpdateSyncWindow(IEnumerable<Quest> quests, TtsExportSettings ttsSettings)
        {
            InitializeComponent();

            _quests = quests ?? throw new ArgumentNullException(nameof(quests));
            _ttsSettings = ttsSettings ?? throw new ArgumentNullException(nameof(ttsSettings));
            _languageCode = ttsSettings.LanguageCode;

            // Snapshot-Ordner ermitteln
            var snapshotFolder = QuestSourceSnapshotRepository.GetDefaultSnapshotFolder(ttsSettings.OutputRootPath);

            // Service initialisieren
            _updateSyncService = new UpdateSyncService(
                snapshotFolder,
                ttsSettings.OutputRootPath,
                _languageCode);

            // DataGrid binden
            DiffDataGrid.ItemsSource = _diffEntries;

            // UI initialisieren
            LoadMetadataToUI();
        }

        /// <summary>
        /// Lädt die Metadaten in die UI.
        /// </summary>
        private void LoadMetadataToUI()
        {
            var metadata = _updateSyncService.LoadMetadata();
            var snapshot = _updateSyncService.LoadLastSnapshot();

            // Status-Felder aktualisieren
            if (snapshot != null)
            {
                LastSnapshotText.Text = $"{snapshot.DataVersion} ({snapshot.QuestCount} Quests)";
            }
            else
            {
                LastSnapshotText.Text = "Kein Snapshot vorhanden";
            }

            LastSyncText.Text = metadata.LastSyncDisplay;
            TotalVoicedText.Text = metadata.TotalQuestsVoiced.ToString();
            LanguageText.Text = _languageCode;

            if (!string.IsNullOrEmpty(metadata.LastWowBuild))
            {
                WowBuildTextBox.Text = metadata.LastWowBuild;
            }

            if (!string.IsNullOrEmpty(metadata.AudioPackVersion))
            {
                AudioPackVersionTextBox.Text = metadata.AudioPackVersion;
            }
        }

        /// <summary>
        /// Handler für "Questdaten scannen" Button.
        /// </summary>
        private async void OnScanQuestsClick(object sender, RoutedEventArgs e)
        {
            if (_quests == null || !_quests.Any())
            {
                MessageBox.Show("Keine Quests geladen. Bitte lade zuerst Quests im Hauptfenster.",
                    "Keine Quests", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ScanButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                Log("Starte Quest-Scan...");
                ClearStatistics();

                var progress = new Progress<UpdateSyncProgress>(p =>
                {
                    ProgressBar.Value = p.Percentage;
                    ProgressText.Text = $"{p.Percentage:F0}%";
                    Log($"[{p.Phase}] {p.Message}");
                });

                _currentScanResult = await _updateSyncService.ScanWithQuestsAsync(
                    _quests, progress, _cancellationTokenSource.Token);

                if (_currentScanResult.Success)
                {
                    // Statistiken aktualisieren
                    var diff = _currentScanResult.Diff;
                    NewCountText.Text = diff.NewCount.ToString();
                    ChangedCountText.Text = diff.ChangedCount.ToString();
                    RemovedCountText.Text = diff.RemovedCount.ToString();
                    UnchangedCountText.Text = diff.UnchangedCount.ToString();

                    // Alle Einträge speichern und gefiltert anzeigen
                    _allDiffEntries = diff.AllEntries.ToList();
                    UpdateZoneFilter();
                    ApplyFilters();

                    Log($"Scan abgeschlossen: {diff.Summary}");
                    Log($"Dauer: {_currentScanResult.Duration.TotalSeconds:F1} Sekunden");

                    // Apply-Button aktivieren wenn es etwas zu vertonen gibt
                    ApplyButton.IsEnabled = diff.ToVoiceCount > 0;

                    if (diff.ToVoiceCount > 0)
                    {
                        Log($"{diff.ToVoiceCount} Quests zum Vertonen bereit.");
                    }
                    else
                    {
                        Log("Keine neuen oder geaenderten Quests gefunden.");
                    }
                }
                else
                {
                    Log($"FEHLER: {_currentScanResult.ErrorMessage}");
                    MessageBox.Show(_currentScanResult.ErrorMessage, "Scan-Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Log("Scan abgebrochen.");
            }
            catch (Exception ex)
            {
                Log($"FEHLER: {ex.Message}");
                MessageBox.Show($"Scan fehlgeschlagen:\n\n{ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ScanButton.IsEnabled = true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// Handler für "Neue & geänderte Quests vertonen" Button.
        /// </summary>
        private async void OnApplyTtsClick(object sender, RoutedEventArgs e)
        {
            if (_currentScanResult == null || !_currentScanResult.Success)
            {
                MessageBox.Show("Bitte führe zuerst einen Scan durch.",
                    "Kein Scan", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (GenerateTtsForQuestAsync == null)
            {
                MessageBox.Show("TTS-Generierung nicht verfügbar. Bitte konfiguriere zuerst die TTS-Einstellungen.",
                    "TTS nicht verfügbar", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var toVoice = _currentScanResult.Diff.ToVoiceCount;
            var result = MessageBox.Show(
                $"Möchtest du {toVoice} Quest(s) vertonen?\n\n" +
                $"- Neue Quests: {_currentScanResult.Diff.NewCount}\n" +
                $"- Geänderte Quests: {_currentScanResult.Diff.ChangedCount}\n\n" +
                (AutoExportCheckBox.IsChecked == true ? "Das Addon wird anschließend automatisch exportiert." : ""),
                "TTS-Generierung starten?",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            ScanButton.IsEnabled = false;
            ApplyButton.IsEnabled = false;
            _cancellationTokenSource = new CancellationTokenSource();

            // Service-Delegates setzen
            _updateSyncService.GenerateTtsForQuestAsync = GenerateTtsForQuestAsync;
            _updateSyncService.ExportAddonAsync = ExportAddonAsync;

            try
            {
                Log("");
                Log("=== STARTE TTS-GENERIERUNG ===");

                var progress = new Progress<UpdateSyncProgress>(p =>
                {
                    ProgressBar.Value = p.Percentage;
                    ProgressText.Text = $"{p.Percentage:F0}%";
                    Log($"[{p.Phase}] {p.Message}");
                });

                var applyResult = await _updateSyncService.ApplyTtsForDiffAsync(
                    _currentScanResult,
                    onlyNewAndChanged: true,
                    autoExportAddon: AutoExportCheckBox.IsChecked == true,
                    progress,
                    _cancellationTokenSource.Token);

                Log("");
                if (applyResult.Success)
                {
                    Log("=== TTS-GENERIERUNG ABGESCHLOSSEN ===");
                    Log(applyResult.Summary);

                    if (applyResult.SnapshotSaved)
                    {
                        Log($"Snapshot gespeichert: {applyResult.SavedDataVersion}");
                    }

                    if (applyResult.AddonExported)
                    {
                        Log("Addon wurde erfolgreich exportiert.");
                    }

                    if (applyResult.FailedQuests.Count > 0)
                    {
                        Log("");
                        Log($"WARNUNGEN: {applyResult.FailedQuests.Count} Quests fehlgeschlagen:");
                        foreach (var (questId, error) in applyResult.FailedQuests.Take(10))
                        {
                            Log($"  - Quest {questId}: {error}");
                        }
                        if (applyResult.FailedQuests.Count > 10)
                        {
                            Log($"  ... und {applyResult.FailedQuests.Count - 10} weitere");
                        }
                    }

                    Log($"Dauer: {applyResult.Duration.TotalSeconds:F1} Sekunden");

                    // Metadaten neu laden
                    LoadMetadataToUI();

                    MessageBox.Show(
                        $"TTS-Generierung abgeschlossen!\n\n{applyResult.Summary}\n\n" +
                        $"Dauer: {applyResult.Duration.TotalMinutes:F1} Minuten",
                        "Fertig", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log($"FEHLER: {applyResult.ErrorMessage}");
                    MessageBox.Show(applyResult.ErrorMessage, "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Log("TTS-Generierung abgebrochen.");
            }
            catch (Exception ex)
            {
                Log($"FEHLER: {ex.Message}");
                MessageBox.Show($"TTS-Generierung fehlgeschlagen:\n\n{ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ScanButton.IsEnabled = true;
                ApplyButton.IsEnabled = _currentScanResult?.Diff.ToVoiceCount > 0;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// Handler für "Initial-Snapshot erstellen" Button.
        /// </summary>
        private void OnCreateInitialSnapshotClick(object sender, RoutedEventArgs e)
        {
            if (_quests == null || !_quests.Any())
            {
                MessageBox.Show("Keine Quests geladen. Bitte lade zuerst Quests im Hauptfenster.",
                    "Keine Quests", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show(
                $"Möchtest du einen Initial-Snapshot mit {_quests.Count()} Quests erstellen?\n\n" +
                "Dies speichert den aktuellen Stand als Referenz für zukünftige Updates.\n" +
                "Es werden KEINE TTS-Dateien generiert.",
                "Initial-Snapshot erstellen?",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                var wowBuild = string.IsNullOrWhiteSpace(WowBuildTextBox.Text) ? null : WowBuildTextBox.Text.Trim();
                _updateSyncService.CreateInitialSnapshot(_quests, wowBuild);

                Log($"Initial-Snapshot erstellt ({_quests.Count()} Quests)");
                LoadMetadataToUI();

                MessageBox.Show($"Initial-Snapshot erfolgreich erstellt!\n\n{_quests.Count()} Quests gespeichert.",
                    "Snapshot erstellt", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"FEHLER beim Erstellen des Snapshots: {ex.Message}");
                MessageBox.Show($"Snapshot konnte nicht erstellt werden:\n\n{ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Handler für Filter-Checkboxen.
        /// </summary>
        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            ApplyFilters();
        }

        /// <summary>
        /// Handler für Zone-Filter ComboBox.
        /// </summary>
        private void OnZoneFilterChanged(object sender, SelectionChangedEventArgs e)
        {
            ApplyFilters();
        }

        /// <summary>
        /// Wendet die aktuellen Filter auf die Diff-Liste an.
        /// </summary>
        private void ApplyFilters()
        {
            if (_allDiffEntries == null || _allDiffEntries.Count == 0)
            {
                _diffEntries.Clear();
                return;
            }

            var showNew = ShowNewCheckBox.IsChecked == true;
            var showChanged = ShowChangedCheckBox.IsChecked == true;
            var showRemoved = ShowRemovedCheckBox.IsChecked == true;
            var showUnchanged = ShowUnchangedCheckBox.IsChecked == true;

            string? selectedZone = null;
            if (ZoneFilterCombo.SelectedIndex > 0 && ZoneFilterCombo.SelectedItem is ComboBoxItem item)
            {
                selectedZone = item.Content?.ToString();
            }

            var filtered = _allDiffEntries.Where(entry =>
            {
                // DiffType Filter
                var typeMatch = entry.DiffType switch
                {
                    QuestDiffType.New => showNew,
                    QuestDiffType.Changed => showChanged,
                    QuestDiffType.Removed => showRemoved,
                    QuestDiffType.Unchanged => showUnchanged,
                    _ => true
                };

                if (!typeMatch) return false;

                // Zone Filter
                if (!string.IsNullOrEmpty(selectedZone) &&
                    !string.Equals(entry.Zone, selectedZone, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                return true;
            }).ToList();

            _diffEntries.Clear();
            foreach (var entry in filtered)
            {
                _diffEntries.Add(entry);
            }
        }

        /// <summary>
        /// Aktualisiert den Zone-Filter mit allen verfügbaren Zonen.
        /// </summary>
        private void UpdateZoneFilter()
        {
            var zones = _allDiffEntries
                .Select(e => e.Zone)
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(z => z)
                .ToList();

            ZoneFilterCombo.Items.Clear();
            ZoneFilterCombo.Items.Add(new ComboBoxItem { Content = "Alle Zonen", IsSelected = true });

            foreach (var zone in zones)
            {
                ZoneFilterCombo.Items.Add(new ComboBoxItem { Content = zone });
            }
        }

        /// <summary>
        /// Setzt die Statistik-Anzeigen zurück.
        /// </summary>
        private void ClearStatistics()
        {
            NewCountText.Text = "0";
            ChangedCountText.Text = "0";
            RemovedCountText.Text = "0";
            UnchangedCountText.Text = "0";
            _diffEntries.Clear();
            _allDiffEntries.Clear();
            ProgressBar.Value = 0;
            ProgressText.Text = "";
        }

        /// <summary>
        /// Schreibt eine Log-Nachricht.
        /// </summary>
        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                if (!string.IsNullOrEmpty(LogTextBox.Text))
                {
                    LogTextBox.AppendText(Environment.NewLine);
                }
                LogTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}");
                LogTextBox.ScrollToEnd();
            });
        }

        /// <summary>
        /// Handler für "Schließen" Button.
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Handler für Window-Schließen.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _cancellationTokenSource?.Cancel();
            base.OnClosing(e);
        }
    }
}
