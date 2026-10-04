using System.Collections.Generic;
using UnityEngine;

public sealed class ThirdFactionWorldSnapshot
{
    public struct Actor
    {
        public Team Team;
        public Kind Kind;
        public Type Type;
        public Vector3Int Cell;
        public float Power, HPRatio;
        public bool Objective, Boss;
    }
    public sealed class Faction
    {
        public float MilitaryPower, EconomyPower, ObjectiveHPRatio = 1, ObjectiveHP;
        public int UnitCount, Facilities, Territory;
        public Vector3 Center;
    }
    public int Turn, ThreatLevel, AverageCombatLevel = 1;
    public readonly List<Actor> Actors = new List<Actor>();
    public readonly List<Vector3Int> Dungeons = new List<Vector3Int>();
    public Faction Player = new Faction(), Enemy = new Faction(), Third = new Faction();
    public int RecentBattles, RecentObjectiveDamage, TerritoryChanges;
    public float Growth, ArmyDistance = 999, ActiveWar, Stagnation;
    public bool Decisive, BalancedWar;
    public ThirdFactionConflictPrediction Prediction;

    public static float UnitPower(Status u)
    {
        float ratio = Mathf.Clamp01((float)u.HP / Mathf.Max(1, u.MaxHP));
        float reach = u.kind == Kind.Archer || u.kind == Kind.Crossbow || u.kind == Kind.Magicsniper ? 1.2f : 1;
        float role = u.kind == Kind.Bomber || u.kind == Kind.Magic ? 1.15f : u.kind == Kind.Priest ? 1.25f : 1;
        float disabled = StatusEffectSystem.IsStunned(u) ? .4f : 1;
        return Mathf.Max(1, (u.ATK * 2 + u.DEF + Mathf.Sqrt(Mathf.Max(1, u.MaxHP)) + u.Level * 2)
            * (.3f + ratio * .7f) * reach * role * disabled * (u.isWildBoss ? 1.5f : 1));
    }

    public static ThirdFactionWorldSnapshot Capture(GameSystems s, int turn, int threat, ThirdFactionDirectorState state,
        List<Status> buffer)
    {
        var world = new ThirdFactionWorldSnapshot { Turn = turn, ThreatLevel = threat };
        int levels = 0, levelCount = 0;
        CombatRegistry.Collect(buffer); // Exactly one full actor collection for Director candidate evaluation.
        foreach (var u in buffer)
        {
            bool objective = (u.team == Team.Player || u.team == Team.Enemy) && (u.kind == Kind.King || u.kind == Kind.Crystal);
            var a = new Actor { Team = u.team, Kind = u.kind, Type = u.type, Cell = u.GridPosition,
                Power = UnitPower(u), HPRatio = Mathf.Clamp01((float)u.HP / Mathf.Max(1, u.MaxHP)), Objective = objective, Boss = u.isWildBoss };
            world.Actors.Add(a);
            if ((u.team == Team.Player || u.team == Team.Enemy) && u.type == Type.Unit)
            { levels += u.Level; levelCount++; }
            var f = u.team == Team.Player ? world.Player : u.team == Team.Enemy ? world.Enemy : world.Third;
            if (u.type == Type.Unit && u.kind != Kind.Crystal)
            { f.MilitaryPower += a.Power; f.UnitCount++; f.Center += u.transform.position; }
            else if (u.kind != Kind.Crystal) f.Facilities++;
            if (objective) { f.ObjectiveHPRatio = Mathf.Min(f.ObjectiveHPRatio, a.HPRatio); f.ObjectiveHP += u.HP; }
        }
        world.AverageCombatLevel = levelCount == 0 ? 1 : Mathf.Max(1, Mathf.RoundToInt((float)levels / levelCount));
        world.Actors.Sort((a, b) =>
        { int n = a.Team.CompareTo(b.Team); if (n != 0) return n; n = a.Cell.x.CompareTo(b.Cell.x); if (n != 0) return n;
            n = a.Cell.z.CompareTo(b.Cell.z); return n != 0 ? n : a.Kind.CompareTo(b.Kind); });
        if (world.Player.UnitCount > 0) world.Player.Center /= world.Player.UnitCount;
        if (world.Enemy.UnitCount > 0) world.Enemy.Center /= world.Enemy.UnitCount;
        if (world.Third.UnitCount > 0) world.Third.Center /= world.Third.UnitCount;
        world.Player.Territory = s.TerritorySystem?.GetTerritory(Team.Player)?.Count ?? 0;
        world.Enemy.Territory = s.TerritorySystem?.GetTerritory(Team.Enemy)?.Count ?? 0;
        world.Player.EconomyPower = Economy(s.FactionState.PlayerResources, world.Player.Facilities);
        world.Enemy.EconomyPower = Economy(s.FactionState.EnemyResources, world.Enemy.Facilities);
        if (s.DungeonSystem != null) foreach (var d in s.DungeonSystem.Dungeons) if (!d.Cleared) world.Dungeons.Add(d.Position);
        world.RecentBattles = state.BattleCount;
        world.RecentObjectiveDamage = state.ObjectiveDamage;
        foreach (var h in state.History)
        { world.RecentBattles += h.Battles; world.RecentObjectiveDamage += h.ObjectiveDamage; }
        if (state.History.Count > 0)
        {
            var h = state.History[0];
            world.TerritoryChanges = Mathf.Abs(world.Player.Territory + world.Enemy.Territory - h.Territory);
            // Spending resources on troops is still growth. Losses are not counted as positive growth.
            world.Growth = Mathf.Clamp01((Mathf.Max(0, world.Player.EconomyPower - h.PlayerEconomy)
                + Mathf.Max(0, world.Enemy.EconomyPower - h.EnemyEconomy)
                + Mathf.Max(0, world.Player.MilitaryPower - h.PlayerPower)
                + Mathf.Max(0, world.Enemy.MilitaryPower - h.EnemyPower))
                / Mathf.Max(1, h.PlayerEconomy + h.EnemyEconomy + h.PlayerPower + h.EnemyPower));
        }
        foreach (var a in world.Actors)
        {
            if (a.Team != Team.Player || a.Type != Type.Unit || a.Objective) continue;
            foreach (var b in world.Actors)
                if (b.Team == Team.Enemy && b.Type == Type.Unit && !b.Objective)
                    world.ArmyDistance = Mathf.Min(world.ArmyDistance, GridHelper.ChebyshevDistance(a.Cell, b.Cell));
        }
        return world;
    }
    static float Economy(FactionState.ResourceData r, int facilities)
        => r == null ? facilities * 10 : facilities * 10 + Mathf.Max(0, r.Wood + r.Stone + r.Iron * 2 + r.MagicOre * 2 + r.Bread + r.Water) * .05f;
}

public static class ThirdFactionWorldAnalyzer
{
    public static void Analyze(ThirdFactionWorldSnapshot w, ThirdFactionDirectorConfig c)
    {
        w.ActiveWar = Mathf.Clamp01(w.RecentBattles * .12f + (w.ArmyDistance <= 3 ? .35f : 0) + (w.RecentObjectiveDamage > 0 ? .3f : 0));
        float max = Mathf.Max(1, Mathf.Max(w.Player.MilitaryPower, w.Enemy.MilitaryPower));
        w.BalancedWar = w.ActiveWar >= c.ActiveWarThreshold
            && Mathf.Min(w.Player.MilitaryPower, w.Enemy.MilitaryPower) / max >= c.BalancedPowerRatio;
        w.Stagnation = Mathf.Clamp01(1 - w.ActiveWar - w.Growth - Mathf.Min(.4f, w.TerritoryChanges * .03f));
        w.Decisive = w.Player.ObjectiveHPRatio <= c.DecisiveHPRatio || w.Enemy.ObjectiveHPRatio <= c.DecisiveHPRatio;
        w.Prediction = NaturalConflictPredictor.Predict(w, c);
    }
}

public static class NaturalConflictPredictor
{
    public static ThirdFactionConflictPrediction Predict(ThirdFactionWorldSnapshot w, ThirdFactionDirectorConfig c)
    {
        var p = new ThirdFactionConflictPrediction { EstimatedTurns = c.PredictionTurns + 1 };
        if (w.ArmyDistance <= 2 + c.PredictionTurns * 2)
        { p.Probability = w.ArmyDistance <= 3 ? .9f : .75f; p.EstimatedTurns = Mathf.Max(1, Mathf.CeilToInt((w.ArmyDistance - 2) / 2));
            p.Kind = "main_armies_approaching"; p.Focus = GridHelper.ToGridXZ((w.Player.Center + w.Enemy.Center) * .5f); }
        foreach (var dungeon in w.Dungeons)
        {
            bool player = false, enemy = false;
            foreach (var a in w.Actors)
            {
                if (a.Type != Type.Unit || GridHelper.ChebyshevDistance(a.Cell, dungeon) > 3) continue;
                player |= a.Team == Team.Player; enemy |= a.Team == Team.Enemy;
            }
            if (player && enemy) { p.Probability = .95f; p.EstimatedTurns = 1; p.Kind = "shared_dungeon"; p.Focus = dungeon; }
        }
        foreach (var objective in w.Actors)
        {
            if (!objective.Objective) continue;
            foreach (var a in w.Actors)
                if ((a.Team == Team.Player || a.Team == Team.Enemy) && a.Team != objective.Team && a.Type == Type.Unit
                    && GridHelper.ChebyshevDistance(a.Cell, objective.Cell) <= 2 + c.PredictionTurns)
                { p.Probability = .95f; p.EstimatedTurns = 1; p.Kind = "objective_siege"; p.Focus = objective.Cell; }
        }
        return p;
    }
}
