using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Bounded round-robin combat: shared movement/attack/skill patterns, terrain, AP and damage rules.</summary>
public sealed class ThirdFactionTacticalAI
{
    readonly GameSystems s;
    readonly NeutralFactionSystem neutral;
    readonly ThirdFactionDirectorConfig config;
    readonly List<Status> roster = new List<Status>(), targets = new List<Status>(), areaTargets = new List<Status>();
    readonly Dictionary<int, Vector3> wander = new Dictionary<int, Vector3>();
    readonly Dictionary<int, Vector3> previous = new Dictionary<int, Vector3>();
    readonly List<Status> visibleThreats = new List<Status>();
    readonly ThirdFactionPathPlanner paths = new ThirdFactionPathPlanner();
    readonly ThirdFactionPathPlanner.StepProvider pathStep;
    Status planningUnit;
    public ThirdFactionTacticalAI(GameSystems systems, NeutralFactionSystem factions, ThirdFactionDirectorConfig settings)
    { s = systems; neutral = factions; config = settings; pathStep = TryPathStep; }

    public IEnumerator Execute(ThirdFactionDirectorState state, int round)
    {
        roster.Clear(); neutral.UnitParent.GetComponentsInChildren(false, roster);
        roster.Sort((a, b) => neutral.ActorId(a).CompareTo(neutral.ActorId(b)));
        CombatRegistry.Collect(targets);
        targets.Sort((a, b) => { int v = a.team.CompareTo(b.team); if (v != 0) return v;
            v = a.GridPosition.x.CompareTo(b.GridPosition.x); return v != 0 ? v : a.GridPosition.z.CompareTo(b.GridPosition.z); });
        wander.Clear(); previous.Clear();
        var slice = System.Diagnostics.Stopwatch.StartNew();
        int actions = 0;
        bool acted;
        do
        {
            acted = false;
            foreach (var u in roster)
            {
                if (s.MoveGenerator.turnGenerator.IsGameOver) yield break;
                if (u == null || !u.IsAlive || !u.gameObject.activeInHierarchy || !neutral.CanAct(u, round)
                    || (u.team != Team.Monster && u.team != Team.Intruder) || StatusEffectSystem.IsStunned(u)) continue;
                int id = neutral.ActorId(u);
                int vision = neutral.Origin(u)?.VisionRange ?? 3;
                Status enemy = null;
                float nearest = float.MaxValue;
                visibleThreats.Clear();
                foreach (var other in targets)
                {
                    if (other == null || !other.IsAlive || !other.gameObject.activeInHierarchy || !NeutralFactionSystem.AreHostile(u, other)) continue;
                    float dist = GridHelper.ChebyshevDistance(u.GridPosition, other.GridPosition);
                    if (!CanSee(u, other, vision) || !s.MapCreate.HasClearTerrainLine(u.transform.position, other.transform.position)) continue;
                    visibleThreats.Add(other);
                    if (dist < nearest) { nearest = dist; enemy = other; }
                }
                // Facing is the same free N/S operation available to principal factions.
                if (enemy != null && !AttackPatterns.IsDirectionIndependent(u))
                    u.direction = enemy.transform.position.z >= u.transform.position.z ? Direction.N : Direction.S;
                bool success = TrySkill(u, vision);
                if (!success && s.APSystem.CanAct(u.team, APSystem.ActionType.Attack, u))
                {
                    Status best = null;
                    float score = 0;
                    foreach (var t in targets)
                    {
                        if (!VisibleHostile(u, t, vision) || t.ShieldTurns > 0
                            || !AttackPatterns.CanAttack(u, u.direction, t.transform.position.x - u.transform.position.x, t.transform.position.z - u.transform.position.z)
                            || !s.MapCreate.CanAttackAcrossTerrain(u, t.transform.position)) continue;
                        float value = Mathf.Min(t.HP, DamageCalculator.CalcNormal(u, t));
                        if (value >= t.HP) value += 25;
                        if (value > score) { best = t; score = value; }
                    }
                    if (best != null)
                    {
                        s.APSystem.Consume(u.team, APSystem.ActionType.Attack, u);
                        var turn = s.MoveGenerator.turnGenerator;
                        var selection = turn.Context.SelectUnit; var target = s.BattleSystem.Target;
                        try { turn.Context.SelectUnit = u; s.BattleSystem.SetTarget(best); s.BattleSystem.ProcessDamage(turn); }
                        finally { turn.Context.SelectUnit = selection; s.BattleSystem.SetTarget(target); }
                        success = true;
                    }
                }
                if (!success && !StatusEffectSystem.IsMovementBlocked(u))
                {
                    var objective = state.Objectives.Find(o => o.ActorId == id && o.ExpireTurn >= round);
                    if (!wander.TryGetValue(id, out var goal))
                    { goal = u.transform.position + new Vector3(state.NextRandom(7) - 3, 0, state.NextRandom(7) - 3); wander[id] = goal; }
                    if (objective != null) goal = objective.TargetCell;
                    if (enemy != null) goal = enemy.transform.position;
                    bool withdraw = objective?.Kind == ThirdFactionObjectiveKind.Withdraw || u.HPRatio < .3f && enemy != null;
                    if (!MovePatterns.IsDirectionIndependent(u))
                        u.direction = (withdraw ? u.transform.position.z - goal.z : goal.z - u.transform.position.z) >= 0 ? Direction.N : Direction.S;
                    float best = 0; Vector3 destination = u.transform.position;
                    foreach (var offset in MovePatterns.Offsets(u))
                    {
                        int dir = MovePatterns.IsDirectionIndependent(u) ? 1 : MovePatterns.DirZ(u.direction);
                        int x = u.GridPosition.x + offset.x, z = u.GridPosition.z + offset.y * dir;
                        if (!s.MapCreate.TryGetHeight(x, z, out float y)) continue;
                        var cell = new Vector3(x, y, z);
                        if (cell == u.transform.position || previous.TryGetValue(id, out var last) && cell == last
                            || !s.MapCreate.CanTraverse(u.transform.position, cell)
                            || s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(cell))
                            || !s.APSystem.CanAct(u.team, APSystem.ActionType.Move, u, u.transform.position, cell)) continue;
                        float value = GridHelper.ChebyshevDistance(u.transform.position, goal) - GridHelper.ChebyshevDistance(cell, goal);
                        if (withdraw) value = -value;
                        value -= Danger(u, cell) * (withdraw ? 2 : .2f);
                        if (value > best) { destination = cell; best = value; }
                    }
                    bool routed = false;
                    if (best <= 0 && !withdraw)
                    {
                        planningUnit = u;
                        routed = paths.TryNextStep(u, u.transform.position, goal, pathStep,
                            config.TacticalPathExpansions, config.TacticalPathBudgetMs, out destination);
                        planningUnit = null;
                    }
                    if (best > 0 || routed)
                    {
                        var old = u.transform.position; previous[id] = old;
                        if (!MovePatterns.CanMove(u, u.direction, destination.x - old.x, destination.z - old.z))
                            u.direction = u.direction == Direction.N ? Direction.S : Direction.N;
                        s.APSystem.Consume(u.team, APSystem.ActionType.Move, u, old, destination);
                        u.transform.position = destination; u.HasMovedThisTurn = true;
                        s.MoveGenerator.MoveUpdate(s.MoveGenerator.Cell(old), s.MoveGenerator.Cell(destination));
                        success = true;
                    }
                }
                if (success) { acted = true; actions++; }
                if (actions >= config.TacticalMaxActions) yield break;
                if (slice.Elapsed.TotalMilliseconds >= config.TacticalSliceMs)
                { yield return null; slice.Restart(); }
            }
        } while (acted);
    }
    bool VisibleHostile(Status u, Status t, int range) => t != null && t.IsAlive && t.gameObject.activeInHierarchy
        && NeutralFactionSystem.AreHostile(u, t) && CanSee(u, t, range)
        && s.MapCreate.HasClearTerrainLine(u.transform.position, t.transform.position);

    static bool CanSee(Status actor, Status target, int legacyRange)
    {
        var profile = BoardActionProfile.For(actor);
        var mask = profile != null ? profile.vision : null;
        int dx = target.GridPosition.x - actor.GridPosition.x, dz = target.GridPosition.z - actor.GridPosition.z;
        return mask != null && mask.useCustom ? (dx == 0 && dz == 0) || mask.Contains(dx, dz, actor.direction)
            : GridHelper.ChebyshevDistance(actor.GridPosition, target.GridPosition) <= legacyRange;
    }

    bool TrySkill(Status u, int vision)
    {
        if (!AttackPatterns.CanUseSkills(u) || u.SkillCooldown > 0 || StatusEffectSystem.HasDebuff(u, StatusEffectType.Seal)
            || !SkillData.Table.TryGetValue(u.AssignedSkillId, out var skill)
            || !s.APSystem.CanUseSkill(u.team, s.APSystem.CalcSkillCost(skill.APCost, u))) return false;
        Status selected = null;
        float best = 0;
        Vector3Int center = u.GridPosition;
        bool area = skill.Target == SkillTarget.SelfArea || skill.Target == SkillTarget.DesignatedTile
            || skill.Target == SkillTarget.AdjacentCenter || skill.Target == SkillTarget.DirectionLine || skill.Target == SkillTarget.DesignatedRow;
        if (skill.Target == SkillTarget.Self && skill.FixedHeal > 0 && u.HP < u.MaxHP) selected = u;
        if (skill.Target == SkillTarget.Self && skill.GrantBuff != BuffType.None
            && !StatusEffectSystem.HasBuff(u, skill.GrantBuff)) selected = u;
        if (skill.Target == SkillTarget.AllySingle)
        {
            foreach (var ally in roster)
                if (ally != null && ally.IsAlive && ally != u && ally.team == u.team && ally.HP < ally.MaxHP
                    && Vector3.Distance(u.transform.position, ally.transform.position) <= 4
                    && s.MapCreate.CanAttackAcrossTerrain(u, ally.transform.position)
                    && ally.MaxHP - ally.HP > best) { best = ally.MaxHP - ally.HP; selected = ally; }
        }
        else if (skill.Target != SkillTarget.Self)
        {
            AttackPatterns.SkillFixedPositions.TryGetValue(skill.Id, out var offsets);
            if (offsets == null) AttackPatterns.SkillAttackPositions.TryGetValue(u.kind, out offsets);
            foreach (var t in targets)
            {
                if (!VisibleHostile(u, t, vision) || t.ShieldTurns > 0) continue;
                bool inRange = skill.Target == SkillTarget.SelfArea;
                if (offsets != null) foreach (var offset in offsets)
                    if (u.GridPosition.x + offset.x == t.GridPosition.x
                        && u.GridPosition.z + offset.y * MovePatterns.DirZ(u.direction) == t.GridPosition.z) { inRange = true; break; }
                if (!inRange || !s.MapCreate.CanAttackAcrossTerrain(u, t.transform.position)) continue;
                var testCenter = skill.Target == SkillTarget.SelfArea ? u.GridPosition : t.GridPosition;
                var cells = SkillSystem.GetAreaPositions(skill.Area, testCenter, u.direction);
                cells = SkillSystem.FilterLineSkillBlocked(cells, skill.Area, testCenter, s.MapCreate, s.MoveGenerator);
                float score = 0;
                if (area)
                {
                    foreach (var other in targets)
                        if (VisibleHostile(u, other, vision) && ContainsXZ(cells, other.GridPosition)) score += Mathf.Min(other.HP, DamageCalculator.CalcSkill(u, other, skill));
                }
                else score = Mathf.Min(t.HP, DamageCalculator.CalcSkill(u, t, skill));
                if (score > best) { best = score; selected = t; center = testCenter; }
            }
        }
        if (selected == null) return false;
        s.APSystem.ConsumeSkill(u.team, skill.APCost, u);
        if (area)
        {
            areaTargets.Clear();
            var cells = SkillSystem.GetAreaPositions(skill.Area, center, u.direction);
            cells = SkillSystem.FilterLineSkillBlocked(cells, skill.Area, center, s.MapCreate, s.MoveGenerator);
            foreach (var t in targets) if (VisibleHostile(u, t, vision) && ContainsXZ(cells, t.GridPosition)) areaTargets.Add(t);
            s.SkillSystem.ExecuteAreaSkill(u, skill, areaTargets);
        }
        else s.SkillSystem.ExecuteSkill(u, selected, skill);
        u.SkillCooldown = (int)skill.Rarity + 1;
        return true;
    }
    static bool ContainsXZ(List<Vector3Int> cells, Vector3Int target)
    { foreach (var cell in cells) if (GridHelper.MatchXZ(cell, target)) return true; return false; }

    float Danger(Status unit, Vector3 cell)
    {
        float damage = 0;
        // Only locally observed attackers are used; distant hidden units do not inform tactics.
        for (int i = 0; i < visibleThreats.Count && i < 16; i++)
        {
            var enemy = visibleThreats[i];
            if (enemy == null || !enemy.IsAlive || StatusEffectSystem.IsStunned(enemy)
                || !AttackPatterns.CanAttack(enemy, enemy.direction, cell.x - enemy.transform.position.x, cell.z - enemy.transform.position.z)
                || !s.MapCreate.CanAttackAcrossTerrain(enemy, cell)) continue;
            damage += DamageCalculator.CalcNormal(enemy, unit);
        }
        return damage / Mathf.Max(1, unit.HP);
    }
    bool TryPathStep(Vector3 from, Vector2Int offset, out Vector3 cell, out float cost)
    {
        cell = default; cost = 0;
        var u = planningUnit;
        int x = Mathf.RoundToInt(from.x) + offset.x, z = Mathf.RoundToInt(from.z) + offset.y;
        if (!s.MapCreate.TryGetHeight(x, z, out float y)) return false;
        cell = new Vector3(x, y, z);
        if (!s.MapCreate.CanTraverse(from, cell) || s.MoveGenerator.IsOccupied(s.MoveGenerator.Cell(cell))) return false;
        if (GridHelper.MatchXZ(GridHelper.ToGridXZ(from), u.GridPosition)
            && (previous.TryGetValue(neutral.ActorId(u), out var last) && cell == last
                || !s.APSystem.CanAct(u.team, APSystem.ActionType.Move, u, from, cell))) return false;
        cost = s.APSystem.CalcCost(APSystem.ActionType.Move, u, from, cell) + Danger(u, cell) * 4;
        return true;
    }
}
