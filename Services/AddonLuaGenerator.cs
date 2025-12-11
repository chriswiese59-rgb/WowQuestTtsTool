using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Generiert alle Lua-Dateien fuer das WoW-Addon.
    /// </summary>
    public class AddonLuaGenerator
    {
        private readonly AddonSettings _settings;

        public AddonLuaGenerator(AddonSettings settings)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        }

        /// <summary>
        /// Generiert alle Addon-Dateien (TOC, Core, Options, Frames, Data).
        /// </summary>
        public async Task GenerateAllFilesAsync(
            string addonFolder,
            QuestAudioIndex audioIndex,
            string languageCode = "deDE",
            CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(addonFolder))
            {
                Directory.CreateDirectory(addonFolder);
            }

            // 1. TOC-Datei
            await GenerateTocFileAsync(addonFolder, languageCode, cancellationToken);

            // 2. Data.lua (Audio-Index + Default-Config) - muss zuerst geladen werden
            await GenerateDataFileAsync(addonFolder, audioIndex, languageCode, cancellationToken);

            // 3. Core.lua
            await GenerateCoreFileAsync(addonFolder, cancellationToken);

            // 4. Frames.lua (Kontrollpanel + Minimap-Button)
            await GenerateFramesFileAsync(addonFolder, cancellationToken);

            // 5. Options.lua (vollstaendiges Options-Panel)
            await GenerateOptionsFileAsync(addonFolder, cancellationToken);
        }

        /// <summary>
        /// Generiert die TOC-Datei.
        /// </summary>
        public async Task GenerateTocFileAsync(
            string addonFolder,
            string languageCode,
            CancellationToken cancellationToken)
        {
            var sb = new StringBuilder();
            var name = _settings.AddonName;

            sb.AppendLine($"## Interface: {_settings.InterfaceVersion}");
            sb.AppendLine($"## Title: {name} - Quest Voiceover");
            sb.AppendLine($"## Notes: Spricht Quest-Texte mit TTS-Stimmen vor");
            sb.AppendLine($"## Notes-deDE: Spricht Quest-Texte mit TTS-Stimmen vor");
            sb.AppendLine($"## Notes-enUS: Reads quest texts with TTS voices");
            sb.AppendLine($"## Author: {_settings.AddonAuthor}");
            sb.AppendLine($"## Version: {_settings.AddonVersion}");
            sb.AppendLine($"## SavedVariables: {name}DB");
            sb.AppendLine($"## SavedVariablesPerCharacter: {name}CharDB");
            sb.AppendLine($"## DefaultState: enabled");
            sb.AppendLine($"## IconTexture: Interface\\Icons\\INV_Misc_Book_09");
            sb.AppendLine();
            sb.AppendLine("# Lua files - Reihenfolge wichtig!");
            sb.AppendLine($"{name}_Data.lua");
            sb.AppendLine($"{name}_Core.lua");
            sb.AppendLine($"{name}_Frames.lua");
            sb.AppendLine($"{name}_Options.lua");

            var tocPath = Path.Combine(addonFolder, $"{name}.toc");
            await File.WriteAllTextAsync(tocPath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Generiert die Core.lua mit der Hauptlogik.
        /// </summary>
        public async Task GenerateCoreFileAsync(string addonFolder, CancellationToken cancellationToken)
        {
            var name = _settings.AddonName;
            var nameLower = name.ToLowerInvariant();

            var lua = $@"-- {name} Core
-- Quest Voiceover Addon - Hauptlogik
-- Generiert von WowQuestTtsTool
-- ============================================================================

local addonName, addon = ...
_G[addonName] = addon

-- ============================================================================
-- Lokale Variablen & State
-- ============================================================================

local currentSoundHandle = nil
local lastPlayedQuestId = nil
local lastQuestId = nil          -- Letzte angezeigte Quest
local lastQuestTitle = nil       -- Titel der letzten Quest
local isCurrentlyPlaying = false
local db = nil                   -- Account-weite SavedVariables
local charDb = nil               -- Charakter-spezifische SavedVariables

-- Callbacks fuer UI-Updates
addon.callbacks = {{}}

-- ============================================================================
-- Event Frame & Registration
-- ============================================================================

local eventFrame = CreateFrame(""Frame"")
eventFrame:RegisterEvent(""ADDON_LOADED"")
eventFrame:RegisterEvent(""PLAYER_LOGIN"")
eventFrame:RegisterEvent(""PLAYER_LOGOUT"")

-- Quest-Events
eventFrame:RegisterEvent(""QUEST_DETAIL"")
eventFrame:RegisterEvent(""QUEST_PROGRESS"")
eventFrame:RegisterEvent(""QUEST_COMPLETE"")
eventFrame:RegisterEvent(""QUEST_ACCEPTED"")
eventFrame:RegisterEvent(""QUEST_FINISHED"")
eventFrame:RegisterEvent(""QUEST_REMOVED"")

-- ============================================================================
-- Datenbank / Config Management
-- ============================================================================

-- Deep-Copy fuer Tabellen
local function DeepCopy(orig)
    local copy
    if type(orig) == ""table"" then
        copy = {{}}
        for k, v in pairs(orig) do
            copy[k] = DeepCopy(v)
        end
    else
        copy = orig
    end
    return copy
end

-- Initialisiert die SavedVariables mit Defaults
local function InitializeDB()
    -- Account-weite Einstellungen
    if not {name}DB then
        {name}DB = {{}}
    end

    -- Charakter-spezifische Einstellungen
    if not {name}CharDB then
        {name}CharDB = {{}}
    end

    -- Defaults setzen falls nicht vorhanden
    local defaults = {name}ConfigDefaults or {{}}

    for key, value in pairs(defaults) do
        if {name}DB[key] == nil then
            if type(value) == ""table"" then
                {name}DB[key] = DeepCopy(value)
            else
                {name}DB[key] = value
            end
        end
    end

    -- Frame-Position initialisieren
    if not {name}CharDB.framePosition then
        {name}CharDB.framePosition = {{ point = ""RIGHT"", x = -50, y = 0 }}
    end

    if {name}CharDB.controlPanelShown == nil then
        {name}CharDB.controlPanelShown = true
    end

    db = {name}DB
    charDb = {name}CharDB
end

-- Config-Getter (Account-weit)
function addon:GetConfig(key)
    if db and db[key] ~= nil then
        return db[key]
    end
    if {name}ConfigDefaults and {name}ConfigDefaults[key] ~= nil then
        return {name}ConfigDefaults[key]
    end
    return nil
end

-- Config-Setter (Account-weit)
function addon:SetConfig(key, value)
    if db then
        db[key] = value
        self:FireCallback(""CONFIG_CHANGED"", key, value)
    end
end

-- Charakter-spezifischer Config-Getter
function addon:GetCharConfig(key)
    if charDb and charDb[key] ~= nil then
        return charDb[key]
    end
    return nil
end

-- Charakter-spezifischer Config-Setter
function addon:SetCharConfig(key, value)
    if charDb then
        charDb[key] = value
    end
end

-- Setzt alle Einstellungen auf Defaults zurueck
function addon:ResetToDefaults()
    local defaults = {name}ConfigDefaults or {{}}
    for key, value in pairs(defaults) do
        if type(value) == ""table"" then
            db[key] = DeepCopy(value)
        else
            db[key] = value
        end
    end
    self:FireCallback(""CONFIG_RESET"")
end

-- ============================================================================
-- Callback-System fuer UI-Updates
-- ============================================================================

function addon:RegisterCallback(event, callback)
    if not self.callbacks[event] then
        self.callbacks[event] = {{}}
    end
    table.insert(self.callbacks[event], callback)
end

function addon:FireCallback(event, ...)
    if self.callbacks[event] then
        for _, callback in ipairs(self.callbacks[event]) do
            pcall(callback, ...)
        end
    end
end

-- ============================================================================
-- Quest-Tracking
-- ============================================================================

function addon:GetLastQuestId()
    return lastQuestId
end

function addon:GetLastQuestTitle()
    return lastQuestTitle
end

function addon:GetLastPlayedQuestId()
    return lastPlayedQuestId
end

function addon:SetCurrentQuest(questId, title)
    lastQuestId = questId
    lastQuestTitle = title
    self:FireCallback(""QUEST_CHANGED"", questId, title)
end

-- ============================================================================
-- Audio-Index & Abfragen
-- ============================================================================

function addon:GetQuestAudio(questId)
    local index = {name}AudioIndex
    if not index then return nil end
    return index[questId]
end

function addon:HasQuestAudio(questId)
    return self:GetQuestAudio(questId) ~= nil
end

function addon:GetAudioPath(questId, gender)
    local questData = self:GetQuestAudio(questId)
    if not questData or not questData.files then return nil, nil end

    -- Gender ermitteln
    gender = gender or self:GetConfig(""defaultVoice"") or ""male""

    -- Bei ""auto"" Spielergeschlecht verwenden
    if gender == ""auto"" then
        local _, _, raceId = UnitRace(""player"")
        local sex = UnitSex(""player"") -- 2 = male, 3 = female
        gender = (sex == 3) and ""female"" or ""male""
    end

    -- Fallback falls gewuenschtes Gender nicht verfuegbar
    local path = questData.files[gender]
    if not path then
        path = questData.files[""male""] or questData.files[""female""]
    end

    return path, questData
end

-- Zaehlt verfuegbare Quests im Index
function addon:GetQuestCount()
    local index = {name}AudioIndex or {{}}
    local count = 0
    for _ in pairs(index) do
        count = count + 1
    end
    return count
end

-- Gibt alle Zonen zurueck, die Quests haben
function addon:GetAvailableZones()
    local zones = {{}}
    local index = {name}AudioIndex or {{}}
    for _, questData in pairs(index) do
        if questData.zone and questData.zone ~= """" then
            zones[questData.zone] = (zones[questData.zone] or 0) + 1
        end
    end
    return zones
end

-- ============================================================================
-- Quest-Filter-Logik
-- ============================================================================

function addon:ShouldPlayQuest(questData)
    if not self:GetConfig(""enableTts"") then return false end
    if not questData then return false end

    local category = questData.category or ""Side""

    -- Hauptquests immer (wenn TTS aktiv)
    if category == ""Main"" then
        return true
    end

    -- Nur Hauptquests?
    if self:GetConfig(""onlyMainQuests"") then
        return false
    end

    -- Kategorie-Filter pruefen
    local categoryFilters = {{
        Side = ""includeSideQuests"",
        Group = ""includeGroupQuests"",
        Dungeon = ""includeDungeonQuests"",
        Raid = ""includeRaidQuests"",
        Daily = ""includeDailyQuests"",
        World = ""includeWorldQuests"",
    }}

    local filterKey = categoryFilters[category]
    if filterKey and not self:GetConfig(filterKey) then
        return false
    end

    -- Zonen-Filter pruefen
    local zonesEnabled = self:GetConfig(""zonesEnabled"")
    if zonesEnabled and questData.zone then
        local zoneEnabled = zonesEnabled[questData.zone]
        if zoneEnabled == false then
            return false
        end
    end

    return true
end

-- ============================================================================
-- Audio-Wiedergabe
-- ============================================================================

function addon:PlayQuestAudio(questId, forceGender)
    if not questId then
        questId = lastQuestId or GetQuestID()
    end

    if not questId or questId == 0 then
        return false, ""Keine Quest-ID""
    end

    local path, questData = self:GetAudioPath(questId, forceGender)
    if not path then
        return false, ""Kein Audio verfuegbar""
    end

    if not self:ShouldPlayQuest(questData) then
        return false, ""Quest durch Filter blockiert""
    end

    -- Vorheriges Audio stoppen (wenn nicht Overlap erlaubt)
    if not self:GetConfig(""allowOverlap"") then
        self:StopAudio()
    end

    -- Sound-Kanal ermitteln
    local channel = self:GetConfig(""soundChannel"") or ""Dialog""

    -- Audio abspielen
    local willPlay, handle = PlaySoundFile(path, channel)

    if willPlay then
        currentSoundHandle = handle
        lastPlayedQuestId = questId
        isCurrentlyPlaying = true

        -- UI informieren
        self:FireCallback(""PLAYBACK_STARTED"", questId, questData)

        -- Chat-Benachrichtigung
        if self:GetConfig(""showNotifications"") and questData then
            local title = questData.title or (""Quest "" .. questId)
            print(string.format(""|cFF00FF00[{name}]|r Spiele: %s"", title))
        end

        return true
    end

    return false, ""PlaySoundFile fehlgeschlagen""
end

function addon:StopAudio()
    if currentSoundHandle then
        StopSound(currentSoundHandle)
        currentSoundHandle = nil
        isCurrentlyPlaying = false
        self:FireCallback(""PLAYBACK_STOPPED"")
    end
end

function addon:ReplayAudio()
    if lastPlayedQuestId then
        return self:PlayQuestAudio(lastPlayedQuestId)
    end
    return false, ""Keine vorherige Quest""
end

function addon:IsPlaying()
    return isCurrentlyPlaying and currentSoundHandle ~= nil
end

-- Test-Funktionen fuer Options-Panel
function addon:TestVoice(gender)
    -- Versuche erste verfuegbare Quest zu finden
    local index = {name}AudioIndex or {{}}
    for questId, questData in pairs(index) do
        if questData.files and questData.files[gender] then
            local path = questData.files[gender]
            local channel = self:GetConfig(""soundChannel"") or ""Dialog""
            self:StopAudio()
            local willPlay, handle = PlaySoundFile(path, channel)
            if willPlay then
                currentSoundHandle = handle
                isCurrentlyPlaying = true
                self:FireCallback(""PLAYBACK_STARTED"", questId, questData)
                return true
            end
        end
    end
    return false
end

-- ============================================================================
-- Event-Handler
-- ============================================================================

local function OnQuestEvent(questId, eventType)
    if not questId or questId == 0 then
        questId = GetQuestID()
    end

    if questId and questId > 0 then
        -- Quest-Daten abrufen
        local questData = addon:GetQuestAudio(questId)
        local title = questData and questData.title or (""Quest "" .. questId)

        -- Aktuelle Quest setzen
        addon:SetCurrentQuest(questId, title)

        -- Audio abspielen je nach Modus
        local mode = addon:GetConfig(""playbackMode"")

        if eventType == ""QUEST_DETAIL"" then
            if mode == ""AutoOnQuestOpen"" then
                addon:PlayQuestAudio(questId)
            end
        elseif eventType == ""QUEST_ACCEPTED"" then
            if mode == ""AutoOnAccept"" then
                addon:PlayQuestAudio(questId)
            end
        elseif eventType == ""QUEST_PROGRESS"" then
            if addon:GetConfig(""playOnQuestProgress"") then
                addon:PlayQuestAudio(questId)
            end
        elseif eventType == ""QUEST_COMPLETE"" then
            if addon:GetConfig(""playOnQuestComplete"") then
                addon:PlayQuestAudio(questId)
            end
        end
    end
end

eventFrame:SetScript(""OnEvent"", function(self, event, arg1, ...)
    if event == ""ADDON_LOADED"" and arg1 == addonName then
        InitializeDB()

        -- Frames initialisieren (falls vorhanden)
        if addon.InitializeFrames then
            addon:InitializeFrames()
        end

        -- Options-Panel registrieren
        if addon.InitializeOptions then
            addon:InitializeOptions()
        end

        print(string.format(""|cFF00FF00[{name}]|r v{_settings.AddonVersion} geladen. Tippe /{nameLower} fuer Hilfe.""))

    elseif event == ""PLAYER_LOGIN"" then
        -- Kontrollpanel anzeigen falls aktiviert
        if addon.ShowControlPanel and charDb and charDb.controlPanelShown then
            C_Timer.After(1, function()
                addon:ShowControlPanel()
            end)
        end

    elseif event == ""QUEST_DETAIL"" then
        OnQuestEvent(nil, ""QUEST_DETAIL"")

    elseif event == ""QUEST_ACCEPTED"" then
        OnQuestEvent(arg1, ""QUEST_ACCEPTED"")

    elseif event == ""QUEST_PROGRESS"" then
        OnQuestEvent(nil, ""QUEST_PROGRESS"")

    elseif event == ""QUEST_COMPLETE"" then
        OnQuestEvent(nil, ""QUEST_COMPLETE"")

    elseif event == ""QUEST_FINISHED"" then
        if addon:GetConfig(""stopOnQuestClose"") then
            addon:StopAudio()
        end

    elseif event == ""QUEST_REMOVED"" then
        -- Quest wurde aufgegeben
        if arg1 == lastQuestId then
            addon:SetCurrentQuest(nil, nil)
        end
    end
end)

-- ============================================================================
-- Slash-Commands
-- ============================================================================

SLASH_{name.ToUpperInvariant()}1 = ""/{nameLower}""
SLASH_{name.ToUpperInvariant()}2 = ""/tts""

SlashCmdList[""{name.ToUpperInvariant()}""] = function(msg)
    local cmd, arg = msg:match(""^(%S+)%s*(.*)$"")
    cmd = cmd and cmd:lower() or msg:lower()

    if cmd == """" or cmd == ""help"" then
        print(""|cFF00FF00[{name}] Befehle:|r"")
        print(""  /{nameLower} - Kontrollpanel ein/ausblenden"")
        print(""  /{nameLower} play [questId] - Quest-Audio abspielen"")
        print(""  /{nameLower} stop - Audio stoppen"")
        print(""  /{nameLower} replay - Letztes Audio wiederholen"")
        print(""  /{nameLower} toggle - TTS an/aus"")
        print(""  /{nameLower} voice [male|female|auto] - Stimme waehlen"")
        print(""  /{nameLower} options - Einstellungen oeffnen"")
        print(""  /{nameLower} status - Status anzeigen"")
        print(""  /{nameLower} reset - Einstellungen zuruecksetzen"")

    elseif cmd == ""play"" then
        local questId = tonumber(arg)
        local success, err = addon:PlayQuestAudio(questId)
        if success then
            print(""|cFF00FF00[{name}]|r Audio gestartet"")
        else
            print(string.format(""|cFFFF0000[{name}]|r %s"", err or ""Fehler""))
        end

    elseif cmd == ""stop"" then
        addon:StopAudio()
        print(""|cFF00FF00[{name}]|r Audio gestoppt"")

    elseif cmd == ""replay"" then
        local success, err = addon:ReplayAudio()
        if success then
            print(""|cFF00FF00[{name}]|r Wiederholung gestartet"")
        else
            print(string.format(""|cFFFF0000[{name}]|r %s"", err or ""Fehler""))
        end

    elseif cmd == ""toggle"" then
        local enabled = not addon:GetConfig(""enableTts"")
        addon:SetConfig(""enableTts"", enabled)
        print(string.format(""|cFF00FF00[{name}]|r TTS %s"", enabled and ""aktiviert"" or ""deaktiviert""))

    elseif cmd == ""voice"" then
        if arg == ""male"" or arg == ""female"" or arg == ""auto"" then
            addon:SetConfig(""defaultVoice"", arg)
            local names = {{ male = ""Maennlich"", female = ""Weiblich"", auto = ""Automatisch"" }}
            print(string.format(""|cFF00FF00[{name}]|r Stimme: %s"", names[arg]))
        else
            print(""|cFFFFFF00[{name}]|r Verwendung: /{nameLower} voice [male|female|auto]"")
        end

    elseif cmd == ""options"" or cmd == ""config"" then
        Settings.OpenToCategory(""{name}"")

    elseif cmd == ""status"" then
        print(""|cFF00FF00[{name}] Status:|r"")
        print(string.format(""  TTS: %s"", addon:GetConfig(""enableTts"") and ""|cFF00FF00Aktiv|r"" or ""|cFFFF0000Inaktiv|r""))
        local voiceNames = {{ male = ""Maennlich"", female = ""Weiblich"", auto = ""Automatisch"" }}
        print(string.format(""  Stimme: %s"", voiceNames[addon:GetConfig(""defaultVoice"")] or ""Maennlich""))
        local modeNames = {{ AutoOnAccept = ""Auto bei Annahme"", AutoOnQuestOpen = ""Auto beim Oeffnen"", ManualOnly = ""Manuell"" }}
        print(string.format(""  Modus: %s"", modeNames[addon:GetConfig(""playbackMode"")] or ""Auto bei Annahme""))
        print(string.format(""  Verfuegbare Quests: %d"", addon:GetQuestCount()))
        if lastQuestTitle then
            print(string.format(""  Aktuelle Quest: %s"", lastQuestTitle))
        end

    elseif cmd == ""reset"" then
        addon:ResetToDefaults()
        print(""|cFF00FF00[{name}]|r Einstellungen zurueckgesetzt"")

    else
        -- Ohne Argument: Panel togglen
        if addon.ToggleControlPanel then
            addon:ToggleControlPanel()
        else
            print(""|cFFFFFF00[{name}]|r Unbekannter Befehl. Tippe /{nameLower} help"")
        end
    end
end

-- ============================================================================
-- API fuer andere Addons
-- ============================================================================

function addon:API_PlayQuest(questId, gender)
    return self:PlayQuestAudio(questId, gender)
end

function addon:API_StopAudio()
    self:StopAudio()
end

function addon:API_HasAudio(questId)
    return self:HasQuestAudio(questId)
end

function addon:API_GetQuestCount()
    return self:GetQuestCount()
end

function addon:API_IsPlaying()
    return self:IsPlaying()
end
";

            var corePath = Path.Combine(addonFolder, $"{name}_Core.lua");
            await File.WriteAllTextAsync(corePath, lua, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Generiert die Frames.lua mit Kontrollpanel und Minimap-Button.
        /// </summary>
        public async Task GenerateFramesFileAsync(string addonFolder, CancellationToken cancellationToken)
        {
            var name = _settings.AddonName;
            var nameLower = name.ToLowerInvariant();

            var lua = $@"-- {name} Frames
-- Quest Voiceover Addon - UI Frames (Kontrollpanel, Minimap-Button)
-- Generiert von WowQuestTtsTool
-- ============================================================================

local addonName, addon = ...

-- ============================================================================
-- Lokale Variablen
-- ============================================================================

local controlPanel = nil
local minimapButton = nil
local isPlayingIndicator = nil

-- ============================================================================
-- Kontrollpanel (TTS Control Panel)
-- ============================================================================

local function CreateControlPanel()
    -- Hauptframe erstellen
    local frame = CreateFrame(""Frame"", ""{name}ControlPanel"", UIParent, ""BackdropTemplate"")
    frame:SetSize(220, 140)
    frame:SetPoint(""RIGHT"", UIParent, ""RIGHT"", -50, 0)
    frame:SetMovable(true)
    frame:EnableMouse(true)
    frame:SetClampedToScreen(true)
    frame:RegisterForDrag(""LeftButton"")

    -- Backdrop (Hintergrund)
    frame:SetBackdrop({{
        bgFile = ""Interface\\DialogFrame\\UI-DialogBox-Background"",
        edgeFile = ""Interface\\DialogFrame\\UI-DialogBox-Border"",
        tile = true, tileSize = 32, edgeSize = 16,
        insets = {{ left = 4, right = 4, top = 4, bottom = 4 }}
    }})
    frame:SetBackdropColor(0, 0, 0, 0.9)

    -- Drag-Funktionalitaet
    frame:SetScript(""OnDragStart"", function(self)
        self:StartMoving()
    end)

    frame:SetScript(""OnDragStop"", function(self)
        self:StopMovingOrSizing()
        -- Position speichern
        local point, _, _, x, y = self:GetPoint()
        addon:SetCharConfig(""framePosition"", {{ point = point, x = x, y = y }})
    end)

    -- ==================== Titel-Leiste ====================

    local titleBar = CreateFrame(""Frame"", nil, frame)
    titleBar:SetSize(220, 24)
    titleBar:SetPoint(""TOP"", frame, ""TOP"", 0, -4)

    -- Titel-Text
    local titleText = titleBar:CreateFontString(nil, ""OVERLAY"", ""GameFontNormalSmall"")
    titleText:SetPoint(""LEFT"", titleBar, ""LEFT"", 8, 0)
    titleText:SetText(""|cFF00FF00{name}|r"")

    -- Playing-Indikator (kleiner gruener Punkt)
    isPlayingIndicator = titleBar:CreateTexture(nil, ""OVERLAY"")
    isPlayingIndicator:SetSize(10, 10)
    isPlayingIndicator:SetPoint(""LEFT"", titleText, ""RIGHT"", 5, 0)
    isPlayingIndicator:SetTexture(""Interface\\COMMON\\Indicator-Green"")
    isPlayingIndicator:Hide()

    -- Schliessen-Button
    local closeBtn = CreateFrame(""Button"", nil, titleBar, ""UIPanelCloseButton"")
    closeBtn:SetSize(20, 20)
    closeBtn:SetPoint(""RIGHT"", titleBar, ""RIGHT"", -2, 0)
    closeBtn:SetScript(""OnClick"", function()
        addon:HideControlPanel()
    end)

    -- ==================== Quest-Info ====================

    local questLabel = frame:CreateFontString(nil, ""OVERLAY"", ""GameFontHighlightSmall"")
    questLabel:SetPoint(""TOPLEFT"", frame, ""TOPLEFT"", 10, -32)
    questLabel:SetText(""Aktive Quest:"")

    local questTitle = frame:CreateFontString(nil, ""OVERLAY"", ""GameFontNormalSmall"")
    questTitle:SetPoint(""TOPLEFT"", questLabel, ""BOTTOMLEFT"", 0, -2)
    questTitle:SetWidth(200)
    questTitle:SetJustifyH(""LEFT"")
    questTitle:SetText(""|cFF888888Keine Quest aktiv|r"")
    frame.questTitle = questTitle

    -- ==================== Buttons ====================

    local buttonWidth = 60
    local buttonHeight = 22

    -- Play-Button
    local playBtn = CreateFrame(""Button"", nil, frame, ""UIPanelButtonTemplate"")
    playBtn:SetSize(buttonWidth, buttonHeight)
    playBtn:SetPoint(""TOPLEFT"", frame, ""TOPLEFT"", 10, -72)
    playBtn:SetText(""|TInterface\\Buttons\\UI-SpellbookIcon-NextPage-Up:14|t"")
    playBtn:SetScript(""OnClick"", function()
        local success, err = addon:PlayQuestAudio()
        if not success and err then
            print(string.format(""|cFFFF0000[{name}]|r %s"", err))
        end
    end)
    playBtn:SetScript(""OnEnter"", function(self)
        GameTooltip:SetOwner(self, ""ANCHOR_TOP"")
        GameTooltip:SetText(""Abspielen"", 1, 1, 1)
        GameTooltip:AddLine(""Spielt das Audio der aktuellen Quest ab"", nil, nil, nil, true)
        GameTooltip:Show()
    end)
    playBtn:SetScript(""OnLeave"", function() GameTooltip:Hide() end)

    -- Stop-Button
    local stopBtn = CreateFrame(""Button"", nil, frame, ""UIPanelButtonTemplate"")
    stopBtn:SetSize(buttonWidth, buttonHeight)
    stopBtn:SetPoint(""LEFT"", playBtn, ""RIGHT"", 5, 0)
    stopBtn:SetText(""|TInterface\\Buttons\\UI-StopButton:14|t"")
    stopBtn:SetScript(""OnClick"", function()
        addon:StopAudio()
    end)
    stopBtn:SetScript(""OnEnter"", function(self)
        GameTooltip:SetOwner(self, ""ANCHOR_TOP"")
        GameTooltip:SetText(""Stoppen"", 1, 1, 1)
        GameTooltip:AddLine(""Stoppt die aktuelle Wiedergabe"", nil, nil, nil, true)
        GameTooltip:Show()
    end)
    stopBtn:SetScript(""OnLeave"", function() GameTooltip:Hide() end)

    -- Replay-Button
    local replayBtn = CreateFrame(""Button"", nil, frame, ""UIPanelButtonTemplate"")
    replayBtn:SetSize(buttonWidth, buttonHeight)
    replayBtn:SetPoint(""LEFT"", stopBtn, ""RIGHT"", 5, 0)
    replayBtn:SetText(""|TInterface\\Buttons\\UI-RefreshButton:14|t"")
    replayBtn:SetScript(""OnClick"", function()
        local success, err = addon:ReplayAudio()
        if not success and err then
            print(string.format(""|cFFFF0000[{name}]|r %s"", err))
        end
    end)
    replayBtn:SetScript(""OnEnter"", function(self)
        GameTooltip:SetOwner(self, ""ANCHOR_TOP"")
        GameTooltip:SetText(""Wiederholen"", 1, 1, 1)
        GameTooltip:AddLine(""Spielt das letzte Audio erneut ab"", nil, nil, nil, true)
        GameTooltip:Show()
    end)
    replayBtn:SetScript(""OnLeave"", function() GameTooltip:Hide() end)

    -- ==================== Auto-TTS Checkbox ====================

    local autoCheckbox = CreateFrame(""CheckButton"", nil, frame, ""ChatConfigCheckButtonTemplate"")
    autoCheckbox:SetPoint(""TOPLEFT"", playBtn, ""BOTTOMLEFT"", -2, -8)
    autoCheckbox.Text:SetText(""Auto-TTS"")
    autoCheckbox.Text:SetFontObject(""GameFontNormalSmall"")
    autoCheckbox:SetChecked(addon:GetConfig(""enableTts"") or false)
    autoCheckbox:SetScript(""OnClick"", function(self)
        addon:SetConfig(""enableTts"", self:GetChecked())
    end)
    autoCheckbox:SetScript(""OnEnter"", function(self)
        GameTooltip:SetOwner(self, ""ANCHOR_TOP"")
        GameTooltip:SetText(""Auto-TTS"", 1, 1, 1)
        GameTooltip:AddLine(""Aktiviert/deaktiviert automatische Wiedergabe"", nil, nil, nil, true)
        GameTooltip:Show()
    end)
    autoCheckbox:SetScript(""OnLeave"", function() GameTooltip:Hide() end)
    frame.autoCheckbox = autoCheckbox

    -- Options-Button
    local optionsBtn = CreateFrame(""Button"", nil, frame, ""UIPanelButtonTemplate"")
    optionsBtn:SetSize(80, buttonHeight)
    optionsBtn:SetPoint(""LEFT"", autoCheckbox, ""RIGHT"", 20, 0)
    optionsBtn:SetText(""Optionen"")
    optionsBtn:SetScript(""OnClick"", function()
        Settings.OpenToCategory(""{name}"")
    end)

    -- ==================== Speichern ====================

    controlPanel = frame
    return frame
end

-- Position aus SavedVariables wiederherstellen
local function RestorePosition()
    if controlPanel then
        local pos = addon:GetCharConfig(""framePosition"")
        if pos then
            controlPanel:ClearAllPoints()
            controlPanel:SetPoint(pos.point or ""RIGHT"", UIParent, pos.point or ""RIGHT"", pos.x or -50, pos.y or 0)
        end
    end
end

-- ============================================================================
-- Kontrollpanel API
-- ============================================================================

function addon:InitializeFrames()
    if not controlPanel then
        CreateControlPanel()
        RestorePosition()
    end

    -- Callbacks registrieren
    self:RegisterCallback(""QUEST_CHANGED"", function(questId, title)
        if controlPanel and controlPanel.questTitle then
            if title then
                controlPanel.questTitle:SetText(""|cFFFFFFFF"" .. title .. ""|r"")
            else
                controlPanel.questTitle:SetText(""|cFF888888Keine Quest aktiv|r"")
            end
        end
    end)

    self:RegisterCallback(""PLAYBACK_STARTED"", function()
        if isPlayingIndicator then
            isPlayingIndicator:Show()
        end
    end)

    self:RegisterCallback(""PLAYBACK_STOPPED"", function()
        if isPlayingIndicator then
            isPlayingIndicator:Hide()
        end
    end)

    self:RegisterCallback(""CONFIG_CHANGED"", function(key, value)
        if key == ""enableTts"" and controlPanel and controlPanel.autoCheckbox then
            controlPanel.autoCheckbox:SetChecked(value)
        end
    end)
end

function addon:ShowControlPanel()
    if not controlPanel then
        CreateControlPanel()
        RestorePosition()
    end
    controlPanel:Show()
    addon:SetCharConfig(""controlPanelShown"", true)
end

function addon:HideControlPanel()
    if controlPanel then
        controlPanel:Hide()
    end
    addon:SetCharConfig(""controlPanelShown"", false)
end

function addon:ToggleControlPanel()
    if controlPanel and controlPanel:IsShown() then
        self:HideControlPanel()
    else
        self:ShowControlPanel()
    end
end

function addon:IsControlPanelShown()
    return controlPanel and controlPanel:IsShown()
end

-- ============================================================================
-- Minimap-Button
-- ============================================================================

local function CreateMinimapButton()
    local btn = CreateFrame(""Button"", ""{name}MinimapButton"", Minimap)
    btn:SetSize(32, 32)
    btn:SetFrameStrata(""MEDIUM"")
    btn:SetFrameLevel(8)

    -- Position (45 Grad vom Minimap-Rand)
    local angle = math.rad(225)
    local x = math.cos(angle) * 80
    local y = math.sin(angle) * 80
    btn:SetPoint(""CENTER"", Minimap, ""CENTER"", x, y)

    btn:SetMovable(true)
    btn:EnableMouse(true)
    btn:RegisterForClicks(""LeftButtonUp"", ""RightButtonUp"")
    btn:RegisterForDrag(""LeftButton"")

    -- Icon
    local icon = btn:CreateTexture(nil, ""BACKGROUND"")
    icon:SetSize(20, 20)
    icon:SetPoint(""CENTER"")
    icon:SetTexture(""Interface\\Icons\\INV_Misc_Book_09"")

    -- Border
    local border = btn:CreateTexture(nil, ""OVERLAY"")
    border:SetSize(54, 54)
    border:SetPoint(""CENTER"")
    border:SetTexture(""Interface\\Minimap\\MiniMap-TrackingBorder"")

    -- Highlight
    local highlight = btn:CreateTexture(nil, ""HIGHLIGHT"")
    highlight:SetSize(24, 24)
    highlight:SetPoint(""CENTER"")
    highlight:SetTexture(""Interface\\Minimap\\UI-Minimap-ZoomButton-Highlight"")

    -- Drag um Minimap
    local isDragging = false
    btn:SetScript(""OnDragStart"", function(self)
        isDragging = true
        self:SetScript(""OnUpdate"", function(self)
            local mx, my = Minimap:GetCenter()
            local px, py = GetCursorPosition()
            local scale = Minimap:GetEffectiveScale()
            px, py = px / scale, py / scale
            local angle = math.atan2(py - my, px - mx)
            local x = math.cos(angle) * 80
            local y = math.sin(angle) * 80
            self:ClearAllPoints()
            self:SetPoint(""CENTER"", Minimap, ""CENTER"", x, y)
            -- Position speichern
            addon:SetCharConfig(""minimapAngle"", math.deg(angle))
        end)
    end)

    btn:SetScript(""OnDragStop"", function(self)
        isDragging = false
        self:SetScript(""OnUpdate"", nil)
    end)

    -- Klick-Handler
    btn:SetScript(""OnClick"", function(self, button)
        if button == ""LeftButton"" then
            addon:ToggleControlPanel()
        elseif button == ""RightButton"" then
            Settings.OpenToCategory(""{name}"")
        end
    end)

    -- Tooltip
    btn:SetScript(""OnEnter"", function(self)
        GameTooltip:SetOwner(self, ""ANCHOR_LEFT"")
        GameTooltip:SetText(""|cFF00FF00{name}|r"", 1, 1, 1)
        GameTooltip:AddLine(""Quest Voiceover"", nil, nil, nil, true)
        GameTooltip:AddLine("" "")
        GameTooltip:AddLine(""|cFFFFFFFFLinksklick:|r Kontrollpanel"", nil, nil, nil, true)
        GameTooltip:AddLine(""|cFFFFFFFFRechtsklick:|r Einstellungen"", nil, nil, nil, true)
        GameTooltip:Show()
    end)

    btn:SetScript(""OnLeave"", function()
        GameTooltip:Hide()
    end)

    -- Position wiederherstellen
    local savedAngle = addon:GetCharConfig(""minimapAngle"")
    if savedAngle then
        local angle = math.rad(savedAngle)
        local x = math.cos(angle) * 80
        local y = math.sin(angle) * 80
        btn:ClearAllPoints()
        btn:SetPoint(""CENTER"", Minimap, ""CENTER"", x, y)
    end

    minimapButton = btn
    return btn
end

-- Minimap-Button erstellen beim Laden
C_Timer.After(0, function()
    if not minimapButton then
        CreateMinimapButton()
    end
end)
";

            var framesPath = Path.Combine(addonFolder, $"{name}_Frames.lua");
            await File.WriteAllTextAsync(framesPath, lua, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Generiert die Options.lua mit dem Einstellungs-UI.
        /// </summary>
        public async Task GenerateOptionsFileAsync(string addonFolder, CancellationToken cancellationToken)
        {
            var name = _settings.AddonName;
            var nameLower = name.ToLowerInvariant();

            var lua = $@"-- {name} Options
-- Quest Voiceover Addon - Einstellungs-UI (Erweitert)
-- Generiert von WowQuestTtsTool
-- ============================================================================

local addonName, addon = ...

-- ============================================================================
-- Lokale Hilfsfunktionen
-- ============================================================================

local checkboxes = {{}}
local dropdowns = {{}}

-- ============================================================================
-- Options Panel Hauptfunktion
-- ============================================================================

function addon:InitializeOptions()
    -- Haupt-Panel mit Scroll-Support
    local panel = CreateFrame(""Frame"")
    panel.name = addonName

    -- ScrollFrame fuer lange Inhalte
    local scrollFrame = CreateFrame(""ScrollFrame"", nil, panel, ""UIPanelScrollFrameTemplate"")
    scrollFrame:SetPoint(""TOPLEFT"", 10, -10)
    scrollFrame:SetPoint(""BOTTOMRIGHT"", -30, 10)

    local scrollChild = CreateFrame(""Frame"")
    scrollFrame:SetScrollChild(scrollChild)
    scrollChild:SetSize(550, 900)

    local yOffset = -10

    -- ==================== Titel ====================

    local title = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontNormalLarge"")
    title:SetPoint(""TOPLEFT"", 6, yOffset)
    title:SetText(""|cFF00FF00{name}|r - Quest Voiceover"")

    local version = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontHighlightSmall"")
    version:SetPoint(""LEFT"", title, ""RIGHT"", 10, 0)
    version:SetText(""v{_settings.AddonVersion}"")

    yOffset = yOffset - 25

    local desc = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontHighlight"")
    desc:SetPoint(""TOPLEFT"", 6, yOffset)
    desc:SetWidth(530)
    desc:SetJustifyH(""LEFT"")
    desc:SetText(""Spricht Quest-Texte mit TTS-Stimmen vor. Verfuegbare Quests: "" .. addon:GetQuestCount())

    yOffset = yOffset - 30

    -- ==================== Hilfsfunktionen ====================

    local function CreateSectionHeader(text)
        yOffset = yOffset - 10
        local header = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontNormal"")
        header:SetPoint(""TOPLEFT"", 6, yOffset)
        header:SetText(""|cFFFFD100"" .. text .. ""|r"")

        local line = scrollChild:CreateTexture(nil, ""ARTWORK"")
        line:SetHeight(1)
        line:SetPoint(""TOPLEFT"", header, ""BOTTOMLEFT"", 0, -2)
        line:SetPoint(""RIGHT"", scrollChild, ""RIGHT"", -10, 0)
        line:SetColorTexture(0.5, 0.5, 0.5, 0.5)

        yOffset = yOffset - 22
    end

    local function CreateCheckbox(label, configKey, tooltip, indent)
        local cb = CreateFrame(""CheckButton"", nil, scrollChild, ""InterfaceOptionsCheckButtonTemplate"")
        cb:SetPoint(""TOPLEFT"", (indent or 0) + 6, yOffset)
        cb.Text:SetText(label)
        cb.Text:SetFontObject(""GameFontHighlight"")

        cb:SetChecked(addon:GetConfig(configKey) or false)
        cb:SetScript(""OnClick"", function(self)
            addon:SetConfig(configKey, self:GetChecked())
        end)

        if tooltip then
            cb:SetScript(""OnEnter"", function(self)
                GameTooltip:SetOwner(self, ""ANCHOR_RIGHT"")
                GameTooltip:SetText(label, 1, 1, 1)
                GameTooltip:AddLine(tooltip, nil, nil, nil, true)
                GameTooltip:Show()
            end)
            cb:SetScript(""OnLeave"", function() GameTooltip:Hide() end)
        end

        checkboxes[configKey] = cb
        yOffset = yOffset - 24
        return cb
    end

    local function CreateDropdown(label, configKey, options, width)
        local lbl = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontHighlight"")
        lbl:SetPoint(""TOPLEFT"", 6, yOffset)
        lbl:SetText(label)

        local dropdown = CreateFrame(""Frame"", ""{name}_"" .. configKey .. ""_Dropdown"", scrollChild, ""UIDropDownMenuTemplate"")
        dropdown:SetPoint(""TOPLEFT"", lbl, ""BOTTOMLEFT"", -16, -2)

        UIDropDownMenu_SetWidth(dropdown, width or 200)
        UIDropDownMenu_Initialize(dropdown, function(self, level)
            for _, opt in ipairs(options) do
                local info = UIDropDownMenu_CreateInfo()
                info.text = opt.text
                info.value = opt.value
                info.func = function()
                    addon:SetConfig(configKey, opt.value)
                    UIDropDownMenu_SetText(dropdown, opt.text)
                end
                info.checked = (addon:GetConfig(configKey) == opt.value)
                UIDropDownMenu_AddButton(info, level)
            end
        end)

        -- Aktuellen Wert setzen
        local currentValue = addon:GetConfig(configKey)
        for _, opt in ipairs(options) do
            if opt.value == currentValue then
                UIDropDownMenu_SetText(dropdown, opt.text)
                break
            end
        end

        dropdowns[configKey] = dropdown
        yOffset = yOffset - 55
        return dropdown
    end

    local function CreateButton(text, onClick, width)
        local btn = CreateFrame(""Button"", nil, scrollChild, ""UIPanelButtonTemplate"")
        btn:SetSize(width or 120, 22)
        btn:SetPoint(""TOPLEFT"", 6, yOffset)
        btn:SetText(text)
        btn:SetScript(""OnClick"", onClick)
        return btn
    end

    -- ==================== Bereich: Allgemein ====================

    CreateSectionHeader(""Allgemein"")
    CreateCheckbox(""TTS aktiviert"", ""enableTts"", ""Schaltet die Quest-Vertonung an oder aus"")

    yOffset = yOffset - 5

    -- ==================== Bereich: Quest-Filter ====================

    CreateSectionHeader(""Quest-Filter"")
    CreateCheckbox(""Nur Hauptquests vertonen"", ""onlyMainQuests"", ""Wenn aktiv, werden nur Story-Quests abgespielt"")
    CreateCheckbox(""Nebenquests vertonen"", ""includeSideQuests"", ""Nebenquests mit abspielen"", 20)
    CreateCheckbox(""Gruppenquests vertonen"", ""includeGroupQuests"", ""Gruppenquests mit abspielen"", 20)
    CreateCheckbox(""Dungeon-Quests vertonen"", ""includeDungeonQuests"", ""Dungeon-Quests mit abspielen"", 20)
    CreateCheckbox(""Raid-Quests vertonen"", ""includeRaidQuests"", ""Raid-Quests mit abspielen"", 20)
    CreateCheckbox(""Tagesquests vertonen"", ""includeDailyQuests"", ""Tagesquests mit abspielen"", 20)
    CreateCheckbox(""Weltquests vertonen"", ""includeWorldQuests"", ""Weltquests mit abspielen"", 20)

    -- ==================== Bereich: Wiedergabe-Verhalten ====================

    CreateSectionHeader(""Wiedergabe-Verhalten"")

    CreateDropdown(""Wiedergabemodus:"", ""playbackMode"", {{
        {{ value = ""AutoOnAccept"", text = ""Automatisch bei Questannahme"" }},
        {{ value = ""AutoOnQuestOpen"", text = ""Automatisch beim Oeffnen des Questfensters"" }},
        {{ value = ""ManualOnly"", text = ""Nur manuell (per Button/Tastenkuerzel)"" }},
    }}, 280)

    -- Modus-Beschreibung
    local modeDesc = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontHighlightSmall"")
    modeDesc:SetPoint(""TOPLEFT"", 6, yOffset + 5)
    modeDesc:SetWidth(500)
    modeDesc:SetJustifyH(""LEFT"")
    modeDesc:SetTextColor(0.7, 0.7, 0.7)
    modeDesc:SetText(""Bestimmt, wann Audio automatisch abgespielt wird."")
    yOffset = yOffset - 20

    CreateCheckbox(""Bei Quest-Fortschritt abspielen"", ""playOnQuestProgress"", ""Spielt Audio ab, wenn du zum Questgeber zurueckkehrst"")
    CreateCheckbox(""Bei Quest-Abschluss abspielen"", ""playOnQuestComplete"", ""Spielt Audio beim Abgeben der Quest ab"")
    CreateCheckbox(""Stoppen beim Schliessen"", ""stopOnQuestClose"", ""Stoppt das Audio, wenn das Questfenster geschlossen wird"")
    CreateCheckbox(""Mehrere Audios gleichzeitig"", ""allowOverlap"", ""Erlaubt das Abspielen mehrerer Audios gleichzeitig"")

    -- ==================== Bereich: Audio / Stimme ====================

    CreateSectionHeader(""Audio / Stimme"")

    CreateDropdown(""Standard-Stimme:"", ""defaultVoice"", {{
        {{ value = ""male"", text = ""Maennlich"" }},
        {{ value = ""female"", text = ""Weiblich"" }},
        {{ value = ""auto"", text = ""Automatisch (Spielergeschlecht)"" }},
    }}, 220)

    CreateDropdown(""Audio-Kanal:"", ""soundChannel"", {{
        {{ value = ""Dialog"", text = ""Dialog (empfohlen)"" }},
        {{ value = ""Master"", text = ""Master"" }},
        {{ value = ""SFX"", text = ""Soundeffekte"" }},
        {{ value = ""Music"", text = ""Musik"" }},
        {{ value = ""Ambience"", text = ""Umgebung"" }},
    }}, 180)

    -- Test-Buttons
    local testLabel = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontHighlight"")
    testLabel:SetPoint(""TOPLEFT"", 6, yOffset)
    testLabel:SetText(""Stimme testen:"")
    yOffset = yOffset - 25

    local testMaleBtn = CreateButton(""Maennliche Stimme"", function()
        if addon:TestVoice(""male"") then
            print(""|cFF00FF00[{name}]|r Teste maennliche Stimme..."")
        else
            print(""|cFFFF0000[{name}]|r Keine maennliche Stimme verfuegbar"")
        end
    end, 140)

    local testFemaleBtn = CreateFrame(""Button"", nil, scrollChild, ""UIPanelButtonTemplate"")
    testFemaleBtn:SetSize(140, 22)
    testFemaleBtn:SetPoint(""LEFT"", testMaleBtn, ""RIGHT"", 10, 0)
    testFemaleBtn:SetText(""Weibliche Stimme"")
    testFemaleBtn:SetScript(""OnClick"", function()
        if addon:TestVoice(""female"") then
            print(""|cFF00FF00[{name}]|r Teste weibliche Stimme..."")
        else
            print(""|cFFFF0000[{name}]|r Keine weibliche Stimme verfuegbar"")
        end
    end)

    local stopTestBtn = CreateFrame(""Button"", nil, scrollChild, ""UIPanelButtonTemplate"")
    stopTestBtn:SetSize(80, 22)
    stopTestBtn:SetPoint(""LEFT"", testFemaleBtn, ""RIGHT"", 10, 0)
    stopTestBtn:SetText(""Stoppen"")
    stopTestBtn:SetScript(""OnClick"", function()
        addon:StopAudio()
    end)

    yOffset = yOffset - 35

    -- ==================== Bereich: UI-Einstellungen ====================

    CreateSectionHeader(""UI-Einstellungen"")
    CreateCheckbox(""Chat-Benachrichtigungen"", ""showNotifications"", ""Zeigt im Chat an, welche Quest abgespielt wird"")
    CreateCheckbox(""Play-Button im Questfenster"", ""showPlayButton"", ""Zeigt einen Play-Button im Questfenster"")
    CreateCheckbox(""Stop-Button im Questfenster"", ""showStopButton"", ""Zeigt einen Stop-Button im Questfenster"")

    -- ==================== Bereich: Zonen-Einstellungen ====================

    CreateSectionHeader(""Zonen-Einstellungen"")

    local zonesDesc = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontHighlightSmall"")
    zonesDesc:SetPoint(""TOPLEFT"", 6, yOffset)
    zonesDesc:SetWidth(500)
    zonesDesc:SetJustifyH(""LEFT"")
    zonesDesc:SetTextColor(0.7, 0.7, 0.7)
    zonesDesc:SetText(""Hier kannst du einzelne Zonen aktivieren oder deaktivieren."")
    yOffset = yOffset - 20

    -- Zonen aus AudioIndex ermitteln
    local zones = addon:GetAvailableZones()
    local zoneList = {{}}
    for zoneName, count in pairs(zones) do
        table.insert(zoneList, {{ name = zoneName, count = count }})
    end
    table.sort(zoneList, function(a, b) return a.name < b.name end)

    -- Zonen-Checkboxen erstellen (max 10 anzeigen)
    local displayedZones = 0
    for _, zone in ipairs(zoneList) do
        if displayedZones < 10 then
            local zonesEnabled = addon:GetConfig(""zonesEnabled"") or {{}}
            local isEnabled = zonesEnabled[zone.name] ~= false

            local cb = CreateFrame(""CheckButton"", nil, scrollChild, ""InterfaceOptionsCheckButtonTemplate"")
            cb:SetPoint(""TOPLEFT"", 26, yOffset)
            cb.Text:SetText(string.format(""%s |cFF888888(%d Quests)|r"", zone.name, zone.count))
            cb.Text:SetFontObject(""GameFontHighlightSmall"")
            cb:SetChecked(isEnabled)

            cb:SetScript(""OnClick"", function(self)
                local currentZones = addon:GetConfig(""zonesEnabled"") or {{}}
                currentZones[zone.name] = self:GetChecked()
                addon:SetConfig(""zonesEnabled"", currentZones)
            end)

            yOffset = yOffset - 22
            displayedZones = displayedZones + 1
        end
    end

    if #zoneList > 10 then
        local moreText = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontHighlightSmall"")
        moreText:SetPoint(""TOPLEFT"", 26, yOffset)
        moreText:SetText(string.format(""|cFF888888... und %d weitere Zonen|r"", #zoneList - 10))
        yOffset = yOffset - 20
    end

    if #zoneList == 0 then
        local noZonesText = scrollChild:CreateFontString(nil, ""ARTWORK"", ""GameFontHighlightSmall"")
        noZonesText:SetPoint(""TOPLEFT"", 26, yOffset)
        noZonesText:SetText(""|cFF888888Keine Zonen im Audio-Index gefunden.|r"")
        yOffset = yOffset - 20
    end

    -- ==================== Standard-Buttons ====================

    yOffset = yOffset - 20
    CreateSectionHeader(""Einstellungen verwalten"")

    local resetBtn = CreateButton(""Auf Standardwerte zuruecksetzen"", function()
        StaticPopup_Show(""{name.ToUpperInvariant()}_RESET_CONFIRM"")
    end, 220)

    -- Reset-Bestaetigung
    StaticPopupDialogs[""{name.ToUpperInvariant()}_RESET_CONFIRM""] = {{
        text = ""Alle Einstellungen auf Standardwerte zuruecksetzen?"",
        button1 = ""Ja"",
        button2 = ""Nein"",
        OnAccept = function()
            addon:ResetToDefaults()
            -- Checkboxen aktualisieren
            for key, cb in pairs(checkboxes) do
                cb:SetChecked(addon:GetConfig(key) or false)
            end
            -- Dropdowns aktualisieren
            for key, dd in pairs(dropdowns) do
                UIDropDownMenu_Initialize(dd, function() end)
            end
            print(""|cFF00FF00[{name}]|r Einstellungen zurueckgesetzt"")
        end,
        timeout = 0,
        whileDead = true,
        hideOnEscape = true,
    }}

    -- ==================== Panel registrieren ====================

    local category = Settings.RegisterCanvasLayoutCategory(panel, addonName)
    Settings.RegisterAddOnCategory(category)
end
";

            var optionsPath = Path.Combine(addonFolder, $"{name}_Options.lua");
            await File.WriteAllTextAsync(optionsPath, lua, Encoding.UTF8, cancellationToken);
        }

        /// <summary>
        /// Generiert die Data.lua mit Audio-Index und Default-Config.
        /// </summary>
        public async Task GenerateDataFileAsync(
            string addonFolder,
            QuestAudioIndex audioIndex,
            string languageCode,
            CancellationToken cancellationToken)
        {
            var name = _settings.AddonName;
            var sb = new StringBuilder();

            sb.AppendLine($"-- {name} Data");
            sb.AppendLine($"-- Quest Voiceover Addon - Daten");
            sb.AppendLine($"-- Generiert: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            sb.AppendLine($"-- Sprache: {languageCode}");
            sb.AppendLine();

            // ==================== Default-Konfiguration ====================
            sb.AppendLine("-- ==================== Default-Konfiguration ====================");
            sb.AppendLine();
            sb.AppendLine($"{name}ConfigDefaults = {{");
            sb.AppendLine($"    enableTts = {BoolToLua(_settings.EnableTts)},");
            sb.AppendLine($"    onlyMainQuests = {BoolToLua(_settings.OnlyMainQuests)},");
            sb.AppendLine($"    includeSideQuests = {BoolToLua(_settings.IncludeSideQuests)},");
            sb.AppendLine($"    includeGroupQuests = {BoolToLua(_settings.IncludeGroupQuests)},");
            sb.AppendLine($"    includeDungeonQuests = {BoolToLua(_settings.IncludeDungeonQuests)},");
            sb.AppendLine($"    includeRaidQuests = {BoolToLua(_settings.IncludeRaidQuests)},");
            sb.AppendLine($"    includeDailyQuests = {BoolToLua(_settings.IncludeDailyQuests)},");
            sb.AppendLine($"    includeWorldQuests = {BoolToLua(_settings.IncludeWorldQuests)},");
            sb.AppendLine($"    playbackMode = \"{PlaybackModeToLua(_settings.PlaybackMode)}\",");
            sb.AppendLine($"    playOnQuestProgress = {BoolToLua(_settings.PlayOnQuestProgress)},");
            sb.AppendLine($"    playOnQuestComplete = {BoolToLua(_settings.PlayOnQuestComplete)},");
            sb.AppendLine($"    stopOnQuestClose = {BoolToLua(_settings.StopOnQuestClose)},");
            sb.AppendLine($"    allowOverlap = {BoolToLua(_settings.AllowOverlap)},");
            sb.AppendLine($"    defaultVoice = \"{VoiceGenderToLua(_settings.DefaultVoice)}\",");
            sb.AppendLine($"    volumeMultiplier = {_settings.VolumeMultiplier:F2},");
            sb.AppendLine($"    soundChannel = \"{EscapeLua(_settings.SoundChannel)}\",");
            sb.AppendLine($"    showNotifications = {BoolToLua(_settings.ShowNotifications)},");
            sb.AppendLine($"    showPlayButton = {BoolToLua(_settings.ShowPlayButton)},");
            sb.AppendLine($"    showStopButton = {BoolToLua(_settings.ShowStopButton)},");

            // Zonen-Einstellungen
            if (_settings.ZonesEnabled.Count > 0)
            {
                sb.AppendLine($"    zonesEnabled = {{");
                foreach (var zone in _settings.ZonesEnabled.OrderBy(z => z.Key))
                {
                    sb.AppendLine($"        [\"{EscapeLua(zone.Key)}\"] = {BoolToLua(zone.Value)},");
                }
                sb.AppendLine($"    }},");
            }
            else
            {
                sb.AppendLine($"    zonesEnabled = {{}},");
            }

            sb.AppendLine("}");
            sb.AppendLine();

            // ==================== Audio-Index ====================
            sb.AppendLine("-- ==================== Audio-Index ====================");
            sb.AppendLine();
            sb.AppendLine($"{name}AudioIndex = {{");

            if (audioIndex?.Entries != null)
            {
                // Gruppiere nach QuestId
                var questGroups = audioIndex.Entries
                    .GroupBy(e => e.QuestId)
                    .OrderBy(g => g.Key);

                foreach (var group in questGroups)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var questId = group.Key;
                    var entries = group.ToList();
                    var first = entries[0];

                    sb.AppendLine($"    [{questId}] = {{");
                    sb.AppendLine($"        title = \"{EscapeLua(first.Title)}\",");
                    sb.AppendLine($"        zone = \"{EscapeLua(first.Zone)}\",");
                    sb.AppendLine($"        category = \"{EscapeLua(first.Category)}\",");
                    sb.AppendLine($"        isMainStory = {BoolToLua(first.IsMainStory)},");
                    sb.AppendLine($"        files = {{");

                    foreach (var entry in entries)
                    {
                        // Pfad fuer WoW anpassen (Interface\\AddOns\\...)
                        var relativePath = entry.RelativePath.Replace("/", "\\\\");
                        var wowPath = $"Interface\\\\AddOns\\\\{name}\\\\Sound\\\\{relativePath}";
                        sb.AppendLine($"            [\"{entry.Gender}\"] = \"{wowPath}\",");
                    }

                    sb.AppendLine($"        }},");
                    sb.AppendLine($"    }},");
                }
            }

            sb.AppendLine("}");

            var dataPath = Path.Combine(addonFolder, $"{name}_Data.lua");
            await File.WriteAllTextAsync(dataPath, sb.ToString(), Encoding.UTF8, cancellationToken);
        }

        // ==================== Hilfsmethoden ====================

        private static string BoolToLua(bool value) => value ? "true" : "false";

        private static string PlaybackModeToLua(AddonPlaybackMode mode) => mode switch
        {
            AddonPlaybackMode.AutoOnAccept => "AutoOnAccept",
            AddonPlaybackMode.AutoOnQuestOpen => "AutoOnQuestOpen",
            AddonPlaybackMode.ManualOnly => "ManualOnly",
            _ => "AutoOnAccept"
        };

        private static string VoiceGenderToLua(AddonVoiceGender gender) => gender switch
        {
            AddonVoiceGender.Male => "male",
            AddonVoiceGender.Female => "female",
            AddonVoiceGender.Auto => "auto",
            _ => "male"
        };

        private static string EscapeLua(string? input)
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
}
