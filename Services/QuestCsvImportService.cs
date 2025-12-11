using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Service zum Importieren von Quest-Texten aus CSV-Dateien.
    /// Unterstützt verschiedene CSV-Formate (Semikolon, Komma, Tab).
    /// </summary>
    public class QuestCsvImportService
    {
        /// <summary>
        /// Ergebnis eines CSV-Imports.
        /// </summary>
        public class ImportResult
        {
            public int TotalRows { get; set; }
            public int UpdatedQuests { get; set; }
            public int SkippedRows { get; set; }
            public int NotFoundQuests { get; set; }
            public List<string> Errors { get; } = [];
            public List<string> Warnings { get; } = [];

            public bool HasErrors => Errors.Count > 0;
            public bool IsSuccess => !HasErrors && UpdatedQuests > 0;

            public string Summary =>
                $"Import: {UpdatedQuests} Quests aktualisiert, {SkippedRows} übersprungen, {NotFoundQuests} nicht gefunden";
        }

        /// <summary>
        /// Importiert Quest-Texte aus einer CSV-Datei.
        /// Erwartet mindestens die Spalten: quest_id (oder QuestId, ID)
        /// Optionale Spalten: objectives, completion, description, title
        /// </summary>
        public static ImportResult ImportFromCsv(string csvPath, IList<Quest> quests)
        {
            var result = new ImportResult();

            if (!File.Exists(csvPath))
            {
                result.Errors.Add($"Datei nicht gefunden: {csvPath}");
                return result;
            }

            try
            {
                var lines = File.ReadAllLines(csvPath, Encoding.UTF8);
                if (lines.Length == 0)
                {
                    result.Errors.Add("CSV-Datei ist leer.");
                    return result;
                }

                // Trennzeichen erkennen
                var delimiter = DetectDelimiter(lines[0]);

                // Header parsen
                var headers = ParseCsvLine(lines[0], delimiter)
                    .Select(h => h.Trim().ToLowerInvariant())
                    .ToArray();

                var columnMap = MapColumns(headers);
                if (columnMap.QuestIdIndex < 0)
                {
                    result.Errors.Add("Keine Quest-ID-Spalte gefunden. Erwartet: quest_id, questid, id");
                    return result;
                }

                // Quest-Dictionary für schnellen Zugriff
                var questDict = quests.ToDictionary(q => q.QuestId);

                // Daten importieren
                for (int i = 1; i < lines.Length; i++)
                {
                    result.TotalRows++;
                    var line = lines[i];

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        result.SkippedRows++;
                        continue;
                    }

                    try
                    {
                        var values = ParseCsvLine(line, delimiter);

                        // Quest-ID auslesen
                        if (columnMap.QuestIdIndex >= values.Length)
                        {
                            result.Warnings.Add($"Zeile {i + 1}: Nicht genug Spalten");
                            result.SkippedRows++;
                            continue;
                        }

                        var questIdStr = values[columnMap.QuestIdIndex].Trim();
                        if (!int.TryParse(questIdStr, out int questId))
                        {
                            result.Warnings.Add($"Zeile {i + 1}: Ungültige Quest-ID '{questIdStr}'");
                            result.SkippedRows++;
                            continue;
                        }

                        // Quest finden
                        if (!questDict.TryGetValue(questId, out var quest))
                        {
                            result.NotFoundQuests++;
                            continue;
                        }

                        // Felder aktualisieren
                        bool updated = false;

                        if (columnMap.ObjectivesIndex >= 0 && columnMap.ObjectivesIndex < values.Length)
                        {
                            var objectives = CleanCsvValue(values[columnMap.ObjectivesIndex]);
                            if (!string.IsNullOrWhiteSpace(objectives))
                            {
                                quest.Objectives = objectives;
                                updated = true;
                            }
                        }

                        if (columnMap.CompletionIndex >= 0 && columnMap.CompletionIndex < values.Length)
                        {
                            var completion = CleanCsvValue(values[columnMap.CompletionIndex]);
                            if (!string.IsNullOrWhiteSpace(completion))
                            {
                                quest.Completion = completion;
                                updated = true;
                            }
                        }

                        if (columnMap.DescriptionIndex >= 0 && columnMap.DescriptionIndex < values.Length)
                        {
                            var description = CleanCsvValue(values[columnMap.DescriptionIndex]);
                            if (!string.IsNullOrWhiteSpace(description))
                            {
                                quest.Description = description;
                                updated = true;
                            }
                        }

                        if (columnMap.TitleIndex >= 0 && columnMap.TitleIndex < values.Length)
                        {
                            var title = CleanCsvValue(values[columnMap.TitleIndex]);
                            if (!string.IsNullOrWhiteSpace(title))
                            {
                                quest.Title = title;
                                updated = true;
                            }
                        }

                        if (updated)
                        {
                            result.UpdatedQuests++;
                        }
                        else
                        {
                            result.SkippedRows++;
                        }
                    }
                    catch (Exception ex)
                    {
                        result.Warnings.Add($"Zeile {i + 1}: {ex.Message}");
                        result.SkippedRows++;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Fehler beim Lesen der CSV-Datei: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Erkennt das Trennzeichen der CSV-Datei.
        /// </summary>
        private static char DetectDelimiter(string headerLine)
        {
            var delimiters = new[] { ';', ',', '\t' };
            var counts = delimiters.Select(d => new { Delimiter = d, Count = headerLine.Count(c => c == d) });
            return counts.OrderByDescending(x => x.Count).First().Delimiter;
        }

        /// <summary>
        /// Mappt die Header-Spalten auf ihre Indizes.
        /// </summary>
        private static ColumnMap MapColumns(string[] headers)
        {
            var map = new ColumnMap();

            for (int i = 0; i < headers.Length; i++)
            {
                var header = headers[i];

                // Quest-ID
                if (header is "quest_id" or "questid" or "id" or "quest id")
                {
                    map.QuestIdIndex = i;
                }
                // Objectives
                else if (header is "objectives" or "ziele" or "objective" or "quest_objectives")
                {
                    map.ObjectivesIndex = i;
                }
                // Completion
                else if (header is "completion" or "abschluss" or "completion_text" or "reward" or "belohnung")
                {
                    map.CompletionIndex = i;
                }
                // Description
                else if (header is "description" or "beschreibung" or "desc" or "quest_description")
                {
                    map.DescriptionIndex = i;
                }
                // Title
                else if (header is "title" or "titel" or "name" or "quest_title")
                {
                    map.TitleIndex = i;
                }
            }

            return map;
        }

        /// <summary>
        /// Parst eine CSV-Zeile unter Berücksichtigung von Anführungszeichen.
        /// </summary>
        private static string[] ParseCsvLine(string line, char delimiter)
        {
            var values = new List<string>();
            var current = new StringBuilder();
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        // Escaped quote
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == delimiter && !inQuotes)
                {
                    values.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            values.Add(current.ToString());
            return [.. values];
        }

        /// <summary>
        /// Bereinigt einen CSV-Wert (entfernt umschließende Anführungszeichen, etc.).
        /// </summary>
        private static string CleanCsvValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            value = value.Trim();

            // Umschließende Anführungszeichen entfernen
            if (value.StartsWith('"') && value.EndsWith('"'))
            {
                value = value[1..^1];
            }

            // Doppelte Anführungszeichen durch einfache ersetzen
            value = value.Replace("\"\"", "\"");

            // Zeilenumbrüche normalisieren
            value = value.Replace("\\n", "\n").Replace("\\r", "");

            return value.Trim();
        }

        private class ColumnMap
        {
            public int QuestIdIndex { get; set; } = -1;
            public int ObjectivesIndex { get; set; } = -1;
            public int CompletionIndex { get; set; } = -1;
            public int DescriptionIndex { get; set; } = -1;
            public int TitleIndex { get; set; } = -1;
        }
    }
}
