#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class AIR2CommandTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(string label, bool ok)
    { if (!ok) throw new InvalidOperationException("[AIR2Command] FAIL " + label); checks++; Debug.Log("[AIR2Command] PASS " + label); }
    public static void Run(GameSystems live, TurnGenerator turn)
    {
        checks = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            Retreat(live, team);
            Recon(live, team);
            Fallback(live, team);
            Formation(live, team);
            DestroyedObservation(live, team);
        }
        Debug.Log("[AIR2Command] ALL PASSED " + checks);
    }
    static void Retreat(GameSystems live, Team team)
    {
        using var f = new AITerritorySafetyTests.Fixture(live, team, true);
        var enemyProfile = ScriptableObject.CreateInstance<BoardActionProfile>();
        try
        {
            enemyProfile.actionType = ActorActionType.Combat;
            enemyProfile.movement.useCustom = enemyProfile.attack.useCustom = true;
            enemyProfile.attack.directionIndependent = true;
            enemyProfile.attack.SetCell(2, 3, true); enemyProfile.attack.SetCell(1, 3, true);
            f.Foe.GrowthData.actionProfile = enemyProfile;
            var origin = f.Actor.transform.position;
            f.Foe.transform.position = origin + Vector3.right; f.Foe.ATK = 300;
            f.Actor.Level = 20; f.Actor.HP = 8; f.Actor.Barrier = 0;
            f.Economy.SetOwnCrystalPosition(origin - Vector3.right); f.Economy.Crystal.transform.position = origin - Vector3.right;
            foreach (var cell in new[] { origin, origin + Vector3.right, origin - Vector3.right, origin + Vector3.forward })
            { f.Explored.Add(GridHelper.ToGridXZ(cell)); f.See(cell); }
            var board = f.Board(60);
            var options = new List<AIAction>(); AIActionGenerator.GenerateRetreatCandidates(f.Actor, board, options);
            float before = board.Governor.IncomingDamage(board, f.Actor, origin, null);
            Check(team + " observed focus fire is lethal to the wounded veteran", before >= f.Actor.HP);
            AIAction lateral = options.Find(a => a.TargetPos == origin + Vector3.forward);
            Check(team + " safe lateral retreat exists even though it does not approach home", lateral != null);
            Check(team + " lateral retreat lowers observed HP loss", board.Governor.IncomingDamage(board, f.Actor, lateral.TargetPos, null) < f.Actor.HP);
            var attack = new AIAction { ActionType = AIActionType.Attack, Unit = f.Actor, TargetUnit = f.Foe,
                APCost = board.CalcAttackCost(f.Actor), Score = 10000 };
            lateral.Score = 1;
            var choices = new List<AIAction> { lateral, attack }; board.Governor.Filter(choices, board);
            Check(team + " lifesaving veteran retreat is not suppressed by an ordinary attack tier", lateral.StrategicPriority < attack.StrategicPriority);
            var commander = live.AICommander;
            var boardField = typeof(AICommander).GetField("_board", Hidden);
            var previousBoard = boardField.GetValue(commander);
            try
            {
                boardField.SetValue(commander, board);
                var selected = typeof(AICommander).GetMethod("SelectBestAction", Hidden).Invoke(commander,
                    new object[] { choices, new HashSet<string>(), new HashSet<string>() });
                Check(team + " actual selector chooses rescue despite the ordinary attack's huge score", ReferenceEquals(selected, lateral));
            }
            finally { boardField.SetValue(commander, previousBoard); }
            int ap = f.Economy.State.GetAP(team), cost = board.CalcMoveCost(f.Actor, lateral.TargetPos);
            Check(team + " selected lateral retreat really executes with exact AP", ExecuteMovement(live, f, lateral, board)
                && f.Actor.transform.position == lateral.TargetPos && f.Economy.State.GetAP(team) == ap - cost);
            f.Foe.transform.position = new Vector3(-35000, 0, -36000);
            // Run the actual owner-turn natural recovery rule; do not assign recovered HP.
            for (int round = 61; round <= 76; round++) NaturalRecoverySystem.ProcessActor(f.Actor, round);
            Check(team + " the evacuated veteran recovers through real safe owner turns", f.Actor.HP == f.Actor.MaxHP);
            f.Actor.HasMovedThisTurn = false;
            board = f.Board(77); choices.Clear(); AIActionGenerator.GenerateRetreatCandidates(f.Actor, board, choices);
            Check(team + " healthy actor with no observed danger is released from retreat", choices.Count == 0);
            var rejoin = new AIAction { ActionType = AIActionType.Move, Unit = f.Actor,
                TargetPos = f.Actor.transform.position + Vector3.right };
            rejoin.APCost = board.CalcMoveCost(f.Actor, rejoin.TargetPos);
            Check(team + " recovered veteran can legally redeploy using the actual executor", board.GetValidMoves(f.Actor).Contains(rejoin.TargetPos)
                && ExecuteMovement(live, f, rejoin, board));
        }
        finally { UnityEngine.Object.DestroyImmediate(enemyProfile); }
    }
    static bool ExecuteMovement(GameSystems live, AITerritorySafetyTests.Fixture f, AIAction action, AIBoardState board)
    {
        var host = new GameObject("R2 isolated retreat execution"); host.SetActive(false);
        try
        {
            var hub = host.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch(); hub.Context.Turn = board.TurnCount;
            var moves = host.AddComponent<MoveGenerator>(); moves.mapcreate = live.MapCreate; moves.turnGenerator = hub; moves.BuildSystemRef = f.Economy.Builder;
            typeof(MoveGenerator).GetField("crystalsystem", Hidden).SetValue(moves, f.Economy.Crystals);
            typeof(MoveGenerator).GetField("unitsetting", Hidden).SetValue(moves, f.Economy.Units);
            typeof(MoveGenerator).GetField("PlayerUnit", Hidden).SetValue(moves, f.Economy.Units.PlayerUnit);
            typeof(MoveGenerator).GetField("EnemyUnit", Hidden).SetValue(moves, f.Economy.Units.EnemyUnit);
            hub.Systems.MapCreate = live.MapCreate; hub.Systems.MoveGenerator = moves; hub.Systems.APSystem = f.Economy.AP;
            hub.Systems.FactionState = f.Economy.State; hub.Systems.UnitSetting = f.Economy.Units;
            hub.Systems.CrystalSystem = f.Economy.Crystals; hub.Systems.BuildSystem = f.Economy.Builder;
            var executor = new AIActionExecutor(hub, moves, live.AttackGenerator, live.BattleSystem, f.Economy.AP,
                live.SkillSystem, null, f.Economy.Builder, null, new AILearning(false), f.Economy.Team);
            using (BoardActorAnimation.SuppressEffects()) return executor.Execute(action, board);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }
    static void Recon(GameSystems live, Team team)
    {
        using var f = new AITerritorySafetyTests.Fixture(live, team);
        f.Actor.kind = Kind.Scout; f.Actor.HP = f.Actor.MaxHP;
        var board = f.Board(60); board.ReconThreatLevel = 15;
        var manager = new AIPlanManager();
        manager.Restore(new AIPlan { Goal = AIPlanGoal.Reconnaissance, Step = AIPlanStep.Strike, StartedTurn = 60,
            StepStartedTurn = 60, LastObservedTurn = 60, InitialArmy = 1, ExpectedTurns = 8, Target = f.Actor.transform.position,
            RallyPoint = f.Cells[0], RequiredUnits = new[] { Kind.Scout }, Steps = new[] { AIPlanStep.Strike, AIPlanStep.Withdraw } });
        manager.Update(board);
        Check(team + " confirming reconnaissance does not order an unconditional homeward withdrawal", manager.Current.Step == AIPlanStep.Complete);
        manager.Restore(new AIPlan { Goal = AIPlanGoal.Reconnaissance, Step = AIPlanStep.Withdraw, StartedTurn = 60,
            InitialArmy = 1, ExpectedTurns = 8, Target = f.Cells[5], RallyPoint = f.Cells[0],
            RequiredUnits = new[] { Kind.Scout }, Steps = new[] { AIPlanStep.Strike, AIPlanStep.Withdraw } });
        Check(team + " loading old reconnaissance withdrawal cannot resurrect a conflicting home command",
            manager.Bonus(new AIAction { ActionType = AIActionType.Move, Unit = f.Actor, TargetPos = f.Cells[1] }, board) == 0);
        Check(team + " reconnaissance snapshot retains observed target without runtime instance identity", manager.Snapshot().TargetId == 0);
    }
    static void Fallback(GameSystems live, Team team)
    {
        using var f = new AITerritorySafetyTests.Fixture(live, team);
        var commander = live.AICommander;
        var field = typeof(AICommander).GetField("_board", Hidden);
        var strategy = typeof(AICommander).GetField("_currentStrategy", Hidden);
        var pending = typeof(AICommander).GetField("_fallbackStrategyPending", Hidden);
        var method = typeof(AICommander).GetMethod("ApplyIterationStrategy", Hidden);
        var oldBoard = field.GetValue(commander); var oldStrategy = strategy.GetValue(commander); var oldPending = pending.GetValue(commander);
        try
        {
            field.SetValue(commander, f.Board(60)); strategy.SetValue(commander, TurnStrategy.Assault); pending.SetValue(commander, true);
            var decision = new TurnStrategyPlanner.StrategyDecision { Strategy = TurnStrategy.Balanced, Reason = "normal" };
            method.Invoke(commander, new object[] { decision });
            Check(team + " fallback strategy survives one real iteration decision", (TurnStrategy)strategy.GetValue(commander) == TurnStrategy.Assault);
            method.Invoke(commander, new object[] { decision });
            Check(team + " ordinary strategy resumes after the fallback's evaluation pass", (TurnStrategy)strategy.GetValue(commander) == TurnStrategy.Balanced);
            f.Economy.Crystal.HP = 1; f.Foe.transform.position = f.Cells[1]; f.Foe.ATK = 5000; f.See(f.Cells[1]);
            var emergency = f.Board(61); field.SetValue(commander, emergency); pending.SetValue(commander, true);
            decision.Strategy = TurnStrategy.CrystalDefense;
            method.Invoke(commander, new object[] { decision });
            Check(team + " an actual observed emergency can interrupt a fallback", emergency.Governor.Mode == StrategicMode.EmergencyDefense
                && (TurnStrategy)strategy.GetValue(commander) == TurnStrategy.CrystalDefense);
        }
        finally { field.SetValue(commander, oldBoard); strategy.SetValue(commander, oldStrategy); pending.SetValue(commander, oldPending); }
    }
    static void Formation(GameSystems live, Team team)
    {
        using var f = new AITerritorySafetyTests.Fixture(live, team, true);
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        try
        {
            profile.actionType = ActorActionType.Combat; profile.attack.useCustom = profile.movement.useCustom = true;
            profile.attack.directionIndependent = true; profile.attack.SetCell(2, 3, true);
            f.Foe.GrowthData.actionProfile = profile;
            var origin = f.Actor.transform.position; var dest = origin + Vector3.right;
            f.Foe.transform.position = dest + Vector3.right; f.Foe.ATK = 20;
            f.Actor.HP = f.Actor.MaxHP = 100; f.Actor.Barrier = 0;
            foreach (var cell in new[] { origin, dest, f.Foe.transform.position, dest + Vector3.forward })
            { f.Explored.Add(GridHelper.ToGridXZ(cell)); f.See(cell); }
            var board = f.Board(60);
            var alone = new AIAction { ActionType = AIActionType.Move, Unit = f.Actor, TargetPos = dest, Score = 100 };
            AIActionModifiers.ApplyCounterDangerPenalty(new List<AIAction> { alone }, board, f.Economy.Personality);
            Check(team + " isolated exposed advance carries an observed-risk penalty", alone.Score < 100);
            var support = f.Economy.Unit(); support.transform.position = dest + Vector3.forward;
            board = f.Board(61);
            var covered = new AIAction { ActionType = AIActionType.Move, Unit = f.Actor, TargetPos = dest, Score = 100 };
            AIActionModifiers.ApplyCounterDangerPenalty(new List<AIAction> { covered }, board, f.Economy.Personality);
            Check(team + " supporting formation reduces the isolation penalty at equal attack exposure", covered.Score > alone.Score);
            f.Foe.transform.position = new Vector3(-35000, 0, -36000); support.gameObject.SetActive(false);
            f.Actor.kind = Kind.Assassin; board = f.Board(62);
            var solo = new AIAction { ActionType = AIActionType.Move, Unit = f.Actor, TargetPos = dest, Score = 100 };
            AIActionModifiers.ApplyCounterDangerPenalty(new List<AIAction> { solo }, board, f.Economy.Personality);
            Check(team + " safe solitary infiltration is not blocked merely for lacking an escort", solo.Score == 100);
        }
        finally { UnityEngine.Object.DestroyImmediate(profile); }
    }
    static void DestroyedObservation(GameSystems live, Team team)
    {
        using var f = new AITerritorySafetyTests.Fixture(live, team);
        var transient = f.Economy.Unit(); transient.HP = 1; transient.transform.position = f.Actor.transform.position;
        var board = f.Board(60);
        bool cached = false;
        foreach (var actor in board.SpecialObservedActors) if (actor == transient) cached = true;
        Check(team + " destruction regression starts with a genuinely cached observed component", cached);
        UnityEngine.Object.DestroyImmediate(transient.gameObject);
        var method = typeof(AIActionEvaluator).GetMethod("ObservedMagicianMultiplier", BindingFlags.Static | BindingFlags.NonPublic);
        float multiplier = (float)method.Invoke(null, new object[] { f.Foe, f.Actor, board });
        Check(team + " destroyed low-HP component cannot throw or grant a magician multiplier", multiplier == 1);
    }
}
#endif
