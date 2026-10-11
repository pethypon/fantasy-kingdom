using System;
using System.Collections.Generic;
using UnityEngine;

public enum AIPresentationKind
{ Move, DebugLabel, Log, Damage, Kill, Death, Skill, CrystalDestruction, SubCrystalDestruction, Artifact, OperationStart, OperationComplete, Win, Lose }
public struct AIPresentationEvent
{
    public AIPresentationKind Kind;
    public int ActorRuntimeId;
    public string Text;
    public Vector3 Position;
    public Color Color;
    public bool Popup;
    public bool Critical => Kind >= AIPresentationKind.Damage;
}

/// <summary>
/// Responsibility: bounded presentation-only backlog; never accepts AI actions or mutable game state.
/// Owner: ActionLogUI in the current scene. Lifetime: scene. Maximum: config QueueLimit (8..4,096).
/// Cleanup: dequeue on visible frames, clear on scene teardown. Failure: a faulty UI sink is isolated.
/// Overflow: only noncritical presentations can be dropped; a full critical backlog delivers its oldest
/// event synchronously before accepting another, rather than allocating unbounded overflow storage.
/// </summary>
public sealed class AIPresentationQueue
{
    readonly AIPerformanceConfig config;
    readonly Action<AIPresentationEvent> deliver;
    readonly List<AIPresentationEvent> pending;
    bool warningLogged;
    public int Count => pending.Count;
    public bool CatchUp { get; private set; }
    public long DeliveredCritical { get; private set; }
    public long DroppedLowPriority { get; private set; }
    public float AnimationMultiplier => CatchUp ? config.SafeAnimationMultiplier : 1f;
    public AIPresentationQueue(Action<AIPresentationEvent> deliver, AIPerformanceConfig config = null)
    {
        this.config = config != null ? config : AIPerformanceConfig.Active;
        this.deliver = deliver ?? throw new ArgumentNullException(nameof(deliver));
        pending = new List<AIPresentationEvent>(this.config.QueueLimit);
    }
    /// <summary>Queues display data only. Critical events are delivered even at the capacity boundary.</summary>
    public void Enqueue(AIPresentationEvent value)
    {
        using (AIPerformanceCounters.Measure(AIPerformanceStage.Presentation))
        {
            int limit = config.QueueLimit;
            while (pending.Count >= limit)
            {
                int disposable = -1;
                for (int i = 0; i < pending.Count; i++) if (!pending[i].Critical) { disposable = i; break; }
                if (disposable >= 0) { pending.RemoveAt(disposable); DroppedLowPriority++; }
                else if (!value.Critical) { DroppedLowPriority++; UpdateMode(); return; }
                else DeliverFirst();
            }
            if (CatchUp && !value.Critical && value.ActorRuntimeId != 0 && value.Kind == AIPresentationKind.Move)
                for (int i = pending.Count - 1; i >= 0; i--)
                    if (pending[i].Kind == value.Kind && pending[i].ActorRuntimeId == value.ActorRuntimeId)
                    { pending[i] = value; DroppedLowPriority++; UpdateMode(); return; }
            pending.Add(value); UpdateMode();
        }
    }
    /// <summary>Delivers a bounded number of UI events; presentation throughput never affects AI selection.</summary>
    public void Drain(int maximum)
    {
        using (AIPerformanceCounters.Measure(AIPerformanceStage.Presentation))
        {
            for (int i = 0; i < Math.Max(0, maximum) && pending.Count > 0; i++) DeliverFirst();
            UpdateMode();
        }
    }
    void DeliverFirst()
    {
        var value = pending[0]; pending.RemoveAt(0);
        try { deliver(value); if (value.Critical) DeliveredCritical++; }
        catch (Exception e)
        {
            if (!warningLogged) { warningLogged = true; Debug.LogWarning("[AI表示] 表示に失敗しました。ゲームの処理は継続します: " + e.Message); }
        }
    }
    void UpdateMode()
    {
        if (!CatchUp && pending.Count >= config.HighWater) CatchUp = true;
        else if (CatchUp && pending.Count <= config.LowWater) CatchUp = false;
        AIPerformanceCounters.SetPresentationQueueCount(pending.Count);
    }
    /// <summary>Releases scene display data. Has no effect on rewards, actions or learning.</summary>
    public void Clear() { pending.Clear(); CatchUp = false; AIPerformanceCounters.SetPresentationQueueCount(0); }
}
