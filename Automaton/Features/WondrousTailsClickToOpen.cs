using Dalamud.Game.Addon.Events;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using System.Text.RegularExpressions;
using static FFXIVClientStructs.FFXIV.Client.Game.UI.ContentsFinderQueueInfo.QueueStates;

namespace Automaton.Features;

[Tweak]
internal class WondrousTailsClickToOpen : Tweak
{
    public override string Name => "Wondrous Tails Click To Open";
    public override string Description => "Click on a WT duty to open the duty finder to it. Ctrl+click to queue into the duty semi-smartly.";

    private List<ContentFinderCondition> _sheet = null!;
    private unsafe ContentsFinderQueueInfo QueueInfo => ContentsFinder.Instance()->QueueInfo;

    public override void Enable()
    {
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PostSetup, "WeeklyBingo", OnAddonSetup);
        Svc.AddonLifecycle.RegisterListener(AddonEvent.PreFinalize, "WeeklyBingo", OnAddonFinalize);
        _sheet = [.. GetSheet<ContentFinderCondition>().Where(x => !x.MSQRoulette)];
    }

    public override void Disable()
    {
        Svc.AddonLifecycle.UnregisterListener(OnAddonSetup);
        Svc.AddonLifecycle.UnregisterListener(OnAddonFinalize);
    }

    private unsafe void OnAddonSetup(AddonEvent type, AddonArgs args)
    {
        var addonWeeklyBingo = (AddonWeeklyBingo*)args.Addon;
        ResetEventHandles();
        foreach (var index in Enumerable.Range(0, 16))
        {
            var dutySlot = addonWeeklyBingo->DutySlotList[index];
            eventHandles[index] = Svc.AddonEventManager.AddEvent((nint)addonWeeklyBingo, (nint)dutySlot.DutyButton->OwnerNode, AddonEventType.ButtonClick, OnDutySlotClick);
        }
    }

    private unsafe void QueueDuty(uint dutyId, bool roulette = false)
    {
        if (roulette)
        {
            if (ImGuiEx.Ctrl)
            {
                ContentsFinder.Instance()->ResetFlags();
                QueueInfo.QueueRoulette((byte)dutyId);
            }
            else
                AgentContentsFinder.Instance()->OpenRouletteDuty((byte)dutyId);
        }
        else
        {
            if (ImGuiEx.Ctrl)
            {
                ContentsFinder.Instance()->IsUnrestrictedParty = true;
                QueueInfo.QueueDuties(&dutyId, 1);
            }
            else
                AgentContentsFinder.Instance()->OpenRegularDuty(dutyId);
        }
    }

    private unsafe void QueueDuty(List<uint> duties)
    {
        if (duties.Count == 0) return;
        if (QueueInfo.QueueState is Pending or Queued) QueueInfo.CancelQueue();
        if (ImGuiEx.Ctrl)
        {
            ContentsFinder.Instance()->ResetFlags();
            var array = stackalloc uint[duties.Count];
            for (var i = 0; i < duties.Count; i++)
                array[i] = duties[i];
            Log($"Queueing [{string.Join(", ", duties)}] [{string.Join(", ", new Span<uint>(array, duties.Count).ToArray())}]");
            QueueInfo.QueueDuties(array, duties.Count);
        }
        else
            AgentContentsFinder.Instance()->OpenRegularDuty(duties.FirstOrDefault());
    }

    private void OnAddonFinalize(AddonEvent type, AddonArgs args) => ResetEventHandles();

    private unsafe void OnDutySlotClick(AddonEventType atkEventType, nint atkUnitBase, nint atkResNode)
    {
        var dutyButtonNode = (AtkResNode*)atkResNode;
        var tileIndex = (int)dutyButtonNode->NodeId - 12;
        var selectedTask = PlayerState.Instance()->GetWeeklyBingoTaskStatus(tileIndex);
        var bingoData = PlayerState.Instance()->WeeklyBingoOrderData[tileIndex];

        if (selectedTask is PlayerState.WeeklyBingoTaskStatus.Open)
        {
            var dutiesForTask = GetInstanceListFromId(bingoData);
            var territoryType = dutiesForTask.FirstOrDefault();
            if (FindRow<ContentFinderCondition>(c => c.TerritoryType.RowId == territoryType) is { ClassJobLevelRequired: < 80 } cfc)
                QueueDuty(cfc.RowId);
            else
                QueueDuty(dutiesForTask);
        }
    }

    private readonly IAddonEventHandle?[] eventHandles = new IAddonEventHandle?[16];
    private void ResetEventHandles()
    {
        foreach (var index in Enumerable.Range(0, 16))
        {
            if (eventHandles[index] is { } handle)
            {
                Svc.AddonEventManager.RemoveEvent(handle);
                eventHandles[index] = null;
            }
        }
    }

    private unsafe List<uint> GetInstanceListFromId(uint orderDataId)
    {
        var bingoOrderData = GetSheet<WeeklyBingoOrderData>().GetRow(orderDataId);
        Debug($"{nameof(OnDutySlotClick)}: [row={bingoOrderData.RowId}; type={bingoOrderData.Type}; text={bingoOrderData.Text.Value.Description};]");
        switch (bingoOrderData.Type)
        {
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
                switch (bingoOrderData.Unknown1)
                {
                    // Treasure Maps, nothing to do
                    case 1: return [];

                    // PvP
                    case 2:
                        switch (bingoOrderData.RowId)
                        {
                            // Crystalline Conflict
                            case 52:
                                QueueDuty(40, true); // Casual Match
                                break;
                            // Frontlines
                            case 54:
                                QueueDuty(7, true);
                                break;
                            // Rival Wings
                            case 67:
                                QueueDuty(599, true); // Hidden Gorge
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
                return bingoOrderData.Data.RowId switch
                {
                    // Binding Coil, Second Coil, Final Coil
                    2 => [241, 242, 243, 244, 245],
                    3 => [355, 356, 357, 358],
                    4 => [193, 194, 195, 196],

                    // Gordias, Midas, The Creator
                    5 => [442, 443, 444, 445],
                    6 => [520, 521, 522, 523],
                    7 => [580, 581, 582, 583],

                    // Deltascape, Sigmascape, Alphascape
                    8 => [691, 692, 693, 694],
                    9 => [748, 749, 750, 751],
                    10 => [798, 799, 800, 801],
                    // Eden's Gate: Resurrection or Descent
                    11 => [849, 850],
                    // Eden's Gate: Inundation or Sepulture
                    12 => [851, 852],
                    // Eden's Verse: Fulmination or Furor
                    13 => [902, 903],
                    // Eden's Verse: Iconoclasm or Refulgence
                    14 => [904, 905],
                    // Eden's Promise: Umbra or Litany
                    15 => [942, 943],
                    // Eden's Promise: Anamorphosis or Eternity
                    16 => [944, 945],
                    // Asphodelos: First or Second Circles
                    17 => [1002, 1004],
                    // Asphodelos: Third or Fourth Circles
                    18 => [1006, 1008],
                    // Abyssos: Fifth or Sixth Circles
                    19 => [1081, 1083],
                    // Abyssos: Seventh or Eight Circles
                    20 => [1085, 1087],
                    // Anabaseios: Ninth or Tenth Circles
                    21 => [1147, 1149],
                    // Anabaseios: Eleventh or Twelwth Circles
                    22 => [1151, 1153],
                    // Eden's Gate
                    23 => [849, 850, 851, 852],
                    // Eden's Verse
                    24 => [902, 903, 904, 905],
                    // Eden's Promise
                    25 => [942, 943, 944, 945],
                    // Alliance Raids (A Realm Reborn)
                    26 => [174, 372, 151],
                    // Alliance Raids (Heavensward)
                    27 => [508, 556, 627],
                    // Alliance Raids (Stormblood)
                    28 => [734, 776, 826],
                    // Alliance Raids (Shadowbringers)
                    29 => [882, 917, 966],
                    // Alliance Raids (Endwalker)
                    30 => [1054, 1118, 1178],
                    // Asphodelos: First to Fourth Circles
                    31 => [1002, 1004, 1006, 1008],
                    // Abyssos: Fifth to Eighth Circles
                    32 => [1081, 1083, 1085, 1087],
                    // Anabaseios: Ninth to Twelfth Circles
                    33 => [1147, 1149, 1151, 1153],
                    // AAC Light-heavyweight M1 or M2
                    34 => [1225, 1227],
                    // AAC Light-heavyweight M3 or M4
                    35 => [1229, 1231],
                    _ => [],
                };
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
