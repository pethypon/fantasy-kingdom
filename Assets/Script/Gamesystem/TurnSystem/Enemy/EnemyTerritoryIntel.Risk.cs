using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Battle-local route evidence. Losses are own facts; threats are accepted only while observed.</summary>
[Serializable]
public sealed class AIRouteRiskRecord
{
    public Vector3Int Cell;
    public int Losses, LastLossTurn = -1, LastThreatTurn = -1, LastSafeSurveyTurn = -1;
    public float ObservedThreat;
    public AIRouteRiskRecord Copy() => (AIRouteRiskRecord)MemberwiseClone();
}

public sealed partial class EnemyTerritoryIntel
{
    public const int MaximumRouteRisks = 128, MaximumLossIdentities = 256;
    readonly Dictionary<Vector3Int, AIRouteRiskRecord> routeRisks = new Dictionary<Vector3Int, AIRouteRiskRecord>();
    readonly List<string> lossIdentities = new List<string>();
    readonly HashSet<string> lossIdentityIndex = new HashSet<string>(StringComparer.Ordinal);
    readonly Dictionary<Vector3Int, float> routeRiskHeat = new Dictionary<Vector3Int, float>();
    readonly Dictionary<Vector3Int, float> repeatedLossHeat = new Dictionary<Vector3Int, float>();
    int riskRevision, heatRevision = -1, heatTurn = -1;
    public int RouteRiskRevision => riskRevision;
    public int RememberedRouteRisks => routeRisks.Count;
    public int RecordedRouteLosses { get; private set; }

    AIRouteRiskRecord RiskRecord(Vector3Int cell)
    {
        if (routeRisks.TryGetValue(cell, out var record)) return record;
        if (routeRisks.Count >= MaximumRouteRisks)
        {
            Vector3Int oldest = default; int last = int.MaxValue;
            foreach (var pair in routeRisks)
            {
                int time = Math.Max(pair.Value.LastLossTurn, pair.Value.LastThreatTurn);
                if (time < last || time == last && CompareCell(pair.Key, oldest) < 0) { oldest = pair.Key; last = time; }
            }
            routeRisks.Remove(oldest);
        }
        record = new AIRouteRiskRecord { Cell = cell }; routeRisks.Add(cell, record); return record;
    }
    void AdvanceRiskRevision() { unchecked { riskRevision++; } heatRevision = -1; }

    public void RecordOwnCombatLoss(string life, Vector3 position, int round)
    {
        var cell = GridHelper.ToGridXZ(position);
        if (string.IsNullOrWhiteSpace(life) || life.Length > 128 || round < 0 || cell.x < 0 || cell.z < 0
            || width > 0 && !Inside(cell) || !lossIdentityIndex.Add(life)) return;
        if (lossIdentities.Count >= MaximumLossIdentities)
        { lossIdentityIndex.Remove(lossIdentities[0]); lossIdentities.RemoveAt(0); }
        lossIdentities.Add(life);
        var record = RiskRecord(cell); record.Losses = Math.Min(8, record.Losses + 1); record.LastLossTurn = round;
        record.LastSafeSurveyTurn = -1;
        RecordedRouteLosses = (int)Math.Min(int.MaxValue, (long)RecordedRouteLosses + 1); AdvanceRiskRevision();
    }

    void ObserveRouteRisks(AIBoardState board)
    {
        float ownPower = 0; int ownCount = 0;
        foreach (var actor in board.AliveEnemyUnits)
            if (actor != null && actor.type == Type.Unit && !MatchObjectiveRules.IsCommander(actor))
            { ownPower += Mathf.Max(1, actor.ATK); ownCount++; }
        float strongAttack = Mathf.Max(20, ownPower / Mathf.Max(1, ownCount) * 2);
        foreach (var hostile in board.AlivePlayerUnits)
        {
            if (hostile == null || !board.IsSpecialActorObserved(hostile) || hostile.type != Type.Unit
                || hostile.ATK < strongAttack && hostile.kind != Kind.Boss) continue;
            var record = RiskRecord(GridHelper.ToGridXZ(hostile.transform.position));
            float threat = Mathf.Clamp(hostile.ATK, 20, 1000);
            if (record.LastThreatTurn == board.TurnCount && record.ObservedThreat == threat) continue;
            record.LastThreatTurn = board.TurnCount; record.ObservedThreat = threat; record.LastSafeSurveyTurn = -1; AdvanceRiskRevision();
        }
        foreach (var record in routeRisks.Values)
        {
            int lastDanger = Math.Max(record.LastLossTurn, record.LastThreatTurn);
            if (lastDanger < 0 || record.LastSafeSurveyTurn >= lastDanger) continue;
            bool scoutNear = false;
            foreach (var actor in board.AliveEnemyUnits)
                if (actor != null && actor.kind == Kind.Scout && actor.IsAlive
                    && (actor.Special == null || actor.Special.visionZeroTurns <= 0)
                    && GridHelper.ChebyshevDistance(actor.transform.position, record.Cell) <= 4) { scoutNear = true; break; }
            if (!scoutNear) continue;
            bool surveyed = true;
            for (int dx = -2; dx <= 2 && surveyed; dx++) for (int dz = -2; dz <= 2; dz++)
            {
                var p = record.Cell + new Vector3Int(dx, 0, dz);
                if (Inside(p) && !board.IsVisibleToEnemy(p)) { surveyed = false; break; }
            }
            if (!surveyed) continue;
            foreach (var hostile in board.AlivePlayerUnits)
                if (hostile != null && board.IsSpecialActorObserved(hostile)
                    && GridHelper.ChebyshevDistance(hostile.transform.position, record.Cell) <= 4) { surveyed = false; break; }
            if (surveyed) { record.LastSafeSurveyTurn = board.TurnCount; record.Losses = 0; AdvanceRiskRevision(); }
        }
    }

    void PrepareRiskHeat()
    {
        int round = Math.Max(0, observationTurn);
        if (heatRevision == riskRevision && heatTurn == round) return;
        heatRevision = riskRevision; heatTurn = round; routeRiskHeat.Clear(); repeatedLossHeat.Clear();
        foreach (var record in routeRisks.Values)
        {
            float losses = record.LastLossTurn < 0 || record.LastSafeSurveyTurn >= record.LastLossTurn ? 0
                : record.Losses / (1 + Mathf.Max(0, round - record.LastLossTurn - 12) / 12f);
            float threat = record.LastThreatTurn < 0 || record.LastSafeSurveyTurn >= record.LastThreatTurn ? 0
                : record.ObservedThreat / (1 + Mathf.Max(0, round - record.LastThreatTurn) / 8f);
            for (int dx = -2; dx <= 2; dx++) for (int dz = -2; dz <= 2; dz++)
            {
                int distance = Math.Max(Math.Abs(dx), Math.Abs(dz)); float weight = distance == 0 ? 1 : distance == 1 ? .65f : .3f;
                var cell = record.Cell + new Vector3Int(dx, 0, dz);
                if (!Inside(cell)) continue;
                routeRiskHeat.TryGetValue(cell, out float risk); routeRiskHeat[cell] = Mathf.Min(1000, risk + weight * (losses * 12 + threat));
                repeatedLossHeat.TryGetValue(cell, out float repeated); repeatedLossHeat[cell] = repeated + losses * weight;
            }
        }
    }
    public float RouteRiskAt(Vector3 position)
    { PrepareRiskHeat(); return routeRiskHeat.TryGetValue(GridHelper.ToGridXZ(position), out float value) ? value : 0; }
    /// <summary>A route hypothesis, not a combat packet. Repeated losses request another corridor or a safe survey.</summary>
    public float RoutePlanningRisk(int currentHP, Vector3 position)
    {
        float risk = RouteRiskAt(position);
        if (repeatedLossHeat.TryGetValue(GridHelper.ToGridXZ(position), out float losses) && losses >= 1.25f)
            risk = Mathf.Max(risk, Mathf.Max(1, currentHP));
        return risk;
    }
    public bool WouldRepeatDangerousRoute(AIAction action)
    {
        if (action?.Unit == null || action.StrategicPriority <= 2
            || action.ActionType != AIActionType.Move && action.ActionType != AIActionType.Support && action.ActionType != AIActionType.Surround) return false;
        PrepareRiskHeat();
        return repeatedLossHeat.TryGetValue(GridHelper.ToGridXZ(action.TargetPos), out float losses) && losses >= 1.25f
            && RouteRiskAt(action.TargetPos) >= RouteRiskAt(action.Unit.transform.position);
    }
    void ClearRouteRisks()
    {
        routeRisks.Clear(); lossIdentities.Clear(); lossIdentityIndex.Clear(); routeRiskHeat.Clear(); repeatedLossHeat.Clear();
        RecordedRouteLosses = riskRevision = 0; heatRevision = heatTurn = -1;
    }
}
