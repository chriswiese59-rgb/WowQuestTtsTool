using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services.TtsEngines
{
    /// <summary>
    /// Event-Argumente fuer TTS-Fehler.
    /// </summary>
    public class TtsErrorEventArgs : EventArgs
    {
        public string EngineId { get; }
        public TtsErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public TtsRequest Request { get; }

        public TtsErrorEventArgs(string engineId, TtsErrorCode errorCode, string errorMessage, TtsRequest request)
        {
            EngineId = engineId;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            Request = request;
        }
    }

    /// <summary>
    /// Event-Argumente fuer erfolgreiche TTS-Generierung.
    /// </summary>
    public class TtsCompletedEventArgs : EventArgs
    {
        public string EngineId { get; }
        public TtsResult Result { get; }
        public TtsRequest Request { get; }

        public TtsCompletedEventArgs(string engineId, TtsResult result, TtsRequest request)
        {
            EngineId = engineId;
            Result = result;
            Request = request;
        }
    }

    /// <summary>
    /// Info-Klasse fuer UI-Binding.
    /// </summary>
    public class TtsEngineInfo
    {
        public string EngineId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool IsAvailable { get; set; }
        public bool IsConfigured { get; set; }
        public string StatusText { get; set; } = "";

        public static TtsEngineInfo FromEngine(ITtsEngine engine)
        {
            string status;
            if (!engine.IsConfigured)
                status = "Nicht konfiguriert";
            else if (!engine.IsAvailable)
                status = "Nicht verfuegbar";
            else
                status = "Bereit";

            return new TtsEngineInfo
            {
                EngineId = engine.EngineId,
                DisplayName = engine.DisplayName,
                IsAvailable = engine.IsAvailable,
                IsConfigured = engine.IsConfigured,
                StatusText = status
            };
        }
    }

    /// <summary>
    /// Zentrale Verwaltung aller TTS-Engines.
    /// </summary>
    public class TtsEngineManager : IDisposable
    {
        private readonly Dictionary<string, ITtsEngine> _engines = new();
        private readonly TtsEngineSettings _settings;
        private readonly TtsUsageTracker _usageTracker;
        private bool _disposed;

        /// <summary>
        /// Event wird ausgeloest wenn ein TTS-Fehler auftritt.
        /// </summary>
        public event EventHandler<TtsErrorEventArgs>? TtsErrorOccurred;

        /// <summary>
        /// Event wird ausgeloest wenn TTS erfolgreich abgeschlossen wurde.
        /// </summary>
        public event EventHandler<TtsCompletedEventArgs>? TtsCompleted;

        /// <summary>
        /// Alle registrierten Engines.
        /// </summary>
        public IReadOnlyDictionary<string, ITtsEngine> RegisteredEngines => _engines;

        /// <summary>
        /// Nur verfuegbare Engines.
        /// </summary>
        public IReadOnlyList<ITtsEngine> AvailableEngines =>
            _engines.Values.Where(e => e.IsAvailable).ToList();

        /// <summary>
        /// Alle Engines als Info-Objekte fuer UI.
        /// </summary>
        public IReadOnlyList<TtsEngineInfo> AllEngineInfos =>
            _engines.Values.Select(TtsEngineInfo.FromEngine).ToList();

        /// <summary>
        /// Die aktive Standard-Engine.
        /// </summary>
        public ITtsEngine? ActiveEngine =>
            _engines.TryGetValue(_settings.ActiveEngineId, out var engine) ? engine : null;

        /// <summary>
        /// ID der aktiven Engine.
        /// </summary>
        public string ActiveEngineId
        {
            get => _settings.ActiveEngineId;
            set
            {
                if (_engines.ContainsKey(value))
                {
                    _settings.ActiveEngineId = value;
                    _settings.Save();
                }
            }
        }

        /// <summary>
        /// Singleton-Instanz.
        /// </summary>
        private static TtsEngineManager? _instance;
        public static TtsEngineManager Instance => _instance ??= new TtsEngineManager();

        public TtsEngineManager() : this(TtsEngineSettings.Instance, TtsUsageTracker.Instance)
        {
        }

        public TtsEngineManager(TtsEngineSettings settings, TtsUsageTracker usageTracker)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _usageTracker = usageTracker ?? throw new ArgumentNullException(nameof(usageTracker));

            Initialize();
        }

        /// <summary>
        /// Initialisiert alle Engines.
        /// </summary>
        private void Initialize()
        {
            // Alle vier Engines registrieren
            RegisterEngine(new OpenAiTtsEngine(_settings));
            RegisterEngine(new GoogleCloudTtsEngine(_settings)); // Google Cloud TTS (statt Gemini Platzhalter)
            RegisterEngine(new ClaudeTtsEngine(_settings));
            RegisterEngine(new ExternalTtsEngine(_settings));

            Debug.WriteLine($"TtsEngineManager initialisiert mit {_engines.Count} Engines.");
            Debug.WriteLine($"Aktive Engine: {ActiveEngineId}");
            Debug.WriteLine($"Verfuegbare Engines: {string.Join(", ", AvailableEngines.Select(e => e.EngineId))}");
        }

        /// <summary>
        /// Registriert eine Engine.
        /// </summary>
        public void RegisterEngine(ITtsEngine engine)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            _engines[engine.EngineId] = engine;
        }

        /// <summary>
        /// Holt eine Engine nach ID.
        /// </summary>
        public ITtsEngine? GetEngine(string engineId)
        {
            if (string.IsNullOrWhiteSpace(engineId))
                return ActiveEngine;

            return _engines.TryGetValue(engineId, out var engine) ? engine : null;
        }

        /// <summary>
        /// Setzt die aktive Engine.
        /// </summary>
        public void SetActiveEngine(string engineId)
        {
            if (!_engines.ContainsKey(engineId))
            {
                throw new ArgumentException($"Engine '{engineId}' nicht gefunden.", nameof(engineId));
            }

            _settings.ActiveEngineId = engineId;
            _settings.Save();
        }

        /// <summary>
        /// Generiert Audio mit der angegebenen oder aktiven Engine.
        /// </summary>
        public async Task<TtsResult> GenerateAudioAsync(
            TtsRequest request,
            string? engineId = null,
            CancellationToken ct = default)
        {
            var engine = GetEngine(engineId ?? _settings.ActiveEngineId);
            if (engine == null)
            {
                return TtsResult.Failed(TtsErrorCode.EngineNotAvailable, $"Engine '{engineId}' nicht gefunden.");
            }

            var actualEngineId = engine.EngineId;
            var result = await ExecuteWithRetryAsync(engine, request, ct);

            // Bei Fehler: Fallback versuchen
            if (!result.Success && _settings.AutoFallbackEnabled && !string.IsNullOrEmpty(_settings.FallbackEngineId))
            {
                var fallbackEngine = GetEngine(_settings.FallbackEngineId);
                if (fallbackEngine != null && fallbackEngine.IsAvailable && fallbackEngine.EngineId != actualEngineId)
                {
                    Debug.WriteLine($"TTS Fallback: {actualEngineId} -> {fallbackEngine.EngineId}");
                    result = await ExecuteWithRetryAsync(fallbackEngine, request, ct);
                    actualEngineId = fallbackEngine.EngineId;
                }
            }

            // Usage tracken
            if (result.Success && _settings.UsageTrackingEnabled)
            {
                _usageTracker.RecordUsage(
                    actualEngineId,
                    result.CharacterCount,
                    result.EstimatedTokens,
                    result.DurationMs
                );
            }

            // Events ausloesen
            if (result.Success)
            {
                TtsCompleted?.Invoke(this, new TtsCompletedEventArgs(actualEngineId, result, request));
            }
            else
            {
                TtsErrorOccurred?.Invoke(this, new TtsErrorEventArgs(
                    actualEngineId,
                    result.ErrorCode,
                    result.ErrorMessage ?? "Unbekannter Fehler",
                    request
                ));
            }

            return result;
        }

        /// <summary>
        /// Fuehrt TTS mit Retry-Logik aus.
        /// </summary>
        private async Task<TtsResult> ExecuteWithRetryAsync(
            ITtsEngine engine,
            TtsRequest request,
            CancellationToken ct)
        {
            var maxRetries = _settings.MaxRetries;
            var retryDelay = _settings.RetryDelayMs;
            TtsResult? lastResult = null;

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                if (attempt > 0)
                {
                    Debug.WriteLine($"TTS Retry {attempt}/{maxRetries} nach {retryDelay}ms...");
                    await Task.Delay(retryDelay, ct);
                    retryDelay *= 2; // Exponential backoff
                }

                lastResult = await engine.GenerateAudioAsync(request, ct);

                if (lastResult.Success)
                {
                    return lastResult;
                }

                // Nur bei bestimmten Fehlern retry
                if (!ShouldRetry(lastResult.ErrorCode))
                {
                    break;
                }
            }

            return lastResult ?? TtsResult.Failed(TtsErrorCode.UnknownError, "Kein Ergebnis erhalten.");
        }

        /// <summary>
        /// Bestimmt ob bei einem Fehlercode ein Retry sinnvoll ist.
        /// </summary>
        private static bool ShouldRetry(TtsErrorCode errorCode)
        {
            return errorCode switch
            {
                TtsErrorCode.RateLimitExceeded => true,
                TtsErrorCode.Timeout => true,
                TtsErrorCode.NetworkError => true,
                TtsErrorCode.ServerError => true,
                _ => false
            };
        }

        /// <summary>
        /// Validiert alle Engines.
        /// </summary>
        public async Task<Dictionary<string, TtsValidationResult>> ValidateAllEnginesAsync(CancellationToken ct = default)
        {
            var results = new Dictionary<string, TtsValidationResult>();

            foreach (var engine in _engines.Values)
            {
                try
                {
                    results[engine.EngineId] = await engine.ValidateConfigurationAsync(ct);
                }
                catch (Exception ex)
                {
                    results[engine.EngineId] = TtsValidationResult.Invalid($"Validierung fehlgeschlagen: {ex.Message}");
                }
            }

            return results;
        }

        /// <summary>
        /// Aktualisiert die Engine-Infos (z.B. nach Settings-Aenderung).
        /// </summary>
        public void RefreshEngines()
        {
            // Engines mit neuen Settings neu initialisieren
            _engines.Clear();
            Initialize();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                foreach (var engine in _engines.Values)
                {
                    if (engine is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _engines.Clear();
                _disposed = true;
            }
        }

        /// <summary>
        /// Setzt die Singleton-Instanz zurueck (fuer Tests).
        /// </summary>
        public static void ResetInstance()
        {
            _instance?.Dispose();
            _instance = null;
        }
    }
}
