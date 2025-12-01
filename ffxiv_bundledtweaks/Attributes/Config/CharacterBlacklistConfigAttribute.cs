using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using System.Reflection;

namespace ComplexTweaks.Attributes.Config;

[AttributeUsage(AttributeTargets.Field)]
public class CharacterBlacklistConfigAttribute : BaseConfigAttribute {
    public override void Draw(Tweak tweak, object config, FieldInfo fieldInfo) {
        var value = (List<ulong>)fieldInfo.GetValue(config)!;
        var attr = fieldInfo.GetCustomAttribute<BaseConfigAttribute>();

        ImGui.DrawSection($"Character Blacklist ({value.Count} excluded)");

        var currentCharacterId = Svc.ClientState.LocalContentId;
        var isExcluded = value.Contains(currentCharacterId);

        if (!isExcluded) {
            if (ImGui.IconButton(FontAwesomeIcon.UserMinus, "minus", "Exclude Current Character")) {
                value.Add(currentCharacterId);
                OnChangeInternal(tweak, fieldInfo);
            }
        }
        else {
            if (ImGui.IconButton(FontAwesomeIcon.UserPlus, "plus", "Remove Character Exclusion")) {
                value.Remove(currentCharacterId);
                OnChangeInternal(tweak, fieldInfo);
            }
        }

        ImGui.SameLine();
        if (ImGui.IconButton(FontAwesomeIcon.Trash, "trash", "Clear All")) {
            value.Clear();
            OnChangeInternal(tweak, fieldInfo);
        }

        if (!attr?.Description.IsNullOrEmpty() ?? false)
            ImGui.TextColoredWrapped(Colors.Grey, attr!.Description);
    }
}
