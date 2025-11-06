using ComplexTweaks.Utilities.Movement;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;
using Achievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace ComplexTweaks.Tasks;

[Flags]
public enum MovementOptions
{
    None = 0,
    Mount = 1 << 0,
    Fly = 1 << 1,
    Dismount = 1 << 2,
}

public enum PathingStrategy
{
    Auto = 0,
    Navmesh = 1,
    Direct = 2,
}

public readonly record struct MovementConfig(float? Tolerance, MovementOptions Movement, PathingStrategy Pathing)
{
    public static MovementConfig Default => new(null, MovementOptions.None, PathingStrategy.Auto);
    public static MovementConfig Everything => new(null, MovementOptions.Mount | MovementOptions.Fly | MovementOptions.Dismount, PathingStrategy.Auto);
    public static MovementConfig GroundMove => new(null, MovementOptions.Mount | MovementOptions.Dismount, PathingStrategy.Auto);
    public static MovementConfig InteractRange => new(3, MovementOptions.None, PathingStrategy.Auto);

    public MovementConfig WithTolerance(float? tolerance) => this with { Tolerance = tolerance };
    public MovementConfig WithOptions(MovementOptions movement) => this with { Movement = movement };
    public MovementConfig WithStrategy(PathingStrategy pathing) => this with { Pathing = pathing };
}

[Flags]
public enum UiSkipOptions
{
    None = 0,
    Talk = 1 << 0,
    YesNo = 1 << 1,
    Request = 1 << 2,
}

public abstract class CommonTasks : AutoTask
{
    private readonly OverrideMovement movement = new();
    private readonly Memory.AchievementProgress achv = new();

    private async Task NavmeshReady()
    {
        using var scope = BeginScope("WaitingForNavmesh");
        Status = "Waiting for Navmesh";
        await WaitWhile(() => Service.Navmesh.BuildProgress() >= 0, "BuildMesh");
        ErrorIf(!Service.Navmesh.IsReady(), "Failed to build navmesh for the zone");
    }

    protected async Task MoveTo(FlagMapMarker flag, MovementConfig config, Func<bool>? stopCondition = null, Func<Task>? onStopReached = null)
    {
        using var scope = BeginScope("MoveToFlag");
        await TeleportTo(flag.TerritoryId, flag.ToVector3());
        await MoveTo(flag.ToVector3(), config, stopCondition, onStopReached);
    }

    protected async Task MoveTo(Vector3 dest, MovementConfig config, Func<bool>? stopCondition = null, Func<Task>? onStopReached = null)
    {
        using var scope = BeginScope("MoveTo");
        await WaitUntil(() => Player.Available, "WaitingForPlayer");
        var tolerance = config.Tolerance ?? Service.Navmesh.GetTolerance();
        if (Player.DistanceTo(dest) < tolerance)
            return;

        if (Coords.IsTeleportingFaster(dest))
        {
            Log("Teleporting faster");
            await TeleportTo(Player.Territory, dest, allowSameZoneTeleport: true);
        }

        if (config.Movement.HasFlag(MovementOptions.Mount) || config.Movement.HasFlag(MovementOptions.Fly))
            await Mount();

        if (config.Pathing == PathingStrategy.Direct)
            await MoveToDirectly(dest, tolerance);
        else
        {
            await NavmeshReady();
            await WaitUntil(() => !Service.Navmesh.PathfindInProgress(), "Waiting for in-progress calls to finish");
            ErrorIf(!Service.Navmesh.PathfindAndMoveTo(dest, config.Movement.HasFlag(MovementOptions.Fly) && Player.CanFly), "Failed to start pathfinding to destination");
            Status = $"Moving to {dest}";
            using var stop = new OnDispose(Service.Navmesh.Stop);

            if (stopCondition is null)
                await WaitWhile(() => !(Player.DistanceTo(dest) < tolerance), "Navigate");
            else
            {
                await WaitWhile(() => !(Player.DistanceTo(dest) < tolerance || stopCondition()), "Navigate");
                if (stopCondition() && onStopReached is not null)
                    await onStopReached();
            }
        }

        if (config.Movement.HasFlag(MovementOptions.Dismount))
            await Dismount();
    }

    protected async Task MoveToDirectly(Vector3 dest, Func<bool> stopCondition)
    {
        using var scope = BeginScope("MoveDirectly");
        if (stopCondition())
            return;

        Status = $"Moving to {dest}";
        movement.DesiredPosition = dest;
        movement.Enabled = true;
        using var stop = new OnDispose(() => movement.Enabled = false);
        await WaitUntil(stopCondition, "WaitForCondition");
    }

    protected async Task MoveToDirectly(Vector3 dest, float tolerance)
    {
        using var scope = BeginScope("MoveDirectlyWithTolerance");
        await MoveToDirectly(dest, () => !(Player.DistanceTo(dest) < tolerance));
    }

    protected async Task TeleportTo(uint territoryId, Vector3 destination, bool allowSameZoneTeleport = false)
    {
        using var scope = BeginScope("Teleport");
        if (!allowSameZoneTeleport && Player.Territory == territoryId)
            return; // already in correct zone

        var closestAetheryteId = Coords.FindClosestAetheryte(territoryId, destination) ?? 0;
        var teleportAetheryteId = Coords.FindPrimaryAetheryte(closestAetheryteId);
        ErrorIf(teleportAetheryteId == 0, $"Failed to find aetheryte in [{territoryId}] {GetRow<TerritoryType>(territoryId)?.PlaceName.Value.Name}");
        if (GetRow<Aetheryte>(teleportAetheryteId) is { Territory.RowId: var destinationId, PlaceName.Value.Name: var destinationName } && Player.Territory != destinationId)
        {
            Status = $"Teleporting to {destinationName}";
            ErrorIf(!Coords.ExecuteTeleport(teleportAetheryteId), $"Failed to teleport to {teleportAetheryteId}");
            await WaitUntilTerritory(destinationId);
        }

        if (teleportAetheryteId != closestAetheryteId)
        {
            Status = $"Interacting with aethernet to get to [{territoryId}]";
            var (aetheryteId, aetherytePos) = Coords.FindAetheryte(teleportAetheryteId);
            await MoveTo(aetherytePos, MovementConfig.Default.WithTolerance(10));
            ErrorIf(!PlayerEx.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipping(() => Game.AddonActive("SelectString"), "WaitSelectAethernet", UiSkipOptions.Talk);
            Game.TeleportToAethernet(teleportAetheryteId, closestAetheryteId);
            await WaitUntil(() => Player.IsBusy, "TeleportStart"); // TODO: something better
            await WaitUntil(() => Player.Territory == territoryId && Game.IsTerritoryLoaded() && Player.Interactable, "TeleportFinish");
        }

        if (territoryId == 886)
        {
            // firmament special case
            Status = $"Interacting with aetheryte to get to the Firmament";
            var (aetheryteId, aetherytePos) = Coords.FindAetheryte(teleportAetheryteId);
            await MoveTo(aetherytePos, MovementConfig.Default.WithTolerance(10));
            ErrorIf(!PlayerEx.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipping(() => Game.AddonActive("SelectString"), "WaitSelectFirmament", UiSkipOptions.Talk);
            Game.TeleportToFirmament(teleportAetheryteId);
            await WaitUntilTerritory(territoryId);
        }

        // I think this check gives more problems than it solves
        WarningIf(Player.Territory != territoryId, $"Failed to teleport to expected zone (exp: {territoryId}, act: {Player.Territory})");
    }

    protected async Task Mount()
    {
        using var scope = BeginScope("Mount");
        if (Player.Mounted) return;
        Status = "Mounting";
        await WaitUntil(PlayerEx.Mount, "MountCast");
        await WaitUntil(() => Player.Mounted, "Mounting");
        ErrorIf(!Player.Mounted, "Failed to mount");
    }

    protected async Task Dismount()
    {
        using var scope = BeginScope("Dismount");
        if (!Player.Mounted) return;

        if (Svc.Condition[ConditionFlag.InFlight])
        {
            Game.UseAction(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 23);
            await WaitWhile(() => Svc.Condition[ConditionFlag.InFlight], "WaitingToLand");
        }
        if (Player.Mounted && !Svc.Condition[ConditionFlag.InFlight])
        {
            Game.UseAction(FFXIVClientStructs.FFXIV.Client.Game.ActionType.GeneralAction, 23);
            await WaitWhile(() => Player.Mounted, "WaitingToDismount");
        }
        ErrorIf(Player.Mounted, "Failed to dismount");
    }

    protected async Task WaitUntilSkipping(Func<bool> condition, string scopeName, UiSkipOptions skip)
    {
        using var scope = BeginScope(scopeName);
        while (!condition())
        {
            if (skip.HasFlag(UiSkipOptions.Talk) && Game.AddonActive("Talk"))
            {
                Log("progressing talk...");
                Game.ProgressTalk();
            }
            if (skip.HasFlag(UiSkipOptions.YesNo) && Game.AddonActive("SelectYesno"))
            {
                Log("progressing yes/no...");
                Game.SelectYes();
            }
            if (skip.HasFlag(UiSkipOptions.Request) && Game.AddonActive("Request"))
            {
                Log("progressing request...");
                Game.TurnInRequests();
            }
            Log("waiting...");
            await NextFrame();
        }
    }

    protected async Task WaitUntilTerritory(uint territoryId)
    {
        using var scope = BeginScope("WaitUntilTerritory");
        await WaitUntil(() => Player.Territory == territoryId && Game.IsTerritoryLoaded() && Player.Interactable, "WaitingForTerritory");
    }

    protected async Task<(uint, uint)> GetAchievementProgress(uint achievementId, string scopeName)
    {
        using var scope = BeginScope(scopeName);
        achv.ReceiveAchievementProgressHook.Enable();
        unsafe { Achievement.Instance()->RequestAchievementProgress(achievementId); }
        static unsafe bool IsState(Achievement.AchievementState state) => Achievement.Instance()->ProgressRequestState == state;
        await WaitUntil(() => IsState(Achievement.AchievementState.Requested), "WaitingForRequestStart");
        await WaitUntil(() => IsState(Achievement.AchievementState.Loaded), "WaitingForRequestFinish");
        achv.ReceiveAchievementProgressHook.Disable();
        return achv.LastId == achievementId ? (achv.LastCurrent, achv.LastMax) : throw new Exception($"Expected data for achievement [#{achievementId}], got [#{achv.LastId}]");
    }

    protected async Task BuyFromShop(ulong vendorInstanceId, uint shopId, uint itemId, int count, Game.ShopType shopType = Game.ShopType.None)
    {
        using var scope = BeginScope("Buy");
        if (!Game.IsShopOpen(shopId, shopType))
        {
            Log("Opening shop...");
            ErrorIf(!Game.OpenShop(vendorInstanceId, shopId), $"Failed to open shop {vendorInstanceId:X}.{shopId:X}");
            await WaitWhile(() => !Game.IsShopOpen(shopId, shopType), "WaitForOpen");
            await WaitWhile(() => !Svc.Condition[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        }

        Log("Buying...");
        ErrorIf(!Game.BuyItemFromShop(shopId, itemId, count), $"Failed to buy {count}x {itemId} from shop {vendorInstanceId:X}.{shopId:X}");
        await WaitWhile(() => Game.ShopTransactionInProgress(shopId), "Transaction");
        Log("Closing shop...");
        ErrorIf(!Game.CloseShop(), $"Failed to close shop {vendorInstanceId:X}.{shopId:X}");
        await WaitWhile(() => Game.IsShopOpen(), "WaitForClose");
        await WaitWhile(() => Svc.Condition[ConditionFlag.OccupiedInEvent], "WaitForCondition");
        await NextFrame();
    }

    protected async Task InteractWith(DGameObject obj, Func<bool>? waitUntil = null, int? selectStringIndex = null, UiSkipOptions skip = UiSkipOptions.None)
    {
        using var scope = BeginScope("InteractWith");

        if (!Game.InInteractRange(obj))
        {
            Log("Not in interact range, moving closer");
            await MoveToDirectly(obj.Position, () => Game.InInteractRange(obj));
        }

        Status = $"Interacting with {obj.GameObjectId}";
        await WaitWhile(() => Player.IsJumping, "WaitForAbleToInteract");
        const int maxAttempts = 5;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            if (Game.InteractWith(obj.GameObjectId))
            {
                if (selectStringIndex is { } index)
                {
                    await WaitUntil(() => Game.AddonActive("SelectString"), "WaitingForSelectString");
                    Game.SelectString(index);
                }
                if (waitUntil is { } condition)
                {
                    await WaitUntilSkipping(condition, "WaitingForNpcInteractionToFinish", skip);
                    return;
                }
                else return;
            }
            await NextFrame();
        }
        ErrorIf(true, $"Failed to interact with object after {maxAttempts} tries");
    }
}
