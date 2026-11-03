#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

public static class NormalAttackTurnTests
{
    static int checks;
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string text, bool ok) { if (!ok) throw new InvalidOperationException("[NormalAttackTurn] FAIL " + text); checks++; Debug.Log("[NormalAttackTurn] PASS " + text); }
    public static void InstallTwoAttackDefault()
    {
        var data=GameDevelopmentStudioWindow.EnsureGameplayTuning();
        var so=new SerializedObject(data);so.FindProperty("normalAttacksPerTurn").intValue=2;
        so.ApplyModifiedPropertiesWithoutUndo();AssetDatabase.SaveAssetIfDirty(data);GameDevelopmentStudioWindow.RefreshSettingsCaches();
    }
    public static void Run(GameSystems live, TurnGenerator turn)
    {
        checks = 0;
        foreach (var side in new[] { Team.Player, Team.Enemy })
        using (var f = new AIR2RecruitmentTests.Fixture(live, turn, side, 1))
        {
            var hub = (TurnGenerator)typeof(SummonSystem).GetField("turnGenerator", Hidden).GetValue(f.Summon);
            var systems = hub.Systems;
            var actor = f.OwnActors().First();
            actor.kind = Kind.Knight; actor.SummonAttackLocked = false; actor.ResetTurnFlags(); actor.ATK = 10;
            var target = f.EconomyFixture.Opponent(actor.transform.position + Vector3.forward, 1000);
            target.HP = target.MaxHP = 1000; target.Barrier = 0;
            hub.Context.SelectUnit = actor; live.BattleSystem.SetTarget(target);
            Check(side + " fresh unit can attack", SpecialSkillRuntime.CanNormalAttackTarget(actor, target));
            int before = target.HP; live.BattleSystem.ProcessDamage(hub);
            Check(side + " first native attack deals damage and leaves another attack", target.HP < before && actor.NormalAttacksThisTurn == 1
                && SpecialSkillRuntime.CanNormalAttackTarget(actor,target) && systems.APSystem.CanAct(side,APSystem.ActionType.Attack,actor));
            before=target.HP; live.BattleSystem.ProcessDamage(hub);
            Check(side + " second native attack deals damage and exhausts the quota",target.HP<before && actor.NormalAttacksThisTurn==2);
            before = target.HP; live.BattleSystem.ProcessDamage(hub);
            Check(side + " third direct battle call causes no damage", target.HP == before && actor.NormalAttacksThisTurn == 2);
            Check(side + " AP eligibility rejects the spent normal attack", !systems.APSystem.CanAct(side, APSystem.ActionType.Attack, actor));
            var board = f.Board();
            Check(side + " live AI offers no normal attack after use", board.GetAttackTargets(actor).Count == 0);
            var save = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));
            actor.NormalAttacksThisTurn = 0; SaveGameApplier.ApplyStatusFields(actor, save);
            Check(side + " save and restore cannot grant an extra attack", actor.NormalAttacksThisTurn == 2 && NormalAttackTurnRules.Exhausted(actor));
            Check(side + " offensive skill is not blocked by normal attack use", !SummonTurnRules.SkillBlocked(actor, SkillData.Table.Values.First(s => s.Multiplier > 0)));
            before=target.HP; SpecialSkillRuntime.ApplyAbilityDamage(actor,target,3,HpChangeKind.NormalDamage,cause:HpChangeCause.Skill);
            Check(side + " offensive skill damage retains its separate slot",target.HP<before && actor.NormalAttacksThisTurn==2);
            Check(side + " future opponent threat still includes this unit", SpecialSkillRuntime.CanNormalAttackTarget(actor, target, nextOwnerTurn: true));
            actor.ResetTurnFlags();
            Check(side + " own turn boundary restores normal attack", actor.NormalAttacksThisTurn == 0 && SpecialSkillRuntime.CanNormalAttackTarget(actor, target));
        }
        live.BattleSystem.SetTarget(null);
        Simulation();
        Debug.Log("[NormalAttackTurn] RESULT checks=" + checks);
    }
    static void Simulation()
    {
        var actor = new SimUnit { Id = 1, Kind = Kind.Knight, Type = Type.Unit, Team = Team.Enemy, HP = 100, MaxHP = 100, ATK = 10, Position = new Vector3Int(2,0,2) };
        var target = new SimUnit { Id = 2, Kind = Kind.Knight, Type = Type.Unit, Team = Team.Player, HP = 1000, MaxHP = 1000, Position = new Vector3Int(2,0,3) };
        var board = new SimBoardState { Units = new List<SimUnit> { actor, target }, EnemyAP = 60, PlayerAP = 60,
            MapTiles = new HashSet<Vector3Int>(), EnemyCrystalPos = Vector3Int.zero, PlayerCrystalPos = new Vector3Int(7,0,7),
            EnemyBuildingCounts = new Dictionary<FacilityKind,int>(), PlayerBuildingCounts = new Dictionary<FacilityKind,int>() };
        for (int x=0;x<8;x++) for(int z=0;z<8;z++) board.MapTiles.Add(new Vector3Int(x,0,z)); board.RebuildOccupied();
        var attack = new SimAction { Type=SimActionType.Attack, UnitId=1, TargetUnitId=2, ActorTeam=Team.Enemy, APCost=2 };
        Check("simulation accepts the first normal attack", board.ApplyAction(attack) && actor.NormalAttacksThisTurn == 1);
        var second=SimActionGenerator.GenerateAllActions(board,Team.Enemy).FirstOrDefault(a=>a.Type==SimActionType.Attack && a.UnitId==1 && a.TargetUnitId==2);
        Check("AI generates the second attack with its fatigued AP quote",second!=null && second.APCost==board.CalcAttackCost(actor) && second.APCost>attack.APCost);
        Check("simulation accepts the second normal attack",board.ApplyAction(second) && actor.NormalAttacksThisTurn==2);
        int hp=target.HP, ap=board.EnemyAP;
        Check("simulation rejects a stale third attack without payment", !board.ApplyAction(attack) && target.HP==hp && board.EnemyAP==ap);
        Check("simulation candidate generation honors the quota", !SimActionGenerator.GenerateAllActions(board,Team.Enemy).Any(a=>a.Type==SimActionType.Attack && a.UnitId==1));
        var copy=actor.Clone(); Check("simulation copies preserve the used attacks", copy.NormalAttacksThisTurn==2);
        board.SimulateTurnTransition(Team.Player); Check("opponent turn does not reset the unit", actor.NormalAttacksThisTurn==2);
        board.SimulateTurnTransition(Team.Enemy); Check("next own simulated turn resets the unit", actor.NormalAttacksThisTurn==0 && board.ApplyAction(attack));
    }
}
#endif
