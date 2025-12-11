using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WowQuestTtsTool.Services
{
    /// <summary>
    /// Hilfsklasse fuer die Erzeugung sicherer Ordner- und Dateinamen
    /// sowie vollstaendiger Pfade fuer TTS-Audio-Dateien.
    ///
    /// NEUE Ordnerstruktur (v2):
    /// {RootFolder}/{Language}/{ZoneFolder}/{Category}/Quest_{ID}_{Category}_{Gender}_{ShortTitle}.mp3
    ///
    /// Wobei Category = "Main" | "Side" | "Group"
    ///
    /// Beispiel:
    /// C:/TtsOutput/deDE/ElwynnForest/Main/Quest_176_Main_male_GESUCHT_Hogger.mp3
    /// </summary>
    public static class AudioPathHelper
    {
        // Kompilierte Regex fuer bessere Performance
#pragma warning disable SYSLIB1045 // GeneratedRegex nicht verwendbar in statischer Klasse ohne partial
        private static readonly Regex s_invalidFolderCharsRegex = new(@"[^a-zA-Z0-9_\-]", RegexOptions.Compiled);
        private static readonly Regex s_multipleUnderscoresRegex = new(@"_+", RegexOptions.Compiled);
        private static readonly Regex s_whitespaceRegex = new(@"\s+", RegexOptions.Compiled);
#pragma warning restore SYSLIB1045

        /// <summary>
        /// Erzeugt den vollstaendigen Pfad fuer eine Audio-Datei mit der NEUEN Struktur.
        /// Struktur: {Root}/{Language}/{Zone}/{Category}/Quest_{ID}_{Category}_{Gender}_{ShortTitle}.mp3
        /// </summary>
        /// <param name="rootFolder">Basis-Ausgabeordner (z.B. TtsOutputRootFolder)</param>
        /// <param name="language">Sprachcode (z.B. "deDE")</param>
        /// <param name="quest">Quest-Objekt</param>
        /// <param name="genderCode">Stimm-Gender ("male", "female", "neutral")</param>
        /// <param name="extension">Dateiendung (Standard: ".mp3")</param>
        /// <returns>Vollstaendiger Dateipfad</returns>
        public static string GetAudioFilePath(
            string rootFolder,
            string language,
            Quest quest,
            string genderCode,
            string extension = ".mp3")
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(rootFolder, nameof(rootFolder));
            ArgumentNullException.ThrowIfNull(quest, nameof(quest));

            // Defaults
            if (string.IsNullOrWhiteSpace(language))
                language = "deDE";

            if (string.IsNullOrWhiteSpace(genderCode))
                genderCode = "neutral";

            genderCode = genderCode.ToLowerInvariant();

            // Extension normalisieren
            extension = NormalizeExtension(extension);

            // Zone-Ordner
            var zoneName = string.IsNullOrWhiteSpace(quest.Zone) ? "UnknownZone" : quest.Zone;
            var safeZoneFolder = MakeSafeFolderName(zoneName);

            // Kategorie-Ordner: Main / Side / Group (basierend auf Quest-Klassifizierung)
            var categoryFolder = QuestClassificationService.GetAudioFolderCategory(quest);

            // Quest-ID
            var questIdStr = quest.QuestId > 0 ? quest.QuestId.ToString() : "Unknown";

            // Kurztitel fuer Dateiname (max 40 Zeichen, sicher)
            var shortTitle = MakeSafeFileName(quest.Title, 40);

            // NEUER Dateiname: Quest_{ID}_{Category}_{gender}_{ShortTitle}.mp3
            var fileName = $"Quest_{questIdStr}_{categoryFolder}_{genderCode}_{shortTitle}{extension}";

            // Vollstaendiger Pfad
            var fullPath = Path.Combine(
                rootFolder,
                MakeSafeFolderName(language),
                safeZoneFolder,
                categoryFolder,
                fileName);

            // Ordner erstellen falls nicht vorhanden
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            return fullPath;
        }

        /// <summary>
        /// Erzeugt den Pfad fuer maennliche Stimme mit neuer Struktur.
        /// </summary>
        public static string GetMaleAudioPath(string rootFolder, string language, Quest quest)
        {
            return GetAudioFilePath(rootFolder, language, quest, "male");
        }

        /// <summary>
        /// Erzeugt den Pfad fuer weibliche Stimme mit neuer Struktur.
        /// </summary>
        public static string GetFemaleAudioPath(string rootFolder, string language, Quest quest)
        {
            return GetAudioFilePath(rootFolder, language, quest, "female");
        }

        /// <summary>
        /// Erzeugt einen sicheren Ordnernamen aus einem beliebigen String.
        /// - Umlaute werden ersetzt (ae, oe, ue, ss)
        /// - Nur Buchstaben, Ziffern, _ und - erlaubt
        /// - Leerzeichen werden zu _
        /// - Maximale Laenge wird begrenzt
        /// </summary>
        public static string MakeSafeFolderName(string input, int maxLength = 64)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "Unknown";

            var result = ReplaceUmlauts(input);

            // Leerzeichen zu Unterstrich
            result = result.Replace(' ', '_');

            // Nur erlaubte Zeichen behalten: Buchstaben, Ziffern, _, -
            result = s_invalidFolderCharsRegex.Replace(result, "");

            // Mehrfache Unterstriche reduzieren
            result = s_multipleUnderscoresRegex.Replace(result, "_");

            // Fuehrende/folgende Unterstriche entfernen
            result = result.Trim('_', '-');

            // Laenge begrenzen
            if (result.Length > maxLength)
                result = result[..maxLength].TrimEnd('_', '-');

            // Falls leer nach Bereinigung
            if (string.IsNullOrWhiteSpace(result))
                return "Unknown";

            return result;
        }

        /// <summary>
        /// Erzeugt einen sicheren Dateinamen aus einem beliebigen String.
        /// - Umlaute werden ersetzt
        /// - Unerlaubte Dateinamenzeichen werden entfernt
        /// - Text wird in GROSSBUCHSTABEN konvertiert
        /// - Maximale Laenge wird begrenzt
        /// </summary>
        public static string MakeSafeFileName(string? input, int maxLength = 40)
        {
            if (string.IsNullOrWhiteSpace(input))
                return "UNKNOWN";

            var result = ReplaceUmlauts(input);

            // In Grossbuchstaben (fuer bessere Lesbarkeit im Dateisystem)
            result = result.ToUpperInvariant();

            // Unerlaubte Zeichen fuer Dateinamen entfernen
            foreach (var c in Path.GetInvalidFileNameChars())
            {
                result = result.Replace(c, '_');
            }

            // Leerzeichen zu Unterstrich
            result = s_whitespaceRegex.Replace(result, "_");

            // Mehrfache Unterstriche reduzieren
            result = s_multipleUnderscoresRegex.Replace(result, "_");

            // Fuehrende/folgende Unterstriche entfernen
            result = result.Trim('_', '-', '.');

            // Laenge begrenzen
            if (result.Length > maxLength)
                result = result[..maxLength].TrimEnd('_', '-', '.');

            // Falls leer nach Bereinigung
            if (string.IsNullOrWhiteSpace(result))
                return "UNKNOWN";

            return result;
        }

        /// <summary>
        /// Ersetzt deutsche Umlaute durch ASCII-Aequivalente.
        /// </summary>
        public static string ReplaceUmlauts(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            var sb = new StringBuilder(input);
            sb.Replace("ä", "ae");
            sb.Replace("ö", "oe");
            sb.Replace("ü", "ue");
            sb.Replace("Ä", "Ae");
            sb.Replace("Ö", "Oe");
            sb.Replace("Ü", "Ue");
            sb.Replace("ß", "ss");
            return sb.ToString();
        }

        /// <summary>
        /// Normalisiert eine Dateiendung (fuegt Punkt hinzu, macht klein).
        /// </summary>
        private static string NormalizeExtension(string ext)
        {
            if (string.IsNullOrWhiteSpace(ext))
                return ".mp3";

            ext = ext.Trim().ToLowerInvariant();

            if (!ext.StartsWith('.'))
                ext = "." + ext;

            return ext;
        }

        /// <summary>
        /// Berechnet den relativen Pfad von fullPath relativ zu rootFolder.
        /// Verwendet immer / als Separator (fuer WoW-Addon Kompatibilitaet).
        /// </summary>
        public static string GetRelativePath(string rootFolder, string fullPath)
        {
            if (string.IsNullOrWhiteSpace(rootFolder) || string.IsNullOrWhiteSpace(fullPath))
                return Path.GetFileName(fullPath) ?? "unknown.mp3";

            // Pfade normalisieren
            var normalizedRoot = Path.GetFullPath(rootFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedFull = Path.GetFullPath(fullPath);

            // Pruefen ob fullPath unterhalb von rootFolder liegt
            if (normalizedFull.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = normalizedFull[normalizedRoot.Length..]
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Separator auf / normalisieren (fuer WoW-Addon)
                return relativePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            }

            // Fallback: nur Dateiname
            return Path.GetFileName(fullPath) ?? "unknown.mp3";
        }

        /// <summary>
        /// Extrahiert die Quest-ID aus einem Dateinamen im neuen Format.
        /// Format: Quest_{ID}_{Category}_{Gender}_{ShortTitle}.mp3
        /// </summary>
        public static int? ExtractQuestIdFromFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            // Dateiname ohne Pfad und Extension
            var name = Path.GetFileNameWithoutExtension(fileName);

            // Format: Quest_123_Main_male_TITLE
            if (!name.StartsWith("Quest_", StringComparison.OrdinalIgnoreCase))
                return null;

            var parts = name.Split('_');
            if (parts.Length >= 2 && int.TryParse(parts[1], out var questId))
            {
                return questId;
            }

            return null;
        }

        /// <summary>
        /// Erzeugt die Info fuer einen QuestAudioIndexEntry aus dem Pfad.
        /// </summary>
        public static (string Zone, string Category, string Gender) ParsePathInfo(string relativePath)
        {
            // Format: deDE/ElwynnForest/Main/Quest_123_Main_male_TITLE.mp3
            var parts = relativePath.Replace('\\', '/').Split('/');

            string zone = "Unknown";
            string category = "Side";
            string gender = "neutral";

            if (parts.Length >= 4)
            {
                // parts[0] = language (deDE)
                // parts[1] = zone
                // parts[2] = category (Main/Side/Group)
                // parts[3] = filename
                zone = parts[1];
                category = parts[2];

                // Gender aus Dateinamen extrahieren
                var fileName = Path.GetFileNameWithoutExtension(parts[3]);
                var fileParts = fileName.Split('_');
                if (fileParts.Length >= 4)
                {
                    gender = fileParts[3].ToLowerInvariant();
                }
            }

            return (zone, category, gender);
        }
    }
}
