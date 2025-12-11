using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Interaktionslogik fuer ProgressWindow.xaml
    /// Zeigt den Vertonungs-Fortschritt pro Zone/Gebiet an.
    /// </summary>
    public partial class ProgressWindow : Window, INotifyPropertyChanged
    {
        // ==================== Datenquellen ====================

        /// <summary>
        /// Alle verfuegbaren Quests (aus dem Hauptfenster uebergeben).
        /// </summary>
        private readonly IReadOnlyList<Quest> _allQuests;

        /// <summary>
        /// Audio-Index mit allen vertonten Quests.
        /// </summary>
        private QuestAudioIndex? _audioIndex;

        /// <summary>
        /// Lookup-Dictionary fuer schnellen Zugriff auf Audio-Index-Eintraege.
        /// Key: "questId|gender" (z.B. "176|male")
        /// </summary>
        private Dictionary<string, QuestAudioIndexEntry>? _audioLookup;

        /// <summary>
        /// Export-Einstellungen (fuer Pfade und Sprache).
        /// </summary>
        private readonly TtsExportSettings _exportSettings;

        // ==================== Observable Collections ====================

        /// <summary>
        /// Zonen-Fortschritts-Daten fuer das DataGrid.
        /// </summary>
        public ObservableCollection<ZoneProgressInfo> ZoneProgressData { get; } = new();

        /// <summary>
        /// Fehlende Quests in der ausgewaehlten Zone.
        /// </summary>
        public ObservableCollection<Quest> MissingQuestsInZone { get; } = new();

        /// <summary>
        /// Aktuell ausgewaehlte Zone im DataGrid.
        /// </summary>
        private ZoneProgressInfo? _selectedZone;

        /// <summary>
        /// Aktueller Filter-Modus fuer die Detail-Liste.
        /// </summary>
        private Services.QuestFilterMode _currentFilterMode = Services.QuestFilterMode.MissingAudio;

        /// <summary>
        /// Service fuer Fortschritts-Berechnungen.
        /// </summary>
        private readonly Services.QuestProgressService _progressService = new();

        // ==================== Events ====================

        /// <summary>
        /// Event, das ausgeloest wird, wenn der Benutzer eine Zone im Hauptfenster laden moechte.
        /// </summary>
        public event Action<string>? LoadZoneInMainRequested;

        public event PropertyChangedEventHandler? PropertyChanged;

        // ==================== Konstruktor ====================

        /// <summary>
        /// Erstellt ein neues ProgressWindow.
        /// </summary>
        /// <param name="allQuests">Alle verfuegbaren Quests aus der Datenbank/Cache</param>
        /// <param name="exportSettings">TTS-Export-Einstellungen (fuer Pfade)</param>
        public ProgressWindow(IReadOnlyList<Quest> allQuests, TtsExportSettings exportSettings)
        {
            InitializeComponent();
            DataContext = this;

            _allQuests = allQuests ?? throw new ArgumentNullException(nameof(allQuests));
            _exportSettings = exportSettings ?? throw new ArgumentNullException(nameof(exportSettings));

            // DataGrid-Bindings setzen
            ZoneProgressDataGrid.ItemsSource = ZoneProgressData;
            MissingQuestsDataGrid.ItemsSource = MissingQuestsInZone;

            // Daten laden
            LoadProgressData();
        }

        // ==================== Datenlade-Logik ====================

        /// <summary>
        /// Laedt alle Fortschrittsdaten aus dem Audio-Index und den Quest-Daten.
        /// Verwendet den QuestProgressService fuer die Berechnungen.
        /// </summary>
        private void LoadProgressData()
        {
            ZoneProgressData.Clear();
            MissingQuestsInZone.Clear();
            SelectedZoneText.Text = "(Keine Zone ausgewaehlt)";

            // Audio-Index laden
            if (!string.IsNullOrEmpty(_exportSettings.OutputRootPath))
            {
                _audioIndex = AudioIndexWriter.LoadIndex(_exportSettings.OutputRootPath, _exportSettings.LanguageCode);
                _audioLookup = AudioIndexWriter.BuildLookupDictionary(_audioIndex);
            }
            else
            {
                _audioIndex = new QuestAudioIndex();
                _audioLookup = new Dictionary<string, QuestAudioIndexEntry>();
            }

            // Fortschrittsdaten per Service berechnen
            var zoneProgressList = _progressService.CalculateZoneProgress(_allQuests, _audioLookup, requireBothGenders: true);
            foreach (var zoneInfo in zoneProgressList)
            {
                ZoneProgressData.Add(zoneInfo);
            }

            // Gesamtstatistik berechnen
            var totalStats = _progressService.CalculateTotalStats(_allQuests, _audioLookup, requireBothGenders: true);

            TotalQuestsText.Text = totalStats.TotalQuests.ToString("N0");
            VoicedQuestsText.Text = totalStats.VoicedQuests.ToString("N0");
            MissingQuestsText.Text = totalStats.MissingQuests.ToString("N0");
            TotalProgressText.Text = $"{totalStats.ProgressPercent:F1}%";

            // Problem-Quests in der Statistik-Anzeige aktualisieren (falls vorhanden)
            if (ProblemQuestsText != null)
            {
                ProblemQuestsText.Text = totalStats.ProblemQuests.ToString("N0");
            }
        }

        /// <summary>
        /// Laedt die fehlenden/problematischen Quests fuer die ausgewaehlte Zone.
        /// Verwendet den QuestProgressService fuer die Filterung.
        /// </summary>
        private void LoadMissingQuestsForZone(string zoneName)
        {
            MissingQuestsInZone.Clear();

            if (string.IsNullOrEmpty(zoneName) || _audioLookup == null)
                return;

            // Gefilterte Quests per Service laden
            var filteredQuests = _progressService.GetFilteredQuestsForZone(
                zoneName,
                _allQuests,
                _audioLookup,
                _currentFilterMode,
                requireBothGenders: true);

            foreach (var quest in filteredQuests)
            {
                MissingQuestsInZone.Add(quest);
            }
        }

        /// <summary>
        /// Aktualisiert den Detail-Filter und laedt die Liste neu.
        /// </summary>
        private void UpdateDetailFilter(Services.QuestFilterMode newMode)
        {
            _currentFilterMode = newMode;

            // Falls eine Zone ausgewaehlt ist, Liste neu laden
            if (_selectedZone != null)
            {
                LoadMissingQuestsForZone(_selectedZone.ZoneName);
            }
        }

        // ==================== Event-Handler ====================

        /// <summary>
        /// Wird aufgerufen wenn eine Zone im DataGrid ausgewaehlt wird.
        /// </summary>
        private void ZoneProgressDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ZoneProgressDataGrid.SelectedItem is ZoneProgressInfo selectedZone)
            {
                _selectedZone = selectedZone;
                SelectedZoneText.Text = selectedZone.ZoneName;
                LoadMissingQuestsForZone(selectedZone.ZoneName);
            }
        }

        /// <summary>
        /// Aktualisiert die Fortschrittsdaten.
        /// </summary>
        private void OnRefreshClick(object sender, RoutedEventArgs e)
        {
            LoadProgressData();

            // Falls eine Zone ausgewaehlt war, diese erneut laden
            if (_selectedZone != null)
            {
                // Zone in den neuen Daten finden
                var refreshedZone = ZoneProgressData.FirstOrDefault(z => z.ZoneName == _selectedZone.ZoneName);
                if (refreshedZone != null)
                {
                    ZoneProgressDataGrid.SelectedItem = refreshedZone;
                    LoadMissingQuestsForZone(refreshedZone.ZoneName);
                }
            }

            MessageBox.Show(
                $"Fortschrittsdaten aktualisiert.\n\n" +
                $"Zonen: {ZoneProgressData.Count}\n" +
                $"Audio-Index Eintraege: {_audioIndex?.TotalCount ?? 0}",
                "Aktualisiert",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Laedt die ausgewaehlte Zone im Hauptfenster.
        /// </summary>
        private void OnLoadZoneInMainClick(object sender, RoutedEventArgs e)
        {
            if (_selectedZone == null)
            {
                MessageBox.Show(
                    "Bitte zuerst eine Zone in der Liste auswaehlen.",
                    "Keine Zone ausgewaehlt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Event ausloesen, damit das Hauptfenster die Zone laden kann
            LoadZoneInMainRequested?.Invoke(_selectedZone.ZoneName);

            // Optional: Fenster schliessen
            var result = MessageBox.Show(
                $"Zone '{_selectedZone.ZoneName}' wird im Hauptfenster geladen.\n\n" +
                "Moechtest du dieses Fenster schliessen?",
                "Zone laden",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                Close();
            }
        }

        /// <summary>
        /// Schliesst das Fenster.
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Event-Handler fuer Filter "Nur fehlende Quests".
        /// </summary>
        private void OnFilterMissingChecked(object sender, RoutedEventArgs e)
        {
            UpdateDetailFilter(Services.QuestFilterMode.MissingAudio);
        }

        /// <summary>
        /// Event-Handler fuer Filter "Nur Problem-Quests".
        /// </summary>
        private void OnFilterProblemsChecked(object sender, RoutedEventArgs e)
        {
            UpdateDetailFilter(Services.QuestFilterMode.ProblemQuestsOnly);
        }

        /// <summary>
        /// Event-Handler fuer Filter "Fehlende + Problem-Quests".
        /// </summary>
        private void OnFilterBothChecked(object sender, RoutedEventArgs e)
        {
            UpdateDetailFilter(Services.QuestFilterMode.MissingAndProblem);
        }

        /// <summary>
        /// Event-Handler fuer Filter "Alle Quests".
        /// </summary>
        private void OnFilterAllChecked(object sender, RoutedEventArgs e)
        {
            UpdateDetailFilter(Services.QuestFilterMode.All);
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ==================== Hilfsklasse fuer Zonen-Fortschritt ====================

    /// <summary>
    /// Enthaelt Fortschrittsinformationen fuer eine Zone/Gebiet.
    /// </summary>
    public class ZoneProgressInfo : INotifyPropertyChanged
    {
        private string _zoneName = "";
        private int _totalQuests;
        private int _voicedQuests;
        private int _missingQuests;
        private double _progressPercent;
        private int _problemQuests;
        private int _mainQuests;
        private int _mainQuestsVoiced;
        private double _mainProgressPercent;

        /// <summary>
        /// Name der Zone/des Gebiets.
        /// </summary>
        public string ZoneName
        {
            get => _zoneName;
            set
            {
                if (_zoneName != value)
                {
                    _zoneName = value;
                    OnPropertyChanged(nameof(ZoneName));
                }
            }
        }

        /// <summary>
        /// Gesamtzahl der Quests in dieser Zone.
        /// </summary>
        public int TotalQuests
        {
            get => _totalQuests;
            set
            {
                if (_totalQuests != value)
                {
                    _totalQuests = value;
                    OnPropertyChanged(nameof(TotalQuests));
                }
            }
        }

        /// <summary>
        /// Anzahl der bereits vertonten Quests.
        /// </summary>
        public int VoicedQuests
        {
            get => _voicedQuests;
            set
            {
                if (_voicedQuests != value)
                {
                    _voicedQuests = value;
                    OnPropertyChanged(nameof(VoicedQuests));
                }
            }
        }

        /// <summary>
        /// Anzahl der fehlenden (noch nicht vertonten) Quests.
        /// </summary>
        public int MissingQuests
        {
            get => _missingQuests;
            set
            {
                if (_missingQuests != value)
                {
                    _missingQuests = value;
                    OnPropertyChanged(nameof(MissingQuests));
                }
            }
        }

        /// <summary>
        /// Fortschritt in Prozent (0-100).
        /// </summary>
        public double ProgressPercent
        {
            get => _progressPercent;
            set
            {
                if (Math.Abs(_progressPercent - value) > 0.001)
                {
                    _progressPercent = value;
                    OnPropertyChanged(nameof(ProgressPercent));
                    OnPropertyChanged(nameof(IsComplete));
                    OnPropertyChanged(nameof(ProgressColor));
                    OnPropertyChanged(nameof(ProgressColorBrush));
                }
            }
        }

        /// <summary>
        /// Anzahl der Problem-Quests (MixedGermanEnglish oder Incomplete).
        /// </summary>
        public int ProblemQuests
        {
            get => _problemQuests;
            set
            {
                if (_problemQuests != value)
                {
                    _problemQuests = value;
                    OnPropertyChanged(nameof(ProblemQuests));
                    OnPropertyChanged(nameof(HasProblems));
                }
            }
        }

        /// <summary>
        /// Anzahl der Hauptquests in dieser Zone.
        /// </summary>
        public int MainQuests
        {
            get => _mainQuests;
            set
            {
                if (_mainQuests != value)
                {
                    _mainQuests = value;
                    OnPropertyChanged(nameof(MainQuests));
                }
            }
        }

        /// <summary>
        /// Anzahl der vertonten Hauptquests.
        /// </summary>
        public int MainQuestsVoiced
        {
            get => _mainQuestsVoiced;
            set
            {
                if (_mainQuestsVoiced != value)
                {
                    _mainQuestsVoiced = value;
                    OnPropertyChanged(nameof(MainQuestsVoiced));
                }
            }
        }

        /// <summary>
        /// Fortschritt der Hauptquests in Prozent.
        /// </summary>
        public double MainProgressPercent
        {
            get => _mainProgressPercent;
            set
            {
                if (Math.Abs(_mainProgressPercent - value) > 0.001)
                {
                    _mainProgressPercent = value;
                    OnPropertyChanged(nameof(MainProgressPercent));
                }
            }
        }

        /// <summary>
        /// Gibt an, ob die Zone vollstaendig vertont ist (100%).
        /// </summary>
        public bool IsComplete => Math.Abs(ProgressPercent - 100) < 0.001 || MissingQuests == 0;

        /// <summary>
        /// Gibt an, ob Problem-Quests vorhanden sind.
        /// </summary>
        public bool HasProblems => ProblemQuests > 0;

        /// <summary>
        /// Farbcode basierend auf Fortschritt:
        /// - Rot: 0-25%
        /// - Orange: 25-50%
        /// - Gelb: 50-75%
        /// - Gruen: 75-100%
        /// </summary>
        public string ProgressColor
        {
            get
            {
                if (ProgressPercent >= 75) return "#4CAF50"; // Gruen
                if (ProgressPercent >= 50) return "#FFC107"; // Gelb
                if (ProgressPercent >= 25) return "#FF9800"; // Orange
                return "#F44336"; // Rot
            }
        }

        /// <summary>
        /// SolidColorBrush basierend auf Fortschritt fuer XAML-Binding.
        /// </summary>
        public System.Windows.Media.SolidColorBrush ProgressColorBrush
        {
            get
            {
                var colorHex = ProgressColor;
                try
                {
                    var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(colorHex);
                    return new System.Windows.Media.SolidColorBrush(color);
                }
                catch
                {
                    return new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
