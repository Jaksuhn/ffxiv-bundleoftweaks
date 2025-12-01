using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.GameFunctions;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;

public sealed class GatherLeve : CommonTasks {
    private unsafe DGameObject? LeveNode => Svc.Objects.FirstOrDefault(o => o.IsTargetable && o.ObjectKind == ObjectKind.GatheringPoint && o.Struct()->NamePlateIconId == 71244);
    protected override async Task Execute() {
        // travel to quest location
        // start leve
        // find the gathering point circles on the map
        // find nearest node and gather
        // repeat from 3 if no nearby nodes
        if (LeveNode is { } node) {
            await MoveTo(node.Position, MovementConfig.Everything);
            await InteractWith(node);
        }
    }
}
