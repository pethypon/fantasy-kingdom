using System.Collections.Generic;
using UnityEngine;

public partial class AIBoardState
{
    int environmentGeneration = -1, environmentRevision = -1;
    bool knownEnvironment;
    public bool HasKnownEnvironment
    {
        get
        {
            if (ReconMap == null) return false;
            var environment = ReconMap.Environment;
            if (environmentGeneration != Generation || environmentRevision != environment.Revision)
            {
                environmentGeneration = Generation; environmentRevision = environment.Revision;
                knownEnvironment = environment.HasKnownCell(knownCells, ActorTeam);
            }
            return knownEnvironment;
        }
    }
    void AddKnownCaveMove(Status unit, List<Vector3> result)
    {
        var map = _moveGen.mapcreate;
        if (!IsTerrainKnown(unit.transform.position) || !map.Environment.TryCaveExit(unit.transform.position, out var exit)
            || !IsTerrainKnown(exit) || !map.Environment.IsKnownTo(ActorTeam, unit.transform.position)
            || !map.Environment.IsKnownTo(ActorTeam, exit)
            || !map.Environment.CanUseCave(unit, unit.transform.position, exit, TurnCount)) return;
        var key = GridHelper.ToGridXZ(exit);
        if (ownedWallCells.Contains(key) || key == GridHelper.ToGridXZ(EnemyCrystalPos)
            || CanUsePlayerCrystalAsTarget() && key == GridHelper.ToGridXZ(PlayerCrystalPos)) return;
        foreach (var actor in AliveEnemyUnits) if (actor != null && actor.GridPosition == key) return;
        foreach (var actor in AlivePlayerUnits) if (actor != null && actor.GridPosition == key) return;
        if (!result.Contains(exit)) result.Add(exit);
    }

    public int KnownEnvironmentDamage(Status actor, Vector3 position, bool entry)
    {
        if (actor == null || ReconMap == null || !IsTerrainKnown(position) || !ReconMap.Environment.IsKnownTo(ActorTeam, position)) return 0;
        var environment = ReconMap.Environment;
        return entry ? environment.EntryDamage(actor, position) : environment.StandingDamage(actor, position);
    }
    public int KnownEnvironmentHeal(Status actor, Vector3 position)
        => actor != null && ReconMap != null && IsTerrainKnown(position) && ReconMap.Environment.IsKnownTo(ActorTeam, position)
            ? ReconMap.Environment.StandingHeal(actor, position) : 0;
    public TerrainEnvironmentDefinition KnownEnvironmentDefinition(Vector3 position)
        => ReconMap != null && IsTerrainKnown(position) ? ReconMap.Environment.KnownDefinitionAt(ActorTeam, position) : null;
    public int KnownEnvironmentMoveCost(Vector3 from, Vector3 to)
        => KnownEnvironmentDefinition(to) != null ? ReconMap.Environment.MoveAPAdjustment(from, to) : 0;
    public int KnownEnvironmentVisionModifier(Vector3 position)
        => KnownEnvironmentDefinition(position) != null ? ReconMap.Environment.VisionModifier(position) : 0;
    // A conservative two-packet forecast. One-use protection must not be reused for both entry and next-turn damage.
    public int KnownEnvironmentRisk(Status actor, Vector3 position)
    {
        if (actor == null || ReconMap == null || !IsTerrainKnown(position)) return 0;
        long risk = KnownEnvironmentDamage(actor, position, false);
        if (GridHelper.ToGridXZ(actor.transform.position) != GridHelper.ToGridXZ(position))
            risk += AIActionEvaluator.EstimateObservedHitLoss(actor, this, KnownEnvironmentDamage(actor, position, true), HpChangeKind.IgnoreDefenceDamage);
        return (int)System.Math.Min(int.MaxValue, risk);
    }
}
