using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Service fuer die Optimierung von Quest-Texten mittels LLM (ChatGPT, Claude, Gemini).
    /// Wandelt knappe Quest-Texte in hoerbuchreife, lebendige Erzaehlungen um.
    /// </summary>
    public class LlmTextEnhancerService
    {
        private readonly HttpClient _httpClient;
        private readonly LlmConfig _config;

        /// <summary>
        /// System-Prompt fuer die Hoerbuch-Optimierung.
        /// Kann ueber Config angepasst werden.
        /// </summary>
        private const string DefaultSystemPrompt = @"Du bist ein erfahrener Hoerbuch-Autor und Uebersetzer, spezialisiert auf Fantasy-Rollenspiele wie World of Warcraft.

Deine Aufgabe: Wandle knappe Quest-Texte in lebendige, hoerbuchreife Erzaehlungen um.

WICHTIGE REGELN:
1. INHALT NICHT AENDERN: Alle Fakten, Zahlen, Namen und Ziele muessen exakt erhalten bleiben
2. Stil: Episch, immersiv, wie ein Erzaehler in einem Hoerbuch
3. Sprache: Deutsch, gehoben aber verstaendlich
4. Laenge: Moderat erweitern, nicht zu lang (max. 2-3x Original)
5. Keine Anrede an den Spieler mit ""du"" - stattdessen ""Ihr"" oder passive Formulierungen
6. Platzhalter wie [NPC], [Zone] oder $n beibehalten

BEISPIEL:
Original: ""Toetet 8 Schweine und kehrt zurueck""
Optimiert: ""Begebt Euch zu den Feldern im Westen und bezwingt acht der wilden Schweine, die dort die Ernte bedrohen. Kehrt danach zurueck, um Eure wohlverdiente Belohnung zu erhalten.""

Antworte NUR mit dem optimierten Text, ohne Erklaerungen oder Kommentare.";

        /// <summary>
        /// Erstellt einen neuen LlmTextEnhancerService.
        /// </summary>
        /// <param name="config">LLM-Konfiguration (API-Keys, Provider, etc.)</param>
        public LlmTextEnhancerService(LlmConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(60)
            };
        }

        /// <summary>
        /// Gibt an, ob der Service konfiguriert und einsatzbereit ist.
        /// </summary>
        public bool IsConfigured => !string.IsNullOrWhiteSpace(_config.ApiKey) &&
                                    !string.IsNullOrWhiteSpace(_config.Provider);

        /// <summary>
        /// Aktueller Provider-Name.
        /// </summary>
        public string ProviderName => _config.Provider;

        /// <summary>
        /// Optimiert einen Quest-Text fuer Hoerbuch-Qualitaet.
        /// </summary>
        /// <param name="questTitle">Quest-Titel (fuer Kontext)</param>
        /// <param name="originalText">Original-Quest-Text</param>
        /// <param name="questContext">Optionaler zusaetzlicher Kontext (Zone, NPC-Namen, etc.)</param>
        /// <param name="cancellationToken">Abbruch-Token</param>
        /// <returns>Optimierter Text oder Original bei Fehler</returns>
        public async Task<TextEnhancementResult> EnhanceTextAsync(
            string questTitle,
            string originalText,
            string? questContext = null,
            CancellationToken cancellationToken = default)
        {
            if (!IsConfigured)
            {
                return TextEnhancementResult.Failure(originalText, "LLM nicht konfiguriert. Bitte API-Key in den Einstellungen hinterlegen.");
            }

            if (string.IsNullOrWhiteSpace(originalText))
            {
                return TextEnhancementResult.Failure(originalText, "Kein Text zum Optimieren vorhanden.");
            }

            try
            {
                // User-Prompt zusammenbauen
                var userPrompt = BuildUserPrompt(questTitle, originalText, questContext);

                // Je nach Provider unterschiedliche API aufrufen
                var enhancedText = _config.Provider.ToLowerInvariant() switch
                {
                    "openai" or "chatgpt" => await CallOpenAiAsync(userPrompt, cancellationToken),
                    "anthropic" or "claude" => await CallClaudeAsync(userPrompt, cancellationToken),
                    "google" or "gemini" => await CallGeminiAsync(userPrompt, cancellationToken),
                    _ => throw new NotSupportedException($"Provider '{_config.Provider}' wird nicht unterstuetzt.")
                };

                if (string.IsNullOrWhiteSpace(enhancedText))
                {
                    return TextEnhancementResult.Failure(originalText, "LLM hat leere Antwort zurueckgegeben.");
                }

                return TextEnhancementResult.Success(enhancedText, originalText);
            }
            catch (OperationCanceledException)
            {
                return TextEnhancementResult.Failure(originalText, "Anfrage wurde abgebrochen.");
            }
            catch (Exception ex)
            {
                return TextEnhancementResult.Failure(originalText, $"Fehler bei LLM-Anfrage: {ex.Message}");
            }
        }

        /// <summary>
        /// Optimiert mehrere Quest-Texte auf einmal (Batch).
        /// </summary>
        public async Task<TextEnhancementResult> EnhanceQuestAsync(
            Quest quest,
            CancellationToken cancellationToken = default)
        {
            if (quest == null)
            {
                return TextEnhancementResult.Failure("", "Keine Quest angegeben.");
            }

            // Kontext aus Quest-Daten zusammenbauen
            var context = $"Zone: {quest.Zone ?? "Unbekannt"}, Kategorie: {quest.CategoryShortName}";

            // Den kompletten TTS-Text optimieren
            var originalTtsText = quest.AutoGeneratedTtsText;

            return await EnhanceTextAsync(
                quest.Title ?? $"Quest {quest.QuestId}",
                originalTtsText,
                context,
                cancellationToken);
        }

        private string BuildUserPrompt(string questTitle, string originalText, string? context)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"Quest-Titel: {questTitle}");

            if (!string.IsNullOrWhiteSpace(context))
            {
                sb.AppendLine($"Kontext: {context}");
            }

            sb.AppendLine();
            sb.AppendLine("Original-Text:");
            sb.AppendLine(originalText);
            sb.AppendLine();
            sb.AppendLine("Bitte optimiere diesen Text fuer ein Hoerbuch:");

            return sb.ToString();
        }

        #region OpenAI / ChatGPT

        private async Task<string> CallOpenAiAsync(string userPrompt, CancellationToken cancellationToken)
        {
            var requestBody = new
            {
                model = _config.ModelId ?? "gpt-4o-mini",
                messages = new[]
                {
                    new { role = "system", content = _config.SystemPrompt ?? DefaultSystemPrompt },
                    new { role = "user", content = userPrompt }
                },
                temperature = _config.Temperature,
                max_tokens = _config.MaxTokens
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _config.ApiKey);

            var response = await _httpClient.PostAsync(
                "https://api.openai.com/v1/chat/completions",
                content,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<OpenAiResponse>(responseJson);

            return responseObj?.Choices?[0]?.Message?.Content?.Trim() ?? "";
        }

        #endregion

        #region Anthropic / Claude

        private async Task<string> CallClaudeAsync(string userPrompt, CancellationToken cancellationToken)
        {
            var requestBody = new
            {
                model = _config.ModelId ?? "claude-3-haiku-20240307",
                max_tokens = _config.MaxTokens,
                system = _config.SystemPrompt ?? DefaultSystemPrompt,
                messages = new[]
                {
                    new { role = "user", content = userPrompt }
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("x-api-key", _config.ApiKey);
            _httpClient.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            var response = await _httpClient.PostAsync(
                "https://api.anthropic.com/v1/messages",
                content,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<ClaudeResponse>(responseJson);

            return responseObj?.Content?[0]?.Text?.Trim() ?? "";
        }

        #endregion

        #region Google / Gemini

        private async Task<string> CallGeminiAsync(string userPrompt, CancellationToken cancellationToken)
        {
            var modelId = _config.ModelId ?? "gemini-1.5-flash";
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{modelId}:generateContent?key={_config.ApiKey}";

            var requestBody = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = _config.SystemPrompt ?? DefaultSystemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        parts = new[] { new { text = userPrompt } }
                    }
                },
                generationConfig = new
                {
                    temperature = _config.Temperature,
                    maxOutputTokens = _config.MaxTokens
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            _httpClient.DefaultRequestHeaders.Clear();

            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var responseObj = JsonSerializer.Deserialize<GeminiResponse>(responseJson);

            return responseObj?.Candidates?[0]?.Content?.Parts?[0]?.Text?.Trim() ?? "";
        }

        #endregion
    }

    #region Response Models

    internal class OpenAiResponse
    {
        [JsonPropertyName("choices")]
        public OpenAiChoice[]? Choices { get; set; }
    }

    internal class OpenAiChoice
    {
        [JsonPropertyName("message")]
        public OpenAiMessage? Message { get; set; }
    }

    internal class OpenAiMessage
    {
        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    internal class ClaudeResponse
    {
        [JsonPropertyName("content")]
        public ClaudeContent[]? Content { get; set; }
    }

    internal class ClaudeContent
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    internal class GeminiResponse
    {
        [JsonPropertyName("candidates")]
        public GeminiCandidate[]? Candidates { get; set; }
    }

    internal class GeminiCandidate
    {
        [JsonPropertyName("content")]
        public GeminiContent? Content { get; set; }
    }

    internal class GeminiContent
    {
        [JsonPropertyName("parts")]
        public GeminiPart[]? Parts { get; set; }
    }

    internal class GeminiPart
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    #endregion

    #region Config & Result Classes

    /// <summary>
    /// Modus fuer die Text-KI: API (automatisch, kostet Tokens) oder Manual (Premium-Account im Browser).
    /// </summary>
    public enum TextGenerationMode
    {
        /// <summary>
        /// Automatischer API-Modus - Tool ruft direkt die Text-API auf (kostet Tokens).
        /// </summary>
        Api,

        /// <summary>
        /// Manueller Modus - User kopiert Prompt in Browser (ChatGPT+, Gemini Pro, Claude Enterprise)
        /// und fuegt Ergebnis manuell ein. Keine API-Kosten.
        /// </summary>
        Manual
    }

    /// <summary>
    /// Konfiguration fuer den LLM Text Enhancer Service.
    /// </summary>
    public class LlmConfig
    {
        /// <summary>
        /// API-Provider: "OpenAI", "Anthropic", "Google"
        /// </summary>
        [JsonPropertyName("provider")]
        public string Provider { get; set; } = "OpenAI";

        /// <summary>
        /// API-Key fuer den gewaehlten Provider.
        /// </summary>
        [JsonPropertyName("api_key")]
        public string ApiKey { get; set; } = "";

        /// <summary>
        /// Model-ID (z.B. "gpt-4o-mini", "claude-3-haiku-20240307", "gemini-1.5-flash")
        /// </summary>
        [JsonPropertyName("model_id")]
        public string? ModelId { get; set; }

        /// <summary>
        /// Temperatur fuer die Text-Generierung (0.0 = deterministisch, 1.0 = kreativ).
        /// </summary>
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; } = 0.7;

        /// <summary>
        /// Maximale Anzahl an Output-Tokens.
        /// </summary>
        [JsonPropertyName("max_tokens")]
        public int MaxTokens { get; set; } = 1024;

        /// <summary>
        /// Optionaler benutzerdefinierter System-Prompt.
        /// </summary>
        [JsonPropertyName("system_prompt")]
        public string? SystemPrompt { get; set; }

        /// <summary>
        /// Ob die Text-Optimierung automatisch bei TTS-Generierung aktiviert ist.
        /// </summary>
        [JsonPropertyName("auto_enhance")]
        public bool AutoEnhance { get; set; } = false;

        /// <summary>
        /// Text-Generierungs-Modus: Api (automatisch, Tokens) oder Manual (Browser-Premium).
        /// </summary>
        [JsonPropertyName("text_generation_mode")]
        public TextGenerationMode Mode { get; set; } = TextGenerationMode.Api;
    }

    /// <summary>
    /// Ergebnis einer Text-Optimierung.
    /// </summary>
    public class TextEnhancementResult
    {
        /// <summary>
        /// Ob die Optimierung erfolgreich war.
        /// </summary>
        public bool IsSuccess { get; private set; }

        /// <summary>
        /// Der optimierte Text (oder Original bei Fehler).
        /// </summary>
        public string EnhancedText { get; private set; } = "";

        /// <summary>
        /// Der urspruengliche Text.
        /// </summary>
        public string OriginalText { get; private set; } = "";

        /// <summary>
        /// Fehlermeldung (falls nicht erfolgreich).
        /// </summary>
        public string? ErrorMessage { get; private set; }

        /// <summary>
        /// Differenz der Zeichenanzahl (positiv = laenger).
        /// </summary>
        public int CharacterDifference => EnhancedText.Length - OriginalText.Length;

        /// <summary>
        /// Prozentuale Laengenaenderung.
        /// </summary>
        public double LengthChangePercent => OriginalText.Length > 0
            ? ((EnhancedText.Length - OriginalText.Length) * 100.0 / OriginalText.Length)
            : 0;

        private TextEnhancementResult() { }

        public static TextEnhancementResult Success(string enhancedText, string originalText)
        {
            return new TextEnhancementResult
            {
                IsSuccess = true,
                EnhancedText = enhancedText,
                OriginalText = originalText
            };
        }

        public static TextEnhancementResult Failure(string originalText, string errorMessage)
        {
            return new TextEnhancementResult
            {
                IsSuccess = false,
                EnhancedText = originalText, // Fallback auf Original
                OriginalText = originalText,
                ErrorMessage = errorMessage
            };
        }
    }

    #endregion
}
