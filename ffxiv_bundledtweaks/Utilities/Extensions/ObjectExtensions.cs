using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace ComplexTweaks.Utilities.Extensions;

public static unsafe class ObjectExtensions {
    public static BattleChara* BattleChara(this DGameObject obj) => (BattleChara*)obj.Address;
    public static Character* Character(this DGameObject obj) => (Character*)obj.Address;

    public static BattleChara* BattleChara(this CSGameObject obj) => (BattleChara*)&obj;
    public static Character* Character(this CSGameObject obj) => (Character*)&obj;
}
