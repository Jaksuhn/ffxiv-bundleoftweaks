using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Network;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using System.Reflection;

namespace ComplexTweaks.Tweaks;

#if DEBUG
[Tweak(debug: true)]
public unsafe partial class DebugLogging : Tweak {
    public override string Name => "Logger";
    public override string Description => "It just logs random hooks.";

    private readonly uint[] _blacklist = [1, 3, 4, 31, 32, 96, 97, 98, 99, 101, 104, 105, 106, 110, 142, 144, 148, 1003, 1005, 1006, 1007, 1008]; // these are checked every frame

    [AddressHook<Conditions>(nameof(Conditions.MemberFunctionPointers.HasPermission))]
    internal bool HasPermission(Conditions* thisPtr, uint permissionId, int excludedCondition1 = 0, int excludedCondition2 = 0) {
        var ret = HasPermissionHook.Original(thisPtr, permissionId, excludedCondition1, excludedCondition2);
        if (!_blacklist.Contains(permissionId))
            MethodBase.GetCurrentMethod()?.Log([(nint)thisPtr, permissionId, excludedCondition1, excludedCondition2], ret);
        return ret;
    }

    [AddressHook<GameMain>(nameof(GameMain.MemberFunctionPointers.ExecuteCommand))]
    internal bool ExecuteCommand(int command, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0) {
        var ret = ExecuteCommandHook.Original(command, param1, param2, param3, param4);
        MethodBase.GetCurrentMethod()?.Log([(CommandFlag)command, param1, param2, param3, param4], additionalValues: ret);
        return ret;
    }

    [AddressHook<GameMain>(nameof(GameMain.MemberFunctionPointers.ExecuteLocationCommand))]
    internal bool ExecuteLocationCommand(int command, Vector3* location, int param1 = 0, int param2 = 0, int param3 = 0, int param4 = 0) {
        var ret = ExecuteLocationCommandHook.Original(command, location, param1, param2, param3, param4);
        MethodBase.GetCurrentMethod()?.Log([(LocationCommandFlag)command, *location, param1, param2, param3, param4], ret);
        return ret;
    }

    //[AddressHook<PacketDispatcher>(nameof(PacketDispatcher.MemberFunctionPointers.HandleActorControlPacket))]
    //internal void HandleActorControlPacket(uint entityId, uint category, uint arg1, uint arg2, uint arg3, uint arg4, uint arg5, uint arg6, uint arg7, uint arg8, GameObjectId targetId, bool isRecorded) {
    //    MethodBase.GetCurrentMethod()?.Log([entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId.Id, isRecorded]);
    //    HandleActorControlPacketHook.Original(entityId, category, arg1, arg2, arg3, arg4, arg5, arg6, arg7, arg8, targetId, isRecorded);
    //}

    [AddressHook<ActionManager>(nameof(ActionManager.MemberFunctionPointers.GetActionInRangeOrLoS))]
    internal uint GetActionInRangeOrLoS(uint actionId, GameObject* sourceObject, GameObject* targetObject) {
        var ret = GetActionInRangeOrLoSHook.Original(actionId, sourceObject, targetObject);
        MethodBase.GetCurrentMethod()?.Log([actionId, (nint)sourceObject, (nint)targetObject], ret);
        return ret;
    }

    [AddressHook<AgentCatch>(nameof(AgentCatch.MemberFunctionPointers.UpdateCatch))]
    internal void UpdateCatch(AgentCatch* thisPtr, uint fishId, bool isLarge, ushort size, byte amount, byte level, byte a6, byte a7, bool isMoochable, bool isFirstTimeCatch, byte a10, byte a11) {
        MethodBase.GetCurrentMethod()?.Log([(nint)thisPtr, fishId, isLarge, size, amount, level, a6, a7, isMoochable, isFirstTimeCatch, a10, a11]);
        UpdateCatchHook.Original(thisPtr, fishId, isLarge, size, amount, level, a6, a7, isMoochable, isFirstTimeCatch, a10, a11);
    }

    [AddressHook<PacketDispatcher>(nameof(PacketDispatcher.MemberFunctionPointers.SendEventCompletePacket))]
    internal void SendEventCompletePacket(EventId eventId, short scene, byte a3, uint* payload, byte payloadSize, void* a6) {
        MethodBase.GetCurrentMethod()?.Log([ToString(eventId), scene, a3, (nint)payload, payloadSize, FormatPayload(payload, payloadSize), (nint)a6]);
        SendEventCompletePacketHook.Original(eventId, scene, a3, payload, payloadSize, a6);
    }

    private static string ToString(EventId eventid) => $"{eventid.Id}/{eventid.EntryId}/{eventid.ContentId}";

    private static string FormatPayload(uint* payload, byte payloadSize) {
        if (payload == null || payloadSize == 0)
            return "[]";

        var values = new string[payloadSize];
        for (var i = 0; i < payloadSize; i++)
            values[i] = payload[i].ToString();
        return $"[{string.Join(", ", values)}]";
    }
}
#endif
