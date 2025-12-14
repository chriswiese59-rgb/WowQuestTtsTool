using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using WowQuestTtsTool.Services;
using WowQuestTtsTool.Services.TtsEngines;

namespace WowQuestTtsTool
{
    /// <summary>
    /// ViewModel fuer eine Zeile in der Usage-Tabelle.
    /// </summary>
    public class UsageRowViewModel
    {
        public string EngineName { get; set; } = "";
        public string EngineId { get; set; } = "";
        public long TotalCharacters { get; set; }
        public long TotalTokens { get; set; }
        public int TotalRequests { get; set; }
        public string EstimatedCost { get; set; } = "";
    }

    /// <summary>
    /// Fenster zur Anzeige der TTS-Nutzungsstatistiken.
    /// </summary>
    public partial class TtsUsageWindow : Window
    {
        private readonly TtsUsageTracker _tracker;
        private readonly TtsExportSettings _exportSettings;

        public TtsUsageWindow()
        {
            InitializeComponent();
            _tracker = TtsUsageTracker.Instance;
            _exportSettings = TtsExportSettings.Instance;
            LoadUsageData();
        }

        /// <summary>
        /// Laedt die Nutzungsdaten und zeigt sie an.
        /// </summary>
        private void LoadUsageData()
        {
            var rows = new List<UsageRowViewModel>();

            // Engine-Namen Mapping
            var engineNames = new Dictionary<string, string>
            {
                { "OpenAI", "OpenAI TTS" },
                { "Gemini", "Google Gemini" },
                { "Claude", "Anthropic Claude" },
                { "External", "ElevenLabs" }
            };

            var sessionStats = _tracker.GetSessionStats();

            foreach (var kvp in sessionStats)
            {
                var engineId = kvp.Key;
                var stats = kvp.Value;

                // Nur Engines mit Nutzung anzeigen
                if (stats.TotalRequests == 0) continue;

                var engineName = engineNames.TryGetValue(engineId, out var name) ? name : engineId;
                var cost = CalculateCost(stats.TotalCharacters, engineId);

                rows.Add(new UsageRowViewModel
                {
                    EngineId = engineId,
                    EngineName = engineName,
                    TotalCharacters = stats.TotalCharacters,
                    TotalTokens = stats.TotalTokensEstimate,
                    TotalRequests = stats.TotalRequests,
                    EstimatedCost = FormatCost(cost)
                });
            }

            // Falls keine Daten vorhanden, zeige Platzhalter
            if (rows.Count == 0)
            {
                rows.Add(new UsageRowViewModel
                {
                    EngineName = "(Keine Nutzung in dieser Session)",
                    TotalCharacters = 0,
                    TotalTokens = 0,
                    TotalRequests = 0,
                    EstimatedCost = "-"
                });
            }

            UsageDataGrid.ItemsSource = rows;

            // Session-Info
            SessionInfoText.Text = $"Session gestartet: {_tracker.SessionStartTime:dd.MM.yyyy HH:mm:ss}";

            // Gesamtsumme berechnen
            var totalChars = rows.Sum(r => r.TotalCharacters);
            var totalTokens = rows.Sum(r => r.TotalTokens);
            var totalRequests = rows.Sum(r => r.TotalRequests);
            var totalCost = CalculateTotalCost(sessionStats);

            TotalCharsText.Text = $"{totalChars:N0} Zeichen";
            TotalTokensText.Text = $"~{totalTokens:N0} Tokens";
            TotalRequestsText.Text = $"{totalRequests:N0} Requests";
            TotalCostText.Text = FormatCost(totalCost);
        }

        /// <summary>
        /// Berechnet die Kosten fuer eine Engine basierend auf Zeichen.
        /// </summary>
        private decimal CalculateCost(long characters, string engineId)
        {
            // Kosten pro 1k Zeichen je nach Engine
            // ElevenLabs: ca. $0.18 pro 1000 Zeichen
            // OpenAI TTS: ca. $0.015 pro 1000 Zeichen
            var costPer1kChars = engineId switch
            {
                "External" => 0.18m, // ElevenLabs
                "OpenAI" => 0.015m,  // OpenAI TTS
                "Gemini" => 0.001m,  // Gemini (Platzhalter)
                "Claude" => 0.001m,  // Claude (Platzhalter)
                _ => _exportSettings.CostPer1kTokens / 1000m * (decimal)_exportSettings.AvgCharsPerToken
            };

            return characters / 1000m * costPer1kChars;
        }

        /// <summary>
        /// Berechnet die Gesamtkosten.
        /// </summary>
        private decimal CalculateTotalCost(Dictionary<string, TtsUsageStats> stats)
        {
            decimal total = 0;
            foreach (var kvp in stats)
            {
                total += CalculateCost(kvp.Value.TotalCharacters, kvp.Key);
            }
            return total;
        }

        /// <summary>
        /// Formatiert Kosten mit Waehrungssymbol.
        /// </summary>
        private string FormatCost(decimal cost)
        {
            var symbol = _exportSettings.CurrencySymbol ?? "EUR";
            return $"~{cost:F2} {symbol}";
        }

        /// <summary>
        /// Setzt die Session-Statistiken zurueck.
        /// </summary>
        private void OnResetSessionClick(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Moechtest du die Session-Statistiken wirklich zuruecksetzen?\n\nDie Daten dieser Session werden verworfen.",
                "Session zuruecksetzen",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _tracker.ResetSession();
                LoadUsageData();
                MessageBox.Show("Session-Statistiken wurden zurueckgesetzt.", "Erledigt",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Exportiert die Nutzungsdaten als CSV.
        /// </summary>
        private void OnExportCsvClick(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV-Dateien (*.csv)|*.csv|Alle Dateien (*.*)|*.*",
                DefaultExt = ".csv",
                FileName = $"tts_usage_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var csv = _tracker.ExportToCsv();
                    File.WriteAllText(dialog.FileName, csv, Encoding.UTF8);
                    MessageBox.Show($"Nutzungsdaten wurden exportiert:\n{dialog.FileName}",
                        "Export erfolgreich", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Fehler beim Export:\n{ex.Message}",
                        "Fehler", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// Schliesst das Fenster.
        /// </summary>
        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
