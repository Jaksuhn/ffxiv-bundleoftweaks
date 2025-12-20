using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Utilities.Extensions;

public static class LuminaExtensions {
    public static unsafe bool IsHeldRaw(this ConfigKey key) {
        if (!key.TryGetInputId(out var inputId)) return false;
        var keybind = UIInputData.Instance()->GetKeybind(inputId);
        foreach (var ks in keybind->KeySettings) {
            if (!Svc.KeyState.IsVirtualKeyValid((VirtualKey)ks.Key)) continue;
            if (Svc.KeyState.GetRawValue((VirtualKey)ks.Key) != 0) return true;
        }
        return false;
    }
    public static unsafe void ResetKeyState(this ConfigKey key) {
        if (key.TryGetInputId(out var inputId)) {
            var keybind = UIInputData.Instance()->GetKeybind(inputId);
            foreach (var ks in keybind->KeySettings) {
                if (!Svc.KeyState.IsVirtualKeyValid((VirtualKey)ks.Key)) continue;
                Svc.KeyState.SetRawValue((VirtualKey)ks.Key, 0);
                if (ks.KeyModifier == KeyModifierFlag.Ctrl)
                    Svc.KeyState.SetRawValue(VirtualKey.CONTROL, 0);
                if (ks.KeyModifier == KeyModifierFlag.Shift)
                    Svc.KeyState.SetRawValue(VirtualKey.LSHIFT, 0);
                if (ks.KeyModifier == KeyModifierFlag.Alt)
                    Svc.KeyState.SetRawValue(VirtualKey.MENU, 0);
            }
        }
    }
}
