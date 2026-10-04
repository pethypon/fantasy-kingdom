using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class ThirdFactionEventCandidate
{
    public ThirdFactionEventDefinition Definition;
    public R1ContentCatalog.Encounter Encounter;
    public readonly List<Vector3> SpawnCells = new List<Vector3>();
    public Team TargetTeam;
    public Vector3Int TargetCell;
    public float Score, PlayerLoss, EnemyLoss;
    public string Rejection;
    public bool Safe => string.IsNullOrEmpty(Rejection);
}
public sealed class ThirdFactionDecision
{
    public ThirdFactionEventCandidate Event;
    public string Reason = "no_event_needed";
    public ThirdFactionDirectorMode Mode;
    public readonly List<ThirdFactionEventCandidate> Candidates = new List<ThirdFactionEventCandidate>();
    public bool NoEvent => Event == null;
}

public static class ThirdFactionInterventionGuard
{
    public static string Reject(ThirdFactionEventCandidate a, ThirdFactionWorldSnapshot w,
        ThirdFactionDirectorConfig c, ThirdFactionDirectorState state)
    {
        var d = a.Definition;
        if (d == null || string.IsNullOrWhiteSpace(d.EventId) || d.IpCost < 0 || d.IpCost > 50) return "event_condition_not_met";
        if (w.ThreatLevel < d.MinThreatLevel || w.ThreatLevel > d.MaxThreatLevel) return "threat_level_locked";
        if (state.CurrentIP < d.IpCost) return "insufficient_ip";
        if (w.Turn - state.LastAnyEventTurn < c.GlobalEventCooldown) return "global_event_cooldown";
        if (d.IsMajor && w.Turn - state.LastMajorEventTurn < Mathf.Max(c.MinimumTurnsBetweenMajorEvents, d.MajorCooldownTurns)) return "major_event_cooldown";
        foreach (var stamp in state.Events)
            if (stamp.Id == d.EventId && w.Turn - stamp.Turn < d.CooldownTurns) return "same_event_cooldown";
        if (w.BalancedWar && !d.AllowedDuringPreserveWar) return "active_balanced_war";
        if (w.Decisive && !d.AllowedDuringDecisivePhase) return "decisive_phase_protected";
        if (w.Stagnation < d.MinimumStagnation) return "event_condition_not_met";
        // Scenario opt-ins do not waive spawn or advantage safety.
        if (a.SpawnCells.Count == 0) return "invalid_spawn_position";
        float p = w.Player.MilitaryPower, e = w.Enemy.MilitaryPower;
        float total = Mathf.Max(1, p + e);
        if (Mathf.Max(a.PlayerLoss, a.EnemyLoss) / total > c.MaximumPowerSwingRatio) return "would_break_game";
        float afterP = Mathf.Max(0, p - a.PlayerLoss), afterE = Mathf.Max(0, e - a.EnemyLoss);
        if ((p > 0 && afterP <= 0) || (e > 0 && afterE <= 0)) return "would_force_unavoidable_loss";
        float lead = Mathf.Abs(p - e), afterLead = Mathf.Abs(afterP - afterE);
        if (c.PreventLeaderFlip && Mathf.Abs(p - e) > .001f && (p - e) * (afterP - afterE) <= 0) return "would_erase_legitimate_advantage";
        if (lead > total * .05f && Mathf.Max(0, lead - afterLead) / lead > c.MaximumAdvantageEraseRatio) return "would_erase_legitimate_advantage";
        return null;
    }

    public static bool SafeSpawn(ThirdFactionWorldSnapshot w, ThirdFactionDirectorConfig c, Vector3 cell,
        int responseDistance = 0)
    {
        var g = GridHelper.ToGridXZ(cell);
        foreach (var a in w.Actors)
        {
            if (GridHelper.MatchXZ(a.Cell, g)) return false;
            if (a.Objective && GridHelper.ChebyshevDistance(a.Cell, g)
                < Mathf.Max(c.MinimumObjectiveSpawnDistance, responseDistance)) return false;
        }
        return true;
    }
}

/// <summary>One immutable snapshot is used for all candidates. No candidate can mutate the live world.</summary>
public sealed class ThirdFactionDirector
{
    readonly ThirdFactionDirectorConfig config;
    readonly List<Vector3> safeCells = new List<Vector3>();
    public ThirdFactionDirector(ThirdFactionDirectorConfig value) { config = value; }

    public ThirdFactionDecision Decide(ThirdFactionWorldSnapshot world, ThirdFactionDirectorState state,
        GameSystems systems, R1ContentCatalog catalog)
    {
        var result = new ThirdFactionDecision();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            ThirdFactionWorldAnalyzer.Analyze(world, config);
            if (state.PredictionStartedTurn < 0 || state.Prediction == null
                || state.Prediction.Kind != world.Prediction.Kind || state.Prediction.Focus != world.Prediction.Focus)
                state.PredictionStartedTurn = world.Turn;
            if (world.Turn - state.PredictionStartedTurn > config.PredictionTurns + 1 && world.RecentBattles == 0)
                world.Prediction.Probability *= .4f;
            state.Prediction = world.Prediction;
            bool predicted = world.Prediction.Probability >= config.ReserveProbability
                && world.Prediction.EstimatedTurns <= config.PredictionTurns;
            float noEventScore = world.BalancedWar || world.Decisive || predicted ? 10000 : 24;
            result.Reason = world.BalancedWar ? "active_balanced_war" : world.Decisive ? "decisive_phase_protected"
                : predicted ? "reserve_for_predicted_conflict" : state.CurrentIP < 15 ? "save_ip_for_future" : "no_event_needed";
            result.Mode = predicted ? ThirdFactionDirectorMode.Reserve : ThirdFactionDirectorMode.Observe;
            safeCells.Clear();
            // Deterministic evenly-spaced sample; capped preparation cost even on very large maps.
            var map = systems.MapCreate;
            int stride = Mathf.Max(1, Mathf.CeilToInt((float)map.SetPos.Count / config.MaxSpawnCells));
            for (int i = 0; i < map.SetPos.Count; i += stride)
            {
                if (watch.Elapsed.TotalMilliseconds > config.DirectorBudgetMs) return Timeout(result, state);
                var cell = map.SetPos[i];
                if (ThirdFactionInterventionGuard.SafeSpawn(world, config, cell)
                    && map.CanTraverse(cell, cell) && !(systems.BuildSystem?.HasBuildingAt(GridHelper.ToGrid(cell)) ?? false)) safeCells.Add(cell);
            }
            var definitions = config.Events ?? Array.Empty<ThirdFactionEventDefinition>();
            for (int index = 0; index < definitions.Length && result.Candidates.Count < config.MaxCandidates; index++)
            {
                if (watch.Elapsed.TotalMilliseconds > config.DirectorBudgetMs) return Timeout(result, state);
                var d = definitions[index];
                if (d == null) continue;
                var candidate = Generate(d, world, state, catalog, systems);
                candidate.Rejection = candidate.Rejection ?? ThirdFactionInterventionGuard.Reject(candidate, world, config, state);
                candidate.Score = Evaluate(candidate, world, state);
                result.Candidates.Add(candidate);
                if (candidate.Safe && candidate.Score > noEventScore)
                { result.Event = candidate; noEventScore = candidate.Score; result.Reason = "event_opportunity"; }
            }
            if (result.Event != null)
                result.Mode = world.Stagnation >= config.StagnationThreshold ? ThirdFactionDirectorMode.AntiStalemate
                    : result.Event.Definition.Category == ThirdFactionEventCategory.DungeonRaid ? ThirdFactionDirectorMode.DungeonPressure
                    : result.Event.Definition.Category == ThirdFactionEventCategory.TerritoryRaid ? ThirdFactionDirectorMode.TerritoryPressure
                    : result.Event.Definition.Category == ThirdFactionEventCategory.GrowthOpportunity ? ThirdFactionDirectorMode.GrowthOpportunity
                    : result.Event.Definition.Category == ThirdFactionEventCategory.ScenarioSpecial ? ThirdFactionDirectorMode.Scenario : ThirdFactionDirectorMode.EventOpportunity;
            else if (!world.BalancedWar && !world.Decisive && !predicted && result.Candidates.Count > 0)
                result.Reason = result.Candidates.Exists(a => a.Safe) ? "all_candidates_low_value" : "all_candidates_unsafe";
            state.Mode = result.Mode;
            return result;
        }
        catch (Exception error)
        { Debug.LogException(error); result.Event = null; result.Reason = "director_error"; state.Mode = result.Mode = ThirdFactionDirectorMode.Observe; return result; }
    }
    static ThirdFactionDecision Timeout(ThirdFactionDecision d, ThirdFactionDirectorState state)
    { state.Mode = ThirdFactionDirectorMode.Observe; d.Event = null; d.Mode = ThirdFactionDirectorMode.Observe; d.Reason = "director_time_budget"; return d; }

    ThirdFactionEventCandidate Generate(ThirdFactionEventDefinition d, ThirdFactionWorldSnapshot w,
        ThirdFactionDirectorState state, R1ContentCatalog catalog, GameSystems s)
    {
        var a = new ThirdFactionEventCandidate { Definition = d };
        if (d.SpawnTeam != Team.Monster && d.SpawnTeam != Team.Intruder) { a.Rejection = "invalid_spawn_team"; return a; }
        bool expansion = d.Category == ThirdFactionEventCategory.StrongEnemyExpansion;
        var boss = s.WildBossSystem?.SpawnedBoss;
        if (expansion && (boss == null || !boss.IsAlive || !d.IsMajor || d.LocalExpansionRadius > 2
            || boss.wildBossTerritoryRadius + d.LocalExpansionRadius > state.StrongEnemyBaseRadius + config.MaximumStrongEnemyExpansion))
        { a.Rejection = "event_condition_not_met"; return a; }
        if (!expansion)
        {
            a.Encounter = ResolveEncounter(d, catalog);
            if (a.Encounter == null || a.Encounter.Prefab == null || a.Encounter.Stats == null)
            { a.Rejection = "event_content_missing"; return a; }
            if (w.Turn < a.Encounter.FirstRound) { a.Rejection = "event_condition_not_met"; return a; }
        }
        if (d.SpawnPolicy == ThirdFactionSpawnPolicy.NearStrongEnemy && (boss == null || !boss.IsAlive))
        { a.Rejection = "event_condition_not_met"; return a; }
        if ((d.SpawnPolicy == ThirdFactionSpawnPolicy.NearDungeon || d.Category == ThirdFactionEventCategory.DungeonRaid) && w.Dungeons.Count == 0)
        { a.Rejection = "event_condition_not_met"; return a; }
        Vector3 focus = expansion || d.SpawnPolicy == ThirdFactionSpawnPolicy.NearStrongEnemy ? boss.transform.position : d.SpawnPolicy == ThirdFactionSpawnPolicy.NearDungeon
            ? (Vector3)w.Dungeons[state.NextRandom(w.Dungeons.Count)] : (w.Player.Center + w.Enemy.Center) * .5f;
        if (d.TargetThirdFaction && !d.TargetPlayer && !d.TargetEnemy && !expansion
            && d.SpawnPolicy == ThirdFactionSpawnPolicy.Frontier) focus = w.Third.Center;
        if (d.SpawnPolicy == ThirdFactionSpawnPolicy.NearTerritory)
            focus = d.TargetPlayer && d.TargetEnemy ? (state.NextRandom(2) == 0 ? w.Player.Center : w.Enemy.Center)
                : d.TargetPlayer ? w.Player.Center : w.Enemy.Center;
        if (d.SpawnPolicy == ThirdFactionSpawnPolicy.NearBattle) focus = w.Prediction.Focus;
        float best = float.MaxValue;
        Vector3 first = default;
        bool found = false;
        int response = d.Category == ThirdFactionEventCategory.SummonStrongEnemy ? config.MinimumObjectiveSpawnDistance + 3 : 4 + config.ResponseTurns * 2;
        foreach (var cell in safeCells)
        {
            if (!ThirdFactionInterventionGuard.SafeSpawn(w, config, cell, response)) continue;
            if (expansion && GridHelper.ChebyshevDistance(cell, focus) > boss.wildBossTerritoryRadius + d.LocalExpansionRadius) continue;
            if (expansion && GridHelper.ChebyshevDistance(cell, focus) <= boss.wildBossTerritoryRadius) continue;
            if (!expansion && s.TerritorySystem.IsInAnyTerritory(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.z))) continue;
            float distance = (cell - focus).sqrMagnitude;
            if (distance >= best) continue;
            best = distance; first = cell; found = true;
        }
        if (!found) { a.Rejection = "invalid_spawn_position"; return a; }
        a.SpawnCells.Add(first);
        int count = expansion ? 1 : Mathf.Clamp(d.SpawnCount, 1, 8) + Mathf.Min(8, a.Encounter.Retinue?.Length ?? 0);
        foreach (var cell in safeCells)
        {
            if (a.SpawnCells.Count >= count) break;
            if (cell == first || GridHelper.ChebyshevDistance(first, cell) > 3
                || !ThirdFactionInterventionGuard.SafeSpawn(w, config, cell, response)
                || s.TerritorySystem.IsInAnyTerritory(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.z))) continue;
            a.SpawnCells.Add(cell);
        }
        if (a.SpawnCells.Count < count) { a.Rejection = "invalid_spawn_position"; return a; }
        a.TargetCell = GridHelper.ToGridXZ(focus);
        float pd = GridHelper.ChebyshevDistance(first, w.Player.Center), ed = GridHelper.ChebyshevDistance(first, w.Enemy.Center);
        a.TargetTeam = d.TargetPlayer && (!d.TargetEnemy || pd <= ed) ? Team.Player : d.TargetEnemy ? Team.Enemy : Team.None;
        if (d.TargetThirdFaction && w.Third.UnitCount > 0
            && (a.TargetTeam == Team.None || GridHelper.ChebyshevDistance(first, w.Third.Center) < Mathf.Min(d.TargetPlayer ? pd : float.MaxValue, d.TargetEnemy ? ed : float.MaxValue)))
            a.TargetTeam = Team.Monster;
        float swing = (w.Player.MilitaryPower + w.Enemy.MilitaryPower) * Mathf.Clamp01(d.EstimatedPowerSwing);
        if (!expansion)
        {
            float power = EstimatedSpawnPower(a.Encounter.Stats, w.AverageCombatLevel) * Mathf.Clamp(d.SpawnCount, 1, 8);
            for (int i = 0; i < Mathf.Min(8, a.Encounter.Retinue?.Length ?? 0); i++)
            {
                var member = a.Encounter.Retinue[i]?.GetComponentInChildren<Status>(true);
                if (member == null || !s.UnitSetting.UnitDataMap.TryGetValue(member.kind, out var stats))
                { a.Rejection = "event_content_missing"; return a; }
                power += EstimatedSpawnPower(stats, w.AverageCombatLevel);
            }
            swing = Mathf.Max(swing, power);
        }
        if (a.TargetTeam == Team.Player) a.PlayerLoss = swing;
        else if (a.TargetTeam == Team.Enemy) a.EnemyLoss = swing;
        return a;
    }
    static float EstimatedSpawnPower(UnitData stats, int level)
        => UnitData.CalcStat(stats.baseATK, stats.atkGrowth, level) * 2
            + UnitData.CalcStat(stats.baseDEF, stats.defGrowth, level)
            + Mathf.Sqrt(Mathf.Max(1, UnitData.CalcStat(stats.baseHP, stats.hpGrowth, level)));
    public static R1ContentCatalog.Encounter ResolveEncounter(ThirdFactionEventDefinition d, R1ContentCatalog catalog)
    {
        if (!string.IsNullOrWhiteSpace(d.EncounterId) && catalog != null)
            return catalog.Monsters.Find(e => e != null && e.Id == d.EncounterId) ?? catalog.Intruders.Find(e => e != null && e.Id == d.EncounterId);
        return d.Prefab != null && d.Stats != null ? new R1ContentCatalog.Encounter { Id = d.EventId,
            Prefab = d.Prefab, Stats = d.Stats, Count = d.SpawnCount, Relic = d.Reward } : null;
    }
    float Evaluate(ThirdFactionEventCandidate a, ThirdFactionWorldSnapshot w, ThirdFactionDirectorState state)
    {
        float repeat = 0;
        foreach (var stamp in state.Events)
            if (stamp.Category == a.Definition.Category) repeat += Mathf.Max(0, 1 - (w.Turn - stamp.Turn) / 10f) * stamp.RepeatCount * 10;
        float opportunity = w.Dungeons.Count > 0 && (a.Definition.Category == ThirdFactionEventCategory.DungeonRaid
            || a.Definition.Category == ThirdFactionEventCategory.ArtifactOpportunity) ? 15 : 0;
        float reserve = w.Prediction.Probability * a.Definition.IpCost * .8f;
        // No term rewards attacking the leading faction or equalizing its opponent's strength.
        return w.Stagnation * 45 + opportunity + (1 - w.Growth) * 8 + 6
            - a.Definition.EstimatedDisruption * 30 - a.Definition.IpCost * .4f - repeat - reserve;
    }
}

public interface IThirdFactionPreparedEvent : IDisposable { void Commit(); void Rollback(); }
public static class ThirdFactionEventExecutor
{
    public static bool Execute(ThirdFactionEventCandidate candidate, ThirdFactionIPSystem ip,
        Func<ThirdFactionEventCandidate, IThirdFactionPreparedEvent> prepare)
    {
        if (candidate == null || !candidate.Safe) return false;
        using (var reservation = ip.TryReserve(candidate.Definition.IpCost))
        {
            if (reservation == null) return false;
            IThirdFactionPreparedEvent transaction = null;
            try
            {
                transaction = prepare(candidate);
                if (transaction == null) return false;
                transaction.Commit();
                reservation.Commit();
                return true;
            }
            catch (Exception error)
            {
                try { transaction?.Rollback(); }
                catch (Exception rollbackError) { Debug.LogWarning("[ThirdFaction] Rollback error: " + rollbackError.Message); }
                Debug.LogWarning("[ThirdFaction] Event rolled back: " + error.Message); return false;
            }
            finally
            {
                try { transaction?.Dispose(); }
                catch (Exception cleanupError) { Debug.LogWarning("[ThirdFaction] Cleanup error: " + cleanupError.Message); }
            }
        }
    }
}
