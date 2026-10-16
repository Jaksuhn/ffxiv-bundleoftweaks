namespace ComplexTweaks.TweakSystem;

public abstract class ARTweak<T> : Tweak<T> where T : new() {
    public ARTweak() : base() => AutoRetainer = new(Name);

    public AutoRetainerIPC AutoRetainer { get; set; }

    public abstract void OnCharacterPostProcessStep();
    public abstract void OnCharacterReadyToPostProcess();

    public override void OnEnable() {
        AutoRetainer.OnCharacterPostprocessStep += OnCharacterPostProcessStep;
        AutoRetainer.OnCharacterReadyToPostProcess += OnCharacterReadyToPostProcess;
        base.OnEnable();
    }

    public override void OnDisable() {
        AutoRetainer.OnCharacterPostprocessStep -= OnCharacterPostProcessStep;
        AutoRetainer.OnCharacterReadyToPostProcess -= OnCharacterReadyToPostProcess;
        base.OnDisable();
    }

    public override void Dispose() {
        lock (Lifecycle) {
            AutoRetainer.Dispose();
            base.Dispose();
        }
    }
}
