using UnityEngine;

public enum ThirdFactionEventCategory { GrowthOpportunity, GrowthPressure, DungeonRaid, TerritoryRaid,
    ArtifactOpportunity, BattleIntervention, AntiStalemate, StrongEnemyExpansion, SummonStrongEnemy, Chaos, ScenarioSpecial }
public enum ThirdFactionEventScale { Minor, Medium, Major, Scenario }
public enum ThirdFactionSpawnPolicy { Frontier, NearDungeon, NearTerritory, NearBattle, NearStrongEnemy }
public enum ThirdFactionDirectorMode { Observe, Reserve, AntiStalemate, GrowthOpportunity, DungeonPressure,
    TerritoryPressure, EventOpportunity, Scenario }
public enum ThirdFactionObjectiveKind { RaidDungeon, RaidTerritory, HoldArea, HuntTarget, Withdraw, GuardEventObject }

[CreateAssetMenu(menuName = "Fantasy Kingdom/ゲーム制作/第三陣営のイベント")]
public sealed class ThirdFactionEventDefinition : ScriptableObject
{
    public string EventId, DisplayName;
    [Range(0, 50)] public int IpCost = 15;
    [Min(1)] public int MinThreatLevel = 1, MaxThreatLevel = 100;
    public ThirdFactionEventCategory Category;
    public ThirdFactionEventScale Scale;
    [Min(1)] public int CooldownTurns = 8;
    [Min(0)] public int MajorCooldownTurns = 8;
    public bool TargetPlayer = true, TargetEnemy = true, TargetThirdFaction;
    public bool AllowedDuringPreserveWar, AllowedDuringDecisivePhase;
    public ThirdFactionSpawnPolicy SpawnPolicy;
    public ThirdFactionObjectiveKind Objective = ThirdFactionObjectiveKind.HoldArea;
    [Min(1)] public int ObjectiveTurns = 5;
    [Range(0, 1)] public float EstimatedPowerSwing = .05f;
    [Range(0, 1)] public float EstimatedDisruption = .15f;
    [Range(0, 1)] public float MinimumStagnation;
    public string[] Tags = new string[0];
    public string EncounterId;
    public GameObject Prefab;
    public UnitData Stats;
    public Team SpawnTeam = Team.Monster;
    [Min(1)] public int SpawnCount = 1;
    public bool CanActImmediately;
    public R1ContentCatalog.UniqueReward Reward;
    [Min(1)] public int LocalExpansionRadius = 1;
    public bool IsMajor => Scale == ThirdFactionEventScale.Major || Scale == ThirdFactionEventScale.Scenario;
}
