#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Real Lv1/Lv100 actors, battle/skill entry points, AI estimates and search-state execution.</summary>
public static class MinimumHitDamageTests
{
    public static void Run()
    {
        const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
        var loaded = typeof(GameAuthoringRules).GetField("loaded", hidden);
        var didLoad = typeof(GameAuthoringRules).GetField("didLoad", hidden);
        object previousRules = loaded.GetValue(null), previousLoaded = didLoad.GetValue(null);
        var previousMode = AIConfig.Mode;
        var rules = ScriptableObject.CreateInstance<GameAuthoringRules>(); rules.applyRules = true; rules.minimumAttackDamage = 1;
        var root = new GameObject("Minimum damage isolated battle");
        var lowDefinition = ScriptableObject.CreateInstance<UnitData>();
        var highDefinition = ScriptableObject.CreateInstance<UnitData>();
        int checks = 0;
        void Check(bool passed, string label) { if (!passed) throw new InvalidOperationException("[MinimumDamage] " + label); checks++; }
        Status Actor(string name, Team team, Kind kind, Vector3 position)
        {
            var child = new GameObject(name); child.transform.SetParent(root.transform); child.transform.position = position;
            var value = child.AddComponent<Status>(); value.team = team; value.kind = kind; value.type = Type.Unit;
            value.HP = value.MaxHP = 100; value.Level = 1; return value;
        }
        try
        {
            loaded.SetValue(null, rules); didLoad.SetValue(null, true);
            var hub = root.AddComponent<TurnGenerator>(); hub.enabled = false; hub.MarkDeveloperMatch();
            var attacker = Actor("Lv1 scout", Team.Player, Kind.Scout, new Vector3(10000, 1, 10000));
            var target = Actor("Lv100 knight", Team.Enemy, Kind.Knight, new Vector3(10001, 1, 10000));
            lowDefinition.kind = Kind.Scout; lowDefinition.baseHP = 20; lowDefinition.baseATK = 6; lowDefinition.baseDEF = 9;
            highDefinition.kind = Kind.Knight; highDefinition.baseHP = 32; highDefinition.baseATK = 10; highDefinition.baseDEF = 13;
            lowDefinition.ApplyToStatus(attacker, 1); highDefinition.ApplyToStatus(target, 100);
            Check(attacker.Level == 1 && target.Level == 100 && target.DEF > attacker.ATK * 10, "actual level growth reproduces high defense gap");
            Check(DamageCalculator.CalcRawBase(attacker.ATK, target.DEF) < 0, "regression crosses the former zero-damage boundary");
            Check(DamageCalculator.CalcNormal(attacker, target) == 1 && SkillSystem.CalcNormalDamage(attacker, target) == 1, "live normal calculation guarantees one point");
            Check(AIEvalHelpers.EstimateDamage(attacker, target) == 1 && DamageCalculator.EstimateBaseDamage(attacker.ATK, target.DEF) == 1, "AI simplified predictions share the floor");
            Check(DamageCalculator.CalcFromValues(0, 99999, .7f) == 1, "floor occurs after barrier/reduction and negative base");
            Check(DamageCalculator.FinalizeHitDamage(.1f) == 1, "rounding cannot remove a valid small hit");
            Check(DamageCalculator.EstimateBaseDamage(100, 30) == 61, "ordinary positive base damage remains unchanged");
            Check(DamageCalculator.FinalizeHitDamage(float.MaxValue) == int.MaxValue, "large valid damage saturates instead of integer-overflowing to zero");
            var battle = root.AddComponent<BattleSystem>(); hub.Context.SelectUnit = attacker; battle.SetTarget(target);
            int hp = target.HP; battle.ProcessDamage(hub);
            Check(target.HP == hp - 1, "actual low-level normal attack removes one HP from high-level defender");
            var skillSystem = root.AddComponent<SkillSystem>(); skillSystem.turnGenerator = hub;
            var skill = new SkillData { Name = "Minimum hit regression", Multiplier = 1, Area = SkillAreaShape.Single, Target = SkillTarget.EnemySingle };
            Check(DamageCalculator.CalcSkill(attacker, target, skill) == 1, "live attack skill floor");
            hp = target.HP; skillSystem.ExecuteSkill(attacker, target, skill);
            Check(target.HP == hp - 1, "actual attack skill damages high-level defender");
            var cannon = Actor("Low-attack cannon", Team.Player, Kind.None, new Vector3(10000, 1, 10000));
            cannon.type = Type.Building; cannon.facilityKind = FacilityKind.Cannon; cannon.ATK = 1;
            var buildingAttack = root.AddComponent<BuildingAttackSystem>(); buildingAttack.Init(null, root.AddComponent<MoveGenerator>(), null, null, null, null);
            var applyBuilding = typeof(BuildingAttackSystem).GetMethod("ApplyBuildingDamage", BindingFlags.Instance | BindingFlags.NonPublic);
            hp = target.HP; applyBuilding.Invoke(buildingAttack, new object[] { cannon, target });
            Check(target.HP == hp - 1, "actual building attack shares the same minimum");
            skill.Area = SkillAreaShape.SingleDouble; skill.SecondMultiplier = .5f;
            hp = target.HP; skillSystem.ExecuteSkill(attacker, target, skill);
            Check(target.HP == hp - 2, "each valid hit of a double strike remains effective");
            skill.Area = SkillAreaShape.Single; skill.SecondMultiplier = 0;
            StatusEffectSystem.ApplyBuff(target, BuffType.Barrier);
            StatusEffectSystem.ApplyDebuff(attacker, StatusEffectType.Weaken);
            Check(DamageCalculator.CalcNormal(attacker, target) == 1 && DamageCalculator.CalcSkill(attacker, target, skill) == 1, "defensive buffs and attacker penalties do not erase the minimum");
            target.ActiveEffects.Clear(); attacker.ActiveEffects.Clear();
            var simAttacker = new SimUnit { Id = 1, Team = Team.Enemy, Kind = Kind.Scout, Type = Type.Unit, HP = 20, MaxHP = 20, ATK = attacker.ATK, DEF = attacker.DEF, Position = Vector3Int.zero };
            var simTarget = new SimUnit { Id = 2, Team = Team.Player, Kind = Kind.Knight, Type = Type.Unit, HP = target.HP, MaxHP = target.MaxHP, ATK = target.ATK, DEF = target.DEF, Position = Vector3Int.right };
            Check(SimBoardState.CalcDamage(simAttacker, simTarget) == 1 && SimBoardState.CalcSkillDamage(simAttacker, simTarget, skill) == 1, "Minimax normal and skill estimates share the floor");
            var board = new SimBoardState { Units = new List<SimUnit> { simAttacker, simTarget }, EnemyAP = 30, PlayerAP = 30,
                MapTiles = new HashSet<Vector3Int> { Vector3Int.zero, Vector3Int.right }, EnemyCrystalPos = new Vector3Int(-5, 0, -5), PlayerCrystalPos = new Vector3Int(-6, 0, -6) };
            board.RebuildOccupied();
            try
            {
                hp = simTarget.HP;
                Check(board.ApplyAction(new SimAction { Type = SimActionType.Attack, UnitId = 1, TargetUnitId = 2, ActorTeam = Team.Enemy, APCost = 2 })
                    && simTarget.HP == hp - 1, "Minimax applied action uses actual chip damage");
                rules.minimumAttackDamage = 3;
                Check(DamageCalculator.CalcNormal(attacker, target) == 3 && AIEvalHelpers.EstimateDamage(attacker, target) == 3
                    && SimBoardState.CalcDamage(simAttacker, simTarget) == 3, "formal editable rule reaches live/AI/search paths");
                hp = target.HP; battle.ProcessDamage(hub); Check(target.HP == hp - 3, "edited minimum applies to actual battle execution");
                rules.minimumAttackDamage = 0; Check(DamageCalculator.MinimumAttackDamage == 1, "legacy zero/missing rule is safely one");
                rules.minimumAttackDamage = 1000; Check(DamageCalculator.MinimumAttackDamage == 100, "out-of-range rule is bounded");
                rules.minimumAttackDamage = 1;
                target.ShieldTurns = simTarget.ShieldTurns = 2; hp = target.HP;
                Check(DamageCalculator.CalcNormal(attacker, target) == 0 && DamageCalculator.CalcSkill(attacker, target, skill) == 0
                    && AIEvalHelpers.EstimateDamage(attacker, target) == 0 && SimBoardState.CalcDamage(simAttacker, simTarget) == 0
                    && SimBoardState.CalcSkillDamage(simAttacker, simTarget, skill) == 0, "shield remains complete cancellation in calculations");
                battle.ProcessDamage(hub); skillSystem.ExecuteSkill(attacker, target, skill);
                applyBuilding.Invoke(buildingAttack, new object[] { cannon, target });
                Check(target.HP == hp, "shield remains complete cancellation in actual normal/skill execution");
                int simHP = simTarget.HP;
                Check(board.ApplyAction(new SimAction { Type = SimActionType.Attack, UnitId = 1, TargetUnitId = 2, ActorTeam = Team.Enemy, APCost = 2 })
                    && simTarget.HP == simHP, "simulation preserves shield cancellation");
                target.ShieldTurns = simTarget.ShieldTurns = 0;
            }
            finally { board.ReturnOccupiedSet(); }
            Check(DamageCalculator.CalcFromValues(100, 0, 0) == 0 && DamageCalculator.CalcSkillFromValues(100, 0, 1, 0, 0) == 0,
                "explicit zero incoming/attack multiplier never becomes damage");
            StatusEffectSystem.ApplyDebuff(attacker, StatusEffectType.Seal);
            var sealedSkill = new SkillData { Multiplier = .1f, Area = SkillAreaShape.Single };
            Check(DamageCalculator.CalcSkill(attacker, target, sealedSkill) == 0, "fully canceled skill multiplier remains zero");
            Check(DamageCalculator.CalcSkill(attacker, target, new SkillData { FixedHeal = 10 }) == 0, "healing/support skill does not become an attack");
            attacker.ActiveEffects.Clear(); StatusEffectSystem.ApplyDebuff(attacker, StatusEffectType.Stun);
            hp = target.HP; battle.ProcessDamage(hub); Check(target.HP == hp, "stunned actor cannot gain an attack from the minimum rule");
            attacker.ActiveEffects.Clear();
            target.isWildBoss = true; target.wildBossTerritoryCenter = new Vector3Int(10000, 0, 10000); target.wildBossTerritoryRadius = 0;
            hp = target.HP; battle.ProcessDamage(hub); Check(target.HP == hp, "territory-based strong-enemy immunity remains effective");
            target.isWildBoss = false; target.specialAbility = SpecialAbility.SurvivalInstinct; target.HP = 1; target.SurvivalInstinctUsed = false;
            battle.ProcessDamage(hub); Check(target.HP == 1 && target.SurvivalInstinctUsed, "one-use lethal survival still prevents a lethal minimum hit");
            target.specialAbility = SpecialAbility.None; target.HP = 0;
            Check(DamageCalculator.CalcNormal(attacker, target) == 0 && DamageCalculator.CalcNormal(null, attacker) == 0,
                "dead/missing targets are not turned into valid hits");
            rules.applyRules = false; Check(DamageCalculator.MinimumAttackDamage == 1, "disabled rule asset retains one-point default");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(lowDefinition); UnityEngine.Object.DestroyImmediate(highDefinition);
            loaded.SetValue(null, previousRules); didLoad.SetValue(null, previousLoaded); AIConfig.Mode = previousMode;
            UnityEngine.Object.DestroyImmediate(rules);
        }
        Debug.Log("[MinimumDamage] ALL PASSED checks=" + checks + " actualLevels=1/100 shieldAndSurvivalPreserved=true");
    }
}
#endif
