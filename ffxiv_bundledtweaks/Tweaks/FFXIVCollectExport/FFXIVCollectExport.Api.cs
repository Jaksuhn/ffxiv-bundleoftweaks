using NetStone;
using NetStone.Search.Character;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public partial class FFXIVCollectExport {
    private sealed class CollectCategory {
        public int Count;
        public int Total;
        public bool Public = true;
        public HashSet<uint> Ids = [];
        public List<(string Name, int Count, int Total)> Breakdown = [];
    }

    private sealed class CollectProfile {
        public ulong LodestoneId;
        public ulong ContentId;
        public string Name = "";
        public string Server = "";
        public bool Verified;
        public DateTime? LastParsed;
        public DateTime Fetched;
        public Dictionary<string, CollectCategory> Categories = [];
    }

    private sealed record KnownItem(uint Id, string Name);

    private sealed class CollectApiException(string message) : Exception(message);

    private static class CollectApi {
        private const string BaseUrl = "https://ffxivcollect.com/api";
        private const int MaxIdsPerRequest = 150;

        private static readonly HttpClient Http = CreateClient();
        private static LodestoneClient? _lodestone;

        public static async Task<ulong?> FindLodestoneId(string name, string world) {
            _lodestone ??= await LodestoneClient.GetClientAsync();
            var response = await _lodestone.SearchCharacter(new CharacterSearchQuery { CharacterName = name, World = world });
            var match = response?.Results.FirstOrDefault(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));
            return ulong.TryParse(match?.Id, out var id) ? id : null;
        }

        public static async Task<CollectProfile> GetProfile(ulong lodestoneId, CancellationToken cancel) {
            using var document = await GetJson($"{BaseUrl}/characters/{lodestoneId}?ids=1", cancel);
            var root = document.RootElement;
            var profile = new CollectProfile {
                LodestoneId = lodestoneId,
                Name = root.GetProperty("name").GetString() ?? "",
                Server = root.GetProperty("server").GetString() ?? "",
                Verified = root.TryGetProperty("verified", out var verified) && verified.ValueKind == JsonValueKind.True,
                LastParsed = root.TryGetProperty("last_parsed", out var parsed) && parsed.TryGetDateTime(out var time) ? time.ToUniversalTime() : null,
                Fetched = DateTime.UtcNow,
            };

            foreach (var category in Categories) {
                if (!root.TryGetProperty(category.Key, out var element) || element.ValueKind != JsonValueKind.Object)
                    continue;

                var data = new CollectCategory {
                    Count = element.TryGetProperty("count", out var count) ? count.GetInt32() : 0,
                    Total = element.TryGetProperty("total", out var total) ? total.GetInt32() : 0,
                    Public = !element.TryGetProperty("public", out var isPublic) || isPublic.ValueKind != JsonValueKind.False,
                };
                if (element.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    data.Ids = [.. ids.EnumerateArray().Select(id => id.GetUInt32())];

                // leves and relics have per-type counts alongside the totals
                foreach (var sub in element.EnumerateObject())
                    if (sub.Value.ValueKind == JsonValueKind.Object && sub.Value.TryGetProperty("count", out var subCount) && sub.Value.TryGetProperty("total", out var subTotal))
                        data.Breakdown.Add((sub.Name, subCount.GetInt32(), subTotal.GetInt32()));

                profile.Categories[category.Key] = data;
            }

            return profile;
        }

        // returns which of the given ids ffxiv collect has in the collection, along with names for display
        public static async Task<List<KnownItem>> GetKnown(string endpoint, IEnumerable<uint> ids, CancellationToken cancel) {
            var result = new List<KnownItem>();
            foreach (var chunk in ids.Order().Chunk(MaxIdsPerRequest)) {
                using var document = await GetJson($"{BaseUrl}/{endpoint}?id_in={string.Join(',', chunk)}", cancel);
                if (!document.RootElement.TryGetProperty("results", out var results))
                    continue;
                foreach (var item in results.EnumerateArray()) {
                    var name = item.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    result.Add(new(item.GetProperty("id").GetUInt32(), name));
                }
            }
            return result;
        }

        private static async Task<JsonDocument> GetJson(string url, CancellationToken cancel) {
            using var response = await Http.GetAsync(url, cancel);
            switch (response.StatusCode) {
                case HttpStatusCode.NotFound:
                    throw new CollectApiException("Character isn't on FFXIV Collect yet. Open the profile link to add it, then refresh.");
                case HttpStatusCode.Forbidden:
                    throw new CollectApiException("Character is set to private.");
            }
            response.EnsureSuccessStatusCode();
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        }

        private static HttpClient CreateClient() {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CBT");
            return client;
        }
    }
}
