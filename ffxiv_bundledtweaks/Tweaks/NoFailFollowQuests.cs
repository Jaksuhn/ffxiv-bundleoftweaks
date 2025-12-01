namespace ComplexTweaks.Tweaks;

[Tweak(debug: true)]
public partial class NoFailFollowQuests : Tweak {
    public override string Name => "No Fail Follow Quests";
    public override string Description => "Prevents being seen during follow quests (you can still be too far away).";

    [SigHook(Memory.Signatures.FollowQuestRecast)]
    private bool FollowQuestRecast(nint a1, nint a2, nint a3, nint a4, nint a5, nint a6) => false;
}
