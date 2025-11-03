using Dalamud.Game.Text;
using Dalamud.Bindings.ImGui;
using System.Reflection;

namespace ComplexTweaks.FeaturesSetup.Attributes;

[AttributeUsage(AttributeTargets.Field)]
public class ChatChannelConfigAttribute : BaseConfigAttribute
{
    public ChatChannelMode Mode { get; set; } = ChatChannelMode.All;
    public XivChatType[]? CustomChannels { get; set; }

    private static readonly XivChatType[] PlayerChatChannels =
    [
        XivChatType.Say,
        XivChatType.Shout,
        XivChatType.TellIncoming,
        XivChatType.Party,
        XivChatType.Alliance,
        XivChatType.Ls1,
        XivChatType.Ls2,
        XivChatType.Ls3,
        XivChatType.Ls4,
        XivChatType.Ls5,
        XivChatType.Ls6,
        XivChatType.Ls7,
        XivChatType.Ls8,
        XivChatType.FreeCompany,
        XivChatType.NoviceNetwork,
        XivChatType.Yell,
        XivChatType.CrossParty,
        XivChatType.PvPTeam,
        XivChatType.CrossLinkShell1,
        XivChatType.CrossLinkShell2,
        XivChatType.CrossLinkShell3,
        XivChatType.CrossLinkShell4,
        XivChatType.CrossLinkShell5,
        XivChatType.CrossLinkShell6,
        XivChatType.CrossLinkShell7,
        XivChatType.CrossLinkShell8,
    ];

    public enum ChatChannelMode
    {
        All,
        PlayerChat,
        Custom
    }

    public override void Draw(Tweak tweak, object config, FieldInfo fieldInfo)
    {
        var value = (List<XivChatType>)fieldInfo.GetValue(config)!;
        var attr = fieldInfo.GetCustomAttribute<BaseConfigAttribute>();

        ImGui.TextUnformatted(fieldInfo.Name.SplitWords());

        using var indent = ImGuiX.ConfigIndent();

        var chatTypes = Mode switch
        {
            ChatChannelMode.All => [.. Enum.GetValues<XivChatType>()],
            ChatChannelMode.PlayerChat => [.. PlayerChatChannels],
            ChatChannelMode.Custom => CustomChannels?.ToList() ?? [],
            _ => throw new ArgumentOutOfRangeException($"Invalid {nameof(ChatChannelMode)}")
        };

        var style = ImGui.GetStyle();
        var checkboxWidth = ImGui.GetFrameHeight();
        var spacing = style.ItemSpacing.X;
        var availableWidth = ImGui.GetContentRegionAvail().X;

        // Calculate max width needed for any chat type name
        var maxTextWidth = chatTypes.Max(ct => ImGui.CalcTextSize(ct.ToString()).X);
        var itemWidth = checkboxWidth + spacing + maxTextWidth;

        // Calculate how many columns we can fit, with a minimum width per column
        var minColumnWidth = itemWidth + spacing * 2; // Add extra spacing for padding
        var columns = Math.Max(1, (int)(availableWidth / minColumnWidth));
        var rows = (int)Math.Ceiling(chatTypes.Count / (float)columns);

        // Calculate actual column width to fill available space
        var columnWidth = availableWidth / columns;

        if (ImGui.BeginTable("ChatChannels", columns, ImGuiTableFlags.NoBordersInBody))
        {
            for (var row = 0; row < rows; row++)
            {
                ImGui.TableNextRow();
                for (var col = 0; col < columns; col++)
                {
                    var index = row * columns + col;
                    if (index >= chatTypes.Count) break;

                    ImGui.TableNextColumn();
                    var chatType = chatTypes[index];
                    var isSelected = value.Contains(chatType);

                    if (ImGui.Checkbox($"{chatType}##{chatType}", ref isSelected))
                    {
                        if (isSelected)
                            value.Add(chatType);
                        else
                            value.Remove(chatType);
                        OnChangeInternal(tweak, fieldInfo);
                    }
                }
            }
            ImGui.EndTable();
        }

        if (!attr?.Description.IsNullOrEmpty() ?? false)
            ImGui.TextColoredWrapped(Colors.Grey, attr!.Description);
    }
}
