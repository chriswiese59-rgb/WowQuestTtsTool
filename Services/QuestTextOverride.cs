using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Einzelner Text-Override fuer eine Quest.
    /// Speichert manuelle Aenderungen an Title, Description, Objectives, Completion.
    /// </summary>
    public class QuestTextOverride
    {
        [JsonPropertyName("quest_id")]
        public int QuestId { get; set; }

        [JsonPropertyName("title")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Title { get; set; }

        [JsonPropertyName("description")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Description { get; set; }

        [JsonPropertyName("objectives")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Objectives { get; set; }

        [JsonPropertyName("completion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Completion { get; set; }

        /// <summary>
        /// Kategorie-Override (falls manuell geaendert).
        /// </summary>
        [JsonPropertyName("category")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? Category { get; set; }

        /// <summary>
        /// IsMainStory-Override (falls manuell geaendert).
        /// </summary>
        [JsonPropertyName("is_main_story")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsMainStory { get; set; }

        /// <summary>
        /// IsGroupQuest-Override (falls manuell geaendert).
        /// </summary>
        [JsonPropertyName("is_group_quest")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? IsGroupQuest { get; set; }

        /// <summary>
        /// Zeitstempel der letzten Aenderung.
        /// </summary>
        [JsonPropertyName("modified_at")]
        public DateTime ModifiedAt { get; set; } = DateTime.Now;

        /// <summary>
        /// Prueft ob dieser Override ueberhaupt Daten enthaelt.
        /// </summary>
        [JsonIgnore]
        public bool HasAnyOverride =>
            !string.IsNullOrEmpty(Title) ||
            !string.IsNullOrEmpty(Description) ||
            !string.IsNullOrEmpty(Objectives) ||
            !string.IsNullOrEmpty(Completion) ||
            !string.IsNullOrEmpty(Category) ||
            IsMainStory.HasValue ||
            IsGroupQuest.HasValue;
    }

    /// <summary>
    /// Container fuer alle Quest-Text-Overrides.
    /// Wird als quest_overrides.json persistiert.
    /// </summary>
    public class QuestTextOverridesContainer
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("generated_at")]
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        [JsonPropertyName("overrides")]
        public Dictionary<int, QuestTextOverride> Overrides { get; set; } = [];
    }

    /// <summary>
    /// Service zum Laden, Speichern und Anwenden von Quest-Text-Overrides.
    /// </summary>
    public static class QuestTextOverridesStore
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        private const string DefaultFileName = "quest_overrides.json";

        /// <summary>
        /// Ermittelt den Standard-Pfad fuer die Override-Datei.
        /// </summary>
        public static string GetDefaultPath()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(baseDir, "data", DefaultFileName);
        }

        /// <summary>
        /// Laedt die Overrides aus einer JSON-Datei.
        /// </summary>
        /// <param name="filePath">Pfad zur Datei (optional, sonst Default)</param>
        /// <returns>Container mit allen Overrides</returns>
        public static QuestTextOverridesContainer Load(string? filePath = null)
        {
            filePath ??= GetDefaultPath();

            if (!File.Exists(filePath))
                return new QuestTextOverridesContainer();

            try
            {
                var json = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
                var container = JsonSerializer.Deserialize<QuestTextOverridesContainer>(json);
                return container ?? new QuestTextOverridesContainer();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der Overrides: {ex.Message}");
                return new QuestTextOverridesContainer();
            }
        }

        /// <summary>
        /// Speichert die Overrides in eine JSON-Datei.
        /// </summary>
        /// <param name="container">Container mit allen Overrides</param>
        /// <param name="filePath">Pfad zur Datei (optional, sonst Default)</param>
        public static void Save(QuestTextOverridesContainer container, string? filePath = null)
        {
            filePath ??= GetDefaultPath();

            try
            {
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Leere Overrides entfernen
                var cleanedOverrides = new Dictionary<int, QuestTextOverride>();
                foreach (var kvp in container.Overrides)
                {
                    if (kvp.Value.HasAnyOverride)
                    {
                        cleanedOverrides[kvp.Key] = kvp.Value;
                    }
                }
                container.Overrides = cleanedOverrides;
                container.GeneratedAt = DateTime.Now;

                var json = JsonSerializer.Serialize(container, s_jsonOptions);
                File.WriteAllText(filePath, json, System.Text.Encoding.UTF8);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Speichern der Overrides: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Wendet alle Overrides auf eine Liste von Quests an.
        /// Sollte NACH dem Laden der Quest-Daten aus DB/API aufgerufen werden.
        /// </summary>
        /// <param name="quests">Liste der Quests</param>
        /// <param name="container">Container mit Overrides</param>
        /// <returns>Anzahl der angewandten Overrides</returns>
        public static int ApplyOverrides(IEnumerable<Quest> quests, QuestTextOverridesContainer container)
        {
            if (quests == null || container?.Overrides == null)
                return 0;

            int count = 0;

            foreach (var quest in quests)
            {
                if (container.Overrides.TryGetValue(quest.QuestId, out var over))
                {
                    // Texte anwenden (nur wenn Override nicht leer)
                    if (!string.IsNullOrEmpty(over.Title))
                        quest.Title = over.Title;

                    if (!string.IsNullOrEmpty(over.Description))
                        quest.Description = over.Description;

                    if (!string.IsNullOrEmpty(over.Objectives))
                        quest.Objectives = over.Objectives;

                    if (!string.IsNullOrEmpty(over.Completion))
                        quest.Completion = over.Completion;

                    // Flags anwenden
                    if (over.IsMainStory.HasValue)
                        quest.IsMainStory = over.IsMainStory.Value;

                    if (over.IsGroupQuest.HasValue)
                        quest.IsGroupQuest = over.IsGroupQuest.Value;

                    // Kategorie anwenden
                    if (!string.IsNullOrEmpty(over.Category) &&
                        Enum.TryParse<QuestCategory>(over.Category, out var cat))
                    {
                        quest.Category = cat;
                    }

                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// Speichert einen Override fuer eine Quest.
        /// </summary>
        /// <param name="container">Container</param>
        /// <param name="quest">Quest mit aktuellen Werten</param>
        /// <param name="originalQuest">Original-Quest (zum Vergleich, optional)</param>
        public static void SetOverride(QuestTextOverridesContainer container, Quest quest, Quest? originalQuest = null)
        {
            ArgumentNullException.ThrowIfNull(container);
            ArgumentNullException.ThrowIfNull(quest);

            var over = new QuestTextOverride
            {
                QuestId = quest.QuestId,
                Title = quest.Title,
                Description = quest.Description,
                Objectives = quest.Objectives,
                Completion = quest.Completion,
                IsMainStory = quest.IsMainStory,
                IsGroupQuest = quest.IsGroupQuest,
                Category = quest.Category.ToString(),
                ModifiedAt = DateTime.Now
            };

            container.Overrides[quest.QuestId] = over;
        }

        /// <summary>
        /// Entfernt einen Override fuer eine Quest.
        /// </summary>
        public static bool RemoveOverride(QuestTextOverridesContainer container, int questId)
        {
            return container?.Overrides?.Remove(questId) ?? false;
        }
    }
}
