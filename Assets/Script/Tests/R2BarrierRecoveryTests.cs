#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>R2 BR01-BR12 contracts. Run explicitly in the isolated native Editor; not a playtest claim.</summary>
public static class R2BarrierRecoveryTests
{
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static void Check(string id, string label, bool value)
    {
        if (!value) throw new InvalidOperationException("[R2Barrier] FAIL " + id + " " + label);
        Debug.Log("[R2Barrier] PASS " + id + " " + label);
    }
    static Status Actor(Team team = Team.Player)
    {
        var go = new GameObject("R2 barrier isolated fixture"); owned.Add(go);
        go.transform.position = new Vector3(2000 + owned.Count * 3, 1, 2000);
        var actor = go.AddComponent<Status>(); actor.kind = Kind.Knight; actor.type = Type.Unit; actor.team = team;
        actor.MaxHP = 100; actor.HP = 100; actor.ATK = 20; actor.ResetBarrier(); actor.Barrier = 0;
        return actor;
    }
    static void End(Status actor, int round) => BarrierSystem.ProcessActorEnd(actor, round);
    public static void Run()
    {
        var random = UnityEngine.Random.state;
        var settings = ScriptableObject.CreateInstance<BarrierCombatSettings>(); owned.Add(settings);
        settings.enabledForPlay = true; settings.PopulateR1Values(); settings.recoveryAtOwnerTurnStart = false;
        using var rules = BarrierCombatSettings.UseForValidation(settings);
        using var effects = BoardActorAnimation.SuppressEffects();
        try
        {
            var injured70 = Actor(); injured70.HP = 70;
            var injured90 = Actor(); injured90.HP = 90;
            for (int i = 1; i <= 5; i++) { End(injured70, i); End(injured90, i); }
            Check("BR01", "70% and 90% HP never regenerate after three or more boundaries",
                injured70.Barrier == 0 && injured90.Barrier == 0 && injured70.BarrierRecovery.SafeTurns == 0);

            var full = Actor();
            Check("BR02", "full-HP arrival starts a zero-length waiting interval",
                full.Barrier == 0 && full.BarrierRecovery.FullHPObserved && full.BarrierRecovery.SafeTurns == 0);
            End(full, 1); End(full, 2);
            Check("BR03", "first and second own boundaries do not regenerate", full.Barrier == 0 && full.BarrierRecovery.SafeTurns == 2);
            End(full, 3);
            Check("BR04", "third own boundary restores twenty percent", full.Barrier == 14 && full.BarrierRecovery.SafeTurns == 3);
            for (int i = 4; i <= 20; i++) End(full, i);
            Check("BR05", "later own boundaries continue and clamp at maximum", full.Barrier == 70 && full.MaxBarrier == 70);

            var shieldHit = Actor(); shieldHit.Barrier = 70; End(shieldHit, 1); End(shieldHit, 2);
            settings.fullyNegatedHitsResetRecovery = true; // Old setting cannot revive the superseded R1 rule.
            shieldHit.ApplyDamage(1);
            bool preserved = shieldHit.HP == 100 && shieldHit.Barrier == 69 && shieldHit.BarrierRecovery.SafeTurns == 2;
            End(shieldHit, 3);
            Check("BR06", "barrier-only damage preserves full-HP history", preserved && shieldHit.Barrier == 70);

            var hpHit = Actor(); End(hpHit, 1); End(hpHit, 2); hpHit.HP = 99;
            bool reset = !hpHit.BarrierRecovery.FullHPObserved && hpHit.BarrierRecovery.SafeTurns == 0;
            hpHit.ApplyHeal(1); End(hpHit, 3); End(hpHit, 4);
            bool waitedAgain = hpHit.Barrier == 0 && hpHit.BarrierRecovery.SafeTurns == 2;
            End(hpHit, 5);
            Check("BR07", "one HP loss resets immediately; real healing begins a new three-boundary wait", reset && waitedAgain && hpHit.Barrier == 14);

            var dead = Actor(); dead.HP = 0; for (int i = 1; i <= 5; i++) End(dead, i);
            Check("BR08", "dead actors cannot regenerate", !dead.IsAlive && dead.Barrier == 0);

            bool fixedMaximum = true;
            foreach (var entry in settings.initialBarriers)
                foreach (int level in new[] { -10, 1, 20, 100, int.MaxValue })
                    fixedMaximum &= BarrierSystem.Maximum(entry.initialBarrier, level) == entry.initialBarrier;
            Check("BR09", "initial barrier table is neither level-scaled nor multiplied by 1.75", fixedMaximum
                && BarrierSystem.Maximum(int.MaxValue, int.MaxValue) == int.MaxValue && BarrierSystem.Maximum(-1, 100) == 0);

            var saved = Actor(); End(saved, 1); End(saved, 2);
            var snapshot = JsonUtility.FromJson<BarrierRecoveryState>(JsonUtility.ToJson(saved.BarrierRecovery));
            saved.RestoreBarrier(true, 0, snapshot); End(saved, 2);
            bool duplicateIgnored = saved.Barrier == 0 && saved.BarrierRecovery.SafeTurns == 2;
            End(saved, 3);
            var legacy = JsonUtility.FromJson<BarrierRecoveryState>("{\"SafeTurns\":3,\"HasRecovered\":true,\"LastProcessedRound\":30}");
            saved.RestoreBarrier(true, 0, legacy); End(saved, 30); End(saved, 31);
            Check("BR10", "roundtrip neither advances nor replays a boundary; legacy safe-hit history grants no full-HP credit",
                duplicateIgnored && saved.Barrier == 0 && saved.BarrierRecovery.SafeTurns == 0 && !legacy.FullHPObserved);

            var live = Actor(); End(live, 100); End(live, 101);
            var capture = typeof(SimBoardState).GetMethod("CaptureUnit", BindingFlags.Static | BindingFlags.NonPublic);
            var process = typeof(SimBoardState).GetMethod("ProcessBarrierRecovery", BindingFlags.Static | BindingFlags.NonPublic);
            if (capture == null || process == null) throw new MissingMethodException("R2 live/search barrier bridge");
            var sim = (SimUnit)capture.Invoke(null, new object[] { live, 1, Team.Enemy, 101, false });
            int turns = live.BarrierRecovery.SafeTurns; bool damage = live.BarrierRecovery.DamageSinceOwnTurn,
                recovered = live.BarrierRecovery.HasRecovered, seen = live.BarrierRecovery.FullHPObserved;
            int preview = BarrierSystem.AdvanceRecovery(ref turns, ref damage, ref recovered, ref seen, live.HP, live.MaxHP, live.MaxBarrier);
            End(live, 102); process.Invoke(null, new object[] { sim, 102 });
            var clone = sim.Clone(); clone.HP = 99; clone.ApplyHeal(1);
            Check("BR11", "live, captured simulation and pure forecast agree; cloned clocks are independent",
                preview == 14 && live.Barrier == sim.Barrier && sim.Barrier == 14 && sim.BarrierSafeTurns == 3
                && sim.BarrierFullHPObserved && clone.BarrierSafeTurns == 0 && sim.BarrierSafeTurns == 3);
            SimBoardPool.ReturnUnit(clone);

            var allied = Actor(Team.Player); allied.HP = 90; allied.ApplyHeal(10);
            var enemy = Actor(Team.Enemy); enemy.HP = 90;
            enemy.NaturalRecovery = new NaturalRecoveryState { ConsecutiveSafeTurns = 4, HasTurnBaseline = true };
            NaturalRecoverySystem.ProcessActorEnd(enemy, 200);
            bool bothFull = allied.HP == 100 && enemy.HP == 100 && allied.BarrierRecovery.SafeTurns == 0 && enemy.BarrierRecovery.SafeTurns == 0;
            for (int i = 201; i <= 203; i++) { End(allied, i); End(enemy, i); }
            Check("BR12", "player direct-heal and enemy natural-heal share the same new full-HP clock",
                bothFull && allied.Barrier == 14 && enemy.Barrier == 14);
            Debug.Log("[R2Barrier] COMPLETE BR01-BR12");
        }
        finally
        {
            UnityEngine.Random.state = random;
            foreach (var value in owned) if (value != null) UnityEngine.Object.DestroyImmediate(value);
            owned.Clear();
        }
    }
}
#endif
