using ECommons.GameFunctions;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace ComplexTweaks.Utilities.Extensions;

public static unsafe class ObjectExtensions {
    public static BattleChara* BattleChara(this DGameObject obj) => (BattleChara*)obj.Address;
    public static Character* Character(this DGameObject obj) => (Character*)obj.Address;

    public static BattleChara* BattleChara(this CSGameObject obj) => (BattleChara*)&obj;
    public static Character* Character(this CSGameObject obj) => (Character*)&obj;

    public static bool IsTargetingPlayer(this DGameObject obj) => obj.TargetObjectId == Player.Object.GameObjectId;

    public static FFXIVClientStructs.FFXIV.Client.Game.Event.EventHandlerInfo? EventInfo(this DGameObject obj)
        => obj == null || obj.Struct() == null || obj.Struct()->EventHandler == null ? null : obj.Struct()->EventHandler->Info;

    public static float DistanceTo(this DGameObject obj, Vector3 position) => Vector3.Distance(obj.Position, position);
}
