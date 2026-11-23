using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using WeaponSlot = FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer.WeaponSlot;

namespace ComplexTweaks.Tweaks;

public class EnhancedWeaponsConfiguration {
    [BoolConfig(Description = "Keep weapon VFX on while sheathed.")]
    public bool KeepVfx = false;

    [BoolConfig(Description = "Replace normal draw/sheathe with the emote variants.")]
    public bool UseEmotes = false;

    [BoolConfig(Description = "When sheathing, reset enmity on any striking dummies you're in combat with.")]
    public bool ResetEnmity = false;
}

public unsafe partial class EnhancedWeapons : Tweak<EnhancedWeaponsConfiguration> {
    public override string Name => "Enhanced Draw/Sheathe";
    public override string Description => "Various tweaks to drawing and sheathing your weapons.";

    private enum VfxState : int {
        Off = 5,
        On = 6,
        Unk9 = 9,
        Unk10 = 10,
    }

    public override void OnDisable() {
        if (Control.GetLocalPlayer() is var chara && chara != null && !chara->IsWeaponDrawn)
            foreach (var slot in Slots)
                if (GetLocalWeapon(slot) is var cb && cb != null)
                    SetVfxStateHook.Original(cb, (int)VfxState.Off);
    }

    [SigHook("48 89 5C 24 ?? 48 89 74 24 ?? 48 89 7C 24 ?? 41 56 48 83 EC 20 48 8B 01 8B FA 8B B1 ?? ?? ?? ?? 4C 8B F1 48 8B 99 ?? ?? ?? ?? FF 90")]
    private int SetVfxState(CharacterBase* cb, int state) {
        if (Config.KeepVfx && state == VfxState.Off.Value && IsLocalWeapon(cb))
            return 0;
        return SetVfxStateHook.Original(cb, state);
    }

    [SigHook("48 89 5C 24 ?? 57 48 83 EC 30 F3 0F 10 41 04 0F B6 FA 0F 29 74 24 ?? 48 8B D9 0F 57 F6 0F 2F C6 0F 87")]
    private byte RequestToggleWeapon(WeaponState* state, byte drawArg) {
        var draw = (drawArg & 0xFF) != 0;
        if (state->IsUnsheathed == draw)
            return RequestToggleWeaponHook.Original(state, drawArg);

        if (!draw && Config.ResetEnmity)
            ResetStrikingDummies();

        var emote = (ushort)(draw ? 238 : 237);
        if (Config.UseEmotes && UIState.Instance()->IsEmoteUnlocked(emote) && EmoteManager.Instance()->CanExecuteEmote(emote)) {
            var option = new EmoteController.PlayEmoteOption { TargetId = 0xE0000000, DisableLogMessage = true };
            AgentEmote.Instance()->ExecuteEmote(emote, &option, false, false);
            return 1;
        }
        return RequestToggleWeaponHook.Original(state, drawArg);
    }

    private static void ResetStrikingDummies() {
        if (!ICondition.Get()[ConditionFlag.InCombat])
            return;

        UIState.Instance()->Hater.Haters.ToArray().ForEach(h => { if (h.GetGameObject() is not null and var obj) obj->ResetEnmity(); });
    }

    private static readonly WeaponSlot[] Slots = [WeaponSlot.MainHand, WeaponSlot.OffHand];

    private static bool IsLocalWeapon(CharacterBase* cb)
        => cb != null && (cb == GetLocalWeapon(WeaponSlot.MainHand) || cb == GetLocalWeapon(WeaponSlot.OffHand));

    private static CharacterBase* GetLocalWeapon(WeaponSlot slot) {
        var player = Control.GetLocalPlayer();
        if (player == null)
            return null;
        var cb = (CharacterBase*)player->DrawData.Weapon(slot).DrawData.DrawObject;
        return cb != null && cb->GetModelType() == CharacterBase.ModelType.Weapon ? cb : null;
    }
}
