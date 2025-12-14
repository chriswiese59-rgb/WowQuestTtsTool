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
using WowQuestTtsTool.Services.TtsEngines;
using WowQuestTtsTool.Services.Update;

namespace WowQuestTtsTool
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        // Wiederverwendbare JsonSerializerOptions (CA1869)
        private static readonly JsonSerializerOptions s_jsonOptions = new() { WriteIndented = true };

        private Quest? _selectedQuest;
        private string _searchText = string.Empty;
        private string _zoneFilter = "";
        private QuestCategory? _categoryFilter = null;
        private bool? _groupQuestFilter = null;
        private string _ttsStatusFilter = "all"; // all, complete, without, without_male, without_female, incomplete
        private string _localizationFilter = "all"; // all, fully_german, mixed, only_english, incomplete
        private string _sortMode = "standard"; // standard, id_only, zone_id, category_id, localization_id
        private string _workflowFilter = "all"; // active (Open+InProgress), open_only, in_progress, completed, all - Standard: all fuer Kompatibilitaet
        private string _voicedFilter = "all"; // not_voiced, voiced, voiced_complete, voiced_incomplete, all
        private ElevenLabsService? _elevenLabsService;
        private readonly TtsConfigService _configService;

        // Text-Overrides Container
        private QuestTextOverridesContainer _textOverrides = new();

        // Skip-Logik: Bereits vertonte Quests im Batch ueberspringen (Standard: true)
        private bool _forceReTtsExisting = false;

        /// <summary>
        /// Wenn true, werden im Batch auch bereits vertonte Quests neu generiert.
        /// Wenn false (Standard), werden bereits vertonte Quests uebersprungen.
        /// </summary>
        public bool ForceReTtsExisting
        {
            get => _forceReTtsExisting;
            set
            {
                if (_forceReTtsExisting != value)
                {
                    _forceReTtsExisting = value;
                    OnPropertyChanged(nameof(ForceReTtsExisting));
                }
            }
        }

        // Datenbank-Konfiguration fuer AzerothCore Quest-Import
        private readonly QuestDbConfig _questDbConfig = new();

        /// <summary>
        /// Gibt die aktuell ausgewaehlte TTS-Engine ID zurueck.
        /// </summary>
        private string? SelectedTtsEngineId
        {
            get
            {
                if (TtsEngineCombo?.SelectedItem is ComboBoxItem item)
                {
                    return item.Tag?.ToString();
                }
                return TtsEngineSettings.Instance.ActiveEngineId;
            }
        }

        /// <summary>
        /// Gibt die maennliche Voice-ID fuer die ausgewaehlte Engine zurueck.
        /// </summary>
        private string GetMaleVoiceIdForSelectedEngine()
        {
            var engineId = SelectedTtsEngineId ?? "External";
            var settings = TtsEngineSettings.Instance;

            return engineId switch
            {
                "OpenAI" => settings.OpenAi.MaleVoice,
                "Gemini" => settings.Gemini.MaleVoice,
                "Claude" => settings.Claude.MaleVoice,
                "External" => settings.External.MaleVoiceId,
                _ => _exportSettings.MaleVoiceId // Fallback
            };
        }

        /// <summary>
        /// Gibt die weibliche Voice-ID fuer die ausgewaehlte Engine zurueck.
        /// </summary>
        private string GetFemaleVoiceIdForSelectedEngine()
        {
            var engineId = SelectedTtsEngineId ?? "External";
            var settings = TtsEngineSettings.Instance;

            return engineId switch
            {
                "OpenAI" => settings.OpenAi.FemaleVoice,
                "Gemini" => settings.Gemini.FemaleVoice,
                "Claude" => settings.Claude.FemaleVoice,
                "External" => settings.External.FemaleVoiceId,
                _ => _exportSettings.FemaleVoiceId // Fallback
            };
        }

        /// <summary>
        /// Generiert Audio ueber den TtsEngineManager mit der ausgewaehlten Engine.
        /// </summary>
        private async Task<byte[]> GenerateAudioWithSelectedEngineAsync(
            string text,
            string voiceGender,
            string? outputPath = null,
            CancellationToken ct = default)
        {
            var engineId = SelectedTtsEngineId ?? "External";
            var voiceId = voiceGender.ToLower() == "female"
                ? GetFemaleVoiceIdForSelectedEngine()
                : GetMaleVoiceIdForSelectedEngine();

            var request = new TtsRequest
            {
                Text = text,
                VoiceId = voiceId,
                VoiceGender = voiceGender,
                OutputPath = outputPath
            };

            var result = await TtsEngineManager.Instance.GenerateAudioAsync(request, engineId, ct);

            if (!result.Success)
            {
                throw new Exception($"TTS-Fehler ({engineId}): {result.ErrorMessage}");
            }

            // Audio-Datei lesen und zurueckgeben
            if (!string.IsNullOrEmpty(result.AudioFilePath) && File.Exists(result.AudioFilePath))
            {
                var audioData = await File.ReadAllBytesAsync(result.AudioFilePath, ct);

                // Temporaere Datei loeschen wenn kein Ausgabepfad angegeben wurde
                if (string.IsNullOrEmpty(outputPath))
                {
                    try { File.Delete(result.AudioFilePath); } catch { }
                }

                return audioData;
            }

            throw new Exception("Keine Audio-Datei generiert.");
        }

        /// <summary>
        /// Konfiguration fuer die MySQL-Datenbankverbindung (fuer XAML-Binding).
        /// </summary>
        public QuestDbConfig QuestDbConfig => _questDbConfig;
        private string? _currentAudioPath;
        private readonly string _questsCachePath;
        private readonly string _blizzardCachePath;  // Separater Cache fuer Blizzard-Daten

        // TTS Export Service
        private ITtsService _ttsService;
        private readonly TtsExportSettings _exportSettings;

        // Audio-Preview Service
        private readonly AudioPreviewService _audioPreviewService;

        // LLM Text Enhancer Service (Hoerbuch-Optimierung)
        private LlmTextEnhancerService? _llmEnhancerService;

        // Update Manager
        private readonly UpdateManager _updateManager = new();

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

        // === Audio-Index Properties ===

        private bool _isAudioIndexScanning;
        private string _audioIndexStats = "";

        /// <summary>
        /// Gibt an, ob der AudioIndexService gerade scannt.
        /// </summary>
        public bool IsAudioIndexScanning
        {
            get => _isAudioIndexScanning;
            set
            {
                if (_isAudioIndexScanning != value)
                {
                    _isAudioIndexScanning = value;
                    OnPropertyChanged(nameof(IsAudioIndexScanning));
                }
            }
        }

        /// <summary>
        /// Statistik-Text fuer den Audio-Index (z.B. "123 mit Audio / 456 offen").
        /// </summary>
        public string AudioIndexStats
        {
            get => _audioIndexStats;
            set
            {
                if (_audioIndexStats != value)
                {
                    _audioIndexStats = value;
                    OnPropertyChanged(nameof(AudioIndexStats));
                }
            }
        }

        /// <summary>
        /// Aktueller Filter fuer den Audio/Voiced-Status.
        /// </summary>
        public string VoicedFilter
        {
            get => _voicedFilter;
            set
            {
                if (_voicedFilter != value)
                {
                    _voicedFilter = value;
                    OnPropertyChanged(nameof(VoicedFilter));
                    ApplyFilter();
                }
            }
        }

        public ObservableCollection<Quest> Quests { get; } = [];
        public ObservableCollection<Quest> FilteredQuests { get; } = [];
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
                    UpdateTtsPreviewInfo();
                    UpdateLockButtonVisibility();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        // Speichert die Benutzerauswahl vom Startup-Dialog
        private StartupDialog.StartupChoice _startupChoice = StartupDialog.StartupChoice.ContinueLastProject;
        private string? _selectedProjectPath;
        private bool _loadOnlyVoiced;

        /// <summary>
        /// Konstruktor mit Startup-Auswahl (wird von App.xaml.cs aufgerufen).
        /// </summary>
        /// <param name="startupChoice">Die Auswahl aus dem Startup-Dialog</param>
        /// <param name="selectedProjectPath">Pfad zum ausgewaehlten Projekt (optional)</param>
        /// <param name="loadOnlyVoiced">Ob nur vertonte Quests geladen werden sollen</param>
        public MainWindow(StartupDialog.StartupChoice startupChoice, string? selectedProjectPath = null, bool loadOnlyVoiced = false)
        {
            _startupChoice = startupChoice;
            _selectedProjectPath = selectedProjectPath;
            _loadOnlyVoiced = loadOnlyVoiced;

            InitializeComponent();
            DataContext = this;

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _questsCachePath = Path.Combine(baseDir, "data", "quests_cache.json");
            _blizzardCachePath = Path.Combine(baseDir, "data", "blizzard_quests_cache.json");

            _configService = new TtsConfigService();

            // TTS Export initialisieren
            _exportSettings = TtsExportSettings.Instance;

            // Dummy-Service als Fallback (wird in InitializeElevenLabs überschrieben)
            _ttsService = new DummyTtsService();

            // Audio-Preview Service initialisieren
            _audioPreviewService = new AudioPreviewService();
            _audioPreviewService.StatusChanged += AudioPreviewService_StatusChanged;
            _audioPreviewService.PlaybackEnded += AudioPreviewService_PlaybackEnded;

            InitializeElevenLabs();
            InitializeLlmEnhancer();
            InitializeTtsExportUI();
            InitializeVoiceProfileComboBox();
            InitializeUpdateManager();

            // CollectionView für Sortierung
            QuestsView = CollectionViewSource.GetDefaultView(FilteredQuests);

            // Beim Schließen Quests speichern
            Closing += MainWindow_Closing;

            // Startup-Auswahl verarbeiten
            ProcessStartupChoice();
        }

        /// <summary>
        /// Verarbeitet die Startup-Auswahl.
        /// </summary>
        private void ProcessStartupChoice()
        {
            switch (_startupChoice)
            {
                case StartupDialog.StartupChoice.ContinueLastProject:
                    LoadExistingProject();
                    break;

                case StartupDialog.StartupChoice.LoadOtherProject:
                    LoadProjectFromPath(_selectedProjectPath);
                    break;

                case StartupDialog.StartupChoice.NewProject:
                    StartNewProject();
                    break;
            }
        }

        /// <summary>
        /// Laedt ein Projekt von einem bestimmten Pfad.
        /// </summary>
        private void LoadProjectFromPath(string? projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
            {
                LoadExistingProject();
                return;
            }

            try
            {
                // Projekt-Settings anwenden
                if (ProjectService.Instance.HasExistingProject)
                {
                    ProjectService.Instance.ApplySettingsToExport(_exportSettings);
                }

                // Text-Overrides laden
                _textOverrides = QuestTextOverridesStore.Load();

                // Quests aus dem angegebenen Pfad laden
                if (File.Exists(projectPath))
                {
                    LoadQuestsFromPath(projectPath);
                    StatusText.Text = $"Projekt geladen: {Quests.Count} Quests aus {Path.GetFileName(projectPath)}";
                }
                else
                {
                    MessageBox.Show(
                        $"Die Projektdatei wurde nicht gefunden:\n{projectPath}",
                        "Fehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Projekt-Daten mergen
                if (ProjectService.Instance.HasExistingProject)
                {
                    ProjectService.Instance.MergeWithQuests(Quests);
                }

                // Text-Overrides anwenden
                var overrideCount = QuestTextOverridesStore.ApplyOverrides(Quests, _textOverrides);
                if (overrideCount > 0)
                {
                    StatusText.Text += $" ({overrideCount} Overrides)";
                }

                // Sortierung und Filter anwenden
                ApplyDefaultQuestSorting();
                PopulateZoneFilter();

                // Audio-Index initialisieren
                InitializeAudioIndex();

                // Wenn nur vertonte Quests geladen werden sollen
                if (_loadOnlyVoiced)
                {
                    Dispatcher.InvokeAsync(async () =>
                    {
                        await Task.Delay(500);
                        FilterToVoicedQuestsOnly();
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Laden des Projekts:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Laedt das bestehende Projekt und alle gespeicherten Daten.
        /// </summary>
        private void LoadExistingProject()
        {
            try
            {
                // Projekt-Settings anwenden
                if (ProjectService.Instance.HasExistingProject)
                {
                    ProjectService.Instance.ApplySettingsToExport(_exportSettings);
                }

                // Text-Overrides laden
                _textOverrides = QuestTextOverridesStore.Load();

                // Pfad zum Laden bestimmen (Standard-Cache)
                string pathToLoad;
                if (File.Exists(_questsCachePath))
                {
                    pathToLoad = _questsCachePath;
                }
                else
                {
                    LoadQuestsFromJson();
                    ApplyDefaultQuestSorting();
                    PopulateZoneFilter();
                    InitializeAudioIndex();
                    return;
                }

                // Quests laden
                LoadQuestsFromPath(pathToLoad);

                // Projekt-Daten mergen (TTS-Status aus Projekt uebernehmen)
                if (ProjectService.Instance.HasExistingProject)
                {
                    ProjectService.Instance.MergeWithQuests(Quests);
                }

                // Text-Overrides anwenden
                var overrideCount = QuestTextOverridesStore.ApplyOverrides(Quests, _textOverrides);

                // Audio-Index initialisieren (muss vor Filter passieren)
                InitializeAudioIndex();

                // Warten bis Audio-Index geladen ist, dann filtern
                Dispatcher.InvokeAsync(async () =>
                {
                    // Kurz warten bis Audio-Index geladen
                    await Task.Delay(500);

                    // Wenn nur vertonte Quests geladen werden sollen
                    if (_loadOnlyVoiced)
                    {
                        FilterToVoicedQuestsOnly();
                    }

                    // Status-Text aktualisieren
                    var statusText = $"Projekt geladen: {FilteredQuests.Count} Quests";

                    if (_loadOnlyVoiced)
                    {
                        statusText += " (nur vertonte)";
                    }
                    if (overrideCount > 0)
                    {
                        statusText += $" ({overrideCount} Overrides)";
                    }

                    StatusText.Text = statusText;
                });

                // Sortierung und Zonen-Filter anwenden
                ApplyDefaultQuestSorting();
                PopulateZoneFilter();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Laden des Projekts:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Startet ein neues Projekt (setzt Fortschritt zurueck).
        /// </summary>
        private void StartNewProject()
        {
            try
            {
                // Projekt zuruecksetzen (erstellt Backup)
                ProjectService.Instance.ResetProject();

                // Text-Overrides zuruecksetzen
                _textOverrides = new QuestTextOverridesContainer();

                // Quests leeren
                Quests.Clear();
                FilteredQuests.Clear();

                StatusText.Text = "Neues Projekt gestartet. Bitte Quests laden (Blizzard oder JSON).";

                // Sortierung und Zonen-Filter anwenden
                ApplyDefaultQuestSorting();
                PopulateZoneFilter();

                // Audio-Index initialisieren
                InitializeAudioIndex();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Starten des neuen Projekts:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Startet ein neues Projekt und laedt Quests von Blizzard.
        /// </summary>
        private async void StartNewProjectAndFetchBlizzard()
        {
            try
            {
                // Projekt zuruecksetzen (erstellt Backup)
                ProjectService.Instance.ResetProject();

                // Text-Overrides zuruecksetzen
                _textOverrides = new QuestTextOverridesContainer();

                // Quests leeren
                Quests.Clear();
                FilteredQuests.Clear();

                StatusText.Text = "Neues Projekt - Lade Quests von Blizzard...";

                // Audio-Index initialisieren
                InitializeAudioIndex();

                // Blizzard-Quests laden (asynchron)
                await Task.Delay(100); // UI aktualisieren lassen
                OnFetchFromBlizzardClick(this, new RoutedEventArgs());
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Laden von Blizzard:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Initialisiert den AudioIndexService und startet den ersten Scan.
        /// </summary>
        private async void InitializeAudioIndex()
        {
            // Event-Handler registrieren
            AudioIndexService.Instance.IndexUpdated += OnAudioIndexUpdated;
            AudioIndexService.Instance.ScanProgressChanged += OnAudioIndexScanProgress;

            // Ersten Scan starten (asynchron)
            await RefreshAudioIndexAsync();
        }

        /// <summary>
        /// Event-Handler wenn der Audio-Index aktualisiert wurde.
        /// </summary>
        private void OnAudioIndexUpdated(object? sender, EventArgs e)
        {
            // Auf UI-Thread ausfuehren
            Dispatcher.Invoke(() =>
            {
                UpdateQuestsFromAudioIndex();
                UpdateAudioIndexStats();
                ApplyFilter();
            });
        }

        /// <summary>
        /// Event-Handler fuer Scan-Fortschritt.
        /// </summary>
        private void OnAudioIndexScanProgress(object? sender, (int FilesScanned, int QuestsFound) progress)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"Scanne Audio-Ordner... {progress.FilesScanned} Dateien, {progress.QuestsFound} Quests";
            });
        }

        /// <summary>
        /// Fuehrt einen neuen Audio-Index-Scan durch.
        /// </summary>
        private async Task RefreshAudioIndexAsync()
        {
            if (AudioIndexService.Instance.IsScanning)
                return;

            IsAudioIndexScanning = true;
            var rootPath = TtsExportSettings.Instance.OutputRootPath;

            try
            {
                // Pruefen ob Ordner konfiguriert ist
                if (string.IsNullOrWhiteSpace(rootPath))
                {
                    var result = MessageBox.Show(
                        "Der Audio-Ordner ist nicht konfiguriert.\n\n" +
                        "Moechten Sie jetzt einen Ordner auswaehlen, in dem Ihre Audio-Dateien gespeichert sind?",
                        "Audio-Ordner nicht konfiguriert",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        var dialog = new Microsoft.Win32.OpenFolderDialog
                        {
                            Title = "Audio-Ordner auswaehlen"
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            rootPath = dialog.FolderName;
                            TtsExportSettings.Instance.OutputRootPath = rootPath;
                            TtsExportSettings.Instance.Save();
                            OutputPathTextBox.Text = rootPath;
                        }
                        else
                        {
                            StatusText.Text = "Audio-Scan abgebrochen.";
                            return;
                        }
                    }
                    else
                    {
                        StatusText.Text = "Audio-Ordner nicht konfiguriert! Bitte in Einstellungen setzen.";
                        return;
                    }
                }

                // Pruefen ob Ordner existiert
                if (!System.IO.Directory.Exists(rootPath))
                {
                    var result = MessageBox.Show(
                        $"Der konfigurierte Audio-Ordner existiert nicht:\n{rootPath}\n\n" +
                        "Moechten Sie einen anderen Ordner auswaehlen?",
                        "Ordner nicht gefunden",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result == MessageBoxResult.Yes)
                    {
                        var dialog = new Microsoft.Win32.OpenFolderDialog
                        {
                            Title = "Audio-Ordner auswaehlen"
                        };

                        if (dialog.ShowDialog() == true)
                        {
                            rootPath = dialog.FolderName;
                            TtsExportSettings.Instance.OutputRootPath = rootPath;
                            TtsExportSettings.Instance.Save();
                            OutputPathTextBox.Text = rootPath;
                        }
                        else
                        {
                            StatusText.Text = "Audio-Scan abgebrochen.";
                            return;
                        }
                    }
                    else
                    {
                        StatusText.Text = $"Audio-Ordner existiert nicht: {rootPath}";
                        return;
                    }
                }

                StatusText.Text = $"Scanne Audio-Ordner: {rootPath}";

                var count = await AudioIndexService.Instance.ScanAudioFolderAsync();
                UpdateQuestsFromAudioIndex();
                UpdateAudioIndexStats();
                ApplyFilter(); // Filter neu anwenden nach Index-Update

                var lastScan = AudioIndexService.Instance.LastScanTime?.ToString("HH:mm:ss") ?? "-";
                var questsWithAudio = Quests.Count(q => q.HasAudioFromIndex);
                StatusText.Text = $"Audio-Scan: {count} Quests im Index, {questsWithAudio} von {Quests.Count} geladenen Quests haben Audio";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Audio-Scan Fehler: {ex.Message}";
                MessageBox.Show($"Audio-Scan Fehler:\n{ex.Message}\n\nPfad: {rootPath}", "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsAudioIndexScanning = false;
            }
        }

        /// <summary>
        /// Aktualisiert alle Quest-Objekte mit den Daten aus dem Audio-Index.
        /// </summary>
        private void UpdateQuestsFromAudioIndex()
        {
            var audioService = AudioIndexService.Instance;
            int updatedCount = 0;

            foreach (var quest in Quests)
            {
                var entry = audioService.GetAudioEntry(quest.QuestId);
                quest.UpdateFromAudioIndex(entry);
                if (entry != null && entry.HasAnyAudio)
                    updatedCount++;
            }

            System.Diagnostics.Debug.WriteLine($"UpdateQuestsFromAudioIndex: {updatedCount} von {Quests.Count} Quests haben Audio im Index");

            // DataGrid aktualisieren
            QuestDataGrid?.Items.Refresh();
        }

        /// <summary>
        /// Aktualisiert die Audio-Index-Statistiken.
        /// </summary>
        private void UpdateAudioIndexStats()
        {
            var (totalQuests, withMale, withFemale, withBoth, totalFiles) = AudioIndexService.Instance.GetStatistics();
            var openQuests = Quests.Count - Quests.Count(q => q.HasAudioFromIndex);
            AudioIndexStats = $"{totalQuests} vertont / {openQuests} offen";
        }

        /// <summary>
        /// Handler fuer den "Audio-Ordner scannen"-Button.
        /// </summary>
        private async void OnRefreshAudioIndexClick(object sender, RoutedEventArgs e)
        {
            await RefreshAudioIndexAsync();
        }

        /// <summary>
        /// Handler fuer den VoicedFilter-ComboBox.
        /// </summary>
        private void VoicedFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert waehrend InitializeComponent()
            if (FilteredQuests == null) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {
                VoicedFilter = item.Tag?.ToString() ?? "all";
            }
        }

        private void WorkflowFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert waehrend InitializeComponent()
            if (FilteredQuests == null) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {
                _workflowFilter = item.Tag?.ToString() ?? "active";
                ApplyFilter();
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

            // TTS-Engine ComboBox initialisieren
            InitializeTtsEngineCombo();
        }

        /// <summary>
        /// Initialisiert die TTS-Engine Auswahl ComboBox.
        /// </summary>
        private void InitializeTtsEngineCombo()
        {
            TtsEngineCombo.Items.Clear();

            var manager = TtsEngineManager.Instance;
            var engines = manager.RegisteredEngines.Values;
            var activeEngineId = TtsEngineSettings.Instance.ActiveEngineId;

            foreach (var engine in engines)
            {
                // Status-Text
                string statusText;
                if (engine.IsAvailable)
                    statusText = "bereit";
                else if (engine.IsConfigured)
                    statusText = "konfiguriert";
                else
                    statusText = "nicht konfiguriert";

                var item = new ComboBoxItem
                {
                    Content = engine.IsAvailable
                        ? engine.DisplayName
                        : $"{engine.DisplayName} ({statusText})",
                    Tag = engine.EngineId,
                    IsEnabled = true, // Immer waehlbar
                    ToolTip = $"{engine.DisplayName} - {statusText}"
                };

                TtsEngineCombo.Items.Add(item);

                // Standard-Engine auswaehlen
                if (engine.EngineId == activeEngineId ||
                    (string.IsNullOrEmpty(activeEngineId) && engine.EngineId == "External"))
                {
                    TtsEngineCombo.SelectedItem = item;
                }
            }

            // Fallback: Erste Engine auswaehlen
            if (TtsEngineCombo.SelectedItem == null && TtsEngineCombo.Items.Count > 0)
            {
                TtsEngineCombo.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Event-Handler fuer TTS-Engine Auswahl.
        /// </summary>
        private void TtsEngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert waehrend InitializeComponent()
            if (TtsEngineCombo?.SelectedItem == null) return;

            var selectedItem = TtsEngineCombo.SelectedItem as ComboBoxItem;
            var engineId = selectedItem?.Tag?.ToString();

            if (!string.IsNullOrEmpty(engineId))
            {
                TtsEngineSettings.Instance.ActiveEngineId = engineId;
                TtsEngineSettings.Instance.Save();

                // Provider-Text aktualisieren
                var engine = TtsEngineManager.Instance.GetEngine(engineId);
                if (engine != null)
                {
                    TtsProviderText.Text = $"TTS: {engine.DisplayName}";
                }
            }
        }

        private void MainWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Audio-Preview stoppen und aufräumen
            _audioPreviewService?.Dispose();

            // Quests automatisch speichern beim Schließen
            if (Quests.Count > 0)
            {
                SaveQuestsToCache();
                SaveProject();
            }

            // Export-Settings speichern
            _exportSettings.Save();
        }

        /// <summary>
        /// Speichert das Projekt mit allen Quest-Metadaten und Einstellungen.
        /// </summary>
        private void SaveProject()
        {
            try
            {
                // Quest-Metadaten aktualisieren
                ProjectService.Instance.UpdateQuests(Quests);

                // Einstellungen aktualisieren
                ProjectService.Instance.UpdateSettingsFromExport(_exportSettings);

                // Statistiken aktualisieren
                ProjectService.Instance.UpdateStatistics(Quests);

                // Speichern
                ProjectService.Instance.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern des Projekts: {ex.Message}");
            }
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

                var options = s_jsonOptions;
                var json = JsonSerializer.Serialize(Quests.ToArray(), options);
                File.WriteAllText(_questsCachePath, json);
            }
            catch
            {
                // Stilles Fehlschlagen beim Cache-Speichern
            }
        }

        /// <summary>
        /// Speichert Blizzard-Quests in einen SEPARATEN Cache.
        /// Wird nicht von anderen Quellen ueberschrieben.
        /// </summary>
        private void SaveBlizzardCache(IEnumerable<Quest> quests)
        {
            try
            {
                var directory = Path.GetDirectoryName(_blizzardCachePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Markiere alle als Blizzard-Quelle
                var blizzardQuests = quests.Select(q =>
                {
                    q.HasBlizzardSource = true;
                    q.HasAcoreSource = false;
                    return q;
                }).ToArray();

                var options = s_jsonOptions;
                var json = JsonSerializer.Serialize(blizzardQuests, options);
                File.WriteAllText(_blizzardCachePath, json);

                System.Diagnostics.Debug.WriteLine($"Blizzard-Cache gespeichert: {blizzardQuests.Length} Quests -> {_blizzardCachePath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern des Blizzard-Cache: {ex.Message}");
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
                PopulateZoneLoadComboBox(); // NEU: Zone-Lade-ComboBox befuellen

                if (FilteredQuests.Count > 0)
                    SelectedQuest = FilteredQuests[0];
            }

            StatusText.Text = $"Geladen: {Quests.Count} Quests aus {Path.GetFileName(path)}";
        }

        /// <summary>
        /// Laedt Quests aus einer SQLite-Datenbank.
        /// </summary>
        /// <param name="limit">Maximale Anzahl Quests (0 = alle)</param>
        private async Task LoadQuestsFromSqliteAsync(int limit = 0)
        {
            var dbPath = _exportSettings.SqliteDatabasePath;

            if (string.IsNullOrWhiteSpace(dbPath) || !File.Exists(dbPath))
            {
                MessageBox.Show(
                    "SQLite-Datenbank nicht gefunden.\n\n" +
                    "Bitte in Einstellungen -> Quest-Datenbank den Pfad zur quests_deDE.db angeben.",
                    "Datenbank fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var limitText = limit > 0 ? $" (max. {limit})" : " (alle)";
            StatusText.Text = $"Lade Quests aus SQLite{limitText}...";
            FetchFromBlizzardButton.IsEnabled = false;
            LoadFromSqliteButton.IsEnabled = false;

            try
            {
                using var repository = new SqliteQuestRepository(dbPath);
                var allQuests = await repository.GetAllQuestsAsync();

                Quests.Clear();
                FilteredQuests.Clear();

                // Sortiert nach Zone und QuestId, dann Limit anwenden
                IEnumerable<Quest> sortedQuests = allQuests
                    .OrderBy(q => q.Zone ?? "")
                    .ThenBy(q => q.QuestId);

                // Limit anwenden wenn > 0
                if (limit > 0)
                {
                    sortedQuests = sortedQuests.Take(limit);
                }

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
                PopulateZoneLoadComboBox();

                if (FilteredQuests.Count > 0)
                    SelectedQuest = FilteredQuests[0];

                var totalInfo = limit > 0 && allQuests.Count > limit
                    ? $" (von {allQuests.Count} gesamt)"
                    : "";
                StatusText.Text = $"Geladen: {Quests.Count} Quests aus SQLite{totalInfo}";

                // Cache aktualisieren
                SaveQuestsToCache();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Laden aus SQLite:\n{ex.Message}",
                    "SQLite-Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"SQLite-Fehler: {ex.Message}";
            }
            finally
            {
                FetchFromBlizzardButton.IsEnabled = true;
                LoadFromSqliteButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Handler fuer "Von SQLite laden"-Button.
        /// </summary>
        private async void OnLoadFromSqliteClick(object sender, RoutedEventArgs e)
        {
            // Limit aus TextBox lesen
            int limit = 0;
            if (int.TryParse(SqliteLoadLimitBox.Text, out int parsedLimit) && parsedLimit > 0)
            {
                limit = parsedLimit;
            }

            await LoadQuestsFromSqliteAsync(limit);
            ApplyDefaultQuestSorting();
            PopulateZoneFilter();
            PopulateZoneLoadComboBox();
        }

        /// <summary>
        /// Handler fuer "Merged laden"-Button.
        /// Kombiniert Blizzard-Daten mit AzerothCore-Daten.
        /// </summary>
        private async void OnLoadMergedClick(object sender, RoutedEventArgs e)
        {
            await LoadQuestsFromMergedRepositoryAsync();
            ApplyDefaultQuestSorting();
            PopulateZoneFilter();
            PopulateZoneLoadComboBox();
        }

        /// <summary>
        /// Laedt Quests aus dem MergedQuestRepository (Blizzard + AzerothCore).
        /// WICHTIG:
        /// - Blizzard ist die BASIS (nur diese Quests werden geladen)
        /// - AzerothCore ERGAENZT nur fehlende Felder bei den Blizzard-Quests
        /// - AzerothCore-only Quests werden NICHT hinzugefuegt!
        /// </summary>
        private async Task LoadQuestsFromMergedRepositoryAsync()
        {
            // Blizzard-Quelle: Separater Cache (wird NICHT von SQLite ueberschrieben)
            var blizzardJsonPath = _blizzardCachePath;

            // Falls manuell ein anderer Pfad konfiguriert wurde, diesen verwenden
            if (!string.IsNullOrWhiteSpace(_exportSettings.BlizzardJsonPath) && File.Exists(_exportSettings.BlizzardJsonPath))
            {
                blizzardJsonPath = _exportSettings.BlizzardJsonPath;
            }

            // AzerothCore-Quelle: SQLite-Datenbank
            var acoreSqlitePath = _exportSettings.SqliteDatabasePath;

            // Blizzard MUSS vorhanden sein (ist die Basis)
            bool hasBlizzard = File.Exists(blizzardJsonPath);
            bool hasAcore = !string.IsNullOrWhiteSpace(acoreSqlitePath) && File.Exists(acoreSqlitePath);

            if (!hasBlizzard)
            {
                MessageBox.Show(
                    "Keine Blizzard-Daten vorhanden!\n\n" +
                    "Bitte zuerst 'Von Blizzard laden' klicken (orange Button).\n\n" +
                    "Die Blizzard-Quests sind die Basis.\n" +
                    "AzerothCore ergaenzt nur fehlende Texte.",
                    "Blizzard-Daten fehlen",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!hasAcore)
            {
                MessageBox.Show(
                    "Keine AzerothCore-Datenbank konfiguriert!\n\n" +
                    "Bitte in Einstellungen -> Quest-Datenbank\n" +
                    "den Pfad zur SQLite-Datei angeben.\n\n" +
                    "(Erstellt mit WowQuestExporter)",
                    "AzerothCore fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            System.Diagnostics.Debug.WriteLine($"Merged: Blizzard={blizzardJsonPath}, ACore={acoreSqlitePath}");

            StatusText.Text = "Lade und merge Quests (Blizzard + AzerothCore)...";
            FetchFromBlizzardButton.IsEnabled = false;
            LoadFromSqliteButton.IsEnabled = false;
            LoadMergedButton.IsEnabled = false;

            try
            {
                // Quellen erstellen
                var blizzardSource = new BlizzardJsonQuestSource(blizzardJsonPath);
                var acoreSource = new AcoreSqliteQuestSource(acoreSqlitePath ?? "");

                // Merged Repository erstellen
                var mergedRepo = new MergedQuestRepository(blizzardSource, acoreSource);

                // Alle Quests laden (gemerged)
                var mergedQuests = await mergedRepo.GetAllQuestsAsync();

                Quests.Clear();
                FilteredQuests.Clear();

                // Sortiert nach Zone und QuestId hinzufuegen
                var sortedQuests = mergedQuests
                    .OrderBy(q => q.Zone ?? "")
                    .ThenBy(q => q.QuestId);

                foreach (var q in sortedQuests)
                {
                    Quests.Add(q);
                    FilteredQuests.Add(q);
                }

                // Kategorien aus Feldern ableiten
                UpdateAllQuestsCategories();

                // TTS-Flags basierend auf vorhandenen Dateien aktualisieren
                UpdateAllQuestsTtsFlags();

                // Filter-ComboBoxen befuellen
                PopulateZoneFilter();
                PopulateCategoryFilter();
                PopulateZoneLoadComboBox();

                if (FilteredQuests.Count > 0)
                    SelectedQuest = FilteredQuests[0];

                // Statistik zaehlen
                int blizzOnlyCount = mergedQuests.Count(q => q.HasBlizzardSource && !q.HasAcoreSource);
                int acoreOnlyCount = mergedQuests.Count(q => q.HasAcoreSource && !q.HasBlizzardSource);
                int bothCount = mergedQuests.Count(q => q.HasBlizzardSource && q.HasAcoreSource);
                int totalBlizzard = blizzOnlyCount + bothCount;
                int totalAcore = acoreOnlyCount + bothCount;

                StatusText.Text = $"Merged: {Quests.Count} Quests (Blizzard: {totalBlizzard}, ACore: {totalAcore}, Kombiniert: {bothCount})";

                // MessageBox mit Details
                MessageBox.Show(
                    $"Merged erfolgreich!\n\n" +
                    $"Gesamt: {Quests.Count} Quests\n\n" +
                    $"Quellen:\n" +
                    $"  - Nur Blizzard: {blizzOnlyCount}\n" +
                    $"  - Nur AzerothCore: {acoreOnlyCount}\n" +
                    $"  - Beide kombiniert: {bothCount}\n\n" +
                    $"Blizzard-Daten: {totalBlizzard} Quests\n" +
                    $"AzerothCore-Daten: {totalAcore} Quests",
                    "Merged laden",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                // Cache aktualisieren
                SaveQuestsToCache();

                // Sortierung anwenden
                ApplyDefaultQuestSorting();

                // Dispose Sources
                if (acoreSource is IDisposable disposable)
                    disposable.Dispose();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Merged-Laden:\n{ex.Message}",
                    "Merge-Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = $"Merge-Fehler: {ex.Message}";
            }
            finally
            {
                FetchFromBlizzardButton.IsEnabled = true;
                LoadFromSqliteButton.IsEnabled = true;
                LoadMergedButton.IsEnabled = true;
            }
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
                    PopulateZoneLoadComboBox(); // NEU: Zone-Lade-ComboBox befuellen
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
                PopulateCategoryFilter();
                PopulateZoneLoadComboBox();

                // WICHTIG: Blizzard-Daten in SEPARATEN Cache speichern (wird nicht ueberschrieben)
                SaveBlizzardCache(quests);

                // Auch in den normalen Cache speichern (fuer UI-Wiederherstellung)
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

        #region Zone-Based Quest Loading (NEU)

        /// <summary>
        /// Initialisiert die Zone-Lade-ComboBox mit allen verfuegbaren Zonen.
        /// Wird beim Laden von Quests aufgerufen.
        /// </summary>
        private void PopulateZoneLoadComboBox()
        {
            // Alle verfuegbaren Zonen aus den Quests sammeln
            var zones = Quests
                .Select(q => q.Zone ?? "Unbekannt")
                .Where(z => !string.IsNullOrWhiteSpace(z))
                .Distinct()
                .OrderBy(z => z)
                .ToList();

            ZoneLoadComboBox.Items.Clear();

            // "Alle Zonen" Option an erster Stelle
            ZoneLoadComboBox.Items.Add(new ComboBoxItem
            {
                Content = "-- Alle Zonen --",
                Tag = ""
            });

            foreach (var zone in zones)
            {
                ZoneLoadComboBox.Items.Add(new ComboBoxItem
                {
                    Content = zone,
                    Tag = zone
                });
            }

            ZoneLoadComboBox.SelectedIndex = 0;
        }

        /// <summary>
        /// Event-Handler fuer den "Quests laden" Button.
        /// Laedt Quests basierend auf Zone und Filterkriterien.
        /// </summary>
        private void OnLoadZoneQuestsClick(object sender, RoutedEventArgs e)
        {
            // Zone aus ComboBox auslesen
            string? selectedZone = null;
            if (ZoneLoadComboBox.SelectedItem is ComboBoxItem zoneItem)
            {
                selectedZone = zoneItem.Tag?.ToString();
            }

            // Filter-Optionen auslesen
            bool onlyMainQuests = LoadOnlyMainQuestsCheckBox.IsChecked == true;
            bool onlyGroupQuests = LoadOnlyGroupQuestsCheckBox.IsChecked == true;
            bool onlyWithoutTts = LoadOnlyWithoutTtsCheckBox.IsChecked == true;

            // Max-Anzahl auslesen
            int maxQuests = 0;
            if (int.TryParse(MaxQuestsTextBox.Text, out var parsedMax) && parsedMax > 0)
            {
                maxQuests = parsedMax;
            }

            // Quests filtern
            LoadQuestsForZone(selectedZone, onlyMainQuests, onlyGroupQuests, onlyWithoutTts, maxQuests);
        }

        /// <summary>
        /// Laedt Quests aus einer bestimmten Zone mit optionalen Filtern.
        /// </summary>
        /// <param name="zone">Zone-Name (null oder leer = alle Zonen)</param>
        /// <param name="onlyMainQuests">Nur Hauptquests laden</param>
        /// <param name="onlyGroupQuests">Nur Gruppenquests laden</param>
        /// <param name="onlyWithoutTts">Nur Quests ohne TTS laden</param>
        /// <param name="maxQuests">Maximale Anzahl (0 = unbegrenzt)</param>
        public void LoadQuestsForZone(
            string? zone,
            bool onlyMainQuests = false,
            bool onlyGroupQuests = false,
            bool onlyWithoutTts = false,
            int maxQuests = 0)
        {
            // Ausgangs-Collection: Alle Quests aus dem Cache
            IEnumerable<Quest> sourceQuests = Quests;

            // Falls keine Quests geladen sind, abbrechen
            if (!sourceQuests.Any())
            {
                MessageBox.Show(
                    "Keine Quests im Cache vorhanden.\n\n" +
                    "Bitte zuerst Quests laden:\n" +
                    "- 'Von Blizzard laden' oder\n" +
                    "- 'JSON laden'",
                    "Keine Quests",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Filter anwenden
            IEnumerable<Quest> filtered = sourceQuests;

            // Zone-Filter
            if (!string.IsNullOrEmpty(zone))
            {
                filtered = filtered.Where(q => (q.Zone ?? "Unbekannt") == zone);
            }

            // Hauptquest-Filter
            if (onlyMainQuests)
            {
                filtered = filtered.Where(q => q.IsMainStory || q.Category == QuestCategory.Main);
            }

            // Gruppenquest-Filter
            if (onlyGroupQuests)
            {
                filtered = filtered.Where(q => q.IsGroupQuest || q.Category == QuestCategory.Group ||
                                               q.Category == QuestCategory.Dungeon || q.Category == QuestCategory.Raid);
            }

            // Ohne-TTS-Filter: Quests ohne beide Stimmen
            if (onlyWithoutTts)
            {
                // Audio-Index laden fuer praezisere Pruefung
                if (!string.IsNullOrEmpty(_exportSettings.OutputRootPath))
                {
                    var audioIndex = AudioIndexWriter.LoadIndex(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
                    var audioLookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

                    filtered = filtered.Where(q =>
                    {
                        bool hasMale = AudioIndexWriter.IsAlreadyVoiced(audioLookup, q.QuestId, "male");
                        bool hasFemale = AudioIndexWriter.IsAlreadyVoiced(audioLookup, q.QuestId, "female");
                        // Quest fehlt wenn nicht beide Stimmen vorhanden sind
                        return !hasMale || !hasFemale;
                    });
                }
                else
                {
                    // Fallback auf Quest-Flags
                    filtered = filtered.Where(q => !q.HasMaleTts || !q.HasFemaleTts);
                }
            }

            // Sortierung anwenden (Standard-Sortierung: Kategorie -> Zone -> ID)
            var sorted = filtered
                .OrderBy(q => q.LocalizationStatus)
                .ThenByDescending(q => q.IsMainStory)
                .ThenBy(q => q.Category)
                .ThenBy(q => q.Zone ?? "")
                .ThenBy(q => q.QuestId);

            // Limit anwenden
            if (maxQuests > 0)
            {
                sorted = (IOrderedEnumerable<Quest>)sorted.Take(maxQuests).OrderBy(q => q.QuestId);
            }

            // Ergebnis materialisieren
            var resultList = sorted.ToList();

            // FilteredQuests aktualisieren (aber Quests bleibt unveraendert)
            FilteredQuests.Clear();
            foreach (var quest in resultList)
            {
                FilteredQuests.Add(quest);
            }

            // Erste Quest auswaehlen
            if (FilteredQuests.Count > 0)
            {
                SelectedQuest = FilteredQuests[0];
            }
            else
            {
                SelectedQuest = null;
            }

            // Statistik anzeigen
            var zoneText = string.IsNullOrEmpty(zone) ? "Alle Zonen" : zone;
            StatusText.Text = $"Zone '{zoneText}': {FilteredQuests.Count} Quests geladen";

            if (FilteredQuests.Count == 0)
            {
                MessageBox.Show(
                    $"Keine Quests fuer Zone '{zoneText}' mit den angegebenen Filtern gefunden.",
                    "Keine Ergebnisse",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Laedt eine Zone im Hauptfenster (wird vom ProgressWindow aufgerufen).
        /// </summary>
        public void LoadZoneFromProgress(string zoneName)
        {
            // Zone in ComboBox auswaehlen
            for (int i = 0; i < ZoneLoadComboBox.Items.Count; i++)
            {
                if (ZoneLoadComboBox.Items[i] is ComboBoxItem item &&
                    item.Tag?.ToString() == zoneName)
                {
                    ZoneLoadComboBox.SelectedIndex = i;
                    break;
                }
            }

            // Quests fuer diese Zone laden (nur ohne TTS)
            LoadQuestsForZone(zoneName, onlyMainQuests: false, onlyGroupQuests: false, onlyWithoutTts: true, maxQuests: 0);
        }

        #endregion

        #region Progress Window (NEU)

        /// <summary>
        /// Oeffnet das Progress-Fenster mit der Vertonungs-Uebersicht pro Zone.
        /// </summary>
        private void OnShowProgressClick(object sender, RoutedEventArgs e)
        {
            // Pruefen ob Quests geladen sind
            if (Quests.Count == 0)
            {
                MessageBox.Show(
                    "Keine Quests geladen.\n\n" +
                    "Bitte zuerst Quests laden, um den Fortschritt anzuzeigen.",
                    "Keine Daten",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pruefen ob Output-Pfad gesetzt ist
            if (string.IsNullOrEmpty(_exportSettings.OutputRootPath))
            {
                MessageBox.Show(
                    "Kein Ausgabeordner konfiguriert.\n\n" +
                    "Bitte zuerst einen Ausgabeordner waehlen, damit der Fortschritt berechnet werden kann.",
                    "Ausgabeordner fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // ProgressWindow erstellen und oeffnen
                var progressWindow = new ProgressWindow([.. Quests], _exportSettings)
                {
                    Owner = this
                };

                // Event abonnieren: Wenn eine Zone im Hauptfenster geladen werden soll
                progressWindow.LoadZoneInMainRequested += (zoneName) =>
                {
                    LoadZoneFromProgress(zoneName);
                };

                // Fenster als nicht-modal oeffnen (kann parallel zum Hauptfenster verwendet werden)
                progressWindow.Show();

                StatusText.Text = "Fortschrittsfenster geoeffnet";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Oeffnen des Fortschrittsfensters:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Oeffnet das Addon-Export-Fenster.
        /// </summary>
        private void OnAddonExportClick(object sender, RoutedEventArgs e)
        {
            // Pruefen ob Output-Pfad gesetzt ist
            if (string.IsNullOrEmpty(_exportSettings.OutputRootPath))
            {
                MessageBox.Show(
                    "Kein Ausgabeordner konfiguriert.\n\n" +
                    "Bitte zuerst einen Ausgabeordner in den TTS-Export-Einstellungen waehlen.",
                    "Ausgabeordner fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var exportWindow = new AddonExportWindow(_exportSettings)
                {
                    Owner = this
                };

                exportWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Oeffnen des Export-Fensters:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Oeffnet das End-to-End-Test-Fenster fuer Pipeline-Tests.
        /// </summary>
        private void OnEndToEndTestClick(object sender, RoutedEventArgs e)
        {
            // Pruefen ob Output-Pfad gesetzt ist
            if (string.IsNullOrEmpty(_exportSettings.OutputRootPath))
            {
                MessageBox.Show(
                    "Kein Ausgabeordner konfiguriert.\n\n" +
                    "Bitte zuerst einen Ausgabeordner in den TTS-Export-Einstellungen waehlen.",
                    "Ausgabeordner fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Pruefen ob TTS-Service konfiguriert ist
            if (_ttsService == null || !_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    "TTS-Service nicht konfiguriert.\n\n" +
                    "Bitte zuerst API-Key und Voice-IDs in den Einstellungen konfigurieren.",
                    "TTS nicht konfiguriert",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Pruefen ob Quests geladen sind
            if (Quests.Count == 0)
            {
                MessageBox.Show(
                    "Keine Quests geladen.\n\n" +
                    "Bitte lade zuerst Quests (z.B. via 'Von Blizzard laden' oder 'JSON laden').",
                    "Keine Quests",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var testWindow = new EndToEndTestWindow(_ttsService, _exportSettings, Quests)
                {
                    Owner = this
                };

                testWindow.ShowDialog();

                // Nach dem Schliessen: Quest-TTS-Status aktualisieren
                RefreshQuestTtsFlags();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Oeffnen des Test-Fensters:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Oeffnet das Update & Sync Fenster fuer selektive TTS-Generierung.
        /// </summary>
        private void OnUpdateSyncClick(object sender, RoutedEventArgs e)
        {
            // Pruefen ob Output-Pfad gesetzt ist
            if (string.IsNullOrEmpty(_exportSettings.OutputRootPath))
            {
                MessageBox.Show(
                    "Kein Ausgabeordner konfiguriert.\n\n" +
                    "Bitte zuerst einen Ausgabeordner in den TTS-Export-Einstellungen waehlen.",
                    "Ausgabeordner fehlt",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Pruefen ob Quests geladen sind
            if (Quests.Count == 0)
            {
                MessageBox.Show(
                    "Keine Quests geladen.\n\n" +
                    "Bitte lade zuerst Quests (z.B. via 'Von Blizzard laden' oder 'JSON laden').",
                    "Keine Quests",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var updateWindow = new UpdateSyncWindow(Quests, _exportSettings)
                {
                    Owner = this,
                    GenerateTtsForQuestAsync = async (quest, languageCode, ct) =>
                    {
                        return await GenerateTtsForUpdateSyncAsync(quest, ct);
                    },
                    ExportAddonAsync = async (ct) =>
                    {
                        return await ExportAddonInternalAsync(ct);
                    }
                };

                updateWindow.ShowDialog();

                // Nach dem Schliessen: Quest-TTS-Status aktualisieren
                RefreshQuestTtsFlags();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Oeffnen des Update & Sync-Fensters:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Generiert TTS fuer eine einzelne Quest (Male + Female).
        /// Wird vom UpdateSyncWindow aufgerufen.
        /// </summary>
        private async Task<bool> GenerateTtsForUpdateSyncAsync(Quest quest, CancellationToken ct)
        {
            try
            {
                var maleVoiceId = GetMaleVoiceIdForSelectedEngine();
                var femaleVoiceId = GetFemaleVoiceIdForSelectedEngine();

                if (string.IsNullOrEmpty(maleVoiceId) || string.IsNullOrEmpty(femaleVoiceId))
                {
                    return false;
                }

                var ttsText = quest.TtsText;
                if (string.IsNullOrWhiteSpace(ttsText))
                {
                    return false;
                }

                var successCount = 0;

                // Male generieren
                var malePath = _exportSettings.GetMaleOutputPath(quest);
                var maleDir = Path.GetDirectoryName(malePath);
                if (!string.IsNullOrEmpty(maleDir) && !Directory.Exists(maleDir))
                {
                    Directory.CreateDirectory(maleDir);
                }

                var maleAudio = await GenerateAudioWithSelectedEngineAsync(ttsText, "male");
                if (maleAudio != null && maleAudio.Length > 0)
                {
                    await File.WriteAllBytesAsync(malePath, maleAudio, ct);
                    quest.HasMaleTts = true;
                    successCount++;

                    // Audio-Index aktualisieren
                    AudioIndexService.Instance.AddOrUpdateEntry(quest.QuestId, malePath);

                    // Session-Tracker aktualisieren
                    SessionCharCount += ttsText.Length;
                    var (_, tokens, cost) = _exportSettings.CalculateCostEstimate(ttsText.Length, 1);
                    SessionTokenEstimate += tokens;
                    SessionCostEstimate += cost;
                }

                ct.ThrowIfCancellationRequested();

                // Female generieren
                var femalePath = _exportSettings.GetFemaleOutputPath(quest);
                var femaleDir = Path.GetDirectoryName(femalePath);
                if (!string.IsNullOrEmpty(femaleDir) && !Directory.Exists(femaleDir))
                {
                    Directory.CreateDirectory(femaleDir);
                }

                var femaleAudio = await GenerateAudioWithSelectedEngineAsync(ttsText, "female");
                if (femaleAudio != null && femaleAudio.Length > 0)
                {
                    await File.WriteAllBytesAsync(femalePath, femaleAudio, ct);
                    quest.HasFemaleTts = true;
                    successCount++;

                    // Audio-Index aktualisieren
                    AudioIndexService.Instance.AddOrUpdateEntry(quest.QuestId, femalePath);

                    // Session-Tracker aktualisieren
                    SessionCharCount += ttsText.Length;
                    var (_, tokens, cost) = _exportSettings.CalculateCostEstimate(ttsText.Length, 1);
                    SessionTokenEstimate += tokens;
                    SessionCostEstimate += cost;
                }

                // Audio-Index aktualisieren
                if (successCount > 0)
                {
                    var audioIndex = AudioIndexWriter.LoadIndex(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
                    var lookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

                    if (quest.HasMaleTts)
                    {
                        AudioIndexWriter.UpdateEntry(audioIndex, lookup, _exportSettings.OutputRootPath,
                            _exportSettings.LanguageCode, quest, "male", malePath);
                    }

                    if (quest.HasFemaleTts)
                    {
                        AudioIndexWriter.UpdateEntry(audioIndex, lookup, _exportSettings.OutputRootPath,
                            _exportSettings.LanguageCode, quest, "female", femalePath);
                    }

                    AudioIndexWriter.SaveIndex(audioIndex, _exportSettings.OutputRootPath, _exportSettings.LanguageCode);
                }

                quest.HasTtsAudio = quest.HasMaleTts && quest.HasFemaleTts;
                quest.LastTtsGeneratedAt = DateTime.Now;
                quest.ClearTtsError();

                return successCount == 2;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                quest.SetTtsError(ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Fuehrt den Addon-Export intern durch (fuer Update & Sync).
        /// </summary>
        private async Task<bool> ExportAddonInternalAsync(CancellationToken ct)
        {
            try
            {
                var audioIndex = AudioIndexWriter.LoadIndex(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
                if (audioIndex.TotalCount == 0)
                {
                    return false;
                }

                var addonSettings = new AddonSettings
                {
                    AddonName = "QuestVoiceover",
                    AddonVersion = "1.0.0"
                };

                var parentDir = Path.GetDirectoryName(_exportSettings.OutputRootPath);
                var targetPath = !string.IsNullOrEmpty(parentDir)
                    ? Path.Combine(parentDir, "QuestVoiceover_Addon")
                    : Path.Combine(_exportSettings.OutputRootPath, "addon");

                var exportService = new AddonExportService
                {
                    AddonName = addonSettings.AddonName,
                    AddonVersion = addonSettings.AddonVersion,
                    UseExtendedExport = true,
                    Settings = addonSettings
                };

                var result = await exportService.ExportAddonAsync(
                    _exportSettings.OutputRootPath,
                    targetPath,
                    _exportSettings.LanguageCode,
                    null,
                    ct);

                return result.Success;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Aktualisiert die TTS-Flags aller Quests basierend auf dem Dateisystem.
        /// </summary>
        private void RefreshQuestTtsFlags()
        {
            if (string.IsNullOrEmpty(_exportSettings.OutputRootPath))
                return;

            foreach (var quest in Quests)
            {
                quest.UpdateTtsFlagsFromFileSystem(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
            }

            // DataGrid aktualisieren
            QuestDataGrid.Items.Refresh();
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

            // Gesperrt/Completed-Check
            if (!SelectedQuest.IsEditable)
            {
                var reason = SelectedQuest.IsCompleted ? "abgeschlossen" : "gesperrt";
                MessageBox.Show($"Diese Quest ist {reason} und kann nicht bearbeitet werden.\n\nBitte zuerst entsperren.", "Nicht bearbeitbar",
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
                var gender = voiceProfile.Contains("female") ? "female" : "male";
                var audioBytes = await GenerateAudioWithSelectedEngineAsync(text, gender);

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
                    questsToProcess = [.. FilteredQuests];
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
                        var gender = voiceProfile.Contains("female") ? "female" : "male";
                        var audioBytes = await GenerateAudioWithSelectedEngineAsync(text, gender, ct: _batchCancellation?.Token ?? default);

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
            GenerateSingleTtsButton.IsEnabled = !isBatchMode;
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
                var maleAudio = await GenerateAudioWithSelectedEngineAsync(text, "male", ct: _batchCancellation?.Token ?? default);
                await File.WriteAllBytesAsync(malePath, maleAudio);
                SelectedQuest.HasMaleTts = true;
                AudioIndexService.Instance.AddOrUpdateEntry(SelectedQuest.QuestId, malePath);

                // Weibliche Stimme generieren
                StatusText.Text = $"Quest {SelectedQuest.QuestId}: Generiere weibliche Stimme...";
                var femaleAudio = await GenerateAudioWithSelectedEngineAsync(text, "female", ct: _batchCancellation?.Token ?? default);
                await File.WriteAllBytesAsync(femalePath, femaleAudio);
                SelectedQuest.HasFemaleTts = true;
                AudioIndexService.Instance.AddOrUpdateEntry(SelectedQuest.QuestId, femalePath);

                // Legacy-Flag und Zeitstempel aktualisieren
                SelectedQuest.HasTtsAudio = true;
                SelectedQuest.LastTtsGeneratedAt = DateTime.Now;

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
        /// Ermittelt die ausgewaehlte Stimmen-Option aus der ComboBox.
        /// </summary>
        /// <returns>"both", "male" oder "female"</returns>
        private string GetSelectedVoiceOption()
        {
            if (VoiceSelectionCombo.SelectedItem is ComboBoxItem selected)
            {
                return selected.Tag?.ToString() ?? "both";
            }
            return "both";
        }

        /// <summary>
        /// Generiert TTS fuer eine einzelne ausgewaehlte Quest (Button-Handler).
        /// </summary>
        private async void OnGenerateSingleTtsClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswaehlen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ausgabeordner waehlen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    $"TTS-Service '{_ttsService.ProviderName}' ist nicht konfiguriert.\n\n" +
                    "Bitte API-Key in den Einstellungen hinterlegen.",
                    "TTS nicht verfuegbar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var text = SelectedQuest.TtsText;
            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Kein Text fuer TTS vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Ausgewaehlte Stimmen-Option ermitteln
            var voiceOption = GetSelectedVoiceOption();
            bool generateMale = voiceOption == "both" || voiceOption == "male";
            bool generateFemale = voiceOption == "both" || voiceOption == "female";

            // Pruefen ob diese Quest bereits vertont ist (ueber Audio-Index)
            var audioIndex = AudioIndexWriter.LoadIndex(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
            var audioLookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

            bool maleExists = AudioIndexWriter.IsAlreadyVoiced(audioLookup, SelectedQuest.QuestId, "male");
            bool femaleExists = AudioIndexWriter.IsAlreadyVoiced(audioLookup, SelectedQuest.QuestId, "female");

            // Nur relevante existierende Stimmen pruefen
            bool relevantExists = (generateMale && maleExists) || (generateFemale && femaleExists);

            if (relevantExists)
            {
                // Bestehende Vertonungen - Benutzer fragen ob ueberschreiben
                var existingVoices = new List<string>();
                if (generateMale && maleExists) existingVoices.Add("Maennlich");
                if (generateFemale && femaleExists) existingVoices.Add("Weiblich");

                var confirmResult = MessageBox.Show(
                    $"Quest {SelectedQuest.QuestId} hat bereits Audio-Dateien:\n" +
                    $"  {string.Join(", ", existingVoices)}\n\n" +
                    $"Moechtest du diese ueberschreiben?\n\n" +
                    $"(Dies verbraucht API-Tokens)",
                    "Audio bereits vorhanden",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirmResult != MessageBoxResult.Yes)
                {
                    StatusText.Text = "Generierung abgebrochen - Audio bereits vorhanden";
                    return;
                }
            }

            try
            {
                GenerateSingleTtsButton.IsEnabled = false;
                VoiceSelectionCombo.IsEnabled = false;
                TtsProgressPanel.Visibility = Visibility.Visible;
                TtsProgressBar.Value = 0;

                int totalVoices = (generateMale ? 1 : 0) + (generateFemale ? 1 : 0);
                TtsProgressText.Text = $"0/{totalVoices}";

                var result = await GenerateTtsForQuestAsync(SelectedQuest, default, generateMale, generateFemale);

                TtsProgressPanel.Visibility = Visibility.Collapsed;

                if (result.Success)
                {
                    // Audio-Index aktualisieren
                    UpdateAudioIndexForQuest(SelectedQuest);

                    // UI aktualisieren
                    QuestDataGrid.Items.Refresh();
                    CheckExistingAudio();
                    SaveQuestsToCache();

                    var voiceInfo = voiceOption switch
                    {
                        "male" => "M",
                        "female" => "W",
                        _ => "M+W"
                    };
                    StatusText.Text = $"TTS fuer Quest {SelectedQuest.QuestId} generiert ({voiceInfo})";

                    var resultMsg = new List<string>();
                    if (generateMale) resultMsg.Add($"Maennliche Stimme: {(result.MaleGenerated ? "OK" : "Uebersprungen")}");
                    if (generateFemale) resultMsg.Add($"Weibliche Stimme: {(result.FemaleGenerated ? "OK" : "Uebersprungen")}");

                    MessageBox.Show(
                        $"TTS erfolgreich generiert fuer Quest {SelectedQuest.QuestId}!\n\n" +
                        string.Join("\n", resultMsg),
                        "TTS generiert",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusText.Text = $"Fehler: {result.Error}";
                    MessageBox.Show(
                        $"Fehler bei der TTS-Generierung:\n{result.Error}",
                        "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                GenerateSingleTtsButton.IsEnabled = true;
                VoiceSelectionCombo.IsEnabled = true;
                TtsProgressPanel.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Ergebnis der TTS-Generierung fuer eine Quest.
        /// </summary>
        private class TtsGenerationResult
        {
            public bool Success { get; set; }
            public bool MaleGenerated { get; set; }
            public bool FemaleGenerated { get; set; }
            public string? Error { get; set; }
        }

        /// <summary>
        /// Generiert TTS fuer eine einzelne Quest.
        /// Gemeinsame Helper-Methode fuer Einzel- und Batch-Verarbeitung.
        /// </summary>
        /// <param name="quest">Die Quest fuer die TTS generiert werden soll</param>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <param name="generateMale">True = maennliche Stimme generieren</param>
        /// <param name="generateFemale">True = weibliche Stimme generieren</param>
        private async Task<TtsGenerationResult> GenerateTtsForQuestAsync(
            Quest quest,
            CancellationToken cancellationToken = default,
            bool generateMale = true,
            bool generateFemale = true)
        {
            var result = new TtsGenerationResult();

            try
            {
                // Gesperrt/Completed-Check
                if (!quest.IsEditable)
                {
                    result.Error = quest.IsCompleted ? "Quest ist abgeschlossen" : "Quest ist gesperrt";
                    return result;
                }

                var text = quest.TtsText;
                if (string.IsNullOrWhiteSpace(text))
                {
                    result.Error = "Kein Text vorhanden";
                    return result;
                }

                var malePath = _exportSettings.GetMaleOutputPath(quest);
                var femalePath = _exportSettings.GetFemaleOutputPath(quest);

                if (generateMale)
                {
                    var maleFolder = Path.GetDirectoryName(malePath);
                    if (!string.IsNullOrEmpty(maleFolder)) Directory.CreateDirectory(maleFolder);
                }
                if (generateFemale)
                {
                    var femaleFolder = Path.GetDirectoryName(femalePath);
                    if (!string.IsNullOrEmpty(femaleFolder)) Directory.CreateDirectory(femaleFolder);
                }

                int voicesGenerated = 0;
                int totalVoices = (generateMale ? 1 : 0) + (generateFemale ? 1 : 0);
                int currentVoice = 0;

                // Maennliche Stimme
                if (generateMale)
                {
                    currentVoice++;
                    int progressPercent = totalVoices == 1 ? 50 : 25;

                    Dispatcher.Invoke(() =>
                    {
                        TtsProgressBar.Value = progressPercent;
                        TtsProgressText.Text = $"{currentVoice}/{totalVoices} (M)";
                        StatusText.Text = $"Quest {quest.QuestId}: Generiere maennliche Stimme...";
                    });

                    var maleAudio = await GenerateAudioWithSelectedEngineAsync(text, "male", ct: _batchCancellation?.Token ?? default);
                    await File.WriteAllBytesAsync(malePath, maleAudio, cancellationToken);
                    quest.HasMaleTts = true;
                    result.MaleGenerated = true;
                    voicesGenerated++;
                    AudioIndexService.Instance.AddOrUpdateEntry(quest.QuestId, malePath);
                }

                // Weibliche Stimme
                if (generateFemale)
                {
                    currentVoice++;
                    int progressPercent = totalVoices == 1 ? 50 : 75;

                    Dispatcher.Invoke(() =>
                    {
                        TtsProgressBar.Value = progressPercent;
                        TtsProgressText.Text = $"{currentVoice}/{totalVoices} (W)";
                        StatusText.Text = $"Quest {quest.QuestId}: Generiere weibliche Stimme...";
                    });

                    var femaleAudio = await GenerateAudioWithSelectedEngineAsync(text, "female", ct: _batchCancellation?.Token ?? default);
                    await File.WriteAllBytesAsync(femalePath, femaleAudio, cancellationToken);
                    quest.HasFemaleTts = true;
                    result.FemaleGenerated = true;
                    voicesGenerated++;
                    AudioIndexService.Instance.AddOrUpdateEntry(quest.QuestId, femalePath);
                }

                // Zeitstempel und Session-Tracker aktualisieren
                if (voicesGenerated > 0)
                {
                    UpdateSessionTracker(text.Length, voicesGenerated);
                    quest.LastTtsGeneratedAt = DateTime.Now;
                }

                quest.HasTtsAudio = quest.HasMaleTts && quest.HasFemaleTts;
                quest.ClearTtsError();
                result.Success = true;

                Dispatcher.Invoke(() =>
                {
                    TtsProgressBar.Value = 100;
                    TtsProgressText.Text = $"{totalVoices}/{totalVoices}";
                });
            }
            catch (OperationCanceledException)
            {
                result.Error = "Abgebrochen";
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                quest.SetTtsError(ex.Message);
            }

            return result;
        }

        /// <summary>
        /// Aktualisiert den Audio-Index fuer eine einzelne Quest.
        /// </summary>
        private void UpdateAudioIndexForQuest(Quest quest)
        {
            if (string.IsNullOrEmpty(_exportSettings.OutputRootPath))
                return;

            try
            {
                var audioIndex = AudioIndexWriter.LoadIndex(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);

                if (quest.HasMaleTts)
                {
                    var malePath = _exportSettings.GetMaleOutputPath(quest);
                    AudioIndexWriter.RemoveEntry(audioIndex, quest.QuestId, "male");
                    AudioIndexWriter.AddEntry(audioIndex, _exportSettings.OutputRootPath, _exportSettings.LanguageCode, quest, "male", malePath);
                }

                if (quest.HasFemaleTts)
                {
                    var femalePath = _exportSettings.GetFemaleOutputPath(quest);
                    AudioIndexWriter.RemoveEntry(audioIndex, quest.QuestId, "female");
                    AudioIndexWriter.AddEntry(audioIndex, _exportSettings.OutputRootPath, _exportSettings.LanguageCode, quest, "female", femalePath);
                }

                AudioIndexWriter.SaveIndex(audioIndex, _exportSettings.OutputRootPath, _exportSettings.LanguageCode);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Aktualisieren des Audio-Index: {ex.Message}");
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
                    questsToProcess = [.. FilteredQuests];
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
            int failed = 0;
            var errors = new List<string>();

            // Statistik fuer uebersprungene Stimmen (bereits vertont)
            int skippedMale = 0;
            int skippedFemale = 0;

            // Batch-Start loggen
            TtsErrorLogger.Instance.LogBatchStart(questsToProcess.Count);

            // Audio-Index fuer JSON-Export vorbereiten
            var audioIndex = AudioIndexWriter.LoadIndex(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
            var audioLookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

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

                        // Skip-Logik: Pruefen ob bereits im Index vorhanden
                        bool maleAlreadyVoiced = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "male");
                        bool femaleAlreadyVoiced = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "female");

                        // Männliche Stimme - mit Skip-Logik
                        bool shouldGenerateMale = ForceReTtsExisting || !maleAlreadyVoiced || !File.Exists(malePath);
                        if (shouldGenerateMale)
                        {
                            var maleAudio = await GenerateAudioWithSelectedEngineAsync(text, "male", ct: _batchCancellation?.Token ?? default);
                            await File.WriteAllBytesAsync(malePath, maleAudio, _batchCancellation.Token);
                            quest.HasMaleTts = true;
                            voicesGenerated++;

                            // Zum Audio-Index hinzufuegen und Lookup aktualisieren
                            AudioIndexWriter.UpdateEntry(audioIndex, audioLookup, _exportSettings.OutputRootPath, _exportSettings.LanguageCode, quest, "male", malePath);
                        }
                        else
                        {
                            skippedMale++;
                            // HasMaleTts Flag setzen wenn Datei existiert
                            if (File.Exists(malePath))
                                quest.HasMaleTts = true;
                        }

                        // Weibliche Stimme - mit Skip-Logik
                        bool shouldGenerateFemale = ForceReTtsExisting || !femaleAlreadyVoiced || !File.Exists(femalePath);
                        if (shouldGenerateFemale)
                        {
                            var femaleAudio = await GenerateAudioWithSelectedEngineAsync(text, "female", ct: _batchCancellation?.Token ?? default);
                            await File.WriteAllBytesAsync(femalePath, femaleAudio, _batchCancellation.Token);
                            quest.HasFemaleTts = true;
                            voicesGenerated++;

                            // Zum Audio-Index hinzufuegen und Lookup aktualisieren
                            AudioIndexWriter.UpdateEntry(audioIndex, audioLookup, _exportSettings.OutputRootPath, _exportSettings.LanguageCode, quest, "female", femalePath);
                        }
                        else
                        {
                            skippedFemale++;
                            // HasFemaleTts Flag setzen wenn Datei existiert
                            if (File.Exists(femalePath))
                                quest.HasFemaleTts = true;
                        }

                        // Session-Tracker und Zeitstempel aktualisieren
                        if (voicesGenerated > 0)
                        {
                            UpdateSessionTracker(text.Length, voicesGenerated);
                            quest.LastTtsGeneratedAt = DateTime.Now;
                        }

                        quest.HasTtsAudio = quest.HasMaleTts && quest.HasFemaleTts;
                        quest.ClearTtsError(); // Fehler zurücksetzen bei Erfolg
                        successful++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = ex.Message;
                        errors.Add($"Quest {quest.QuestId}: {errorMsg}");
                        quest.SetTtsError(errorMsg);
                        TtsErrorLogger.Instance.LogError(quest.QuestId, quest.Title ?? "", errorMsg);
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

                // Gesamtanzahl uebersprungener Stimmen berechnen
                int totalSkippedVoices = skippedMale + skippedFemale;

                // Batch-Ende loggen
                TtsErrorLogger.Instance.LogBatchEnd(successful, failed, totalSkippedVoices);

                // Audio-Index speichern
                if (successful > 0 && !string.IsNullOrEmpty(_exportSettings.OutputRootPath))
                {
                    try
                    {
                        AudioIndexWriter.SaveIndex(audioIndex, _exportSettings.OutputRootPath, _exportSettings.LanguageCode);
                        StatusText.Text = $"Audio-Index gespeichert: {audioIndex.TotalCount} Eintraege";
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern des Audio-Index: {ex.Message}");
                    }
                }

                SaveQuestsToCache();

                // Detaillierte Statistik erstellen
                var message = $"Batch-Verarbeitung abgeschlossen.\n\n" +
                              $"Erfolgreich: {successful} Quests\n" +
                              $"Fehlgeschlagen: {failed}\n" +
                              $"Gesamt: {processed} von {questsToProcess.Count}\n\n" +
                              $"Uebersprungene Stimmen (bereits vertont):\n" +
                              $"  Maennlich: {skippedMale}\n" +
                              $"  Weiblich: {skippedFemale}\n" +
                              $"  Gesamt: {totalSkippedVoices}";

                if (!ForceReTtsExisting && totalSkippedVoices > 0)
                {
                    message += "\n\nTipp: Aktiviere 'Bereits vertonte neu generieren' um alle Quests neu zu vertonen.";
                }

                if (errors.Count > 0 && errors.Count <= 10)
                {
                    message += "\n\nFehler:\n" + string.Join("\n", errors);
                }
                else if (errors.Count > 10)
                {
                    message += $"\n\n{errors.Count} Fehler aufgetreten.";
                    message += $"\nDetails siehe: {TtsErrorLogger.Instance.CurrentLogFile}";
                }

                MessageBox.Show(message, "Batch-TTS Ergebnis",
                    MessageBoxButton.OK,
                    failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                StatusText.Text = $"Batch fertig: {successful} erfolgreich, {totalSkippedVoices} Stimmen uebersprungen, {failed} fehlgeschlagen";
            }
        }

        /// <summary>
        /// Generiert TTS nur fuer maennliche Stimme (alle gefilterten Quests).
        /// </summary>
        private async void OnBatchMaleTtsClick(object sender, RoutedEventArgs e)
        {
            await RunBatchTtsAsync(generateMale: true, generateFemale: false);
        }

        /// <summary>
        /// Generiert TTS nur fuer weibliche Stimme (alle gefilterten Quests).
        /// </summary>
        private async void OnBatchFemaleTtsClick(object sender, RoutedEventArgs e)
        {
            await RunBatchTtsAsync(generateMale: false, generateFemale: true);
        }

        /// <summary>
        /// Generische Batch-TTS-Methode fuer maennlich, weiblich oder beide.
        /// </summary>
        private async Task RunBatchTtsAsync(bool generateMale, bool generateFemale)
        {
            if (FilteredQuests.Count == 0)
            {
                MessageBox.Show("Keine Quests zum Verarbeiten vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ausgabeordner waehlen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_ttsService.IsConfigured)
            {
                MessageBox.Show(
                    $"TTS-Service '{_ttsService.ProviderName}' ist nicht konfiguriert.\n\n" +
                    "Bitte API-Key in den Einstellungen hinterlegen.",
                    "TTS nicht verfuegbar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Bestimme welche Stimme(n) generiert werden
            string genderLabel = (generateMale, generateFemale) switch
            {
                (true, false) => "maennlich",
                (false, true) => "weiblich",
                _ => "beide"
            };

            // Quests filtern basierend auf gewuenschter Stimme
            var questsToProcess = FilteredQuests
                .Where(q =>
                    (generateMale && !q.HasMaleTts) ||
                    (generateFemale && !q.HasFemaleTts) ||
                    ForceReTtsExisting)
                .ToList();

            if (questsToProcess.Count == 0)
            {
                var result = MessageBox.Show(
                    $"Alle gefilterten Quests haben bereits {genderLabel} Stimme.\n\n" +
                    "Moechtest du die Audio-Dateien neu generieren?",
                    "Alle Quests haben Audio",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    questsToProcess = [.. FilteredQuests];
                }
                else
                {
                    return;
                }
            }

            // Kostenberechnung und Bestaetigung
            int voiceCount = (generateMale ? 1 : 0) + (generateFemale ? 1 : 0);
            if (!ShowForecastDialogAndConfirm(questsToProcess, voiceCount, genderLabel))
            {
                return;
            }

            // UI vorbereiten
            _batchCancellation = new CancellationTokenSource();
            SetBatchModeUI(true);

            int processed = 0;
            int successful = 0;
            int failed = 0;
            int skipped = 0;
            var errors = new List<string>();

            TtsErrorLogger.Instance.LogBatchStart(questsToProcess.Count);

            var audioIndex = AudioIndexWriter.LoadIndex(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
            var audioLookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

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
                    UpdateProgress(i, questsToProcess.Count, $"[{genderLabel.ToUpper()}] Quest {quest.QuestId}: {quest.Title}");

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

                        int voicesGenerated = 0;

                        // Maennliche Stimme
                        if (generateMale)
                        {
                            var malePath = _exportSettings.GetMaleOutputPath(quest);
                            var maleFolder = Path.GetDirectoryName(malePath);
                            if (!string.IsNullOrEmpty(maleFolder)) Directory.CreateDirectory(maleFolder);

                            bool maleAlreadyVoiced = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "male");
                            bool shouldGenerate = ForceReTtsExisting || !maleAlreadyVoiced || !File.Exists(malePath);

                            if (shouldGenerate)
                            {
                                var maleAudio = await GenerateAudioWithSelectedEngineAsync(text, "male", ct: _batchCancellation?.Token ?? default);
                                await File.WriteAllBytesAsync(malePath, maleAudio, _batchCancellation.Token);
                                quest.HasMaleTts = true;
                                voicesGenerated++;
                                AudioIndexWriter.UpdateEntry(audioIndex, audioLookup, _exportSettings.OutputRootPath, _exportSettings.LanguageCode, quest, "male", malePath);
                            }
                            else
                            {
                                skipped++;
                                if (File.Exists(malePath))
                                    quest.HasMaleTts = true;
                            }
                        }

                        // Weibliche Stimme
                        if (generateFemale)
                        {
                            var femalePath = _exportSettings.GetFemaleOutputPath(quest);
                            var femaleFolder = Path.GetDirectoryName(femalePath);
                            if (!string.IsNullOrEmpty(femaleFolder)) Directory.CreateDirectory(femaleFolder);

                            bool femaleAlreadyVoiced = AudioIndexWriter.IsAlreadyVoiced(audioLookup, quest.QuestId, "female");
                            bool shouldGenerate = ForceReTtsExisting || !femaleAlreadyVoiced || !File.Exists(femalePath);

                            if (shouldGenerate)
                            {
                                var femaleAudio = await GenerateAudioWithSelectedEngineAsync(text, "female", ct: _batchCancellation?.Token ?? default);
                                await File.WriteAllBytesAsync(femalePath, femaleAudio, _batchCancellation.Token);
                                quest.HasFemaleTts = true;
                                voicesGenerated++;
                                AudioIndexWriter.UpdateEntry(audioIndex, audioLookup, _exportSettings.OutputRootPath, _exportSettings.LanguageCode, quest, "female", femalePath);
                            }
                            else
                            {
                                skipped++;
                                if (File.Exists(femalePath))
                                    quest.HasFemaleTts = true;
                            }
                        }

                        if (voicesGenerated > 0)
                        {
                            UpdateSessionTracker(text.Length, voicesGenerated);
                            quest.LastTtsGeneratedAt = DateTime.Now;
                        }

                        quest.HasTtsAudio = quest.HasMaleTts && quest.HasFemaleTts;
                        quest.ClearTtsError();
                        successful++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = ex.Message;
                        errors.Add($"Quest {quest.QuestId}: {errorMsg}");
                        quest.SetTtsError(errorMsg);
                        TtsErrorLogger.Instance.LogError(quest.QuestId, quest.Title ?? "", errorMsg);
                        failed++;
                    }

                    processed++;
                    QuestDataGrid.Items.Refresh();

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

                TtsErrorLogger.Instance.LogBatchEnd(successful, failed, skipped);

                if (successful > 0 && !string.IsNullOrEmpty(_exportSettings.OutputRootPath))
                {
                    try
                    {
                        AudioIndexWriter.SaveIndex(audioIndex, _exportSettings.OutputRootPath, _exportSettings.LanguageCode);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern des Audio-Index: {ex.Message}");
                    }
                }

                SaveQuestsToCache();

                var message = $"Batch-TTS ({genderLabel.ToUpper()}) abgeschlossen.\n\n" +
                              $"Erfolgreich: {successful} Quests\n" +
                              $"Uebersprungen: {skipped}\n" +
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

                MessageBox.Show(message, $"Batch-TTS ({genderLabel.ToUpper()}) Ergebnis",
                    MessageBoxButton.OK,
                    failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);

                StatusText.Text = $"Batch ({genderLabel}) fertig: {successful} erfolgreich, {skipped} uebersprungen, {failed} fehlgeschlagen";
            }
        }

        /// <summary>
        /// Wiederholt TTS-Generierung für alle Quests mit Fehlern.
        /// </summary>
        private async void OnRetryErrorsClick(object sender, RoutedEventArgs e)
        {
            // Alle Quests mit Fehlern finden
            var questsWithErrors = Quests.Where(q => q.HasTtsError).ToList();

            if (questsWithErrors.Count == 0)
            {
                MessageBox.Show("Keine Quests mit TTS-Fehlern gefunden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
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

            var result = MessageBox.Show(
                $"Es wurden {questsWithErrors.Count} Quests mit Fehlern gefunden.\n\n" +
                "Möchtest du die TTS-Generierung für diese Quests erneut versuchen?",
                "Fehler erneut versuchen",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            // Fehler zurücksetzen und Batch-Generierung starten
            foreach (var quest in questsWithErrors)
            {
                quest.ClearTtsError();
            }

            // Filter auf fehlerbehaftete Quests setzen und Batch starten
            FilteredQuests.Clear();
            foreach (var quest in questsWithErrors)
            {
                FilteredQuests.Add(quest);
            }

            StatusText.Text = $"Retry: {questsWithErrors.Count} Quests mit Fehlern";

            // Batch-Verarbeitung starten (nutzt FilteredQuests)
            await RunBatchTtsForQuests(questsWithErrors);

            // Filter zurücksetzen
            ApplyFilter();
        }

        /// <summary>
        /// Führt Batch-TTS für eine spezifische Liste von Quests aus.
        /// </summary>
        private async Task RunBatchTtsForQuests(List<Quest> questsToProcess)
        {
            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ausgabeordner wählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _batchCancellation = new CancellationTokenSource();
            SetBatchModeUI(true);

            int processed = 0;
            int successful = 0;
            int skipped = 0;
            int failed = 0;
            var errors = new List<string>();

            TtsErrorLogger.Instance.LogBatchStart(questsToProcess.Count);

            try
            {
                for (int i = 0; i < questsToProcess.Count; i++)
                {
                    if (_batchCancellation.Token.IsCancellationRequested)
                    {
                        break;
                    }

                    var quest = questsToProcess[i];
                    UpdateProgress(i, questsToProcess.Count, $"Retry {quest.QuestId}: {quest.Title}");

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
                            var maleAudio = await GenerateAudioWithSelectedEngineAsync(text, "male", ct: _batchCancellation?.Token ?? default);
                            await File.WriteAllBytesAsync(malePath, maleAudio, _batchCancellation.Token);
                            quest.HasMaleTts = true;
                            voicesGenerated++;
                        }

                        // Weibliche Stimme
                        if (!quest.HasFemaleTts || !File.Exists(femalePath))
                        {
                            var femaleAudio = await GenerateAudioWithSelectedEngineAsync(text, "female", ct: _batchCancellation?.Token ?? default);
                            await File.WriteAllBytesAsync(femalePath, femaleAudio, _batchCancellation.Token);
                            quest.HasFemaleTts = true;
                            voicesGenerated++;
                        }

                        if (voicesGenerated > 0)
                        {
                            UpdateSessionTracker(text.Length, voicesGenerated);
                            quest.LastTtsGeneratedAt = DateTime.Now;
                        }

                        quest.HasTtsAudio = quest.HasMaleTts && quest.HasFemaleTts;
                        quest.ClearTtsError();
                        successful++;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        var errorMsg = ex.Message;
                        errors.Add($"Quest {quest.QuestId}: {errorMsg}");
                        quest.SetTtsError(errorMsg);
                        TtsErrorLogger.Instance.LogError(quest.QuestId, quest.Title ?? "", errorMsg);
                        failed++;
                    }

                    processed++;
                    QuestDataGrid.Items.Refresh();

                    await Task.Delay(100, _batchCancellation.Token);
                }
            }
            catch (OperationCanceledException)
            {
                StatusText.Text = "Retry abgebrochen.";
            }
            finally
            {
                SetBatchModeUI(false);
                _batchCancellation?.Dispose();
                _batchCancellation = null;

                TtsErrorLogger.Instance.LogBatchEnd(successful, failed, skipped);
                SaveQuestsToCache();

                var message = $"Retry abgeschlossen.\n\n" +
                              $"Erfolgreich: {successful}\n" +
                              $"Fehlgeschlagen: {failed}\n" +
                              $"Gesamt: {processed}";

                if (errors.Count > 0)
                {
                    message += errors.Count <= 5
                        ? "\n\nFehler:\n" + string.Join("\n", errors)
                        : $"\n\n{errors.Count} Fehler. Siehe Log: {TtsErrorLogger.Instance.CurrentLogFile}";
                }

                MessageBox.Show(message, "Retry Ergebnis",
                    MessageBoxButton.OK,
                    failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Berechnet die Kosten-Forecast und zeigt einen Bestätigungsdialog.
        /// </summary>
        private bool ShowForecastDialogAndConfirm(List<Quest> quests)
        {
            return ShowForecastDialogAndConfirm(quests, 2, "beide");
        }

        private bool ShowForecastDialogAndConfirm(List<Quest> quests, int voiceCount, string genderLabel)
        {
            long totalChars = quests.Sum(q => (long)(q.TtsText?.Length ?? 0));
            var (effectiveChars, estimatedTokens, estimatedCost) = _exportSettings.CalculateCostEstimate(totalChars, voiceCount);

            // Stimmen-Info aufbauen (von ausgewaehlter Engine)
            var maleVoice = GetMaleVoiceIdForSelectedEngine();
            var femaleVoice = GetFemaleVoiceIdForSelectedEngine();
            string voiceInfo;
            if (voiceCount == 2)
            {
                voiceInfo = $"Maennliche Stimme: {maleVoice}\n" +
                           $"Weibliche Stimme: {femaleVoice}";
            }
            else if (genderLabel == "maennlich")
            {
                voiceInfo = $"Stimme: {maleVoice} (maennlich)";
            }
            else
            {
                voiceInfo = $"Stimme: {femaleVoice} (weiblich)";
            }

            var message = $"Batch-TTS Kostenvorhersage:\n\n" +
                          $"Quests: {quests.Count}\n" +
                          $"Stimme(n): {genderLabel.ToUpper()} ({voiceCount}x)\n" +
                          $"Zeichen (gesamt): {totalChars:N0}\n" +
                          $"Zeichen (effektiv): {effectiveChars:N0}\n" +
                          $"Geschaetzte Tokens: ~{estimatedTokens:N0}\n" +
                          $"Geschaetzte Kosten: ~{estimatedCost:N2} {_exportSettings.CurrencySymbol}\n\n" +
                          $"Provider: {_ttsService.ProviderName}\n" +
                          $"{voiceInfo}\n\n" +
                          $"Force-Modus: {(ForceReTtsExisting ? "Ja (bereits vertonte werden ueberschrieben)" : "Nein (bereits vertonte werden uebersprungen)")}\n\n" +
                          "Fortfahren?";

            var result = MessageBox.Show(message, $"Batch-TTS ({genderLabel.ToUpper()}) starten",
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
        /// Zeigt das TTS-Nutzungsstatistik-Fenster an.
        /// </summary>
        private void OnShowUsageDetailsClick(object sender, RoutedEventArgs e)
        {
            var usageWindow = new TtsUsageWindow
            {
                Owner = this
            };
            usageWindow.ShowDialog();
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

            _searchText = SearchTextBox.Text ?? string.Empty;
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

            // Workflow-Status Filter (WICHTIG: Standard zeigt nur Open + InProgress)
            filtered = _workflowFilter switch
            {
                "active" => filtered.Where(q => q.WorkflowStatus == QuestWorkflowStatus.Open || q.WorkflowStatus == QuestWorkflowStatus.InProgress),
                "open_only" => filtered.Where(q => q.WorkflowStatus == QuestWorkflowStatus.Open),
                "in_progress" => filtered.Where(q => q.WorkflowStatus == QuestWorkflowStatus.InProgress),
                "completed" => filtered.Where(q => q.WorkflowStatus == QuestWorkflowStatus.Completed),
                _ => filtered // "all"
            };

            // Audio-Status Filter (Audio-Index basiert)
            filtered = _voicedFilter switch
            {
                "not_voiced" => filtered.Where(q => !q.HasAudioFromIndex),
                "voiced" => filtered.Where(q => q.HasAudioFromIndex),
                "voiced_complete" => filtered.Where(q => q.IsFullyVoiced),
                "voiced_incomplete" => filtered.Where(q => q.HasAudioFromIndex && !q.IsFullyVoiced),
                _ => filtered // "all"
            };

            // TTS-Status Filter
            filtered = _ttsStatusFilter switch
            {
                "complete" => filtered.Where(q => q.HasMaleTts && q.HasFemaleTts),
                "without" => filtered.Where(q => !q.HasMaleTts && !q.HasFemaleTts),
                "without_male" => filtered.Where(q => !q.HasMaleTts),
                "without_female" => filtered.Where(q => !q.HasFemaleTts),
                "incomplete" => filtered.Where(q => (q.HasMaleTts || q.HasFemaleTts) && !(q.HasMaleTts && q.HasFemaleTts)),
                "not_reviewed" => filtered.Where(q => !q.TtsReviewed && (q.HasMaleTts || q.HasFemaleTts)),
                "reviewed" => filtered.Where(q => q.TtsReviewed),
                "with_errors" => filtered.Where(q => q.HasTtsError),
                _ => filtered // "all"
            };

            // Lokalisierungs-Filter
            filtered = _localizationFilter switch
            {
                "fully_german" => filtered.Where(q => q.LocalizationStatus == QuestLocalizationStatus.FullyGerman),
                "mixed" => filtered.Where(q => q.LocalizationStatus == QuestLocalizationStatus.MixedGermanEnglish),
                "only_english" => filtered.Where(q => q.LocalizationStatus == QuestLocalizationStatus.OnlyEnglish),
                "incomplete" => filtered.Where(q => q.LocalizationStatus == QuestLocalizationStatus.Incomplete),
                "not_incomplete" => filtered.Where(q => q.LocalizationStatus != QuestLocalizationStatus.Incomplete),
                _ => filtered // "all"
            };

            // Text Filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filtered = filtered.Where(q =>
                    (q.Title?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true) ||
                    (q.Description?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true) ||
                    (q.Zone?.Contains(_searchText, StringComparison.OrdinalIgnoreCase) == true) ||
                    q.QuestId.ToString().Contains(_searchText, StringComparison.OrdinalIgnoreCase));
            }

            // Sortierung basierend auf ausgewaehltem Modus anwenden
            // QuestId wird immer als int (numerisch) sortiert, nicht als string!
            var sorted = ApplySortMode(filtered, _sortMode);

            foreach (var quest in sorted)
            {
                FilteredQuests.Add(quest);
            }

            if (FilteredQuests.Count > 0 && (SelectedQuest == null || !FilteredQuests.Contains(SelectedQuest)))
            {
                SelectedQuest = FilteredQuests[0];
            }

            StatusText.Text = $"Filter: {FilteredQuests.Count} von {Quests.Count} Quests";
        }

        /// <summary>
        /// Filtert die Quest-Liste auf nur vertonte Quests.
        /// Wird aufgerufen wenn "Nur vertonte Quests laden" im Projektauswahl-Dialog aktiviert war.
        /// </summary>
        private void FilterToVoicedQuestsOnly()
        {
            // VoicedFilter auf "voiced" setzen
            _voicedFilter = "voiced";

            // ComboBox aktualisieren falls vorhanden
            if (VoicedFilterComboBox != null)
            {
                foreach (ComboBoxItem item in VoicedFilterComboBox.Items)
                {
                    if (item.Tag?.ToString() == "voiced")
                    {
                        VoicedFilterComboBox.SelectedItem = item;
                        break;
                    }
                }
            }

            // Filter anwenden
            ApplyFilter();

            System.Diagnostics.Debug.WriteLine($"FilterToVoicedQuestsOnly: {FilteredQuests.Count} vertonte Quests gefiltert");
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
                _ttsStatusFilter = item.Tag?.ToString() ?? "all";
                ApplyFilter();
            }
        }

        private void LocalizationFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert während InitializeComponent()
            if (FilteredQuests == null) return;

            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem item)
            {
                _localizationFilter = item.Tag?.ToString() ?? "all";
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
                    _ttsStatusFilter = "without";
                    break;

                case "side_no_tts":
                    // Nebenquests ohne TTS
                    _categoryFilter = QuestCategory.Side;
                    _groupQuestFilter = null;
                    _ttsStatusFilter = "without";
                    break;

                case "group_quests":
                    // Alle Gruppenquests
                    _categoryFilter = null;
                    _groupQuestFilter = true;
                    _ttsStatusFilter = "all";
                    break;

                case "all_no_tts":
                    // Alle ohne TTS
                    _categoryFilter = null;
                    _groupQuestFilter = null;
                    _ttsStatusFilter = "without";
                    break;

                case "reset":
                    // Alle Filter zurücksetzen
                    _categoryFilter = null;
                    _groupQuestFilter = null;
                    _ttsStatusFilter = "all";
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
                "complete" => 1,
                "without" => 2,
                "without_male" => 3,
                "without_female" => 4,
                "incomplete" => 5,
                "not_reviewed" => 6,
                "reviewed" => 7,
                "with_errors" => 8,
                _ => 0 // "all"
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
            if (SelectedQuest == null)
            {
                EnableAudioControls(false, false, false);
                return;
            }

            // Male/Female Pfade prüfen
            var malePath = !string.IsNullOrEmpty(_exportSettings.OutputRootPath)
                ? _exportSettings.GetMaleOutputPath(SelectedQuest)
                : null;
            var femalePath = !string.IsNullOrEmpty(_exportSettings.OutputRootPath)
                ? _exportSettings.GetFemaleOutputPath(SelectedQuest)
                : null;

            bool hasMale = !string.IsNullOrEmpty(malePath) && File.Exists(malePath);
            bool hasFemale = !string.IsNullOrEmpty(femalePath) && File.Exists(femalePath);

            // Male/Female Buttons aktivieren
            PlayMaleButton.IsEnabled = hasMale;
            PlayFemaleButton.IsEnabled = hasFemale;

            // Legacy-Pfad für allgemeines Audio
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
                EnableAudioControls(true, hasMale, hasFemale);
                AudioPlayer.Source = new Uri(audioPath);

                // HasTtsAudio aktualisieren falls nicht gesetzt
                if (!SelectedQuest.HasTtsAudio)
                {
                    SelectedQuest.HasTtsAudio = true;
                    SelectedQuest.TtsAudioPath = audioPath;
                }
            }
            else if (hasMale || hasFemale)
            {
                _currentAudioPath = null;
                var status = hasMale && hasFemale ? "M+W vorhanden" :
                             hasMale ? "Nur M vorhanden" : "Nur W vorhanden";
                AudioStatusText.Text = status;
                AudioStatusText.Foreground = new SolidColorBrush(Colors.Green);
                EnableAudioControls(false, hasMale, hasFemale);
            }
            else
            {
                _currentAudioPath = null;
                AudioStatusText.Text = "Kein Audio vorhanden";
                AudioStatusText.Foreground = new SolidColorBrush(Colors.Gray);
                EnableAudioControls(false, false, false);
            }
        }

        private void EnableAudioControls(bool enabled, bool hasMale = false, bool hasFemale = false)
        {
            PlayButton.IsEnabled = enabled;
            PauseButton.IsEnabled = enabled;
            StopButton.IsEnabled = enabled;
            DownloadButton.IsEnabled = enabled;
            PlayMaleButton.IsEnabled = hasMale;
            PlayFemaleButton.IsEnabled = hasFemale;
        }

        /// <summary>
        /// Aktualisiert die TTS-Vorschau-Informationen (Zeichenzahl, Custom-Indikator).
        /// </summary>
        private void UpdateTtsPreviewInfo()
        {
            if (SelectedQuest == null)
            {
                TtsCharCountText.Text = "";
                CustomTtsIndicator.Text = "";
                UpdateCompletionTextUI(null);
                return;
            }

            var ttsText = SelectedQuest.TtsText;
            var charCount = ttsText?.Length ?? 0;
            var tokenEstimate = (int)Math.Ceiling(charCount / _exportSettings.AvgCharsPerToken);
            var costEstimate = tokenEstimate / 1000.0m * _exportSettings.CostPer1kTokens;

            TtsCharCountText.Text = $"{charCount:N0} Zeichen | ~{tokenEstimate:N0} Tokens | ~{costEstimate:N3} {_exportSettings.CurrencySymbol}";

            // Custom-TTS-Indikator
            CustomTtsIndicator.Text = SelectedQuest.HasCustomTtsText ? "(Custom-Text aktiv)" : "";

            // CompletionText-UI aktualisieren
            UpdateCompletionTextUI(SelectedQuest);
        }

        /// <summary>
        /// Aktualisiert die CompletionText-UI Elemente basierend auf der Quest.
        /// </summary>
        private void UpdateCompletionTextUI(Quest? quest)
        {
            if (quest == null)
            {
                CompletionTextSourceLabel.Text = "";
                CompletionTextOverrideIndicator.Visibility = Visibility.Collapsed;
                ResetCompletionTextButton.IsEnabled = false;
                OriginalCompletionTextPreview.Text = "";
                return;
            }

            // Quelle anzeigen
            CompletionTextSourceLabel.Text = quest.CompletionTextSource switch
            {
                QuestTextSource.AzerothCore => "(Quelle: AzerothCore)",
                QuestTextSource.Blizzard => "(Quelle: Blizzard)",
                QuestTextSource.Manual => "(Manuell)",
                QuestTextSource.AiCorrected => "(KI-korrigiert)",
                _ => quest.HasCompletionText ? "(Quelle: unbekannt)" : "(Kein Belohnungstext)"
            };

            // Ueberschrieben-Indikator
            CompletionTextOverrideIndicator.Visibility = quest.IsCompletionTextOverridden
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Reset-Button aktivieren wenn ueberschrieben
            ResetCompletionTextButton.IsEnabled = quest.IsCompletionTextOverridden
                && !string.IsNullOrWhiteSpace(quest.OriginalCompletionText);

            // Original-Text Vorschau
            if (quest.IsCompletionTextOverridden && !string.IsNullOrWhiteSpace(quest.OriginalCompletionText))
            {
                var preview = quest.OriginalCompletionText.Length > 50
                    ? quest.OriginalCompletionText.Substring(0, 50) + "..."
                    : quest.OriginalCompletionText;
                OriginalCompletionTextPreview.Text = $"Original: {preview}";
            }
            else
            {
                OriginalCompletionTextPreview.Text = "";
            }
        }

        private void OnPlayMaleClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null) return;
            PlayQuestAudioPreview(SelectedQuest, "male");
        }

        private void OnPlayFemaleClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null) return;
            PlayQuestAudioPreview(SelectedQuest, "female");
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

        #region Audio Preview Service

        /// <summary>
        /// Event-Handler fuer Status-Aenderungen des AudioPreviewService.
        /// </summary>
        private void AudioPreviewService_StatusChanged(object? sender, AudioPreviewStatusEventArgs e)
        {
            // UI-Update muss im UI-Thread erfolgen
            Dispatcher.Invoke(() =>
            {
                AudioPreviewStatusText.Text = e.Message;

                // Farbe je nach Status setzen
                AudioPreviewStatusText.Foreground = e.Status switch
                {
                    AudioPreviewStatus.Playing => new SolidColorBrush(Color.FromRgb(76, 175, 80)),  // Gruen
                    AudioPreviewStatus.Error => new SolidColorBrush(Colors.Red),
                    AudioPreviewStatus.Stopped => new SolidColorBrush(Colors.Gray),
                    AudioPreviewStatus.Ended => new SolidColorBrush(Colors.Gray),
                    _ => new SolidColorBrush(Colors.Black)
                };

                // Detail-Text aktualisieren
                if (e.Status == AudioPreviewStatus.Playing && _audioPreviewService.CurrentQuestId > 0)
                {
                    AudioPreviewDetailText.Text = $"Quest-ID: {_audioPreviewService.CurrentQuestId} | Stimme: {AudioPreviewService.GetGenderDisplayName(_audioPreviewService.CurrentGender)}";
                }
                else
                {
                    AudioPreviewDetailText.Text = "";
                }
            });
        }

        /// <summary>
        /// Event-Handler wenn Wiedergabe beendet ist.
        /// </summary>
        private void AudioPreviewService_PlaybackEnded(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                AudioPreviewDetailText.Text = "";
            });
        }

        /// <summary>
        /// Spielt Audio-Preview fuer eine Quest ab.
        /// </summary>
        private void PlayQuestAudioPreview(Quest quest, string gender)
        {
            if (quest == null)
            {
                MessageBox.Show("Keine Quest ausgewaehlt.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_exportSettings.OutputRootPath))
            {
                MessageBox.Show("Bitte zuerst einen Ausgabeordner fuer TTS konfigurieren.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Lautstaerke vom Slider uebernehmen
            _audioPreviewService.Volume = VolumeSlider.Value;

            // Preview starten
            var result = _audioPreviewService.PlayPreview(
                quest,
                gender,
                _exportSettings.OutputRootPath,
                _exportSettings.LanguageCode);

            if (!result.IsSuccess)
            {
                MessageBox.Show(result.ErrorMessage, "Audio-Preview",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                // Aktuellen Pfad merken fuer Download-Funktion
                _currentAudioPath = result.FilePath;
                EnableAudioControls(true, quest.HasMaleTts, quest.HasFemaleTts);
            }
        }

        /// <summary>
        /// Play-Button im DataGrid fuer maennliche Stimme.
        /// </summary>
        private void OnGridPlayMaleClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Quest quest)
            {
                // Quest auswaehlen und abspielen
                SelectedQuest = quest;
                PlayQuestAudioPreview(quest, "male");
            }
        }

        /// <summary>
        /// Play-Button im DataGrid fuer weibliche Stimme.
        /// </summary>
        private void OnGridPlayFemaleClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Quest quest)
            {
                // Quest auswaehlen und abspielen
                SelectedQuest = quest;
                PlayQuestAudioPreview(quest, "female");
            }
        }

        /// <summary>
        /// Stop-Button fuer Audio-Preview.
        /// </summary>
        private void OnStopPreviewClick(object sender, RoutedEventArgs e)
        {
            _audioPreviewService.StopPreview();
            AudioPlayer.Stop();
            AudioStatusText.Text = "";
        }

        /// <summary>
        /// Volume-Slider Aenderung.
        /// </summary>
        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_audioPreviewService != null)
            {
                _audioPreviewService.Volume = e.NewValue;
            }

            // Auch MediaElement Lautstaerke anpassen
            if (AudioPlayer != null)
            {
                AudioPlayer.Volume = e.NewValue;
            }
        }

        /// <summary>
        /// MediaElement Fehler-Handler.
        /// </summary>
        private void AudioPlayer_MediaFailed(object sender, ExceptionRoutedEventArgs e)
        {
            AudioPreviewStatusText.Text = $"Wiedergabe-Fehler: {e.ErrorException?.Message ?? "Unbekannter Fehler"}";
            AudioPreviewStatusText.Foreground = new SolidColorBrush(Colors.Red);
            AudioStatusText.Text = "Fehler";
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

            if (!SelectedQuest.IsEditable)
            {
                var reason = SelectedQuest.IsCompleted ? "abgeschlossen" : "gesperrt";
                MessageBox.Show($"Diese Quest ist {reason} und kann nicht bearbeitet werden.", "Nicht bearbeitbar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedQuest.TtsReviewed = true;
            QuestDataGrid.Items.Refresh();
            StatusText.Text = $"Quest {SelectedQuest.QuestId} als geprüft markiert";
        }

        /// <summary>
        /// Markiert die ausgewaehlte Quest als erledigt und sperrt sie.
        /// </summary>
        private void OnMarkCompletedClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (SelectedQuest.IsCompleted)
            {
                MessageBox.Show("Diese Quest ist bereits als erledigt markiert.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Bestaetigung anfordern
            var result = MessageBox.Show(
                $"Quest '{SelectedQuest.Title}' als erledigt markieren?\n\n" +
                "Die Quest wird gesperrt und kann nicht mehr bearbeitet werden.",
                "Quest abschliessen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            SelectedQuest.MarkAsCompleted();
            QuestDataGrid.Items.Refresh();
            UpdateLockButtonVisibility();
            StatusText.Text = $"Quest {SelectedQuest.QuestId} als erledigt markiert und gesperrt";

            // Projekt speichern
            SaveQuestProgress();
        }

        /// <summary>
        /// Entsperrt eine gesperrte Quest.
        /// </summary>
        private void OnUnlockQuestClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswählen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!SelectedQuest.IsLocked)
            {
                MessageBox.Show("Diese Quest ist nicht gesperrt.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Bestaetigung anfordern
            var result = MessageBox.Show(
                $"Quest '{SelectedQuest.Title}' entsperren?\n\n" +
                "Der Erledigt-Status bleibt erhalten, aber die Quest kann wieder bearbeitet werden.",
                "Quest entsperren",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return;

            SelectedQuest.Unlock();
            QuestDataGrid.Items.Refresh();
            UpdateLockButtonVisibility();
            StatusText.Text = $"Quest {SelectedQuest.QuestId} entsperrt";

            // Projekt speichern
            SaveQuestProgress();
        }

        /// <summary>
        /// Aktualisiert die Sichtbarkeit der Sperr-Buttons basierend auf der ausgewaehlten Quest.
        /// </summary>
        private void UpdateLockButtonVisibility()
        {
            if (SelectedQuest == null)
            {
                MarkCompletedButton.Visibility = Visibility.Visible;
                UnlockButton.Visibility = Visibility.Collapsed;
                return;
            }

            if (SelectedQuest.IsLocked)
            {
                MarkCompletedButton.Visibility = Visibility.Collapsed;
                UnlockButton.Visibility = Visibility.Visible;
            }
            else
            {
                MarkCompletedButton.Visibility = Visibility.Visible;
                UnlockButton.Visibility = Visibility.Collapsed;
            }
        }

        /// <summary>
        /// Speichert den Quest-Fortschritt.
        /// </summary>
        private void SaveQuestProgress()
        {
            try
            {
                ProjectService.Instance.UpdateQuests(Quests);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern des Fortschritts: {ex.Message}");
            }
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

        /// <summary>
        /// Markiert die ausgewaehlte Quest als Hauptquest.
        /// </summary>
        private void OnMarkAsMainQuestClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswaehlen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedQuest.IsMainStory = true;
            SelectedQuest.Category = QuestCategory.Main;
            QuestDataGrid.Items.Refresh();
            SaveQuestsToCache();
            StatusText.Text = $"Quest {SelectedQuest.QuestId} als Hauptquest markiert";
        }

        /// <summary>
        /// Markiert die ausgewaehlte Quest als Nebenquest.
        /// </summary>
        private void OnMarkAsSideQuestClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswaehlen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SelectedQuest.IsMainStory = false;
            SelectedQuest.Category = QuestCategory.Side;
            QuestDataGrid.Items.Refresh();
            SaveQuestsToCache();
            StatusText.Text = $"Quest {SelectedQuest.QuestId} als Nebenquest markiert";
        }

        #endregion

        #region Sorting

        /// <summary>
        /// Wendet den ausgewaehlten Sortiermodus auf die Quests an.
        /// WICHTIG: QuestId wird immer als int sortiert (numerisch), nicht als string!
        /// </summary>
        /// <param name="quests">Zu sortierende Quests</param>
        /// <param name="sortMode">Sortier-Modus</param>
        /// <returns>Sortierte Quests</returns>
        private static IOrderedEnumerable<Quest> ApplySortMode(IEnumerable<Quest> quests, string sortMode)
        {
            return sortMode switch
            {
                // Nur Quest-ID (numerisch aufsteigend)
                "id_only" => quests.OrderBy(q => q.QuestId),

                // Zone + ID (alphabetisch nach Zone, dann numerisch nach ID)
                "zone_id" => quests
                    .OrderBy(q => q.Zone ?? "")
                    .ThenBy(q => q.QuestId),

                // Kategorie + ID (Kategorie, dann numerisch nach ID)
                "category_id" => quests
                    .OrderBy(q => q.Category)
                    .ThenBy(q => q.QuestId),

                // Lokalisierung + ID (LocalizationStatus, dann numerisch nach ID)
                "localization_id" => quests
                    .OrderBy(q => q.LocalizationStatus)
                    .ThenBy(q => q.QuestId),

                // Standard: LocalizationStatus → IsMainStory → Kategorie → Zone → QuestId
                // FullyGerman (0) kommt zuerst, dann Mixed, dann OnlyEnglish, dann Incomplete
                // Hauptquests (IsMainStory=true) werden vor Nebenquests angezeigt
                _ => quests
                    .OrderBy(q => q.LocalizationStatus)
                    .ThenByDescending(q => q.IsMainStory)
                    .ThenBy(q => q.Category)
                    .ThenBy(q => q.Zone ?? "")
                    .ThenBy(q => q.QuestId)
            };
        }

        /// <summary>
        /// Event-Handler fuer Aenderung des Sortier-Modus.
        /// </summary>
        private void SortModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert
            if (FilteredQuests == null || SortModeComboBox == null) return;

            if (SortModeComboBox.SelectedItem is ComboBoxItem item)
            {
                _sortMode = item.Tag?.ToString() ?? "standard";
                ApplyFilter();
            }
        }

        #endregion

        #region Text Overrides

        /// <summary>
        /// Speichert alle Text-Aenderungen als Overrides in quest_overrides.json.
        /// </summary>
        private void OnSaveOverridesClick(object sender, RoutedEventArgs e)
        {
            try
            {
                // Alle Quests durchgehen und Overrides aktualisieren
                foreach (var quest in Quests)
                {
                    QuestTextOverridesStore.SetOverride(_textOverrides, quest);
                }

                // Speichern
                QuestTextOverridesStore.Save(_textOverrides);

                StatusText.Text = $"Text-Overrides gespeichert ({_textOverrides.Overrides.Count} Eintraege)";
                MessageBox.Show(
                    $"Text-Aenderungen erfolgreich gespeichert!\n\n" +
                    $"Datei: {QuestTextOverridesStore.GetDefaultPath()}\n" +
                    $"Anzahl: {_textOverrides.Overrides.Count} Overrides",
                    "Gespeichert",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Speichern der Overrides:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Loescht den Override fuer die ausgewaehlte Quest.
        /// </summary>
        private void OnResetOverrideClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswaehlen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Override fuer Quest {SelectedQuest.QuestId} loeschen?\n\n" +
                "Die originalen Texte aus DB/API werden beim naechsten Laden wiederhergestellt.",
                "Override loeschen",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            if (QuestTextOverridesStore.RemoveOverride(_textOverrides, SelectedQuest.QuestId))
            {
                QuestTextOverridesStore.Save(_textOverrides);
                StatusText.Text = $"Override fuer Quest {SelectedQuest.QuestId} geloescht";
            }
            else
            {
                StatusText.Text = $"Kein Override fuer Quest {SelectedQuest.QuestId} vorhanden";
            }
        }

        #endregion

        #region CompletionText (Belohnungstext)

        /// <summary>
        /// Setzt den CompletionText auf das Original zurueck.
        /// </summary>
        private void OnResetCompletionTextClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswaehlen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!SelectedQuest.IsCompletionTextOverridden)
            {
                MessageBox.Show("Der Belohnungstext wurde nicht ueberschrieben.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Belohnungstext auf Original zuruecksetzen?\n\n" +
                $"Original: {SelectedQuest.OriginalCompletionText?.Substring(0, Math.Min(100, SelectedQuest.OriginalCompletionText?.Length ?? 0))}...",
                "Auf Original zuruecksetzen",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            SelectedQuest.ResetCompletionTextToOriginal();
            UpdateCompletionTextUI(SelectedQuest);
            QuestDataGrid.Items.Refresh();
            StatusText.Text = $"Belohnungstext fuer Quest {SelectedQuest.QuestId} auf Original zurueckgesetzt";
        }

        /// <summary>
        /// Korrigiert den CompletionText mit KI (Grammatik, Stil).
        /// </summary>
        private async void OnCorrectCompletionTextClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show("Bitte zuerst eine Quest auswaehlen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var currentText = SelectedQuest.EffectiveCompletionText;
            if (string.IsNullOrWhiteSpace(currentText))
            {
                MessageBox.Show("Kein Belohnungstext vorhanden.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pruefen ob LLM-Service verfuegbar
            if (_llmEnhancerService == null || !_llmEnhancerService.IsConfigured)
            {
                // Manueller Modus: Prompt in Zwischenablage kopieren
                var prompt = BuildCompletionTextCorrectionPrompt(currentText);
                try
                {
                    Clipboard.SetText(prompt);
                    MessageBox.Show(
                        "KI-Korrektur-Prompt wurde in die Zwischenablage kopiert!\n\n" +
                        "Fuege ihn in ChatGPT, Claude oder Gemini ein und kopiere das Ergebnis zurueck.",
                        "Prompt kopiert",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Kopieren: {ex.Message}", "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return;
            }

            // API-Modus
            try
            {
                CorrectCompletionTextButton.IsEnabled = false;
                StatusText.Text = "KI-Korrektur laeuft...";

                var correctedText = await CorrectCompletionTextWithAiAsync(currentText);

                if (!string.IsNullOrWhiteSpace(correctedText) && correctedText != currentText)
                {
                    SelectedQuest.OverrideCompletionText(correctedText, isAiCorrected: true);
                    UpdateCompletionTextUI(SelectedQuest);
                    QuestDataGrid.Items.Refresh();
                    StatusText.Text = $"Belohnungstext fuer Quest {SelectedQuest.QuestId} KI-korrigiert";
                }
                else
                {
                    StatusText.Text = "Keine Aenderungen durch KI-Korrektur";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler bei KI-Korrektur:\n{ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "KI-Korrektur fehlgeschlagen";
            }
            finally
            {
                CorrectCompletionTextButton.IsEnabled = true;
            }
        }

        /// <summary>
        /// Erstellt den Prompt fuer die CompletionText-Korrektur.
        /// </summary>
        private string BuildCompletionTextCorrectionPrompt(string text)
        {
            return $@"Korrigiere den folgenden deutschen Quest-Belohnungstext fuer ein Hoerbuch:
- Behebe Grammatik- und Rechtschreibfehler
- Verbessere Stil und Lesbarkeit
- Behalte die urspruengliche Bedeutung bei
- Entferne unpassende Sonderzeichen
- Schreibe in natuerlichem, fliessenden Deutsch

Original-Text:
{text}

Korrigierter Text:";
        }

        /// <summary>
        /// Korrigiert den Text mit dem konfigurierten LLM-Service.
        /// </summary>
        private async Task<string> CorrectCompletionTextWithAiAsync(string text)
        {
            if (_llmEnhancerService == null || !_llmEnhancerService.IsConfigured)
                throw new InvalidOperationException("LLM-Service nicht konfiguriert");

            // EnhanceTextAsync erwartet questTitle und originalText
            var result = await _llmEnhancerService.EnhanceTextAsync(
                SelectedQuest?.Title ?? "Belohnungstext",
                text,
                "Korrigiere den Belohnungstext fuer ein Hoerbuch");

            if (result.IsSuccess && !string.IsNullOrWhiteSpace(result.EnhancedText))
            {
                return result.EnhancedText;
            }

            // Fallback: Original zurueckgeben
            return text;
        }

        #endregion

        #region Auto-Classification

        /// <summary>
        /// Fuehrt die automatische Klassifizierung aller Quests durch.
        /// </summary>
        private void OnAutoClassifyClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Alle Quests automatisch klassifizieren?\n\n" +
                "Dies setzt IsMainStory, IsGroupQuest und Category basierend auf:\n" +
                "- Quest-Typ (Group, Dungeon, Raid, etc.)\n" +
                "- SuggestedPartySize\n" +
                "- Titel-Hinweise\n\n" +
                "Bestehende manuelle Markierungen werden ueberschrieben!",
                "Auto-Klassifizierung",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            try
            {
                // Kategorie zuruecksetzen, damit Klassifizierung greifen kann
                foreach (var quest in Quests)
                {
                    quest.Category = QuestCategory.Unknown;
                }

                // Klassifizierung durchfuehren (ohne Meta-Daten, nur Quest-Daten)
                var count = QuestClassificationService.ClassifyAll(Quests);

                // UI aktualisieren
                QuestDataGrid.Items.Refresh();
                ApplyFilter();
                SaveQuestsToCache();

                // Statistik anzeigen
                var mainCount = Quests.Count(q => q.IsMainStory);
                var groupCount = Quests.Count(q => q.IsGroupQuest);
                var sideCount = Quests.Count(q => !q.IsMainStory && !q.IsGroupQuest);

                MessageBox.Show(
                    $"Auto-Klassifizierung abgeschlossen!\n\n" +
                    $"Verarbeitet: {count} Quests\n" +
                    $"Hauptquests: {mainCount}\n" +
                    $"Gruppenquests: {groupCount}\n" +
                    $"Nebenquests: {sideCount}",
                    "Klassifizierung",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                StatusText.Text = $"Auto-Klassifizierung: {mainCount} Main, {groupCount} Grp, {sideCount} Side";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler bei der Klassifizierung:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
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

        /// <summary>
        /// Speichert das Projekt manuell (Button-Click).
        /// </summary>
        private void OnSaveProjectClick(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveProject();
                StatusText.Text = $"Projekt gespeichert: {ProjectService.Instance.ProjectFilePath}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Speichern des Projekts:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Importiert Quest-Texte aus einer CSV-Datei.
        /// </summary>
        private void OnCsvImportClick(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "CSV-Datei mit Quest-Texten auswählen",
                Filter = "CSV-Dateien (*.csv)|*.csv|Alle Dateien (*.*)|*.*",
                DefaultExt = ".csv"
            };

            if (dialog.ShowDialog() != true)
                return;

            try
            {
                var result = QuestCsvImportService.ImportFromCsv(dialog.FileName, Quests);

                if (result.HasErrors)
                {
                    MessageBox.Show(
                        $"Import fehlgeschlagen:\n\n{string.Join("\n", result.Errors)}",
                        "Import-Fehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // Erfolgreicher Import
                var message = result.Summary;
                if (result.Warnings.Count > 0)
                {
                    message += $"\n\nWarnungen ({result.Warnings.Count}):\n" +
                               string.Join("\n", result.Warnings.Take(10));
                    if (result.Warnings.Count > 10)
                        message += $"\n... und {result.Warnings.Count - 10} weitere";
                }

                MessageBox.Show(message, "Import abgeschlossen", MessageBoxButton.OK, MessageBoxImage.Information);

                // UI aktualisieren
                ApplyFilter();
                OnPropertyChanged(nameof(SelectedQuest));

                // Projekt speichern
                SaveProject();
                StatusText.Text = $"CSV-Import: {result.UpdatedQuests} Quests aktualisiert";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Import:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region Export

        private void OnExportFilteredClick(object sender, RoutedEventArgs e)
        {
            ExportQuests([.. FilteredQuests], "gefilterte_quests_export.json");
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

                var options = s_jsonOptions;
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

        private static void ExportToJson(List<Quest> quests, string path, bool includeTitle)
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

            var options = s_jsonOptions;
            var json = JsonSerializer.Serialize(exportData, options);
            File.WriteAllText(path, json);
        }

        private static void ExportToCsv(List<Quest> quests, string path, bool includeTitle)
        {
            // CSV-Header mit allen relevanten Spalten fuer TTS-Pipeline (Gemini + ElevenLabs)
            var lines = new List<string>
            {
                "quest_id,zone,is_main_story,category,title,description,objectives,completion,tts_text,voice_gender,tts_reviewed,has_tts_audio"
            };

            foreach (var q in quests)
            {
                var ttsText = includeTitle ? q.TtsText : q.Description;
                // voice_gender: Standardwert "male", kann spaeter pro Quest angepasst werden
                var voiceGender = "male";

                lines.Add(string.Join(",",
                    q.QuestId,
                    EscapeCsv(q.Zone),
                    q.IsMainStory,
                    EscapeCsv(q.CategoryShortName),
                    EscapeCsv(q.Title),
                    EscapeCsv(q.Description),
                    EscapeCsv(q.Objectives),
                    EscapeCsv(q.Completion),
                    EscapeCsv(ttsText),
                    voiceGender,
                    q.TtsReviewed,
                    q.HasTtsAudio
                ));
            }

            File.WriteAllLines(path, lines, System.Text.Encoding.UTF8);
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

        #region AzerothCore Database Import

        /// <summary>
        /// Importiert Quest-Texte (Objectives, Completion) aus der AzerothCore MySQL-Datenbank.
        /// </summary>
        private async Task<int> ImportQuestTextsFromDatabaseAsync()
        {
            var importer = new QuestDbImporter(_questDbConfig);
            var texts = await importer.LoadQuestTextsAsync();

            var updatedCount = QuestMergeHelper.MergePrivateTextsIntoQuests(Quests, texts);

            // UI aktualisieren
            QuestsView?.Refresh();
            QuestDataGrid.Items.Refresh();

            // Cache speichern
            SaveQuestsToCache();
            SaveProject();

            return updatedCount;
        }

        /// <summary>
        /// Event-Handler fuer den DB-Import Button.
        /// </summary>
        private async void OnImportQuestTextsClick(object sender, RoutedEventArgs e)
        {
            // Passwort aus PasswordBox uebernehmen
            if (FindName("DbPasswordBox") is System.Windows.Controls.PasswordBox passwordBox)
            {
                _questDbConfig.Password = passwordBox.Password;
            }

            // Validierung
            if (string.IsNullOrWhiteSpace(_questDbConfig.Host))
            {
                MessageBox.Show(this,
                    "Bitte einen Host eingeben.",
                    "Validierung",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (Quests.Count == 0)
            {
                MessageBox.Show(this,
                    "Keine Quests geladen. Bitte zuerst Quests von Blizzard oder JSON laden.",
                    "Keine Quests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                StatusText.Text = "Verbinde mit MySQL-Datenbank...";

                // Verbindung testen
                var importer = new QuestDbImporter(_questDbConfig);
                var canConnect = await importer.TestConnectionAsync();

                if (!canConnect)
                {
                    MessageBox.Show(this,
                        "Verbindung zur Datenbank fehlgeschlagen.\n\n" +
                        "Bitte Host, Port, Benutzer und Passwort pruefen.",
                        "Verbindungsfehler",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    StatusText.Text = "Datenbankverbindung fehlgeschlagen.";
                    return;
                }

                StatusText.Text = "Lade Quest-Texte aus Datenbank...";

                // Texte laden
                var texts = await importer.LoadQuestTextsAsync();

                // Statistik vor dem Merge
                var (totalMatches, withObjectives, withCompletion) =
                    QuestMergeHelper.CountPotentialUpdates(Quests, texts);

                StatusText.Text = $"Merge: {totalMatches} Quests gefunden...";

                // Merge durchfuehren
                var updatedCount = QuestMergeHelper.MergePrivateTextsIntoQuests(Quests, texts);

                // UI aktualisieren
                QuestsView?.Refresh();
                QuestDataGrid.Items.Refresh();
                OnPropertyChanged(nameof(SelectedQuest));

                // Cache speichern
                SaveQuestsToCache();
                SaveProject();

                StatusText.Text = $"Import abgeschlossen: {updatedCount} Quests aktualisiert.";

                MessageBox.Show(this,
                    $"Quest-Texte erfolgreich aus der Datenbank importiert.\n\n" +
                    $"Gefundene Quests in DB: {texts.Count}\n" +
                    $"Matches mit geladenen Quests: {totalMatches}\n" +
                    $"Mit Objectives: {withObjectives}\n" +
                    $"Mit Completion: {withCompletion}\n\n" +
                    $"Aktualisierte Quests: {updatedCount}",
                    "Import abgeschlossen",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (MySqlConnector.MySqlException ex)
            {
                StatusText.Text = "MySQL-Fehler.";
                MessageBox.Show(this,
                    $"MySQL-Fehler:\n{ex.Message}\n\n" +
                    $"Error Code: {ex.ErrorCode}",
                    "Datenbankfehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                StatusText.Text = "Fehler beim Import.";
                MessageBox.Show(this,
                    $"Fehler beim Import der Quest-Texte:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Testet die Datenbankverbindung.
        /// </summary>
        private async void OnTestDbConnectionClick(object sender, RoutedEventArgs e)
        {
            // Passwort aus PasswordBox uebernehmen
            if (FindName("DbPasswordBox") is System.Windows.Controls.PasswordBox passwordBox)
            {
                _questDbConfig.Password = passwordBox.Password;
            }

            try
            {
                StatusText.Text = "Teste Datenbankverbindung...";

                var importer = new QuestDbImporter(_questDbConfig);
                var canConnect = await importer.TestConnectionAsync();

                if (canConnect)
                {
                    var questCount = await importer.GetQuestCountAsync();
                    StatusText.Text = $"Verbindung OK. {questCount} Quests in der DB.";
                    MessageBox.Show(this,
                        $"Verbindung erfolgreich!\n\n" +
                        $"Datenbank: {_questDbConfig.Database}\n" +
                        $"Quests in DB: {questCount}",
                        "Verbindungstest",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    StatusText.Text = "Verbindung fehlgeschlagen.";
                    MessageBox.Show(this,
                        "Verbindung zur Datenbank fehlgeschlagen.\n\n" +
                        "Bitte Host, Port, Benutzer und Passwort pruefen.",
                        "Verbindungstest",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = "Verbindungsfehler.";
                MessageBox.Show(this,
                    $"Verbindungsfehler:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        #endregion

        #region LLM Text Enhancement (Hoerbuch-Optimierung)

        /// <summary>
        /// Initialisiert den LLM Text Enhancer Service.
        /// </summary>
        private void InitializeLlmEnhancer()
        {
            try
            {
                var llmConfig = _configService.Config.Llm;
                if (!string.IsNullOrWhiteSpace(llmConfig.ApiKey))
                {
                    _llmEnhancerService = new LlmTextEnhancerService(llmConfig);
                    TextKiProviderText.Text = $"Provider: {_llmEnhancerService.ProviderName}";
                }
                else
                {
                    TextKiProviderText.Text = "API nicht konfiguriert (manueller Modus verfuegbar)";
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"LLM Enhancer konnte nicht initialisiert werden: {ex.Message}");
                TextKiProviderText.Text = "Fehler bei Initialisierung";
            }
        }

        #region Update System

        /// <summary>
        /// Initialisiert den Update-Manager und startet Auto-Check.
        /// </summary>
        private void InitializeUpdateManager()
        {
            // Event-Handler fuer Update-Verfuegbarkeit
            _updateManager.UpdateAvailable += UpdateManager_UpdateAvailable;
            _updateManager.ErrorOccurred += UpdateManager_ErrorOccurred;

            // Auto-Check beim Start (asynchron, leise)
            Dispatcher.InvokeAsync(async () =>
            {
                await Task.Delay(2000); // 2 Sekunden warten bis UI geladen
                await _updateManager.CheckForUpdatesOnStartupAsync();
            });
        }

        /// <summary>
        /// Wird aufgerufen, wenn ein Update verfuegbar ist.
        /// </summary>
        private void UpdateManager_UpdateAvailable(object? sender, UpdateManifest manifest)
        {
            Dispatcher.Invoke(() =>
            {
                // Update-Badge anzeigen
                UpdateBadge.Visibility = Visibility.Visible;
                UpdateButton.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Gruen
                UpdateButton.ToolTip = $"Neue Version {manifest.LatestVersion} verfuegbar!";
            });
        }

        /// <summary>
        /// Wird aufgerufen, wenn ein Fehler beim Update-Check auftritt.
        /// </summary>
        private void UpdateManager_ErrorOccurred(object? sender, string errorMessage)
        {
            // Fehler nur im Debug-Modus anzeigen
            System.Diagnostics.Debug.WriteLine($"Update-Fehler: {errorMessage}");
        }

        /// <summary>
        /// Click-Handler fuer den Update-Button.
        /// </summary>
        private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
        {
            // Wenn Update verfuegbar, Dialog anzeigen
            if (_updateManager.IsUpdateAvailable && _updateManager.AvailableUpdate != null)
            {
                await _updateManager.ShowUpdateDialogAsync(this);
                return;
            }

            // Sonst: Nach Updates suchen
            UpdateButton.IsEnabled = false;
            UpdateButton.Content = "Pruefe...";

            try
            {
                await _updateManager.CheckForUpdatesAsync(showNoUpdateMessage: true);

                if (_updateManager.IsUpdateAvailable && _updateManager.AvailableUpdate != null)
                {
                    await _updateManager.ShowUpdateDialogAsync(this);
                }
                else
                {
                    MessageBox.Show(
                        $"Sie verwenden bereits die neueste Version ({AppVersionHelper.GetCurrentVersionString()}).",
                        "Kein Update verfuegbar",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler bei der Update-Pruefung:\n{ex.Message}",
                    "Update-Fehler",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                UpdateButton.IsEnabled = true;
                UpdateButton.Content = "Updates";

                // Badge zuruecksetzen wenn kein Update
                if (!_updateManager.IsUpdateAvailable)
                {
                    UpdateBadge.Visibility = Visibility.Collapsed;
                    UpdateButton.Background = new SolidColorBrush(Color.FromRgb(96, 125, 139)); // Grau
                }
            }
        }

        #endregion

        /// <summary>
        /// Event-Handler fuer den "Text glaetten (LLM)" Button.
        /// Optimiert den Quest-Text fuer Hoerbuch-Qualitaet.
        /// Unterstuetzt sowohl API-Modus als auch manuellen Modus.
        /// </summary>
        private async void OnEnhanceTextClick(object sender, RoutedEventArgs e)
        {
            if (SelectedQuest == null)
            {
                MessageBox.Show(
                    "Bitte zuerst eine Quest auswaehlen.",
                    "Keine Quest",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pruefen ob manueller Modus aktiviert ist
            if (TextKiManualModeRadio.IsChecked == true)
            {
                // Manueller Modus: ManualTextGenerationWindow oeffnen
                OnEnhanceTextManualMode();
                return;
            }

            // API-Modus: LLM-Service pruefen
            if (_llmEnhancerService == null || !_llmEnhancerService.IsConfigured)
            {
                var result = MessageBox.Show(
                    "Der LLM-Service ist nicht konfiguriert.\n\n" +
                    "Bitte in den Einstellungen einen API-Key fuer OpenAI, Claude oder Gemini hinterlegen.\n\n" +
                    "Alternativ kannst du den manuellen Modus verwenden (Browser-Premium).\n\n" +
                    "Moechtest du die Einstellungen jetzt oeffnen?",
                    "LLM nicht konfiguriert",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    OnSettingsClick(sender, e);
                    // Nach Settings erneut initialisieren
                    InitializeLlmEnhancer();
                }
                return;
            }

            // Bestaetigungsdialog
            var originalText = SelectedQuest.AutoGeneratedTtsText;
            var confirmResult = MessageBox.Show(
                $"Quest-Text fuer Hoerbuch optimieren?\n\n" +
                $"Quest: {SelectedQuest.Title} (ID: {SelectedQuest.QuestId})\n" +
                $"Provider: {_llmEnhancerService.ProviderName}\n\n" +
                $"Der urspruengliche Text wird als Override gespeichert und kann spaeter wiederhergestellt werden.",
                "Text glaetten",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.Yes) return;

            // UI deaktivieren waehrend der Verarbeitung
            EnhanceTextButton.IsEnabled = false;
            EnhanceTextButton.Content = "Optimiere...";
            StatusText.Text = $"Optimiere Quest {SelectedQuest.QuestId} mit {_llmEnhancerService.ProviderName}...";

            try
            {
                // LLM aufrufen
                var enhanceResult = await _llmEnhancerService.EnhanceQuestAsync(SelectedQuest);

                if (!enhanceResult.IsSuccess)
                {
                    MessageBox.Show(
                        $"Fehler bei der Text-Optimierung:\n\n{enhanceResult.ErrorMessage}",
                        "Fehler",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Erfolgreich - Override speichern
                ApplyEnhancedText(enhanceResult.EnhancedText, originalText);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler bei der Text-Optimierung:\n\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Text-Optimierung fehlgeschlagen.";
            }
            finally
            {
                EnhanceTextButton.IsEnabled = true;
                EnhanceTextButton.Content = "Text glaetten (LLM)";
            }
        }

        /// <summary>
        /// Manueller Modus: Oeffnet das ManualTextGenerationWindow.
        /// </summary>
        private void OnEnhanceTextManualMode()
        {
            if (SelectedQuest == null) return;

            var originalText = SelectedQuest.AutoGeneratedTtsText;

            var manualWindow = new ManualTextGenerationWindow(SelectedQuest)
            {
                Owner = this
            };

            if (manualWindow.ShowDialog() == true && manualWindow.WasApplied && !string.IsNullOrWhiteSpace(manualWindow.EnhancedText))
            {
                ApplyEnhancedText(manualWindow.EnhancedText, originalText);
            }
        }

        /// <summary>
        /// Wendet den optimierten Text an und speichert ihn als Override.
        /// </summary>
        private void ApplyEnhancedText(string enhancedText, string originalText)
        {
            if (SelectedQuest == null) return;

            // Override in Quest setzen
            SelectedQuest.CustomTtsText = enhancedText;

            // Override speichern (kompletter TTS-Text wird als Description gespeichert)
            _textOverrides.Overrides[SelectedQuest.QuestId] = new QuestTextOverride
            {
                QuestId = SelectedQuest.QuestId,
                Description = enhancedText,
                ModifiedAt = DateTime.Now
            };
            QuestTextOverridesStore.Save(_textOverrides);

            // UI aktualisieren
            OnPropertyChanged(nameof(SelectedQuest));

            // Erfolg anzeigen
            var diff = enhancedText.Length - originalText.Length;
            var percent = originalText.Length > 0 ? (diff * 100.0 / originalText.Length) : 0;
            var changeInfo = percent >= 0 ? $"+{percent:F0}%" : $"{percent:F0}%";

            StatusText.Text = $"Text optimiert: {originalText.Length} -> {enhancedText.Length} Zeichen ({changeInfo})";

            MessageBox.Show(
                $"Text erfolgreich optimiert!\n\n" +
                $"Original: {originalText.Length} Zeichen\n" +
                $"Optimiert: {enhancedText.Length} Zeichen\n" +
                $"Aenderung: {changeInfo}\n\n" +
                $"Der optimierte Text wurde als Override gespeichert.",
                "Optimierung abgeschlossen",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Oeffnet den Ausgabeordner im Windows Explorer.
        /// </summary>
        private void OnOpenOutputFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                var outputPath = _exportSettings.OutputRootPath;

                if (string.IsNullOrWhiteSpace(outputPath))
                {
                    MessageBox.Show(
                        "Kein Ausgabeordner konfiguriert.",
                        "Ordner nicht gefunden",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!Directory.Exists(outputPath))
                {
                    // Ordner erstellen falls nicht vorhanden
                    var result = MessageBox.Show(
                        $"Der Ordner existiert noch nicht:\n{outputPath}\n\nSoll der Ordner erstellt werden?",
                        "Ordner erstellen?",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Yes)
                    {
                        Directory.CreateDirectory(outputPath);
                    }
                    else
                    {
                        return;
                    }
                }

                // Explorer oeffnen
                System.Diagnostics.Process.Start("explorer.exe", outputPath);
                StatusText.Text = $"Ordner geoeffnet: {outputPath}";
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Fehler beim Oeffnen des Ordners:\n{ex.Message}",
                    "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Voice Profile Management

        /// <summary>
        /// Initialisiert die VoiceProfile-ComboBox mit den konfigurierten Stimmprofilen.
        /// </summary>
        private void InitializeVoiceProfileComboBox()
        {
            VoiceProfileComboBox.Items.Clear();

            // Alle Profile aus der Config laden
            var profiles = _configService.GetAllVoiceProfiles().ToList();

            if (profiles.Count == 0)
            {
                // Fallback: Standard-Profile hinzufuegen
                VoiceProfileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = "Maennlicher Erzaehler",
                    Tag = "male_narrator"
                });
                VoiceProfileComboBox.Items.Add(new ComboBoxItem
                {
                    Content = "Weibliche Erzaehlerin",
                    Tag = "female_narrator"
                });
            }
            else
            {
                foreach (var kvp in profiles)
                {
                    var profile = kvp.Value;
                    VoiceProfileComboBox.Items.Add(new ComboBoxItem
                    {
                        Content = profile.DisplayName,
                        Tag = kvp.Key,
                        ToolTip = $"{profile.GenderDisplayName}, {profile.StyleDisplayName}\n" +
                                  $"Voice-ID: {profile.VoiceId}"
                    });
                }
            }

            // Erstes Profil auswaehlen
            if (VoiceProfileComboBox.Items.Count > 0)
            {
                VoiceProfileComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Event-Handler wenn ein anderes Stimmprofil ausgewaehlt wird.
        /// Laedt die Feintuning-Werte des Profils in die UI.
        /// </summary>
        private void OnVoiceProfileSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: Noch nicht initialisiert
            if (VoiceProfileComboBox == null || StabilitySlider == null) return;

            if (VoiceProfileComboBox.SelectedItem is ComboBoxItem item && item.Tag is string profileKey)
            {
                var profile = _configService.GetVoiceProfile(profileKey);
                if (profile != null)
                {
                    // Slider-Werte aus Profil laden
                    StabilitySlider.Value = profile.Stability;
                    SimilaritySlider.Value = profile.SimilarityBoost;
                    StyleIntensitySlider.Value = profile.StyleIntensity;
                    SpeakerBoostCheckBox.IsChecked = profile.UseSpeakerBoost;
                    SpeedSlider.Value = profile.Speed;
                    PauseSlider.Value = profile.PauseMultiplier;
                    AddBreathPausesCheckBox.IsChecked = profile.AddBreathPauses;

                    // Info-Text aktualisieren
                    VoiceProfileInfoText.Text = profile.ShortInfo;
                }
            }
        }

        /// <summary>
        /// Erstellt ein VoiceProfile aus den aktuellen UI-Einstellungen.
        /// </summary>
        private VoiceProfile GetCurrentVoiceProfileFromUI()
        {
            var profile = new VoiceProfile();

            // Basis-Werte aus ausgewaehltem Profil kopieren
            if (VoiceProfileComboBox.SelectedItem is ComboBoxItem item && item.Tag is string profileKey)
            {
                var baseProfile = _configService.GetVoiceProfile(profileKey);
                if (baseProfile != null)
                {
                    profile = baseProfile.Clone();
                }
            }

            // UI-Werte uebernehmen
            profile.Stability = StabilitySlider.Value;
            profile.SimilarityBoost = SimilaritySlider.Value;
            profile.StyleIntensity = StyleIntensitySlider.Value;
            profile.UseSpeakerBoost = SpeakerBoostCheckBox.IsChecked == true;
            profile.Speed = SpeedSlider.Value;
            profile.PauseMultiplier = PauseSlider.Value;
            profile.AddBreathPauses = AddBreathPausesCheckBox.IsChecked == true;

            return profile;
        }

        #endregion
    }
}
