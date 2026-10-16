#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

public static class NaturalRecoveryTests
{
    static int passed;
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static void Check(string name, bool value)
    { if (!value) throw new Exception("[NaturalRecovery] " + name); passed++; }
    static Status Actor(int hp = 50)
    {
        var go = new GameObject("Natural recovery fixture"); owned.Add(go);
        var actor = go.AddComponent<Status>(); actor.kind = Kind.Archer; actor.type = Type.Unit;
        actor.team = Team.Player; actor.HP = hp; actor.MaxHP = 100;
        return actor;
    }
    static void Ready(Status actor)
        => actor.NaturalRecovery = new NaturalRecoveryState { HasTurnBaseline = true, ConsecutiveSafeTurns = 5 };

    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        int originalAP = systems.FactionState.GetAP(Team.Player);
        try
        {
            var actor = Actor();
            NaturalRecoverySystem.ProcessActor(actor, 10);
            Check("spawn turn creates baseline without awarding elapsed time", actor.HP == 50 && actor.NaturalRecovery.ConsecutiveSafeTurns == 0);
            for (int round = 11; round <= 14; round++) NaturalRecoverySystem.ProcessActor(actor, round);
            Check("four completed intervals cannot heal", actor.HP == 50 && actor.NaturalRecovery.ConsecutiveSafeTurns == 4);
            NaturalRecoverySystem.ProcessActor(actor, 15);
            Check("fifth completed safe interval restores maximum HP ten percent", actor.HP == 60);
            NaturalRecoverySystem.ProcessActor(actor, 15);
            Check("repeated same turn cannot heal twice", actor.HP == 60);
            NaturalRecoverySystem.ProcessActor(actor, 16);
            Check("safe later owner turns continue recovery", actor.HP == 70);
            actor.HP--;
            Check("even an external one HP loss resets immediately", actor.NaturalRecovery.ConsecutiveSafeTurns == 0 && actor.NaturalRecovery.HpLostSinceLastOwnTurn);
            actor.ApplyHeal(1);
            NaturalRecoverySystem.ProcessActor(actor, 17);
            Check("healing does not erase prior loss in the interval", actor.HP == 70 && actor.NaturalRecovery.ConsecutiveSafeTurns == 0);
            for (int round = 18; round <= 21; round++) NaturalRecoverySystem.ProcessActor(actor, round);
            Check("loss restarts all five full safe intervals", actor.HP == 70);
            NaturalRecoverySystem.ProcessActor(actor, 22);
            Check("recovery resumes after new five safe intervals", actor.HP == 80);
            actor.MaxHP = 105; actor.HP = 100; NaturalRecoverySystem.ProcessActor(actor, 23);
            Check("recovery never exceeds maximum and rounds down", actor.HP == 105 && NaturalRecoverySystem.HealAmount(105) == 10);
            Check("percentage calculation cannot overflow", NaturalRecoverySystem.HealAmount(int.MaxValue) == 214748364);
            foreach (var kind in new[] { HpChangeKind.NormalDamage, HpChangeKind.IgnoreDefenceDamage, HpChangeKind.DirectHpReduction })
            {
                var target = Actor(100); Ready(target);
                SpecialSkillRuntime.ApplyHpChange(actor, target, 1, kind);
                Check(kind + " resets only after actual HP loss", target.HP == 99 && target.NaturalRecovery.ConsecutiveSafeTurns == 0);
            }
            var guarded = Actor(100); Ready(guarded);
            guarded.Special.damageNullUnused = true; guarded.Special.damageNullTurns = 2;
            SpecialSkillRuntime.ApplyHpChange(actor, guarded, 100, HpChangeKind.NormalDamage);
            Check("fully nullified damage preserves safe count", guarded.HP == 100 && guarded.NaturalRecovery.ConsecutiveSafeTurns == 5 && !guarded.NaturalRecovery.HpLostSinceLastOwnTurn);
            guarded.ShieldTurns = 2;
            SpecialSkillRuntime.ApplyHpChange(actor, guarded, 100, HpChangeKind.IgnoreDefenceDamage);
            Check("fully shielded damage preserves safe count", guarded.HP == 100 && guarded.NaturalRecovery.ConsecutiveSafeTurns == 5);
            SpecialSkillRuntime.ApplyHpChange(actor, guarded, 1, HpChangeKind.DirectHpReduction);
            Check("direct sacrifice cost bypasses shield and resets recovery", guarded.HP == 99 && guarded.NaturalRecovery.ConsecutiveSafeTurns == 0);
            var poisoned = Actor(100); Ready(poisoned);
            poisoned.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Poison, 2)); StatusEffectSystem.TickEffects(poisoned);
            NaturalRecoverySystem.ProcessActor(poisoned, 30);
            Check("owner-turn poison cannot be healed by a stale safe count", poisoned.HP == 92 && poisoned.NaturalRecovery.ConsecutiveSafeTurns == 0);
            var ward = Actor(100); var guardian = Actor(100); Ready(ward); Ready(guardian);
            ward.Special.guardianPartnerId = guardian.ReflectionLifeId; guardian.Special.guardianPartnerId = ward.ReflectionLifeId;
            SpecialSkillRuntime.ApplyHpChange(actor, ward, 10, HpChangeKind.NormalDamage);
            Check("life link resets actual recipient rather than unharmed ward", ward.NaturalRecovery.ConsecutiveSafeTurns == 5 && guardian.NaturalRecovery.ConsecutiveSafeTurns == 0);
            var saveActor = Actor(40);
            saveActor.NaturalRecovery = new NaturalRecoveryState { HasTurnBaseline = true, ConsecutiveSafeTurns = 4, LastProcessedRound = 40 };
            var saved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(saveActor)));
            NaturalRecoverySystem.ProcessActor(saveActor, 41); int continuedHP = saveActor.HP;
            SaveGameApplier.ApplyStatusFields(saveActor, saved); NaturalRecoverySystem.ProcessActor(saveActor, 41);
            Check("load continues same safe interval and recovery amount", saveActor.HP == continuedHP && saveActor.NaturalRecovery.ConsecutiveSafeTurns == 5);
            saveActor.HP--; saved = SaveSystem.CaptureUnit(saveActor);
            saveActor.NaturalRecovery.HpLostSinceLastOwnTurn = false;
            SaveGameApplier.ApplyStatusFields(saveActor, saved); NaturalRecoverySystem.ProcessActor(saveActor, 42);
            Check("load retains a pending HP loss", saveActor.NaturalRecovery.ConsecutiveSafeTurns == 0);
            saved.NaturalRecovery = null; SaveGameApplier.ApplyStatusFields(saveActor, saved);
            Check("legacy saves start with no invented safe history", !saveActor.NaturalRecovery.HasTurnBaseline && saveActor.NaturalRecovery.ConsecutiveSafeTurns == 0);
            var building = Actor(); building.type = Type.Building; Ready(building);
            NaturalRecoverySystem.ProcessActor(building, 50);
            Check("buildings do not receive unit natural recovery", building.HP == 50);
            var dead = Actor(0); Ready(dead); NaturalRecoverySystem.ProcessActor(dead, 50);
            Check("natural recovery cannot revive defeated actors", dead.HP == 0);
            var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data);
            actor.GrowthData = data; Ready(actor); data.InitializeAbilities(actor);
            Check("new unit life starts with an independent recovery clock", !actor.NaturalRecovery.HasTurnBaseline && actor.NaturalRecovery.ConsecutiveSafeTurns == 0);
            string prefabPath = "Assets/Prefabs/Units/Player/Knight.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            var serializedHP = Regex.Match(File.ReadAllText(prefabPath), @"(?m)^  (?:HP|hitPoints): (-?\d+)\r?$");
            Check("existing prefab serialized HP survives field migration", prefab != null && serializedHP.Success
                && prefab.GetComponentInChildren<Status>(true).HP == int.Parse(serializedHP.Groups[1].Value));
            Check("one faction AP cannot exceed sixty", APSystem.MaximumAP <= 60);
            Check("legacy skills share individual maximum AP25", systems.APSystem.CalcSkillCost(1000, actor) == 25 && APSystem.MaximumSkillAP == 25);
            systems.FactionState.SetAP(Team.Player, 60);
            int before = systems.FactionState.GetAP(Team.Player); systems.APSystem.ConsumeSkill(Team.Player, 1000, actor);
            Check("individual legacy skill actually charges at most25", before - systems.FactionState.GetAP(Team.Player) == 25);
            Simulation();
            Debug.Log("[NaturalRecovery] " + passed + " runtime/save/simulation/compatibility checks passed");
        }
        finally
        {
            systems.FactionState.SetAP(Team.Player, originalAP);
            foreach (var value in owned) if (value != null) UnityEngine.Object.DestroyImmediate(value);
            owned.Clear(); systems.RefreshVision();
        }
    }

    static void Simulation()
    {
        var unit = new SimUnit { Id = 1, Kind = Kind.Archer, Team = Team.Enemy, Type = Type.Unit, HP = 40, MaxHP = 100,
            NaturalRecoveryHasBaseline = true, NaturalRecoverySafeTurns = 4, NaturalRecoveryLastRound = 40 };
        var board = new SimBoardState { Units = new List<SimUnit> { unit }, TurnCount = 40,
            EnemyBuildingCounts = new Dictionary<FacilityKind,int>(), PlayerBuildingCounts = new Dictionary<FacilityKind,int>(),
            MapTiles = new HashSet<Vector3Int>() };
        board.RebuildOccupied(); var copy = board.Clone();
        try
        {
            board.SimulateTurnTransition(Team.Enemy);
            Check("minimax owner boundary heals at same fifth interval", unit.HP == 50 && unit.NaturalRecoverySafeTurns == 5);
            copy.Units[0].ApplyDamage(1); copy.SimulateTurnTransition(Team.Enemy);
            Check("search clone loss resets only its own clock", copy.Units[0].HP == 39 && copy.Units[0].NaturalRecoverySafeTurns == 0 && unit.NaturalRecoverySafeTurns == 5);
            unit.Effects.Add(new SimEffect { Debuff = StatusEffectType.Poison, RemainingTurns = 2 });
            board.SimulateTurnTransition(Team.Enemy);
            Check("simulated poison precedes natural healing", unit.HP == 42 && unit.NaturalRecoverySafeTurns == 0);
            var hash = typeof(AIMinimaxEngine).GetMethod("BoardHash", BindingFlags.Static | BindingFlags.NonPublic);
            long first = (long)hash.Invoke(null, new object[] { board }); unit.NaturalRecoverySafeTurns = 4;
            Check("search cache distinguishes different future recovery", first != (long)hash.Invoke(null, new object[] { board }));
        }
        finally { SimBoardPool.ReturnBoard(copy); SimBoardPool.ReturnBoard(board); }
    }
}
#endif
