using Dalamud.Bindings.ImGui;
using Dalamud.Game.Chat;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using Lumina.Excel.Sheets;
using System.Data;
using System.Text;
using System.Text.RegularExpressions;
using static Dalamud.Game.Text.XivChatType;
using TerritoryIntendedUse = FFXIVClientStructs.FFXIV.Client.Enums.TerritoryIntendedUse;

namespace ComplexTweaks.Tweaks;

public class HuntRelayHelperConfiguration {
    public List<(XivChatType Channel, string Command, bool IsLocal, bool Enabled)> Channels =
    [
        (Ls1, "l1", true, false),
        (Ls2, "l2", true, false),
        (Ls3, "l3", true, false),
        (Ls4, "l4", true, false),
        (Ls5, "l5", true, false),
        (Ls6, "l6", true, false),
        (Ls7, "l7", true, false),
        (Ls8, "l8", true, false),
        (FreeCompany, "fc", true, false),
        (NoviceNetwork, "n", true, false),
        (CrossLinkShell1, "cwl1", false, false),
        (CrossLinkShell2, "cwl2", false, false),
        (CrossLinkShell3, "cwl3", false, false),
        (CrossLinkShell4, "cwl4", false, false),
        (CrossLinkShell5, "cwl5", false, false),
        (CrossLinkShell6, "cwl6", false, false),
        (CrossLinkShell7, "cwl7", false, false),
        (CrossLinkShell8, "cwl8", false, false),
    ];

    [BoolConfig] public bool OnlySendLocalHuntsToLocalChannels = true;
    [BoolConfig] public bool AssumeBlankWorldsAreLocal = false;
    [BoolConfig] public bool DontRepeatRelays = true;
    [BoolConfig] public bool OverrideMinionFlag = true;
    [BoolConfig] public bool AllowPartialWorldMatches = false;
    [BoolConfig] public bool RemoveWorldFromNNCallouts = true;
    [BoolConfig] public bool DryRun = false;
    [StringConfig] public string ChatMessagePattern = "[<world>] <type> -> <flag>";
    [EnumConfig] public HuntRelayHelper.Locality AssumedLocality = HuntRelayHelper.Locality.PlayerHomeWorld;

    public List<(HuntRelayHelper.RelayTypes RelayType, string TypeFormat, string TypeHeuristics)> Types =
    [
        (HuntRelayHelper.RelayTypes.SRank, "S Rank", @"s rank, rank s, /(?:^|\W)(?<!')[sS](?:$|\W)/"),
        (HuntRelayHelper.RelayTypes.Minions, "Minions", @"ssminion, /\bminions?\b/"),
        (HuntRelayHelper.RelayTypes.Train, "Train", @"train"),
        (HuntRelayHelper.RelayTypes.FATE, "FATE", @"boss, fate"),
    ];
}

[Tweak]
public class HuntRelayHelper : Tweak<HuntRelayHelperConfiguration> {
    public override string Name => "Hunt Relay Helper";
    public override string Description => "Appends a clickable icon to messages with a MapLinkPayload to relay them to other channels.";

    private DalamudLinkPayload RelayLinkPayload = null!;
    private readonly string InstanceHeuristics = @"\b(?:instance\s*(?<instanceNumber>\d+)|i(?<iNumber>\d+))\b";
    private RelayPayload? LastRelay;
    private readonly List<RelayPayload> _huntAlertsRelays = [];

    private record struct HuntAlertsMessage(string Message, string HuntType, string HuntKind, string HuntWorld,
        string CurrentWorldName, string CurrentRegionName, string HuntRegionName, string PostedTime,
        long PostedEpoch, string StartLocation, uint StartLocationAetheryteId, string StartZone, int Instance,
        string LocationCoords, bool OpenMapOnArrival, bool LifestreamEnabled);

    public override void Enable() {
        Svc.Chat.ChatMessage += OnChatMessage;
        RelayLinkPayload = Svc.Chat.AddChatLinkHandler((uint)LinkHandlerId.RelayLinkPayload, HandleRelayLink);
        Svc.Interface.GetIpcSubscriber<HuntAlertsMessage, object>("HuntAlerts.OnHuntTrainMessageReceived").Subscribe(OnHuntAlert);
    }

    public override void Disable() {
        Svc.Chat.ChatMessage -= OnChatMessage;
        Svc.Chat.RemoveChatLinkHandler((uint)LinkHandlerId.RelayLinkPayload);
        Svc.Interface.GetIpcSubscriber<HuntAlertsMessage, object>("HuntAlerts.OnHuntTrainMessageReceived").Unsubscribe(OnHuntAlert);
        _huntAlertsRelays.Clear();
    }

    public enum Locality {
        PlayerHomeWorld,
        PlayerCurrentWorld,
        SenderHomeWorld,
    }

    public enum RelayTypes {
        SRank,
        Minions,
        Train,
        FATE,

        None, // Keep this last
    }

    public override void DrawConfig() {
        ImGui.DrawSection("Chat Channels");
        using (ImRaii.Table($"##{nameof(Config.Channels)}", 4)) {
            foreach (var c in Config.Channels.ToList().Select((x, i) => new { Value = x, Index = i })) {
                var column = c.Index % 2 == 0 ? 0 : 2;
                if (column == 0)
                    ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(column);
                ImGui.TextUnformatted(Config.Channels[c.Index].Channel.ToString());

                ImGui.TableSetColumnIndex(column + 1);
                var tmpE = c.Value;
                if (ImGui.Checkbox($"##{c.Value.Channel}{nameof(c.Value.Enabled)}", ref tmpE.Enabled))
                    Config.Channels[c.Index] = (Config.Channels[c.Index].Channel, Config.Channels[c.Index].Command, Config.Channels[c.Index].IsLocal, tmpE.Enabled);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Enable sending to this channel");

                ImGui.SameLine();
                var tmpL = c.Value;
                if (ImGui.Checkbox($"##{c.Value.Channel}{nameof(c.Value.IsLocal)}", ref tmpL.IsLocal))
                    Config.Channels[c.Index] = (Config.Channels[c.Index].Channel, Config.Channels[c.Index].Command, tmpL.IsLocal, Config.Channels[c.Index].Enabled);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Mark channel as \"local\"");
            }
        }

        ImGui.DrawSection("Configuration");

        ImGui.Checkbox("Don't repeat relays", ref Config.DontRepeatRelays);
        ImGuiComponents.HelpMarker("Don't send relays to the channel in which you clicked the relay payload.");

        //ImGui.Checkbox("Override minion flags", ref Config.OverrideMinionFlag);
        //ImGuiComponents.HelpMarker("Override minion relay flags with the location of the SS Rank");

        ImGui.Checkbox("Allow partial world matching", ref Config.AllowPartialWorldMatches);
        ImGuiComponents.HelpMarker("This will allow matching shorthands of worlds (e.g. \"behe\" -> Behemoth) but may result in false positives.");

        ImGui.Checkbox("Remove <world> tags for Novice Network relays", ref Config.RemoveWorldFromNNCallouts);
        ImGuiComponents.HelpMarker("Removes the <world> tag from your relays and any non-whitespace characters surrounding it, then trims any excess whitespace before sending to Novice Network.");

        ImGui.Checkbox("Only send local hunts to local channels", ref Config.OnlySendLocalHuntsToLocalChannels);
        ImGuiComponents.HelpMarker("If a hunt is detected as being off your home world, it will only be relayed to non-local channels.");

        ImGui.Checkbox("Assume blank worlds are local", ref Config.AssumeBlankWorldsAreLocal);
        ImGuiComponents.HelpMarker("If the world is failed to be detected, assume it's meant for the local world.");

        ImGui.Indent();
        foreach (var l in Enum.GetValues<Locality>().Select((x, i) => new { Value = x, Index = i })) {
            if (ImGui.RadioButton($"{l.Value.ToString().SplitWords()}", Config.AssumedLocality == l.Value))
                Config.AssumedLocality = l.Value;
            if (l.Index < Enum.GetValues<Locality>().Length - 1)
                ImGui.SameLine();
        }
        ImGui.Unindent();

#if DEBUG
        ImGui.Checkbox("Dry run", ref Config.DryRun);
        ImGuiComponents.HelpMarker("Enabling this will print the messages to chat without actually sending them to the server. This is just for testing.");
#endif

        ImGui.DrawSection("Chat Message Pattern");
        ImGui.InputText($"##{nameof(Config.ChatMessagePattern)}", ref Config.ChatMessagePattern, 64);
        ImGuiComponents.HelpMarker("Available tags: <world>, <type>, <flag>");

        ImGui.DrawSection("Relay Type Configuration");
        foreach (var t in Config.Types.ToList().Select((x, i) => new { Value = x, Index = i })) {
            ImGui.TextUnformatted($"{t.Value.RelayType.ToString().SplitWords()}");
            ImGui.Indent();
            ImGui.TextV("Format: ");
            ImGui.SameLine();
            var tmpF = Config.Types[t.Index].TypeFormat;
            if (ImGui.InputText($"##{t.Value.RelayType}{nameof(t.Value.TypeFormat)}", ref tmpF, 64))
                Config.Types[t.Index] = (t.Value.RelayType, tmpF, t.Value.TypeHeuristics);
            ImGuiComponents.HelpMarker("This is what will be sent in chat to replace the <type> tag.");

            ImGui.TextV("Heuristics: ");
            ImGui.SameLine();
            var tmpH = Config.Types[t.Index].TypeHeuristics;
            if (ImGui.InputText($"##{t.Value.RelayType}{nameof(t.Value.TypeHeuristics)}", ref tmpH, 128))
                Config.Types[t.Index] = (t.Value.RelayType, t.Value.TypeFormat, tmpH);
            ImGuiComponents.HelpMarker("These are the comma separated heuristics to check against in the message to match to a type.\nThis supports regex if you surrouned the heuristic with \"/\"\nAll special text icons are converted to normal text automatically.");
            ImGui.Unindent();
        }
    }

    private void OnChatMessage(IHandleableChatMessage message) {
        if (!Svc.ClientState.IsLoggedIn) return; // messages sometimes trigger during login, but before fully logged in and thus stuff like checking player DC fails later
        if (message.Sender.TextValue == Svc.PlayerState.CharacterName) return;

        if (message.Message.Payloads.FirstOrDefault(x => x is MapLinkPayload, null) is not MapLinkPayload mlp) {
            if (message.Message.Payloads.OfType<DalamudLinkPayload>().Any(p => p.Plugin == "HuntAlerts") && _huntAlertsRelays.Count > 0) {
                var (world, i, relayType) = DetectWorldInstanceRelayType(message.Message);
                // ignoring instance matching cause that won't ever be right for no instance s ranks and trains
                if (_huntAlertsRelays.FirstOrDefault(r => r.World.RowId == world?.RowId && r.RelayType == relayType) is RelayPayload relay) {
                    Log($"HuntAlerts relay detected: {relay}");
                    mlp = relay.MapLink;
                    message.Message = new SeString().Append(message.Message.Payloads).Append([RelayLinkPayload, new IconPayload(BitmapFontIcon.NotoriousMonster), relay.ToRawPayload(), RawPayload.LinkTerminator]);
                    _huntAlertsRelays.Remove(relay);
                }
            }
            return;
        }

        try {
            var (world, instance, relayType) = DetectWorldInstanceRelayType(message.Message);
            if ((RelayTypes)relayType == RelayTypes.None) {
                Log($"Failed to detect relay type in {nameof(MapLinkPayload)} message: {message.Message}");
                return;
            }
            if (world is null && message.LogKind is XivChatType.NoviceNetwork)
                world = Svc.PlayerState.CurrentWorld.Value;
            if (world is null && Config.AssumeBlankWorldsAreLocal) {
                world = Config.AssumedLocality switch {
                    Locality.PlayerHomeWorld => Svc.PlayerState.HomeWorld.Value,
                    Locality.PlayerCurrentWorld => Svc.PlayerState.CurrentWorld.Value,
                    Locality.SenderHomeWorld => message.Sender.Payloads.OfType<TextPayload>().Select(p => p.Text!.Contains((char)SeIconChar.CrossWorld)
                        ? FindRow<World>(x => x!.IsPublic && p.Text.Split((char)SeIconChar.CrossWorld)[1].Contains(x.Name.ToString(), StringComparison.OrdinalIgnoreCase))
                        : Svc.PlayerState.CurrentWorld.Value)
                        .FirstOrDefault(Svc.PlayerState.CurrentWorld.Value),
                    _ => null
                };
            }
            if (world is { RowId: var id }) {
                Debug($"Adding payload with [world={world.Value.Name}, i={instance}, type={(RelayTypes)relayType}]");
                // can't change IMutableChatMessage in place. Have to assign a new string to it
                var seString = new SeString().Append(message.Message.Payloads).Append([RelayLinkPayload, new IconPayload(BitmapFontIcon.NotoriousMonster), new RelayPayload(mlp, id, instance, relayType, (uint)message.LogKind).ToRawPayload(), RawPayload.LinkTerminator]);
                message.Message = seString;
            }
            else
                Log($"Failed to detect world in {nameof(MapLinkPayload)} message: {message.Message}");
        }
        catch (Exception ex) {
            Error(ex, $"[{nameof(OnChatMessage)}] Unexpected error");
        }
    }

    private void HandleRelayLink(uint _, SeString link) {
        var payload = link.Payloads.OfType<RawPayload>().Select(RelayPayload.Parse).FirstOrDefault(x => x != default);
        if (payload == default) { Error($"Failed to parse {nameof(RelayPayload)}"); return; }
        if (Player.CsTerritoryIntendedUseEnum is TerritoryIntendedUse.CrystallineConflict or TerritoryIntendedUse.CrystallineConflictCustomMatch or TerritoryIntendedUse.DeepDungeon) {
            Log($"Relay link ignored. Player in territory {Player.Territory.RowId} ({Player.TerritoryIntendedUse}) where chat is not permitted.");
            return;
        }
        if (payload == LastRelay) {
            Log("Relay link ignored; same as last relay.");
            return;
        }

        var relay = BuildRelayMessage(payload.MapLink, payload.World, payload.Instance, payload.RelayType);
        var nnRelay = BuildRelayMessage(payload.MapLink, payload.World, payload.Instance, payload.RelayType, true);
        foreach (var (channel, command, islocal, _) in Config.Channels.Where(c => c.Enabled)) {
            var channelName = channel.GetAttribute<XivChatTypeInfoAttribute>()?.FancyName ?? throw new Exception($"Channel has no {nameof(XivChatTypeInfoAttribute)}");
            if (Config.DontRepeatRelays && payload.OriginChannel == ((uint)channel)) continue; // don't send to the channel that relay was clicked from
            if (channelName.StartsWith("Linkshell") && Svc.PlayerState.CurrentWorld.RowId != Svc.PlayerState.HomeWorld.RowId) continue; // don't send to linkshells when off homeworld
            if (Config.OnlySendLocalHuntsToLocalChannels && islocal && !channelName.StartsWith("Novice") && Svc.PlayerState.HomeWorld.RowId != payload.World.RowId) continue; // don't send to non-novice local channels when off homeworld
            if (channelName.StartsWith("Novice") && Svc.PlayerState.CurrentWorld.RowId != payload.World.RowId) continue; // don't send offworld relays to NN
            if (channelName.StartsWith("Novice") && !InfoProxyNoviceNetwork.IsInNoviceNetwork()) continue;

            if (Config.DryRun) {
                Svc.Chat.Print(new() { Type = channel, MessageBytes = [.. Encoding.UTF8.GetBytes($"[DRYRUN] "), .. channelName.StartsWith("Novice") ? nnRelay.ToArray() : relay.ToArray()] });
                continue;
            }

            TaskManager.Enqueue(() => {
                if (Player.Available) // messages can't be sent when travelling between zones where your player goes null
                {
                    Svc.Chat.SendMessageUnsafe([.. Encoding.UTF8.GetBytes($"/{command} "), .. channelName.StartsWith("Novice") ? nnRelay.ToArray() : relay.ToArray()]);
                    return true;
                }
                else return false;
            });
        }

        LastRelay = payload;
    }

    private Lumina.Text.SeStringBuilder BuildRelayMessage(MapLinkPayload MapLink, World World, uint? Instance, uint RelayType, bool removeWorld = false) {
        var pattern = "(?i)(<flag>|<world>|<type>)";
        var msg = removeWorld ? Regex.Replace(Config.ChatMessagePattern, @"[^\s]*<world>[^\s]*", "").Replace(@"\s+", " ").Trim() : Config.ChatMessagePattern;
        var splitMsg = Regex.Split(msg, pattern);
        var sb = new Lumina.Text.SeStringBuilder();
        foreach (var s in splitMsg) {
            switch (s) {
                case "<flag>":
                    // Hook PronounModule.Instance()->VirtualTable->ProcessString and decode the Utf8String to check the args here in case they change in the future
                    sb.BeginMacro(Lumina.Text.Payloads.MacroCode.Fixed)
                        .AppendIntExpression(200)
                        .AppendIntExpression(3) // type of link (player, job, item, map, etc)
                        .AppendUIntExpression(MapLink.TerritoryType.RowId) // territory
                        .AppendUIntExpression(Instance is not null ? MapLink.Map.RowId | ((uint)Instance << 16) : MapLink.Map.RowId) // map or (map | (instance << 16))
                        .AppendIntExpression(MapLink.RawX) // x -> (int)(MathF.Round(posX, 3, MidpointRounding.AwayFromZero) * 1000)
                        .AppendIntExpression(MapLink.RawY) // y
                        .AppendIntExpression(-30000) // z or -30000 for no z
                        .AppendIntExpression(0) // PlaceName override if not 0
                        .EndMacro();
                    break;
                case "<world>":
                    sb.Append(World.Name);
                    break;
                case "<type>":
                    switch (RelayType) {
                        case (uint)RelayTypes.SRank:
                            sb.Append(Config.Types[(int)RelayTypes.SRank].TypeFormat);
                            break;
                        case (uint)RelayTypes.Minions:
                            sb.Append(Config.Types[(int)RelayTypes.Minions].TypeFormat);
                            break;
                        case (uint)RelayTypes.Train:
                            sb.Append(Config.Types[(int)RelayTypes.Train].TypeFormat);
                            break;
                        case (uint)RelayTypes.FATE:
                            sb.Append(Config.Types[(int)RelayTypes.FATE].TypeFormat);
                            break;
                    }
                    break;
                default:
                    sb.Append(s);
                    break;
            }
        }
        return sb;
    }

    private (World?, uint, uint) DetectWorldInstanceRelayType(SeString message) {
        var text = string.Join(" ", message.Payloads.OfType<TextPayload>().Select(x => x.Text));
        var heuristicInstance = 0;
        var mapInstance = text.Select(c => c.ReplaceSeIconInstanceNumber()).OfType<int>().FirstOrDefault(0);

        // trim texts within MapLinkPayload
        const string linkPattern = ".*?\\)";
        var rgx = new Regex(linkPattern);
        text = rgx.Replace(text, "");
        // replace Boxed letters with alphabets
        text = string.Join(string.Empty, text.Select(c => c.ReplaceSeIconLetters()));

        var instanceMatch = Regex.Match(text, InstanceHeuristics, RegexOptions.IgnoreCase);
        if (instanceMatch.Success) {
            if (instanceMatch.Groups["instance"].Success && int.TryParse(instanceMatch.Groups["instance"].Value, out var i1))
                heuristicInstance = i1;
            if (instanceMatch.Groups["iNumber"].Success && int.TryParse(instanceMatch.Groups["iNumber"].Value, out var i2))
                heuristicInstance = i2;
        }

        var relayType = RelayTypes.None;
        foreach (var t in Config.Types) {
            if (t.TypeHeuristics.Split(',').Select(x => x.Trim()).Any(x => { return x.StartsWith('/') && x.EndsWith('/') ? Regex.IsMatch(text, x[1..^1], RegexOptions.IgnoreCase) : text.Contains(x, StringComparison.OrdinalIgnoreCase); })) {
                relayType = t.RelayType;
                break;
            }
        }

        World? partial = null;
        if (Config.AllowPartialWorldMatches)
            foreach (var word in RemoveConflicts(text).Split(' ').Where(t => !ECommons.GenericHelpers.IsNullOrEmpty(t) && t.Length > 2))
                partial ??= FindRow<World>(x => x.IsPublic && x.DataCenter.RowId == Svc.PlayerState.CurrentWorld.Value.DataCenter.RowId && x.Name.ExtractText().Contains(word.FilterNonAlphanumeric(), StringComparison.OrdinalIgnoreCase));

        return (partial ?? FindRow<World>(x => x.IsPublic && RemoveConflicts(text).Contains(x.Name.ExtractText(), StringComparison.OrdinalIgnoreCase)) ?? null, heuristicInstance != 0 ? (uint)heuristicInstance : (uint)mapInstance, (uint)relayType);
    }

    // I think this is the only case where an S rank has the name of a world contained within it
    private string RemoveConflicts(string text) => text.Replace("kaiser behemoth", string.Empty, StringComparison.OrdinalIgnoreCase);

    private void OnHuntAlert(HuntAlertsMessage message) {
        Log($"Received HuntAlert: {message}");
        var territory = TerritoryType.Where(r => r.TerritoryIntendedUse.Value.StructsEnum is TerritoryIntendedUse.Overworld).FirstOrNull(r => r.PlaceName.Value.Name == message.StartZone);
        if (territory is null) return;

        var world = World.Where(r => r.IsPublic).FirstOrNull(r => r.Name == message.HuntWorld);
        if (world is null) return;

        Vector2 pos = new(float.Parse(message.LocationCoords.Split(", ")[0]), float.Parse(message.LocationCoords.Split(", ")[1]));
        var maplink = new MapLinkPayload(territory.Value.RowId, territory.Value.Map.RowId, pos.X, pos.Y);
        var relayType = message.HuntType switch {
            "srank" => RelayTypes.SRank,
            "new_hunt" => RelayTypes.Train,
            _ => RelayTypes.None
        };
        if (relayType == RelayTypes.None)
            return;
        // no way of knowing if a zone has instances so we'll just null all instance 1s to avoid inserting an instance for a zone that doesn't have them
        var relay = new RelayPayload(maplink, world.Value.RowId, message.Instance > 1 ? (uint)message.Instance : null, (uint)relayType, (uint)Echo);
        Log($"Constructed {nameof(RelayPayload)}: {relay}");
        _huntAlertsRelays.Add(relay);
    }
}
