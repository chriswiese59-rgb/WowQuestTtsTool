using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Service fuer den Export von Quest-Audio-Dateien als WoW-Addon-Paket.
    /// Erstellt die notwendige Struktur und Lua-Dateien fuer das Addon.
    /// </summary>
    public class AddonExportService
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// Name des Addons (wird in TOC und Lua verwendet).
        /// </summary>
        public string AddonName { get; set; } = "QuestVoiceover";

        /// <summary>
        /// Addon-Version.
        /// </summary>
        public string AddonVersion { get; set; } = "1.0.0";

        /// <summary>
        /// Autor des Addons.
        /// </summary>
        public string AddonAuthor { get; set; } = "WowQuestTtsTool";

        /// <summary>
        /// Addon-Einstellungen fuer erweiterten Export (optional).
        /// Wenn gesetzt, wird der erweiterte AddonLuaGenerator verwendet.
        /// </summary>
        public AddonSettings? Settings { get; set; }

        /// <summary>
        /// Ob der erweiterte Export mit Ingame-Options-UI verwendet werden soll.
        /// </summary>
        public bool UseExtendedExport { get; set; } = false;

        /// <summary>
        /// Exportiert alle vertonten Quests als WoW-Addon-Paket.
        /// </summary>
        /// <param name="sourceRootPath">Quellordner mit den Audio-Dateien</param>
        /// <param name="targetAddonPath">Zielordner fuer das Addon (z.B. WoW/Interface/AddOns/QuestVoiceover)</param>
        /// <param name="languageCode">Sprachcode (z.B. "deDE")</param>
        /// <param name="progress">Fortschritts-Callback</param>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <returns>Export-Ergebnis mit Statistiken</returns>
        public async Task<AddonExportResult> ExportAddonAsync(
            string sourceRootPath,
            string targetAddonPath,
            string languageCode = "deDE",
            IProgress<AddonExportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new AddonExportResult();
            var startTime = DateTime.Now;

            try
            {
                progress?.Report(new AddonExportProgress("Lade Audio-Index...", 0, 0));

                // 1. Audio-Index laden
                var audioIndex = AudioIndexWriter.LoadIndex(sourceRootPath, languageCode);
                if (audioIndex.TotalCount == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "Keine Audio-Dateien im Index gefunden.";
                    return result;
                }

                result.TotalQuestsInIndex = audioIndex.TotalCount;

                // 2. Zielordner erstellen
                progress?.Report(new AddonExportProgress("Erstelle Addon-Struktur...", 0, audioIndex.TotalCount));

                if (!Directory.Exists(targetAddonPath))
                {
                    Directory.CreateDirectory(targetAddonPath);
                }

                // Unterordner fuer Audio
                var audioSubfolder = Path.Combine(targetAddonPath, "Audio", languageCode);
                if (!Directory.Exists(audioSubfolder))
                {
                    Directory.CreateDirectory(audioSubfolder);
                }

                // 3. Audio-Dateien kopieren
                progress?.Report(new AddonExportProgress("Kopiere Audio-Dateien...", 0, audioIndex.TotalCount));

                var copiedFiles = 0;
                var skippedFiles = 0;
                var failedFiles = new List<string>();

                foreach (var entry in audioIndex.Entries)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var sourceFile = Path.Combine(sourceRootPath, entry.RelativePath.Replace('/', Path.DirectorySeparatorChar));

                    if (!File.Exists(sourceFile))
                    {
                        failedFiles.Add($"Quest {entry.QuestId} ({entry.Gender}): Quelldatei nicht gefunden");
                        skippedFiles++;
                        continue;
                    }

                    // Ziel-Dateiname: Quest_{ID}_{gender}.mp3 (flache Struktur im Addon)
                    var targetFileName = $"Quest_{entry.QuestId}_{entry.Gender}.mp3";
                    var targetFile = Path.Combine(audioSubfolder, targetFileName);

                    try
                    {
                        // Nur kopieren wenn Quelle neuer oder Ziel nicht existiert
                        if (!File.Exists(targetFile) ||
                            File.GetLastWriteTimeUtc(sourceFile) > File.GetLastWriteTimeUtc(targetFile))
                        {
                            await Task.Run(() => File.Copy(sourceFile, targetFile, overwrite: true), cancellationToken);
                            copiedFiles++;
                        }
                        else
                        {
                            skippedFiles++;
                        }

                        result.TotalFilesProcessed++;
                    }
                    catch (Exception ex)
                    {
                        failedFiles.Add($"Quest {entry.QuestId} ({entry.Gender}): {ex.Message}");
                    }

                    progress?.Report(new AddonExportProgress(
                        $"Kopiere Audio-Dateien... ({result.TotalFilesProcessed}/{audioIndex.TotalCount})",
                        result.TotalFilesProcessed,
                        audioIndex.TotalCount));
                }

                result.FilesCopied = copiedFiles;
                result.FilesSkipped = skippedFiles;
                result.FailedFiles = failedFiles;

                // 4. Lua-Dateien erstellen
                if (UseExtendedExport && Settings != null)
                {
                    // Erweiterter Export mit Ingame-Options-UI
                    progress?.Report(new AddonExportProgress("Generiere Lua-Dateien (erweitert)...", audioIndex.TotalCount, audioIndex.TotalCount));

                    // Settings aktualisieren
                    Settings.AddonName = AddonName;
                    Settings.AddonVersion = AddonVersion;
                    Settings.AddonAuthor = AddonAuthor;

                    var luaGenerator = new AddonLuaGenerator(Settings);
                    await luaGenerator.GenerateAllFilesAsync(targetAddonPath, audioIndex, languageCode, cancellationToken);
                }
                else
                {
                    // Standard-Export (einfach)
                    progress?.Report(new AddonExportProgress("Erstelle Lua-Mapping...", audioIndex.TotalCount, audioIndex.TotalCount));
                    await CreateLuaMappingFileAsync(audioIndex, targetAddonPath, languageCode, cancellationToken);

                    progress?.Report(new AddonExportProgress("Erstelle TOC-Datei...", audioIndex.TotalCount, audioIndex.TotalCount));
                    await CreateTocFileAsync(targetAddonPath, languageCode, cancellationToken);

                    await CreateCoreLuaFileAsync(targetAddonPath, cancellationToken);
                }

                // 5. Export-Manifest erstellen (JSON fuer Versionierung)
                await CreateExportManifestAsync(audioIndex, targetAddonPath, languageCode, cancellationToken);

                result.Success = true;
                result.Duration = DateTime.Now - startTime;
                result.AddonPath = targetAddonPath;

                progress?.Report(new AddonExportProgress("Export abgeschlossen!", audioIndex.TotalCount, audioIndex.TotalCount));
            }
            catch (OperationCanceledException)
            {
                result.Success = false;
                result.ErrorMessage = "Export wurde abgebrochen.";
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.ErrorMessage = $"Export fehlgeschlagen: {ex.Message}";
            }

            return result;
        }

        /// <summary>
        /// Erstellt die Lua-Datei mit dem Quest-Audio-Mapping.
        /// </summary>
        private async Task CreateLuaMappingFileAsync(
            QuestAudioIndex audioIndex,
            string addonPath,
            string languageCode,
            CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"-- {AddonName} Quest Audio Mapping");
            sb.AppendLine($"-- Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"-- Language: {languageCode}");
            sb.AppendLine($"-- Total Quests: {audioIndex.TotalCount}");
            sb.AppendLine();
            sb.AppendLine($"{AddonName}_QuestData = {AddonName}_QuestData or {{}}");
            sb.AppendLine($"{AddonName}_QuestData[\"{languageCode}\"] = {{");

            // Gruppiere nach QuestId
            var questGroups = audioIndex.Entries
                .GroupBy(e => e.QuestId)
                .OrderBy(g => g.Key);

            foreach (var group in questGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var questId = group.Key;
                var entries = group.ToList();
                var firstEntry = entries[0];

                // Escape Lua-Strings
                var title = EscapeLuaString(firstEntry.Title);
                var zone = EscapeLuaString(firstEntry.Zone);
                var category = EscapeLuaString(firstEntry.Category);

                sb.AppendLine($"    [{questId}] = {{");
                sb.AppendLine($"        title = \"{title}\",");
                sb.AppendLine($"        zone = \"{zone}\",");
                sb.AppendLine($"        category = \"{category}\",");
                sb.AppendLine($"        isMainStory = {(firstEntry.IsMainStory ? "true" : "false")},");
                sb.AppendLine($"        audio = {{");

                foreach (var entry in entries)
                {
                    var fileName = $"Quest_{entry.QuestId}_{entry.Gender}.mp3";
                    sb.AppendLine($"            [\"{entry.Gender}\"] = \"Interface\\\\AddOns\\\\{AddonName}\\\\Audio\\\\{languageCode}\\\\{fileName}\",");
                }

                sb.AppendLine($"        }},");
                sb.AppendLine($"    }},");
            }

            sb.AppendLine("}");

            var luaPath = Path.Combine(addonPath, $"{AddonName}_Data_{languageCode}.lua");
            await File.WriteAllTextAsync(luaPath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Erstellt die TOC-Datei fuer das Addon.
        /// </summary>
        private async Task CreateTocFileAsync(
            string addonPath,
            string languageCode,
            CancellationToken cancellationToken)
        {
            // WoW Interface Version (The War Within = 110002)
            const string interfaceVersion = "110002";

            var sb = new StringBuilder();
            sb.AppendLine($"## Interface: {interfaceVersion}");
            sb.AppendLine($"## Title: {AddonName}");
            sb.AppendLine($"## Notes: Quest voiceover audio for immersive gameplay");
            sb.AppendLine($"## Author: {AddonAuthor}");
            sb.AppendLine($"## Version: {AddonVersion}");
            sb.AppendLine($"## SavedVariables: {AddonName}_Settings");
            sb.AppendLine($"## DefaultState: enabled");
            sb.AppendLine();
            sb.AppendLine($"{AddonName}_Core.lua");
            sb.AppendLine($"{AddonName}_Data_{languageCode}.lua");

            var tocPath = Path.Combine(addonPath, $"{AddonName}.toc");
            await File.WriteAllTextAsync(tocPath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Erstellt die Core Lua-Datei mit der Addon-Logik.
        /// </summary>
        private async Task CreateCoreLuaFileAsync(string addonPath, CancellationToken cancellationToken)
        {
            var luaCode = $@"-- {AddonName} Core
-- Quest Voiceover Addon

local addonName = ""{AddonName}""
local addon = {{}}
_G[addonName] = addon

-- Settings
{AddonName}_Settings = {AddonName}_Settings or {{
    enabled = true,
    volume = 1.0,
    preferredGender = ""male"",  -- ""male"", ""female"", oder ""auto""
    autoPlay = true,
    showNotifications = true,
}}

-- Lokale Referenzen
local settings = {AddonName}_Settings
local questData = {AddonName}_QuestData or {{}}

-- Aktuell abspielender Sound
local currentSoundHandle = nil

-- Hilfsfunktion: Sprache ermitteln
local function GetCurrentLanguage()
    local locale = GetLocale()
    return locale or ""deDE""
end

-- Hilfsfunktion: Audio-Pfad fuer Quest holen
function addon:GetQuestAudioPath(questId, gender)
    local lang = GetCurrentLanguage()
    local langData = questData[lang]

    if not langData then return nil end

    local quest = langData[questId]
    if not quest or not quest.audio then return nil end

    gender = gender or settings.preferredGender or ""male""

    -- Fallback wenn gewuenschtes Gender nicht verfuegbar
    local path = quest.audio[gender]
    if not path then
        -- Versuche alternatives Gender
        path = quest.audio[""male""] or quest.audio[""female""] or quest.audio[""neutral""]
    end

    return path, quest
end

-- Quest-Audio abspielen
function addon:PlayQuestAudio(questId, gender)
    if not settings.enabled then return false end

    local path, questInfo = self:GetQuestAudioPath(questId, gender)
    if not path then return false end

    -- Vorheriges Audio stoppen
    self:StopCurrentAudio()

    -- Neues Audio abspielen
    local willPlay, handle = PlaySoundFile(path, ""Dialog"")

    if willPlay then
        currentSoundHandle = handle

        if settings.showNotifications and questInfo then
            print(string.format(""|cFF00FF00[%s]|r Spiele: %s"", addonName, questInfo.title or ""Quest ""..questId))
        end

        return true
    end

    return false
end

-- Aktuelles Audio stoppen
function addon:StopCurrentAudio()
    if currentSoundHandle then
        StopSound(currentSoundHandle)
        currentSoundHandle = nil
    end
end

-- Quest-Info abrufen
function addon:GetQuestInfo(questId)
    local lang = GetCurrentLanguage()
    local langData = questData[lang]

    if langData then
        return langData[questId]
    end

    return nil
end

-- Prueft ob Quest Audio verfuegbar ist
function addon:HasQuestAudio(questId)
    local path = self:GetQuestAudioPath(questId)
    return path ~= nil
end

-- Event Frame fuer automatisches Abspielen
local eventFrame = CreateFrame(""Frame"")
eventFrame:RegisterEvent(""QUEST_DETAIL"")
eventFrame:RegisterEvent(""QUEST_PROGRESS"")
eventFrame:RegisterEvent(""QUEST_COMPLETE"")

eventFrame:SetScript(""OnEvent"", function(self, event, ...)
    if not settings.autoPlay then return end

    local questId = GetQuestID()
    if questId and questId > 0 then
        addon:PlayQuestAudio(questId)
    end
end)

-- Slash Commands
SLASH_{AddonName:upper()}1 = ""/{AddonName:lower()}""
SLASH_{AddonName:upper()}2 = ""/qv""

SlashCmdList[addonName:upper()] = function(msg)
    local cmd, arg = msg:match(""^(%S+)%s*(.*)$"")
    cmd = cmd and cmd:lower() or msg:lower()

    if cmd == ""play"" and arg then
        local questId = tonumber(arg)
        if questId then
            addon:PlayQuestAudio(questId)
        else
            print(""|cFFFF0000[{AddonName}]|r Ungueltige Quest-ID"")
        end
    elseif cmd == ""stop"" then
        addon:StopCurrentAudio()
        print(""|cFF00FF00[{AddonName}]|r Audio gestoppt"")
    elseif cmd == ""toggle"" then
        settings.enabled = not settings.enabled
        print(string.format(""|cFF00FF00[{AddonName}]|r %s"", settings.enabled and ""Aktiviert"" or ""Deaktiviert""))
    elseif cmd == ""gender"" then
        if arg == ""male"" or arg == ""female"" or arg == ""auto"" then
            settings.preferredGender = arg
            print(string.format(""|cFF00FF00[{AddonName}]|r Bevorzugtes Geschlecht: %s"", arg))
        else
            print(""|cFFFFFF00[{AddonName}]|r Verwendung: /{AddonName:lower()} gender [male|female|auto]"")
        end
    else
        print(""|cFF00FF00[{AddonName}] Befehle:|r"")
        print(""  /{AddonName:lower()} play <questId> - Quest-Audio abspielen"")
        print(""  /{AddonName:lower()} stop - Audio stoppen"")
        print(""  /{AddonName:lower()} toggle - Addon aktivieren/deaktivieren"")
        print(""  /{AddonName:lower()} gender [male|female|auto] - Bevorzugtes Geschlecht"")
    end
end

print(string.format(""|cFF00FF00[%s]|r v%s geladen. Tippe /{AddonName:lower()} fuer Hilfe."", addonName, ""{AddonVersion}""))
";

            var luaPath = Path.Combine(addonPath, $"{AddonName}_Core.lua");
            await File.WriteAllTextAsync(luaPath, luaCode, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Erstellt ein JSON-Manifest fuer Versionierung und Patch-Erkennung.
        /// </summary>
        private async Task CreateExportManifestAsync(
            QuestAudioIndex audioIndex,
            string addonPath,
            string languageCode,
            CancellationToken cancellationToken)
        {
            var manifest = new AddonExportManifest
            {
                AddonName = AddonName,
                Version = AddonVersion,
                Language = languageCode,
                ExportedAtUtc = DateTime.UtcNow,
                TotalQuests = audioIndex.Entries.Select(e => e.QuestId).Distinct().Count(),
                TotalAudioFiles = audioIndex.TotalCount,
                QuestIds = audioIndex.Entries.Select(e => e.QuestId).Distinct().OrderBy(id => id).ToList()
            };

            var json = JsonSerializer.Serialize(manifest, s_jsonOptions);
            var manifestPath = Path.Combine(addonPath, "export_manifest.json");
            await File.WriteAllTextAsync(manifestPath, json, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Vergleicht zwei Manifest-Dateien und gibt neue/geaenderte Quests zurueck.
        /// </summary>
        public static ManifestQuestDiffResult CompareManifests(string oldManifestPath, string newManifestPath)
        {
            var result = new ManifestQuestDiffResult();

            AddonExportManifest? oldManifest = null;
            AddonExportManifest? newManifest = null;

            if (File.Exists(oldManifestPath))
            {
                try
                {
                    var json = File.ReadAllText(oldManifestPath);
                    oldManifest = JsonSerializer.Deserialize<AddonExportManifest>(json);
                }
                catch { /* Ignorieren */ }
            }

            if (File.Exists(newManifestPath))
            {
                try
                {
                    var json = File.ReadAllText(newManifestPath);
                    newManifest = JsonSerializer.Deserialize<AddonExportManifest>(json);
                }
                catch { /* Ignorieren */ }
            }

            if (newManifest == null)
            {
                return result;
            }

            var oldQuestIds = oldManifest?.QuestIds?.ToHashSet() ?? [];
            var newQuestIds = newManifest.QuestIds?.ToHashSet() ?? [];

            // Neue Quests (in neu, aber nicht in alt)
            result.NewQuestIds = newQuestIds.Except(oldQuestIds).OrderBy(id => id).ToList();

            // Entfernte Quests (in alt, aber nicht in neu)
            result.RemovedQuestIds = oldQuestIds.Except(newQuestIds).OrderBy(id => id).ToList();

            // Ungeaenderte Quests
            result.UnchangedQuestIds = oldQuestIds.Intersect(newQuestIds).OrderBy(id => id).ToList();

            return result;
        }

        /// <summary>
        /// Escaped einen String fuer Lua.
        /// </summary>
        private static string EscapeLuaString(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return "";

            return input
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "")
                .Replace("\t", "\\t");
        }
    }

    #region Result & Progress Classes

    /// <summary>
    /// Fortschritts-Information fuer den Addon-Export.
    /// </summary>
    public class AddonExportProgress
    {
        public string Message { get; }
        public int Current { get; }
        public int Total { get; }
        public double Percentage => Total > 0 ? (Current * 100.0 / Total) : 0;

        public AddonExportProgress(string message, int current, int total)
        {
            Message = message;
            Current = current;
            Total = total;
        }
    }

    /// <summary>
    /// Ergebnis des Addon-Exports.
    /// </summary>
    public class AddonExportResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? AddonPath { get; set; }
        public int TotalQuestsInIndex { get; set; }
        public int TotalFilesProcessed { get; set; }
        public int FilesCopied { get; set; }
        public int FilesSkipped { get; set; }
        public List<string> FailedFiles { get; set; } = [];
        public TimeSpan Duration { get; set; }
    }

    /// <summary>
    /// Manifest fuer exportiertes Addon (fuer Versionierung/Diff).
    /// </summary>
    public class AddonExportManifest
    {
        [System.Text.Json.Serialization.JsonPropertyName("addon_name")]
        public string AddonName { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public string Version { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("language")]
        public string Language { get; set; } = "";

        [System.Text.Json.Serialization.JsonPropertyName("exported_at_utc")]
        public DateTime ExportedAtUtc { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("total_quests")]
        public int TotalQuests { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("total_audio_files")]
        public int TotalAudioFiles { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("quest_ids")]
        public List<int> QuestIds { get; set; } = [];
    }

    /// <summary>
    /// Ergebnis eines Quest-Diff zwischen zwei Manifests (einfache Version).
    /// Nur fuer Manifest-Vergleiche verwendet.
    /// </summary>
    public class ManifestQuestDiffResult
    {
        public List<int> NewQuestIds { get; set; } = [];
        public List<int> RemovedQuestIds { get; set; } = [];
        public List<int> UnchangedQuestIds { get; set; } = [];

        public int NewCount => NewQuestIds.Count;
        public int RemovedCount => RemovedQuestIds.Count;
        public int UnchangedCount => UnchangedQuestIds.Count;
        public bool HasChanges => NewCount > 0 || RemovedCount > 0;
    }

    #endregion
}
