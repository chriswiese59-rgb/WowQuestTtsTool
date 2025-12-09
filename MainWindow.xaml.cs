using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Microsoft.Win32;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private Quest? _selectedQuest;
        private string _searchText = string.Empty;
        private string _zoneFilter = "";
        private QuestCategory? _categoryFilter = null;
        private bool? _groupQuestFilter = null;
        private bool? _ttsStatusFilter = null; // null = alle, true = mit TTS, false = ohne TTS
        private ElevenLabsService? _elevenLabsService;
        private TtsConfigService _configService;
        private string? _currentAudioPath;
        private string _questsCachePath;

        // TTS Export Service
        private ITtsService _ttsService;
        private readonly TtsExportSettings _exportSettings;

        // Batch-TTS Abbruch
        private CancellationTokenSource? _batchCancellation;

        // Session-Tracker für Kosten
        private long _sessionCharCount;
        private long _sessionTokenEstimate;
        private decimal _sessionCostEstimate;

        public long SessionCharCount
        {
            get => _sessionCharCount;
            set
            {
                if (_sessionCharCount != value)
                {
                    _sessionCharCount = value;
                    OnPropertyChanged(nameof(SessionCharCount));
                    OnPropertyChanged(nameof(SessionTrackerText));
                }
            }
        }

        public long SessionTokenEstimate
        {
            get => _sessionTokenEstimate;
            set
            {
                if (_sessionTokenEstimate != value)
                {
                    _sessionTokenEstimate = value;
                    OnPropertyChanged(nameof(SessionTokenEstimate));
                    OnPropertyChanged(nameof(SessionTrackerText));
                }
            }
        }

        public decimal SessionCostEstimate
        {
            get => _sessionCostEstimate;
            set
            {
                if (_sessionCostEstimate != value)
                {
                    _sessionCostEstimate = value;
                    OnPropertyChanged(nameof(SessionCostEstimate));
                    OnPropertyChanged(nameof(SessionTrackerText));
                }
            }
        }

        public string SessionTrackerText =>
            $"Session: {SessionCharCount:N0} Zeichen, ~{SessionTokenEstimate:N0} Tokens, ~{SessionCostEstimate:N2} {_exportSettings?.CurrencySymbol ?? "€"}";

        public ObservableCollection<Quest> Quests { get; } = new();
        public ObservableCollection<Quest> FilteredQuests { get; } = new();
        public ICollectionView? QuestsView { get; private set; }

        public Quest? SelectedQuest
        {
            get => _selectedQuest;
            set
            {
                if (_selectedQuest != value)
                {
                    _selectedQuest = value;
                    OnPropertyChanged(nameof(SelectedQuest));
                    CheckExistingAudio();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _questsCachePath = Path.Combine(baseDir, "data", "quests_cache.json");

            _configService = new TtsConfigService();

            // TTS Export initialisieren
            _exportSettings = TtsExportSettings.Instance;

            // Dummy-Service als Fallback (wird in InitializeElevenLabs überschrieben)
            _ttsService = new DummyTtsService();

            InitializeElevenLabs();
            InitializeTtsExportUI();

            // CollectionView für Sortierung
            QuestsView = CollectionViewSource.GetDefaultView(FilteredQuests);

            // Beim Schließen Quests speichern
            Closing += MainWindow_Closing;

            try
            {
                // Versuche zuerst den Cache zu laden
                if (File.Exists(_questsCachePath))
                {
                    LoadQuestsFromPath(_questsCachePath);
                    StatusText.Text = $"Wiederhergestellt: {Quests.Count} Quests aus Cache";
                }
                else
                {
                    LoadQuestsFromJson();
                }

                // Sortierung und Zonen-Filter anwenden
                ApplyDefaultQuestSorting();
                PopulateZoneFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Laden der Quests:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void InitializeTtsExportUI()
        {
            // Output-Pfad anzeigen
            OutputPathTextBox.Text = _exportSettings.OutputRootPath;

            // Sprache auswählen
            foreach (ComboBoxItem item in LanguageCodeComboBox.Items)
            {
                if (item.Tag?.ToString() == _exportSettings.LanguageCode)
                {
                    LanguageCodeComboBox.SelectedItem = item;
                    break;
                }
            }

            // TTS Provider anzeigen
            TtsProviderText.Text = $"TTS: {_ttsService.ProviderName}";
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Quests automatisch speichern beim Schließen
            if (Quests.Count > 0)
            {
                SaveQuestsToCache();
            }

            // Export-Settings speichern
            _exportSettings.Save();
        }

        private void SaveQuestsToCache()
        {
            try
            {
                var directory = Path.GetDirectoryName(_questsCachePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(Quests.ToArray(), options);
                File.WriteAllText(_questsCachePath, json);
            }
            catch
            {
                // Stilles Fehlschlagen beim Cache-Speichern
            }
        }

        private void InitializeElevenLabs()
        {
            try
            {
                var config = _configService.Config;
                _elevenLabsService = new ElevenLabsService(config.ElevenLabs.ApiKey);

                if (_elevenLabsService.IsConfigured)
                {
                    // ElevenLabs ist konfiguriert - echten TTS-Service verwenden
                    _ttsService = new ElevenLabsTtsService(_elevenLabsService, _configService);
                    _exportSettings.TtsProvider = "ElevenLabs";

                    ApiStatusText.Text = "API: Konfiguriert";
                    ApiStatusText.Foreground = new SolidColorBrush(Colors.Green);
                }
                else
                {
                    // Fallback auf Dummy-Service
                    _ttsService = new DummyTtsService();
                    _exportSettings.TtsProvider = "Dummy";

                    ApiStatusText.Text = "API: Key fehlt";
                    ApiStatusText.Foreground = new SolidColorBrush(Colors.Orange);
                }
            }
            catch (Exception ex)
            {
                // Bei Fehler: Dummy-Service als Fallback
                _ttsService = new DummyTtsService();
                _exportSettings.TtsProvider = "Dummy";

                ApiStatusText.Text = $"API: Fehler - {ex.Message}";
                ApiStatusText.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        #region Quest Loading

        private void LoadQuestsFromJson()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var dataPath = Path.Combine(baseDir, "data", "quests_deDE.json");

            if (!File.Exists(dataPath))
            {
                StatusText.Text = "Keine Quest-Datei gefunden. Bitte 'Von Blizzard laden' oder 'JSON laden' verwenden.";
                return;
            }

            LoadQuestsFromPath(dataPath);
        }

        private void LoadQuestsFromPath(string path)
        {
            var json = File.ReadAllText(path);
            var quests = JsonSerializer.Deserialize<Quest[]>(json);

            Quests.Clear();
            FilteredQuests.Clear();

            if (quests != null)
            {
                // Sortiert nach Zone und QuestId hinzufügen
                var sortedQuests = quests.OrderBy(q => q.Zone ?? "").ThenBy(q => q.QuestId);
                foreach (var q in sortedQuests)
                {
                    Quests.Add(q);
                    FilteredQuests.Add(q);
                }

                // Kategorien aus Feldern ableiten
                UpdateAllQuestsCategories();

                // TTS-Flags basierend auf vorhandenen Dateien aktualisieren
                UpdateAllQuestsTtsFlags();

                // Filter-ComboBoxen befüllen
                PopulateZoneFilter();
                PopulateCategoryFilter();

                if (FilteredQuests.Count > 0)
                    SelectedQuest = FilteredQuests[0];
            }

            StatusText.Text = $"Geladen: {Quests.Count} Quests aus {Path.GetFileName(path)}";
        }

        private void OnImportJsonClick(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON-Dateien (*.json)|*.json|Alle Dateien (*.*)|*.*",
                Title = "Quest-JSON importieren"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    LoadQuestsFromPath(dialog.FileName);
                    ApplyDefaultQuestSorting();
                    PopulateZoneFilter();
                    SaveQuestsToCache();
                    MessageBox.Show(
                        $"Erfolgreich geladen:\n{Quests.Count} Quests",
                        "Import",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Fehler beim Laden:\n{ex.Message}",
                        "Fehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private async void OnFetchFromBlizzardClick(object sender, RoutedEventArgs e)
        {
            var config = _configService.Config.Blizzard;

            if (string.IsNullOrWhiteSpace(config.ClientId) || string.IsNullOrWhiteSpace(config.ClientSecret) ||
                config.ClientId == "YOUR_BLIZZARD_CLIENT_ID")
            {
                MessageBox.Show(
                    "Blizzard API nicht konfiguriert!\n\n" +
                    "Klicke auf 'Einstellungen' und wechsle zum Tab 'Blizzard API'.",
                    "Blizzard API",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                FetchFromBlizzardButton.IsEnabled = false;
                StatusText.Text = "Verbinde mit Blizzard API...";

                var httpClient = new HttpClient();
                var blizzardService = new BlizzardQuestService(
                    httpClient,
                    config.ClientId,
                    config.ClientSecret,
                    config.Region);

                var progress = new Progress<string>(msg =>
                {
                    Dispatcher.Invoke(() => StatusText.Text = msg);
                });

                var quests = await blizzardService.FetchQuestsAsync(
                    maxQuests: config.MaxQuests,
                    progress: progress);

                Quests.Clear();
                FilteredQuests.Clear();

                // Sortiert hinzufügen
                var sortedQuests = quests.OrderBy(q => q.Zone ?? "").ThenBy(q => q.QuestId);
                foreach (var q in sortedQuests)
                {
                    Quests.Add(q);
                    FilteredQuests.Add(q);
                }

                if (Quests.Count > 0)
                    SelectedQuest = Quests[0];

                ApplyDefaultQuestSorting();
                PopulateZoneFilter();
                SaveQuestsToCache();

                StatusText.Text = $"Fertig: {Quests.Count} Quests von Blizzard geladen.";

                MessageBox.Show(
                    $"Erfolgreich geladen:\n{Quests.Count} Quests von der Blizzard API",
                    "Blizzard API",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Fehler beim Laden.";
                MessageBox.Show(
                    $"Fehler beim Laden von Blizzard:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                FetchFromBlizzardButton.IsEnabled = true;
            }
        }

        #endregion

        #region TTS Export

        private void OnSelectOutputFolderClick(object sender, RoutedEventArgs e)
        {
            // WPF hat keinen nativen FolderBrowserDialog, daher nutzen wir einen Workaround
            var dialog = new OpenFileDialog
            {
                Title = "Ausgabeordner wählen (beliebige Datei im Zielordner auswählen)",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Ordner auswählen"
            };

            // Alternative: Microsoft.WindowsAPICodePack verwenden
            // Hier eine einfache Lösung mit OpenFileDialog
            if (dialog.ShowDialog() == true)
            {
                var folder = Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(folder))
                {
                    _exportSettings.OutputRootPath = folder;
                    OutputPathTextBox.Text = folder;
                    _exportSettings.Save();
                    StatusText.Text = $"Ausgabeordner gesetzt: {folder}";
                }
            }
        }

        private void LanguageCodeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: _exportSettings ist noch nicht initialisiert während InitializeComponent()
            if (_exportSettings == null) return;

            if (LanguageCodeComboBox.SelectedItem is ComboBoxItem item && item.Tag != null)
            {
                _exportSettings.LanguageCode = item.Tag.ToString() ?? "deDE";
                _exportSettings.Save();
            }
        }

        private async void OnGenerateTtsClick(object sender, RoutedEventArgs e)
        {
            // Validierung
            if (SelectedQuest == null)
            {
                MessageBox.Show("Keine Quest ausgewählt.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ausgabeordner wählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Prüfen ob TTS-Service konfiguriert ist
            if (!_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    $"TTS-Service '{_ttsService.ProviderName}' ist nicht konfiguriert.\n\n" +
                    "Bitte ElevenLabs API-Key in den Einstellungen hinterlegen.",
                    "TTS nicht verfügbar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Text bestimmen (mit oder ohne Titel)
            var includeTitle = IncludeTitleCheckBox.IsChecked == true;
            var text = includeTitle
                ? SelectedQuest.TtsText
                : (SelectedQuest.Description ?? string.Empty);

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Kein Text für TTS vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Voice-Profil aus ComboBox holen
            var voiceProfile = (VoiceProfileComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "neutral_male";

            // Pfad bestimmen
            var fullPath = _exportSettings.GetOutputPath(SelectedQuest);
            var targetFolder = Path.GetDirectoryName(fullPath);

            try
            {
                GenerateDualTtsButton.IsEnabled = false;
                TtsPreviewButton.IsEnabled = false;
                StatusText.Text = $"Generiere TTS für Quest {SelectedQuest.QuestId} mit {_ttsService.ProviderName}...";

                // Ordner erstellen
                if (!string.IsNullOrEmpty(targetFolder) && !Directory.Exists(targetFolder))
                {
                    Directory.CreateDirectory(targetFolder);
                }

                // TTS generieren (mit Voice-Profil)
                var audioBytes = await _ttsService.GenerateMp3Async(
                    text,
                    _exportSettings.LanguageCode,
                    voiceProfile);

                // Datei schreiben
                await File.WriteAllBytesAsync(fullPath, audioBytes);

                // Quest aktualisieren
                SelectedQuest.HasTtsAudio = true;
                SelectedQuest.TtsAudioPath = fullPath;

                // UI aktualisieren
                QuestDataGrid.Items.Refresh();
                _currentAudioPath = fullPath;
                AudioStatusText.Text = "TTS generiert - bereit zum Abspielen";
                AudioStatusText.Foreground = new SolidColorBrush(Colors.Green);
                EnableAudioControls(true);

                // Audio laden und abspielen
                AudioPlayer.Source = new Uri(fullPath);

                StatusText.Text = $"TTS für Quest {SelectedQuest.QuestId} gespeichert: {fullPath}";

                // Cache speichern
                SaveQuestsToCache();
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Fehler: {ex.Message}";
                MessageBox.Show(
                    $"Fehler bei der TTS-Generierung:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                GenerateDualTtsButton.IsEnabled = true;
                TtsPreviewButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Batch-TTS: Generiert TTS für alle gefilterten Quests (Legacy-Methode, noch vorhanden für Kompatibilität).
        /// </summary>
        private async void OnBatchTtsClick(object sender, RoutedEventArgs e)
        {
            // Validierung
            if (FilteredQuests.Count == 0)
            {
                MessageBox.Show("Keine Quests zum Verarbeiten vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ausgabeordner wählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    $"TTS-Service '{_ttsService.ProviderName}' ist nicht konfiguriert.\n\n" +
                    "Bitte ElevenLabs API-Key in den Einstellungen hinterlegen.",
                    "TTS nicht verfügbar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Quests ohne Audio filtern (optional: alle oder nur fehlende)
            var questsToProcess = FilteredQuests.Where(q => !q.HasTtsAudio).ToList();

            if (questsToProcess.Count == 0)
            {
                var result = MessageBox.Show(
                    "Alle gefilterten Quests haben bereits Audio.\n\n" +
                    "Möchtest du die Audio-Dateien neu generieren?",
                    "Alle Quests haben Audio",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    questsToProcess = FilteredQuests.ToList();
                }
                else
                {
                    return;
                }
            }

            // Bestätigung einholen
            var confirmResult = MessageBox.Show(
                $"Es werden {questsToProcess.Count} Quests verarbeitet.\n\n" +
                $"Provider: {_ttsService.ProviderName}\n" +
                $"Sprache: {_exportSettings.LanguageCode}\n" +
                $"Ausgabeordner: {_exportSettings.OutputRootPath}\n\n" +
                "Fortfahren?",
                "Batch-TTS starten",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes)
            {
                return;
            }

            // UI vorbereiten
            _batchCancellation = new CancellationTokenSource();
            SetBatchModeUI(true);

            var voiceProfile = (VoiceProfileComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "neutral_male";
            var includeTitle = IncludeTitleCheckBox.IsChecked == true;

            int processed = 0;
            int successful = 0;
            int failed = 0;
            var errors = new List<string>();

            try
            {
                for (int i = 0; i < questsToProcess.Count; i++)
                {
                    // Abbruch prüfen
                    if (_batchCancellation.Token.IsCancellationRequested)
                    {
                        StatusText.Text = "Batch-Verarbeitung abgebrochen.";
                        break;
                    }

                    var quest = questsToProcess[i];

                    // Progress aktualisieren
                    UpdateProgress(i, questsToProcess.Count, $"Quest {quest.QuestId}: {quest.Title}");

                    // Quest in der Liste auswählen (visuelles Feedback)
                    SelectedQuest = quest;
                    QuestDataGrid.ScrollIntoView(quest);

                    try
                    {
                        // Text bestimmen
                        var text = includeTitle
                            ? quest.TtsText
                            : (quest.Description ?? string.Empty);

                        if (string.IsNullOrWhiteSpace(text))
                        {
                            errors.Add($"Quest {quest.QuestId}: Kein Text vorhanden");
                            failed++;
                            continue;
                        }

                        // Pfad bestimmen
                        var fullPath = _exportSettings.GetOutputPath(quest);
                        var targetFolder = Path.GetDirectoryName(fullPath);

                        // Ordner erstellen
                        if (!string.IsNullOrEmpty(targetFolder) && !Directory.Exists(targetFolder))
                        {
                            Directory.CreateDirectory(targetFolder);
                        }

                        // TTS generieren
                        var audioBytes = await _ttsService.GenerateMp3Async(
                            text,
                            _exportSettings.LanguageCode,
                            voiceProfile);

                        // Datei schreiben
                        await File.WriteAllBytesAsync(fullPath, audioBytes, _batchCancellation.Token);

                        // Quest aktualisieren
                        quest.HasTtsAudio = true;
                        quest.TtsAudioPath = fullPath;

                        successful++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Quest {quest.QuestId}: {ex.Message}");
                        failed++;
                    }

                    processed++;

                    // UI aktualisieren (DataGrid)
                    QuestDataGrid.Items.Refresh();

                    // Kleine Pause zwischen API-Calls (Rate-Limiting vermeiden)
                    await Task.Delay(100, _batchCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Batch-Verarbeitung abgebrochen.";
            }
            finally
            {
                // UI zurücksetzen
                SetBatchModeUI(false);
                _batchCancellation?.Dispose();
                _batchCancellation = null;

                // Cache speichern
                SaveQuestsToCache();

                // Ergebnis anzeigen
                var message = $"Batch-Verarbeitung abgeschlossen.\n\n" +
                              $"Erfolgreich: {successful}\n" +
                              $"Fehlgeschlagen: {failed}\n" +
                              $"Gesamt: {processed} von {questsToProcess.Count}";

                if (errors.Count > 0 && errors.Count <= 10)
                {
                    message += "\n\nFehler:\n" + string.Join("\n", errors);
                }
                else if (errors.Count > 10)
                {
                    message += $"\n\n{errors.Count} Fehler aufgetreten (zu viele zum Anzeigen).";
                }

                MessageBox.Show(message, "Batch-TTS Ergebnis",
                    MessageBoxButton.OK,
                    failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                StatusText.Text = $"Batch fertig: {successful} erfolgreich, {failed} fehlgeschlagen";
            }
        }

        /// <summary>
        /// Bricht die Batch-Verarbeitung ab.
        /// </summary>
        private void OnCancelBatchClick(object sender, RoutedEventArgs e)
        {
            _batchCancellation?.Cancel();
            StatusText.Text = "Abbruch wird angefordert...";
            CancelBatchButton.IsEnabled = false;
        }

        /// <summary>
        /// Aktualisiert die Fortschrittsanzeige.
        /// </summary>
        private void UpdateProgress(int current, int total, string currentItem)
        {
            var percentage = total > 0 ? (current * 100.0 / total) : 0;
            TtsProgressBar.Value = percentage;
            TtsProgressText.Text = $"{current + 1}/{total} ({percentage:F0}%)";
            StatusText.Text = currentItem;
        }

        /// <summary>
        /// Schaltet die UI zwischen Normal- und Batch-Modus um.
        /// </summary>
        private void SetBatchModeUI(bool isBatchMode)
        {
            // Progress-Panel anzeigen/verstecken
            TtsProgressPanel.Visibility = isBatchMode ? Visibility.Visible : Visibility.Collapsed;

            // Abbrechen-Button anzeigen/verstecken
            CancelBatchButton.Visibility = isBatchMode ? Visibility.Visible : Visibility.Collapsed;
            CancelBatchButton.IsEnabled = isBatchMode;

            // Andere Buttons deaktivieren/aktivieren
            BatchDualTtsButton.IsEnabled = !isBatchMode;
            GenerateDualTtsButton.IsEnabled = !isBatchMode;
            TtsPreviewButton.IsEnabled = !isBatchMode;
            FetchFromBlizzardButton.IsEnabled = !isBatchMode;

            // Progress zurücksetzen wenn Batch-Modus beendet
            if (!isBatchMode)
            {
                TtsProgressBar.Value = 0;
                TtsProgressText.Text = "0%";
            }
        }

        #endregion

        #region Male/Female TTS Generation

        /// <summary>
        /// Generiert TTS für die ausgewählte Quest in beiden Stimmen (männlich + weiblich).
        /// </summary>
        private async void OnGenerateDualTtsClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Keine Quest ausgewählt.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ausgabeordner wählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    $"TTS-Service '{_ttsService.ProviderName}' ist nicht konfiguriert.\n\n" +
                    "Bitte API-Key in den Einstellungen hinterlegen.",
                    "TTS nicht verfügbar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var text = SelectedQuest.TtsText;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Kein Text für TTS vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var malePath = _exportSettings.GetMaleOutputPath(SelectedQuest);
            var femalePath = _exportSettings.GetFemaleOutputPath(SelectedQuest);

            try
            {
                GenerateDualTtsButton.IsEnabled = false;
                StatusText.Text = $"Generiere TTS (männlich + weiblich) für Quest {SelectedQuest.QuestId}...";

                // Ordner erstellen
                var maleFolder = Path.GetDirectoryName(malePath);
                var femaleFolder = Path.GetDirectoryName(femalePath);
                if (!string.IsNullOrEmpty(maleFolder)) Directory.CreateDirectory(maleFolder);
                if (!string.IsNullOrEmpty(femaleFolder)) Directory.CreateDirectory(femaleFolder);

                // Männliche Stimme generieren
                StatusText.Text = $"Quest {SelectedQuest.QuestId}: Generiere männliche Stimme...";
                var maleAudio = await _ttsService.GenerateMp3Async(text, _exportSettings.LanguageCode, _exportSettings.MaleVoiceId);
                await File.WriteAllBytesAsync(malePath, maleAudio);
                SelectedQuest.HasMaleTts = true;

                // Weibliche Stimme generieren
                StatusText.Text = $"Quest {SelectedQuest.QuestId}: Generiere weibliche Stimme...";
                var femaleAudio = await _ttsService.GenerateMp3Async(text, _exportSettings.LanguageCode, _exportSettings.FemaleVoiceId);
                await File.WriteAllBytesAsync(femalePath, femaleAudio);
                SelectedQuest.HasFemaleTts = true;

                // Legacy-Flag aktualisieren
                SelectedQuest.HasTtsAudio = true;

                // Session-Tracker aktualisieren (2 Stimmen)
                UpdateSessionTracker(text.Length, 2);

                // UI aktualisieren
                QuestDataGrid.Items.Refresh();
                SaveQuestsToCache();

                StatusText.Text = $"TTS für Quest {SelectedQuest.QuestId} generiert (männlich + weiblich)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Fehler: {ex.Message}";
                MessageBox.Show(
                    $"Fehler bei der TTS-Generierung:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                GenerateDualTtsButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Batch-Generierung für alle gefilterten Quests (männlich + weiblich).
        /// </summary>
        private async void OnBatchDualTtsClick(object sender, RoutedEventArgs e)
        {
            if (FilteredQuests.Count == 0)
            {
                MessageBox.Show("Keine Quests zum Verarbeiten vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ausgabeordner wählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    $"TTS-Service '{_ttsService.ProviderName}' ist nicht konfiguriert.\n\n" +
                    "Bitte API-Key in den Einstellungen hinterlegen.",
                    "TTS nicht verfügbar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Quests filtern: nur die, die noch nicht beide Stimmen haben
            var questsToProcess = FilteredQuests
                .Where(q => !q.HasMaleTts || !q.HasFemaleTts)
                .ToList();

            if (questsToProcess.Count == 0)
            {
                var result = MessageBox.Show(
                    "Alle gefilterten Quests haben bereits beide Stimmen.\n\n" +
                    "Möchtest du die Audio-Dateien neu generieren?",
                    "Alle Quests haben Audio",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    questsToProcess = FilteredQuests.ToList();
                }
                else
                {
                    return;
                }
            }

            // Forecast berechnen und anzeigen
            if (!ShowForecastDialogAndConfirm(questsToProcess))
            {
                return;
            }

            // UI vorbereiten
            _batchCancellation = new CancellationTokenSource();
            SetBatchModeUI(true);

            int processed = 0;
            int successful = 0;
            int skipped = 0;
            int failed = 0;
            var errors = new List<string>();

            try
            {
                for (int i = 0; i < questsToProcess.Count; i++)
                {
                    if (_batchCancellation.Token.IsCancellationRequested)
                    {
                        StatusText.Text = "Batch-Verarbeitung abgebrochen.";
                        break;
                    }

                    var quest = questsToProcess[i];
                    UpdateProgress(i, questsToProcess.Count, $"Quest {quest.QuestId}: {quest.Title}");

                    SelectedQuest = quest;
                    QuestDataGrid.ScrollIntoView(quest);

                    try
                    {
                        var text = quest.TtsText;
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            errors.Add($"Quest {quest.QuestId}: Kein Text vorhanden");
                            failed++;
                            continue;
                        }

                        var malePath = _exportSettings.GetMaleOutputPath(quest);
                        var femalePath = _exportSettings.GetFemaleOutputPath(quest);

                        var maleFolder = Path.GetDirectoryName(malePath);
                        var femaleFolder = Path.GetDirectoryName(femalePath);
                        if (!string.IsNullOrEmpty(maleFolder)) Directory.CreateDirectory(maleFolder);
                        if (!string.IsNullOrEmpty(femaleFolder)) Directory.CreateDirectory(femaleFolder);

                        int voicesGenerated = 0;

                        // Männliche Stimme
                        if (!quest.HasMaleTts || !File.Exists(malePath))
                        {
                            var maleAudio = await _ttsService.GenerateMp3Async(text, _exportSettings.LanguageCode, _exportSettings.MaleVoiceId);
                            await File.WriteAllBytesAsync(malePath, maleAudio, _batchCancellation.Token);
                            quest.HasMaleTts = true;
                            voicesGenerated++;
                        }
                        else
                        {
                            skipped++;
                        }

                        // Weibliche Stimme
                        if (!quest.HasFemaleTts || !File.Exists(femalePath))
                        {
                            var femaleAudio = await _ttsService.GenerateMp3Async(text, _exportSettings.LanguageCode, _exportSettings.FemaleVoiceId);
                            await File.WriteAllBytesAsync(femalePath, femaleAudio, _batchCancellation.Token);
                            quest.HasFemaleTts = true;
                            voicesGenerated++;
                        }
                        else
                        {
                            skipped++;
                        }

                        // Session-Tracker aktualisieren
                        if (voicesGenerated > 0)
                        {
                            UpdateSessionTracker(text.Length, voicesGenerated);
                        }

                        quest.HasTtsAudio = quest.HasMaleTts && quest.HasFemaleTts;
                        successful++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Quest {quest.QuestId}: {ex.Message}");
                        failed++;
                    }

                    processed++;
                    QuestDataGrid.Items.Refresh();

                    // Rate-Limiting
                    await Task.Delay(100, _batchCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Batch-Verarbeitung abgebrochen.";
            }
            finally
            {
                SetBatchModeUI(false);
                _batchCancellation?.Dispose();
                _batchCancellation = null;

                SaveQuestsToCache();

                var message = $"Batch-Verarbeitung abgeschlossen.\n\n" +
                              $"Erfolgreich: {successful}\n" +
                              $"Übersprungen: {skipped}\n" +
                              $"Fehlgeschlagen: {failed}\n" +
                              $"Gesamt: {processed} von {questsToProcess.Count}";

                if (errors.Count > 0 && errors.Count <= 10)
                {
                    message += "\n\nFehler:\n" + string.Join("\n", errors);
                }
                else if (errors.Count > 10)
                {
                    message += $"\n\n{errors.Count} Fehler aufgetreten.";
                }

                MessageBox.Show(message, "Batch-TTS Ergebnis",
                    MessageBoxButton.OK,
                    failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                StatusText.Text = $"Batch fertig: {successful} erfolgreich, {skipped} übersprungen, {failed} fehlgeschlagen";
            }
        }

        /// <summary>
        /// Berechnet die Kosten-Forecast und zeigt einen Bestätigungsdialog.
        /// </summary>
        private bool ShowForecastDialogAndConfirm(List<Quest> quests)
        {
            long totalChars = quests.Sum(q => (long)(q.TtsText?.Length ?? 0));
            var (effectiveChars, estimatedTokens, estimatedCost) = _exportSettings.CalculateCostEstimate(totalChars, 2);

            var message = $"Batch-TTS Kostenvorhersage:\n\n" +
                          $"Quests: {quests.Count}\n" +
                          $"Zeichen (gesamt): {totalChars:N0}\n" +
                          $"Zeichen (effektiv, 2 Stimmen): {effectiveChars:N0}\n" +
                          $"Geschätzte Tokens: ~{estimatedTokens:N0}\n" +
                          $"Geschätzte Kosten: ~{estimatedCost:N2} {_exportSettings.CurrencySymbol}\n\n" +
                          $"Provider: {_ttsService.ProviderName}\n" +
                          $"Männliche Stimme: {_exportSettings.MaleVoiceId}\n" +
                          $"Weibliche Stimme: {_exportSettings.FemaleVoiceId}\n\n" +
                          "Fortfahren?";

            var result = MessageBox.Show(message, "Batch-TTS starten",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            return result == MessageBoxResult.Yes;
        }

        /// <summary>
        /// Aktualisiert den Session-Tracker nach einer TTS-Generierung.
        /// </summary>
        private void UpdateSessionTracker(int textLength, int voiceCount)
        {
            var chars = textLength * voiceCount;
            var (_, tokens, cost) = _exportSettings.CalculateCostEstimate(textLength, voiceCount);

            SessionCharCount += chars;
            SessionTokenEstimate += tokens;
            SessionCostEstimate += cost;
        }

        /// <summary>
        /// Setzt den Session-Tracker zurück.
        /// </summary>
        private void OnResetSessionTrackerClick(object sender, RoutedEventArgs e)
        {
            SessionCharCount = 0;
            SessionTokenEstimate = 0;
            SessionCostEstimate = 0;
            StatusText.Text = "Session-Tracker zurückgesetzt.";
        }

        /// <summary>
        /// Aktualisiert die TTS-Flags aller Quests basierend auf vorhandenen Dateien.
        /// </summary>
        private void UpdateAllQuestsTtsFlags()
        {
            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
                return;

            foreach (var quest in Quests)
            {
                quest.UpdateTtsFlagsFromFileSystem(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
            }

            QuestDataGrid.Items.Refresh();
        }

        #endregion

        #region Sorting & Filtering

        private void ApplyDefaultQuestSorting()
        {
            if (QuestsView == null) return;

            using (QuestsView.DeferRefresh())
            {
                QuestsView.SortDescriptions.Clear();
                QuestsView.SortDescriptions.Add(new SortDescription(nameof(Quest.Zone), ListSortDirection.Ascending));
                QuestsView.SortDescriptions.Add(new SortDescription(nameof(Quest.QuestId), ListSortDirection.Ascending));
            }
        }

        private void PopulateZoneFilter()
        {
            var zones = Quests
                .Select(q => q.Zone ?? "Unbekannt")
                .Distinct()
                .OrderBy(z => z)
                .ToList();

            ZoneFilterComboBox.Items.Clear();
            ZoneFilterComboBox.Items.Add(new ComboBoxItem { Content = "Alle Zonen", Tag = "" });

            foreach (var zone in zones)
            {
                ZoneFilterComboBox.Items.Add(new ComboBoxItem { Content = zone, Tag = zone });
            }

            ZoneFilterComboBox.SelectedIndex = 0;
        }

        private void ZoneFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert während InitializeComponent()
            if (FilteredQuests == null) return;

            if (ZoneFilterComboBox.SelectedItem is ComboBoxItem item)
            {
                _zoneFilter = item.Tag?.ToString() ?? "";
                ApplyFilter();
            }
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert während InitializeComponent()
            if (FilteredQuests == null) return;

            _searchText = SearchTextBox.Text?.ToLowerInvariant() ?? string.Empty;
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            // Guard: Noch nicht initialisiert während InitializeComponent()
            if (FilteredQuests == null || StatusText == null) return;

            FilteredQuests.Clear();

            IEnumerable<Quest> filtered = Quests;

            // Zone Filter
            if (!string.IsNullOrEmpty(_zoneFilter))
            {
                filtered = filtered.Where(q => (q.Zone ?? "Unbekannt") == _zoneFilter);
            }

            // Kategorie Filter
            if (_categoryFilter.HasValue)
            {
                filtered = filtered.Where(q => q.Category == _categoryFilter.Value);
            }

            // Gruppenquest Filter
            if (_groupQuestFilter.HasValue)
            {
                filtered = filtered.Where(q => q.IsGroupQuest == _groupQuestFilter.Value);
            }

            // TTS-Status Filter
            if (_ttsStatusFilter.HasValue)
            {
                if (_ttsStatusFilter.Value)
                {
                    // Nur Quests MIT TTS (beide Stimmen)
                    filtered = filtered.Where(q => q.HasMaleTts && q.HasFemaleTts);
                }
                else
                {
                    // Nur Quests OHNE TTS (mindestens eine fehlt)
                    filtered = filtered.Where(q => !q.HasMaleTts || !q.HasFemaleTts);
                }
            }

            // Text Filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filtered = filtered.Where(q =>
                    (q.Title?.ToLowerInvariant().Contains(_searchText) == true) ||
                    (q.Description?.ToLowerInvariant().Contains(_searchText) == true) ||
                    (q.Zone?.ToLowerInvariant().Contains(_searchText) == true) ||
                    q.QuestId.ToString().Contains(_searchText));
            }

            // Sortiert hinzufügen: Kategorie → Zone → QuestId
            foreach (var quest in filtered
                .OrderBy(q => q.Category)
                .ThenBy(q => q.Zone ?? "")
                .ThenBy(q => q.QuestId))
            {
                FilteredQuests.Add(quest);
            }

            if (FilteredQuests.Count > 0 && (SelectedQuest == null || !FilteredQuests.Contains(SelectedQuest)))
            {
                SelectedQuest = FilteredQuests[0];
            }

            StatusText.Text = $"Filter: {FilteredQuests.Count} von {Quests.Count} Quests";
        }

        private void CategoryFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert während InitializeComponent()
            if (FilteredQuests == null || CategoryFilterComboBox == null) return;

            if (CategoryFilterComboBox.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                if (string.IsNullOrEmpty(tag) || tag == "all")
                {
                    _categoryFilter = null;
                }
                else if (Enum.TryParse<QuestCategory>(tag, out var category))
                {
                    _categoryFilter = category;
                }
                ApplyFilter();
            }
        }

        private void GroupQuestFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert während InitializeComponent()
            if (FilteredQuests == null || GroupQuestFilterComboBox == null) return;

            if (GroupQuestFilterComboBox.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                _groupQuestFilter = tag switch
                {
                    "true" => true,
                    "false" => false,
                    _ => null
                };
                ApplyFilter();
            }
        }

        private void TtsStatusFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert während InitializeComponent()
            if (FilteredQuests == null || TtsStatusFilterComboBox == null) return;

            if (TtsStatusFilterComboBox.SelectedItem is ComboBoxItem item)
            {
                var tag = item.Tag?.ToString();
                _ttsStatusFilter = tag switch
                {
                    "with" => true,
                    "without" => false,
                    _ => null
                };
                ApplyFilter();
            }
        }

        /// <summary>
        /// Wendet ein Filter-Preset an.
        /// </summary>
        private void OnFilterPresetClick(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button) return;

            var preset = button.Tag?.ToString();
            switch (preset)
            {
                case "main_no_tts":
                    // Hauptstory ohne TTS
                    _categoryFilter = QuestCategory.Main;
                    _groupQuestFilter = null;
                    _ttsStatusFilter = false;
                    break;

                case "side_no_tts":
                    // Nebenquests ohne TTS
                    _categoryFilter = QuestCategory.Side;
                    _groupQuestFilter = null;
                    _ttsStatusFilter = false;
                    break;

                case "group_quests":
                    // Alle Gruppenquests
                    _categoryFilter = null;
                    _groupQuestFilter = true;
                    _ttsStatusFilter = null;
                    break;

                case "all_no_tts":
                    // Alle ohne TTS
                    _categoryFilter = null;
                    _groupQuestFilter = null;
                    _ttsStatusFilter = false;
                    break;

                case "reset":
                    // Alle Filter zurücksetzen
                    _categoryFilter = null;
                    _groupQuestFilter = null;
                    _ttsStatusFilter = null;
                    _zoneFilter = "";
                    _searchText = "";
                    SearchTextBox.Text = "";
                    ZoneFilterComboBox.SelectedIndex = 0;
                    break;

                default:
                    return;
            }

            // UI-ComboBoxen aktualisieren
            UpdateFilterComboBoxes();
            ApplyFilter();
        }

        /// <summary>
        /// Aktualisiert die Filter-ComboBoxen basierend auf den aktuellen Filter-Werten.
        /// </summary>
        private void UpdateFilterComboBoxes()
        {
            // Kategorie
            if (_categoryFilter == null)
            {
                CategoryFilterComboBox.SelectedIndex = 0;
            }
            else
            {
                for (int i = 0; i < CategoryFilterComboBox.Items.Count; i++)
                {
                    if (CategoryFilterComboBox.Items[i] is ComboBoxItem item &&
                        item.Tag?.ToString() == _categoryFilter.ToString())
                    {
                        CategoryFilterComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Gruppenquest
            GroupQuestFilterComboBox.SelectedIndex = _groupQuestFilter switch
            {
                true => 1,
                false => 2,
                _ => 0
            };

            // TTS-Status
            TtsStatusFilterComboBox.SelectedIndex = _ttsStatusFilter switch
            {
                true => 1,
                false => 2,
                _ => 0
            };
        }

        /// <summary>
        /// Aktualisiert die Kategorien aller Quests basierend auf ihren Feldern.
        /// </summary>
        private void UpdateAllQuestsCategories()
        {
            foreach (var quest in Quests)
            {
                quest.UpdateCategoryFromFields();
            }
            QuestDataGrid.Items.Refresh();
        }

        /// <summary>
        /// Füllt die Kategorie-Filter ComboBox.
        /// </summary>
        private void PopulateCategoryFilter()
        {
            CategoryFilterComboBox.Items.Clear();
            CategoryFilterComboBox.Items.Add(new ComboBoxItem { Content = "Alle Kategorien", Tag = "all" });

            // Nur Kategorien hinzufügen, die in den Quests vorkommen
            var usedCategories = Quests
                .Select(q => q.Category)
                .Distinct()
                .OrderBy(c => (int)c);

            foreach (var category in usedCategories)
            {
                CategoryFilterComboBox.Items.Add(new ComboBoxItem
                {
                    Content = category.ToDisplayName(),
                    Tag = category.ToString()
                });
            }

            CategoryFilterComboBox.SelectedIndex = 0;
        }

        #endregion

        #region Audio

        private void CheckExistingAudio()
        {
            if (SelectedQuest == null) return;

            // Zuerst im Export-Pfad suchen
            string? audioPath = null;

            if (!string.IsNullOrEmpty(_exportSettings.OutputRootPath))
            {
                audioPath = _exportSettings.GetOutputPath(SelectedQuest);
            }

            // Fallback auf alten Pfad
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            {
                audioPath = _configService.GetAudioOutputPath(SelectedQuest.QuestId);
            }

            if (File.Exists(audioPath))
            {
                _currentAudioPath = audioPath;
                AudioStatusText.Text = "Audio vorhanden - bereit zum Abspielen";
                AudioStatusText.Foreground = new SolidColorBrush(Colors.Green);
                EnableAudioControls(true);
                AudioPlayer.Source = new Uri(audioPath);

                // HasTtsAudio aktualisieren falls nicht gesetzt
                if (!SelectedQuest.HasTtsAudio)
                {
                    SelectedQuest.HasTtsAudio = true;
                    SelectedQuest.TtsAudioPath = audioPath;
                }
            }
            else
            {
                _currentAudioPath = null;
                AudioStatusText.Text = "Kein Audio vorhanden";
                AudioStatusText.Foreground = new SolidColorBrush(Colors.Gray);
                EnableAudioControls(false);
            }
        }

        private void EnableAudioControls(bool enabled)
        {
            PlayButton.IsEnabled = enabled;
            PauseButton.IsEnabled = enabled;
            StopButton.IsEnabled = enabled;
            DownloadButton.IsEnabled = enabled;
        }

        private void OnPlayClick(object sender, RoutedEventArgs e)
        {
            AudioPlayer.Play();
            AudioStatusText.Text = "Wird abgespielt...";
        }

        private void OnPauseClick(object sender, RoutedEventArgs e)
        {
            AudioPlayer.Pause();
            AudioStatusText.Text = "Pausiert";
        }

        private void OnStopClick(object sender, RoutedEventArgs e)
        {
            AudioPlayer.Stop();
            AudioStatusText.Text = "Gestoppt";
        }

        private void AudioPlayer_MediaEnded(object sender, RoutedEventArgs e)
        {
            AudioStatusText.Text = "Wiedergabe beendet";
        }

        private void OnDownloadClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_currentAudioPath) || !File.Exists(_currentAudioPath))
            {
                MessageBox.Show("Keine Audio-Datei zum Herunterladen vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "MP3-Dateien (*.mp3)|*.mp3",
                Title = "Audio-Datei speichern",
                FileName = SelectedQuest != null ? $"quest_{SelectedQuest.QuestId}.mp3" : "audio.mp3"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    File.Copy(_currentAudioPath, dialog.FileName, overwrite: true);
                    StatusText.Text = $"Audio gespeichert: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Speichern:\n{ex.Message}", "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region TTS Preview (ElevenLabs)

        private async void OnTtsPreviewClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var useElevenLabs = UseElevenLabsCheckBox.IsChecked == true;
            var includeTitle = IncludeTitleCheckBox.IsChecked == true;

            var text = includeTitle
                ? SelectedQuest.TtsText
                : (SelectedQuest.Description ?? string.Empty);

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Kein Text zum Vorlesen vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!useElevenLabs || _elevenLabsService == null || !_elevenLabsService.IsConfigured)
            {
                MessageBox.Show(
                    $"[TTS-Vorschau (ohne API)]\n\nText:\n{text}",
                    "TTS-Preview",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            await GenerateAndPlayTtsAsync(text);
        }

        private async Task GenerateAndPlayTtsAsync(string text)
        {
            if (_elevenLabsService == null || SelectedQuest == null) return;

            TtsPreviewButton.IsEnabled = false;
            StatusText.Text = "Generiere Audio...";

            try
            {
                var voiceProfile = (VoiceProfileComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "neutral_male";
                var voiceId = _configService.GetVoiceId(voiceProfile);

                var outputPath = _configService.GetAudioOutputPath(SelectedQuest.QuestId);

                await Task.Run(async () =>
                {
                    await _elevenLabsService.GenerateAndSaveAsync(
                        text,
                        voiceId,
                        outputPath,
                        _configService.Config.ElevenLabs.ModelId);
                });

                _currentAudioPath = outputPath;

                AudioStatusText.Text = "Audio generiert - wird abgespielt";
                AudioStatusText.Foreground = new SolidColorBrush(Colors.Green);
                StatusText.Text = $"Audio gespeichert: {Path.GetFileName(outputPath)}";

                AudioPlayer.Source = new Uri(outputPath);
                AudioPlayer.Play();
                EnableAudioControls(true);
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Fehler: {ex.Message}";
                MessageBox.Show(
                    $"Fehler bei der TTS-Generierung:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                TtsPreviewButton.IsEnabled = true;
            }
        }

        #endregion

        #region Review & Navigation

        private void OnMarkReviewedClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedQuest.TtsReviewed = true;
            QuestDataGrid.Items.Refresh();
            StatusText.Text = $"Quest {SelectedQuest.QuestId} als geprüft markiert";
        }

        private void OnNextQuestClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null || FilteredQuests.Count == 0) return;

            var currentIndex = FilteredQuests.IndexOf(SelectedQuest);
            if (currentIndex < FilteredQuests.Count - 1)
            {
                SelectedQuest = FilteredQuests[currentIndex + 1];
                QuestDataGrid.ScrollIntoView(SelectedQuest);
            }
        }

        #endregion

        #region Settings

        private void OnSettingsClick(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow(_configService)
            {
                Owner = this
            };

            if (settingsWindow.ShowDialog() == true && settingsWindow.SettingsSaved)
            {
                InitializeElevenLabs();
            }
        }

        #endregion

        #region Export

        private void OnExportFilteredClick(object sender, RoutedEventArgs e)
        {
            ExportQuests(FilteredQuests.ToList(), "gefilterte_quests_export.json");
        }

        private void OnBatchExportClick(object sender, RoutedEventArgs e)
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var exportPath = Path.Combine(baseDir, _configService.Config.Paths.BatchExport);

            var includeTitle = IncludeTitleCheckBox.IsChecked == true;
            var voiceProfile = (VoiceProfileComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "neutral_male";

            var exportData = FilteredQuests.Select(q => new
            {
                quest_id = q.QuestId,
                title = q.Title,
                description = q.Description,
                zone = q.Zone,
                is_main_story = q.IsMainStory,
                tts_text = includeTitle ? q.TtsText : q.Description,
                voice_profile = voiceProfile,
                tts_reviewed = q.TtsReviewed,
                has_tts_audio = q.HasTtsAudio
            }).ToList();

            try
            {
                var directory = Path.GetDirectoryName(exportPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(exportData, options);
                File.WriteAllText(exportPath, json);

                StatusText.Text = $"Batch-Export gespeichert: {exportPath}";
                MessageBox.Show(
                    $"Export erfolgreich!\n\n" +
                    $"Datei: {exportPath}\n" +
                    $"Quests: {exportData.Count}",
                    "Batch-Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Export:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExportQuests(List<Quest> quests, string defaultFileName)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON-Dateien (*.json)|*.json|CSV-Dateien (*.csv)|*.csv",
                Title = "Quests exportieren",
                FileName = defaultFileName
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var includeTitle = IncludeTitleCheckBox.IsChecked == true;

                    if (dialog.FileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                    {
                        ExportToCsv(quests, dialog.FileName, includeTitle);
                    }
                    else
                    {
                        ExportToJson(quests, dialog.FileName, includeTitle);
                    }

                    StatusText.Text = $"Export gespeichert: {dialog.FileName}";
                    MessageBox.Show(
                        $"Export erfolgreich!\n{quests.Count} Quests exportiert.",
                        "Export",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Fehler beim Export:\n{ex.Message}",
                        "Fehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
        }

        private void ExportToJson(List<Quest> quests, string path, bool includeTitle)
        {
            var exportData = quests.Select(q => new
            {
                quest_id = q.QuestId,
                title = q.Title,
                zone = q.Zone,
                tts_text = includeTitle ? q.TtsText : q.Description,
                tts_reviewed = q.TtsReviewed,
                has_tts_audio = q.HasTtsAudio
            }).ToList();

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(exportData, options);
            File.WriteAllText(path, json);
        }

        private void ExportToCsv(List<Quest> quests, string path, bool includeTitle)
        {
            var lines = new List<string>
            {
                "quest_id,title,zone,tts_text,tts_reviewed,has_tts_audio"
            };

            foreach (var q in quests)
            {
                var ttsText = includeTitle ? q.TtsText : q.Description;
                lines.Add($"{q.QuestId},{EscapeCsv(q.Title)},{EscapeCsv(q.Zone)},{EscapeCsv(ttsText)},{q.TtsReviewed},{q.HasTtsAudio}");
            }

            File.WriteAllLines(path, lines);
        }

        private static string EscapeCsv(string? field)
        {
            if (string.IsNullOrEmpty(field)) return "";
            if (field.Contains(',') || field.Contains('"') || field.Contains('\n'))
            {
                return $"\"{field.Replace("\"", "\"\"")}\"";
            }
            return field;
        }

        #endregion

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
