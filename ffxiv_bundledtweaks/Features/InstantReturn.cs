namespace ComplexTweaks.Features;

[Tweak(debug: true)]
public unsafe class InstantReturn : Tweak
{
    public override string Name => "Quick Return";
    public override string Description => "Calls the return function directly";

    private readonly Memory.AgentReturn Return = new();
    public override void Enable() => Return.ReturnHook.Enable();
    public override void Disable() => Return.ReturnHook.Disable();
}
