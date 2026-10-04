using UnityEngine;

/// <summary>A stable identity independent of the compatibility behaviour enum.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/ゲーム制作/建物")]
public sealed class FacilityDefinitionData : ScriptableObject
{
    public string definitionId;
    public string displayName = "新しい建物";
    public string category = "生産";
    public FacilityKind behaviourKind = FacilityKind.LoggingCamp;
    public GameObject prefab;
    public BoardActionProfile actionProfile;
    public Team defaultTeam = Team.Player;
    public FacilityData.ResourceCost buildCost;
    [Range(0, 50)] public int buildAP = 4;
    public FacilityData.FacilityLevelData[] levels = { new FacilityData.FacilityLevelData { HP = 100 } };
    public bool availableToPlayer = true, availableToEnemy = true;

    public bool IsValid => !string.IsNullOrWhiteSpace(definitionId) && levels != null && levels.Length > 0
        && FacilityData.Table.ContainsKey(behaviourKind);
    public bool IsAvailable(Team team) => IsValid && (team == Team.Player ? availableToPlayer : team == Team.Enemy && availableToEnemy);
    public FacilityData.FacilityLevelData GetLevel(int level)
    {
        if (levels == null || levels.Length == 0) return default;
        var data = levels[Mathf.Clamp(level - 1, 0, levels.Length - 1)];
        data.HP = Mathf.Max(1, data.HP); data.ATK = Mathf.Max(0, data.ATK); data.DEF = Mathf.Max(0, data.DEF);
        data.UpgradeAP = Mathf.Clamp(data.UpgradeAP, 0, GameConstants.MaxAP);
        data.UpgradeCost = Sanitize(data.UpgradeCost);
        data.Input = Sanitize(data.Input); data.Output = Sanitize(data.Output); data.Maintenance = Sanitize(data.Maintenance);
        data.BonusOutput1 = Sanitize(data.BonusOutput1); data.BonusOutput2 = Sanitize(data.BonusOutput2);
        data.BonusChance1 = Mathf.Clamp01(data.BonusChance1); data.BonusChance2 = Mathf.Clamp01(data.BonusChance2);
        data.SpecialValue = Mathf.Max(0, data.SpecialValue);
        return data;
    }
    public FacilityData.FacilityInfo GetInfo()
    {
        var first = GetLevel(1);
        return new FacilityData.FacilityInfo { DisplayName = displayName, APCost = Mathf.Clamp(buildAP, 0, GameConstants.MaxAP),
            BuildCost = Sanitize(buildCost), HP = first.HP, DEF = first.DEF, ATK = first.ATK, MaxLevel = Mathf.Max(1, levels?.Length ?? 0) };
    }
    public static FacilityData.ResourceCost Sanitize(FacilityData.ResourceCost value)
        => new FacilityData.ResourceCost(Mathf.Max(0,value.Wood), Mathf.Max(0,value.Stone), Mathf.Max(0,value.Iron),
            Mathf.Max(0,value.MagicOre), Mathf.Max(0,value.Water), Mathf.Max(0,value.Citizen));
    static FacilityData.ProductionBundle Sanitize(FacilityData.ProductionBundle value)
        => new FacilityData.ProductionBundle { Wood = Mathf.Max(0,value.Wood), Stone = Mathf.Max(0,value.Stone),
            Iron = Mathf.Max(0,value.Iron), MagicOre = Mathf.Max(0,value.MagicOre), Wheat = Mathf.Max(0,value.Wheat),
            Bread = Mathf.Max(0,value.Bread), Water = Mathf.Max(0,value.Water), Citizen = Mathf.Max(0,value.Citizen) };
}
