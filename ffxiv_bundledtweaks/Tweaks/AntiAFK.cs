using FFXIVClientStructs.FFXIV.Client.UI;

namespace ComplexTweaks.Tweaks;

[Tweak]
internal class AntiAFK : Tweak {
    public override string Name => "Anti-AFK";
    public override string Description => "Prevents being kicked for being AFK.";

    public override void Enable() => Svc.Framework.Update += OnUpdate;
    public override void Disable() => Svc.Framework.Update -= OnUpdate;

    private unsafe void OnUpdate(IFramework framework) => UIModule.Instance()->GetInputTimerModule()->ResetAfkTimer();
}
