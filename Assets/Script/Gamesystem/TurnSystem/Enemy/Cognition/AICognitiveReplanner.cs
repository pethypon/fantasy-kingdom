using System;
using System.Collections.Generic;
using System.Globalization;

/// <summary>Failures suppress the same means under the same observed facts, without discarding the final goal.</summary>
public sealed class AICognitiveReplanner
{
    readonly Dictionary<string, ulong> failures = new Dictionary<string, ulong>(StringComparer.Ordinal);
    readonly Queue<string> failureOrder = new Queue<string>();
    const int MaximumFailures = 64;
    public string Reason { get; private set; } = "initial_plan";

    public bool CanRetry(string signature, AIObservedWorldModel world)
        => !failures.TryGetValue(signature ?? string.Empty, out ulong failedAt) || failedAt != Fingerprint(world);
    public bool CanRetry(string signature, ulong observedFingerprint)
        => !failures.TryGetValue(signature ?? string.Empty, out ulong failedAt) || failedAt != observedFingerprint;
    public void Fail(AICognitivePlan plan, AIObservedWorldModel world, string reason)
    {
        Reason = reason ?? "execution_failed";
        if (plan == null) return;
        string key = plan.Signature ?? string.Empty;
        if (!failures.ContainsKey(key))
        {
            while (failures.Count >= MaximumFailures && failureOrder.Count > 0) failures.Remove(failureOrder.Dequeue());
            failureOrder.Enqueue(key);
        }
        failures[key] = Fingerprint(world); plan.Status = AICognitivePlanStatus.Failed;
        foreach (var step in plan.Steps) step.Status = AIPlanOperatorStatus.Invalidated;
    }
    public bool ShouldSwitch(AICognitivePlan previous, AICognitivePlan proposed, float margin)
    {
        if (proposed == null) return false;
        if (previous == null || previous.Status == AICognitivePlanStatus.Failed || previous.Status == AICognitivePlanStatus.Completed ||
            previous.Status == AICognitivePlanStatus.Suspended) { Reason = "old_plan_unavailable"; return true; }
        if (previous.Signature == proposed.Signature) { Reason = "revalidated"; return true; }
        if (proposed.ProvenVictory && !previous.ProvenVictory) { Reason = "formal_victory_candidate"; return true; }
        if (previous.Goal != proposed.Goal || previous.ObjectiveRef != proposed.ObjectiveRef)
        { Reason = "objective_changed_by_observation"; return true; }
        if (proposed.StrategicPriorityBand < previous.StrategicPriorityBand)
        { Reason = "stronger_existing_priority_band"; return true; }
        if (proposed.Utility > previous.Utility + Math.Max(.01f, Math.Abs(previous.Utility)) * Math.Max(0, margin))
        { Reason = "alternative_exceeds_switch_margin"; return true; }
        Reason = "retain_progressing_objective"; return false;
    }
    public void Reset() { failures.Clear(); failureOrder.Clear(); Reason = "initial_plan"; }

    public static ulong Fingerprint(AIObservedWorldModel world)
    {
        if (world == null) return 0;
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            void Add(string value) { foreach (char c in value ?? string.Empty) { hash ^= c; hash *= 1099511628211UL; } }
            void Number(int value) => Add(value.ToString(CultureInfo.InvariantCulture));
            void Position(UnityEngine.Vector3 value)
            { Add(value.x.ToString("R", CultureInfo.InvariantCulture)); Add(value.y.ToString("R", CultureInfo.InvariantCulture)); Add(value.z.ToString("R", CultureInfo.InvariantCulture)); }
            Add(world.BattleId); Number((int)world.ActorTeam); Number(world.OwnAP);
            foreach (var own in world.OwnUnits) { Add(own.LifeId); Position(own.Position); Number(own.HP); Number(own.Barrier); }
            foreach (var enemy in world.ObservedOpponents) { Add(enemy.LifeId); Position(enemy.Position); Number(enemy.HP); Number(enemy.Barrier); }
            foreach (var fact in world.Facts) { Add(fact.Id); Number((int)fact.Truth); Add(fact.Value.ToString("R", CultureInfo.InvariantCulture)); }
            return hash;
        }
    }
}
