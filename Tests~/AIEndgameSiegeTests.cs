#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Terminal integration scenario. Run last in a disposable, newly started Play Mode game.</summary>
public static class AIEndgameSiegeTests
{
    public static IEnumerator Run(TurnGenerator turn)
    {
        if (!Application.isPlaying || turn == null || !(turn.CurrentState is PlayerMove))
            throw new InvalidOperationException("The siege scenario requires a newly started player turn.");
        var systems = turn.Systems;
        var target = systems.CrystalSystem.Playercrystal.GetComponentInChildren<Status>();
        if (target == null || target.kind != Kind.Crystal || target.team != Team.Player)
            throw new InvalidOperationException("Player main crystal missing.");
        // The defending player passes. Move its field troops away once so this measures siege
        // execution without a simultaneous emergency or a competing win against the king.
        var occupied = new List<Status>();
        CombatRegistry.Collect(occupied);
        foreach (var defender in systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>())
        {
            if (!defender.IsAlive || GridHelper.ChebyshevDistance(defender.transform.position, target.transform.position) >= 8) continue;
            bool deployed = false;
            foreach (var cell in systems.MapCreate.SetPos)
            {
                if (GridHelper.ChebyshevDistance(cell, target.transform.position) < 8
                    || GridHelper.ChebyshevDistance(cell, systems.MoveGenerator.EnemyCrystalPos) < 8
                    || !Free(occupied, cell)) continue;
                defender.transform.position = cell; deployed = true; break;
            }
            if (!deployed) throw new InvalidOperationException("No free reserve square for a defending unit.");
        }
        var attacker = DeployBesideCrystal(systems, target, occupied);
        var previousData = attacker.GrowthData;
        var data = previousData != null ? UnityEngine.Object.Instantiate(previousData) : ScriptableObject.CreateInstance<UnitData>();
        var previousProfile = BoardActionProfile.For(attacker);
        var profile = previousProfile != null ? UnityEngine.Object.Instantiate(previousProfile) : ScriptableObject.CreateInstance<BoardActionProfile>();
        // This stationary test piece isolates target choice, AP and the second owner turn;
        // the ordinary games cover route finding and are reported separately.
        profile.actionType = ActorActionType.Stationary;
        data.actionProfile = profile; attacker.GrowthData = data;
        systems.MoveGenerator.UnitPointCore();
        systems.VisionGenerator.MarkVisionDirty();
        systems.VisionGenerator.VisionPoint(systems.MapCreate, systems.MoveGenerator, systems.CrystalSystem);
        if (!systems.VisionGenerator.IsActorVisibleTo(Team.Enemy, target))
            throw new InvalidOperationException("Deployed enemy does not have legal sight of the player crystal.");

        // Require several fairly priced hits. No further edits are made to either actor or to AP.
        int initialHP = Mathf.Clamp(4 * DamageCalculator.EstimateBaseDamage(attacker.ATK, target.DEF), 32, target.MaxHP);
        target.HP = initialHP; target.Barrier = 0;
        string targetId = target.ReflectionLifeId;
        var attackRounds = new HashSet<int>();
        bool paid = true, observed = true, lastHitWasOurs = false;
        int strikes = 0;
        turn.MarkDeveloperMatch();
        var commander = systems.AICommander;
        var reflection = commander.Reflection;
        if (reflection == null || !reflection.Enabled) throw new InvalidOperationException("Enemy action recording is unavailable.");
        void OnAction(AIActionRecord record)
        {
            if (record.ActionType != AIActionType.Attack || !record.ExecutionSucceeded || record.TargetLifeId != targetId) return;
            observed &= record.Before.TargetObserved;
            paid &= Mathf.Approximately(record.Before.ActorAp - record.After.ActorAp, record.Before.ActionApCost);
            strikes++;
            attackRounds.Add(record.Turn);
            lastHitWasOurs |= target.HP == 0 && record.Before.TargetHP > 0;
            Debug.Log($"[EndgameSiege] HIT enemyRound={record.Turn} {record.Before.TargetHP}->{target.HP} AP={record.Before.ActorAp}->{record.After.ActorAp} quoted={record.Before.ActionApCost}");
        }
        reflection.ActionCompleted += OnAction;
        int firstRound = turn.Context.Turn;
        try
        {
            Debug.Log($"[EndgameSiege] CONTROLLED enemy={attacker.kind} atk={attacker.ATK} targetStartHP={initialHP} stationary=true defenderPass=true");
            while (!turn.IsGameOver && turn.Context.Turn - firstRound < 18)
            {
                if (turn.CurrentState is PlayerMove player) player.ExecuteTurnEnd();
                yield return null;
            }
            if (!(turn.CurrentState is GameEndState end) || !end.OutcomeReason.Contains("味方のメインクリスタル")
                || !lastHitWasOurs || !paid || !observed || attackRounds.Count < 2)
                throw new InvalidOperationException($"Controlled siege did not meet the terminal contract. roundsWithHits={attackRounds.Count}, strikes={strikes}, paid={paid}, observed={observed}, finalHP={target.HP}, result={(turn.CurrentState as GameEndState)?.OutcomeReason}");
            Debug.Log($"[EndgameSiege] PASS actual enemy normal attacks across {attackRounds.Count} own turns, strikes={strikes}, {initialHP}->0, exact AP, legal sight, result={end.OutcomeReason}");
        }
        finally
        {
            reflection.ActionCompleted -= OnAction;
            if (attacker != null) attacker.GrowthData = previousData;
            UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(data);
        }
    }

    static Status DeployBesideCrystal(GameSystems systems, Status crystal, List<Status> occupied)
    {
        foreach (var unit in systems.UnitSetting.EnemyUnit.GetComponentsInChildren<Status>())
        {
            if (!unit.IsAlive || unit.type != Type.Unit || MatchObjectiveRules.IsCommander(unit) || !AICapabilityRoles.For(unit).CanAttack) continue;
            var start = unit.transform.position;
            foreach (var cell in systems.MapCreate.SetPos)
            {
                var offset = crystal.transform.position - cell;
                if (!Free(occupied, cell) || !AttackPatterns.CanAttack(unit, unit.direction, offset.x, offset.z)) continue;
                unit.transform.position = cell;
                systems.VisionGenerator.RefreshActorVision(unit, systems.MapCreate, systems.CrystalSystem);
                if (systems.MapCreate.CanAttackAcrossTerrain(unit, crystal.transform.position) && unit.VisionCell.Contains(GridHelper.ToGrid(crystal.transform.position))) return unit;
                unit.transform.position = start;
            }
        }
        throw new InvalidOperationException("No deployed combatant has a legal firing position with sight.");
    }

    static bool Free(List<Status> occupied, Vector3 cell)
    {
        foreach (var actor in occupied)
            if (actor != null && actor.IsAlive && GridHelper.MatchXZ(actor.transform.position, GridHelper.ToGridXZ(cell))) return false;
        return true;
    }
}
#endif
