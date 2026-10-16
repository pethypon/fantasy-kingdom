using System.Collections.Generic;
using UnityEngine;

public static partial class SpecialSkillRuntime
{
    public static SpecialState CopyState(SpecialState source)
    {
        if (source == null) return new SpecialState();
        var copy = JsonUtility.FromJson<SpecialState>(JsonUtility.ToJson(source)) ?? new SpecialState();
        copy.affectedUnitIds = copy.affectedUnitIds ?? new List<string>();
        if (copy.affectedUnitIds.Count > 1024) copy.affectedUnitIds.RemoveRange(1024, copy.affectedUnitIds.Count - 1024);
        copy.cooldown = Mathf.Clamp(copy.cooldown, 0, 100000);
        copy.ownerTurn = Mathf.Clamp(copy.ownerTurn, 0, 1000000);
        copy.stationaryTurns = Mathf.Clamp(copy.stationaryTurns, 0, 100000);
        copy.storedSacrificeDamage = Mathf.Max(0, copy.storedSacrificeDamage);
        copy.visionZeroTurns = Mathf.Clamp(copy.visionZeroTurns, 0, 2);
        copy.scoutVisionPenaltyTurns = Mathf.Clamp(copy.scoutVisionPenaltyTurns, 0, 2);
        copy.damageNullTurns = Mathf.Clamp(copy.damageNullTurns, 0, 2);
        copy.holyKnightTurns = Mathf.Clamp(copy.holyKnightTurns, 0, 3);
        copy.samuraiMaxTurns = Mathf.Clamp(copy.samuraiMaxTurns, 0, 3);
        copy.samuraiGauge = Mathf.Max(0, copy.samuraiGauge);
        copy.ninjaCharge = Mathf.Clamp(copy.ninjaCharge, 0, 5);
        copy.berserkerStage = Mathf.Clamp(copy.berserkerStage, 0, 5);
        return copy;
    }
    public static void RestoreLinks()
    {
        var actors = new List<Status>(); CombatRegistry.Collect(actors);
        foreach (var actor in actors)
        {
            var state = State(actor);
            if (FindActor(state.ritualTargetId) == null) { state.ritualTargetId = null; state.storedSacrificeDamage = 0; }
            if (FindActor(state.sniperTargetId) == null) state.sniperTargetId = null;
            var partner = FindActor(state.guardianPartnerId);
            if (partner == null || State(partner).guardianPartnerId != actor.ReflectionLifeId) state.guardianPartnerId = null;
            if (FindActor(state.stakeAId) == null) state.stakeAId = null;
            if (FindActor(state.stakeBId) == null) state.stakeBId = null;
            // A bomb's dead caster is valid. Its definition snapshot belongs to the holder.
            if (!string.IsNullOrEmpty(state.bombCasterId) && (state.bombRadius < 0 || state.bombMask == 0 || state.bombDeathPolicy == BombDeathPolicy.Unconfigured))
            { state.bombCasterId = null; state.bombDueRound = 0; }
            state.affectedUnitIds.RemoveAll(id => FindActor(id) == null);
        }
    }
}
