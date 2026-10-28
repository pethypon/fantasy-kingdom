#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Registered development contracts. Added with R1; not an executed playtest or acceptance result.</summary>
public static class IntegratedCrystalRulesTests
{
    static int checks;
    static void Check(string message, bool valid)
    { if (!valid) throw new InvalidOperationException("[IntegratedCrystalRules] " + message); checks++; }

    public static void Run(GameSystems unused)
    {
        checks = 0;
        Check("one formal main-crystal HP", CrystalSystem.CrystalHP == 5000 && MainCrystalRules.MaximumHP == 5000);
        Check("old 15000 health ratio is retained", MainCrystalRules.NormalizeSavedHP(7500, 15000, 0) == 2500);
        Check("dead saves are never revived", MainCrystalRules.NormalizeSavedHP(0, 15000, 0) == 0);
        Check("current-rule saves retain exact HP", MainCrystalRules.NormalizeSavedHP(1234, 5000, MainCrystalRules.Version) == 1234);
        Check("legacy CrystalShard max-HP gain survives migration", MainCrystalRules.NormalizeSavedMaximum(16000, 0) == 6000
            && MainCrystalRules.NormalizeSavedHP(8000, 16000, 0) == 3000);
        Check("new-rule artifact gain is not migrated twice", MainCrystalRules.NormalizeSavedMaximum(6000, MainCrystalRules.Version) == 6000
            && MainCrystalRules.NormalizeSavedHP(4321, 6000, MainCrystalRules.Version) == 4321);
        Check("counter uses target maximum HP and existing rounding", MainCrystalRules.CounterDamage(101) == Mathf.RoundToInt(101 * .15f));
        foreach (var side in new[] { Team.Player, Team.Enemy })
        {
            StateIdentity(side);
            RuntimeEnd(side);
            SimulationEnd(side);
        }
        Debug.Log("[IntegratedCrystalRules] PASS checks=" + checks);
    }

    static void StateIdentity(Team side)
    {
        var state = new MainCrystalCounterState();
        state.Register(side, 4); state.Register(side, 4);
        Check(side + " repeated hits create one pending owner turn", state.PendingRound(side) == 4);
        var loaded = JsonUtility.FromJson<MainCrystalCounterState>(JsonUtility.ToJson(state));
        Check(side + " save keeps the pending counter", loaded.TryConsume(side, 4));
        Check(side + " end-turn replay cannot repeat after load", !loaded.TryConsume(side, 4));
        loaded.Register(side, 4);
        Check(side + " registering after consumed turn cannot rearm it", !loaded.TryConsume(side, 4));
        loaded.Register(side, 5);
        Check(side + " another owner turn can counter", loaded.TryConsume(side, 5));
        var swapped = state.Copy(true);
        Check(side + " player perspective translates the pending faction", swapped.PendingRound(side == Team.Player ? Team.Enemy : Team.Player) == 4);
    }

    static Status Actor(Transform parent, Kind kind, Team side, int hp, int x)
    {
        var go = new GameObject("R1 " + kind + " " + x); go.transform.SetParent(parent);
        go.transform.position = new Vector3(x, 0, 0);
        var actor = go.AddComponent<Status>(); actor.kind = kind; actor.type = Type.Unit; actor.team = side;
        actor.HP = actor.MaxHP = hp; actor.ATK = actor.DEF = 0;
        actor.RestoreReflectionLifeId("r1-" + side + "-" + kind + "-" + x);
        return actor;
    }

    static void RuntimeEnd(Team attacker)
    {
        var host = new GameObject("R1 crystal transaction");
        try
        {
            var crystals = host.AddComponent<CrystalSystem>(); var units = host.AddComponent<UnitSetting>();
            var territory = host.AddComponent<TerritorySystem>();
            territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
            var ownHolder = new GameObject("R1 owned crystal"); ownHolder.transform.SetParent(host.transform);
            var otherHolder = new GameObject("R1 other crystal"); otherHolder.transform.SetParent(host.transform);
            crystals.Playercrystal = attacker == Team.Player ? otherHolder.transform : ownHolder.transform;
            crystals.Enemycrystal = attacker == Team.Player ? ownHolder.transform : otherHolder.transform;
            var army = new GameObject("R1 attacking army"); army.transform.SetParent(host.transform);
            units.PlayerUnit = attacker == Team.Player ? army.transform : otherHolder.transform;
            units.EnemyUnit = attacker == Team.Enemy ? army.transform : otherHolder.transform;
            var defender = attacker == Team.Player ? Team.Enemy : Team.Player;
            var crystal = Actor(ownHolder.transform, Kind.Crystal, defender, 5000, 0);
            var a = Actor(army.transform, Kind.Knight, attacker, 100, 1);
            var b = Actor(army.transform, Kind.Archer, attacker, 200, 2);
            var c = Actor(army.transform, Kind.Magic, attacker, 300, 3);
            var d = Actor(army.transform, Kind.Knight, attacker, 400, 4);
            foreach (var actor in new[] { a, b, c, d }) territory.GetTerritory(defender).Add(actor.transform.position);
            var systems = new GameSystems { CrystalSystem = crystals, UnitSetting = units, TerritorySystem = territory };
            crystal.ShieldTurns = 5; crystal.ShieldActivated = crystal.ShieldEverActivated = true;
            MainCrystalRules.ClearLegacyShield(crystal);
            Check(attacker + " main crystal cannot retain old invulnerability", !crystal.HasDamageShield && !crystal.ShieldActivated && crystal.ShieldTurns == 0);
            d.ShieldTurns = 5;
            Check(attacker + " another unit's perfect shield is retained", d.HasDamageShield);
            MainCrystalRules.RegisterAttack(crystal, attacker, 12); MainCrystalRules.RegisterAttack(crystal, attacker, 12);
            BattleSystem.ProcessCrystalCounter(systems, crystal, CrystalCounterTrigger.OnHit);
            BattleSystem.ProcessCrystalCounterAtTurnStart(systems, attacker);
            Check(attacker + " hit and start callbacks do not counter", a.HP == 100 && b.HP == 200 && c.HP == 300);
            BattleSystem.ProcessCrystalCounterAtTurnEnd(systems, attacker, 12);
            Check(attacker + " exactly nearest three receive their own max-HP fraction", a.HP == 85 && b.HP == 170 && c.HP == 255 && d.HP == 400);
            BattleSystem.ProcessCrystalCounterAtTurnEnd(systems, attacker, 12);
            Check(attacker + " repeated owner end is idempotent", a.HP == 85 && b.HP == 170 && c.HP == 255);
            BattleSystem.ProcessCrystalCounterAtTurnEnd(systems, attacker, 13);
            Check(attacker + " an unattacked later turn cannot fire", a.HP == 85);
            MainCrystalRules.RegisterAttack(crystal, attacker, 14); crystal.HP = 0;
            BattleSystem.ProcessCrystalCounterAtTurnEnd(systems, attacker, 14);
            Check(attacker + " destroyed main crystal never counters", a.HP == 85 && crystal.CrystalCounter.PendingRound(attacker) == -1);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    static void SimulationEnd(Team attacker)
    {
        var owner = attacker == Team.Player ? Team.Enemy : Team.Player;
        var crystal = new SimUnit { Id = 0, Kind = Kind.Crystal, Type = Type.Unit, Team = owner,
            HP = 5000, MaxHP = 5000, Position = Vector3Int.zero, LifeIdentity = "crystal" };
        var fighter = new SimUnit { Id = 1, Kind = Kind.Knight, Type = Type.Unit, Team = attacker,
            HP = 100, MaxHP = 100, ATK = 10, Position = Vector3Int.right, LifeIdentity = "fighter" };
        var board = new SimBoardState { Units = new List<SimUnit> { crystal, fighter }, TurnCount = 5,
            EnemyAP = 60, PlayerAP = 60, EnemyAPReset = 60, PlayerAPReset = 60,
            EnemyBuildingCounts = new Dictionary<FacilityKind, int>(), PlayerBuildingCounts = new Dictionary<FacilityKind, int>(),
            MapTiles = new HashSet<Vector3Int> { Vector3Int.zero, Vector3Int.right },
            EnemyCrystalCounterArea = new HashSet<Vector3Int> { Vector3Int.right },
            PlayerCrystalCounterArea = new HashSet<Vector3Int> { Vector3Int.right } };
        board.RebuildOccupied();
        var attack = new SimAction { Type = SimActionType.Attack, UnitId = 1, TargetUnitId = 0, ActorTeam = attacker, APCost = 1 };
        Check(attacker + " virtual attack creates delayed counter without immediate damage", board.ApplyAction(attack) && fighter.HP == 100);
        crystal.ShieldTurns = 5;
        Check(attacker + " virtual main crystal ignores obsolete shield", SimBoardState.CalcDamage(fighter, crystal) > 0);
        var clone = board.Clone();
        try
        {
            clone.SimulateTurnTransition(owner);
            Check(attacker + " cloned virtual end applies one 15-percent packet", clone.GetUnit(1).HP == 85);
            Check(attacker + " clone cannot consume the parent's pending counter", board.GetUnit(1).HP == 100 && crystal.CrystalCounter.PendingRound(attacker) == 5);
        }
        finally { SimBoardPool.ReturnBoard(clone); }
        board.SimulateTurnTransition(owner);
        Check(attacker + " virtual source board consumes independently", fighter.HP == 85 && crystal.CrystalCounter.PendingRound(attacker) == -1);
    }
}
#endif
