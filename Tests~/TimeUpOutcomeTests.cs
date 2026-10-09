#if UNITY_EDITOR
using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

public static class TimeUpOutcomeTests
{
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated project required");
        PlayMode(null);
        UnityEditor.EditorApplication.Exit(0);
    }
    static int passed;
    static void Check(string label, bool value)
    {
        if (!value) throw new InvalidOperationException("[TimeUpOutcome] FAIL " + label);
        passed++; Debug.Log("[TimeUpOutcome] PASS " + label);
    }
    public static void PlayMode(GameSystems unused)
    {
        passed = 0;
        var root = new GameObject("TimeUp result fixture"); root.SetActive(false);
        var groups = new List<GameObject>();
        try
        {
            var turn = root.AddComponent<TurnGenerator>();
            var crystal = root.AddComponent<CrystalSystem>();
            var units = root.AddComponent<UnitSetting>();
            var timer = root.AddComponent<TimerSystem>();
            turn.Systems.CrystalSystem = crystal; turn.Systems.UnitSetting = units; turn.Systems.TimerSystem = timer;
            Status Make(string name, Kind kind, Team team, out Transform parent)
            {
                var group = new GameObject(name); groups.Add(group); parent = group.transform;
                var child = new GameObject(name + " actor"); child.transform.SetParent(parent);
                var actor = child.AddComponent<Status>(); actor.kind = kind; actor.team = team;
                actor.HP = actor.MaxHP = 100; return actor;
            }
            var pc = Make("Player crystal", Kind.Crystal, Team.Player, out var pcParent);
            var ec = Make("Enemy crystal", Kind.Crystal, Team.Enemy, out var ecParent);
            var pk = Make("Player king", Kind.King, Team.Player, out var pkParent);
            var ek = Make("Enemy king", Kind.King, Team.Enemy, out var ekParent);
            crystal.Playercrystal = pcParent; crystal.Enemycrystal = ecParent;
            units.PlayerUnit = pkParent; units.EnemyUnit = ekParent;
            timer.Init(turn, crystal);
            var determine = typeof(TimerSystem).GetMethod("DetermineTimeUpResult", BindingFlags.Instance | BindingFlags.NonPublic);
            void Case(int playerCrystal, int enemyCrystal, int playerKing, int enemyKing, GameResult expected, string criterion)
            {
                pc.HP = playerCrystal; ec.HP = enemyCrystal; pk.HP = playerKing; ek.HP = enemyKing;
                var result = (GameResult)determine.Invoke(timer, null);
                Check("real timer result " + criterion + " " + expected, result == expected);
                string expectedReason = timer.TimeUpReasonText;
                // Result UI remains independent of timer cache fields and repeated Init calls.
                timer.Init(turn, crystal);
                var state = new GameEndState(turn, result);
                Check("result explanation agrees after timer cache reset " + criterion + " " + expected
                    + " expected=[" + expectedReason + "] actual=[" + state.OutcomeReason + "]",
                    state.OutcomeReason == expectedReason && state.OutcomeReason.Contains(criterion));
                pc.HP = ec.HP = pk.HP = ek.HP = 0;
                Check("captured time-up reason is immutable " + expected, state.OutcomeReason == expectedReason);
            }
            Case(80, 40, 10, 90, GameResult.TimeUpWin, "メインクリスタル");
            Case(40, 80, 90, 10, GameResult.TimeUpLose, "メインクリスタル");
            Case(40, 40, 80, 40, GameResult.TimeUpWin, "王");
            Case(40, 40, 40, 80, GameResult.TimeUpLose, "王");
            Case(40, 40, 40, 40, GameResult.TimeUpDraw, "引き分け");
            pc.HP = 1; pc.MaxHP = 2; ec.HP = 2; ec.MaxHP = 4;
            Check("equivalent fractions are exact ties", MatchObjectiveRules.CompareHealthRatios(pc, ec) == 0);
            pc.HP = int.MaxValue - 1; pc.MaxHP = int.MaxValue;
            ec.HP = int.MaxValue - 2; ec.MaxHP = int.MaxValue;
            Check("one HP difference remains detectable at large HP without overflow", MatchObjectiveRules.CompareHealthRatios(pc, ec) > 0);
        }
        finally
        {
            foreach (var group in groups) UnityEngine.Object.DestroyImmediate(group);
            UnityEngine.Object.DestroyImmediate(root);
        }
        Debug.Log("[TimeUpOutcome] " + passed + " real timer/result checks passed");
    }
}
#endif
