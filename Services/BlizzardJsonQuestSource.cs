using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Liest Blizzard-Quests aus einer JSON-Datei.
    /// Die JSON-Datei wurde vorher von der Blizzard API geholt oder manuell erstellt.
    /// </summary>
    public class BlizzardJsonQuestSource : IBlizzardQuestSource
    {
        private readonly string _jsonPath;
        private List<Quest>? _cachedQuests;
        private Dictionary<int, Quest>? _questLookup;

        public BlizzardJsonQuestSource(string jsonPath)
        {
            _jsonPath = jsonPath ?? throw new ArgumentNullException(nameof(jsonPath));
        }

        /// <summary>
        /// Gibt an, ob die JSON-Datei existiert.
        /// </summary>
        public bool IsAvailable => File.Exists(_jsonPath);

        /// <summary>
        /// Laedt eine einzelne Quest nach ID.
        /// </summary>
        public async Task<Quest?> GetQuestByIdAsync(int questId)
        {
            await EnsureLoadedAsync();

            if (_questLookup == null)
                return null;

            _questLookup.TryGetValue(questId, out var quest);
            return quest;
        }

        /// <summary>
        /// Laedt alle Quests aus der JSON-Datei.
        /// </summary>
        public async Task<IReadOnlyList<Quest>> GetAllQuestsAsync()
        {
            await EnsureLoadedAsync();
            return _cachedQuests ?? (IReadOnlyList<Quest>)Array.Empty<Quest>();
        }

        /// <summary>
        /// Laedt die JSON-Datei wenn noch nicht geschehen.
        /// </summary>
        private async Task EnsureLoadedAsync()
        {
            if (_cachedQuests != null)
                return;

            if (!IsAvailable)
            {
                _cachedQuests = new List<Quest>();
                _questLookup = new Dictionary<int, Quest>();
                return;
            }

            try
            {
                var json = await File.ReadAllTextAsync(_jsonPath);
                var quests = JsonSerializer.Deserialize<Quest[]>(json);

                if (quests != null)
                {
                    // Markiere alle als Blizzard-Quelle
                    foreach (var q in quests)
                    {
                        q.HasBlizzardSource = true;
                        q.HasAcoreSource = false;
                    }

                    _cachedQuests = quests.ToList();
                    _questLookup = _cachedQuests.ToDictionary(q => q.QuestId);
                }
                else
                {
                    _cachedQuests = new List<Quest>();
                    _questLookup = new Dictionary<int, Quest>();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Fehler beim Laden der Blizzard-JSON: {ex.Message}");
                _cachedQuests = new List<Quest>();
                _questLookup = new Dictionary<int, Quest>();
            }
        }

        /// <summary>
        /// Invalidiert den Cache (z.B. nach Dateiaktualisierung).
        /// </summary>
        public void InvalidateCache()
        {
            _cachedQuests = null;
            _questLookup = null;
        }
    }
}
