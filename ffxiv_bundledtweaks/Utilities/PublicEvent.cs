using Dalamud.Memory;
using FFXIVClientStructs.FFXIV.Client.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Fate;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using FFXIVClientStructs.FFXIV.Client.Game.WKS;
using FateState = FFXIVClientStructs.FFXIV.Client.Game.Fate.FateState;

namespace ComplexTweaks.Utilities;

public enum FateType {
    Normal,
    DynamicEvent, // forays
    MechaEvent, // cosmic exploration
}

/// <summary>
/// Wrapper for all public event types (FATEs, Dynamic Events, Mecha Events)
/// </summary>
public unsafe class PublicEvent {
    public IntPtr Address { get; init; }
    public FateType FateType { get; init; }
    public uint Id { get; init; }

    public static PublicEvent FromFate(FateContext* fate) => new() {
        Address = (nint)fate,
        FateType = FateType.Normal,
        Id = fate->FateId,
    };

    public static PublicEvent FromDynamicEvent(DynamicEvent* dynamicEvent) => new() {
        Address = (nint)dynamicEvent,
        FateType = FateType.DynamicEvent,
        Id = dynamicEvent->DynamicEventId,
    };
    public static PublicEvent FromMechaEvent(WKSMechaEvent* mechaEvent) => new() {
        Address = (nint)mechaEvent,
        FateType = FateType.MechaEvent,
        Id = mechaEvent->WKSMechaEventDataRowId,
    };

    public static bool IsValid(PublicEvent publicEvent) {
        if (publicEvent is null) return false;
        if (Svc.ClientState is null || Svc.ClientState.LocalContentId is 0) return false;
        return true;
    }

    public static PublicEvent? CurrentFate => Player.StructsIntendedUse switch {
        TerritoryIntendedUse.Overworld => FateManager.Instance()->CurrentFate != null ? FromFate(FateManager.Instance()->CurrentFate) : null,
        TerritoryIntendedUse.Bozja or TerritoryIntendedUse.OccultCrescent => DynamicEventContainer.GetInstance()->GetCurrentEvent() != null ? FromDynamicEvent(DynamicEventContainer.GetInstance()->GetCurrentEvent()) : null,
        TerritoryIntendedUse.CosmicExploration => WKSManager.Instance()->MechaEventModule->CurrentEvent != null ? FromMechaEvent(WKSManager.Instance()->MechaEventModule->CurrentEvent) : null,
        _ => throw new NotImplementedException(),
    };

    public static unsafe IEnumerable<PublicEvent> Fates => Player.StructsIntendedUse switch {
        TerritoryIntendedUse.Overworld => FateManager.Instance()->Fates.Select(evt => FromFate(evt.Value)),
        TerritoryIntendedUse.Bozja or TerritoryIntendedUse.OccultCrescent => DynamicEventContainer.GetInstance()->Events.ToArray().Select(evt => FromDynamicEvent(&evt)),
        TerritoryIntendedUse.CosmicExploration => WKSManager.Instance()->MechaEventModule->Events.ToArray().Select(evt => FromMechaEvent(&evt)),
        _ => [],
    };

    private FateContext* GetFate() => FateManager.Instance()->GetFateById((ushort)Id);
    private DynamicEvent GetDynamicEvent() => DynamicEventContainer.GetInstance()->Events.ToArray().First(e => e.DynamicEventId == Id);
    private WKSMechaEvent GetMechaEvent() => WKSManager.Instance()->MechaEventModule->Events.ToArray().First(e => e.WKSMechaEventDataRowId == Id);

    public bool IsValid() => IsValid(this);

    public Vector3 Position => FateType switch {
        FateType.Normal => GetFate()->Location,
        FateType.DynamicEvent => GetDynamicEvent().MapMarker.Position,
        FateType.MechaEvent => GetMechaEvent().MapMarkers[0].MapMarkerData.Position,
        _ => Vector3.Zero,
    };

    public float Radius => FateType switch {
        FateType.Normal => GetFate()->Radius,
        FateType.DynamicEvent => GetDynamicEvent().MapMarker.Radius,
        FateType.MechaEvent => GetMechaEvent().MapMarkers[0].MapMarkerData.Radius,
        _ => 0f,
    };

    public int Progress => FateType switch {
        FateType.Normal => GetFate()->Progress,
        FateType.DynamicEvent => GetDynamicEvent().Progress,
        FateType.MechaEvent => GetMechaEvent().EventProgress,
        _ => 0,
    };

    public int Duration => FateType switch {
        FateType.Normal => GetFate()->Duration,
        FateType.DynamicEvent => (int)GetDynamicEvent().SecondsDuration,
        FateType.MechaEvent => EndTimeEpoch - StartTimeEpoch,
        _ => 0,
    };

    public float TimeRemaining => FateType switch {
        FateType.Normal => StartTimeEpoch + Duration - DateTimeOffset.Now.ToUnixTimeSeconds(),
        FateType.DynamicEvent => GetDynamicEvent().SecondsLeft,
        FateType.MechaEvent => StartTimeEpoch + Duration - DateTimeOffset.Now.ToUnixTimeSeconds(),
        _ => -1f,
    };

    public int StartTimeEpoch => FateType switch {
        FateType.Normal => GetFate()->StartTimeEpoch,
        FateType.DynamicEvent => GetDynamicEvent().StartTimestamp,
        FateType.MechaEvent => GetMechaEvent().EventStartTimestamp,
        _ => 0,
    };

    public int EndTimeEpoch => FateType switch {
        FateType.Normal => StartTimeEpoch + Duration,
        FateType.DynamicEvent => (int)(GetDynamicEvent().StartTimestamp + GetDynamicEvent().SecondsDuration),
        FateType.MechaEvent => GetMechaEvent().EventEndTimestamp,
        _ => 0,
    };

    public bool HasBonus => FateType switch {
        FateType.Normal => GetFate()->IsBonus,
        _ => false,
    };

    public byte Level => FateType switch {
        FateType.Normal => GetFate()->Level,
        FateType.DynamicEvent => (byte)GetDynamicEvent().MapMarker.RecommendedLevel,
        FateType.MechaEvent => (byte)GetMechaEvent().MapMarkers[0].MapMarkerData.RecommendedLevel,
        _ => 0,
    };

    public string Name => FateType switch {
        // Use Excel sheets instead of MemoryHelper.ReadSeString to avoid MissingMethodException
        FateType.Normal => GetRow<Sheets.Fate>(Id)?.Name.ToString() ?? $"FATE {Id}",
        FateType.DynamicEvent => GetRow<Sheets.DynamicEvent>(Id)?.Name.ToString() ?? $"DynamicEvent {Id}",
        FateType.MechaEvent => GetRow<Sheets.WKSMechaEventData>(GetMechaEvent().WKSMechaEventDataRowId)?.Unknown0.ToString() ?? $"MechaEvent {Id}",
        _ => $"Unknown Type: {Id}",
    };

    public uint MotivationNpcId => FateType switch {
        FateType.Normal => GetFate()->MotivationNpc,
        _ => 0,
    };

    public DGameObject? MotivationNpc => FateType switch {
        FateType.Normal => Svc.Objects.FirstOrDefault(o => o.EntityId == MotivationNpcId),
        _ => null,
    };

    public FateState State => FateType switch {
        FateType.Normal => GetFate()->State,
        FateType.DynamicEvent => ToFateState(GetDynamicEvent().State),
        FateType.MechaEvent => FateState.Running, // ???
        _ => 0,
    };

    private FateState ToFateState(DynamicEventState state) => state switch {
        DynamicEventState.Register => FateState.Preparing,
        DynamicEventState.Warmup => FateState.Preparing,
        DynamicEventState.Battle => FateState.Running,
        _ => FateState.Ending,
    };
}

