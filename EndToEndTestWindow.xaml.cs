using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Interaktionslogik fuer EndToEndTestWindow.xaml
    /// Fenster fuer den End-to-End-Test der kompletten TTS-Pipeline.
    /// </summary>
    public partial class EndToEndTestWindow : Window
    {
        private readonly EndToEndTestService _testService;
        private CancellationTokenSource? _cancellationTokenSource;
        private bool _isRunning = false;

        /// <summary>
        /// Erstellt ein neues EndToEndTestWindow.
        /// </summary>
        /// <param name="ttsService">TTS-Service fuer Audio-Generierung</param>
        /// <param name="exportSettings">Export-Einstellungen</param>
        /// <param name="quests">Verfuegbare Quests fuer den Test</param>
        public EndToEndTestWindow(
            ITtsService ttsService,
            TtsExportSettings exportSettings,
            System.Collections.Generic.IEnumerable<Quest> quests)
        {
            InitializeComponent();

            // Test-Service erstellen
            _testService = new EndToEndTestService(ttsService, exportSettings, quests);

            // Events verbinden
            _testService.OnLog += OnTestLog;
            _testService.OnProgress += OnTestProgress;
        }

        /// <summary>
        /// Handler fuer Log-Meldungen vom Test-Service.
        /// </summary>
        private void OnTestLog(string message)
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
        /// Handler fuer Fortschritts-Updates vom Test-Service.
        /// </summary>
        private void OnTestProgress(int current, int total, string message)
        {
            Dispatcher.Invoke(() =>
            {
                var percentage = total > 0 ? (current * 100.0 / total) : 0;
                ProgressBar.Value = percentage;
                ProgressPercentText.Text = $"{percentage:F0}%";
                StatusText.Text = message;
            });
        }

        /// <summary>
        /// Handler fuer "Test starten" Button.
        /// </summary>
        private async void OnStartTestClick(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
                return;

            // UI vorbereiten
            _isRunning = true;
            StartTestButton.IsEnabled = false;
            CancelTestButton.IsEnabled = true;
            CloseButton.IsEnabled = false;
            LogTextBox.Clear();
            ProgressBar.Value = 0;
            ProgressPercentText.Text = "0%";
            StatusText.Text = "Test wird gestartet...";
            StatusText.Foreground = new SolidColorBrush(Colors.Gray);

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                // Konfiguration erstellen
                var config = new EndToEndTestConfig
                {
                    OnlyMaleVoice = OnlyMaleVoiceCheckBox.IsChecked == true,
                    ExportToAddon = ExportToAddonCheckBox.IsChecked == true,
                    LanguageCode = "deDE"
                };

                // Quest-ID aus TextBox lesen
                if (!string.IsNullOrWhiteSpace(TestQuestIdTextBox.Text))
                {
                    if (int.TryParse(TestQuestIdTextBox.Text.Trim(), out int questId))
                    {
                        config.TestQuestId = questId;
                    }
                    else
                    {
                        MessageBox.Show(
                            "Ungueltige Quest-ID. Bitte eine Zahl eingeben oder das Feld leer lassen.",
                            "Ungueltige Eingabe",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                // Test ausfuehren
                var result = await _testService.RunTestAsync(config, _cancellationTokenSource.Token);

                // Ergebnis anzeigen
                if (result.Success)
                {
                    StatusText.Text = "Test erfolgreich abgeschlossen!";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50)); // Gruen
                    ProgressBar.Value = 100;
                    ProgressPercentText.Text = "100%";

                    MessageBox.Show(
                        $"End-to-End-Test erfolgreich!\n\n" +
                        $"Quest: [{result.TestedQuest?.QuestId}] {result.TestedQuest?.Title}\n" +
                        $"Audio: {result.GeneratedAudioPath}\n" +
                        $"Dauer: {result.Duration.TotalSeconds:F1} Sekunden\n\n" +
                        "Du kannst jetzt WoW starten und die Quest testen!",
                        "Test erfolgreich",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    StatusText.Text = $"Test fehlgeschlagen: {result.ErrorMessage}";
                    StatusText.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40)); // Rot

                    if (!string.IsNullOrEmpty(result.ErrorMessage) &&
                        !result.ErrorMessage.Contains("abgebrochen"))
                    {
                        MessageBox.Show(
                            $"End-to-End-Test fehlgeschlagen!\n\n{result.ErrorMessage}",
                            "Test fehlgeschlagen",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Fehler: {ex.Message}";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(198, 40, 40));

                OnTestLog($"UNERWARTETER FEHLER: {ex.Message}");
                if (ex.InnerException != null)
                {
                    OnTestLog($"Details: {ex.InnerException.Message}");
                }
            }
            finally
            {
                _isRunning = false;
                StartTestButton.IsEnabled = true;
                CancelTestButton.IsEnabled = false;
                CloseButton.IsEnabled = true;
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;
            }
        }

        /// <summary>
        /// Handler fuer "Abbrechen" Button.
        /// </summary>
        private void OnCancelTestClick(object sender, RoutedEventArgs e)
        {
            if (_cancellationTokenSource != null && !_cancellationTokenSource.IsCancellationRequested)
            {
                _cancellationTokenSource.Cancel();
                StatusText.Text = "Abbruch angefordert...";
                CancelTestButton.IsEnabled = false;
            }
        }

        /// <summary>
        /// Handler fuer "Schliessen" Button.
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            if (_isRunning)
            {
                var result = MessageBox.Show(
                    "Ein Test wird noch ausgefuehrt. Wirklich schliessen?",
                    "Test laeuft",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                    return;

                _cancellationTokenSource?.Cancel();
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// Handler fuer Window-Schliessen.
        /// </summary>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_isRunning)
            {
                var result = MessageBox.Show(
                    "Ein Test wird noch ausgefuehrt. Wirklich schliessen?",
                    "Test laeuft",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result == MessageBoxResult.No)
                {
                    e.Cancel = true;
                    return;
                }

                _cancellationTokenSource?.Cancel();
            }

            // Events abmelden
            _testService.OnLog -= OnTestLog;
            _testService.OnProgress -= OnTestProgress;

            base.OnClosing(e);
        }
    }
}
