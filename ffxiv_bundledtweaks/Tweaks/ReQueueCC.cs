using Dalamud.Game.Chat;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;

namespace Automaton.Tweaks;

[Tweak]
public class ReQueueCC : Tweak {
    public override string Name => "CC Error Requeue";
    public override string Description => "Requeues for Crystalline Conflict when your registration was cancelled due to a map change.";

    public override void Enable() => Svc.Chat.LogMessage += CheckForMessage;
    public override void Disable() => Svc.Chat.LogMessage -= CheckForMessage;

    private unsafe void CheckForMessage(ILogMessage message) {
        if (message.LogMessageId is 7392)
            if (AgentContentsFinder.Instance()->SelectedContent.FirstOrNull(x => x.ContentType is ContentsId.ContentsType.Roulette) is { Id: (40 or 41) and var id })
                ContentsFinder.Instance()->QueueInfo.QueueRoulette((byte)id);
    }
}
