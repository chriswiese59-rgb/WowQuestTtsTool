using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services.Update
{
    /// <summary>
    /// Service für Update-Prüfung, Download und Entpacken.
    /// </summary>
    public class UpdateService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly UpdateSettings _settings;
        private bool _disposed;

        /// <summary>
        /// URL zum Update-Manifest.
        /// </summary>
        public string UpdateManifestUrl => _settings.UpdateManifestUrl;

        /// <summary>
        /// Erstellt einen neuen UpdateService mit den angegebenen Einstellungen.
        /// </summary>
        public UpdateService(UpdateSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(10) // 10 Minuten fuer grosse Downloads
            };
            // User-Agent setzen (wichtig für GitHub API)
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WowQuestTtsTool-Updater/1.0");
        }

        /// <summary>
        /// Erstellt einen neuen UpdateService mit Singleton-Einstellungen.
        /// </summary>
        public UpdateService() : this(UpdateSettings.Instance)
        {
        }

        /// <summary>
        /// Prüft, ob ein Update verfügbar ist.
        /// Unterstützt sowohl eigenes JSON-Format als auch GitHub Releases API.
        /// </summary>
        /// <returns>Das Update-Manifest, falls ein Update verfügbar ist; sonst null.</returns>
        public async Task<UpdateManifest?> CheckForUpdateAsync(CancellationToken ct = default)
        {
            try
            {
                // Manifest herunterladen
                var response = await _httpClient.GetAsync(UpdateManifestUrl, ct);
                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);

                UpdateManifest? manifest = null;

                // Prüfen ob GitHub Releases API (erkennt an URL oder JSON-Struktur)
                if (UpdateManifestUrl.Contains("api.github.com") || json.Contains("\"tag_name\""))
                {
                    // GitHub Release Format
                    var githubRelease = JsonSerializer.Deserialize<GitHubReleaseInfo>(json);
                    if (githubRelease != null && !string.IsNullOrEmpty(githubRelease.TagName))
                    {
                        manifest = githubRelease.ToUpdateManifest();
                    }
                }
                else
                {
                    // Eigenes JSON-Format
                    manifest = JsonSerializer.Deserialize<UpdateManifest>(json);
                }

                if (manifest == null || string.IsNullOrEmpty(manifest.LatestVersion))
                {
                    return null;
                }

                // Prüfen, ob Update verfügbar
                if (AppVersionHelper.IsUpdateAvailable(manifest.LatestVersion))
                {
                    // Zeitpunkt der Prüfung speichern
                    _settings.MarkUpdateChecked();
                    return manifest;
                }

                // Kein Update verfügbar
                _settings.MarkUpdateChecked();
                return null;
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update-Prüfung fehlgeschlagen (Netzwerk): {ex.Message}");
                throw new UpdateException("Konnte Update-Server nicht erreichen. Bitte Internetverbindung prüfen.", ex);
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update-Prüfung abgebrochen: {ex.Message}");
                throw;
            }
            catch (JsonException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update-Manifest ungültig: {ex.Message}");
                throw new UpdateException("Update-Manifest konnte nicht gelesen werden.", ex);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Update-Prüfung fehlgeschlagen: {ex.Message}");
                throw new UpdateException($"Update-Prüfung fehlgeschlagen: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Lädt das Update herunter und entpackt es.
        /// </summary>
        /// <param name="manifest">Das Update-Manifest.</param>
        /// <param name="progress">Fortschrittsanzeige (0.0 - 1.0).</param>
        /// <param name="ct">Abbruch-Token.</param>
        /// <returns>Ergebnis des Downloads.</returns>
        public async Task<DownloadResult> DownloadUpdateAsync(
            UpdateManifest manifest,
            IProgress<double>? progress = null,
            CancellationToken ct = default)
        {
            if (manifest == null || string.IsNullOrEmpty(manifest.DownloadUrl))
            {
                return DownloadResult.Failed("Ungültiges Update-Manifest.");
            }

            string zipFilePath = "";
            string extractedDir = "";

            try
            {
                // Temp-Verzeichnis erstellen
                var tempDir = _settings.TempUpdateDirectory;
                if (!Directory.Exists(tempDir))
                {
                    Directory.CreateDirectory(tempDir);
                }

                // Alte Updates aufräumen
                CleanupOldUpdates(tempDir);

                // ZIP-Dateiname
                var zipFileName = $"WowQuestTtsTool_{manifest.LatestVersion}.zip";
                zipFilePath = Path.Combine(tempDir, zipFileName);

                // Zielverzeichnis für Entpacken
                extractedDir = Path.Combine(tempDir, $"WowQuestTtsTool_{manifest.LatestVersion}");

                // Falls schon vorhanden, löschen
                if (File.Exists(zipFilePath))
                {
                    File.Delete(zipFilePath);
                }
                if (Directory.Exists(extractedDir))
                {
                    Directory.Delete(extractedDir, true);
                }

                // Download starten
                using var response = await _httpClient.GetAsync(manifest.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1;
                var downloadedBytes = 0L;

                await using (var contentStream = await response.Content.ReadAsStreamAsync(ct))
                await using (var fileStream = new FileStream(zipFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                {
                    var buffer = new byte[8192];
                    int bytesRead;

                    while ((bytesRead = await contentStream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                        downloadedBytes += bytesRead;

                        // Fortschritt melden
                        if (totalBytes > 0)
                        {
                            progress?.Report((double)downloadedBytes / totalBytes);
                        }
                    }

                    // Explizit flushen und schließen
                    await fileStream.FlushAsync(ct);
                }
                // Streams sind jetzt definitiv geschlossen

                // Hash prüfen (falls vorhanden)
                var hashVerified = false;
                if (!string.IsNullOrEmpty(manifest.FileHash))
                {
                    hashVerified = await VerifyFileHashAsync(zipFilePath, manifest.FileHash);
                    if (!hashVerified)
                    {
                        File.Delete(zipFilePath);
                        return DownloadResult.Failed("Datei-Integritätsprüfung fehlgeschlagen. Download möglicherweise beschädigt.");
                    }
                }

                // ZIP entpacken
                System.Diagnostics.Debug.WriteLine($"Entpacke ZIP: {zipFilePath} -> {extractedDir}");
                System.Diagnostics.Debug.WriteLine($"ZIP existiert: {File.Exists(zipFilePath)}, Größe: {new FileInfo(zipFilePath).Length}");

                Directory.CreateDirectory(extractedDir);

                try
                {
                    ZipFile.ExtractToDirectory(zipFilePath, extractedDir, overwriteFiles: true);
                    System.Diagnostics.Debug.WriteLine($"Entpacken erfolgreich. Dateien: {Directory.GetFiles(extractedDir).Length}");
                }
                catch (Exception zipEx)
                {
                    System.Diagnostics.Debug.WriteLine($"ZIP Entpacken Fehler: {zipEx}");
                    return DownloadResult.Failed($"ZIP konnte nicht entpackt werden: {zipEx.Message}");
                }

                // Prüfen ob Dateien entpackt wurden
                var extractedFiles = Directory.GetFiles(extractedDir, "*", SearchOption.AllDirectories);
                if (extractedFiles.Length == 0)
                {
                    System.Diagnostics.Debug.WriteLine("WARNUNG: Keine Dateien entpackt!");
                    return DownloadResult.Failed("ZIP-Archiv ist leer oder konnte nicht entpackt werden.");
                }

                System.Diagnostics.Debug.WriteLine($"Entpackt: {extractedFiles.Length} Dateien");

                return DownloadResult.Successful(zipFilePath, extractedDir, downloadedBytes, hashVerified);
            }
            catch (HttpRequestException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download HttpRequestException: {ex}");
                return DownloadResult.Failed($"Download fehlgeschlagen: {ex.Message}");
            }
            catch (IOException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download IOException: {ex}");
                return DownloadResult.Failed($"Dateifehler: {ex.Message}");
            }
            catch (TaskCanceledException ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download TaskCanceledException: {ex}");
                // Aufräumen bei Abbruch
                TryDeleteFile(zipFilePath);
                TryDeleteDirectory(extractedDir);
                return DownloadResult.Failed("Download abgebrochen (Timeout oder Benutzer-Abbruch).");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Download Exception: {ex}");
                return DownloadResult.Failed($"Unerwarteter Fehler: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// Prüft den SHA256-Hash einer Datei.
        /// </summary>
        private async Task<bool> VerifyFileHashAsync(string filePath, string expectedHash)
        {
            try
            {
                using var sha256 = SHA256.Create();
                await using var stream = File.OpenRead(filePath);
                var hashBytes = await sha256.ComputeHashAsync(stream);

                // Hash als Hex-String
                var actualHashHex = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                var expectedHashClean = expectedHash.Replace("-", "").ToLowerInvariant();

                if (actualHashHex == expectedHashClean)
                {
                    return true;
                }

                // Versuche Base64
                var actualHashBase64 = Convert.ToBase64String(hashBytes);
                if (actualHashBase64 == expectedHash)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Räumt alte Update-Downloads auf.
        /// </summary>
        private void CleanupOldUpdates(string tempDir)
        {
            try
            {
                var currentVersion = AppVersionHelper.GetCurrentVersionString();

                foreach (var dir in Directory.GetDirectories(tempDir))
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        // Verzeichnisse älter als 7 Tage löschen
                        if (dirInfo.CreationTime < DateTime.Now.AddDays(-7))
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    catch
                    {
                        // Ignorieren
                    }
                }

                foreach (var file in Directory.GetFiles(tempDir, "*.zip"))
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.CreationTime < DateTime.Now.AddDays(-7))
                        {
                            File.Delete(file);
                        }
                    }
                    catch
                    {
                        // Ignorieren
                    }
                }
            }
            catch
            {
                // Cleanup-Fehler ignorieren
            }
        }

        private void TryDeleteFile(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignorieren
            }
        }

        private void TryDeleteDirectory(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // Ignorieren
            }
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _httpClient.Dispose();
                _disposed = true;
            }
        }
    }

    /// <summary>
    /// Ausnahme für Update-Fehler.
    /// </summary>
    public class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
        public UpdateException(string message, Exception innerException) : base(message, innerException) { }
    }
}
