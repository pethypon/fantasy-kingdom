using UnityEngine;

[CreateAssetMenu(menuName = "Fantasy Kingdom/Third Faction/Director Config")]
public sealed class ThirdFactionDirectorConfig : ScriptableObject
{
    // R1 rules are constants; tuning cannot accidentally change the AP/IP contract.
    public const int MaxIP = 50, IPPerTurn = 5;
    [Range(0, 50)] public int InitialIP = 15;
    [Min(2)] public int HistoryTurns = 5;
    [Range(1, 5)] public int PredictionTurns = 2;
    [Range(0, 1)] public float ReserveProbability = .7f;
    [Range(0, 1)] public float ActiveWarThreshold = .45f;
    [Range(0, 1)] public float BalancedPowerRatio = .65f;
    [Range(0, 1)] public float StagnationThreshold = .6f;
    [Range(0, 1)] public float DecisiveHPRatio = .25f;
    [Range(0, 1)] public float MaximumAdvantageEraseRatio = .25f;
    [Range(0, 1)] public float MaximumPowerSwingRatio = .2f;
    public bool PreventLeaderFlip = true;
    [Range(1, 4)] public int MaximumStrongEnemyExpansion = 2;
    [Min(1)] public int MinimumTurnsBetweenMajorEvents = 8;
    [Min(0)] public int GlobalEventCooldown = 2;
    [Min(2)] public int MinimumObjectiveSpawnDistance = 6;
    [Range(1, 8)] public int ResponseTurns = 2;
    [Range(8, 128)] public int MaxCandidates = 32;
    [Range(32, 4096)] public int MaxSpawnCells = 512;
    [Range(1, 20)] public float DirectorBudgetMs = 5;
    [Range(1, 5)] public float TacticalSliceMs = 3;
    [Range(8, 128)] public int TacticalMaxActions = 64;
    [Range(8, 256)] public int TacticalPathExpansions = 64;
    [Range(.25f, 2)] public float TacticalPathBudgetMs = 1;
    public ThirdFactionEventDefinition[] Events = new ThirdFactionEventDefinition[0];
}
