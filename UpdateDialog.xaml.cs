using System;
using System.Threading;
using System.Windows;
using WowQuestTtsTool.Services.Update;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Dialog für Update-Benachrichtigung und Installation.
    /// </summary>
    public partial class UpdateDialog : Window
    {
        private readonly UpdateManager _updateManager;
        private readonly UpdateManifest _manifest;
        private CancellationTokenSource? _downloadCts;

        /// <summary>
        /// Ob der Benutzer diese Version überspringen möchte.
        /// </summary>
        public bool SkipThisVersion => SkipVersionCheckBox.IsChecked == true;

        public UpdateDialog(UpdateManager updateManager, UpdateManifest manifest)
        {
            InitializeComponent();

            _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));

            // UI initialisieren
            CurrentVersionText.Text = AppVersionHelper.GetCurrentVersionString();
            NewVersionText.Text = manifest.LatestVersion;

            // Changelog formatieren (Newlines ersetzen)
            var changelog = manifest.Changelog.Replace("\\n", Environment.NewLine);
            ChangelogText.Text = changelog;

            // Release-Datum anzeigen (falls vorhanden)
            if (!string.IsNullOrEmpty(manifest.ReleaseDate))
            {
                ReleaseDateText.Text = $"Veröffentlicht: {manifest.ReleaseDate}";
            }

            // Dateigröße anzeigen (falls vorhanden)
            if (manifest.FileSize.HasValue && manifest.FileSize > 0)
            {
                var sizeInMb = manifest.FileSize.Value / (1024.0 * 1024.0);
                ReleaseDateText.Text += $" | Größe: {sizeInMb:F1} MB";
            }

            // Events abonnieren
            _updateManager.PropertyChanged += UpdateManager_PropertyChanged;
        }

        private void UpdateManager_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                switch (e.PropertyName)
                {
                    case nameof(UpdateManager.DownloadProgress):
                        DownloadProgressBar.Value = _updateManager.DownloadProgressPercent;
                        DownloadPercentText.Text = $"{_updateManager.DownloadProgressPercent}%";
                        break;

                    case nameof(UpdateManager.StatusMessage):
                        DownloadStatusText.Text = _updateManager.StatusMessage;
                        break;

                    case nameof(UpdateManager.IsDownloading):
                        if (_updateManager.IsDownloading)
                        {
                            DownloadPanel.Visibility = Visibility.Visible;
                            UpdateButton.IsEnabled = false;
                            LaterButton.Content = "Abbrechen";
                            SkipVersionCheckBox.IsEnabled = false;
                        }
                        else
                        {
                            UpdateButton.IsEnabled = true;
                            LaterButton.Content = "Später";
                            SkipVersionCheckBox.IsEnabled = true;
                        }
                        break;
                }
            });
        }

        private async void UpdateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _downloadCts = new CancellationTokenSource();

                DownloadPanel.Visibility = Visibility.Visible;
                UpdateButton.IsEnabled = false;
                LaterButton.Content = "Abbrechen";
                SkipVersionCheckBox.IsEnabled = false;

                var success = await _updateManager.DownloadAndInstallUpdateAsync(_manifest, _downloadCts.Token);

                if (success)
                {
                    // Anwendung wird vom UpdateManager beendet
                    DialogResult = true;
                }
                else
                {
                    // Download fehlgeschlagen - UI zurücksetzen
                    DownloadPanel.Visibility = Visibility.Collapsed;
                    UpdateButton.IsEnabled = true;
                    LaterButton.Content = "Später";
                    SkipVersionCheckBox.IsEnabled = true;

                    MessageBox.Show(
                        _updateManager.StatusMessage,
                        "Update fehlgeschlagen",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (OperationCanceledException)
            {
                DownloadPanel.Visibility = Visibility.Collapsed;
                UpdateButton.IsEnabled = true;
                LaterButton.Content = "Später";
                SkipVersionCheckBox.IsEnabled = true;
            }
            finally
            {
                _downloadCts?.Dispose();
                _downloadCts = null;
            }
        }

        private void LaterButton_Click(object sender, RoutedEventArgs e)
        {
            if (_updateManager.IsDownloading && _downloadCts != null)
            {
                // Download abbrechen
                _downloadCts.Cancel();
                return;
            }

            // Dialog schließen
            DialogResult = false;
            Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            _updateManager.PropertyChanged -= UpdateManager_PropertyChanged;
            _downloadCts?.Cancel();
            _downloadCts?.Dispose();
            base.OnClosed(e);
        }
    }
}
