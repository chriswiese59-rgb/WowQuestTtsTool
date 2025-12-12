using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using WowQuestTtsTool.Services;

namespace WowQuestTtsTool
{
    public partial class SettingsWindow : Window
    {
        private readonly TtsConfigService _configService;
        private bool _apiKeyVisible = false;
        private bool _blizzardSecretVisible = false;
        private bool _llmApiKeyVisible = false;
        private string _apiKey = "";
        private string _blizzardSecret = "";
        private string _llmApiKey = "";

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
            var exportSettings = TtsExportSettings.Instance;

            // Kosten-Tracking
            AvgCharsPerTokenBox.Text = exportSettings.AvgCharsPerToken.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
            CostPer1kTokensBox.Text = exportSettings.CostPer1kTokens.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
            CurrencySymbolBox.Text = exportSettings.CurrencySymbol;
            UpdateCostExample();

            // Stimmen-Profile laden
            LoadVoiceProfiles(exportSettings);

            // ElevenLabs
            _apiKey = config.ElevenLabs.ApiKey;
            if (!string.IsNullOrEmpty(_apiKey) && _apiKey != "YOUR_ELEVENLABS_API_KEY_HERE")
            {
                ApiKeyBox.Password = _apiKey;
            }

            VoiceIdBox.Text = config.ElevenLabs.VoiceId;

            // Male/Female Voice-IDs im ElevenLabs Tab
            ElevenLabsMaleVoiceIdBox.Text = exportSettings.MaleVoiceId;
            ElevenLabsFemaleVoiceIdBox.Text = exportSettings.FemaleVoiceId;

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

            // LLM
            LoadLlmSettings(config);

            // SQLite
            LoadSqliteSettings(exportSettings);

            UpdateStatus();
        }

        private void LoadLlmSettings(TtsConfig config)
        {
            var llm = config.Llm;

            // Merke den Provider für später
            var provider = llm.Provider ?? "OpenAI";

            // Provider auswählen (ohne SelectionChanged auszulösen um Model-Liste nicht zu überschreiben)
            foreach (ComboBoxItem item in LlmProviderCombo.Items)
            {
                if (item.Tag?.ToString()?.Equals(provider, StringComparison.OrdinalIgnoreCase) == true)
                {
                    LlmProviderCombo.SelectedItem = item;
                    break;
                }
            }

            // API Key
            _llmApiKey = llm.ApiKey;
            if (!string.IsNullOrEmpty(_llmApiKey))
            {
                LlmApiKeyBox.Password = _llmApiKey;
            }

            // Model-Liste initialisieren basierend auf Provider
            UpdateLlmModelList(provider);
            UpdateLlmApiKeyHint(provider);

            // Setze das gespeicherte Model (oder Default)
            if (!string.IsNullOrEmpty(llm.ModelId))
            {
                LlmModelCombo.Text = llm.ModelId;
            }
            else if (LlmModelCombo.Items.Count > 0)
            {
                LlmModelCombo.SelectedIndex = 0;
            }

            // Temperature
            LlmTemperatureSlider.Value = llm.Temperature;

            // Max Tokens
            LlmMaxTokensBox.Text = llm.MaxTokens.ToString();

            // Auto-Enhance
            LlmAutoEnhanceCheckBox.IsChecked = llm.AutoEnhance;

            // System Prompt
            LlmSystemPromptBox.Text = llm.SystemPrompt ?? "";
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

        #region Voice-Profile

        private void LoadVoiceProfiles(TtsExportSettings exportSettings)
        {
            // Male Voice
            MaleVoiceIdBox.Text = exportSettings.MaleVoiceId;
            var maleProfile = _configService.GetVoiceProfile("male_narrator");
            if (maleProfile != null)
            {
                MaleVoiceNameBox.Text = maleProfile.Name;
                MaleVoiceDescBox.Text = maleProfile.Description;
            }
            else
            {
                MaleVoiceNameBox.Text = "Male Narrator";
                MaleVoiceDescBox.Text = "";
            }

            // Female Voice
            FemaleVoiceIdBox.Text = exportSettings.FemaleVoiceId;
            var femaleProfile = _configService.GetVoiceProfile("female_narrator");
            if (femaleProfile != null)
            {
                FemaleVoiceNameBox.Text = femaleProfile.Name;
                FemaleVoiceDescBox.Text = femaleProfile.Description;
            }
            else
            {
                FemaleVoiceNameBox.Text = "Female Narrator";
                FemaleVoiceDescBox.Text = "";
            }
        }

        private void SaveVoiceProfiles(TtsExportSettings exportSettings)
        {
            // Male Voice speichern
            var maleVoiceId = MaleVoiceIdBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(maleVoiceId))
            {
                exportSettings.MaleVoiceId = maleVoiceId;
                var maleProfile = new VoiceProfile
                {
                    Name = MaleVoiceNameBox.Text?.Trim() ?? "Male Narrator",
                    VoiceId = maleVoiceId,
                    Provider = "ElevenLabs",
                    Language = exportSettings.LanguageCode,
                    Description = MaleVoiceDescBox.Text?.Trim() ?? "",
                    Gender = "male",
                    Style = "narrator"
                };
                _configService.SaveVoiceProfile("male_narrator", maleProfile);
            }

            // Female Voice speichern
            var femaleVoiceId = FemaleVoiceIdBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(femaleVoiceId))
            {
                exportSettings.FemaleVoiceId = femaleVoiceId;
                var femaleProfile = new VoiceProfile
                {
                    Name = FemaleVoiceNameBox.Text?.Trim() ?? "Female Narrator",
                    VoiceId = femaleVoiceId,
                    Provider = "ElevenLabs",
                    Language = exportSettings.LanguageCode,
                    Description = FemaleVoiceDescBox.Text?.Trim() ?? "",
                    Gender = "female",
                    Style = "narrator"
                };
                _configService.SaveVoiceProfile("female_narrator", femaleProfile);
            }
        }

        private async void TestMaleVoice_Click(object sender, RoutedEventArgs e)
        {
            await TestVoice(MaleVoiceIdBox.Text?.Trim(), "männliche");
        }

        private async void TestFemaleVoice_Click(object sender, RoutedEventArgs e)
        {
            await TestVoice(FemaleVoiceIdBox.Text?.Trim(), "weibliche");
        }

        private async Task TestVoice(string? voiceId, string voiceName)
        {
            if (string.IsNullOrWhiteSpace(voiceId))
            {
                MessageBox.Show($"Bitte zuerst eine Voice-ID für die {voiceName} Stimme eintragen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(_apiKey) || _apiKey == "YOUR_ELEVENLABS_API_KEY_HERE")
            {
                MessageBox.Show("Bitte zuerst einen ElevenLabs API-Key eintragen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = $"Teste {voiceName} Stimme...";
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(204, 229, 255));
            StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(184, 218, 255));

            try
            {
                using var service = new ElevenLabsService(_apiKey);
                var testText = "Dies ist ein Test der Sprachausgabe.";
                var audio = await service.GenerateAudioAsync(testText, voiceId);

                if (audio != null && audio.Length > 0)
                {
                    StatusBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));
                    StatusText.Text = $"{voiceName.Substring(0, 1).ToUpper()}{voiceName.Substring(1)} Stimme erfolgreich getestet! ({audio.Length} Bytes)";
                }
            }
            catch (Exception ex)
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));
                StatusText.Text = $"Fehler bei {voiceName} Stimme: {ex.Message}";
            }
        }

        #endregion

        #region Kosten-Tracking

        private void CostParameter_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateCostExample();
        }

        private void UpdateCostExample()
        {
            if (CostExampleText == null) return;

            if (!double.TryParse(AvgCharsPerTokenBox.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var avgChars) || avgChars <= 0)
                avgChars = 4.0;

            if (!decimal.TryParse(CostPer1kTokensBox.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var costPer1k) || costPer1k < 0)
                costPer1k = 0.30m;

            var currency = string.IsNullOrWhiteSpace(CurrencySymbolBox.Text) ? "€" : CurrencySymbolBox.Text;

            // Beispielrechnung mit 10.000 Zeichen
            var chars = 10000;
            var effectiveChars = chars * 2;
            var tokens = (int)Math.Ceiling(effectiveChars / avgChars);
            var cost = (tokens / 1000m) * costPer1k;

            CostExampleText.Text = $"{chars:N0} Zeichen × 2 Stimmen = {effectiveChars:N0} effektive Zeichen\n" +
                                   $"÷ {avgChars:F1} Zeichen/Token = {tokens:N0} Tokens\n" +
                                   $"× {costPer1k:F2} {currency} / 1.000 Tokens = {cost:F2} {currency}";
        }

        #endregion

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

        private async void TestElevenLabsMaleVoice_Click(object sender, RoutedEventArgs e)
        {
            await TestVoice(ElevenLabsMaleVoiceIdBox.Text?.Trim(), "maennliche");
        }

        private async void TestElevenLabsFemaleVoice_Click(object sender, RoutedEventArgs e)
        {
            await TestVoice(ElevenLabsFemaleVoiceIdBox.Text?.Trim(), "weibliche");
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

        #region LLM

        private void LlmApiKeyBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            _llmApiKey = LlmApiKeyBox.Password;
            UpdateStatus();
        }

        private void LlmApiKeyTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _llmApiKey = LlmApiKeyTextBox.Text;
            UpdateStatus();
        }

        private void ToggleLlmApiKeyVisibility(object sender, RoutedEventArgs e)
        {
            _llmApiKeyVisible = !_llmApiKeyVisible;

            if (_llmApiKeyVisible)
            {
                LlmApiKeyTextBox.Text = _llmApiKey;
                LlmApiKeyTextBox.Visibility = Visibility.Visible;
                LlmApiKeyBox.Visibility = Visibility.Collapsed;
                ((Button)sender).Content = "Verbergen";
            }
            else
            {
                LlmApiKeyBox.Password = _llmApiKey;
                LlmApiKeyBox.Visibility = Visibility.Visible;
                LlmApiKeyTextBox.Visibility = Visibility.Collapsed;
                ((Button)sender).Content = "Zeigen";
            }
        }

        private void LlmProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LlmProviderCombo.SelectedItem is ComboBoxItem selected)
            {
                var provider = selected.Tag?.ToString() ?? "OpenAI";
                UpdateLlmModelList(provider);
                UpdateLlmApiKeyHint(provider);
            }
        }

        private void UpdateLlmModelList(string provider)
        {
            if (LlmModelCombo == null) return;

            LlmModelCombo.Items.Clear();

            switch (provider.ToLowerInvariant())
            {
                case "openai":
                case "chatgpt":
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "gpt-4o-mini", Tag = "gpt-4o-mini" });
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "gpt-4o", Tag = "gpt-4o" });
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "gpt-4-turbo", Tag = "gpt-4-turbo" });
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "gpt-3.5-turbo", Tag = "gpt-3.5-turbo" });
                    LlmModelCombo.SelectedIndex = 0;
                    break;

                case "anthropic":
                case "claude":
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "claude-3-haiku-20240307", Tag = "claude-3-haiku-20240307" });
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "claude-3-5-sonnet-20241022", Tag = "claude-3-5-sonnet-20241022" });
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "claude-3-opus-20240229", Tag = "claude-3-opus-20240229" });
                    LlmModelCombo.SelectedIndex = 0;
                    break;

                case "google":
                case "gemini":
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "gemini-1.5-flash", Tag = "gemini-1.5-flash" });
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "gemini-1.5-pro", Tag = "gemini-1.5-pro" });
                    LlmModelCombo.Items.Add(new ComboBoxItem { Content = "gemini-2.0-flash-exp", Tag = "gemini-2.0-flash-exp" });
                    LlmModelCombo.SelectedIndex = 0;
                    break;
            }
        }

        private void UpdateLlmApiKeyHint(string provider)
        {
            if (LlmApiKeyHint == null) return;

            LlmApiKeyHint.Text = provider.ToLowerInvariant() switch
            {
                "openai" or "chatgpt" => "platform.openai.com → API Keys",
                "anthropic" or "claude" => "console.anthropic.com → API Keys",
                "google" or "gemini" => "aistudio.google.com → API Key",
                _ => "API-Key beim jeweiligen Anbieter erstellen"
            };
        }

        private async void TestLlmConnection_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_llmApiKey))
            {
                MessageBox.Show("Bitte zuerst einen LLM API-Key eintragen.", "Hinweis",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var provider = (LlmProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "OpenAI";
            var modelId = LlmModelCombo.Text;

            StatusText.Text = $"Teste {provider} Verbindung...";
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(204, 229, 255));
            StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(184, 218, 255));

            try
            {
                var testConfig = new LlmConfig
                {
                    Provider = provider,
                    ApiKey = _llmApiKey,
                    ModelId = modelId,
                    Temperature = 0.7,
                    MaxTokens = 50
                };

                var service = new LlmTextEnhancerService(testConfig);

                // Kurzer Test-Prompt
                var result = await service.EnhanceTextAsync(
                    "Test",
                    "Toete 5 Woelfe.",
                    null);

                if (result.IsSuccess)
                {
                    StatusBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));
                    StatusText.Text = $"{provider} Verbindung erfolgreich! Antwort: {result.EnhancedText.Substring(0, Math.Min(50, result.EnhancedText.Length))}...";
                }
                else
                {
                    StatusBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));
                    StatusText.Text = $"{provider} Fehler: {result.ErrorMessage}";
                }
            }
            catch (Exception ex)
            {
                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));
                StatusText.Text = $"{provider} Fehler: {ex.Message}";
            }
        }

        #endregion

        #region SQLite

        private void LoadSqliteSettings(TtsExportSettings exportSettings)
        {
            UseSqliteCheckBox.IsChecked = exportSettings.UseSqliteDatabase;
            SqlitePathBox.Text = exportSettings.SqliteDatabasePath;
            UpdateSqlitePanelState();
        }

        private void UseSqliteCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSqlitePanelState();
        }

        private void UpdateSqlitePanelState()
        {
            var isEnabled = UseSqliteCheckBox.IsChecked ?? false;
            SqlitePathPanel.IsEnabled = isEnabled;
            SqliteStatsGroup.IsEnabled = isEnabled;
            SqlitePathPanel.Opacity = isEnabled ? 1.0 : 0.5;
            SqliteStatsGroup.Opacity = isEnabled ? 1.0 : 0.5;
        }

        private void BrowseSqlitePath_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "SQLite Quest-Datenbank auswählen",
                Filter = "SQLite Datenbanken (*.db)|*.db|Alle Dateien (*.*)|*.*",
                CheckFileExists = true
            };

            if (!string.IsNullOrEmpty(SqlitePathBox.Text))
            {
                var dir = Path.GetDirectoryName(SqlitePathBox.Text);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
                {
                    dialog.InitialDirectory = dir;
                }
            }

            if (dialog.ShowDialog() == true)
            {
                SqlitePathBox.Text = dialog.FileName;
                // Automatisch testen nach Auswahl
                TestSqliteConnection_Click(sender, e);
            }
        }

        private async void TestSqliteConnection_Click(object sender, RoutedEventArgs e)
        {
            var dbPath = SqlitePathBox.Text?.Trim();

            if (string.IsNullOrWhiteSpace(dbPath))
            {
                SqliteStatusText.Text = "Kein Pfad angegeben";
                SqliteQuestCountText.Text = "-";
                SqliteZoneCountText.Text = "-";
                return;
            }

            if (!File.Exists(dbPath))
            {
                SqliteStatusText.Text = "Datei nicht gefunden";
                SqliteStatusText.Foreground = new SolidColorBrush(Colors.Red);
                SqliteQuestCountText.Text = "-";
                SqliteZoneCountText.Text = "-";
                return;
            }

            StatusText.Text = "Teste SQLite-Verbindung...";
            StatusBorder.Background = new SolidColorBrush(Color.FromRgb(204, 229, 255));
            StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(184, 218, 255));

            try
            {
                using var repository = new SqliteQuestRepository(dbPath);

                var isConnected = await repository.TestConnectionAsync();
                if (isConnected)
                {
                    var questCount = await repository.GetQuestCountAsync();
                    var zones = await repository.GetAllZonesAsync();

                    SqliteStatusText.Text = "Verbunden";
                    SqliteStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    SqliteQuestCountText.Text = questCount.ToString("N0");
                    SqliteZoneCountText.Text = zones.Count.ToString("N0");

                    StatusBorder.Background = new SolidColorBrush(Color.FromRgb(212, 237, 218));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(195, 230, 203));
                    StatusText.Text = $"SQLite-Verbindung erfolgreich! {questCount:N0} Quests in {zones.Count} Zonen.";
                }
                else
                {
                    SqliteStatusText.Text = "Verbindung fehlgeschlagen";
                    SqliteStatusText.Foreground = new SolidColorBrush(Colors.Red);
                    SqliteQuestCountText.Text = "-";
                    SqliteZoneCountText.Text = "-";

                    StatusBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                    StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));
                    StatusText.Text = "SQLite-Verbindung fehlgeschlagen.";
                }
            }
            catch (Exception ex)
            {
                SqliteStatusText.Text = "Fehler";
                SqliteStatusText.Foreground = new SolidColorBrush(Colors.Red);
                SqliteQuestCountText.Text = "-";
                SqliteZoneCountText.Text = "-";

                StatusBorder.Background = new SolidColorBrush(Color.FromRgb(248, 215, 218));
                StatusBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 198, 203));
                StatusText.Text = $"SQLite Fehler: {ex.Message}";
            }
        }

        #endregion

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var config = _configService.Config;
            var exportSettings = TtsExportSettings.Instance;

            // Kosten-Tracking
            if (double.TryParse(AvgCharsPerTokenBox.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var avgChars) && avgChars > 0)
            {
                exportSettings.AvgCharsPerToken = avgChars;
            }

            if (decimal.TryParse(CostPer1kTokensBox.Text, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var costPer1k) && costPer1k >= 0)
            {
                exportSettings.CostPer1kTokens = costPer1k;
            }

            if (!string.IsNullOrWhiteSpace(CurrencySymbolBox.Text))
            {
                exportSettings.CurrencySymbol = CurrencySymbolBox.Text.Trim();
            }

            // Voice-Profile speichern
            SaveVoiceProfiles(exportSettings);

            // ElevenLabs
            config.ElevenLabs.ApiKey = _apiKey;
            config.ElevenLabs.VoiceId = VoiceIdBox.Text.Trim();
            config.ElevenLabs.ModelId = (ModelIdCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString()
                                        ?? "eleven_multilingual_v2";
            config.ElevenLabs.VoiceSettings.Stability = StabilitySlider.Value;
            config.ElevenLabs.VoiceSettings.SimilarityBoost = SimilaritySlider.Value;

            // Male/Female Voice-IDs aus ElevenLabs Tab speichern
            var maleVoiceId = ElevenLabsMaleVoiceIdBox.Text?.Trim() ?? "";
            var femaleVoiceId = ElevenLabsFemaleVoiceIdBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(maleVoiceId))
            {
                exportSettings.MaleVoiceId = maleVoiceId;
                // Auch im Stimmen-Tab synchronisieren
                MaleVoiceIdBox.Text = maleVoiceId;
            }
            if (!string.IsNullOrEmpty(femaleVoiceId))
            {
                exportSettings.FemaleVoiceId = femaleVoiceId;
                // Auch im Stimmen-Tab synchronisieren
                FemaleVoiceIdBox.Text = femaleVoiceId;
            }

            // Blizzard
            config.Blizzard.ClientId = BlizzardClientIdBox.Text?.Trim() ?? "";
            config.Blizzard.ClientSecret = _blizzardSecret;
            config.Blizzard.Region = (BlizzardRegionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "eu";

            if (int.TryParse(MaxQuestsBox.Text, out int maxQuests) && maxQuests > 0)
            {
                config.Blizzard.MaxQuests = maxQuests;
            }

            // LLM
            config.Llm.Provider = (LlmProviderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "OpenAI";
            config.Llm.ApiKey = _llmApiKey;
            config.Llm.ModelId = LlmModelCombo.Text;
            config.Llm.Temperature = LlmTemperatureSlider.Value;

            if (int.TryParse(LlmMaxTokensBox.Text, out int maxTokens) && maxTokens > 0)
            {
                config.Llm.MaxTokens = maxTokens;
            }

            config.Llm.AutoEnhance = LlmAutoEnhanceCheckBox.IsChecked ?? false;

            var systemPrompt = LlmSystemPromptBox.Text?.Trim();
            config.Llm.SystemPrompt = string.IsNullOrEmpty(systemPrompt) ? null : systemPrompt;

            // SQLite
            exportSettings.UseSqliteDatabase = UseSqliteCheckBox.IsChecked ?? false;
            exportSettings.SqliteDatabasePath = SqlitePathBox.Text?.Trim() ?? "";

            try
            {
                _configService.SaveConfig(config);
                exportSettings.Save();
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
