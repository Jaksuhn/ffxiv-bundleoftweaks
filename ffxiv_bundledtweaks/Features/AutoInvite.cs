using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game.Group;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ComplexTweaks.Features;

public class AutoInviteConfiguration
{
    [StringConfig(IsRegex = nameof(IsRegex))] public string Pattern = string.Empty;
    [BoolConfig] public bool IsRegex = false;
    [BoolConfig] public bool TurnOffOnceFull = true;
    [IntConfig] public int DelayMs = 250;
    [ChatChannelConfig(Mode = ChatChannelConfigAttribute.ChatChannelMode.PlayerChat)] public List<XivChatType> Channels = [];
}

[Tweak]
public class AutoInvite : Tweak<AutoInviteConfiguration>
{
    // Based on https://github.com/Bluefissure/Inviter but without all the hooks
    public override string Name => "Auto Inviter";
    public override string Description => "Auto invite people to your party based on a chat message.";

    private unsafe EzHook<RaptureLogModule.Delegates.AddMsgSourceEntry>? Hook;
    private bool On
    {
        get;
        set
        {
            Svc.Toasts.ShowNormal($"Auto Inviter {(value ? "enabled" : "disabled")}");
            _attempts = 0;
            field = value;
        }
    }
    private int _attempts = 0;

    public override unsafe void Enable()
    {
        Hook = new((nint)RaptureLogModule.MemberFunctionPointers.AddMsgSourceEntry, Detour, false);
        Hook.Enable();
    }

    public override unsafe void Disable()
    {
        Hook?.Disable();
        Hook = null;
    }

    private unsafe void Detour(RaptureLogModule* thisPtr, ulong contentId, ulong accountId, int messageIndex, ushort worldId, ushort chatType)
    {
        Hook?.Original(thisPtr, contentId, accountId, messageIndex, worldId, chatType);

        if (!On) return;

        if (Config.Pattern.IsNullOrEmpty())
        {
            Log("Skipping invite: no pattern.");
            return;
        }

        if (!Config.Channels.Contains((XivChatType)chatType))
        {
            Log("Skipping invite: not in valid chat channel.");
            return;
        }

        if (GroupManager.Instance()->GetGroup()->MemberCount >= 8)
        {
            Log("Skipping invite: party full.");
            if (Config.TurnOffOnceFull) On = false;
            return;
        }

        if (GroupManager.Instance()->GetGroup()->MemberCount > 0 && !GroupManager.Instance()->MainGroup.IsEntityIdPartyLeader(Player.Object.EntityId))
        {
            Log("Skipping invite: not party leader.");
            return;
        }

        if (!RaptureLogModule.Instance()->GetLogMessageDetail(messageIndex, out var sender, out var rawMessage, out _, out _, out _, out _))
        {
            Log("Skipping invite: unable to get message detail.");
            return;
        }

        if (Svc.Party.Any(p => p.ContentId == (long)contentId))
        {
            Log("Skipping invite: already in party.");
            return;
        }

        var message = SeString.Parse(rawMessage.AsSpan()).TextValue;
        var matches = false;

        if (Config.IsRegex)
        {
            try
            {
                matches = Regex.Match(message, Config.Pattern, RegexOptions.IgnoreCase).Success;
            }
            catch (Exception ex)
            {
                Warning(ex, "Skipping invite: invalid regex pattern.");
                return;
            }
        }
        else
            matches = message.Contains(Config.Pattern, StringComparison.OrdinalIgnoreCase);

        if (matches)
        {
            if (SeString.Parse(sender.AsSpan()).Payloads.FirstOrDefault(p => p is PlayerPayload) is PlayerPayload playerPayload)
            {
                Log($"Attempting to invite {playerPayload.PlayerName}");
                if (InInvitableInstance())
                {
                    Log($"Inviting {playerPayload.PlayerName} to instanced party.");
                    TaskManager.EnqueueDelay(Config.DelayMs);
                    TaskManager.Enqueue(() => InfoProxyPartyInvite.Instance()->InviteToPartyInInstanceByContentId(contentId));
                }
                else
                {
                    Log($"Inviting {playerPayload.PlayerName} to non-instanced party.");
                    TaskManager.EnqueueDelay(Config.DelayMs);
                    TaskManager.Enqueue(() =>
                    {
                        fixed (byte* namePtr = ToTerminatedBytes(playerPayload.PlayerName))
                            InfoProxyPartyInvite.Instance()->InviteToParty(contentId, namePtr, (ushort)playerPayload.World.RowId);
                    });
                }

                if (_attempts > 0)
                {
                    _attempts--;
                    Log($"Invites remaining: {_attempts}");
                    if (_attempts == 0)
                        On = false;
                }
            }
        }
    }

    [CommandHandler("/cinvite", "Toggle Auto Inviter", subCommandStrings: ["[0-9]s|Enable for specified seconds", "[0-9]a|Enable for specified number of invites"])]
    private void MainCommand(string command, string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            On ^= true;
            return;
        }

        if (arguments.EndsWith("a", StringComparison.OrdinalIgnoreCase) && int.TryParse(arguments[..^1], out var attempts))
        {
            On = true;
            _attempts = attempts;
            Log($"Enabled for {attempts} invites.");
            return;
        }

        if (arguments.EndsWith("s", StringComparison.OrdinalIgnoreCase) && int.TryParse(arguments[..^1], out var seconds))
        {
            On = true;
            Log($"Enabled for {seconds} seconds.");
            Task.Run(async () =>
            {
                await Task.Delay(seconds * 1000);
                On = false;
            });
            return;
        }
    }

    private bool InInvitableInstance()
        => Svc.Condition[ConditionFlag.BoundByDuty56] && GetRow<TerritoryType>(Player.Territory)?.TerritoryIntendedUse.RowId is 41 or 47 or 48 or 52 or 53 or 61;

    private byte[] ToTerminatedBytes(string s)
    {
        var utf8 = Encoding.UTF8;
        var bytes = new byte[utf8.GetByteCount(s) + 1];
        utf8.GetBytes(s, 0, s.Length, bytes, 0);
        bytes[^1] = 0;
        return bytes;
    }
}
