using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace WowQuestTtsTool.Services.Update
{
    /// <summary>
    /// Manager-Klasse für die Update-Koordination.
    /// Verwaltet den gesamten Update-Prozess von der Prüfung bis zur Installation.
    /// </summary>
    public class UpdateManager : INotifyPropertyChanged, IDisposable
    {
        private readonly UpdateService _updateService;
        private readonly UpdateSettings _settings;
        private UpdateManifest? _availableUpdate;
        private bool _isChecking;
        private bool _isDownloading;
        private double _downloadProgress;
        private string _statusMessage = "";
        private bool _disposed;

        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>
        /// Event, das ausgelöst wird, wenn ein Update verfügbar ist.
        /// </summary>
        public event EventHandler<UpdateManifest>? UpdateAvailable;

        /// <summary>
        /// Event, das ausgelöst wird, wenn ein Fehler auftritt.
        /// </summary>
        public event EventHandler<string>? ErrorOccurred;

        /// <summary>
        /// Die aktuelle Anwendungsversion.
        /// </summary>
        public string CurrentVersion => AppVersionHelper.GetCurrentVersionString();

        /// <summary>
        /// Das verfügbare Update (falls vorhanden).
        /// </summary>
        public UpdateManifest? AvailableUpdate
        {
            get => _availableUpdate;
            private set
            {
                _availableUpdate = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsUpdateAvailable));
                OnPropertyChanged(nameof(LatestVersionText));
            }
        }

        /// <summary>
        /// Ob ein Update verfügbar ist.
        /// </summary>
        public bool IsUpdateAvailable => _availableUpdate != null;

        /// <summary>
        /// Text der neuesten Version.
        /// </summary>
        public string LatestVersionText => _availableUpdate?.LatestVersion ?? CurrentVersion;

        /// <summary>
        /// Ob gerade eine Prüfung läuft.
        /// </summary>
        public bool IsChecking
        {
            get => _isChecking;
            private set
            {
                _isChecking = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Ob gerade ein Download läuft.
        /// </summary>
        public bool IsDownloading
        {
            get => _isDownloading;
            private set
            {
                _isDownloading = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Download-Fortschritt (0.0 - 1.0).
        /// </summary>
        public double DownloadProgress
        {
            get => _downloadProgress;
            private set
            {
                _downloadProgress = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DownloadProgressPercent));
            }
        }

        /// <summary>
        /// Download-Fortschritt in Prozent (0 - 100).
        /// </summary>
        public int DownloadProgressPercent => (int)(_downloadProgress * 100);

        /// <summary>
        /// Aktuelle Statusmeldung.
        /// </summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                _statusMessage = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Zeitpunkt der letzten Prüfung.
        /// </summary>
        public DateTime? LastCheckTime => _settings.LastUpdateCheck;

        /// <summary>
        /// Formatierter Text für letzte Prüfung.
        /// </summary>
        public string LastCheckText
        {
            get
            {
                if (_settings.LastUpdateCheck == null)
                    return "Noch nie geprüft";

                var diff = DateTime.Now - _settings.LastUpdateCheck.Value;
                if (diff.TotalMinutes < 1)
                    return "Gerade eben";
                if (diff.TotalMinutes < 60)
                    return $"Vor {(int)diff.TotalMinutes} Minuten";
                if (diff.TotalHours < 24)
                    return $"Vor {(int)diff.TotalHours} Stunden";
                return _settings.LastUpdateCheck.Value.ToString("dd.MM.yyyy HH:mm");
            }
        }

        /// <summary>
        /// Ob automatische Update-Prüfung aktiviert ist.
        /// </summary>
        public bool CheckUpdatesOnStartup
        {
            get => _settings.CheckUpdatesOnStartup;
            set
            {
                _settings.CheckUpdatesOnStartup = value;
                _settings.Save();
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Erstellt einen neuen UpdateManager.
        /// </summary>
        public UpdateManager()
        {
            _settings = UpdateSettings.Instance;
            _updateService = new UpdateService(_settings);
        }

        /// <summary>
        /// Prüft auf Updates (mit UI-Feedback).
        /// </summary>
        public async Task CheckForUpdatesAsync(bool showNoUpdateMessage = true, CancellationToken ct = default)
        {
            if (IsChecking)
                return;

            IsChecking = true;
            StatusMessage = "Prüfe auf Updates...";

            try
            {
                var manifest = await _updateService.CheckForUpdateAsync(ct);

                if (manifest != null)
                {
                    AvailableUpdate = manifest;
                    StatusMessage = $"Neue Version {manifest.LatestVersion} verfügbar!";
                    UpdateAvailable?.Invoke(this, manifest);
                }
                else
                {
                    AvailableUpdate = null;
                    StatusMessage = "Sie verwenden bereits die neueste Version.";

                    if (showNoUpdateMessage)
                    {
                        // Optional: Message anzeigen
                    }
                }

                OnPropertyChanged(nameof(LastCheckText));
            }
            catch (UpdateException ex)
            {
                StatusMessage = ex.Message;
                ErrorOccurred?.Invoke(this, ex.Message);
            }
            catch (TaskCanceledException)
            {
                StatusMessage = "Prüfung abgebrochen.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Fehler: {ex.Message}";
                ErrorOccurred?.Invoke(this, ex.Message);
            }
            finally
            {
                IsChecking = false;
            }
        }

        /// <summary>
        /// Prüft auf Updates beim Start (leise, ohne Fehlermeldungen).
        /// </summary>
        public async Task CheckForUpdatesOnStartupAsync()
        {
            if (!_settings.ShouldCheckForUpdates())
                return;

            try
            {
                var manifest = await _updateService.CheckForUpdateAsync();

                if (manifest != null)
                {
                    // Prüfen, ob diese Version übersprungen wurde
                    if (_settings.SkippedVersion == manifest.LatestVersion)
                        return;

                    AvailableUpdate = manifest;
                    StatusMessage = $"Neue Version {manifest.LatestVersion} verfügbar!";
                    UpdateAvailable?.Invoke(this, manifest);
                }

                OnPropertyChanged(nameof(LastCheckText));
            }
            catch
            {
                // Beim Start leise fehlschlagen
                Debug.WriteLine("Auto-Update-Check fehlgeschlagen (leiser Fehler).");
            }
        }

        /// <summary>
        /// Lädt das Update herunter und startet den Update-Prozess.
        /// </summary>
        public async Task<bool> DownloadAndInstallUpdateAsync(UpdateManifest manifest, CancellationToken ct = default)
        {
            if (IsDownloading)
                return false;

            IsDownloading = true;
            DownloadProgress = 0;
            StatusMessage = "Lade Update herunter...";

            try
            {
                // Download mit Fortschrittsanzeige
                var progress = new Progress<double>(p =>
                {
                    DownloadProgress = p;
                    StatusMessage = $"Lade Update herunter... {DownloadProgressPercent}%";
                });

                var result = await _updateService.DownloadUpdateAsync(manifest, progress, ct);

                if (!result.Success)
                {
                    StatusMessage = $"Download fehlgeschlagen: {result.ErrorMessage}";
                    ErrorOccurred?.Invoke(this, result.ErrorMessage ?? "Unbekannter Fehler");
                    return false;
                }

                StatusMessage = "Update heruntergeladen. Starte Installation...";

                // Updater starten
                return StartUpdater(result.ExtractedDirectory);
            }
            catch (TaskCanceledException)
            {
                StatusMessage = "Download abgebrochen.";
                return false;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Fehler: {ex.Message}";
                ErrorOccurred?.Invoke(this, ex.Message);
                return false;
            }
            finally
            {
                IsDownloading = false;
            }
        }

        /// <summary>
        /// Startet den externen Updater und beendet die Anwendung.
        /// </summary>
        private bool StartUpdater(string extractedDirectory)
        {
            try
            {
                Debug.WriteLine($"StartUpdater: extractedDirectory = {extractedDirectory}");
                Debug.WriteLine($"StartUpdater: InstallBaseDirectory = {_settings.InstallBaseDirectory}");

                var updaterPath = _settings.UpdaterExePath;
                Debug.WriteLine($"StartUpdater: UpdaterExePath = {updaterPath}");

                // Prüfen, ob Updater existiert
                if (!File.Exists(updaterPath))
                {
                    Debug.WriteLine($"StartUpdater: Updater nicht gefunden bei {updaterPath}");

                    // Updater aus dem Update-Paket kopieren (falls vorhanden)
                    var updaterInPackage = Path.Combine(extractedDirectory, _settings.UpdaterExeName);
                    Debug.WriteLine($"StartUpdater: Suche Updater in Paket: {updaterInPackage}");

                    // Auch in Unterordnern suchen
                    if (!File.Exists(updaterInPackage))
                    {
                        var dirs = Directory.GetDirectories(extractedDirectory);
                        foreach (var dir in dirs)
                        {
                            var candidate = Path.Combine(dir, _settings.UpdaterExeName);
                            Debug.WriteLine($"StartUpdater: Prüfe {candidate}");
                            if (File.Exists(candidate))
                            {
                                updaterInPackage = candidate;
                                break;
                            }
                        }
                    }

                    if (File.Exists(updaterInPackage))
                    {
                        Debug.WriteLine($"StartUpdater: Kopiere Updater von {updaterInPackage} nach {updaterPath}");
                        File.Copy(updaterInPackage, updaterPath, true);
                    }
                    else
                    {
                        StatusMessage = "Updater nicht gefunden!";
                        ErrorOccurred?.Invoke(this, $"WowQuestTtsUpdater.exe nicht gefunden. Gesucht in: {extractedDirectory}");
                        return false;
                    }
                }

                // Quellverzeichnis finden (kann das extractedDirectory oder ein Unterordner sein)
                var sourceDir = extractedDirectory;
                var mainExeInSource = Path.Combine(sourceDir, _settings.MainExeName);
                if (!File.Exists(mainExeInSource))
                {
                    // In Unterordnern suchen
                    var dirs = Directory.GetDirectories(extractedDirectory);
                    foreach (var dir in dirs)
                    {
                        var candidate = Path.Combine(dir, _settings.MainExeName);
                        if (File.Exists(candidate))
                        {
                            sourceDir = dir;
                            Debug.WriteLine($"StartUpdater: Quelle gefunden in Unterordner: {sourceDir}");
                            break;
                        }
                    }
                }

                // Argumente für den Updater
                var args = $"\"{sourceDir}\" \"{_settings.InstallBaseDirectory}\" \"{_settings.MainExeName}\"";
                Debug.WriteLine($"StartUpdater: Args = {args}");

                // Updater starten
                var startInfo = new ProcessStartInfo
                {
                    FileName = updaterPath,
                    Arguments = args,
                    UseShellExecute = true,
                    WorkingDirectory = _settings.InstallBaseDirectory
                };

                Debug.WriteLine($"StartUpdater: Starte {updaterPath}");
                Process.Start(startInfo);

                // Anwendung beenden
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"StartUpdater: Exception: {ex}");
                StatusMessage = $"Updater konnte nicht gestartet werden: {ex.Message}";
                ErrorOccurred?.Invoke(this, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Markiert eine Version als übersprungen.
        /// </summary>
        public void SkipVersion(string version)
        {
            _settings.SkippedVersion = version;
            _settings.Save();
            AvailableUpdate = null;
            StatusMessage = $"Version {version} wird übersprungen.";
        }

        /// <summary>
        /// Zeigt den Update-Dialog an.
        /// </summary>
        public async Task<bool> ShowUpdateDialogAsync(Window owner)
        {
            if (AvailableUpdate == null)
                return false;

            var dialog = new UpdateDialog(this, AvailableUpdate)
            {
                Owner = owner
            };

            var result = dialog.ShowDialog();

            if (result == true)
            {
                // Update installieren
                return await DownloadAndInstallUpdateAsync(AvailableUpdate);
            }
            else if (dialog.SkipThisVersion)
            {
                SkipVersion(AvailableUpdate.LatestVersion);
            }

            return false;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _updateService.Dispose();
                _disposed = true;
            }
        }
    }
}
