/*using Automaton.Configuration;
using ECommons.Configuration;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace Automaton.Features;

[Tweak]
internal class DisableObjectCulling : Tweak
{
    public override string Name => "Disable Object Culling";
    public override string Description => "Prevents the game from hiding objects when your camera collides with them.";

    private readonly Memory.CameraObjectCulling CameraObjectCulling = new();

    public override void Enable()
    {
        CameraObjectCulling.ShouldDrawHook.Enable();
    }

    public override void Disable()
    {
        CameraObjectCulling.ShouldDrawHook.Disable();
    }
}*/
