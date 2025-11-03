using ECommons.ExcelServices;
using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;

namespace ComplexTweaks.Features;

[Tweak]
internal class AutoEquipXPBoosts : Tweak
{
    public override string Name => "Auto Equip DT Earrings";
    public override string Description => "Automatically equip the DT earrings when level synced to <= 90.";

    public override void Enable() => Svc.Framework.Update += CheckForLevelSync;
    public override void Disable() => Svc.Framework.Update -= CheckForLevelSync;

    private unsafe void CheckForLevelSync(IFramework framework)
    {
        if (!Player.IsLevelSynced || Player.SyncedLevel == 0 || IsOccupied()) return;
        if (Player.TerritoryIntendedUse is not (TerritoryIntendedUseEnum.Dungeon or TerritoryIntendedUseEnum.Raid or TerritoryIntendedUseEnum.Raid_2 or TerritoryIntendedUseEnum.Alliance_Raid)) return;
        if (GetRow<ContentFinderCondition>(GameMain.Instance()->CurrentContentFinderConditionId) is { ContentType.RowId: 28 }) return; // skip ults

        if (Player.SyncedLevel <= 90)
            if (Inventory.HasItem(41081))
                PlayerEx.Equip(41081);
    }
}
