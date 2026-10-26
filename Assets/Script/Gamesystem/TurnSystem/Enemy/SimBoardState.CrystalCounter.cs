using System.Collections.Generic;
using UnityEngine;

public partial class SimBoardState
{
    // Immutable territory snapshots, restricted to owned or currently observed territory.
    public HashSet<Vector3Int> PlayerCrystalCounterArea, EnemyCrystalCounterArea;

    void CaptureCrystalCounterAreas(AIBoardState board)
    {
        PlayerCrystalCounterArea = new HashSet<Vector3Int>(); EnemyCrystalCounterArea = new HashSet<Vector3Int>();
        if (board?.TerritorySystem == null) return;
        foreach (var owner in new[] { Team.Player, Team.Enemy })
        {
            var source = board.TerritorySystem.GetTerritory(owner); if (source == null) continue;
            var destination = ToSimulationTeam(owner) == Team.Player ? PlayerCrystalCounterArea : EnemyCrystalCounterArea;
            foreach (var cell in source)
                if (owner == board.ActorTeam || board.IsVisibleToEnemy(cell)) destination.Add(GridHelper.ToGridXZ(cell));
        }
    }

    void RegisterCrystalAttack(SimUnit attacker, SimUnit target, bool validDamagingAttack)
    {
        if (!validDamagingAttack || attacker == null || target == null || target.Kind != Kind.Crystal
            || !target.IsAlive || attacker.Team == target.Team) return;
        if (target.CrystalCounter == null) target.CrystalCounter = new MainCrystalCounterState();
        target.CrystalCounter.Register(attacker.Team, TurnCount);
    }

    void ProcessCrystalCounterAtTurnEnd(Team attacker, int round)
    {
        if (Units == null || IsTerminal()) return;
        foreach (var crystal in Units)
        {
            if (crystal.Kind != Kind.Crystal || crystal.Team == attacker
                || crystal.CrystalCounter?.TryConsume(attacker, round) != true || !crystal.IsAlive) continue;
            var area = crystal.Team == Team.Player ? PlayerCrystalCounterArea : EnemyCrystalCounterArea;
            if (area == null) continue;
            // Only the three nearest candidates are required. No all-permutations or all-map search.
            SimUnit first = null, second = null, third = null;
            foreach (var unit in Units)
            {
                if (!unit.IsAlive || unit.Team != attacker || unit.Type != Type.Unit || unit.Kind == Kind.Crystal
                    || !area.Contains(unit.Position)) continue;
                if (Before(unit, first, crystal)) { third = second; second = first; first = unit; }
                else if (Before(unit, second, crystal)) { third = second; second = unit; }
                else if (Before(unit, third, crystal)) third = unit;
            }
            ApplyCounter(first); if (IsTerminal()) return;
            ApplyCounter(second); if (IsTerminal()) return;
            ApplyCounter(third); if (IsTerminal()) return;
        }
    }
    static bool Before(SimUnit candidate, SimUnit current, SimUnit crystal)
        => current == null || MainCrystalRules.CompareTargets(crystal.Position, candidate.Position,
            candidate.LifeIdentity, current.Position, current.LifeIdentity) < 0;
    static void ApplyCounter(SimUnit target)
    { if (target != null && target.IsAlive) target.ApplyDamage(MainCrystalRules.CounterDamage(target.MaxHP)); }
}
