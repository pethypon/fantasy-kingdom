using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cosmetic motion for unrigged board models. Only the explicitly bound visual child moves;
/// Status, occupancy, colliders, AP and action ordering are never delayed or modified.
/// One scene driver ticks visible actors only while moving, attacking or reacting to damage.
/// Idle poses are exact and do not run a frame callback. No per-actor Animator, Update or material instances.
/// </summary>
public static class BoardActorAnimation
{
    sealed class View
    {
        public Status Actor;
        public Transform Root;
        public Renderer Renderer;
        public Vector3 Position, Scale, MoveOffset;
        public Quaternion Rotation;
        public Direction FacingAtBind;
        public Vector3 ActionDirection;
        public double MoveStart = -100, ActionStart = -100, HitStart = -100;
        public bool Visible;
        public int ActiveIndex = -1;
    }

    const double MoveSeconds = .18, AttackSeconds = .24, HitSeconds = .17;
    static readonly Dictionary<Status, View> bound = new Dictionary<Status, View>();
    static readonly List<View> active = new List<View>(64);
    static BoardActorAnimationDriver driver;
    static VisionGenerator vision;
    static int effectSuppressionDepth;
    static uint effectSuppressionGeneration;

    public static int BoundCount => bound.Count;
    public static int ActiveCount => active.Count;
    // Kept for existing diagnostics. Defeated actors no longer create detached visual objects.
    public static int DeathPoolCount => 0;
    public static bool EffectsSuppressed => effectSuppressionDepth > 0;
    public static int ActiveDeathCount => 0;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        bound.Clear(); active.Clear(); driver = null; vision = null;
        effectSuppressionDepth = 0; unchecked { effectSuppressionGeneration++; }
    }

    /// <summary>Restoring saved data must not replay combat. Binding and Fog visibility remain operational inside the scope.</summary>
    public static IDisposable SuppressEffects() => new SuppressionScope();

    sealed class SuppressionScope : IDisposable
    {
        readonly uint generation;
        bool disposed;
        public SuppressionScope()
        {
            generation = effectSuppressionGeneration;
            checked { effectSuppressionDepth++; }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            // A scope surviving a play-session reset cannot decrement a different session's scope.
            if (generation == effectSuppressionGeneration && effectSuppressionDepth > 0) effectSuppressionDepth--;
        }
    }

    /// <summary>Call after ActorVisual receives its mesh and team material. Never bind the logical actor root.</summary>
    public static void Bind(Status actor, Transform visualRoot)
    {
        if (!Application.isPlaying || actor == null || visualRoot == null || visualRoot == actor.transform
            || !actor.gameObject.activeInHierarchy || !visualRoot.gameObject.activeInHierarchy
            || !visualRoot.IsChildOf(actor.transform)) return;
        if (bound.TryGetValue(actor, out var previous))
        {
            if (previous.Root == visualRoot) { SetVisible(actor, CanObserve(actor)); return; }
            Unbind(actor);
        }
        // Unity can return a missing-component wrapper in the Editor. Use Unity's
        // null comparison so a mesh on a child is found on container-only roots.
        var filter = visualRoot.GetComponent<MeshFilter>();
        if (filter == null) filter = visualRoot.GetComponentInChildren<MeshFilter>(true);
        var renderer = filter != null ? filter.GetComponent<Renderer>() : null;
        if (renderer == null || filter == null || filter.sharedMesh == null) return;
        EnsureDriver();
        var view = new View { Actor = actor, Root = visualRoot, Renderer = renderer,
            Position = visualRoot.localPosition, Rotation = visualRoot.localRotation, Scale = visualRoot.localScale,
            FacingAtBind = actor.direction };
        bound.Add(actor, view);
        SetVisible(actor, CanObserve(actor));
    }

    public static void Unbind(Status actor)
    {
        // ReferenceEquals deliberately permits Unity fake-null objects during OnDestroy.
        if (ReferenceEquals(actor, null) || !bound.TryGetValue(actor, out var view)) return;
        RemoveActive(view); Restore(view); bound.Remove(actor);
    }

    /// <summary>Visibility permits events; it never starts idle updates. Hiding cancels motion immediately.</summary>
    public static void SetVisible(Status actor, bool visible)
    {
        if (ReferenceEquals(actor, null) || !bound.TryGetValue(actor, out var view)) return;
        visible &= actor != null && actor.IsAlive && view.Root != null && view.Root.gameObject.activeInHierarchy;
        view.Visible = visible;
        if (visible)
        {
            if (view.ActiveIndex < 0) Restore(view);
        }
        else
        {
            Cancel(view);
        }
    }

    public static void NotifyMove(Status actor, Vector3 from)
    {
        BoardActionFeedback.Move(actor, from);
        if (EffectsSuppressed) return;
        if (!TryVisible(actor, out var view)) return;
        var destination = actor.transform.position;
        // A long relocation, cave transition or partially unseen route is a short arrival dip,
        // not a visible trail through hidden cells or a fabricated path across obstacles.
        bool observedPath = (destination - from).sqrMagnitude <= 17 && Mathf.Abs(destination.y - from.y) <= 1.1f;
        for (int i = 0; observedPath && i <= 8; i++)
            observedPath = CanObserveCell(Vector3.Lerp(from, destination, i / 8f));
        view.MoveOffset = observedPath ? actor.transform.InverseTransformVector(from - destination) : Vector3.zero;
        view.MoveStart = Time.unscaledTimeAsDouble;
    }

    public static void NotifyAttack(Status actor, Vector3 target)
    { BoardActionFeedback.Attack(actor, target); BeginAttack(actor, target); }

    /// <summary>Compatibility entry point: skill effects do not add a separate body animation.</summary>
    public static void NotifySkill(Status actor, Vector3? target = null)
    { BoardActionFeedback.Skill(actor); }

    static void BeginAttack(Status actor, Vector3 target)
    {
        if (EffectsSuppressed) return;
        if (!TryVisible(actor, out var view)) return;
        Vector3 direction = CanObserveCell(target) ? target - actor.transform.position : Vector3.zero;
        direction.y = 0;
        view.ActionDirection = actor.transform.InverseTransformDirection(direction.normalized);
        view.ActionStart = Time.unscaledTimeAsDouble;
    }

    public static void NotifyHit(Status actor)
    { BoardActionFeedback.Hit(actor); if (!EffectsSuppressed && TryVisible(actor, out var view)) view.HitStart = Time.unscaledTimeAsDouble; }

    /// <summary>Death ends pending motion without creating a ghost or delaying authoritative removal.</summary>
    public static void NotifyDeath(Status actor)
    {
        if (!EffectsSuppressed && !ReferenceEquals(actor, null) && bound.TryGetValue(actor, out var view)) Cancel(view);
    }

    /// <summary>Call immediately after an authoritative N/S change; idle actors have no frame polling.</summary>
    public static void NotifyFacing(Status actor)
    {
        if (ReferenceEquals(actor, null) || !bound.TryGetValue(actor, out var view) || view.Root == null) return;
        view.Root.localRotation = FacingRotation(view);
    }

    static bool TryVisible(Status actor, out View view)
    {
        view = null;
        if (actor == null || !actor.IsAlive || !bound.TryGetValue(actor, out view) || !view.Visible
            || view.Root == null || view.Renderer == null || !view.Renderer.enabled || view.Renderer.forceRenderingOff) return false;
        if (view.ActiveIndex < 0) { view.ActiveIndex = active.Count; active.Add(view); driver.enabled = true; }
        return true;
    }

    static bool CanObserve(Status actor)
    {
        if (actor == null || !actor.IsAlive) return false;
        if (vision == null) vision = actor.RuntimeSystems?.VisionGenerator;
        if (actor.team == Team.Player) return true;
        return vision != null && vision.IsActorVisibleTo(Team.Player, actor);
    }

    static bool CanObserveCell(Vector3 position)
    { return vision != null && vision.IsPresentationCellVisible(position); }

    static void EnsureDriver()
    {
        if (driver != null) return;
        driver = new GameObject("Board actor animation (shared)").AddComponent<BoardActorAnimationDriver>();
        driver.enabled = false;
    }

    static void RemoveActive(View view)
    {
        int index = view.ActiveIndex;
        if (index < 0) return;
        int last = active.Count - 1;
        if (index != last) { active[index] = active[last]; active[index].ActiveIndex = index; }
        active.RemoveAt(last); view.ActiveIndex = -1;
        if (active.Count == 0 && driver != null) driver.enabled = false;
    }

    static void Restore(View view)
    {
        if (view.Root == null) return;
        view.Root.localPosition = view.Position; view.Root.localRotation = FacingRotation(view); view.Root.localScale = view.Scale;
    }

    static void Cancel(View view)
    {
        RemoveActive(view); Restore(view);
        view.MoveStart = view.ActionStart = view.HitStart = -100;
    }

    static Quaternion FacingRotation(View view)
    {
        bool turned = view.Actor != null && view.Actor.direction != view.FacingAtBind;
        return view.Rotation * (turned ? Quaternion.Euler(0, 180, 0) : Quaternion.identity);
    }

    internal static void Tick(double now)
    {
        for (int i = active.Count - 1; i >= 0; i--)
        {
            var view = active[i]; var actor = view.Actor;
            if (actor == null || !actor.IsAlive || view.Root == null || view.Renderer == null)
            { Unbind(actor); continue; }
            // Visibility updates are authoritative events. Do not scan every visible terrain cell for every actor each frame.
            if (!view.Renderer.enabled || view.Renderer.forceRenderingOff) { SetVisible(actor, false); continue; }
            bool transient = now - view.MoveStart < MoveSeconds || now - view.ActionStart < AttackSeconds || now - view.HitStart < HitSeconds;
            if (!transient) { Cancel(view); continue; }
            Vector3 offset = Vector3.zero, euler = Vector3.zero;
            float scale = 1;
            double moveAge = now - view.MoveStart;
            if (moveAge >= 0 && moveAge < MoveSeconds)
            {
                float t = (float)(moveAge / MoveSeconds), ease = 1 - t;
                offset += view.MoveOffset * ease * ease + Vector3.up * Mathf.Sin(t * Mathf.PI) * .055f;
                euler.x += Mathf.Sin(t * Mathf.PI) * 4;
            }
            double actionAge = now - view.ActionStart;
            if (actionAge >= 0 && actionAge < AttackSeconds)
            {
                float pulse = Mathf.Sin((float)(actionAge / AttackSeconds) * Mathf.PI);
                offset += view.ActionDirection * pulse * .085f;
                Vector3 lean = Quaternion.Inverse(FacingRotation(view)) * view.ActionDirection;
                euler += new Vector3(lean.z * pulse * 9, 0, -lean.x * pulse * 9);
            }
            double hitAge = now - view.HitStart;
            if (hitAge >= 0 && hitAge < HitSeconds)
            {
                float t = (float)(hitAge / HitSeconds), pulse = Mathf.Sin(t * Mathf.PI);
                euler.z += Mathf.Sin(t * Mathf.PI * 4) * (1 - t) * 5;
                scale -= pulse * .045f;
            }
            view.Root.localPosition = view.Position + offset;
            view.Root.localRotation = FacingRotation(view) * Quaternion.Euler(euler);
            view.Root.localScale = view.Scale * scale;
        }
    }

    internal static void DriverDestroyed(BoardActorAnimationDriver value)
    {
        if (driver != value) return;
        foreach (var item in bound) Restore(item.Value);
        bound.Clear(); active.Clear(); driver = null; vision = null;
    }
}
