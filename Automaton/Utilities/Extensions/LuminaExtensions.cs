using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.System.Input;
using FFXIVClientStructs.FFXIV.Client.UI;
using Lumina.Excel.Sheets;

namespace Automaton.Utilities.Extensions;
public static class LuminaExtensions
{
    public record IngredientExtension()
    {
        public required Item Item { get; init; }
        public required int Amount { get; init; }
    }

    public static IEnumerable<IngredientExtension> Ingredients(this Recipe recipe)
    {
        var output = new List<IngredientExtension>();
        for (var i = 0; i < recipe.Ingredient.Count; i++)
        {
            try
            {
                var item = recipe.Ingredient[i].Value;
                var amount = recipe.AmountIngredient[i];

                output.Add(new IngredientExtension() { Item = item, Amount = amount });
            }
            catch { }
        }

        return output;
    }

    public static string Print(this Item item) => $"[{item.RowId}] {item.Name}";
    public static string Print(this EventItem item) => $"[{item.RowId}] {item.Name}";

    public static bool TryGetInputId(this ConfigKey key, out InputId inputId)
    {
        inputId = Enum.GetValues<InputId>().FirstOrDefault(i => Enum.GetName(i) == key.Label.ToString(), InputId.NotFound);
        return inputId != InputId.NotFound;
    }

    public static unsafe bool IsDown(this ConfigKey key) => key.TryGetInputId(out var inputId) && UIInputData.Instance()->IsInputIdDown(inputId);
    public static unsafe bool IsHeld(this ConfigKey key) => key.TryGetInputId(out var inputId) && UIInputData.Instance()->IsInputIdHeld(inputId);
    public static unsafe bool IsPressed(this ConfigKey key) => key.TryGetInputId(out var inputId) && UIInputData.Instance()->IsInputIdPressed(inputId);
    public static unsafe bool IsReleased(this ConfigKey key) => key.TryGetInputId(out var inputId) && UIInputData.Instance()->IsInputIdReleased(inputId);
    public static unsafe void ResetKeyState(this ConfigKey key)
    {
        if (key.TryGetInputId(out var inputId))
        {
            var keybind = UIInputData.Instance()->GetKeybind(inputId);
            foreach (var ks in keybind->KeySettings)
            {
                if (!Svc.KeyState.IsVirtualKeyValid((VirtualKey)ks.Key)) continue;
                Svc.KeyState[(VirtualKey)ks.Key] = false;
                if (ks.KeyModifier == KeyModifierFlag.Ctrl)
                    Svc.KeyState[VirtualKey.CONTROL] = false;
                if (ks.KeyModifier == KeyModifierFlag.Shift)
                    Svc.KeyState[VirtualKey.SHIFT] = false;
                if (ks.KeyModifier == KeyModifierFlag.Alt)
                    Svc.KeyState[VirtualKey.MENU] = false;
            }
        }
    }
}
