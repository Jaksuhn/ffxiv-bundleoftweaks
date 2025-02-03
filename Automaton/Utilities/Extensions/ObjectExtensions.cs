using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace Automaton.Utilities.Extensions;
public static class ObjectExtensions
{
    public static unsafe BattleChara* BattleChara(this DGameObject obj) => (BattleChara*)obj.Address;
    public static unsafe Character* Character(this DGameObject obj) => (Character*)obj.Address;

    public static unsafe BattleChara* BattleChara(this CSGameObject obj) => (BattleChara*)&obj;
    public static unsafe Character* Character(this CSGameObject obj) => (Character*)&obj;

    public static bool IsTargetingPlayer(this DGameObject obj) => obj.TargetObjectId == Player.Object.GameObjectId;
}
