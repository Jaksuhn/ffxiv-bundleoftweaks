using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Dalamud.Game.DutyState;

namespace ComplexTweaks.Tweaks;

public class EnhancedDutyStartEndConfiguration {
    [NetworkWarning]
    [StringConfig]
    public string StartMsg = string.Empty;
    [StringListConfig]
    public HashSet<string> Players = [];
    [BoolConfig] public bool CheckForAll;
    [BoolConfig] public bool CheckForAny;

    [NetworkWarning]
    [StringConfig]
    public string RecommenceMsg = string.Empty;

    [NetworkWarning]
    [StringConfig]
    public string EndMsg = string.Empty;
    [BoolConfig] public bool AutoLeaveOnEnd;
    [IntConfig] public int TimeToWait;
}

public class EnhancedDutyStartEnd : Tweak<EnhancedDutyStartEndConfiguration> {
    public override string Name => "Enhanced Duty Start/End";
    public override string Description => "Automatically execute certain actions when the duty starts or ends.";

    public override void OnEnable() {
        IDutyState.Get().DutyStarted += OnDutyStart;
        IDutyState.Get().DutyCompleted += OnDutyComplete;
        IDutyState.Get().DutyRecommenced += OnDutyRecommenced;
        IClientState.Get().TerritoryChanged += OnTerritoryChanged;
    }

    public override void OnDisable() {
        IDutyState.Get().DutyStarted -= OnDutyStart;
        IDutyState.Get().DutyCompleted -= OnDutyComplete;
        IDutyState.Get().DutyRecommenced -= OnDutyRecommenced;
        IClientState.Get().TerritoryChanged -= OnTerritoryChanged;
    }

    private string _name = string.Empty;
    public override void DrawConfig() {
        ImGui.DrawSection("Duty Start Options");

        ImGui.InputText($"##{nameof(Config.StartMsg)}", ref Config.StartMsg);
        ImGui.TextColoredWrapped(Colors.Grey, "Sends a party chat message when the duty starts.");

        if (ImGui.InputText($"##AddPlayers", ref _name, 50, ImGuiInputTextFlags.EnterReturnsTrue))
            Config.Players.Add(_name);
        ImGui.TextColoredWrapped(Colors.Grey, "Leave if specific players are not present.");

        if (Config.Players.Count > 0) {
            ImGui.DrawSection("Players to Check For");
            if (ImGui.Checkbox("Check for All", ref Config.CheckForAll))
                if (Config.CheckForAll)
                    Config.CheckForAny = !Config.CheckForAll;
            ImGui.SameLine();
            if (ImGui.Checkbox("Check for Any", ref Config.CheckForAny))
                if (Config.CheckForAny)
                    Config.CheckForAll = !Config.CheckForAny;
        }
        foreach (var person in Config.Players) {
            ImGui.TextV(person);
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(person, FontAwesomeIcon.Trash))
                Config.Players.Remove(person);
        }

        ImGui.DrawSection("Duty Recommence Options");

        ImGui.InputText($"##{nameof(Config.EndMsg)}", ref Config.EndMsg);
        ImGui.TextColoredWrapped(Colors.Grey, "Sends a party chat message when the duty recommences (after the wall drops post-wipe).");

        ImGui.DrawSection("Duty End Options");

        ImGui.InputText($"##{nameof(Config.EndMsg)}", ref Config.EndMsg);
        ImGui.TextColoredWrapped(Colors.Grey, "Sends a party chat message when the duty ends.");

        ImGui.Checkbox("Auto Leave##End", ref Config.AutoLeaveOnEnd);
        if (Config.AutoLeaveOnEnd)
            ImGui.SliderInt("Leave after (s)", ref Config.TimeToWait, 0, 100);
    }

    private void OnDutyStart(IDutyStateEventArgs args) {
        if (!Config.StartMsg.IsEmpty) {
            if (Config.StartMsg.StartsWith('/'))
                IChatGui.Get().SendMessage(Config.StartMsg);
            else
                IChatGui.Get().SendMessage($"/p {Config.StartMsg}");
        }

        var allPlayersInParty = Config.Players.Count > 0 && Config.Players.IsSubsetOf(IPartyList.Get().Select(p => p.Name.TextValue));
        var noPlayersInParty = Config.Players.Count > 0 && !Config.Players.Any(p => IPartyList.Get().Any(pm => pm.Name.TextValue == p));
        if (Config.CheckForAll && !allPlayersInParty || Config.CheckForAny && noPlayersInParty)
            EventFramework.LeaveCurrentContent(true);
    }

    private static uint _territoryID;
    private void OnDutyComplete(IDutyStateEventArgs args) {
        _territoryID = IPlayerState.Get().Territory.RowId;
        if (!Config.EndMsg.IsEmpty) {
            if (Config.EndMsg.StartsWith('/'))
                IChatGui.Get().SendMessage(Config.EndMsg);
            else
                IChatGui.Get().SendMessage($"/p {Config.EndMsg}");
        }

        if (Config.AutoLeaveOnEnd) {
            Automation.Start(AutoTask.From(async t => {
                await t.DelayMs(Config.TimeToWait * 1000);
                EventFramework.LeaveCurrentContent(true);
            }, name: "AutoLeaveDuty"));
        }
    }

    private void OnDutyRecommenced(IDutyStateEventArgs args) {
        if (!Config.RecommenceMsg.IsEmpty) {
            if (Config.RecommenceMsg.StartsWith('/'))
                IChatGui.Get().SendMessage(Config.RecommenceMsg);
            else
                IChatGui.Get().SendMessage($"/p {Config.RecommenceMsg}");
        }
    }

    private void OnTerritoryChanged(uint id) {
        // cancel queue if we changed zones via other means to prevent autoleave from triggering in the next duty
        if (id != _territoryID && Automation.Running)
            Automation.Stop();
    }
}
