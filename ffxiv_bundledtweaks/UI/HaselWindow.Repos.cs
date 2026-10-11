using clib.Ui;
using ComplexTweaks.Configuration;
using ComplexTweaks.TweakSystem.Packs;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;

namespace ComplexTweaks.UI;

public partial class HaselWindow {
    private bool _openRepos;
    private string _repoUrl = "";

    private void DrawReposModal() {
        if (_openRepos) {
            ImGui.OpenPopup("Settings###Repos");
            _openRepos = false;
        }

        ImGui.SetNextWindowSize(new Vector2(560, 420), ImGuiCond.Appearing);
        using var modal = ImRaii.PopupModal("Settings###Repos");
        if (!modal)
            return;

        ImGui.Text("Tweak repos");
        ImGui.SetNextItemWidth(-80);
        ImGui.InputText("##RepoUrl", ref _repoUrl, 300);
        ImGui.SameLine();
        if (ImGui.Button("Add"))
            AddRepo();

        ImGui.Separator();

        var repos = ConfigService.Get().Config.Repos;
        if (repos.Count == 0)
            ImGui.TextColoredWrapped(Colors.Grey, "No repos added.");

        foreach (var repo in repos) {
            using var id = ImRaii.PushId(repo.Id);
            ImGui.Text(repo.Id);
            ImGui.SameLine();
            ImGui.TextColoredWrapped(Colors.Grey, repo.InstalledTag ?? "not installed");

            if (!repo.LastError.IsEmpty)
                ImGui.TextColoredWrapped(Color.Red, repo.LastError);

            var busy = TweakPackService.Get().IsUpdating(repo);
            using (ImRaii.Disabled(busy)) {
                if (ImGui.Button("Update"))
                    TweakPackService.Get().QueueUpdate(repo);
                ImGui.SameLine();
                if (ImGui.Button("Reload"))
                    TweakPackService.Get().Reload(repo);
                ImGui.SameLine();
                if (ImGui.Button("Remove")) {
                    TweakPackService.Get().Remove(repo);
                }
            }
        }

        ImGui.Separator();
        var closeWidth = ImGui.CalcTextSize("Close").X + ImGui.GetStyle().FramePadding.X * 2;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - closeWidth) * 0.5f);
        if (ImGui.Button("Close"))
            ImGui.CloseCurrentPopup();
    }

    private void AddRepo() {
        if (!GitHubPackClient.TryParse(_repoUrl, out var owner, out var name)) {
            return;
        }

        TweakPackService.Get().Add(new TweakRepo {
            Url = _repoUrl.Trim(),
            Owner = owner,
            Name = name,
        });
        _repoUrl = "";
    }
}
