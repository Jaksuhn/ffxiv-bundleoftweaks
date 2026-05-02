using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Events.EventDataTypes;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Text.RegularExpressions;

namespace ComplexTweaks.Tweaks;

[Tweak]
internal class WondrousTailsClickToOpen : Tweak {
    public override string Name => "Wondrous Tails Click To Open";
    public override string Description => "Click on a WT duty to open the duty finder to it. Ctrl+click to queue into the duty semi-smartly.";

    private List<ContentFinderCondition> _sheet = null!;
    private unsafe ContentsFinderQueueInfo* QueueInfo => ContentsFinder.Instance()->GetQueueInfo();

    public override void Enable() {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "WeeklyBingo", OnAddonSetup);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "WeeklyBingo", OnAddonFinalize);
        _sheet = [.. GetSheet<ContentFinderCondition>().Where(x => !x.MSQRoulette)];
    }

    public override void Disable() {
        Svc.AddonLifecycle.UnregisterListener(OnAddonSetup);
        Svc.AddonLifecycle.UnregisterListener(OnAddonFinalize);
    }

    private unsafe void OnAddonSetup(AddonEvent type, AddonArgs args) {
        var addonWeeklyBingo = args.GetAddon<AddonWeeklyBingo>();
        ResetEventHandles();
        foreach (var index in Enumerable.Range(0, 16)) {
            var dutySlot = addonWeeklyBingo->DutySlotList[index];
            eventHandles[index] = Svc.AddonEventManager.AddEvent((nint)addonWeeklyBingo, (nint)dutySlot.DutyButton->OwnerNode, AddonEventType.ButtonClick, OnDutySlotClick);
        }
    }

    private unsafe void QueueDuty(List<uint> duties, bool roulette) {
        if (duties.Count == 0) return;
        if (QueueInfo->QueueState is ContentsFinderQueueState.Pending or ContentsFinderQueueState.Queued) QueueInfo->CancelQueue();

        if (roulette) {
            ContentsFinder.Instance()->ResetFlags();
            QueueInfo->QueueRoulette((byte)duties.First());
        }
        else {
            if (GetRow<ContentFinderCondition>(duties.First())?.ClassJobLevelRequired < PlayerState.Instance()->MaxLevel - 20) {
                ContentsFinder.Instance()->IsUnrestrictedParty = true;
                var d = duties.First();
                QueueInfo->QueueDuties(&d, 1);
            }
            else {
                ContentsFinder.Instance()->ResetFlags();
                var array = stackalloc uint[duties.Count];
                for (var i = 0; i < duties.Count; i++)
                    array[i] = duties[i];
                Log($"Queueing [{string.Join(", ", duties)}] [{string.Join(", ", new Span<uint>(array, duties.Count).ToArray())}]");
                QueueInfo->QueueDuties(array, duties.Count);
            }
        }
    }

    private unsafe void OpenDuty(List<uint> duties, bool roulette) {
        if (duties.Count == 0) return;
        Log($"Opening {duties.FirstOrDefault()} from [{string.Join(", ", duties)}]");
        if (roulette)
            AgentContentsFinder.Instance()->OpenRouletteDuty((byte)duties.FirstOrDefault());
        else
            AgentContentsFinder.Instance()->OpenRegularDuty(duties.FirstOrDefault());
    }

    private void OnAddonFinalize(AddonEvent type, AddonArgs args) => ResetEventHandles();

    private unsafe void OnDutySlotClick(AddonEventType atkEventType, AddonEventData data) {
        var dutyButtonNode = (AtkResNode*)data.NodeTargetPointer;
        var tileIndex = (int)dutyButtonNode->NodeId - 12;
        var selectedTask = PlayerState.Instance()->GetWeeklyBingoTaskStatus(tileIndex);
        var bingoData = PlayerState.Instance()->WeeklyBingoOrderData[tileIndex];

        if (selectedTask is PlayerState.WeeklyBingoTaskStatus.Open) {
            var dutiesForTask = GetInstanceListFromId(bingoData);
            var duties = FindRows<ContentFinderCondition>(c => dutiesForTask.Contains(c.TerritoryType.RowId)).Select(x => x.RowId).ToList();
            if (ImGuiEx.Ctrl) {
                if (GetRow<ContentFinderCondition>(duties.First())?.ClassJobLevelRequired < PlayerState.Instance()->MaxLevel - 20)
                    QueueDuty([duties.First()], false);
                else
                    QueueDuty(duties, false);
            }
            else
                OpenDuty(duties, false);
        }
    }

    private readonly IAddonEventHandle?[] eventHandles = new IAddonEventHandle?[16];
    private void ResetEventHandles() {
        foreach (var index in Enumerable.Range(0, 16)) {
            if (eventHandles[index] is { } handle) {
                Svc.AddonEventManager.RemoveEvent(handle);
                eventHandles[index] = null;
            }
        }
    }

    private List<uint> GetInstanceListFromId(uint orderDataId) {
        var bingoOrderData = GetSheet<WeeklyBingoOrderData>().GetRow(orderDataId);
        Debug($"{nameof(OnDutySlotClick)}: [row={bingoOrderData.RowId}; type={bingoOrderData.Type}; text={bingoOrderData.Text.Value.Description};]");
        switch (bingoOrderData.Type) {
            // Specific Duty
            case 0:
                return [.. _sheet
                    .Where(c => c.Content.RowId == bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(c => c.TerritoryType.RowId)];

            // Specific Level Dungeon
            case 1:
                return [.. _sheet
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired == bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];

            // Level Range Dungeon
            case 2:
                return [.. _sheet
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired >= bingoOrderData.Data.RowId - (bingoOrderData.Data.RowId > 50 ? 9 : 49) && m.ClassJobLevelRequired <= bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];

            // Special categories
            case 3:
                // handling AgentContentsFinder here is such a hack
                switch (bingoOrderData.ContentCategory) {
                    // Treasure Maps, nothing to do
                    case 1: return [];

                    // PvP
                    case 2:
                        switch (bingoOrderData.RowId) {
                            // Crystalline Conflict
                            case 52:
                                if (ImGuiEx.Ctrl)
                                    QueueDuty([40], true); // Casual Match
                                else
                                    OpenDuty([40], true);
                                break;
                            // Frontlines
                            case 54:
                                if (ImGuiEx.Ctrl)
                                    QueueDuty([7], true);
                                else
                                    OpenDuty([7], true);
                                break;
                            // Rival Wings
                            case 67:
                                if (ImGuiEx.Ctrl)
                                    QueueDuty([599], false); // Hidden Gorge
                                else
                                    OpenDuty([599], false);
                                break;
                        }
                        return [];

                    // Deep Dungeons
                    case 3:
                        return [.. _sheet
                            .Where(m => m.ContentType.RowId is 21)
                            .OrderBy(row => row.SortKey)
                            .Select(m => m.TerritoryType.RowId)];
                }
                return [];

            // Multi-instance raids
            case 4:
                return GetRow<WeeklyBingoMultipleOrder>(bingoOrderData.Data.RowId)?.Content.Where(c => c.IsValid && c.RowId != 0).Select(c => c.Value.ContentFinderCondition.Value.TerritoryType.RowId).ToList() ?? [];
            // Levelling Dungeons Range
            case 5:
                return [.. _sheet
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired >= GetFirstNumber(bingoOrderData.Text.Value.Description.ExtractText()) && m.ClassJobLevelRequired <= bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];
            // High-Level Dungeons (Capstone) Range
            case 6:
                return [.. _sheet
                    .Where(m => m.ContentType.RowId is 2)
                    .Where(m => m.ClassJobLevelRequired >= GetFirstNumber(bingoOrderData.Text.Value.Description.ExtractText()) && m.ClassJobLevelRequired <= bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];
            // Trials Range
            case 7:
                return [.. _sheet
                    .Where(m => m.ContentType.RowId is 4)
                    .Where(m => m.ClassJobLevelRequired >= GetFirstNumber(bingoOrderData.Text.Value.Description.ExtractText()) && m.ClassJobLevelRequired <= bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];
            // Alliance Raid Range
            case 8:
                return [.. _sheet
                    .Where(m => m.ContentType.RowId is 5 && m.ContentMemberType.RowId is 4)
                    .Where(m => m.ClassJobLevelRequired >= GetFirstNumber(bingoOrderData.Text.Value.Description.ExtractText()) && m.ClassJobLevelRequired <= bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];
            // Normal Raid Range
            case 9:
                return [.. _sheet
                    .Where(m => m.ContentType.RowId is 5 && m.ContentMemberType.RowId is 3)
                    .Where(m => m.ClassJobLevelRequired >= GetFirstNumber(bingoOrderData.Text.Value.Description.ExtractText()) && m.ClassJobLevelRequired <= bingoOrderData.Data.RowId)
                    .OrderBy(row => row.SortKey)
                    .Select(m => m.TerritoryType.RowId)];
        }

        Warning($"[{Name}] Unrecognized ID: {orderDataId}");
        return [];
    }

    // The bingoOrderData.Data.RowId will always be the upper limit of the level range. There's no known way of getting the lower so just extract the number.
    private int GetFirstNumber(string str) => int.TryParse(Regex.Match(str, @"\d+").Value, out var number) ? number : 0;
}
