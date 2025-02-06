using Dalamud.Interface;
using Dalamud.Interface.Components;
using ECommons.Automation;
using ECommons.Events;
using ImGuiNET;

namespace Automaton.Features;
public class EnhancedLoginLogoutConfig
{
    public List<EnhancedLoginLogout.CharacterCommands> Chars = [];
    public bool RunCommandsWhenARIsActive = false;
}

[Tweak]
public class EnhancedLoginLogout : Tweak<EnhancedLoginLogoutConfig>
{
    // TODO: hook logout and run commands then too
    public override string Name => "Enhanced Login";
    public override string Description => "Additional options when logging in.";

    public class CharacterCommands
    {
        public ulong CID;
        public string Name = string.Empty;
        public List<string> LoginCommands = [];
        //public List<string> LogoutCommands = [];
    }

    public override void DrawConfig()
    {
        base.DrawConfig();

        ImGuiX.DrawSection("Login Commands");

        if (Service.AutoRetainerIPC.IsLoaded)
            ImGui.Checkbox("Run Commands if AutoRetainer is active", ref Config.RunCommandsWhenARIsActive);

        if (Config.Chars.All(c => c.CID != 0))
        {
            Config.Chars.Add(new CharacterCommands
            {
                CID = 0,
                Name = "Global",
            });
        }
        if (Config.Chars.All(c => c.CID != Player.CID) && !Player.Name.IsNullOrEmpty()) // there's a delay after getting a cid before you have a name
        {
            Config.Chars.Add(new CharacterCommands
            {
                CID = Player.CID,
                Name = Player.Name,
            });
        }
        Config.Chars.RemoveAll(c => c.LoginCommands.Count == 0 && c.CID != 0 && c.CID != Player.CID);

        foreach (var c in Config.Chars.OrderByDescending(x => x.Name == "Global"))
        {
            ImGuiX.DrawSection(c.Name, drawSeparator: false);
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) Config.Chars.Remove(c);

            foreach (var cmd in c.LoginCommands.ToList())
            {
                var tmp = cmd;
                if (ImGui.InputText($"##{c.CID}_{cmd}", ref tmp, 150))
                    c.LoginCommands[c.LoginCommands.IndexOf(cmd)] = ConvertToCommand(tmp);
                ImGui.SameLine();
                if (ImGuiComponents.IconButton($"{c.CID}_{cmd}", FontAwesomeIcon.Trash))
                    c.LoginCommands.Remove(cmd);
            }
            var newcmd = string.Empty;
            if (ImGui.InputText($"##{c.CID}_new", ref newcmd, 150, ImGuiInputTextFlags.EnterReturnsTrue))
                c.LoginCommands.Add(ConvertToCommand(newcmd));
        }
    }

    public override void Enable() => ProperOnLogin.RegisterInteractable(RunCommands);
#pragma warning disable CS0618 // Type or member is obsolete
    public override void Disable() => ProperOnLogin.Unregister(RunCommands);
#pragma warning restore CS0618 // Type or member is obsolete

    private string ConvertToCommand(string cmd) => cmd.StartsWith('/') ? cmd : $"/{cmd}";
    private void RunCommands()
    {
        if (Service.AutoRetainerIPC.IsLoaded && !Config.RunCommandsWhenARIsActive && (Service.AutoRetainerIPC.IsBusy() || Service.AutoRetainerIPC.GetMultiModeEnabled())) return;
        foreach (var chr in Config.Chars.Where(x => x.CID == 0 || x.CID == Player.CID).OrderByDescending(x => x.Name == "Global"))
            foreach (var cmd in chr.LoginCommands.Where(c => c.Length >= 3))
            {
                TaskManager.EnqueueDelay(250);
                TaskManager.Enqueue(() => Chat.Instance.SendMessage(cmd));
            }
    }
}
