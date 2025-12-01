using Dalamud.Game.ClientState.Fates;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;

namespace Automaton.Events;

public class FateEventArgs : EventArgs
{
    public ushort FateId { get; set; }
    public IFate? Fate { get; set; }
}

public class FateEventTracker : ITweakEvent
{
    public TweakEvent[] Events => [TweakEvent.FateJoined, TweakEvent.FateLeft];

    public void RegisterHandlers(TweakEventManager manager)
    {
        manager.RegisterFrameworkUpdateHandler(TweakEvent.FateJoined, OnFateFrameworkUpdate);
        manager.RegisterFrameworkUpdateHandler(TweakEvent.FateLeft, OnFateFrameworkUpdate);
    }

    private ushort? _currentFateId;
    private unsafe bool IsGrounded => Player.Character->MovementState is MovementStateOptions.Normal && !Player.Mounted;

    private unsafe void OnFateFrameworkUpdate(IFramework framework)
    {
        if (!Player.Available) return;

        var fateManager = FateManager.Instance();
        if (fateManager == null) return;

        var currentFate = fateManager->CurrentFate;
        var currentFateId = currentFate != null ? currentFate->FateId : (ushort)0;

        // only consider it joining when grounded
        if (currentFateId != 0 && _currentFateId != currentFateId && IsGrounded) // joined
        {
            _currentFateId = currentFateId;
            var fate = Svc.Fates.CreateFateReference((nint)currentFate);
            if (fate != null)
            {
                Service.TweakEventManager.Invoke(TweakEvent.FateJoined, typeof(FateEventTracker), new FateEventArgs
                {
                    FateId = currentFateId,
                    Fate = fate
                });
            }
        }

        else if (currentFateId == 0 && _currentFateId.HasValue && _currentFateId.Value != 0) // left
        {
            var previousFateId = _currentFateId.Value;
            _currentFateId = null;
            Service.TweakEventManager.Invoke(TweakEvent.FateLeft, typeof(FateEventTracker), new FateEventArgs
            {
                FateId = previousFateId,
                Fate = null
            });
        }
    }
}

