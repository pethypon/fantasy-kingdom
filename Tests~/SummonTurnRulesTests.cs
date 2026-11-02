#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Production-rule regression tests. PlayMode uses isolated, explicitly funded fixture factions.</summary>
public static class SummonTurnRulesTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string label, bool ok)
    { if (!ok) throw new InvalidOperationException("[SummonRules] FAIL " + label); Debug.Log("[SummonRules] PASS " + label); }

    public static void Core()
    {
        var rules = new SummonTurnRules();
        Check("two shared slots per faction", rules.TryReserve(Team.Player) && rules.TryReserve(Team.Player)
            && !rules.TryReserve(Team.Player) && rules.Remaining(Team.Player) == 0 && rules.TryReserve(Team.Enemy));
        rules.BeginOwnerTurn(Team.Enemy);
        Check("opponent turn never resets player quota", rules.Used(Team.Player) == 2 && rules.Used(Team.Enemy) == 0);
        var saved = JsonUtility.FromJson<SummonTurnRules.Snapshot>(JsonUtility.ToJson(rules.Capture()));
        rules.BeginOwnerTurn(Team.Player); rules.Restore(saved); saved.Player = 0;
        Check("quota survives JSON resume without sharing state", rules.Used(Team.Player) == 2);
        rules.BeginOwnerTurn(Team.Player); rules.TryReserve(Team.Player); rules.ReleaseFailedSummon(Team.Player);
        Check("failed spawn releases reservation; unrelated factions excluded", rules.Used(Team.Player) == 0 && !rules.TryReserve(Team.Monster));
        rules.Restore(new SummonTurnRules.Snapshot { Player = 99, Enemy = -1 });
        Check("corrupt counters cannot exceed quota", rules.Used(Team.Player) == 2 && rules.Used(Team.Enemy) == 0
            && !SummonTurnRules.Valid(new SummonTurnRules.Snapshot { Player = -1 }));
        var legacy = new SaveSystem.GameSaveData { Version = 12, SchemaVersion = 12 };
        legacy.Units.Add(new SaveSystem.UnitSaveData { SummonAttackLocked = true });
        var migrated = (SaveSystem.GameSaveData)typeof(SaveSystem).GetMethod("RunMigrations", BindingFlags.NonPublic | BindingFlags.Static)
            .Invoke(null, new object[] { legacy });
        Check("v12 migrates without inventing old summon history", migrated.Version == SaveSystem.CurrentSaveVersion
            && migrated.SummonTurn.Player == 0 && !migrated.Units[0].SummonAttackLocked);
        Check("offensive preparations included; healing and ally support retained",
            SummonTurnRules.IsAttackSkill(UnitSpecialSkill.KnightBreakthrough)
            && SummonTurnRules.IsAttackSkill(UnitSpecialSkill.CrossbowStakes)
            && SummonTurnRules.IsAttackSkill(UnitSpecialSkill.DragonKnightPierce)
            && !SummonTurnRules.IsAttackSkill(UnitSpecialSkill.WhiteMageHeal)
            && !SummonTurnRules.IsAttackSkill(UnitSpecialSkill.NinjaSwap));
        Simulation();
        Debug.Log("[SummonRules] CORE COMPLETE");
    }

    static void Simulation()
    {
        var board = new SimBoardState {
            Units = new List<SimUnit>(), EnemyBuildingCounts = new Dictionary<FacilityKind,int>(), PlayerBuildingCounts = new Dictionary<FacilityKind,int>(),
            MapTiles = new HashSet<Vector3Int>(), EnemyAP = 60, PlayerAP = 60, EnemyAPReset = 60, PlayerAPReset = 60,
            EnemyCrystalPos = new Vector3Int(0,0,0), PlayerCrystalPos = new Vector3Int(13,0,13),
        };
        for (int x = 0; x < 14; x++) for (int z = 0; z < 14; z++) board.MapTiles.Add(new Vector3Int(x,0,z));
        board.RebuildOccupied();
        Check("simulation shares two slots across different kinds", board.ApplyAction(new SimAction { Type = SimActionType.Summon, ActorTeam = Team.Enemy,
            SummonKind = Kind.Knight, TargetPos = new Vector3Int(2,0,2), APCost = 5 })
            && board.ApplyAction(new SimAction { Type = SimActionType.Summon, ActorTeam = Team.Enemy,
            SummonKind = Kind.Archer, TargetPos = new Vector3Int(3,0,2), APCost = 5 }));
        int ap = board.EnemyAP;
        Check("third simulated summon has no side effects", !board.ApplyAction(new SimAction { Type = SimActionType.Summon, ActorTeam = Team.Enemy,
            SummonKind = Kind.Scout, TargetPos = new Vector3Int(4,0,2), APCost = 5 }) && board.EnemyAP == ap && board.Units.Count == 2);
        var actor = board.Units[0];
        var target = new SimUnit { Id = 20, Team = Team.Player, Kind = Kind.Knight, Type = Type.Unit,
            HP = 500, MaxHP = 500, Position = new Vector3Int(2,0,3) };
        board.Units.Add(target); board.RebuildOccupied();
        var attack = new SimAction { Type = SimActionType.Attack, UnitId = actor.Id, TargetUnitId = target.Id, ActorTeam = Team.Enemy, APCost = 2 };
        Check("new simulated recruit cannot attack", !board.ApplyAction(attack) && target.HP == 500 && board.EnemyAP == ap);
        var offensive = SkillData.Table.Values.First(s => s.Multiplier > 0);
        actor.AssignedSkillId = offensive.Id;
        Check("simulation rejects offensive skills without AP payment", !board.ApplyAction(new SimAction { Type = SimActionType.SkillUse,
            UnitId = actor.Id, TargetUnitId = target.Id, ActorTeam = Team.Enemy, APCost = 5, SkillId = offensive.Id })
            && target.HP == 500 && board.EnemyAP == ap);
        actor.AssignedSkillId = -1;
        var actions = SimActionGenerator.GenerateAllActions(board, Team.Enemy);
        Check("search never generates recruit attacks or a third summon", !actions.Any(a => a.Type == SimActionType.Attack || a.Type == SimActionType.Summon));
        Check("new simulated recruit can move", board.ApplyAction(new SimAction { Type = SimActionType.Move, UnitId = actor.Id,
            ActorTeam = Team.Enemy, TargetPos = new Vector3Int(1,0,2), APCost = 3 }));
        var hash = typeof(AIMinimaxEngine).GetMethod("BoardHash", BindingFlags.Static | BindingFlags.NonPublic);
        long firstHash = (long)hash.Invoke(null, new object[] { board });
        var copy = board.Clone();
        Check("search clone retains both recruitment rules", copy.EnemySummons == 2 && copy.GetUnit(actor.Id).SummonAttackLocked);
        copy.EnemySummons = 1;
        Check("transposition key includes remaining quota", firstHash != (long)hash.Invoke(null, new object[] { copy }));
        copy.EnemySummons = 2; copy.GetUnit(actor.Id).SummonAttackLocked = false;
        Check("transposition key includes summon attack lock", firstHash != (long)hash.Invoke(null, new object[] { copy }));
        SimBoardPool.ReturnBoard(copy);
        board.SimulateTurnTransition(Team.Player);
        Check("simulated opponent turn preserves recruit lock", actor.SummonAttackLocked && board.EnemySummons == 2);
        board.SimulateTurnTransition(Team.Enemy);
        Check("next simulated own turn releases both restrictions", !actor.SummonAttackLocked && board.EnemySummons == 0 && board.ApplyAction(attack));
        SimBoardPool.ReturnBoard(board);
    }

    public static void PlayMode(GameSystems live, TurnGenerator turn)
    {
        foreach (var team in new[] { Team.Player, Team.Enemy })
        using (var f = new AIR2RecruitmentTests.Fixture(live, turn, team, 1))
        {
            f.Resources.Citizen = 30; f.State.SetAP(team, 60);
            var summon = f.Summon;
            var hub = (TurnGenerator)typeof(SummonSystem).GetField("turnGenerator", Hidden).GetValue(summon);
            var systems = hub.Systems;
            systems.SkillSystem = hub.gameObject.AddComponent<SkillSystem>(); systems.SkillSystem.Init(f.State);
            var executor = new AIActionExecutor(hub, systems.MoveGenerator, live.AttackGenerator, live.BattleSystem,
                systems.APSystem, systems.SkillSystem, null, systems.BuildSystem, summon, new AILearning(false), team);
            var positions = summon.AIGetSummonablePositions(team);
            Check(team + " fixture has separated valid territory cells", positions.Count >= 3);
            var foreign = live.MapCreate.SetPos.FirstOrDefault(p => !positions.Any(q => GridHelper.MatchXZ(q,GridHelper.ToGridXZ(p))));
            int count = f.OwnActors().Count, ap = f.State.GetAP(team), wood = f.Resources.Wood;
            Check(team + " outside territory still prohibited", !summon.AISummonUnit(GridHelper.ToGrid(foreign), Kind.Knight, team)
                && summon.SummonsThisTurn(team) == 0 && f.State.GetAP(team) == ap);
            if (team == Team.Player)
            {
                // Actual player placement path, with the input cursor positioned deterministically.
                summon.StartSummonMode(f.Healer);
                var cursor = (BuildCursorController)typeof(SummonSystem).GetField("_cursor", Hidden).GetValue(summon);
                cursor.UpdatePosition(positions[0], true); typeof(SummonSystem).GetField("canPlace", Hidden).SetValue(summon, true);
                Check("player cursor placement shares the same quota", summon.TryPlace());
            }
            else Check("enemy AI first summon", summon.AISummonUnit(positions[0], f.Healer, team));
            var recruit = f.OwnActors().First(a => a.GrowthData == f.Healer);
            Check(team + " successful summon pays and locks only attack", summon.SummonsThisTurn(team) == 1
                && f.OwnActors().Count == count + 1 && f.State.GetAP(team) == ap - f.Healer.costAP
                && f.Resources.Wood == wood - f.Healer.costWood && recruit.SummonAttackLocked && !SpecialSkillRuntime.MovementBlocked(recruit));
            Check(team + " another kind at a distant origin uses the second shared slot",
                summon.AISummonUnit(positions[positions.Count - 1], Kind.Scout, team) && summon.SummonsThisTurn(team) == 2);
            ap = f.State.GetAP(team); wood = f.Resources.Wood; count = f.OwnActors().Count;
            Check(team + " third summon rejected before any payment or spawn", !summon.AISummonUnit(positions[1], Kind.Knight, team)
                && f.State.GetAP(team) == ap && f.Resources.Wood == wood && f.OwnActors().Count == count
                && !summon.CanSummon(team, f.Healer) && summon.AIGetSummonablePositions(team).Count == 0);
            var board = f.Board();
            Check(team + " live AI candidate list has no third summon", board.AffordableUnits.Count == 0 && board.SummonablePositions.Count == 0);
            var sim = SimBoardState.CreateFromGame(board, systems.MoveGenerator, systems.UnitSetting, systems.CrystalSystem, systems.APSystem);
            Check(team + " player-AI normalization preserves quota and readiness", sim.EnemySummons == 2
                && sim.Units.Any(u => u.Team == Team.Enemy && u.SummonAttackLocked)); SimBoardPool.ReturnBoard(sim);
            var foe = f.EconomyFixture.Opponent(recruit.transform.position + Vector3.forward, 20);
            int hp = foe.HP, barrier = foe.Barrier;
            Check(team + " actual normal attack eligibility is blocked", !SpecialSkillRuntime.CanNormalAttackTarget(recruit, foe)
                && !systems.APSystem.CanAct(team, APSystem.ActionType.Attack, recruit));
            hub.Context.SelectUnit = recruit; live.BattleSystem.SetTarget(foe); live.BattleSystem.ProcessDamage(hub);
            Check(team + " direct battle entry cannot bypass lock", foe.HP == hp && foe.Barrier == barrier);
            Check(team + " stale AI attack also rejected", !executor.Execute(new AIAction { ActionType = AIActionType.Attack,
                Unit = recruit, TargetUnit = foe, TargetPos = foe.transform.position, APCost = 2 }, board) && f.State.GetAP(team) == ap);
            AbilityCommands(systems, recruit, foe);
            var saved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(recruit)));
            recruit.SummonAttackLocked = false; SaveGameApplier.ApplyStatusFields(recruit, saved);
            Check(team + " unit lock survives full status restore", recruit.SummonAttackLocked);
            systems.MoveGenerator.UnitPointCore();
            List<Vector3> FreeMoves() => board.GetValidMoves(recruit).Where(p => live.MapCreate.IsLegalMove(recruit, recruit.transform.position, p)
                && !systems.MoveGenerator.IsOccupied(systems.MoveGenerator.Cell(p))).ToList();
            var moves = FreeMoves();
            // Title-scene actors can block the small artificial territory. Arrange a free test site;
            // keep the real movement, occupancy, AP and readiness checks intact.
            if (moves.Count == 0) foreach (var cell in live.MapCreate.SetPos)
            {
                if (systems.MoveGenerator.IsOccupied(systems.MoveGenerator.Cell(cell))) continue;
                recruit.transform.position = cell; systems.MoveGenerator.UnitPointCore();
                moves = FreeMoves(); if (moves.Count > 0) break;
            }
            Check(team + " new recruit has legal movement", moves.Count > 0);
            f.State.SetAP(team, 60); board.Refresh(); board.Governor = null; // Test the formal move rule independently of strategic selection.
            bool moved = executor.Execute(new AIAction { ActionType = AIActionType.Move,
                Unit = recruit, TargetPos = moves[0], APCost = board.CalcMoveCost(recruit, moves[0]) }, board);
            Check(team + " actual AI executor can move recruit immediately (" + executor.LastFailureReason + ")", moved && recruit.SummonAttackLocked);
            var other = team == Team.Player ? Team.Enemy : Team.Player;
            TurnStartHelper.ProcessTurnStart(systems, other, 21);
            Check(team + " real opponent turn preserves both restrictions", summon.SummonsThisTurn(team) == 2 && recruit.SummonAttackLocked);
            TurnStartHelper.ProcessTurnStart(systems, team, 22);
            Check(team + " real next own turn unlocks attacks and both summon slots", summon.RemainingSummons(team) == 2
                && !recruit.SummonAttackLocked && SpecialSkillRuntime.CanNormalAttackTarget(recruit, foe)
                && systems.APSystem.CanAct(team, APSystem.ActionType.Attack, recruit));
            var nextSlots = summon.AIGetSummonablePositions(team);
            Check(team + " next own turn permits recruiting again", nextSlots.Count > 0 && summon.AISummonUnit(nextSlots[0], Kind.Knight, team));
        }
        live.BattleSystem.SetTarget(null);
        SaveResume(live, turn);
        Debug.Log("[SummonRules] PLAYMODE COMPLETE");
    }

    static void AbilityCommands(GameSystems systems, Status recruit, Status foe)
    {
        var data = recruit.GrowthData;
        var original = data.specialSkill;
        var definition = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        var originalSpecial = SpecialSkillRuntime.CopyState(recruit.Special);
        try
        {
            data.specialSkill = definition; definition.enabledForPlay = true; definition.apCost = 1; definition.cooldown = 0;
            recruit.Special = new SpecialState(); recruit.Special.dragonKnightGauge = 2;
            definition.ability = UnitSpecialSkill.DragonKnightPierce;
            int ap = systems.FactionState.GetAP(recruit.team), hp = foe.HP;
            Check(recruit.team + " fixed offensive skill rejected before AP or effects", !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(recruit, foe), systems, out var reason)
                && reason == SummonTurnRules.AttackReason && systems.FactionState.GetAP(recruit.team) == ap && foe.HP == hp
                && recruit.Special.dragonKnightGauge == 2);
            int oldBarrier = foe.Barrier;
            Check(recruit.team + " special damage entry cannot bypass readiness", SpecialSkillRuntime.ApplyAbilityDamage(recruit, foe, 50,
                HpChangeKind.BarrierBypassDamage) == 0 && foe.HP == hp && foe.Barrier == oldBarrier && SpecialSkillRuntime.LastHpChange.ActualHpLoss == 0);
            definition.ability = UnitSpecialSkill.SamuraiCounter; definition.samuraiGaugeMax = 2; definition.samuraiGaugeGain = 1;
            recruit.Special.samuraiGauge = 2; recruit.Special.samuraiMaxTurns = 3;
            SpecialSkillRuntime.ApplyHpChange(foe, recruit, 1, HpChangeKind.NormalDamage, cause: HpChangeCause.NormalAttack);
            Check(recruit.team + " summoned Samurai cannot auto-counter or strip barriers", foe.HP == hp && foe.Barrier == oldBarrier
                && recruit.Special.samuraiGauge == 2);
            StatusEffectSystem.ApplyBuff(recruit, BuffType.Reflect);
            Check(recruit.team + " summoned unit cannot reflect attacks", StatusEffectSystem.ProcessReflect(recruit, foe) == 0 && foe.HP == hp);
            recruit.ActiveEffects.RemoveAll(e => e.buffType == BuffType.Reflect);
            definition.ability = UnitSpecialSkill.WhiteMageHeal;
            Check(recruit.team + " healing skill remains available", SpecialSkillRuntime.CanUse(recruit, systems, out _));
            int originalHP = recruit.HP;
            recruit.HP = Mathf.Max(1,recruit.MaxHP / 2); int injuredHP = recruit.HP;
            if (!recruit.VisionCell.Contains(recruit.GridPosition)) recruit.VisionCell.Add(recruit.GridPosition);
            Check(recruit.team + " newly summoned healer can actually heal", SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(recruit, recruit), systems, out _)
                && recruit.HP > injuredHP && recruit.SummonAttackLocked);
            recruit.HP = originalHP;
            var support = new SkillData { Target = SkillTarget.AllySingle, FixedHeal = 10 };
            var attack = new SkillData { Target = SkillTarget.EnemySingle, Multiplier = 1 };
            Check(recruit.team + " legacy attack blocked, support retained", SummonTurnRules.SkillBlocked(recruit, attack) && !SummonTurnRules.SkillBlocked(recruit, support));
            data.specialSkill = null;
            int hpBefore = foe.HP; systems.SkillSystem.ExecuteSkill(recruit, foe, attack);
            Check(recruit.team + " direct legacy attack cannot bypass lock", foe.HP == hpBefore);
        }
        finally { data.specialSkill = original; recruit.Special = originalSpecial; UnityEngine.Object.DestroyImmediate(definition); }
    }

    static void SaveResume(GameSystems systems, TurnGenerator turn)
    {
        // Actual scene save/load boundary, with no disk writes to a user save slot.
        systems.SummonSystem.RestoreTurnRules(new SummonTurnRules.Snapshot { Player = 2, Enemy = 1 });
        var actor = systems.UnitSetting.PlayerUnit.GetComponentsInChildren<Status>().First(a => a.type == Type.Unit && a.IsAlive);
        actor.SummonAttackLocked = true;
        var data = JsonUtility.FromJson<SaveSystem.GameSaveData>(JsonUtility.ToJson(SaveSystem.CollectGameState(turn, systems.FactionState,
            systems.TimerSystem, systems.VisionGenerator, systems.AICommander)));
        Check("current real-scene snapshot validates", SaveSystem.ValidateGameData(data, out var reason));
        systems.SummonSystem.RestoreTurnRules(null); actor.SummonAttackLocked = false;
        SaveGameApplier.Apply(data, systems.FactionState, turn, systems.UnitSetting, systems.CrystalSystem, systems.BuildSystem,
            systems.MoveGenerator, systems.VisionGenerator, systems.MapCreate, systems.SummonSystem);
        turn.ChangeState(new PlayerStart(turn));
        Check("resumed PlayerStart preserves spent slots and attack lock", turn.CurrentState is PlayerMove && actor.SummonAttackLocked
            && systems.SummonSystem.SummonsThisTurn(Team.Player) == 2 && systems.SummonSystem.SummonsThisTurn(Team.Enemy) == 1);
        systems.UnitPanelUI.Show(actor); systems.UnitPanelUI.Refresh();
        var attackButton = (UnityEngine.UI.Button)typeof(UnitPanelUI).GetField("attackButton", Hidden).GetValue(systems.UnitPanelUI);
        Check("real status UI disables attack and explains when it unlocks", !attackButton.interactable
            && attackButton.GetComponentInChildren<TMPro.TextMeshProUGUI>().text.Contains("次ターンから攻撃"));
        data.SummonTurn.Player = 3;
        Check("invalid saved quota rejected", !SaveSystem.ValidateGameData(data, out _));
    }
}
#endif
