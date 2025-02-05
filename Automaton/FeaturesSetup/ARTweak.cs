
namespace Automaton.FeaturesSetup;
public abstract class ARTweak : Tweak
{
    public ARTweak() : base()
    {
        AutoRetainer = new(Name);
        AutoRetainer.OnCharacterPostprocessStep += OnCharacterPostProcessStep;
        AutoRetainer.OnCharacterReadyToPostProcess += OnCharacterReadyToPostProcess;
    }

    public AutoRetainerApi AutoRetainer { get; set; }

    public abstract void OnCharacterPostProcessStep();
    public abstract void OnCharacterReadyToPostProcess();

    public override void Disable()
    {
        AutoRetainer.OnCharacterPostprocessStep -= OnCharacterPostProcessStep;
        AutoRetainer.OnCharacterReadyToPostProcess -= OnCharacterReadyToPostProcess;
        base.Disable();
    }
}
