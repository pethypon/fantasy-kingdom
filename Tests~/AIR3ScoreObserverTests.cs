#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Diagnostics must observe the existing one-pass calculations without changing them.</summary>
public static class AIR3ScoreObserverTests
{
    public static void Run(GameSystems live)
    {
        int checks = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            using var fixture = new AITerritorySafetyTests.Fixture(live, team);
            var board = fixture.Board(60);
            var state = UnityEngine.Random.state;
            AITurnBudget.Begin(3000);
            var before = AIActionEvaluator.EvaluateAll(fixture.Economy.Personality, board, null);
            var expected = new Dictionary<string, float>();
            foreach (var action in before) expected[action.FailureKey] = action.Score;
            int stages = 0;
            Action<AIBoardState, List<AIAction>, AIAction, string> observe = (seen, actions, chosen, stage) =>
            { if (ReferenceEquals(seen, board) && stage.StartsWith("score:", StringComparison.Ordinal)) stages++; };
            try
            {
                UnityEngine.Random.state = state;
                AIDecisionObserver.Observed += observe;
                AITurnBudget.Begin(3000);
                var after = AIActionEvaluator.EvaluateAll(fixture.Economy.Personality, board, null);
                if (stages != 8 || after.Count != before.Count)
                    throw new InvalidOperationException("[AIR3ScoreObserver] missing score stages or changed candidate count " + team);
                foreach (var action in after)
                    if (!expected.TryGetValue(action.FailureKey, out float score) || score != action.Score)
                        throw new InvalidOperationException("[AIR3ScoreObserver] observing changed a candidate score " + team);
                checks++; Debug.Log("[AIR3ScoreObserver] PASS " + team + " eight observed score stages preserve every candidate and score");
            }
            finally { AIDecisionObserver.Observed -= observe; UnityEngine.Random.state = state; }
        }
        Debug.Log("[AIR3ScoreObserver] ALL PASSED " + checks);
    }
}
#endif
