using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Schreibt und verwaltet den JSON-Index fuer generierte Quest-Audio-Dateien.
    /// </summary>
    public static class AudioIndexWriter
    {
        private static readonly JsonSerializerOptions s_jsonOptions = new()
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>
        /// Fuegt einen neuen Eintrag zum Index hinzu.
        /// </summary>
        /// <param name="index">Der Index, zu dem der Eintrag hinzugefuegt wird</param>
        /// <param name="rootFolder">Basis-Ausgabeordner</param>
        /// <param name="language">Sprachcode (z.B. "deDE")</param>
        /// <param name="quest">Quest-Objekt</param>
        /// <param name="genderCode">Stimm-Gender ("male", "female", "neutral")</param>
        /// <param name="fullFilePath">Vollstaendiger Pfad zur generierten Audio-Datei</param>
        public static void AddEntry(
            QuestAudioIndex index,
            string rootFolder,
            string language,
            Quest quest,
            string genderCode,
            string fullFilePath)
        {
            ArgumentNullException.ThrowIfNull(index, nameof(index));
            ArgumentNullException.ThrowIfNull(quest, nameof(quest));
            ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder, nameof(rootFolder));
            ArgumentException.ThrowIfNullOrWhiteSpace(fullFilePath, nameof(fullFilePath));

            // Defaults
            if (string.IsNullOrWhiteSpace(language))
                language = "deDE";

            if (string.IsNullOrWhiteSpace(genderCode))
                genderCode = "neutral";

            // Relativen Pfad berechnen
            var relativePath = AudioPathHelper.GetRelativePath(rootFolder, fullFilePath);

            // Dateigroesse ermitteln (falls Datei existiert)
            long fileSize = 0;
            if (File.Exists(fullFilePath))
            {
                try
                {
                    fileSize = new FileInfo(fullFilePath).Length;
                }
                catch
                {
                    // Ignorieren falls Zugriff fehlschlaegt
                }
            }

            // Neuen Eintrag erstellen
            var entry = new QuestAudioIndexEntry
            {
                QuestId = quest.QuestId,
                Zone = quest.Zone ?? "Unknown",
                IsMainStory = quest.IsMainStory,
                Gender = genderCode.ToLowerInvariant(),
                RelativePath = relativePath,
                Title = quest.Title ?? "",
                Category = quest.CategoryShortName ?? (quest.IsMainStory ? "Main" : "Side"),
                FileSizeBytes = fileSize
            };

            // Zum Index hinzufuegen
            index.Entries.Add(entry);

            // Index-Metadaten aktualisieren
            index.Language = language;
            index.GeneratedAtUtc = DateTime.UtcNow;
        }

        /// <summary>
        /// Speichert den Index als JSON-Datei.
        /// Pfad: {rootFolder}/{language}/quests_audio_index.json
        /// </summary>
        /// <param name="index">Der zu speichernde Index</param>
        /// <param name="rootFolder">Basis-Ausgabeordner</param>
        /// <param name="language">Sprachcode (Standard: "deDE")</param>
        public static void SaveIndex(QuestAudioIndex index, string rootFolder, string language = "deDE")
        {
            ArgumentNullException.ThrowIfNull(index, nameof(index));
            ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder, nameof(rootFolder));

            if (string.IsNullOrWhiteSpace(language))
                language = "deDE";

            // Sicheren Ordnernamen fuer Sprache
            var safeLanguageFolder = AudioPathHelper.MakeSafeFolderName(language);

            // Zielordner erstellen
            var targetFolder = Path.Combine(rootFolder, safeLanguageFolder);
            if (!Directory.Exists(targetFolder))
            {
                Directory.CreateDirectory(targetFolder);
            }

            // Index-Datei Pfad
            var indexFilePath = Path.Combine(targetFolder, "quests_audio_index.json");

            // Zeitstempel aktualisieren
            index.GeneratedAtUtc = DateTime.UtcNow;

            // JSON serialisieren und speichern
            var json = JsonSerializer.Serialize(index, s_jsonOptions);
            File.WriteAllText(indexFilePath, json, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Laedt einen existierenden Index aus einer JSON-Datei.
        /// </summary>
        /// <param name="rootFolder">Basis-Ausgabeordner</param>
        /// <param name="language">Sprachcode (Standard: "deDE")</param>
        /// <returns>Der geladene Index oder ein neuer leerer Index</returns>
        public static QuestAudioIndex LoadIndex(string rootFolder, string language = "deDE")
        {
            if (string.IsNullOrWhiteSpace(rootFolder))
                return new QuestAudioIndex { Language = language };

            if (string.IsNullOrWhiteSpace(language))
                language = "deDE";

            var safeLanguageFolder = AudioPathHelper.MakeSafeFolderName(language);
            var indexFilePath = Path.Combine(rootFolder, safeLanguageFolder, "quests_audio_index.json");

            if (!File.Exists(indexFilePath))
                return new QuestAudioIndex { Language = language };

            try
            {
                var json = File.ReadAllText(indexFilePath, System.Text.Encoding.UTF8);
                var index = JsonSerializer.Deserialize<QuestAudioIndex>(json);
                return index ?? new QuestAudioIndex { Language = language };
            }
            catch
            {
                // Bei Fehler neuen Index zurueckgeben
                return new QuestAudioIndex { Language = language };
            }
        }

        /// <summary>
        /// Prueft ob ein Eintrag fuer eine bestimmte Quest + Gender bereits existiert.
        /// </summary>
        public static bool EntryExists(QuestAudioIndex index, int questId, string genderCode)
        {
            if (index?.Entries == null)
                return false;

            genderCode = genderCode?.ToLowerInvariant() ?? "neutral";

            foreach (var entry in index.Entries)
            {
                if (entry.QuestId == questId &&
                    string.Equals(entry.Gender, genderCode, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Entfernt einen Eintrag aus dem Index (falls vorhanden).
        /// </summary>
        public static bool RemoveEntry(QuestAudioIndex index, int questId, string genderCode)
        {
            if (index?.Entries == null)
                return false;

            genderCode = genderCode?.ToLowerInvariant() ?? "neutral";

            for (int i = index.Entries.Count - 1; i >= 0; i--)
            {
                var entry = index.Entries[i];
                if (entry.QuestId == questId &&
                    string.Equals(entry.Gender, genderCode, StringComparison.OrdinalIgnoreCase))
                {
                    index.Entries.RemoveAt(i);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Erzeugt ein schnelles Lookup-Dictionary aus dem Index.
        /// Key: "questId|gender" (z.B. "176|male")
        /// Value: Der zugehoerige QuestAudioIndexEntry
        /// </summary>
        /// <param name="index">Der Audio-Index</param>
        /// <returns>Dictionary fuer schnellen Lookup</returns>
        public static Dictionary<string, QuestAudioIndexEntry> BuildLookupDictionary(QuestAudioIndex index)
        {
            var lookup = new Dictionary<string, QuestAudioIndexEntry>(StringComparer.OrdinalIgnoreCase);

            if (index?.Entries == null)
                return lookup;

            foreach (var entry in index.Entries)
            {
                // Key-Format: "questId|gender"
                var key = GetLookupKey(entry.QuestId, entry.Gender);

                // Bei Duplikaten: neuerer Eintrag gewinnt (ueberschreiben)
                lookup[key] = entry;
            }

            return lookup;
        }

        /// <summary>
        /// Erzeugt den Lookup-Key fuer eine Quest + Gender Kombination.
        /// </summary>
        /// <param name="questId">Quest-ID</param>
        /// <param name="genderCode">Gender-Code (male/female/neutral)</param>
        /// <returns>Lookup-Key im Format "questId|gender"</returns>
        public static string GetLookupKey(int questId, string genderCode)
        {
            genderCode = genderCode?.ToLowerInvariant() ?? "neutral";
            return $"{questId}|{genderCode}";
        }

        /// <summary>
        /// Prueft ueber das Lookup-Dictionary, ob eine Quest bereits vertont ist.
        /// </summary>
        /// <param name="lookup">Das Lookup-Dictionary</param>
        /// <param name="questId">Quest-ID</param>
        /// <param name="genderCode">Gender-Code</param>
        /// <returns>True wenn bereits vertont, sonst False</returns>
        public static bool IsAlreadyVoiced(Dictionary<string, QuestAudioIndexEntry> lookup, int questId, string genderCode)
        {
            if (lookup == null)
                return false;

            var key = GetLookupKey(questId, genderCode);
            return lookup.ContainsKey(key);
        }

        /// <summary>
        /// Holt den Index-Eintrag fuer eine Quest + Gender Kombination.
        /// </summary>
        /// <param name="lookup">Das Lookup-Dictionary</param>
        /// <param name="questId">Quest-ID</param>
        /// <param name="genderCode">Gender-Code</param>
        /// <returns>Der Eintrag oder null wenn nicht vorhanden</returns>
        public static QuestAudioIndexEntry? GetEntry(Dictionary<string, QuestAudioIndexEntry> lookup, int questId, string genderCode)
        {
            if (lookup == null)
                return null;

            var key = GetLookupKey(questId, genderCode);
            return lookup.TryGetValue(key, out var entry) ? entry : null;
        }

        /// <summary>
        /// Aktualisiert oder fuegt einen Eintrag im Index hinzu.
        /// Entfernt zuerst eventuell vorhandene alte Eintraege fuer dieselbe Quest+Gender.
        /// </summary>
        public static void UpdateEntry(
            QuestAudioIndex index,
            Dictionary<string, QuestAudioIndexEntry> lookup,
            string rootFolder,
            string language,
            Quest quest,
            string genderCode,
            string fullFilePath)
        {
            // Alten Eintrag entfernen (falls vorhanden)
            RemoveEntry(index, quest.QuestId, genderCode);

            // Neuen Eintrag hinzufuegen
            AddEntry(index, rootFolder, language, quest, genderCode, fullFilePath);

            // Lookup aktualisieren
            if (lookup != null)
            {
                var key = GetLookupKey(quest.QuestId, genderCode);
                var newEntry = index.Entries[^1]; // Letzter Eintrag ist der neue
                lookup[key] = newEntry;
            }
        }
    }
}
