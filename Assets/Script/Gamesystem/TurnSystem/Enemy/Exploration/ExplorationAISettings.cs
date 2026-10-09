using UnityEngine;

/// <summary>All exploration policy and work limits are authorable independently of combat priorities.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/探索 R2 設定", fileName = "ExplorationSettings")]
public sealed class ExplorationAISettings : ScriptableObject
{
    public bool Enabled = true;
    public int RecentHistoryTurns = 8;
    public int StallTurns = 3;
    public int FreshIntelTurns = 5;
    public int LowIntelTurns = 15;
    public int MediumIntelTurns = 30;
    public int StaleIntelTurns = 30;
    public int RegionSize = 16;
    public int MaxCandidateRegions = 64;
    public int MaxSavedActors = 512;
    public int MaxSavedCells = 1048576;
    public int MaxMapDimension = 4096;
    public int FrontierRefreshTurns = 4;
    public int MaxRouteNodes = 2048;
    public int MaxRouteLength = 4096;
    public int RouteCacheTurns = 8;
    public int MaxCachedRoutes = 64;
    public int LoopUniqueCellLimit = 4;
    public int LoopMinimumSamples = 4;
    public int LoopCooldownTurns = 8;
    public int VisitCountCap = 12;
    public int RecentVisitTurns = 8;
    public int CompletionRevealCells = 1;
    public int CompletionDistance = 2;
    public int RevealCreditRadius = 8;
    public int FailedMoveLimit = 3;
    public float UnknownGainWeight = 5f;
    public float StaleIntelWeight = 35f;
    public float DirectionDiversityWeight = 12f;
    public float StrategicRouteWeight = 10f;
    public float DistanceFromRecentAreaWeight = 1.5f;
    public float RecentVisitPenalty = 45f;
    public float RepeatVisitPenalty = 3f;
    public float OtherScoutPenalty = 1000f;
    public float LoopAreaPenalty = 500f;
    public float DangerWeight = 60f;
    public float TravelCostWeight = .75f;
    public float TargetDistanceWeight = 14f;
    public float RouteDangerWeight = 2f;
    public float RouteDeviationPenalty = 30f;
    public float RouteTieDirectionWeight = 2f;
    public float LocalRevealWeight = 2.5f;
    public float MaximumLocalRevealBonus = 40f;
    public float MoveDangerWeight = 90f;
    public float SuicideMovePenalty = 1000f;
    public float MaximumFrontierDangerFraction = .8f;
    public float MinimumFrontierScore = 0f;
    public float MaximumDistanceProgressBonus = 4f;
    public float ProgressDistanceThreshold = .5f;
    public float LowIntelValue = .1f;
    public float MediumIntelValue = .45f;
    public float HighIntelValue = 1f;
    public bool EnableJsonlTelemetry;
    public int TelemetryBufferRecords = 32;
    public int TelemetryMaxRecords = 256;
    public int TelemetryMaxFileBytes = 4 * 1024 * 1024;

    static ExplorationAISettings active;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetActive() { active = null; }
    public static ExplorationAISettings Active
    {
        get
        {
            if (active != null) return active;
            active = Resources.Load<ExplorationAISettings>("AI/ExplorationSettings");
            if (active == null)
            {
                active = CreateInstance<ExplorationAISettings>();
                active.hideFlags = HideFlags.HideAndDontSave;
            }
            return active;
        }
    }
}
