using Automaton.Utilities.Movement;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using Lumina.Excel.Sheets;
using System.Threading.Tasks;
using Achievement = FFXIVClientStructs.FFXIV.Client.Game.UI.Achievement;

namespace Automaton.Tasks;

public class MovementConfig
{
    public float? Tolerance { get; set; }
    public bool Mount { get; set; }
    public bool Fly { get; set; }
    public bool Dismount { get; set; }

    public static MovementConfig Default => new()
    {
        Tolerance = null,
        Mount = false,
        Fly = false,
        Dismount = false
    };

    public static MovementConfig Everything => new()
    {
        Tolerance = null,
        Mount = true,
        Fly = true,
        Dismount = true
    };

    public static MovementConfig InteractRange => new()
    {
        Tolerance = 3,
        Mount = false,
        Fly = false,
        Dismount = false
    };
}

public abstract class CommonTasks : AutoTask
{
    private readonly OverrideMovement movement = new();
    private readonly Memory.AchievementProgress achv = new();

    protected async Task MoveTo(FlagMapMarker flag, MovementConfig config)
    {
        using var scope = BeginScope("MoveToFlag");
        Status = "Waiting for Navmesh";
        await WaitWhile(() => Service.Navmesh.BuildProgress() >= 0, "BuildMesh");
        ErrorIf(!Service.Navmesh.IsReady(), "Failed to build navmesh for the zone");
        var pof = Service.Navmesh.PointOnFloor(Coords.FlagToWorld(flag), false, 5) ?? throw new Exception("Failed to find point on floor");
        await MoveTo(pof, config);
    }

    protected async Task MoveTo(Vector3 dest, MovementConfig config)
    {
        using var scope = BeginScope("MoveTo");
        if (Player.DistanceTo(dest) < (config.Tolerance ?? Service.Navmesh.GetTolerance()))
            return; // already in range

        if (Coords.IsTeleportingFaster(dest))
        {
            Log("Teleporting faster");
            await TeleportTo(Player.Territory, dest, allowSameZoneTeleport: true);
        }

        if (config.Mount || config.Fly)
            await Mount();

        // ensure navmesh is ready
        Status = "Waiting for Navmesh";
        await WaitWhile(() => Service.Navmesh.BuildProgress() >= 0, "BuildMesh");
        ErrorIf(!Service.Navmesh.IsReady(), "Failed to build navmesh for the zone");
        ErrorIf(!Service.Navmesh.PathfindAndMoveTo(dest, config.Fly), "Failed to start pathfinding to destination");
        Status = $"Moving to {dest}";
        using var stop = new OnDispose(Service.Navmesh.Stop);
        await WaitWhile(() => !(Player.DistanceTo(dest) < (config.Tolerance ?? Service.Navmesh.GetTolerance())), "Navigate");
        if (config.Dismount)
            await Dismount();
    }

    protected async Task MoveToDirectly(Vector3 dest, float tolerance)
    {
        using var scope = BeginScope("MoveToDirectly");
        if (Player.DistanceTo(dest) < tolerance)
            return;

        Status = $"Moving to {dest}";
        movement.DesiredPosition = dest;
        movement.Enabled = true;
        using var stop = new OnDispose(() => movement.Enabled = false);
        await WaitWhile(() => !(Player.DistanceTo(dest) < tolerance), "DirectNavigate");
    }

    protected async Task TeleportTo(uint territoryId, Vector3 destination, bool allowSameZoneTeleport = false)
    {
        using var scope = BeginScope("Teleport");
        if (!allowSameZoneTeleport && Player.Territory == territoryId)
            return; // already in correct zone

        var closestAetheryteId = Coords.FindClosestAetheryte(territoryId, destination) ?? 0;
        var teleportAetheryteId = Coords.FindPrimaryAetheryte(closestAetheryteId);
        ErrorIf(teleportAetheryteId == 0, $"Failed to find aetheryte in [{territoryId}] {GetRow<TerritoryType>(territoryId)?.PlaceName.Value.Name}");
        var row = GetRow<Aetheryte>(teleportAetheryteId)!;
        if (Player.Territory != row.Value.Territory.RowId)
        {
            Status = $"Teleporting to {row.Value.PlaceName.Value.Name}";
            ErrorIf(!Coords.ExecuteTeleport(teleportAetheryteId), $"Failed to teleport to {teleportAetheryteId}");
            await WaitUntil(Game.IsCastingTeleport, "TeleportStart");
            await WaitUntil(() => Player.Territory == GetRow<Aetheryte>(teleportAetheryteId)?.Territory.RowId && Game.IsTerritoryLoaded(), "TeleportFinish");
        }

        if (teleportAetheryteId != closestAetheryteId)
        {
            Status = $"Interacting with aethernet to get to [{territoryId}]";
            var (aetheryteId, aetherytePos) = Coords.FindAetheryte(teleportAetheryteId);
            await MoveTo(aetherytePos, new MovementConfig { Tolerance = 10 });
            ErrorIf(!PlayerEx.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipping(() => Game.AddonActive("SelectString"), "WaitSelectAethernet", skipTalk: true);
            Game.TeleportToAethernet(teleportAetheryteId, closestAetheryteId);
            await WaitUntil(() => Player.IsBusy, "TeleportStart");
            await WaitUntil(() => Player.Territory == territoryId && Game.IsTerritoryLoaded(), "TeleportFinish");
        }

        if (territoryId == 886)
        {
            // firmament special case
            Status = $"Interacting with aetheryte to get to the Firmament";
            var (aetheryteId, aetherytePos) = Coords.FindAetheryte(teleportAetheryteId);
            await MoveTo(aetherytePos, new MovementConfig { Tolerance = 10 });
            ErrorIf(!PlayerEx.InteractWith(aetheryteId), "Failed to interact with aetheryte");
            await WaitUntilSkipping(() => Game.AddonActive("SelectString"), "WaitSelectFirmament", skipTalk: true);
            Game.TeleportToFirmament(teleportAetheryteId);
            await WaitUntil(() => Player.IsBusy, "TeleportStart");
            await WaitUntil(() => Player.Territory == territoryId && Game.IsTerritoryLoaded(), "TeleportFinish");
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

    protected async Task WaitUntilSkipping(Func<bool> condition, string scopeName, bool skipTalk = false, bool skipYesNo = false, bool skipRequest = false)
    {
        using var scope = BeginScope(scopeName);
        while (!condition())
        {
            if (skipTalk)
            {
                if (Game.AddonActive("Talk"))
                {
                    Log("progressing talk...");
                    Game.ProgressTalk();
                }
            }
            if (skipYesNo)
            {
                if (Game.AddonActive("SelectYesno"))
                {
                    Log("progressing yes/no...");
                    Game.SelectYes();
                }
            }
            if (skipRequest)
            {
                if (Game.AddonActive("Request"))
                {
                    Log("progressing request...");
                    Game.TurnInRequests();
                }
            }
            Log("waiting...");
            await NextFrame();
        }
    }

    protected async Task WaitUntilTerritory(uint territoryId)
    {
        using var scope = BeginScope("WaitUntilTerritory");
        await WaitWhile(() => Player.Territory != territoryId || Player.IsBusy || !Game.IsTerritoryLoaded(), "WaitingForTerritory");
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

    protected async Task InteractWith(DGameObject obj, Func<bool>? waitUntil = null, int? selectStringIndex = null, bool skipTalk = false, bool skipYesNo = false, bool skipRequest = false)
    {
        using var scope = BeginScope("InteractWith");
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
                    await WaitUntilSkipping(condition, "WaitingForNpcInteractionToFinish", skipTalk: skipTalk, skipYesNo: skipYesNo, skipRequest: skipRequest);
                    return;
                }
                else return;
            }
            await NextFrame();
        }
        ErrorIf(true, $"Failed to interact with object after {maxAttempts} tries");
    }
}
