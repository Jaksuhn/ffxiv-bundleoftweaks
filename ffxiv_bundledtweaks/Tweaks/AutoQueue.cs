using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.STD;

namespace ComplexTweaks.Tweaks;

[Tweak]
internal class AutoQueue : Tweak {
    public override string Name => "Auto Queue";
    public override string Description => "Auto queue into a pre-checked duty (on zone change).\n" +
        "If in a party, waits for all players to be in the overworld, and either targetable or in another zone from you.";

    public override void Enable() => Svc.ClientState.TerritoryChanged += OnTerritoryChanged;
    public override void Disable() => Svc.ClientState.TerritoryChanged -= OnTerritoryChanged;

    private unsafe void OnTerritoryChanged(ushort obj) {
        if (Player.IsInDuty || Player.IsPenalised) return;
        TaskManager.Enqueue(() => !Player.IsBusy);
        TaskManager.Enqueue(() => Svc.Party.All(p => Svc.Condition.CanQueue()), "WaitAllPartyInOverworld");
        TaskManager.Enqueue(() => Svc.Party.Any(p => p.Territory.RowId != Player.Territory.RowId) || Svc.Party.AllTargetable(), "WaitAllPartyNotWithPlayerOrTargetable");
        TaskManager.Enqueue(QueueSelectedDuty);
    }

    private unsafe bool QueueSelectedDuty() {
        var content = AgentContentsFinder.Instance()->SelectedContent;
        if (content.Any(x => x.ContentType is ContentsId.ContentsType.Roulette)) {
            ContentsFinder.Instance()->QueueInfo.QueueRoulette((byte)content.First().Id);
            return true;
        }
        else {
            ContentsFinder.Instance()->QueueInfo.QueueDuties(ToPtr(content), content.Count);
            return true;
        }
    }

    public static unsafe uint* ToPtr(StdVector<ContentsId> contentsIds) {
        var ids = contentsIds.Select(x => x.Id).ToList();
        var array = stackalloc uint[ids.Count];
        for (var i = 0; i < ids.Count; i++)
            array[i] = ids[i];
        return array;
    }
}
