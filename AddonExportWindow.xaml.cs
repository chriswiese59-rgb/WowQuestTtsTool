using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Interaktionslogik fuer AddonExportWindow.xaml
    /// </summary>
    public partial class AddonExportWindow : Window
    {
        private readonly TtsExportSettings _exportSettings;
        private readonly AddonSettings _addonSettings = new();
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isExporting = false;

        public AddonExportWindow(TtsExportSettings exportSettings)
        {
            InitializeComponent();
            _exportSettings = exportSettings ?? throw new ArgumentNullException(nameof(exportSettings));

            // Standard-Pfade setzen
            SourcePathBox.Text = _exportSettings.OutputRootPath ?? "";

            // Ziel-Pfad: Parallel zum Quell-Ordner
            if (!string.IsNullOrEmpty(_exportSettings.OutputRootPath))
            {
                var parentDir = Path.GetDirectoryName(_exportSettings.OutputRootPath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    TargetPathBox.Text = Path.Combine(parentDir, "QuestVoiceover_Addon");
                }
            }

            // Sprache aus Settings
            SelectLanguageInCombo(_exportSettings.LanguageCode);
        }

        private void SelectLanguageInCombo(string languageCode)
        {
            foreach (System.Windows.Controls.ComboBoxItem item in LanguageCombo.Items)
            {
                if (item.Tag?.ToString() == languageCode)
                {
                    LanguageCombo.SelectedItem = item;
                    return;
                }
            }
        }

        private void OnBrowseSourceClick(object sender, RoutedEventArgs e)
        {
            // WPF-kompatibler Ordner-Dialog via OpenFileDialog-Workaround
            var dialog = new OpenFileDialog
            {
                Title = "Audio-Quellordner waehlen (beliebige Datei im Ordner auswaehlen)",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Ordner auswaehlen"
            };

            if (!string.IsNullOrEmpty(SourcePathBox.Text) && Directory.Exists(SourcePathBox.Text))
            {
                dialog.InitialDirectory = SourcePathBox.Text;
            }

            if (dialog.ShowDialog() == true)
            {
                var selectedPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    SourcePathBox.Text = selectedPath;
                }
            }
        }

        private void OnBrowseTargetClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Addon-Zielordner waehlen (beliebige Datei im Ordner auswaehlen)",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Ordner auswaehlen"
            };

            if (!string.IsNullOrEmpty(TargetPathBox.Text))
            {
                var parent = Path.GetDirectoryName(TargetPathBox.Text);
                if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                {
                    dialog.InitialDirectory = parent;
                }
            }

            if (dialog.ShowDialog() == true)
            {
                var selectedPath = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    TargetPathBox.Text = selectedPath;
                }
            }
        }

        private void OnBrowseSnapshotClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Snapshot-Datei waehlen",
                Filter = "JSON Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                CompareSnapshotPathBox.Text = dialog.FileName;
            }
        }

        private void OnAddonSettingsClick(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new AddonSettingsWindow(_addonSettings)
            {
                Owner = this
            };

            settingsWindow.ShowDialog();
        }

        private async void OnExportClick(object sender, RoutedEventArgs e)
        {
            // Validierung
            if (string.IsNullOrWhiteSpace(SourcePathBox.Text) || !Directory.Exists(SourcePathBox.Text))
            {
                MessageBox.Show("Bitte waehle einen gueltigen Audio-Quellordner.", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetPathBox.Text))
            {
                MessageBox.Show("Bitte waehle einen Zielordner fuer das Addon.", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(AddonNameBox.Text))
            {
                MessageBox.Show("Bitte gib einen Addon-Namen ein.", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // UI aktualisieren
            _isExporting = true;
            ExportButton.IsEnabled = false;
            ExportButton.Content = "Exportiere...";
            LogTextBox.Clear();
            ExportProgressBar.Value = 0;
            ProgressText.Text = "0%";

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                var language = (LanguageCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag?.ToString() ?? "deDE";
                var useExtendedExport = UseExtendedExportCheckBox.IsChecked == true;

                // Export-Service erstellen
                var exportService = new AddonExportService
                {
                    AddonName = AddonNameBox.Text.Trim(),
                    AddonVersion = VersionBox.Text.Trim(),
                    UseExtendedExport = useExtendedExport,
                    Settings = useExtendedExport ? _addonSettings : null
                };

                Log($"Starte Export...");
                Log($"Quelle: {SourcePathBox.Text}");
                Log($"Ziel: {TargetPathBox.Text}");
                Log($"Addon: {exportService.AddonName} v{exportService.AddonVersion}");
                Log($"Sprache: {language}");
                Log($"Erweiterter Export: {(useExtendedExport ? "Ja (mit Ingame-Options)" : "Nein (einfach)")}");
                Log("");

                // Progress-Handler
                var progress = new Progress<AddonExportProgress>(p =>
                {
                    ExportProgressBar.Value = p.Percentage;
                    ProgressText.Text = $"{p.Percentage:F0}%";
                    Log(p.Message);
                });

                // Export durchfuehren
                var result = await exportService.ExportAddonAsync(
                    SourcePathBox.Text,
                    TargetPathBox.Text,
                    language,
                    progress,
                    _cancellationTokenSource.Token);

                // Ergebnis anzeigen
                Log("");
                if (result.Success)
                {
                    Log("=== EXPORT ERFOLGREICH ===");
                    Log($"Verarbeitete Dateien: {result.TotalFilesProcessed}");
                    Log($"Kopiert: {result.FilesCopied}");
                    Log($"Uebersprungen (bereits aktuell): {result.FilesSkipped}");
                    Log($"Dauer: {result.Duration.TotalSeconds:F1} Sekunden");
                    Log("");
                    Log($"Addon-Pfad: {result.AddonPath}");
                    Log("");
                    Log("Kopiere den Ordner nach:");
                    Log("WoW/_retail_/Interface/AddOns/");

                    if (result.FailedFiles.Count > 0)
                    {
                        Log("");
                        Log($"WARNUNG: {result.FailedFiles.Count} Dateien konnten nicht verarbeitet werden:");
                        foreach (var failed in result.FailedFiles)
                        {
                            Log($"  - {failed}");
                        }
                    }

                    // Snapshot erstellen falls gewuenscht
                    if (CreateSnapshotCheckBox.IsChecked == true)
                    {
                        await CreateSnapshotAsync(language);
                    }

                    MessageBox.Show(
                        $"Export erfolgreich!\n\n" +
                        $"Dateien: {result.TotalFilesProcessed}\n" +
                        $"Dauer: {result.Duration.TotalSeconds:F1}s\n\n" +
                        $"Kopiere den Ordner '{Path.GetFileName(result.AddonPath)}' in deinen WoW AddOns-Ordner.",
                        "Export abgeschlossen",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    Log("=== EXPORT FEHLGESCHLAGEN ===");
                    Log($"Fehler: {result.ErrorMessage}");

                    MessageBox.Show(
                        $"Export fehlgeschlagen:\n\n{result.ErrorMessage}",
                        "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                Log("");
                Log("=== EXPORT ABGEBROCHEN ===");
            }
            catch (Exception ex)
            {
                Log("");
                Log($"=== FEHLER ===");
                Log(ex.Message);

                MessageBox.Show($"Unerwarteter Fehler:\n\n{ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isExporting = false;
                ExportButton.IsEnabled = true;
                ExportButton.Content = "Export starten";
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        private async Task CreateSnapshotAsync(string language)
        {
            try
            {
                Log("");
                Log("Erstelle Quest-Snapshot fuer kuenftige Vergleiche...");

                // Quests aus Audio-Index extrahieren (vereinfacht)
                var audioIndex = AudioIndexWriter.LoadIndex(SourcePathBox.Text, language);

                if (audioIndex.TotalCount > 0)
                {
                    var snapshotPath = Path.Combine(
                        Path.GetDirectoryName(TargetPathBox.Text) ?? "",
                        $"quest_snapshot_{language}_{DateTime.Now:yyyyMMdd_HHmmss}.json");

                    // Hier wuerde man die echten Quest-Daten benoetigen
                    // Fuer jetzt speichern wir nur die IDs aus dem Index
                    var manifest = new AddonExportManifest
                    {
                        AddonName = AddonNameBox.Text,
                        Version = VersionBox.Text,
                        Language = language,
                        ExportedAtUtc = DateTime.UtcNow,
                        TotalQuests = audioIndex.Entries.Count,
                        QuestIds = []
                    };

                    foreach (var entry in audioIndex.Entries)
                    {
                        if (!manifest.QuestIds.Contains(entry.QuestId))
                        {
                            manifest.QuestIds.Add(entry.QuestId);
                        }
                    }

                    manifest.QuestIds.Sort();

                    var json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    await System.IO.File.WriteAllTextAsync(snapshotPath, json);

                    Log($"Snapshot gespeichert: {snapshotPath}");
                }
            }
            catch (Exception ex)
            {
                Log($"Snapshot konnte nicht erstellt werden: {ex.Message}");
            }
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            if (_isExporting)
            {
                var result = MessageBox.Show(
                    "Export wird ausgefuehrt. Wirklich abbrechen?",
                    "Abbrechen?",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _cancellationTokenSource?.Cancel();
                }
            }
            else
            {
                DialogResult = false;
                Close();
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                LogTextBox.AppendText(message + Environment.NewLine);
                LogTextBox.ScrollToEnd();
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isExporting)
            {
                var result = MessageBox.Show(
                    "Export wird noch ausgefuehrt. Wirklich schliessen?",
                    "Schliessen?",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                _cancellationTokenSource?.Cancel();
            }

            base.OnClosing(e);
        }
    }
}
