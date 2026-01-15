using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using ECommons.ImGuiMethods;

namespace ComplexTweaks.UI;

public partial class HaselWindow : Window {
    // Style from HaselTweaks
    // https://github.com/Haselnussbomber/HaselTweaks
    public HaselWindow() : base($"{Name} v{P.Version.ToString(2)}###{nameof(HaselWindow)}") {
        Size = new(SidebarWidth * 3.5f + ImGui.GetStyle().ItemSpacing.X + ImGui.GetStyle().FramePadding.X * 2, 500);
        Flags |= ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoSavedSettings;
        AllowClickthrough = false;
        AllowPinning = false;
    }

    private const uint SidebarWidth = 250;

    private string _selectedTweak = string.Empty;
    private string? _splashText = null;

    private Tweak? SelectedTweak => Plugin.Tweaks.FirstOrDefault(t => t.Name == _selectedTweak);

    public override void OnClose() {
        _splashText = null;
        AsciiSplash.Reset();
    }

    public override void Draw() {
        DrawSidebar();
        ImGui.SameLine();
        DrawConfig();
    }

    private void DrawSidebar() {
        var scale = ImGuiHelpers.GlobalScale;
        using var child = ImRaii.Child("##Sidebar", new Vector2(SidebarWidth * scale, -1), true);
        if (!child.Success)
            return;

        using var table = ImRaii.Table("##SidebarTable", 2, ImGuiTableFlags.NoSavedSettings);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Checkbox", ImGuiTableColumnFlags.WidthFixed);
        ImGui.TableSetupColumn("Tweak Name", ImGuiTableColumnFlags.WidthStretch);

        foreach (var tweak in Plugin.Tweaks.Where(t => !t.Disabled && (!t.IsDebug || C.ShowDebug)).OrderBy(t => t.Name)) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            var enabled = tweak.Enabled;
            var fixY = false;

            if (!tweak.Ready || tweak.Outdated) {
                var startPos = ImGui.GetCursorPos();
                var drawList = ImGui.GetWindowDrawList();
                var pos = ImGui.GetWindowPos() + startPos - new Vector2(0, ImGui.GetScrollY());
                var frameHeight = ImGui.GetFrameHeight();

                var size = new Vector2(frameHeight);
                ImGui.SetCursorPos(startPos);
                ImGui.Dummy(size);

                if (ImGui.IsItemHovered()) {
                    var (status, color) = GetTweakStatus(tweak);
                    using var tooltip = ImRaii.Tooltip();
                    if (tooltip.Success)
                        ImGuiEx.Text((uint)color, status);
                }

                drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ImGuiCol.FrameBg), 3f, ImDrawFlags.RoundCornersAll);

                var pad = frameHeight / 4f;
                pos += new Vector2(pad);
                size -= new Vector2(pad) * 2;

                drawList.PathLineTo(pos);
                drawList.PathLineTo(pos + size);
                drawList.PathStroke(EzColor.RedBright, ImDrawFlags.None, frameHeight / 5f * 0.5f);

                drawList.PathLineTo(pos + new Vector2(0, size.Y));
                drawList.PathLineTo(pos + new Vector2(size.X, 0));
                drawList.PathStroke(EzColor.RedBright, ImDrawFlags.None, frameHeight / 5f * 0.5f);

                fixY = true;
            }
            else {
                ImGuiEx.CollectionCheckbox($"##Enabled_{tweak.InternalName}", tweak.InternalName, C.EnabledTweaks);
            }

            ImGui.TableNextColumn();

            if (fixY)
                ImGuiEx.PushCursorY(3); // if i only knew why this happens

            using var colour = ImRaii.PushColor(ImGuiCol.Text, !tweak.Ready || tweak.Outdated ? EzColor.RedBright : !enabled ? (uint)Colors.Grey : ImGui.GetColorU32(ImGuiCol.Text), !tweak.Ready || tweak.Outdated || !enabled);

            if (ImGui.Selectable($"{tweak.Name}##Selectable_{tweak.Name}", _selectedTweak == tweak.Name))
                _selectedTweak = _selectedTweak != tweak.Name ? tweak.Name : string.Empty;
        }
    }

    private void DrawConfig() {
        using var child = ImRaii.Child("##Config", new Vector2(-1), true);
        if (!child.Success)
            return;

        var tweak = SelectedTweak;
        if (tweak == null) {
            DrawSplash();
            DrawBottomBar();
            return;
        }

        using var id = ImRaii.PushId(tweak.Name);

        ImGuiEx.Text((uint)Colors.Gold, tweak.Name);

        var (status, color) = GetTweakStatus(tweak);

        var posX = ImGui.GetCursorPosX();
        var windowX = ImGui.GetContentRegionAvail().X;
        var textSize = ImGui.CalcTextSize(status);

        ImGui.SameLine(windowX - textSize.X);

        ImGuiEx.Text(color.Vector4, status);

        if (tweak.DisabledReason is { } reason) {
            ImGui.TextColoredWrapped(Colors.Grey2, reason);
            return;
        }
        else {
            if (!string.IsNullOrEmpty(tweak.Description)) {
                ImGui.DrawPaddedSeparator();
                ImGui.TextColoredWrapped(Colors.Grey2, tweak.Description);
            }
        }

        if (tweak.Requirements.Any(r => !r.IsLoaded)) {
            ImGui.DrawSection("Required Dependencies");
            ImGui.Icon(60074, 24);
            ImGui.SameLine();
            ImGuiEx.TextV(Colors.Grey2, $"Missing {tweak.Requirements.Count(r => !r.IsLoaded)} of the required plugins for this feature to work:");
            foreach (var entry in tweak.Requirements.Where(r => !r.IsLoaded)) {
                ImGui.TextColoredWrapped(Colors.Grey2, $"{entry.Name}:");
                ImGui.SameLine();
                ImGuiEx.TextCopy(entry.Repo);
            }
        }

        if (!tweak.MeetsClientStructsRequirements()) {
            ImGui.DrawSection("Invalid ClientStructs version");
            ImGui.Icon(60074, 24);
            ImGui.SameLine();
            ImGuiEx.TextV(Colors.Grey2, $"[{Svc.PluginInterface.ClientStructsVersion}] not in tweak bounds [{tweak.RequiredClientStructsVersion.Min}/{tweak.RequiredClientStructsVersion.Max}]. Wait for a Dalamud update.");
        }

        if (tweak.IncompatibilityWarnings.Any(entry => entry.IsLoaded)) {
            ImGui.DrawSection("Incompatibility Warning");
            ImGui.Icon(60073, 24);
            ImGui.SameLine();
            var cursorPosX = ImGui.GetCursorPosX();

            static string getConfigName(string tweakName, string configName) => $"{tweakName}: {configName}";

            if (tweak.IncompatibilityWarnings.Length == 1) {
                var entry = tweak.IncompatibilityWarnings[0];
                var pluginName = $"{entry.InternalName}";

                if (entry.IsLoaded) {
                    switch (entry.ConfigNames.Length) {
                        case 0:
                            ImGui.TextColoredWrapped(Colors.Grey2, $"In order for this tweak to work properly, please make sure {pluginName} is disabled.");
                            break;
                        case 1:
                            var configName = getConfigName(entry.InternalName, entry.ConfigNames[0]);
                            ImGui.TextColoredWrapped(Colors.Grey2, $"In order for this tweak to work properly, please make sure {configName} is disabled in {pluginName}.");
                            break;
                        case > 1:
                            var configNames = entry.ConfigNames.Select((configName) => $"{configName}");
                            ImGui.TextColoredWrapped(Colors.Grey2, $"In order for this tweak to work properly, please make sure {pluginName} is disabled." + $"\n - {string.Join("\n- ", configNames)}");
                            break;
                    }
                }
            }
            else if (tweak.IncompatibilityWarnings.Length > 1) {
                ImGui.TextColoredWrapped(Colors.Grey2, "In order for this tweak to work properly, please make sure");

                foreach (var entry in tweak.IncompatibilityWarnings.Where(entry => entry.IsLoaded)) {
                    var pluginName = $"{entry.InternalName}";

                    if (entry.ConfigNames.Length == 0) {
                        ImGui.SetCursorPosX(cursorPosX);
                        ImGui.TextColoredWrapped(Colors.Grey2, $"{pluginName} is disabled");
                    }
                    else if (entry.ConfigNames.Length == 1) {
                        ImGui.SetCursorPosX(cursorPosX);
                        var configName = $"HaselTweaks.Config.IncompatibilityWarning.Plugin.{entry.InternalName}.Config.{entry.ConfigNames[0]}";
                        ImGui.TextColoredWrapped(Colors.Grey2, $"{configName} is disabled in {pluginName}");
                    }
                    else if (entry.ConfigNames.Length > 1) {
                        ImGui.SetCursorPosX(cursorPosX);
                        var configNames = entry.ConfigNames.Select((configName) => $"{configName}");
                        ImGui.TextColoredWrapped(Colors.Grey2, ("{pluginName} is disabled", pluginName) + $"\n    - {string.Join("\n    - ", configNames)}");
                    }
                }
            }
        }

        tweak.DrawConfig();
    }

    private static (string, EzColor) GetTweakStatus(Tweak tweak) {
        var status = "???";
        var color = Colors.Grey3;

        if (tweak.Outdated) {
            status = "Outdated";
            color = EzColor.RedBright;
        }
        else if (!tweak.Ready) {
            status = "Initialization Failed";
            color = EzColor.RedBright;
        }
        else if (tweak.Enabled) {
            status = "Enabled";
            color = EzColor.GreenBright;
        }
        else if (!tweak.Enabled) {
            status = "Disabled";
        }

        return (status, color);
    }

    private static readonly string[] SplashTexts =
    [
        "Some animals were harmed in the making of this plugin.",
        "100% vegan",
        "Welcome to croizat's bundled tweaks",
        "Thanks for using complex bundled tweaks",
        "My favourite feature is the hunt relayer",
        "Does anyone actually know what CBT stands for?",
        "Also try Silksong!",
        "I can just put any message in here",
        "It's like those other tweak plugins, but worse!",
        "Made with love. Love is an alternative name for MSG",
        "Now in Technicolor!",
        "~~Blue text~~",
        "Interrobang!",
    ];

    private void DrawSplash() {
        _splashText ??= SplashTexts[Random.Shared.Next(SplashTexts.Length)];
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetColumnWidth() * 0.5f - ImGui.CalcTextSize(_splashText).X * 0.5f);
        ImGui.FlashText(_splashText, Colors.Gold, ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg], 2);

        AsciiSplash.Draw(80);
    }

    private void DrawBottomBar() {
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(0, ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeight()));
        ImGui.DrawLink("GitHub", "GitHub", "https://github.com/Jaksuhn/ffxiv-bundleoftweaks");
        ImGui.SameLine();
        ImGui.TextUnformatted("•");
        ImGui.SameLine();
        ImGui.DrawLink("Ko-fi", "Ko-fi", "https://ko-fi.com/croizat");

        if (P.Version.ToString(2).Length > 1) {
            ImGui.SetCursorPos(ImGui.GetCursorPos() + ImGui.GetContentRegionAvail() - ImGui.CalcTextSize($"v{P.Version.ToString(2)}"));
            ImGui.TextUnformatted($"v{P.Version.ToString(2)}");
        }
    }
}
