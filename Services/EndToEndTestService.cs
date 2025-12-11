using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Ergebnis eines End-to-End-Tests.
    /// </summary>
    public class EndToEndTestResult
    {
        /// <summary>
        /// Ob der Test erfolgreich war.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Fehlermeldung (falls nicht erfolgreich).
        /// </summary>
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Getestete Quest.
        /// </summary>
        public Quest? TestedQuest { get; set; }

        /// <summary>
        /// Pfad zur generierten Audio-Datei.
        /// </summary>
        public string? GeneratedAudioPath { get; set; }

        /// <summary>
        /// Pfad zur kopierten Addon-Audio-Datei.
        /// </summary>
        public string? AddonAudioPath { get; set; }

        /// <summary>
        /// Ob der Audio-Index aktualisiert wurde.
        /// </summary>
        public bool AudioIndexUpdated { get; set; }

        /// <summary>
        /// Ob WowTts_Data.lua generiert wurde.
        /// </summary>
        public bool DataLuaGenerated { get; set; }

        /// <summary>
        /// Dauer des Tests.
        /// </summary>
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// Alle Schritte, die durchgeführt wurden.
        /// </summary>
        public List<EndToEndTestStep> Steps { get; set; } = [];
    }

    /// <summary>
    /// Einzelner Schritt im End-to-End-Test.
    /// </summary>
    public class EndToEndTestStep
    {
        public string Name { get; set; } = "";
        public bool Success { get; set; }
        public string? Details { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Konfiguration für den End-to-End-Test.
    /// </summary>
    public class EndToEndTestConfig
    {
        /// <summary>
        /// Quest-ID zum Testen (null = automatisch wählen).
        /// </summary>
        public int? TestQuestId { get; set; }

        /// <summary>
        /// Ob nur männliche Stimme getestet werden soll.
        /// </summary>
        public bool OnlyMaleVoice { get; set; } = true;

        /// <summary>
        /// Ob der Addon-Export durchgeführt werden soll.
        /// </summary>
        public bool ExportToAddon { get; set; } = true;

        /// <summary>
        /// Sprachcode für den Test.
        /// </summary>
        public string LanguageCode { get; set; } = "deDE";
    }

    /// <summary>
    /// Service für End-to-End-Tests der kompletten TTS-Pipeline.
    /// Testet: Quest laden -> TTS generieren -> Audio speichern -> Index aktualisieren -> Addon exportieren.
    /// </summary>
    public class EndToEndTestService
    {
        private readonly ITtsService _ttsService;
        private readonly TtsExportSettings _exportSettings;
        private readonly IEnumerable<Quest> _availableQuests;

        /// <summary>
        /// Event für Log-Meldungen während des Tests.
        /// </summary>
        public event Action<string>? OnLog;

        /// <summary>
        /// Event für Fortschritts-Updates.
        /// </summary>
        public event Action<int, int, string>? OnProgress;

        public EndToEndTestService(
            ITtsService ttsService,
            TtsExportSettings exportSettings,
            IEnumerable<Quest> availableQuests)
        {
            _ttsService = ttsService ?? throw new ArgumentNullException(nameof(ttsService));
            _exportSettings = exportSettings ?? throw new ArgumentNullException(nameof(exportSettings));
            _availableQuests = availableQuests ?? [];
        }

        /// <summary>
        /// Führt den kompletten End-to-End-Test durch.
        /// </summary>
        public async Task<EndToEndTestResult> RunTestAsync(
            EndToEndTestConfig config,
            CancellationToken cancellationToken = default)
        {
            var result = new EndToEndTestResult();
            var startTime = DateTime.Now;
            var totalSteps = config.ExportToAddon ? 7 : 5;
            var currentStep = 0;

            try
            {
                Log("========================================");
                Log("🚀 STARTE END-TO-END-TEST");
                Log("========================================");
                Log("");

                // === SCHRITT 1: Quest laden ===
                currentStep++;
                ReportProgress(currentStep, totalSteps, "Quest laden...");
                var questStep = new EndToEndTestStep { Name = "Quest laden" };
                var stepStart = DateTime.Now;

                Quest? quest = null;
                if (config.TestQuestId.HasValue)
                {
                    Log($"📋 Lade Quest mit ID {config.TestQuestId.Value}...");
                    quest = _availableQuests.FirstOrDefault(q => q.QuestId == config.TestQuestId.Value);

                    if (quest == null)
                    {
                        throw new InvalidOperationException(
                            $"Quest mit ID {config.TestQuestId.Value} nicht gefunden. " +
                            "Bitte stelle sicher, dass die Quest geladen ist.");
                    }
                }
                else
                {
                    Log("📋 Wähle automatisch eine Test-Quest...");
                    quest = SelectSampleQuest();

                    if (quest == null)
                    {
                        throw new InvalidOperationException(
                            "Keine geeignete Test-Quest gefunden. " +
                            "Bitte lade zuerst Quests ins Tool.");
                    }
                }

                result.TestedQuest = quest;
                questStep.Success = true;
                questStep.Details = $"Quest [{quest.QuestId}] {quest.Title} ({quest.Zone}, {quest.CategoryShortName})";
                questStep.Duration = DateTime.Now - stepStart;
                result.Steps.Add(questStep);

                Log($"✅ Quest gefunden:");
                Log($"   ID: {quest.QuestId}");
                Log($"   Titel: {quest.Title}");
                Log($"   Zone: {quest.Zone}");
                Log($"   Kategorie: {quest.CategoryShortName}");
                Log($"   Ist Hauptquest: {(quest.IsMainStory ? "Ja" : "Nein")}");
                Log("");

                cancellationToken.ThrowIfCancellationRequested();

                // === SCHRITT 2: TTS-Text vorbereiten ===
                currentStep++;
                ReportProgress(currentStep, totalSteps, "TTS-Text vorbereiten...");
                var textStep = new EndToEndTestStep { Name = "TTS-Text vorbereiten" };
                stepStart = DateTime.Now;

                var ttsText = quest.TtsText;
                if (string.IsNullOrWhiteSpace(ttsText))
                {
                    throw new InvalidOperationException(
                        $"Quest {quest.QuestId} hat keinen TTS-Text. " +
                        "Bitte wähle eine Quest mit deutschen Texten.");
                }

                // Text für Log kürzen
                var displayText = ttsText.Length > 200
                    ? ttsText[..200] + "..."
                    : ttsText;

                textStep.Success = true;
                textStep.Details = $"{ttsText.Length} Zeichen";
                textStep.Duration = DateTime.Now - stepStart;
                result.Steps.Add(textStep);

                Log($"📝 TTS-Text vorbereitet:");
                Log($"   Länge: {ttsText.Length} Zeichen");
                Log($"   Vorschau: {displayText}");
                Log("");

                cancellationToken.ThrowIfCancellationRequested();

                // === SCHRITT 3: TTS generieren ===
                currentStep++;
                ReportProgress(currentStep, totalSteps, "TTS-Audio generieren...");
                var ttsStep = new EndToEndTestStep { Name = "TTS-Audio generieren" };
                stepStart = DateTime.Now;

                var voiceId = _exportSettings.MaleVoiceId;
                var genderCode = "male";

                Log($"🎙️ Generiere TTS-Audio...");
                Log($"   Stimme: {genderCode} (ID: {voiceId})");
                Log($"   Provider: {_ttsService.ProviderName}");

                if (!_ttsService.IsConfigured)
                {
                    throw new InvalidOperationException(
                        "TTS-Service ist nicht konfiguriert. " +
                        "Bitte API-Key und Voice-ID in den Einstellungen prüfen.");
                }

                byte[] audioData;
                try
                {
                    audioData = await _ttsService.GenerateMp3Async(
                        ttsText,
                        config.LanguageCode,
                        voiceId);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"TTS-Generierung fehlgeschlagen: {ex.Message}", ex);
                }

                if (audioData == null || audioData.Length == 0)
                {
                    throw new InvalidOperationException(
                        "TTS-Service hat keine Audio-Daten zurückgegeben.");
                }

                ttsStep.Success = true;
                ttsStep.Details = $"{audioData.Length:N0} Bytes ({audioData.Length / 1024.0:F1} KB)";
                ttsStep.Duration = DateTime.Now - stepStart;
                result.Steps.Add(ttsStep);

                Log($"✅ TTS-Audio generiert:");
                Log($"   Größe: {audioData.Length:N0} Bytes ({audioData.Length / 1024.0:F1} KB)");
                Log($"   Dauer: {ttsStep.Duration.TotalSeconds:F1} Sekunden");
                Log("");

                cancellationToken.ThrowIfCancellationRequested();

                // === SCHRITT 4: Audio-Datei speichern ===
                currentStep++;
                ReportProgress(currentStep, totalSteps, "Audio-Datei speichern...");
                var saveStep = new EndToEndTestStep { Name = "Audio-Datei speichern" };
                stepStart = DateTime.Now;

                var audioPath = _exportSettings.GetMaleOutputPath(quest);
                var audioDir = Path.GetDirectoryName(audioPath);

                Log($"💾 Speichere Audio-Datei...");
                Log($"   Ziel: {audioPath}");

                if (!string.IsNullOrEmpty(audioDir) && !Directory.Exists(audioDir))
                {
                    Directory.CreateDirectory(audioDir);
                    Log($"   Ordner erstellt: {audioDir}");
                }

                await File.WriteAllBytesAsync(audioPath, audioData, cancellationToken);

                // Prüfen ob Datei existiert
                if (!File.Exists(audioPath))
                {
                    throw new InvalidOperationException(
                        $"Audio-Datei wurde nicht geschrieben: {audioPath}");
                }

                var fileInfo = new FileInfo(audioPath);
                result.GeneratedAudioPath = audioPath;

                saveStep.Success = true;
                saveStep.Details = audioPath;
                saveStep.Duration = DateTime.Now - stepStart;
                result.Steps.Add(saveStep);

                Log($"✅ Audio-Datei gespeichert:");
                Log($"   Pfad: {audioPath}");
                Log($"   Größe: {fileInfo.Length:N0} Bytes");
                Log("");

                cancellationToken.ThrowIfCancellationRequested();

                // === SCHRITT 5: Audio-Index aktualisieren ===
                currentStep++;
                ReportProgress(currentStep, totalSteps, "Audio-Index aktualisieren...");
                var indexStep = new EndToEndTestStep { Name = "Audio-Index aktualisieren" };
                stepStart = DateTime.Now;

                Log($"📚 Aktualisiere Audio-Index...");

                var audioIndex = AudioIndexWriter.LoadIndex(
                    _exportSettings.OutputRootPath,
                    config.LanguageCode);
                var lookup = AudioIndexWriter.BuildLookupDictionary(audioIndex);

                // Alten Eintrag entfernen falls vorhanden
                AudioIndexWriter.RemoveEntry(audioIndex, quest.QuestId, genderCode);

                // Neuen Eintrag hinzufügen
                AudioIndexWriter.AddEntry(
                    audioIndex,
                    _exportSettings.OutputRootPath,
                    config.LanguageCode,
                    quest,
                    genderCode,
                    audioPath);

                // Index speichern
                AudioIndexWriter.SaveIndex(
                    audioIndex,
                    _exportSettings.OutputRootPath,
                    config.LanguageCode);

                result.AudioIndexUpdated = true;

                indexStep.Success = true;
                indexStep.Details = $"Index hat jetzt {audioIndex.TotalCount} Einträge";
                indexStep.Duration = DateTime.Now - stepStart;
                result.Steps.Add(indexStep);

                Log($"✅ Audio-Index aktualisiert:");
                Log($"   Index-Einträge: {audioIndex.TotalCount}");
                Log($"   Index-Datei: {Path.Combine(_exportSettings.OutputRootPath, config.LanguageCode, "quests_audio_index.json")}");
                Log("");

                // === OPTIONALE SCHRITTE: Addon-Export ===
                if (config.ExportToAddon)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // === SCHRITT 6: WowTts_Data.lua generieren ===
                    currentStep++;
                    ReportProgress(currentStep, totalSteps, "WowTts_Data.lua generieren...");
                    var luaStep = new EndToEndTestStep { Name = "WowTts_Data.lua generieren" };
                    stepStart = DateTime.Now;

                    Log($"📄 Generiere WowTts_Data.lua...");

                    // Addon-Ordner ermitteln
                    var parentDir = Path.GetDirectoryName(_exportSettings.OutputRootPath);
                    var addonFolder = !string.IsNullOrEmpty(parentDir)
                        ? Path.Combine(parentDir, "QuestVoiceover_Addon")
                        : Path.Combine(_exportSettings.OutputRootPath, "addon");

                    if (!Directory.Exists(addonFolder))
                    {
                        Directory.CreateDirectory(addonFolder);
                    }

                    // AddonSettings erstellen
                    var addonSettings = new AddonSettings
                    {
                        AddonName = "QuestVoiceover",
                        AddonVersion = "1.0.0"
                    };

                    // Lua-Generator verwenden
                    var luaGenerator = new AddonLuaGenerator(addonSettings);
                    await luaGenerator.GenerateAllFilesAsync(
                        addonFolder,
                        audioIndex,
                        config.LanguageCode,
                        cancellationToken);

                    result.DataLuaGenerated = true;

                    luaStep.Success = true;
                    luaStep.Details = addonFolder;
                    luaStep.Duration = DateTime.Now - stepStart;
                    result.Steps.Add(luaStep);

                    Log($"✅ WowTts_Data.lua generiert:");
                    Log($"   Addon-Ordner: {addonFolder}");
                    Log("");

                    cancellationToken.ThrowIfCancellationRequested();

                    // === SCHRITT 7: Audio ins Addon kopieren ===
                    currentStep++;
                    ReportProgress(currentStep, totalSteps, "Audio ins Addon kopieren...");
                    var copyStep = new EndToEndTestStep { Name = "Audio ins Addon kopieren" };
                    stepStart = DateTime.Now;

                    Log($"📦 Kopiere Audio ins Addon...");

                    // Ziel-Pfad im Addon
                    var addonAudioFolder = Path.Combine(addonFolder, "Audio", config.LanguageCode);
                    if (!Directory.Exists(addonAudioFolder))
                    {
                        Directory.CreateDirectory(addonAudioFolder);
                    }

                    var addonAudioFileName = $"Quest_{quest.QuestId}_{genderCode}.mp3";
                    var addonAudioPath = Path.Combine(addonAudioFolder, addonAudioFileName);

                    File.Copy(audioPath, addonAudioPath, overwrite: true);
                    result.AddonAudioPath = addonAudioPath;

                    copyStep.Success = true;
                    copyStep.Details = addonAudioPath;
                    copyStep.Duration = DateTime.Now - stepStart;
                    result.Steps.Add(copyStep);

                    Log($"✅ Audio ins Addon kopiert:");
                    Log($"   Ziel: {addonAudioPath}");
                    Log("");
                }

                // === TEST ERFOLGREICH ===
                result.Success = true;
                result.Duration = DateTime.Now - startTime;

                Log("========================================");
                Log("✅ END-TO-END-TEST ERFOLGREICH!");
                Log("========================================");
                Log("");
                Log($"📊 Zusammenfassung:");
                Log($"   Quest: [{quest.QuestId}] {quest.Title}");
                Log($"   Zone: {quest.Zone}");
                Log($"   Audio: {result.GeneratedAudioPath}");
                if (config.ExportToAddon)
                {
                    Log($"   Addon: {result.AddonAudioPath}");
                }
                Log($"   Dauer: {result.Duration.TotalSeconds:F1} Sekunden");
                Log("");
                Log("🎮 Du kannst jetzt WoW starten und die Quest testen!");
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Test wurde abgebrochen.";
                result.Duration = DateTime.Now - startTime;

                Log("");
                Log("⚠️ Test wurde abgebrochen.");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = ex.Message;
                result.Duration = DateTime.Now - startTime;

                Log("");
                Log("========================================");
                Log("❌ END-TO-END-TEST FEHLGESCHLAGEN!");
                Log("========================================");
                Log($"Fehler: {ex.Message}");

                if (ex.InnerException != null)
                {
                    Log($"Details: {ex.InnerException.Message}");
                }
            }

            return result;
        }

        /// <summary>
        /// Wählt eine geeignete Sample-Quest aus der verfügbaren Liste.
        /// Bevorzugt Quests mit deutschen Texten und vollständigen Daten.
        /// </summary>
        private Quest? SelectSampleQuest()
        {
            // Priorität 1: Deutsche Hauptquests mit vollständigen Texten
            var candidates = _availableQuests
                .Where(q => !string.IsNullOrWhiteSpace(q.TtsText))
                .Where(q => q.TtsText.Length > 50) // Mindestens etwas Text
                .Where(q => !string.IsNullOrWhiteSpace(q.Zone))
                .ToList();

            if (candidates.Count == 0)
            {
                // Fallback: Irgendeine Quest mit Text
                return _availableQuests.FirstOrDefault(q => !string.IsNullOrWhiteSpace(q.TtsText));
            }

            // Bevorzuge Hauptquests
            var mainQuests = candidates.Where(q => q.IsMainStory).ToList();
            if (mainQuests.Count > 0)
            {
                return mainQuests[Random.Shared.Next(mainQuests.Count)];
            }

            // Sonst zufällige Quest aus Kandidaten
            return candidates[Random.Shared.Next(candidates.Count)];
        }

        /// <summary>
        /// Schreibt eine Log-Nachricht.
        /// </summary>
        private void Log(string message)
        {
            OnLog?.Invoke(message);
        }

        /// <summary>
        /// Meldet Fortschritt.
        /// </summary>
        private void ReportProgress(int current, int total, string message)
        {
            OnProgress?.Invoke(current, total, message);
        }
    }
}
