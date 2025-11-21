namespace ComplexTweaks.Tweaks;

[Tweak]
internal class AutoSnipeQuests : Tweak
{
    public override string Name => "Sniper no sniping";
    public override string Description => "Automatically completes snipe quests.";

    private readonly Memory.SnipeQuestSequence SnipeQuestSequence = new();
    public override void Enable() => SnipeQuestSequence.SnipeHook.Enable();
    public override void Disable() => SnipeQuestSequence.SnipeHook.Disable();
}
