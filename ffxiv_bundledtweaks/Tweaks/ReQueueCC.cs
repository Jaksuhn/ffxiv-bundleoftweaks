using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;

namespace Automaton.Tweaks;

[Tweak]
public class ReQueueCC : Tweak {
    public override string Name => "CC Error Requeue";
    public override string Description => "Requeues for CC when after your registration was cancelled due to a map change.";

    public override void Enable() => Svc.Chat.ChatMessage += CheckForMessage;
    public override void Disable() => Svc.Chat.ChatMessage -= CheckForMessage;
    private unsafe void CheckForMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled) {
        if (type is XivChatType.ErrorMessage && message.Equals(LogMessage.GetRow(7392).Text)) {
            if (AgentContentsFinder.Instance()->SelectedContent.FirstOrNull(x => x.ContentType is ContentsId.ContentsType.Roulette) is { Id: (40 or 41) and var id }) {
                ContentsFinder.Instance()->QueueInfo.QueueRoulette((byte)id);
            }
        }
    }
}
