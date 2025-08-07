using Dalamud.Interface;
using Dalamud.Bindings.ImGui;
using System.Reflection;

namespace Automaton.FeaturesSetup.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class CharacterBlacklistConfigAttribute : BaseConfigAttribute
{
    public override void Draw(Tweak tweak, object config, FieldInfo fieldInfo)
    {
        var value = (List<ulong>)fieldInfo.GetValue(config)!;
        var attr = fieldInfo.GetCustomAttribute<BaseConfigAttribute>();

        ImGuiX.DrawSection($"Character Blacklist ({value.Count} excluded)");

        var currentCharacterId = Svc.ClientState.LocalContentId;
        var isExcluded = value.Contains(currentCharacterId);

        if (!isExcluded)
        {
            if (ImGuiX.IconButton(FontAwesomeIcon.UserMinus, "minus", "Exclude Current Character"))
            {
                value.Add(currentCharacterId);
                OnChangeInternal(tweak, fieldInfo);
            }
        }
        else
        {
            if (ImGuiX.IconButton(FontAwesomeIcon.UserPlus, "plus", "Remove Character Exclusion"))
            {
                value.Remove(currentCharacterId);
                OnChangeInternal(tweak, fieldInfo);
            }
        }

        ImGui.SameLine();
        if (ImGuiX.IconButton(FontAwesomeIcon.Trash, "trash", "Clear All"))
        {
            value.Clear();
            OnChangeInternal(tweak, fieldInfo);
        }

        if (!attr?.Description.IsNullOrEmpty() ?? false)
            ImGui.TextColoredWrapped(Colors.Grey, attr!.Description);
    }
}
