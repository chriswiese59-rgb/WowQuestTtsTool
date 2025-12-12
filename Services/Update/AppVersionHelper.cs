using System;
using System.Reflection;

namespace WowQuestTtsTool.Services.Update
{
    /// <summary>
    /// Hilfsklasse zur Ermittlung der aktuellen Anwendungsversion.
    /// </summary>
    public static class AppVersionHelper
    {
        // Fallback-Version, falls keine Assembly-Version gefunden wird
        private static readonly Version DefaultVersion = new(1, 0, 0);

        /// <summary>
        /// Gibt die aktuelle Version der Anwendung zurück.
        /// Liest die AssemblyVersion aus der ausführenden Assembly.
        /// </summary>
        public static Version GetCurrentVersion()
        {
            try
            {
                // Versuche AssemblyVersion zu lesen
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;

                if (version != null)
                {
                    return version;
                }

                // Fallback: Versuche AssemblyFileVersion
                var fileVersionAttr = assembly.GetCustomAttribute<AssemblyFileVersionAttribute>();
                if (fileVersionAttr != null && Version.TryParse(fileVersionAttr.Version, out var fileVersion))
                {
                    return fileVersion;
                }

                // Fallback: Versuche AssemblyInformationalVersion
                var infoVersionAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (infoVersionAttr != null)
                {
                    // Informational Version kann Suffix haben (z.B. "1.2.3-beta")
                    var versionString = infoVersionAttr.InformationalVersion;
                    var dashIndex = versionString.IndexOf('-');
                    if (dashIndex > 0)
                    {
                        versionString = versionString[..dashIndex];
                    }
                    var plusIndex = versionString.IndexOf('+');
                    if (plusIndex > 0)
                    {
                        versionString = versionString[..plusIndex];
                    }

                    if (Version.TryParse(versionString, out var infoVersion))
                    {
                        return infoVersion;
                    }
                }
            }
            catch
            {
                // Bei Fehlern Fallback-Version verwenden
            }

            return DefaultVersion;
        }

        /// <summary>
        /// Gibt die aktuelle Version als formatierte Zeichenkette zurück.
        /// </summary>
        /// <param name="fieldCount">Anzahl der anzuzeigenden Versionsteile (2-4).</param>
        public static string GetCurrentVersionString(int fieldCount = 3)
        {
            var version = GetCurrentVersion();
            return version.ToString(Math.Min(fieldCount, 4));
        }

        /// <summary>
        /// Vergleicht zwei Versionen.
        /// </summary>
        /// <returns>
        /// -1: current ist älter als latest
        ///  0: Versionen sind gleich
        ///  1: current ist neuer als latest
        /// </returns>
        public static int CompareVersions(Version current, Version latest)
        {
            return current.CompareTo(latest);
        }

        /// <summary>
        /// Prüft, ob ein Update verfügbar ist.
        /// </summary>
        public static bool IsUpdateAvailable(Version latestVersion)
        {
            var current = GetCurrentVersion();
            return latestVersion > current;
        }

        /// <summary>
        /// Prüft, ob ein Update verfügbar ist (mit String-Version).
        /// </summary>
        public static bool IsUpdateAvailable(string latestVersionString)
        {
            if (Version.TryParse(latestVersionString, out var latestVersion))
            {
                return IsUpdateAvailable(latestVersion);
            }
            return false;
        }
    }
}
