#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>Save ownership and detachment contracts only. Source added; NOT RUN.</summary>
public static class AIR3RecoveryEconomyStateTests
{
    static void Check(string label, bool result)
    { if (!result) throw new InvalidOperationException("[R3RecoveryState] FAIL " + label); Debug.Log("[R3RecoveryState] PASS " + label); }

    static AIUnitPreservationState Fixture(Team owner) => new AIUnitPreservationState
    {
        Owner = owner,
        Records = new System.Collections.Generic.List<AIUnitPreservationRecord>
        {
            new AIUnitPreservationRecord { ActorLifeId = "original-life", Phase = AIUnitRecoveryPhase.Recovering,
                LastTurn = 8, Refuge = new Vector3(4, 1, 5) }
        }
    };

    public static void Run()
    {
        foreach (var owner in new[] { Team.Player, Team.Enemy })
        {
            var foreign = owner == Team.Player ? Team.Enemy : Team.Player;
            var source = Fixture(owner);
            var governor = new AIStrategicGovernor();
            governor.RestorePreservationState(source, owner);
            source.Records[0].Refuge = Vector3.zero;
            var saved = governor.CapturePreservationState(owner);
            Check(owner + "/restore-does-not-retain-input-reference", saved.Records.Count == 1
                && saved.Records[0].Refuge == new Vector3(4, 1, 5));
            saved.Records[0].ActorLifeId = "modified-copy";
            Check(owner + "/capture-is-detached", governor.CapturePreservationState(owner).Records[0].ActorLifeId == "original-life");
            Check(owner + "/capture-cannot-relabel-foreign-records", governor.CapturePreservationState(foreign).Records.Count == 0);
            var roundTrip = JsonUtility.FromJson<AIUnitPreservationState>(JsonUtility.ToJson(governor.CapturePreservationState(owner)));
            governor.RestorePreservationState(roundTrip, owner);
            Check(owner + "/roundtrip-retains-life-phase-and-turn", governor.CapturePreservationState(owner).Records.Count == 1
                && governor.CapturePreservationState(owner).Records[0].Phase == AIUnitRecoveryPhase.Recovering
                && governor.CapturePreservationState(owner).Records[0].LastTurn == 8);
            governor.RestorePreservationState(roundTrip, foreign);
            Check(owner + "/foreign-restore-clears-old-state", governor.CapturePreservationState(owner).Records.Count == 0
                && governor.CapturePreservationState(foreign).Records.Count == 0);
            governor.RestorePreservationState(Fixture(owner), owner);
            governor.ReleasePreservationState();
            Check(owner + "/release-clears-records-and-owner", governor.TrackedRecoveryUnits == 0
                && governor.CapturePreservationState(owner).Records.Count == 0);
        }

        var malformed = Fixture(Team.Enemy);
        malformed.Records.Add(malformed.Records[0].Copy());
        malformed.Records.Add(new AIUnitPreservationRecord { ActorLifeId = "ready", Phase = AIUnitRecoveryPhase.Ready });
        malformed.Records.Add(new AIUnitPreservationRecord { ActorLifeId = "nan", Phase = AIUnitRecoveryPhase.Withdrawing, Refuge = new Vector3(float.NaN, 0, 1) });
        malformed.Records.Add(new AIUnitPreservationRecord { ActorLifeId = "future-phase", Phase = (AIUnitRecoveryPhase)99 });
        Check("malformed-records-do-not-duplicate-or-revive-state", AIUnitPreservationState.CopyForOwner(malformed, Team.Enemy).Records.Count == 1);
        Check("unknown-owner-rejected", AIUnitPreservationState.CopyForOwner(new AIUnitPreservationState { Owner = (Team)999 }, (Team)999) == null);
        var unknownVersion = Fixture(Team.Enemy); unknownVersion.Version++;
        Check("unknown-version-not-interpreted", AIUnitPreservationState.CopyForOwner(unknownVersion, Team.Enemy) == null);
        var oversized = Fixture(Team.Enemy);
        for (int i = 0; i < AIUnitPreservationState.MaximumRecords; i++) oversized.Records.Add(oversized.Records[0].Copy());
        Check("oversized-save-is-bounded", AIUnitPreservationState.CopyForOwner(oversized, Team.Enemy) == null);
    }
}
#endif
