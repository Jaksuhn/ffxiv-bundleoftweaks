using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;

internal class VoidMatches(string name) : CommonTasks {
    protected override async Task Execute() {
        foreach (var obj in Svc.Objects.OfType<IBattleChara>().Where(o => o.Name.TextValue.Contains(name, StringComparison.InvariantCultureIgnoreCase))) {
            Svc.Targets.Target = obj;
            Chat.SendMessage("/voidtarget");
            await NextFrame();
        }
    }
}
