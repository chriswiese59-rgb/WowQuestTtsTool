using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace WowQuestTtsTool.Services
{
    public class BlizzardQuestService
    {
        private readonly HttpClient _http;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _region;

        private string? _accessToken;
        private DateTime _tokenExpiresUtc;

        private string BaseUrl => $"https://{_region}.api.blizzard.com";
        private string TokenUrl => $"https://{_region}.battle.net/oauth/token";

        public BlizzardQuestService(HttpClient http, string clientId, string clientSecret, string region = "eu")
        {
            _http = http;
            _clientId = clientId;
            _clientSecret = clientSecret;
            _region = region;
        }

        public bool IsConfigured => !string.IsNullOrEmpty(_clientId) && !string.IsNullOrEmpty(_clientSecret);

        public async Task<List<Quest>> FetchQuestsAsync(
            int maxQuests = 1000,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            var token = await GetAccessTokenAsync(ct);

            var allQuestIds = new HashSet<int>();

            progress?.Report("Lade Quest-Indizes...");
            await CollectIdsFromIndexAsync(token, "area", allQuestIds, progress, ct);
            await CollectIdsFromIndexAsync(token, "category", allQuestIds, progress, ct);
            await CollectIdsFromIndexAsync(token, "type", allQuestIds, progress, ct);

            var questIds = new List<int>(allQuestIds);
            questIds.Sort();

            if (questIds.Count > maxQuests)
                questIds = questIds.GetRange(0, maxQuests);

            progress?.Report($"Lade Details für {questIds.Count} Quests...");

            var result = new List<Quest>();
            int total = questIds.Count;
            int i = 0;

            foreach (var id in questIds)
            {
                ct.ThrowIfCancellationRequested();
                i++;

                var quest = await GetQuestAsync(token, id, ct);
                if (quest != null)
                {
                    result.Add(quest);
                }

                if (i % 25 == 0)
                    progress?.Report($"[{i}/{total}] {result.Count} Quests geladen...");

                await Task.Delay(100, ct); // Rate limiting
            }

            progress?.Report($"Fertig: {result.Count} Quests geladen.");
            return result;
        }

        private async Task<string> GetAccessTokenAsync(CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiresUtc)
                return _accessToken!;

            var req = new HttpRequestMessage(HttpMethod.Post, TokenUrl)
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials"
                })
            };

            var byteArray = Encoding.ASCII.GetBytes($"{_clientId}:{_clientSecret}");
            req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));

            var resp = await _http.SendAsync(req, ct);
            var content = await resp.Content.ReadAsStringAsync(ct);

            resp.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;

            _accessToken = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.TryGetProperty("expires_in", out var expEl) ? expEl.GetInt32() : 3600;

            _tokenExpiresUtc = DateTime.UtcNow.AddSeconds(expiresIn - 60);
            return _accessToken!;
        }

        private async Task CollectIdsFromIndexAsync(
            string token,
            string kind,
            HashSet<int> target,
            IProgress<string>? progress,
            CancellationToken ct)
        {
            var uri = $"{BaseUrl}/data/wow/quest/{kind}/index?namespace=static-{_region}&locale=de_DE";

            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                progress?.Report($"Index {kind}: Fehler {(int)resp.StatusCode}");
                return;
            }

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Try different property names based on kind
            string[] possibleProps = kind switch
            {
                "area" => new[] { "areas", "area" },
                "category" => new[] { "categories", "category" },
                "type" => new[] { "types", "type" },
                _ => new[] { kind + "s", kind }
            };

            JsonElement arr = default;
            foreach (var prop in possibleProps)
            {
                if (root.TryGetProperty(prop, out arr) && arr.ValueKind == JsonValueKind.Array)
                    break;
            }

            if (arr.ValueKind != JsonValueKind.Array)
            {
                progress?.Report($"Index {kind}: Keine Daten gefunden");
                return;
            }

            int count = 0;
            foreach (var el in arr.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                if (!el.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                    continue;

                int id = idEl.GetInt32();
                var detail = await GetDetailAsync(token, kind, id, ct);

                if (detail.TryGetProperty("quests", out var qArr) && qArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var q in qArr.EnumerateArray())
                    {
                        if (q.TryGetProperty("id", out var qIdEl) && qIdEl.ValueKind == JsonValueKind.Number)
                        {
                            target.Add(qIdEl.GetInt32());
                        }
                    }
                }

                count++;
                if (count % 10 == 0)
                    progress?.Report($"Index {kind}: {count} verarbeitet, {target.Count} Quest-IDs...");

                await Task.Delay(50, ct);
            }
        }

        private async Task<JsonElement> GetDetailAsync(string token, string kind, int id, CancellationToken ct)
        {
            var uri = $"{BaseUrl}/data/wow/quest/{kind}/{id}?namespace=static-{_region}&locale=de_DE";

            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return default;

            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }

        private async Task<Quest?> GetQuestAsync(string token, int questId, CancellationToken ct)
        {
            var uri = $"{BaseUrl}/data/wow/quest/{questId}?namespace=static-{_region}&locale=de_DE";

            var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var resp = await _http.SendAsync(req, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
                return null;

            using var doc = JsonDocument.Parse(body);
            var raw = doc.RootElement;

            return TransformQuest(raw);
        }

        private Quest? TransformQuest(JsonElement raw)
        {
            if (raw.ValueKind != JsonValueKind.Object)
                return null;

            int id = raw.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.Number
                ? idEl.GetInt32()
                : 0;

            string title = raw.TryGetProperty("title", out var tEl)
                ? (tEl.GetString() ?? "")
                : "";

            string description = raw.TryGetProperty("description", out var dEl)
                ? (dEl.GetString() ?? "")
                : "";

            string zone = "";
            if (raw.TryGetProperty("area", out var areaEl) &&
                areaEl.ValueKind == JsonValueKind.Object &&
                areaEl.TryGetProperty("name", out var nameEl))
            {
                zone = nameEl.GetString() ?? "";
            }

            string completion = BuildCompletionText(raw);

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(description))
                return null;

            return new Quest
            {
                QuestId = id,
                Title = title,
                Description = description,
                Zone = zone,
                Completion = completion,
                Objectives = "",
                IsMainStory = false
            };
        }

        private string BuildCompletionText(JsonElement raw)
        {
            if (!raw.TryGetProperty("rewards", out var rewardsEl) ||
                rewardsEl.ValueKind != JsonValueKind.Object)
                return "";

            var parts = new List<string>();

            if (rewardsEl.TryGetProperty("experience", out var xpEl) &&
                xpEl.ValueKind == JsonValueKind.Number)
            {
                int xp = xpEl.GetInt32();
                if (xp > 0)
                    parts.Add($"Erfahrung: {xp}");
            }

            if (rewardsEl.TryGetProperty("money", out var moneyEl) &&
                moneyEl.ValueKind == JsonValueKind.Object &&
                moneyEl.TryGetProperty("value", out var valEl))
            {
                var gold = valEl.GetInt64() / 10000;
                if (gold > 0)
                    parts.Add($"Gold: {gold}");
            }

            return string.Join(", ", parts);
        }
    }
}
