using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    public partial class SettingsWindow : Window
    {
        private readonly TtsConfigService _configService;
        private bool _apiKeyVisible = false;
        private bool _blizzardSecretVisible = false;
        private string _apiKey = "";
        private string _blizzardSecret = "";

        public bool SettingsSaved { get; private set; } = false;

        public SettingsWindow(TtsConfigService configService)
        {
            InitializeComponent();
            _configService = configService;
            LoadSettings();
        }

        private void LoadSettings()
        {
            var config = _configService.Config;

            // ElevenLabs
            _apiKey = config.ElevenLabs.ApiKey;
            if (!string.IsNullOrEmpty(_apiKey) && _apiKey != "YOUR_ELEVENLABS_API_KEY_HERE")
            {
                ApiKeyBox.Password = _apiKey;
            }

            VoiceIdBox.Text = config.ElevenLabs.VoiceId;

            foreach (ComboBoxItem item in ModelIdCombo.Items)
            {
                if (item.Tag?.ToString() == config.ElevenLabs.ModelId)
                {
                    ModelIdCombo.SelectedItem = item;
                    break;
                }
            }

            StabilitySlider.Value = config.ElevenLabs.VoiceSettings.Stability;
            SimilaritySlider.Value = config.ElevenLabs.VoiceSettings.SimilarityBoost;

            // Blizzard
            if (!string.IsNullOrEmpty(config.Blizzard.ClientId) && config.Blizzard.ClientId != "YOUR_BLIZZARD_CLIENT_ID")
            {
                BlizzardClientIdBox.Text = config.Blizzard.ClientId;
            }

            _blizzardSecret = config.Blizzard.ClientSecret;
            if (!string.IsNullOrEmpty(_blizzardSecret) && _blizzardSecret != "YOUR_BLIZZARD_CLIENT_SECRET")
            {
                BlizzardSecretBox.Password = _blizzardSecret;
            }

            foreach (ComboBoxItem item in BlizzardRegionCombo.Items)
            {
                if (item.Tag?.ToString() == config.Blizzard.Region)
                {
                    BlizzardRegionCombo.SelectedItem = item;
                    break;
                }
            }

            MaxQuestsBox.Text = config.Blizzard.MaxQuests.ToString();

            UpdateStatus();
        }

        private void UpdateStatus()
        {
            var hasElevenLabsKey = !string.IsNullOrWhiteSpace(_apiKey) && _apiKey != "YOUR_ELEVENLABS_API_KEY_HERE";
            var hasBlizzardConfig = !string.IsNullOrWhiteSpace(BlizzardClientIdBox.Text) &&
                                    !string.IsNullOrWhiteSpace(_blizzardSecret) &&
                                    _blizzardSecret != "YOUR_BLIZZARD_CLIENT_SECRET";

            if (hasElevenLabsKey && hasBlizzardConfig)
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));
                StatusText.Text = "Beide APIs konfiguriert. Bereit!";
            }
            else if (hasElevenLabsKey)
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 205));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 238, 186));
                StatusText.Text = "ElevenLabs konfiguriert. Blizzard API fehlt noch.";
            }
            else if (hasBlizzardConfig)
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 205));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 238, 186));
                StatusText.Text = "Blizzard API konfiguriert. ElevenLabs fehlt noch.";
            }
            else
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 243, 205));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 238, 186));
                StatusText.Text = "Konfiguriere deine API-Zugangsdaten.";
            }
        }

        #region ElevenLabs

        private void ApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _apiKey = ApiKeyBox.Password;
            UpdateStatus();
        }

        private void ApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _apiKey = ApiKeyTextBox.Text;
            UpdateStatus();
        }

        private void ToggleApiKeyVisibility(object sender, RoutedEventArgs e)
        {
            _apiKeyVisible = !_apiKeyVisible;

            if (_apiKeyVisible)
            {
                ApiKeyTextBox.Text = _apiKey;
                ApiKeyTextBox.Visibility = Visibility.Visible;
                ApiKeyBox.Visibility = Visibility.Collapsed;
                ((Button)sender).Content = "Verbergen";
            }
            else
            {
                ApiKeyBox.Password = _apiKey;
                ApiKeyBox.Visibility = Visibility.Visible;
                ApiKeyTextBox.Visibility = Visibility.Collapsed;
                ((Button)sender).Content = "Zeigen";
            }
        }

        private async void TestElevenLabsConnection_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_ELEVENLABS_API_KEY_HERE")
            {
                MessageBox.Show("Bitte zuerst einen API-Key eintragen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "Teste ElevenLabs Verbindung...";
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(204, 229, 255));
            StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(184, 218, 255));

            try
            {
                using var service = new ElevenLabsService(_apiKey);
                var result = await Task.Run(() => service.GetVoicesAsync());

                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));
                StatusText.Text = "ElevenLabs Verbindung erfolgreich!";
            }
            catch (Exception ex)
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));
                StatusText.Text = $"ElevenLabs Fehler: {ex.Message}";
            }
        }

        #endregion

        #region Blizzard

        private void BlizzardSecretBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _blizzardSecret = BlizzardSecretBox.Password;
            UpdateStatus();
        }

        private void BlizzardSecretTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _blizzardSecret = BlizzardSecretTextBox.Text;
            UpdateStatus();
        }

        private void ToggleBlizzardSecretVisibility(object sender, RoutedEventArgs e)
        {
            _blizzardSecretVisible = !_blizzardSecretVisible;

            if (_blizzardSecretVisible)
            {
                BlizzardSecretTextBox.Text = _blizzardSecret;
                BlizzardSecretTextBox.Visibility = Visibility.Visible;
                BlizzardSecretBox.Visibility = Visibility.Collapsed;
                ((Button)sender).Content = "Verbergen";
            }
            else
            {
                BlizzardSecretBox.Password = _blizzardSecret;
                BlizzardSecretBox.Visibility = Visibility.Visible;
                BlizzardSecretTextBox.Visibility = Visibility.Collapsed;
                ((Button)sender).Content = "Zeigen";
            }
        }

        private async void TestBlizzardConnection_Click(object sender, RoutedEventArgs e)
        {
            var clientId = BlizzardClientIdBox.Text?.Trim();
            var clientSecret = _blizzardSecret;
            var region = (BlizzardRegionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "eu";

            if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) ||
                clientSecret == "YOUR_BLIZZARD_CLIENT_SECRET")
            {
                MessageBox.Show("Bitte Client-ID und Client-Secret eintragen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "Teste Blizzard Verbindung...";
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(204, 229, 255));
            StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(184, 218, 255));

            try
            {
                using var httpClient = new HttpClient();
                var service = new BlizzardQuestService(httpClient, clientId, clientSecret, region);

                // Versuche Token zu holen (Authentifizierung)
                var progress = new Progress<string>(msg => StatusText.Text = msg);

                // Lade nur 1 Quest zum Testen
                await Task.Run(async () =>
                {
                    await service.FetchQuestsAsync(maxQuests: 1, progress: progress);
                });

                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));
                StatusText.Text = "Blizzard Verbindung erfolgreich!";
            }
            catch (Exception ex)
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));
                StatusText.Text = $"Blizzard Fehler: {ex.Message}";
            }
        }

        #endregion

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = _configService.Config;

            // ElevenLabs
            config.ElevenLabs.ApiKey = _apiKey;
            config.ElevenLabs.VoiceId = VoiceIdBox.Text.Trim();
            config.ElevenLabs.ModelId = (ModelIdCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                                        ?? "eleven_multilingual_v2";
            config.ElevenLabs.VoiceSettings.Stability = StabilitySlider.Value;
            config.ElevenLabs.VoiceSettings.SimilarityBoost = SimilaritySlider.Value;

            // Blizzard
            config.Blizzard.ClientId = BlizzardClientIdBox.Text?.Trim() ?? "";
            config.Blizzard.ClientSecret = _blizzardSecret;
            config.Blizzard.Region = (BlizzardRegionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "eu";

            if (int.TryParse(MaxQuestsBox.Text, out int maxQuests) && maxQuests > 0)
            {
                config.Blizzard.MaxQuests = maxQuests;
            }

            try
            {
                _configService.SaveConfig(config);
                SettingsSaved = true;
                MessageBox.Show("Einstellungen gespeichert!", "Erfolg",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Fehler beim Speichern:\n{ex.Message}", "Fehler",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
