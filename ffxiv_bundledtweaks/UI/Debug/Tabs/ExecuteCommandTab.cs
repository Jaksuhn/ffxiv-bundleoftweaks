using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using ECommons.ImGuiMethods;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace ComplexTweaks.UI.Debug.Tabs;

internal unsafe class ExecuteCommandTab : DebugTab {
    private CommandFlag _flag;
    private LocationCommandFlag _flag2;

    private int _ecId;
    private bool _useEcId;

    private int _elcId;
    private bool _useElcId;

    private readonly int[] ecParams = new int[4];
    private readonly int[] elcParams = new int[4];

    private enum ExecuteLocationSource {
        Player,
        MouseWorld,
        Target,
        Custom
    }

    private ExecuteLocationSource locationSource = ExecuteLocationSource.Player;
    private Vector3 customLocation = Vector3.Zero;

    public override void Draw() {
        DrawSimpleExecuteCommand();
        DrawLocationExecuteCommand();
    }

    private void DrawSimpleExecuteCommand() {
        using var _ = ImRaii.PushId("simple");
        using var table = ImRaii.Table("simpleExec", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg);
        if (!table) return;

        ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("value");

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("ExecuteCommand");
        ImGui.TableSetColumnIndex(1);

        ImGui.SetNextItemWidth(250f);
        ImGuiEx.EnumCombo("ExecuteCommandEnum", ref _flag);
        ImGui.SameLine();

        if (!_useEcId)
            _ecId = (int)_flag;

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("ID", ref _ecId);
        ImGui.SameLine();
        ImGui.Checkbox("Use raw ID", ref _useEcId);

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("Params");
        ImGui.TableSetColumnIndex(1);

        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("p1", ref ecParams[0]);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("p2", ref ecParams[1]);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("p3", ref ecParams[2]);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("p4", ref ecParams[3]);

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(string.Empty);
        ImGui.TableSetColumnIndex(1);

        if (ImGui.Button("Execute")) {
            var id = _useEcId ? _ecId : (int)_flag;
            GameMain.ExecuteCommand(id, ecParams[0], ecParams[1], ecParams[2], ecParams[3]);
        }
    }

    private void DrawLocationExecuteCommand() {
        using var _ = ImRaii.PushId("location");
        using var table = ImRaii.Table("locationExec", 2, ImGuiTableFlags.SizingStretchSame | ImGuiTableFlags.RowBg);
        if (!table) return;

        ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("value");

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("ExecuteLocationCommand");
        ImGui.TableSetColumnIndex(1);

        ImGui.SetNextItemWidth(250f);
        ImGuiEx.EnumCombo("ExecuteLocationCommandEnum", ref _flag2);
        ImGui.SameLine();

        if (!_useElcId)
            _elcId = (int)_flag2;

        ImGui.SetNextItemWidth(120f);
        ImGui.InputInt("ID##location", ref _elcId);
        ImGui.SameLine();
        ImGui.Checkbox("Use raw ID##location", ref _useElcId);

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("Location source");
        ImGui.TableSetColumnIndex(1);

        ImGui.SetNextItemWidth(180f);
        using (var combo = ImRaii.Combo("locationSource", locationSource.ToString())) {
            if (combo.Success) {
                foreach (var src in Enum.GetValues<ExecuteLocationSource>()) {
                    var selected = src == locationSource;
                    if (ImGui.Selectable(src.ToString(), selected))
                        locationSource = src;
                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }
            }
        }

        if (locationSource == ExecuteLocationSource.Custom) {
            ImGui.SameLine();
            ImGui.TextUnformatted("Custom XYZ");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80f);
            ImGui.InputFloat("X##loc", ref customLocation.X);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80f);
            ImGui.InputFloat("Y##loc", ref customLocation.Y);
            ImGui.SameLine();
            ImGui.SetNextItemWidth(80f);
            ImGui.InputFloat("Z##loc", ref customLocation.Z);
        }

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted("Params");
        ImGui.TableSetColumnIndex(1);

        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("p1", ref elcParams[0]);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("p2", ref elcParams[1]);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("p3", ref elcParams[2]);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(80f);
        ImGui.InputInt("p4", ref elcParams[3]);

        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(string.Empty);
        ImGui.TableSetColumnIndex(1);

        if (ImGui.Button("Execute")) {
            var id = _useElcId ? _elcId : (int)_flag2;
            var pos = GetExecuteLocation();
            GameMain.ExecuteLocationCommand(id, &pos, elcParams[0], elcParams[1], elcParams[2], elcParams[3]);
        }
    }

    private Vector3 GetExecuteLocation() {
        var pos = locationSource switch {
            ExecuteLocationSource.MouseWorld => Svc.GameGui.ScreenToWorld(ImGui.GetMousePos(), out var world) ? world : Player.Position,
            ExecuteLocationSource.Target => Svc.Targets.Target?.Position ?? Player.Position,
            ExecuteLocationSource.Custom => customLocation,
            _ => Player.Position,
        };
        return pos;
    }
}
