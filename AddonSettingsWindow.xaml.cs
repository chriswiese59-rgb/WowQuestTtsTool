using System.Windows;
using System.Windows.Controls;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    /// <summary>
    /// Interaktionslogik fuer AddonSettingsWindow.xaml
    /// </summary>
    public partial class AddonSettingsWindow : Window
    {
        private readonly AddonSettings _settings;

        public AddonSettingsWindow(AddonSettings settings)
        {
            InitializeComponent();
            _settings = settings;

            // Volume-Slider Event
            VolumeSlider.ValueChanged += (s, e) =>
            {
                VolumeText.Text = $"{VolumeSlider.Value * 100:F0}%";
            };

            LoadSettingsToUI();
        }

        /// <summary>
        /// Laedt die Einstellungen in die UI-Elemente.
        /// </summary>
        private void LoadSettingsToUI()
        {
            // Globale Einstellungen
            EnableTtsCheckBox.IsChecked = _settings.EnableTts;
            OnlyMainQuestsCheckBox.IsChecked = _settings.OnlyMainQuests;

            // Quest-Filter
            IncludeSideQuestsCheckBox.IsChecked = _settings.IncludeSideQuests;
            IncludeGroupQuestsCheckBox.IsChecked = _settings.IncludeGroupQuests;
            IncludeDungeonQuestsCheckBox.IsChecked = _settings.IncludeDungeonQuests;
            IncludeRaidQuestsCheckBox.IsChecked = _settings.IncludeRaidQuests;
            IncludeDailyQuestsCheckBox.IsChecked = _settings.IncludeDailyQuests;
            IncludeWorldQuestsCheckBox.IsChecked = _settings.IncludeWorldQuests;

            // Wiedergabe-Verhalten
            SelectComboByTag(PlaybackModeCombo, _settings.PlaybackMode.ToString());
            PlayOnQuestProgressCheckBox.IsChecked = _settings.PlayOnQuestProgress;
            PlayOnQuestCompleteCheckBox.IsChecked = _settings.PlayOnQuestComplete;
            StopOnQuestCloseCheckBox.IsChecked = _settings.StopOnQuestClose;
            AllowOverlapCheckBox.IsChecked = _settings.AllowOverlap;

            // Audio-Einstellungen
            SelectComboByTag(DefaultVoiceCombo, _settings.DefaultVoice.ToString());
            SelectComboByTag(SoundChannelCombo, _settings.SoundChannel);
            VolumeSlider.Value = _settings.VolumeMultiplier;
            VolumeText.Text = $"{_settings.VolumeMultiplier * 100:F0}%";

            // UI-Einstellungen
            ShowNotificationsCheckBox.IsChecked = _settings.ShowNotifications;
            ShowPlayButtonCheckBox.IsChecked = _settings.ShowPlayButton;
            ShowStopButtonCheckBox.IsChecked = _settings.ShowStopButton;

            // Addon-Metadaten
            AddonAuthorBox.Text = _settings.AddonAuthor;
            InterfaceVersionBox.Text = _settings.InterfaceVersion;
        }

        /// <summary>
        /// Speichert die UI-Werte zurueck in die Einstellungen.
        /// </summary>
        private void SaveUIToSettings()
        {
            // Globale Einstellungen
            _settings.EnableTts = EnableTtsCheckBox.IsChecked == true;
            _settings.OnlyMainQuests = OnlyMainQuestsCheckBox.IsChecked == true;

            // Quest-Filter
            _settings.IncludeSideQuests = IncludeSideQuestsCheckBox.IsChecked == true;
            _settings.IncludeGroupQuests = IncludeGroupQuestsCheckBox.IsChecked == true;
            _settings.IncludeDungeonQuests = IncludeDungeonQuestsCheckBox.IsChecked == true;
            _settings.IncludeRaidQuests = IncludeRaidQuestsCheckBox.IsChecked == true;
            _settings.IncludeDailyQuests = IncludeDailyQuestsCheckBox.IsChecked == true;
            _settings.IncludeWorldQuests = IncludeWorldQuestsCheckBox.IsChecked == true;

            // Wiedergabe-Verhalten
            var playbackModeTag = GetSelectedComboTag(PlaybackModeCombo);
            _settings.PlaybackMode = playbackModeTag switch
            {
                "AutoOnAccept" => AddonPlaybackMode.AutoOnAccept,
                "AutoOnQuestOpen" => AddonPlaybackMode.AutoOnQuestOpen,
                "ManualOnly" => AddonPlaybackMode.ManualOnly,
                _ => AddonPlaybackMode.AutoOnAccept
            };
            _settings.PlayOnQuestProgress = PlayOnQuestProgressCheckBox.IsChecked == true;
            _settings.PlayOnQuestComplete = PlayOnQuestCompleteCheckBox.IsChecked == true;
            _settings.StopOnQuestClose = StopOnQuestCloseCheckBox.IsChecked == true;
            _settings.AllowOverlap = AllowOverlapCheckBox.IsChecked == true;

            // Audio-Einstellungen
            var voiceTag = GetSelectedComboTag(DefaultVoiceCombo);
            _settings.DefaultVoice = voiceTag switch
            {
                "Male" => AddonVoiceGender.Male,
                "Female" => AddonVoiceGender.Female,
                "Auto" => AddonVoiceGender.Auto,
                _ => AddonVoiceGender.Male
            };
            _settings.SoundChannel = GetSelectedComboTag(SoundChannelCombo) ?? "Dialog";
            _settings.VolumeMultiplier = (float)VolumeSlider.Value;

            // UI-Einstellungen
            _settings.ShowNotifications = ShowNotificationsCheckBox.IsChecked == true;
            _settings.ShowPlayButton = ShowPlayButtonCheckBox.IsChecked == true;
            _settings.ShowStopButton = ShowStopButtonCheckBox.IsChecked == true;

            // Addon-Metadaten
            _settings.AddonAuthor = AddonAuthorBox.Text?.Trim() ?? "WowQuestTtsTool";
            _settings.InterfaceVersion = InterfaceVersionBox.Text?.Trim() ?? "110002";
        }

        private void SelectComboByTag(ComboBox combo, string? tag)
        {
            if (string.IsNullOrEmpty(tag))
                return;

            foreach (ComboBoxItem item in combo.Items)
            {
                if (item.Tag?.ToString() == tag)
                {
                    combo.SelectedItem = item;
                    return;
                }
            }
        }

        private string? GetSelectedComboTag(ComboBox combo)
        {
            return (combo.SelectedItem as ComboBoxItem)?.Tag?.ToString();
        }

        private void OnOkClick(object sender, RoutedEventArgs e)
        {
            SaveUIToSettings();
            DialogResult = true;
            Close();
        }

        private void OnCancelClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnResetClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Alle Einstellungen auf Standardwerte zuruecksetzen?",
                "Zuruecksetzen?",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                // Reset zu Defaults
                var defaults = new AddonSettings();

                // Kopiere Default-Werte
                _settings.EnableTts = defaults.EnableTts;
                _settings.OnlyMainQuests = defaults.OnlyMainQuests;
                _settings.IncludeSideQuests = defaults.IncludeSideQuests;
                _settings.IncludeGroupQuests = defaults.IncludeGroupQuests;
                _settings.IncludeDungeonQuests = defaults.IncludeDungeonQuests;
                _settings.IncludeRaidQuests = defaults.IncludeRaidQuests;
                _settings.IncludeDailyQuests = defaults.IncludeDailyQuests;
                _settings.IncludeWorldQuests = defaults.IncludeWorldQuests;
                _settings.PlaybackMode = defaults.PlaybackMode;
                _settings.PlayOnQuestProgress = defaults.PlayOnQuestProgress;
                _settings.PlayOnQuestComplete = defaults.PlayOnQuestComplete;
                _settings.StopOnQuestClose = defaults.StopOnQuestClose;
                _settings.AllowOverlap = defaults.AllowOverlap;
                _settings.DefaultVoice = defaults.DefaultVoice;
                _settings.SoundChannel = defaults.SoundChannel;
                _settings.VolumeMultiplier = defaults.VolumeMultiplier;
                _settings.ShowNotifications = defaults.ShowNotifications;
                _settings.ShowPlayButton = defaults.ShowPlayButton;
                _settings.ShowStopButton = defaults.ShowStopButton;
                _settings.AddonAuthor = defaults.AddonAuthor;
                _settings.InterfaceVersion = defaults.InterfaceVersion;

                LoadSettingsToUI();
            }
        }
    }
}
