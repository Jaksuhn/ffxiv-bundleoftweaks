using Dalamud.Game.ClientState.Objects.Types;
using ECommons.Automation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Automaton.Tasks;
internal class VoidMatches(string name) : CommonTasks
{
    protected override async Task Execute()
    {
        foreach (var obj in Svc.Objects.OfType<IBattleChara>().Where(o => o.Name.TextValue.Contains(name, StringComparison.InvariantCultureIgnoreCase)))
        {
            Svc.Targets.Target = obj;
            Chat.SendMessage("/voidtarget");
            await NextFrame();
        }
    }
}
