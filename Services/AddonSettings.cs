using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Einstellungen fuer das WoW-Addon, die im Studio konfiguriert
    /// und als Lua-Defaults exportiert werden.
    /// </summary>
    public class AddonSettings : INotifyPropertyChanged
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        // ==================== Globale Einstellungen ====================

        private bool _enableTts = true;
        private bool _onlyMainQuests = false;
        private bool _includeSideQuests = true;
        private bool _includeGroupQuests = true;
        private bool _includeDungeonQuests = true;
        private bool _includeRaidQuests = false;
        private bool _includeDailyQuests = false;
        private bool _includeWorldQuests = false;

        /// <summary>
        /// TTS-System an/aus.
        /// </summary>
        [JsonPropertyName("enable_tts")]
        public bool EnableTts
        {
            get => _enableTts;
            set { _enableTts = value; OnPropertyChanged(nameof(EnableTts)); }
        }

        /// <summary>
        /// Nur Hauptquests vertonen.
        /// </summary>
        [JsonPropertyName("only_main_quests")]
        public bool OnlyMainQuests
        {
            get => _onlyMainQuests;
            set { _onlyMainQuests = value; OnPropertyChanged(nameof(OnlyMainQuests)); }
        }

        /// <summary>
        /// Nebenquests mit vertonen.
        /// </summary>
        [JsonPropertyName("include_side_quests")]
        public bool IncludeSideQuests
        {
            get => _includeSideQuests;
            set { _includeSideQuests = value; OnPropertyChanged(nameof(IncludeSideQuests)); }
        }

        /// <summary>
        /// Gruppenquests mit vertonen.
        /// </summary>
        [JsonPropertyName("include_group_quests")]
        public bool IncludeGroupQuests
        {
            get => _includeGroupQuests;
            set { _includeGroupQuests = value; OnPropertyChanged(nameof(IncludeGroupQuests)); }
        }

        /// <summary>
        /// Dungeon-Quests mit vertonen.
        /// </summary>
        [JsonPropertyName("include_dungeon_quests")]
        public bool IncludeDungeonQuests
        {
            get => _includeDungeonQuests;
            set { _includeDungeonQuests = value; OnPropertyChanged(nameof(IncludeDungeonQuests)); }
        }

        /// <summary>
        /// Raid-Quests mit vertonen.
        /// </summary>
        [JsonPropertyName("include_raid_quests")]
        public bool IncludeRaidQuests
        {
            get => _includeRaidQuests;
            set { _includeRaidQuests = value; OnPropertyChanged(nameof(IncludeRaidQuests)); }
        }

        /// <summary>
        /// Tagesquests mit vertonen.
        /// </summary>
        [JsonPropertyName("include_daily_quests")]
        public bool IncludeDailyQuests
        {
            get => _includeDailyQuests;
            set { _includeDailyQuests = value; OnPropertyChanged(nameof(IncludeDailyQuests)); }
        }

        /// <summary>
        /// Weltquests mit vertonen.
        /// </summary>
        [JsonPropertyName("include_world_quests")]
        public bool IncludeWorldQuests
        {
            get => _includeWorldQuests;
            set { _includeWorldQuests = value; OnPropertyChanged(nameof(IncludeWorldQuests)); }
        }

        // ==================== Wiedergabe-Verhalten ====================

        private AddonPlaybackMode _playbackMode = AddonPlaybackMode.AutoOnAccept;
        private bool _playOnQuestProgress = true;
        private bool _playOnQuestComplete = true;
        private bool _stopOnQuestClose = false;
        private bool _allowOverlap = false;

        /// <summary>
        /// Wiedergabemodus.
        /// </summary>
        [JsonPropertyName("playback_mode")]
        public AddonPlaybackMode PlaybackMode
        {
            get => _playbackMode;
            set { _playbackMode = value; OnPropertyChanged(nameof(PlaybackMode)); }
        }

        /// <summary>
        /// Bei Quest-Fortschritt abspielen (QUEST_PROGRESS Event).
        /// </summary>
        [JsonPropertyName("play_on_quest_progress")]
        public bool PlayOnQuestProgress
        {
            get => _playOnQuestProgress;
            set { _playOnQuestProgress = value; OnPropertyChanged(nameof(PlayOnQuestProgress)); }
        }

        /// <summary>
        /// Bei Quest-Abschluss abspielen (QUEST_COMPLETE Event).
        /// </summary>
        [JsonPropertyName("play_on_quest_complete")]
        public bool PlayOnQuestComplete
        {
            get => _playOnQuestComplete;
            set { _playOnQuestComplete = value; OnPropertyChanged(nameof(PlayOnQuestComplete)); }
        }

        /// <summary>
        /// Audio stoppen wenn Quest-Fenster geschlossen wird.
        /// </summary>
        [JsonPropertyName("stop_on_quest_close")]
        public bool StopOnQuestClose
        {
            get => _stopOnQuestClose;
            set { _stopOnQuestClose = value; OnPropertyChanged(nameof(StopOnQuestClose)); }
        }

        /// <summary>
        /// Mehrere Audios gleichzeitig erlauben.
        /// </summary>
        [JsonPropertyName("allow_overlap")]
        public bool AllowOverlap
        {
            get => _allowOverlap;
            set { _allowOverlap = value; OnPropertyChanged(nameof(AllowOverlap)); }
        }

        // ==================== Audio-Einstellungen ====================

        private AddonVoiceGender _defaultVoice = AddonVoiceGender.Male;
        private float _volumeMultiplier = 1.0f;
        private string _soundChannel = "Dialog";

        /// <summary>
        /// Standard-Stimme (maennlich/weiblich/automatisch).
        /// </summary>
        [JsonPropertyName("default_voice")]
        public AddonVoiceGender DefaultVoice
        {
            get => _defaultVoice;
            set { _defaultVoice = value; OnPropertyChanged(nameof(DefaultVoice)); }
        }

        /// <summary>
        /// Lautstaerke-Multiplikator (0.0 - 1.0).
        /// </summary>
        [JsonPropertyName("volume_multiplier")]
        public float VolumeMultiplier
        {
            get => _volumeMultiplier;
            set { _volumeMultiplier = Math.Clamp(value, 0f, 1f); OnPropertyChanged(nameof(VolumeMultiplier)); }
        }

        /// <summary>
        /// WoW Sound-Kanal (Dialog, Master, SFX, Music, Ambience).
        /// </summary>
        [JsonPropertyName("sound_channel")]
        public string SoundChannel
        {
            get => _soundChannel;
            set { _soundChannel = value; OnPropertyChanged(nameof(SoundChannel)); }
        }

        // ==================== UI-Einstellungen ====================

        private bool _showNotifications = true;
        private bool _showPlayButton = true;
        private bool _showStopButton = true;
        private string _keybindPlay = "";
        private string _keybindStop = "";

        /// <summary>
        /// Benachrichtigungen im Chat anzeigen.
        /// </summary>
        [JsonPropertyName("show_notifications")]
        public bool ShowNotifications
        {
            get => _showNotifications;
            set { _showNotifications = value; OnPropertyChanged(nameof(ShowNotifications)); }
        }

        /// <summary>
        /// Play-Button im Quest-Fenster anzeigen.
        /// </summary>
        [JsonPropertyName("show_play_button")]
        public bool ShowPlayButton
        {
            get => _showPlayButton;
            set { _showPlayButton = value; OnPropertyChanged(nameof(ShowPlayButton)); }
        }

        /// <summary>
        /// Stop-Button im Quest-Fenster anzeigen.
        /// </summary>
        [JsonPropertyName("show_stop_button")]
        public bool ShowStopButton
        {
            get => _showStopButton;
            set { _showStopButton = value; OnPropertyChanged(nameof(ShowStopButton)); }
        }

        /// <summary>
        /// Tastenkuerzel fuer Abspielen.
        /// </summary>
        [JsonPropertyName("keybind_play")]
        public string KeybindPlay
        {
            get => _keybindPlay;
            set { _keybindPlay = value ?? ""; OnPropertyChanged(nameof(KeybindPlay)); }
        }

        /// <summary>
        /// Tastenkuerzel fuer Stoppen.
        /// </summary>
        [JsonPropertyName("keybind_stop")]
        public string KeybindStop
        {
            get => _keybindStop;
            set { _keybindStop = value ?? ""; OnPropertyChanged(nameof(KeybindStop)); }
        }

        // ==================== Addon-Metadaten ====================

        private string _addonName = "WowTts";
        private string _addonVersion = "1.0.0";
        private string _addonAuthor = "WowQuestTtsTool";
        private string _interfaceVersion = "110002"; // The War Within

        /// <summary>
        /// Addon-Name (ohne Leerzeichen/Sonderzeichen).
        /// </summary>
        [JsonPropertyName("addon_name")]
        public string AddonName
        {
            get => _addonName;
            set { _addonName = SanitizeAddonName(value); OnPropertyChanged(nameof(AddonName)); }
        }

        /// <summary>
        /// Addon-Version.
        /// </summary>
        [JsonPropertyName("addon_version")]
        public string AddonVersion
        {
            get => _addonVersion;
            set { _addonVersion = value ?? "1.0.0"; OnPropertyChanged(nameof(AddonVersion)); }
        }

        /// <summary>
        /// Addon-Autor.
        /// </summary>
        [JsonPropertyName("addon_author")]
        public string AddonAuthor
        {
            get => _addonAuthor;
            set { _addonAuthor = value ?? ""; OnPropertyChanged(nameof(AddonAuthor)); }
        }

        /// <summary>
        /// WoW Interface-Version.
        /// </summary>
        [JsonPropertyName("interface_version")]
        public string InterfaceVersion
        {
            get => _interfaceVersion;
            set { _interfaceVersion = value ?? "110002"; OnPropertyChanged(nameof(InterfaceVersion)); }
        }

        // ==================== Zonen-Einstellungen ====================

        /// <summary>
        /// Aktivierte/deaktivierte Zonen (Zone-Name -> aktiv).
        /// </summary>
        [JsonPropertyName("zones_enabled")]
        public Dictionary<string, bool> ZonesEnabled { get; set; } = [];

        // ==================== Persistenz ====================

        /// <summary>
        /// Laedt die Einstellungen aus einer JSON-Datei.
        /// </summary>
        public static AddonSettings Load(string filePath)
        {
            if (!File.Exists(filePath))
                return new AddonSettings();

            try
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AddonSettings>(json) ?? new AddonSettings();
            }
            catch
            {
                return new AddonSettings();
            }
        }

        /// <summary>
        /// Speichert die Einstellungen in eine JSON-Datei.
        /// </summary>
        public void Save(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(this, s_jsonOptions);
            File.WriteAllText(filePath, json);
        }

        // ==================== Hilfsmethoden ====================

        private static string SanitizeAddonName(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "WowTts";

            // Nur Buchstaben, Zahlen und Unterstriche erlaubt
            var result = new System.Text.StringBuilder();
            foreach (var c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                    result.Append(c);
            }

            return result.Length > 0 ? result.ToString() : "WowTts";
        }

        // ==================== INotifyPropertyChanged ====================

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Wiedergabemodus fuer das Addon.
    /// </summary>
    public enum AddonPlaybackMode
    {
        /// <summary>
        /// Automatisch beim Annehmen einer Quest.
        /// </summary>
        AutoOnAccept,

        /// <summary>
        /// Automatisch beim Oeffnen des Quest-Fensters.
        /// </summary>
        AutoOnQuestOpen,

        /// <summary>
        /// Nur manuell (Button/Tastendruck).
        /// </summary>
        ManualOnly
    }

    /// <summary>
    /// Standard-Stimme fuer das Addon.
    /// </summary>
    public enum AddonVoiceGender
    {
        /// <summary>
        /// Maennliche Stimme.
        /// </summary>
        Male,

        /// <summary>
        /// Weibliche Stimme.
        /// </summary>
        Female,

        /// <summary>
        /// Automatisch basierend auf Spielercharakter.
        /// </summary>
        Auto
    }
}
