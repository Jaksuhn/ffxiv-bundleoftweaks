namespace ComplexTweaks.TweakSystem;
public abstract class ARTweak : Tweak
{
    public ARTweak() : base() => AutoRetainer = new(Name);

    public AutoRetainerApi AutoRetainer { get; set; }

    public abstract void OnCharacterPostProcessStep();
    public abstract void OnCharacterReadyToPostProcess();

    public override void Enable()
    {
        AutoRetainer.OnCharacterPostprocessStep += OnCharacterPostProcessStep;
        AutoRetainer.OnCharacterReadyToPostProcess += OnCharacterReadyToPostProcess;
        base.Enable();
    }

    public override void Disable()
    {
        AutoRetainer.OnCharacterPostprocessStep -= OnCharacterPostProcessStep;
        AutoRetainer.OnCharacterReadyToPostProcess -= OnCharacterReadyToPostProcess;
        base.Disable();
    }

    public override void Dispose()
    {
        AutoRetainer.Dispose();
        base.Dispose();
    }
}
