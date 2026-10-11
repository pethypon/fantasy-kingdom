#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

/// <summary>The frozen prior profiler is an independent oracle for full-history learning and match pruning.</summary>
public static class PlayerProfilerPerformanceTests
{
    public static void Run()
    {
        var current = new PlayerProfiler(); var previous = new LegacyPlayerProfiler();
        var attackerRoot = new GameObject("Profiler attacker"); var targetRoot = new GameObject("Profiler target");
        int comparisons = 0;
        try
        {
            var attacker = attackerRoot.AddComponent<Status>(); var target = targetRoot.AddComponent<Status>();
            attacker.kind = Kind.Knight; attacker.type = Type.Unit; target.kind = Kind.Scout; target.type = Type.Unit;
            target.MaxHP = 40;
            var formation = new List<Status> { attacker, target };
            for (int turn = 1; turn <= 20000; turn++)
            {
                attacker.transform.position = new Vector3(turn % 31, 0, turn % 29);
                target.transform.position = attacker.transform.position + new Vector3(turn % 3 + 1, 0, turn % 5 + 1);
                target.HP = turn % 4 == 0 ? 10 : 40;
                target.VisionCell.Clear(); if (turn % 3 == 0) target.VisionCell.Add(attacker.GridPosition);
                if (turn % 5 != 0)
                {
                    current.ObserveAttack(attacker, target, Vector3.zero, turn);
                    previous.ObserveAttack(attacker, target, Vector3.zero, turn);
                }
                var destination = attacker.transform.position + (turn % 2 == 0 ? Vector3.one : -Vector3.one);
                current.ObserveMove(attacker, attacker.transform.position, destination, Vector3.zero, turn);
                previous.ObserveMove(attacker, attacker.transform.position, destination, Vector3.zero, turn);
                current.ObserveFormation(formation, Vector3.zero, turn); previous.ObserveFormation(formation, Vector3.zero, turn);
                if (turn < 12 || turn % 499 == 0) { Compare(current, previous); comparisons++; }
            }
            var clock = Stopwatch.StartNew(); for (int i = 0; i < 200; i++) previous.UpdateProfile();
            double oldMilliseconds = clock.Elapsed.TotalMilliseconds;
            clock.Restart(); for (int i = 0; i < 200; i++) current.UpdateProfile();
            double newMilliseconds = clock.Elapsed.TotalMilliseconds;
            Compare(current, previous); comparisons++;
            current.OnMatchEnd(); previous.OnMatchEnd(); Compare(current, previous); comparisons++;
            // The retained second half of each history must rebuild the same statistics, not start anew.
            current.UpdateProfile(); previous.UpdateProfile(); Compare(current, previous); comparisons++;
            current.ObserveAttack(attacker, target, Vector3.zero, 20001); previous.ObserveAttack(attacker, target, Vector3.zero, 20001);
            current.ObserveFormation(formation, Vector3.zero, 20001); previous.ObserveFormation(formation, Vector3.zero, 20001);
            Compare(current, previous); comparisons++;
            UnityEngine.Debug.Log("[PlayerProfilerPerformance] " + comparisons + " exact feature comparisons passed; observations=20000 updates=200 beforeMs="
                + oldMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " afterMs="
                + newMilliseconds.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
        }
        finally { UnityEngine.Object.DestroyImmediate(attackerRoot); UnityEngine.Object.DestroyImmediate(targetRoot); }
    }
    static void Compare(PlayerProfiler current, LegacyPlayerProfiler previous)
    {
        current.UpdateProfile(); previous.UpdateProfile();
        var a = current.ToFeatureVector(); var b = previous.ToFeatureVector();
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i])
            throw new InvalidOperationException("[PlayerProfilerPerformance] Feature " + i + " differs: " + a[i] + " / " + b[i]);
        if (current.Profile.TotalObservations != previous.Profile.TotalObservations)
            throw new InvalidOperationException("[PlayerProfilerPerformance] Observation count differs");
        foreach (var pair in previous.Profile.UnitPreference)
            if (!current.Profile.UnitPreference.TryGetValue(pair.Key, out var value) || value != pair.Value)
                throw new InvalidOperationException("[PlayerProfilerPerformance] Unit preference differs");
    }
}
#endif
