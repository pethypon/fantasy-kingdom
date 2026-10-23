#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Boundary contracts use an isolated temporary rule proposal; never activate production settings.</summary>
public static class BarrierSystemTests
{
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static int count;
    static void Check(string label, bool value)
    { if (!value) throw new Exception("[Barrier] FAIL " + label); count++; Debug.Log("[Barrier] PASS " + label); }
    static Status Actor(Kind kind = Kind.Knight, Team team = Team.Player)
    {
        var go = new GameObject("Barrier contract fixture"); owned.Add(go);
        var actor = go.AddComponent<Status>(); actor.kind = kind; actor.type = Type.Unit; actor.team = team;
        actor.HP = actor.MaxHP = 100; actor.ATK = 20; actor.DEF = 1000; actor.Level = 1;
        actor.transform.position = new Vector3(1000 + owned.Count * 2, 1, 1000);
        actor.ResetBarrier(); return actor;
    }
    static SpecialSkillDefinition Ability(Status actor, UnitSpecialSkill ability)
    {
        var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data);
        data.kind = actor.kind; data.baseHP = 100; data.baseATK = actor.ATK;
        var definition = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); owned.Add(definition);
        definition.ability = ability; definition.apCost = 1; definition.cooldown = 0;
        definition.samuraiGaugeMax = 2; definition.samuraiGaugeGain = 1;
        data.specialSkill = definition; actor.GrowthData = data;
        SpecialSkillRuntime.InvalidateRules(); return definition;
    }
    public static void Run(GameSystems systems)
    {
        count = 0;
        var config = ScriptableObject.CreateInstance<BarrierCombatSettings>(); owned.Add(config);
        config.PopulateTrialValues(); config.enabledForPlay = true;
        using var rules = BarrierCombatSettings.UseForValidation(config);
        var random = UnityEngine.Random.state;
        try
        {
            Check("18 confirmed R1 role entries", config.specificationRevision == 1 && config.initialBarriers.Count == 18
                && config.Initial(Kind.Knight) == 70 && config.Initial(Kind.Assassin) == 5);
            Check("common +2 each level", BarrierSystem.Maximum(70, 1) == 70 && BarrierSystem.Maximum(70, 10) == 88
                && BarrierSystem.Maximum(70, 50) == 168 && BarrierSystem.Maximum(70, 100) == 268
                && BarrierSystem.Maximum(5, 100) == 203);
            Check("ceil regeneration without integer overflow", BarrierSystem.RegenerationAmount(9) == 2
                && BarrierSystem.RegenerationAmount(int.MaxValue) == 429496730);
            Check("70 percent exact boundary", !BarrierSystem.CanRegenerate(69, 100, 3) && BarrierSystem.CanRegenerate(70, 100, 3)
                && !BarrierSystem.CanRegenerate(70, 100, 2));
            int[] confirmed = {70,25,30,5,20,20,15,40,20,40,15,50,30,10,30,30,50,30};
            for (int role = 0; role < UnitRoster.SummonableKinds.Length; role++)
            {
                var kind = UnitRoster.SummonableKinds[role];
                Check("confirmed initial value " + kind, config.Initial(kind) == confirmed[role]);
                for (int level = 1; level <= 100; level++)
                    if (BarrierSystem.Maximum(confirmed[role], level) != confirmed[role] + 2*(level-1))
                        throw new Exception("R1 level table mismatch " + kind + " Lv" + level);
            }
            var target = Actor(); var targetData = ScriptableObject.CreateInstance<UnitData>(); owned.Add(targetData);
            targetData.kind = target.kind; targetData.baseHP = 100; targetData.baseATK = 20; targetData.baseBarrier = 30; target.GrowthData = targetData; target.ResetBarrier(); target.Barrier = 8;
            int total = SpecialSkillRuntime.ApplyHpChange(null, target, 20, HpChangeKind.NormalDamage);
            Check("8 barrier then 12 HP overflow", target.Barrier == 0 && target.HP == 88 && total == 20);
            Check("HP event records both channels", SpecialSkillRuntime.LastHpChange.ActualBarrierLoss == 8
                && SpecialSkillRuntime.LastHpChange.ActualHpLoss == 12 && SpecialSkillRuntime.LastHpChange.TotalDamage == 20);
            target.HP = 100; target.ResetBarrier(); target.NaturalRecovery = new NaturalRecoveryState();
            total = target.ApplyDamage(5);
            Check("absorbed damage counts as a hit without HP loss", total == 5 && target.HP == 100 && target.Barrier == 25);
            Check("barrier hit resets barrier clock only", target.BarrierRecovery.DamageSinceOwnTurn
                && !target.NaturalRecovery.HpLostSinceLastOwnTurn && !SpecialSkillRuntime.LastHpChange.WasCompletelyNegated);
            BarrierSystem.ProcessActorEnd(target, 1);
            Check("damaged interval does not count as safe", target.BarrierRecovery.SafeTurns == 0 && target.Barrier == 25);
            BarrierSystem.ProcessActorEnd(target, 2); BarrierSystem.ProcessActorEnd(target, 3);
            Check("two safe own ends do not regenerate", target.Barrier == 25 && target.BarrierRecovery.SafeTurns == 2);
            BarrierSystem.ProcessActorEnd(target, 4);
            Check("third safe own end regenerates20percent capped", target.Barrier == 30);
            target.Barrier = 20; BarrierSystem.ProcessActorEnd(target, 4);
            Check("same own end cannot heal twice", target.Barrier == 20);
            target.HP = 69; target.BarrierRecovery = new BarrierRecoveryState { SafeTurns = 3 };
            BarrierSystem.ProcessActorEnd(target, 5);
            Check("below70percent prevents barrier recovery", target.Barrier == 20);
            target.HP = 70; BarrierSystem.ProcessActorEnd(target, 6);
            Check("70percent permits next tick", target.Barrier == 26);
            target.ShieldTurns = 1; target.BarrierRecovery.SafeTurns = 3; target.BarrierRecovery.DamageSinceOwnTurn = false;
            int before = target.Barrier;
            Check("perfect shield absorbs before finite barrier", target.ApplyDamage(100) == 0 && target.Barrier == before && target.HP == 70);
            Check("complete negation preserves safe count", !target.BarrierRecovery.DamageSinceOwnTurn && target.BarrierRecovery.SafeTurns == 3);
            target.ShieldTurns = 0; SpecialSkillRuntime.State(target).damageNullUnused = true; SpecialSkillRuntime.State(target).damageNullTurns = 1;
            Check("single-use nullification preserves barrier", target.ApplyDamage(100) == 0 && target.Barrier == before);
            Check("nullification consumed once", !SpecialSkillRuntime.State(target).damageNullUnused);
            SpecialSkillRuntime.ApplyHpChange(null, target, 10, HpChangeKind.DirectHpReduction);
            Check("self cost changes HP only", target.HP == 60 && target.Barrier == before);
            target.HP = 100; target.ResetBarrier(); target.Barrier = 8;
            Check("preview returns HP overflow", SpecialSkillRuntime.PreviewHpDamage(target, 20) == 12);
            Check("dedicated bypass preview ignores barrier", SpecialSkillRuntime.PreviewHpDamage(target, 20, HpChangeKind.BarrierBypassDamage) == 20);
            SpecialSkillRuntime.ApplyHpChange(null, target, 20, HpChangeKind.BarrierBypassDamage);
            Check("dedicated bypass leaves barrier intact", target.HP == 80 && target.Barrier == 8);
            var attacker = Actor(); attacker.ATK = 20; target.HP = 100;
            int ordinary = DamageCalculator.CalcNormal(attacker, target); target.DEF = 0;
            Check("ordinary role DEF no longer changes damage", DamageCalculator.CalcNormal(attacker, target) == ordinary && ordinary > 1);
            var king = Actor(Kind.King);
            Check("commanders do not receive an unconfirmed barrier", !BarrierSystem.UsesBarrier(king) && king.MaxBarrier == 0);
            int kingHit = DamageCalculator.CalcNormal(attacker, king); king.DEF = 0;
            Check("commander DEF is abolished too", kingHit == DamageCalculator.CalcNormal(attacker, king) && kingHit > 1);
            target.type = Type.Building; target.DEF = 1000;
            int buildingHit = DamageCalculator.CalcNormal(attacker, target); target.DEF = 0;
            Check("buildings use HP without DEF or invented barrier", !BarrierSystem.UsesBarrier(target)
                && buildingHit == DamageCalculator.CalcNormal(attacker, target) && buildingHit > 1);
            Check("R1 value estimates ignore old DEF", DamageCalculator.EstimateBaseDamage(20, 1000) == DamageCalculator.EstimateBaseDamage(20, 0));
            target.type = Type.Unit;
            Check("breaker bonus cannot create HP overflow", BarrierSystem.AmplifyBarrierPortion(10, 15, 2) == 15
                && BarrierSystem.AmplifyBarrierPortion(20, 8, 2) == 20);
            target.HP = 100; target.ResetBarrier(); target.Barrier = 9; target.BarrierRecovery.SafeTurns = 2;
            var snapshot = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(target)));
            target.Barrier = 0; SaveGameApplier.ApplyStatusFields(target, snapshot);
            Check("save roundtrip retains depleted barrier and clock", target.Barrier == 9 && target.BarrierRecovery.SafeTurns == 2);
            snapshot.HasBarrierSnapshot = false; SaveGameApplier.ApplyStatusFields(target, snapshot);
            Check("legacy saves receive definition maximum once", target.Barrier == target.MaxBarrier);
            target.Barrier = 0; snapshot = SaveSystem.CaptureUnit(target); SaveGameApplier.ApplyStatusFields(target, snapshot);
            Check("depleted new save is never mistaken for old missing field", target.Barrier == 0);
            target.BarrierRecovery.SafeTurns = 3; SpecialSkillRuntime.ApplyHpChange(null, target, 1, HpChangeKind.NormalDamage, cause: HpChangeCause.Poison);
            Check("poison resets shared barrier damage clock", target.BarrierRecovery.SafeTurns == 0);
            var dragon = Actor(Kind.DragonKnight); Ability(dragon, UnitSpecialSkill.DragonKnightPierce);
            Check("dragon begins empty", SpecialSkillRuntime.State(dragon).dragonKnightGauge == 0);
            SpecialSkillRuntime.OnNormalAttackResolved(dragon, target, 0, false);
            Check("using a blocked normal attack still charges once", SpecialSkillRuntime.State(dragon).dragonKnightGauge == 1);
            SpecialSkillRuntime.OnNormalAttackResolved(dragon, target, 5, false);
            SpecialSkillRuntime.OnNormalAttackResolved(dragon, target, 5, false);
            Check("dragon gauge caps at2", SpecialSkillRuntime.State(dragon).dragonKnightGauge == 2);
            var copied = SpecialSkillRuntime.CopyState(SpecialSkillRuntime.State(dragon));
            Check("dragon gauge survives state copy", copied.dragonKnightGauge == 2);
            copied.dragonKnightGauge = 100;
            Check("malformed gauge clamps on load", SpecialSkillRuntime.CopyState(copied).dragonKnightGauge == 2);
            var samurai = Actor(Kind.Samurai); Ability(samurai, UnitSpecialSkill.SamuraiCounter);
            var enemy = Actor(Kind.Knight, Team.Enemy); enemy.Barrier = enemy.MaxBarrier;
            SpecialSkillRuntime.State(samurai).samuraiGauge = 2; SpecialSkillRuntime.State(samurai).samuraiMaxTurns = 3;
            SpecialSkillRuntime.ApplyHpChange(enemy, samurai, 4, HpChangeKind.NormalDamage);
            Check("passive counter strips barrier before HP damage", enemy.Barrier == 0 && enemy.HP < 100);
            Check("counter never changes maximum barrier", enemy.MaxBarrier == 70);
            enemy.HP = 100; enemy.ResetBarrier(); samurai.HP = 100; samurai.ResetBarrier();
            SpecialSkillRuntime.State(enemy).tauntedById = samurai.ReflectionLifeId; SpecialSkillRuntime.State(enemy).tauntTurns = 2;
            SpecialSkillRuntime.State(samurai).samuraiGauge = 0;
            SpecialSkillRuntime.ApplyHpChange(enemy, samurai, 4, HpChangeKind.NormalDamage);
            Check("active challenge counter also strips barrier", enemy.Barrier == 0 && enemy.HP < 100);
            enemy.HP = 100; enemy.ResetBarrier(); enemy.ShieldTurns = 1;
            SpecialSkillRuntime.ApplyHpChange(enemy, samurai, 4, HpChangeKind.NormalDamage);
            Check("counter respects perfect protection", enemy.Barrier == 70 && enemy.HP == 100);
            SkillCycle(systems);
            UpkeepDefection();
            target.HP = 10; target.Barrier = 4;
            SpecialSkillRuntime.ApplyHpChange(null, target, 100, HpChangeKind.NormalDamage);
            Check("overkill returns actual losses", SpecialSkillRuntime.LastHpChange.ActualHpLoss == 10
                && SpecialSkillRuntime.LastHpChange.ActualBarrierLoss == 4 && !target.IsAlive);
            Check("dead unit cannot regenerate", target.Barrier == 0);
            Check("save migration chain includes barrier revision", SaveSystem.ValidateMigrationChain(out _) && SaveSystem.CurrentSaveVersion >= 10);
            Debug.Log("[Barrier] ALL PASSED " + count);
        }
        finally
        {
            UnityEngine.Random.state = random;
            foreach (var value in owned) if (value != null) UnityEngine.Object.DestroyImmediate(value);
            owned.Clear(); SpecialSkillRuntime.InvalidateRules();
        }
    }
    static void UpkeepDefection()
    {
        var go = new GameObject("Barrier upkeep contract"); owned.Add(go);
        var unitSetting = go.AddComponent<UnitSetting>(); unitSetting.PlayerUnit = go.transform;
        var factions = go.AddComponent<FactionState>();
        factions.PlayerNation = go.AddComponent<NationState>();
        var economy = go.AddComponent<EconomySystem>(); economy.Init(null, factions, unitSetting);
        var actor = Actor(); actor.transform.SetParent(go.transform);
        var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data);
        data.kind = actor.kind; data.baseHP = 100; data.baseATK = 20; data.baseBarrier = 200;
        actor.GrowthData = data; actor.ResetBarrier(); actor.ShieldTurns = 3;
        var state = SpecialSkillRuntime.State(actor); state.damageNullUnused = true; state.damageNullTurns = 3;
        var partner = Actor(); state.guardianPartnerId = partner.ReflectionLifeId;
        SpecialSkillRuntime.State(partner).guardianPartnerId = actor.ReflectionLifeId;
        actor.UpkeepUnpaidTurns = GameConstants.UpkeepPenaltyDefectTurns - 2;
        var process = typeof(EconomySystem).GetMethod("ProcessUnitMaintenance", BindingFlags.Instance | BindingFlags.NonPublic);
        if (process == null) throw new MissingMethodException("EconomySystem.ProcessUnitMaintenance");
        var args = new object[] { Team.Player, factions.PlayerResources };
        process.Invoke(economy, args);
        Check("ninth unpaid turn preserves live protected unit", actor.IsAlive && actor.gameObject.activeSelf
            && actor.UpkeepUnpaidTurns == 9 && actor.Barrier == 200);
        process.Invoke(economy, args);
        Check("tenth unpaid turn removes unit despite barrier and protection", !actor.IsAlive && !actor.gameObject.activeSelf
            && actor.HP == 0 && actor.Barrier == 0);
        Check("economic defection never redirects to linked ally", partner.HP == 100 && partner.Barrier == partner.MaxBarrier);
    }
    static void SkillCycle(GameSystems live)
    {
        var selection = live.TurnGenerator.Context.SelectUnit;
        var battleTarget = live.BattleSystem.Target;
        var originalMap = live.MapCreate;
        int ap = live.FactionState.GetAP(Team.Player);
        try
        {
            // Mechanical fixtures share the real battle/AP command entry points while removing terrain as a variable.
            live.MapCreate = null;
            var dragon = Actor(Kind.DragonKnight); Ability(dragon, UnitSpecialSkill.DragonKnightPierce);
            var victim = Actor(Kind.Archer, Team.Enemy); victim.HP = victim.MaxHP = 1000;
            var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data);
            data.kind = victim.kind; data.baseHP = 1000; data.baseBarrier = 200; victim.GrowthData = data; victim.ResetBarrier();
            victim.transform.position = dragon.transform.position + Vector3.forward;
            dragon.VisionCell.Add(victim.GridPosition);
            live.TurnGenerator.Context.SelectUnit = dragon; live.BattleSystem.SetTarget(victim);
            live.BattleSystem.ProcessDamage(live.TurnGenerator);
            Check("actual first normal attack only drains barrier and charges1", victim.HP == 1000 && victim.Barrier < 200
                && SpecialSkillRuntime.State(dragon).dragonKnightGauge == 1);
            live.BattleSystem.ProcessDamage(live.TurnGenerator);
            Check("actual second normal attack charges2 without HP bypass", victim.HP == 1000 && victim.Barrier > 0
                && SpecialSkillRuntime.State(dragon).dragonKnightGauge == 2);
            var commands = new GameSystems { FactionState = live.FactionState, APSystem = live.APSystem };
            live.FactionState.SetAP(Team.Player, 60);
            int barrier = victim.Barrier, hp = victim.HP, beforeAP = live.FactionState.GetAP(Team.Player);
            Check("dragon skill command succeeds when full", SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(dragon, victim), commands, out _));
            Check("dragon skill preserves barrier and consumes gauge/AP", victim.HP < hp && victim.Barrier == barrier
                && SpecialSkillRuntime.State(dragon).dragonKnightGauge == 0 && live.FactionState.GetAP(Team.Player) == beforeAP - 1);
            Check("dragon cannot use skill again without two normal attacks", !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(dragon, victim), commands, out _));
            var samurai = Actor(Kind.Samurai, Team.Enemy); Ability(samurai, UnitSpecialSkill.SamuraiCounter);
            samurai.transform.position = dragon.transform.position + Vector3.forward;
            SpecialSkillRuntime.State(samurai).samuraiGauge = 2; SpecialSkillRuntime.State(samurai).samuraiMaxTurns = 3;
            dragon.Barrier = 0; dragon.HP = 1; live.BattleSystem.SetTarget(samurai);
            live.BattleSystem.ProcessDamage(live.TurnGenerator);
            Check("lethal counter cannot recharge dead dragon", !dragon.IsAlive && SpecialSkillRuntime.State(dragon).dragonKnightGauge == 0);
            var heavy = Actor(Kind.HeavyWarrior); Ability(heavy, UnitSpecialSkill.HeavyWarriorBreak);
            var linked = Actor(Kind.Knight, Team.Enemy); var partner = Actor(Kind.Archer, Team.Enemy);
            linked.Barrier = linked.MaxBarrier; partner.Barrier = 0;
            SpecialSkillRuntime.State(linked).guardianPartnerId = partner.ReflectionLifeId;
            SpecialSkillRuntime.State(partner).guardianPartnerId = linked.ReflectionLifeId;
            int raw = DamageCalculator.CalcNormal(heavy, linked);
            Check("breaker never transfers bonus barrier damage into linked HP", SpecialSkillRuntime.PreviewDedicatedSkillDamage(heavy, linked) == raw);
            var settings = BarrierCombatSettings.Active;
            settings.barrierDamageGrantsExperience = false;
            victim.HP = 1000; victim.ResetBarrier();
            int damage = SpecialSkillRuntime.ApplyHpChange(heavy, victim, 5, HpChangeKind.NormalDamage);
            Check("HP-only XP option excludes absorbed damage", SpecialSkillRuntime.ExperienceDamage(heavy, victim, damage) == 0);
            settings.barrierDamageGrantsExperience = true;
            Check("barrier XP option includes absorbed damage", SpecialSkillRuntime.ExperienceDamage(heavy, victim, damage) == 5);
        }
        finally
        {
            live.MapCreate = originalMap; live.FactionState.SetAP(Team.Player, ap);
            live.TurnGenerator.Context.SelectUnit = selection; live.BattleSystem.SetTarget(battleTarget);
        }
    }

}
#endif
