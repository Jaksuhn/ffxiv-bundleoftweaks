using FFXIVClientStructs.FFXIV.Client.Enums;

namespace ComplexTweaks.Utilities.Extensions;

public static class EnumExtensions {
    public static Ipc[] ToArray(this Ipc flags)
        => flags == Ipc.None ? [] : [.. Enum.GetValues<Ipc>().Where(flag => flag != Ipc.None && flags.HasFlag(flag))];

    public static bool NotDuty(this TerritoryIntendedUse territoryUse) => territoryUse switch {
        TerritoryIntendedUse.Town => true,
        TerritoryIntendedUse.Overworld => true,
        TerritoryIntendedUse.Inn => true,
        TerritoryIntendedUse.HousingOutdoor => true,
        TerritoryIntendedUse.HousingIndoor => true,
        TerritoryIntendedUse.Firmament => true,
        TerritoryIntendedUse.GoldSaucer => true,
        TerritoryIntendedUse.TripleTriadBattlehall => true,
        TerritoryIntendedUse.IslandSanctuary => true,
        _ => false
    };
}
