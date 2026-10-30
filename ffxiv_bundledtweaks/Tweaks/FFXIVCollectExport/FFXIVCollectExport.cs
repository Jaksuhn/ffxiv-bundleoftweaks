using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using System.Text.Json;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public partial class FFXIVCollectExport : Tweak {
    public override string Name => "FFXIV Collect Export";
    public override string Description => "See your FFXIVCollect progress and import (most) manually tracked categories.";

    private sealed record Category(string Endpoint, string Label, bool Manual) {
        public string Key => Endpoint[(Endpoint.LastIndexOf('/') + 1)..]; // profile + import key, triad/cards -> cards
    }

    private static readonly Category[] Categories = [
        new("achievements", "Achievements", false),
        new("mounts", "Mounts", false),
        new("minions", "Minions", false),
        new("emotes", "Emotes", false),
        new("facewear", "Facewear", false),
        new("orchestrions", "Orchestrion Rolls", true),
        new("hairstyles", "Hairstyles", true),
        new("bardings", "Bardings", true),
        new("fashions", "Fashion Accessories", true),
        new("outfits", "Outfits", true),
        new("armoires", "Armoire", true),
        new("frames", "Framer's Kits", true),
        new("spells", "Blue Magic", true),
        new("beasts", "Beastmaster Beasts", true),
        new("triad/cards", "Triple Triad Cards", true),
        new("triad/npcs", "Triple Triad NPCs", true),
        new("survey_records", "Survey Records", true),
        new("field_records", "Field Records", true),
        new("occult_records", "Occult Records", true),
        new("leves", "Leves", true),
        new("relics", "Relics", true),
    ];

    private sealed class CategoryStatus {
        public string? Unavailable;
        public int InGame;
        public List<KnownItem> Missing = []; // unlocked in game, not on ffxiv collect
        public int OnlyOnCollect; // on ffxiv collect, not found in game
    }

    private sealed class SyncResult {
        public required CollectProfile Profile;
        public Dictionary<string, CategoryStatus> Statuses = [];
    }

    private readonly Dictionary<ulong, ulong> _lodestoneIds = []; // content id -> lodestone id
    private string? _syncError;

    private SyncResult? CurrentSync { get => field is { } s && s.Profile.ContentId == IPlayerState.Get().ContentId ? s : null; set; }

    protected override void DrawCustomConfig() {
        DrawProfile();
        DrawExport();
    }

    private void DrawProfile() {
        ImGui.DrawSection("FFXIV Collect Profile");
        if (!IClientState.Get().IsLoggedIn) {
            ImGui.TextColoredWrapped(Colors.Grey, "Log in to see your collection progress.");
            return;
        }

        using (ImRaii.Disabled(Automation.Running)) {
            if (ImGui.Button("Refresh"))
                Automation.Start(new CollectTask(this, sync: true, copy: false));
        }
        if (_lodestoneIds.TryGetValue(IPlayerState.Get().ContentId, out var lodestoneId)) {
            ImGui.SameLine();
            if (ImGui.Button("Open Profile"))
                Util.OpenLink($"https://ffxivcollect.com/characters/{lodestoneId}");
        }

        if (Automation.Running)
            ImGui.TextColoredWrapped(Colors.Grey, Automation.Status);
        else if (_syncError != null)
            ImGui.TextColoredWrapped(Colors.Invalid, _syncError);

        if (CurrentSync is not { } sync) {
            if (!Automation.Running)
                ImGui.TextColoredWrapped(Colors.Grey, "Refresh to compare FFXIV Collect against your in-game unlocks.");
            return;
        }

        var profile = sync.Profile;
        ImGui.TextUnformatted($"{profile.Name} @ {profile.Server}");
        if (!profile.Verified) {
            ImGui.SameLine();
            ImGui.TextColored(Colors.Grey, "(unverified)");
        }
        ImGui.TextColoredWrapped(Colors.Grey, $"Lodestone data last read by FFXIV Collect {Ago(profile.LastParsed)}. Fetched {Ago(profile.Fetched)}.");

        DrawTable(sync);
    }

    private static void DrawTable(SyncResult sync) {
        using var table = ImRaii.Table("Collections", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV);
        if (!table)
            return;

        ImGui.TableSetupColumn("Collection", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("FFXIV Collect", ImGuiTableColumnFlags.WidthFixed, 160 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("In Game");
        ImGui.TableSetupColumn("Status");
        ImGui.TableHeadersRow();

        foreach (var category in Categories) {
            if (!sync.Profile.Categories.TryGetValue(category.Key, out var collect))
                continue;
            sync.Statuses.TryGetValue(category.Key, out var status);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(category.Label);
            if (collect.Breakdown.Count > 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip(string.Join('\n', collect.Breakdown.Select(b => $"{Capitalise(b.Name)}: {b.Count}/{b.Total}")));

            ImGui.TableNextColumn();
            var fraction = collect.Total > 0 ? (float)collect.Count / collect.Total : 0;
            ImGui.ProgressBar(fraction, new Vector2(-1, 0), $"{collect.Count}/{collect.Total}");

            ImGui.TableNextColumn();
            if (!category.Manual)
                ImGui.TextColored(Colors.Grey, "-");
            else if (status?.Unavailable != null)
                ImGui.TextColored(Colors.Grey, "?");
            else if (status != null)
                ImGui.TextUnformatted(status.InGame.ToString());

            ImGui.TableNextColumn();
            DrawStatus(category, collect, status);
        }
    }

    private static void DrawStatus(Category category, CollectCategory collect, CategoryStatus? status) {
        if (!collect.Public) {
            ImGui.TextColored(Colors.Invalid, "Private");
            ImGui.TooltipOnHover("This collection is private on FFXIV Collect, so its progress isn't shown.");
            return;
        }

        if (!category.Manual) {
            ImGui.TextColored(Colors.Grey, "Lodestone");
            ImGui.TooltipOnHover("Fetched automatically by FFXIV Collect. No need to import.");
            return;
        }

        if (status == null)
            return;

        if (status.Unavailable != null) {
            ImGui.TextColored(Colors.Grey, "Not checked");
            ImGui.TooltipOnHover(status.Unavailable);
            return;
        }

        if (status.Missing.Count > 0)
            ImGui.TextColored(ImGuiColors.DalamudYellow, $"+{status.Missing.Count} to import");
        else
            ImGui.TextColored(Colors.Valid, "Up to date");

        if (status.OnlyOnCollect > 0)
            ImGui.TooltipOnHover($"{status.OnlyOnCollect} marked on FFXIV Collect weren't found in game. Importing will remove them.");
    }

    private void DrawExport() {
        ImGui.DrawSection("Export");

        using (ImRaii.Disabled(!IClientState.Get().IsLoggedIn || Automation.Running)) {
            if (ImGui.Button("Copy to Clipboard"))
                Automation.Start(new CollectTask(this, sync: false, copy: true));
        }

        ImGui.SameLine();
        if (ImGui.Button("Open Import Page"))
            Util.OpenLink("https://ffxivcollect.com/import");
    }

    private static string Capitalise(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Ago(DateTime? time) {
        if (time is not { } t)
            return "never";
        var span = DateTime.UtcNow - t;
        return span.TotalMinutes < 1 ? "just now"
            : span.TotalHours < 1 ? $"{(int)span.TotalMinutes}m ago"
            : span.TotalDays < 1 ? $"{(int)span.TotalHours}h ago"
            : $"{(int)span.TotalDays}d ago";
    }

    private sealed class CollectTask(FFXIVCollectExport tweak, bool sync, bool copy) : AutoTask {
        protected override async Task Execute() {
            using var scope = BeginScope(nameof(CollectTask));

            if (!IClientState.Get().IsLoggedIn) {
                tweak.ModuleMessage("Log in first.");
                return;
            }

            var live = CollectLive();

            if (sync) {
                tweak._syncError = null;
                try {
                    tweak.CurrentSync = await Sync(live);
                }
                catch (CollectApiException ex) {
                    tweak._syncError = ex.Message;
                }
                catch (Exception ex) when (ex is not OperationCanceledException) {
                    tweak.Error(ex, "Failed to sync with FFXIV Collect");
                    tweak._syncError = $"Couldn't reach FFXIV Collect: {ex.Message}";
                }
            }

            if (copy) {
                var payload = BuildPayload(live);
                ImGui.SetClipboardText(JsonSerializer.Serialize(payload, new JsonSerializerOptions() { WriteIndented = true }));
                var total = payload.Values.Sum(v => v.Count);
                var skipped = live.Unavailable.Count > 0 ? $" Left out: {string.Join(", ", live.Unavailable.Keys.Select(Label))}." : "";
                Log($"Copied {total} owned IDs across {payload.Count} collections to the clipboard.{skipped}");
            }
        }

        private async Task<SyncResult> Sync(Snapshot live) {
            var player = IPlayerState.Get();
            if (!tweak._lodestoneIds.TryGetValue(live.ContentId, out var lodestoneId)) {
                Status = "Searching the Lodestone";
                var name = player.CharacterName;
                var world = player.HomeWorld.Value.Name.ToString();
                lodestoneId = await CollectApi.FindLodestoneId(name, world) ?? throw new CollectApiException($"Couldn't find {name} @ {world} on the Lodestone.");
                tweak._lodestoneIds[live.ContentId] = lodestoneId;
            }

            Status = "Fetching FFXIV Collect profile";
            var profile = await CollectApi.GetProfile(lodestoneId, CancelToken);
            profile.ContentId = live.ContentId;
            var result = new SyncResult { Profile = profile };

            foreach (var category in Categories.Where(c => c.Manual)) {
                if (!profile.Categories.TryGetValue(category.Key, out var collect))
                    continue;

                var status = result.Statuses[category.Key] = new CategoryStatus();
                if (live.Unavailable.TryGetValue(category.Key, out var reason) || !live.Ids.TryGetValue(category.Key, out var ids)) {
                    status.Unavailable = reason ?? "Not readable in game.";
                    continue;
                }

                Status = $"Checking {category.Label}";
                var candidates = ids.Except(collect.Ids).ToList();
                status.Missing = candidates.Count > 0 ? await CollectApi.GetKnown(category.Endpoint, candidates, CancelToken) : [];
                status.InGame = ids.Count(collect.Ids.Contains) + status.Missing.Count;
                status.OnlyOnCollect = collect.Ids.Count(id => !ids.Contains(id));
            }

            return result;
        }

        private static Dictionary<string, List<uint>> BuildPayload(Snapshot live) => live.Ids.Where(kv => !live.Unavailable.ContainsKey(kv.Key)).OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key, kv => kv.Value.Order().ToList());
        private static string Label(string key) => Categories.FirstOrDefault(c => c.Key == key)?.Label ?? key;
    }
}
