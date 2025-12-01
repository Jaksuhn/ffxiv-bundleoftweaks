using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using ECommons.Automation;
using ECommons.GameFunctions;
using ECommons.ImGuiMethods;
using Dalamud.Bindings.ImGui;
using System.Threading.Tasks;

namespace ComplexTweaks.Tweaks;

public class GMAlertConfiguration {
    [BoolConfig] public bool Toast = false;
    [BoolConfig] public bool ChatMessage = false;
    [BoolConfig] public bool Sound = false;
    [IntConfig(DependsOn = "Sound", DefaultValue = 3)] public int BeepCount = 3;
    [IntConfig(DependsOn = "Sound", DefaultValue = 250)] public int BeepDuration = 250;
    [IntConfig(DependsOn = "Sound", DefaultValue = 900)] public int BeepFrequency = 900;
    [BoolConfig] public bool KillGame = false;
    public HashSet<string> Commands = [];
}

[Tweak]
public class GMAlert : Tweak<GMAlertConfiguration> {
    public override string Name => "GM Alert";
    public override string Description => "Various alerts for when a GM is nearby.";

    public override void Enable() {
        Svc.Framework.Update += OnUpdate;
    }

    public override void Disable() {
        Svc.Framework.Update -= OnUpdate;
    }

    private string _cmd = string.Empty;
    public override void DrawConfig() {
        ImGui.DrawSection("Upon GM Appearance");

        ImGui.Checkbox("Send Toast Alert", ref Config.Toast);
        ImGui.Checkbox("Send Chat Alert", ref Config.ChatMessage);
        ImGui.Checkbox("Send Sound Alert", ref Config.Sound);
        if (Config.Sound) {
            ImGui.SameLine();
            if (ImGui.IconButton(FontAwesomeIcon.Music, "##SoundPreview", "Preview Beeps"))
                for (var i = 0; i < Config.BeepCount; i++)
                    Task.Run(() => Console.Beep(Config.BeepFrequency, Config.BeepDuration));

            ImGui.Indent();
            ImGui.SliderInt("Beep Count", ref Config.BeepCount, 1, 100);
            ImGui.SameLine();
            ImGui.ResetButton(ref Config.BeepCount, 3);

            ImGui.SliderInt("Beep Duration", ref Config.BeepDuration, 1, 1000);
            ImGui.SameLine();
            ImGui.ResetButton(ref Config.BeepDuration, 250);

            ImGui.SliderInt("Beep Frequency", ref Config.BeepFrequency, 100, 5000);
            ImGui.SameLine();
            ImGui.ResetButton(ref Config.BeepFrequency, 900);
            ImGui.Unindent();
        }

        ImGui.Checkbox("Kill Game", ref Config.KillGame);

        ImGui.TextColoredWrapped(Colors.Gold, "Execute Commands");
        if (ImGui.InputText($"##Commands", ref _cmd, 50, ImGuiInputTextFlags.EnterReturnsTrue))
            Config.Commands.Add(_cmd.StartsWith('/') ? _cmd : $"/{_cmd}");

        foreach (var cmd in Config.Commands) {
            ImGuiEx.TextV(cmd);
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(cmd, FontAwesomeIcon.Trash))
                Config.Commands.Remove(cmd);
        }
    }

    public bool sent;
    private unsafe void OnUpdate(IFramework framework) {
        if (!Player.Available) return;

        var gms = Svc.Objects.OfType<IPlayerCharacter>().Where(pc => pc.EntityId != 0xE000000 && pc.Character()->CharacterData.OnlineStatus is <= 3 and > 0);

        if (!gms.Any()) {
            sent = false;
            return;
        }

        if (sent) return;

        foreach (var player in gms) {
            if (Config.Toast)
                Svc.Toasts.ShowNormal($"GM {player.Name} is nearby!");
            if (Config.ChatMessage)
                ModuleMessage($"GM {player.Name} is nearby!");
            if (Config.Sound)
                for (var i = 0; i < Config.BeepCount; i++)
                    Task.Run(() => Console.Beep(Config.BeepFrequency, Config.BeepDuration));
        }
        sent = true;

        if (Config.Commands.Count > 0)
            foreach (var cmd in Config.Commands)
                Chat.ExecuteCommand(cmd);
        if (Config.KillGame)
            Chat.ExecuteCommand("/xlkill");
    }
}
