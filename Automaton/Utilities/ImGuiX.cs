using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using ECommons;
using ECommons.Configuration;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System.ComponentModel;
using System.Reflection;
using System.Threading.Tasks;

namespace Automaton.Utilities;
public static class ImGuiX
{
    public static void DrawPaddedSeparator()
    {
        var style = ImGui.GetStyle();
        ImGuiEx.PushCursorY(style.ItemSpacing.Y);
        ImGui.Separator();
        ImGuiEx.PushCursorY(style.ItemSpacing.Y - 1);
    }

    public static void DrawLink(string label, string title, string url)
    {
        ImGui.TextUnformatted(label);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

            using var tooltip = ImRaii.Tooltip();
            if (tooltip.Success)
            {
                ImGuiEx.Text(EzColor.White, title);

                var pos = ImGui.GetCursorPos();
                ImGui.GetWindowDrawList().AddText(
                    UiBuilder.IconFont, 12,
                    ImGui.GetWindowPos() + pos + new Vector2(2),
                    Colors.Grey,
                    FontAwesomeIcon.ExternalLinkAlt.ToIconString()
                );
                ImGui.SetCursorPos(pos + new Vector2(20, 0));
                ImGuiEx.Text((uint)Colors.Grey, url);
            }
        }

        if (ImGui.IsItemClicked())
        {
            Task.Run(() => Dalamud.Utility.Util.OpenLink(url));
        }
    }

    public static void DrawSection(string Label, bool PushDown = true, bool RespectUiTheme = false, uint UIColor = 1, bool drawSeparator = true)
    {
        var style = ImGui.GetStyle();

        // push down a bit
        if (PushDown)
            ImGuiEx.PushCursorY(style.ItemSpacing.Y * 2);

        var color = Colors.Gold;
        if (RespectUiTheme && Colors.IsLightTheme)
            color = EzColor.FromUiForeground(UIColor);

        ImGuiEx.Text(color, Label);

        if (drawSeparator)
        {
            // pull up the separator
            ImGuiEx.PushCursorY(-style.ItemSpacing.Y + 3);
            ImGui.Separator();
            ImGuiEx.PushCursorY(style.ItemSpacing.Y * 2 - 1);
        }
    }

    public static ImRaii.Indent ConfigIndent(bool enabled = true) => ImRaii.PushIndent(ImGui.GetFrameHeight() + ImGui.GetStyle().ItemSpacing.X / 2f, true, enabled);

    public static void Checkbox(string name, ref bool v)
    {
        if (ImGui.Checkbox(name, ref v))
            EzConfig.Save();
    }

    public static void Icon(FontAwesomeIcon icon, EzColor? col = null, string? tooltip = null)
    {
        using (col is { } c ? ImRaii.PushColor(ImGuiCol.Text, c.Vector4) : null)
        {
            using (ImRaii.PushFont(UiBuilder.IconFont))
                ImGui.TextUnformatted(icon.ToIconString());
        }
        if (tooltip is { } && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    public static void Icon(ushort iconID, int size) => Icon(Utils.GetIcon(iconID), size.Vec2());
    public static void Icon(ushort iconID, Vector2 size) => Icon(Utils.GetIcon(iconID), size);
    public static void Icon(uint iconID, int size) => Icon(Utils.GetIcon(iconID), size.Vec2());
    public static void Icon(uint iconID, Vector2 size) => Icon(Utils.GetIcon(iconID), size);
    public static void Icon(IDalamudTextureWrap? icon, Vector2 size)
    {
        if (icon != null)
            ImGui.Image(icon.Handle, size);
        else
            ImGui.Dummy(size);
    }

    public static float IconUnitHeight() => ImGuiHelpers.GetButtonSize(FontAwesomeIcon.Trash.ToIconString()).Y;
    public static float IconUnitWidth() => ImGuiHelpers.GetButtonSize(FontAwesomeIcon.Trash.ToIconString()).X;

    public static bool IconButton(FontAwesomeIcon icon, string key, string tooltip = "", Vector2 size = default, bool disabled = false, bool active = false)
    {
        using var iconFont = ImRaii.PushFont(UiBuilder.IconFont);
        if (!key.StartsWith("##")) key = "##" + key;

        var disposables = new List<IDisposable>();

        if (disabled)
        {
            disposables.Add(ImRaii.PushColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]));
            disposables.Add(ImRaii.PushColor(ImGuiCol.ButtonActive, ImGui.GetStyle().Colors[(int)ImGuiCol.Button]));
            disposables.Add(ImRaii.PushColor(ImGuiCol.ButtonHovered, ImGui.GetStyle().Colors[(int)ImGuiCol.Button]));
        }
        else if (active)
        {
            disposables.Add(ImRaii.PushColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]));
        }

        var pressed = ImGui.Button(icon.ToIconString() + key, size);

        foreach (var disposable in disposables)
            disposable.Dispose();

        iconFont?.Dispose();

        if (tooltip != string.Empty && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        return pressed;
    }

    public static void ResetButton<T>(ref T var, T value)
    {
        if (IconButton(FontAwesomeIcon.Undo, $"##FormatReset{var}", $"Reset To Default: {var}"))
            var = value;
    }

    // https://github.com/KazWolfe/CollectorsAnxiety/blob/bf48a4b0681e5f70fb67e3b1cb22b4565ecfcc02/CollectorsAnxiety/Util/ImGuiUtil.cs#L10
    public static void DrawProgressBar(int progress, int total, Vector4 colour)
    {
        try
        {
            using (ImRaii.Group())
            {
                var cursor = ImGui.GetCursorPos();
                var sizeVec = new Vector2(ImGui.GetContentRegionAvail().X - IconUnitWidth() - ImGui.GetStyle().WindowPadding.X * 2, IconUnitHeight());

                var percentage = progress / (float)total;
                var label = string.Format("{0:P} Complete ({1} / {2})", percentage, progress, total);
                var labelSize = ImGui.CalcTextSize(label);

                using var _ = ImRaii.PushColor(ImGuiCol.PlotHistogram, colour);
                ImGui.ProgressBar(percentage, sizeVec, "");

                ImGui.SetCursorPos(new Vector2(cursor.X + sizeVec.X - labelSize.X - 4, cursor.Y));
                ImGuiEx.TextV(label);
            }
        }
        catch (Exception e) { e.Log(); }
    }

    public static void PathfindButton(NavmeshIPC nav, Vector3 pos)
    {
        if (ImGuiComponents.IconButton($"###Pathfind{pos}", FontAwesomeIcon.Map))
        {
            if (!nav.IsRunning())
                nav.PathfindAndMoveTo(pos, Svc.Condition[ConditionFlag.InFlight]);
            else
                nav.Stop();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pathfind");
    }

    private static float startTime;
    public static void FlashText(string text, Vector4 colour1, Vector4 colour2, float duration)
    {
        var currentTime = (float)ImGui.GetTime();
        var elapsedTime = currentTime - startTime;

        var t = (float)Math.Sin(elapsedTime / duration * Math.PI * 2) * 0.5f + 0.5f;

        // Interpolate the color difference
        Vector4 interpolatedColor = new(
            colour1.X + t * (colour2.X - colour1.X),
            colour1.Y + t * (colour2.Y - colour1.Y),
            colour1.Z + t * (colour2.Z - colour1.Z),
            1.0f
        );

        using var _ = ImRaii.PushColor(ImGuiCol.Text, interpolatedColor);
        ImGui.TextUnformatted(text);

        if (elapsedTime >= duration)
            startTime = currentTime;
    }

    public static string EnumString(Enum v)
    {
        var name = v.ToString();
        return v.GetType().GetField(name)?.GetCustomAttribute<DescriptionAttribute>()?.Description ?? name;
    }

    public static bool Enum<T>(string label, ref T v) where T : Enum
    {
        var res = false;
        ImGui.SetNextItemWidth(200);
        using var combo = ImRaii.Combo(label, EnumString(v));
        if (!combo) return false;
        foreach (var opt in System.Enum.GetValues(v.GetType()))
        {
            if (ImGui.Selectable(EnumString((Enum)opt), opt.Equals(v)))
            {
                v = (T)opt;
                res = true;
            }
        }
        return res;
    }

    public static void DrawTableColumn(string name)
    {
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(name);
    }

    public static void FieldAndValue(string field, object value, bool? valueCondition = null)
    {
        using (var _ = ImRaii.PushColor(ImGuiCol.Text, (uint)Colors.Field))
            ImGui.TextUnformatted($"{field}:");
        ImGui.SameLine();
        using (var _ = ImRaii.PushColor(ImGuiCol.Text, EzColor.White.Vector4))
            ImGui.TextUnformatted($"{(valueCondition is { } condition && condition || valueCondition is not { } ? value : "N/A")}");
    }
}
