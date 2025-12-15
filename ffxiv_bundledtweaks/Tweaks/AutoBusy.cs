using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.UI.Info;

namespace Automaton.Tweaks;

[Tweak]
public unsafe partial class AutoBusy : Tweak {
    public override string Name => "Auto Busy";
    public override string Description => "Toggles busy while you're teleporting.";

    [AddressHook<ActionManager>(nameof(ActionManager.MemberFunctionPointers.UseAction))]
    private bool UseAction(ActionManager* self, ActionType actionType, uint actionId, ulong targetId, uint extraParam, ActionManager.UseActionMode mode, uint comboRouteId, bool* outOptAreaTargeted) {
        if (actionType is ActionType.Action && actionId is 5 && Player.OnlineStatus is not 12) {
            InfoProxyDetail.Instance()->SendOnlineStatusUpdate(12);
        }
        return UseActionHook.Original(self, actionType, actionId, targetId, extraParam, mode, comboRouteId, outOptAreaTargeted);
    }

    [AddressHook<ActionEffectHandler>(nameof(ActionEffectHandler.MemberFunctionPointers.Receive))]
    private void ProcessPacketActionEffect(uint casterID, Character* casterObj, Vector3* targetPos, ActionEffectHandler.Header* header, ActionEffectHandler.TargetEffects* effects, GameObjectId* targets) {
        if (header->ActionType is ActionType.Action && header->ActionId is 5 && Player.OnlineStatus is 12) {
            InfoProxyDetail.Instance()->RefreshOnlineStatus();
        }
        ProcessPacketActionEffectHook.Original(casterID, casterObj, targetPos, header, effects, targets);
    }
}
