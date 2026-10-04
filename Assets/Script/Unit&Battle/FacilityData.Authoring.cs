public static partial class FacilityData
{
    public static FacilityLevelData GetLevel(Status building, int level)
        => building != null && building.AuthoredFacility != null ? building.AuthoredFacility.GetLevel(level)
            : GetLevel(building != null ? building.facilityKind : default, level);
    public static int GetMaxLevel(Status building)
        => building != null && building.AuthoredFacility != null ? building.AuthoredFacility.GetInfo().MaxLevel
            : GetMaxLevel(building != null ? building.facilityKind : default);
    public static bool CanUpgrade(FactionState.ResourceData resources, int ap, Status building, int currentLevel)
    {
        if (building == null || currentLevel >= GetMaxLevel(building)) return false;
        var next = GetLevel(building, currentLevel + 1);
        return resources != null && ap >= next.UpgradeAP && CanAfford(resources, next.UpgradeCost);
    }
    public static string DisplayName(Status building)
        => building != null && building.AuthoredFacility != null ? building.AuthoredFacility.displayName
            : building != null && Table.TryGetValue(building.facilityKind, out var info) ? info.DisplayName : "建物";
}
