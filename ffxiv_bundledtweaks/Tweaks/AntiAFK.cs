namespace ComplexTweaks.Tweaks;

[Tweak]
internal class AntiAFK : Tweak
{
    public override string Name => "Anti-AFK";
    public override string Description => "Prevents being kicked for being AFK.";

    public override void Enable() => Svc.Framework.Update += OnUpdate;
    public override void Disable() => Svc.Framework.Update -= OnUpdate;

    private void OnUpdate(IFramework framework) => PlayerEx.ResetTimers();
}
