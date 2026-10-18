#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

/// <summary>Actual Play Mode regression for event-only cosmetic movement, attacks and hits.</summary>
public static class BoardActorAnimationTests
{
    static int checks;
    static void Check(bool result, string label)
    { checks++; if (!result) throw new Exception("[BoardActorAnimation] " + label); }
    static void Tick(double now)
        => typeof(BoardActorAnimation).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { now });
    static bool Pose(Transform root, Vector3 position, Quaternion rotation, Vector3 scale)
        => root.localPosition == position && Quaternion.Angle(root.localRotation, rotation) < .001f && root.localScale == scale;

    public static void PlayMode(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.VisionGenerator == null) throw new Exception("Actual Play Mode scene required");
        checks = 0;
        Tick(Time.unscaledTimeAsDouble + 1);
        int boundBefore = BoardActorAnimation.BoundCount, activeBefore = BoardActorAnimation.ActiveCount;
        int playerAP = systems.FactionState.GetAP(Team.Player), enemyAP = systems.FactionState.GetAP(Team.Enemy);
        var randomBefore = UnityEngine.Random.state;
        var root = new GameObject("Animation authority fixture");
        var hidden = new GameObject("Animation hidden fixture");
        var child = GameObject.CreatePrimitive(PrimitiveType.Cube);
        UnityEngine.Object.DestroyImmediate(child.GetComponent<Collider>());
        child.transform.SetParent(root.transform, false);
        child.transform.localPosition = new Vector3(.03f, .2f, .04f);
        child.transform.localRotation = Quaternion.Euler(0, 13, 0);
        child.transform.localScale = new Vector3(.6f, .8f, .6f);
        var bodyCollider = root.AddComponent<BoxCollider>(); bodyCollider.size = Vector3.one * .65f;
        var actor = root.AddComponent<Status>();
        actor.kind = Kind.Knight; actor.team = Team.Player; actor.type = Type.Unit; actor.HP = actor.MaxHP = 40;
        actor.Level = 3; actor.ATK = 13; actor.DEF = 9; actor.direction = Direction.N;
        BoardActorAnimation.Unbind(actor);
        Vector3 visibleCell = Vector3.zero; bool found = false;
        foreach (var cell in systems.VisionGenerator.PlayerVisionBox) { visibleCell = cell; found = true; break; }
        if (!found) throw new Exception("Fixture requires observed terrain");
        root.transform.position = visibleCell;
        var basePosition = child.transform.localPosition;
        var baseRotation = child.transform.localRotation;
        var baseScale = child.transform.localScale;
        try
        {
            BoardActorAnimation.Bind(actor, root.transform);
            Check(BoardActorAnimation.BoundCount == boundBefore, "Logical root binding is rejected");
            BoardActorAnimation.Bind(actor, child.transform);
            Check(BoardActorAnimation.BoundCount == boundBefore + 1, "One reusable record per actor");
            Check(BoardActorAnimation.ActiveCount == activeBefore, "Binding does not start idle updates");
            for (int i = 0; i < 50; i++) BoardActorAnimation.Bind(actor, child.transform);
            Check(BoardActorAnimation.BoundCount == boundBefore + 1 && BoardActorAnimation.ActiveCount == activeBefore,
                "Repeated binding never grows registration or wakes idle actors");
            var driver = UnityEngine.Object.FindFirstObjectByType<BoardActorAnimationDriver>();
            Check(driver != null && UnityEngine.Object.FindObjectsByType<BoardActorAnimationDriver>(FindObjectsSortMode.None).Length == 1,
                "One shared scene driver");
            Check(!driver.enabled, "Shared frame callback is disabled while idle");
            for (int i = 0; i < 60; i++) Tick(Time.unscaledTimeAsDouble + i * .25);
            Check(Pose(child.transform, basePosition, baseRotation, baseScale), "Idle stays exactly still at every sampled time");
            actor.isWildBoss = true; actor.wildBossArchetype = WildBossArchetype.GhostKing;
            for (int i = 0; i < 12; i++) Tick(Time.unscaledTimeAsDouble + i);
            Check(Pose(child.transform, basePosition, baseRotation, baseScale) && BoardActorAnimation.ActiveCount == activeBefore,
                "Ghost King never floats or rotates during idle");
            actor.isWildBoss = false;
            Check(root.GetComponents<Animator>().Length == 0 && child.GetComponents<Animator>().Length == 0, "No actor Animator added");

            Vector3 destination = visibleCell + Vector3.right;
            root.transform.position = destination;
            var authority = root.transform.position;
            var authorityRotation = root.transform.rotation;
            var colliderBounds = bodyCollider.bounds;
            float originalTimeScale = Time.timeScale;
            try
            {
                Time.timeScale = 0;
                double now = Time.unscaledTimeAsDouble;
                BoardActorAnimation.NotifyMove(actor, visibleCell); Tick(now + .06);
                Check(child.transform.localPosition != basePosition, "Movement animates while game time is paused");
                Check(driver.enabled && BoardActorAnimation.ActiveCount == activeBefore + 1, "Action alone wakes the callback");
                Check(root.transform.position == authority && root.transform.rotation == authorityRotation, "Logical pose stays untouched");
                Check(bodyCollider.bounds == colliderBounds && bodyCollider.enabled, "Click collider stays at destination");
                Tick(now + 1);
                Check(Pose(child.transform, basePosition, baseRotation, baseScale) && BoardActorAnimation.ActiveCount == activeBefore,
                    "Movement restores the exact pose and removes its active record");
                Check(!driver.enabled, "Completed movement disables the callback");
                BoardActorAnimation.NotifyAttack(actor, visibleCell); Tick(Time.unscaledTimeAsDouble + .08);
                Check(!Pose(child.transform, basePosition, baseRotation, baseScale), "Normal attack has body motion");
                Tick(Time.unscaledTimeAsDouble + 1);
                Check(Pose(child.transform, basePosition, baseRotation, baseScale) && !driver.enabled, "Attack ends without residual motion");
                BoardActorAnimation.NotifyHit(actor); Tick(Time.unscaledTimeAsDouble + .07);
                Check(child.transform.localScale != baseScale, "Damage reaction changes cosmetic scale");
                Tick(Time.unscaledTimeAsDouble + 1);
                Check(Pose(child.transform, basePosition, baseRotation, baseScale) && !driver.enabled, "Hit ends without residual motion");
                BoardActorAnimation.NotifySkill(actor, authority); Tick(Time.unscaledTimeAsDouble + .09);
                Check(Pose(child.transform, basePosition, baseRotation, baseScale) && !driver.enabled,
                    "Skill notification never adds an extra body pulse");
            }
            finally { Time.timeScale = originalTimeScale; }
            Check(actor.HP == 40 && actor.MaxHP == 40 && actor.ATK == 13 && actor.DEF == 9 && actor.Level == 3, "Combat values stay untouched");
            Check(systems.FactionState.GetAP(Team.Player) == playerAP && systems.FactionState.GetAP(Team.Enemy) == enemyAP, "No AP consumed");
            BoardActorAnimation.NotifyHit(actor); Tick(Time.unscaledTimeAsDouble + .07);
            BoardActorAnimation.SetVisible(actor, false);
            Check(Pose(child.transform, basePosition, baseRotation, baseScale) && !driver.enabled, "Fog immediately cancels motion and restores pose");
            BoardActorAnimation.NotifyMove(actor, visibleCell); BoardActorAnimation.NotifyAttack(actor, authority); BoardActorAnimation.NotifyHit(actor);
            Check(BoardActorAnimation.ActiveCount == activeBefore, "Hidden actor never registers an action");
            BoardActorAnimation.SetVisible(actor, true);
            Check(!driver.enabled && BoardActorAnimation.ActiveCount == activeBefore, "Visibility alone never starts idle updates");
            actor.direction = Direction.S; BoardActorAnimation.NotifyFacing(actor);
            Check(Pose(child.transform, basePosition, baseRotation * Quaternion.Euler(0, 180, 0), baseScale)
                && root.transform.rotation == authorityRotation, "Idle facing changes immediately without rotating authority");
            Check(!driver.enabled, "Facing never starts an animation callback");
            actor.direction = Direction.N; BoardActorAnimation.NotifyFacing(actor);
            Check(Pose(child.transform, basePosition, baseRotation, baseScale), "North restores authored orientation");

            using (var loadScope = BoardActorAnimation.SuppressEffects())
            {
                Check(BoardActorAnimation.EffectsSuppressed, "Load suppression is active");
                BoardActorAnimation.Bind(actor, child.transform);
                BoardActorAnimation.SetVisible(actor, false); BoardActorAnimation.SetVisible(actor, true);
                Check(BoardActorAnimation.BoundCount == boundBefore + 1, "Load suppression permits binding and visibility");
                using (var nested = BoardActorAnimation.SuppressEffects())
                {
                    BoardActorAnimation.NotifyMove(actor, visibleCell); BoardActorAnimation.NotifyAttack(actor, visibleCell);
                    BoardActorAnimation.NotifySkill(actor, authority); BoardActorAnimation.NotifyHit(actor); BoardActorAnimation.NotifyDeath(actor);
                    actor.HP = 0;
                    Check(actor.HP == 0 && BoardActorAnimation.ActiveCount == activeBefore, "Saved HP changes data without replaying motion");
                    actor.HP = 40; nested.Dispose(); nested.Dispose();
                }
                Check(BoardActorAnimation.EffectsSuppressed, "Nested repeated Dispose preserves outer suppression");
                Tick(Time.unscaledTimeAsDouble + .08);
                Check(Pose(child.transform, basePosition, baseRotation, baseScale) && !driver.enabled, "Restoration remains exactly stationary");
            }
            Check(!BoardActorAnimation.EffectsSuppressed, "Load scope releases suppression");
            try { using (BoardActorAnimation.SuppressEffects()) throw new InvalidOperationException("Expected fixture failure"); }
            catch (InvalidOperationException) { }
            Check(!BoardActorAnimation.EffectsSuppressed, "Load exception releases suppression");
            BoardActorAnimation.NotifyHit(actor); Tick(Time.unscaledTimeAsDouble + .07);
            Check(child.transform.localScale != baseScale, "Hit reactions resume after loading");
            BoardActorAnimation.NotifyDeath(actor);
            Check(Pose(child.transform, basePosition, baseRotation, baseScale) && !driver.enabled, "Death only cancels a pending action");
            Check(BoardActorAnimation.DeathPoolCount == 0 && BoardActorAnimation.ActiveDeathCount == 0, "No detached death objects allocated");
            Check(!child.GetComponent<Renderer>().forceRenderingOff && actor.HP == 40 && bodyCollider.enabled,
                "Death notification leaves rendering, HP and collider authority untouched");
            BoardActorAnimation.Unbind(actor);
            Check(BoardActorAnimation.BoundCount == boundBefore, "Unbind removes actor record");

            var hiddenActor = hidden.AddComponent<Status>();
            hiddenActor.kind = Kind.Knight; hiddenActor.team = Team.Enemy; hiddenActor.type = Type.Unit; hiddenActor.HP = 40;
            BoardActorAnimation.Unbind(hiddenActor);
            hidden.transform.position = new Vector3(-1000, 0, -1000); child.transform.SetParent(hidden.transform, false);
            BoardActorAnimation.Bind(hiddenActor, child.transform);
            BoardActorAnimation.NotifyAttack(hiddenActor, visibleCell); BoardActorAnimation.NotifySkill(hiddenActor, visibleCell);
            BoardActorAnimation.NotifyHit(hiddenActor); BoardActorAnimation.NotifyMove(hiddenActor, visibleCell); BoardActorAnimation.NotifyDeath(hiddenActor);
            Check(BoardActorAnimation.ActiveCount == activeBefore && !driver.enabled, "Unknown enemy never reveals action motion");
            BoardActorAnimation.Unbind(hiddenActor);
            var container = new GameObject("Nested visual container"); container.transform.SetParent(root.transform, false);
            container.transform.localPosition = new Vector3(.07f, .03f, -.02f);
            child.transform.SetParent(container.transform, false); root.transform.position = visibleCell;
            child.GetComponent<Renderer>().enabled = true;
            var nestedBase = container.transform.localPosition;
            BoardActorAnimation.Bind(actor, container.transform);
            Check(BoardActorAnimation.BoundCount == boundBefore + 1, "Nested mesh binds one motion container");
            for (int i = 0; i < 12; i++)
            { BoardActorAnimation.SetVisible(actor, true); BoardActorAnimation.NotifyHit(actor); BoardActorAnimation.NotifyDeath(actor); }
            Check(BoardActorAnimation.ActiveCount == activeBefore && !driver.enabled && container.transform.localPosition == nestedBase,
                "Repeated cancellations leave no active record or offset");
            Check(root.GetComponentsInChildren<Collider>(true).Length == 1, "No click colliders added");
            Check(UnityEngine.Random.state.Equals(randomBefore), "Gameplay Random stays untouched");
            BoardActorAnimation.Unbind(actor);
            Check(BoardActorAnimation.BoundCount == boundBefore, "All fixture bindings are released");
        }
        finally
        {
            BoardActorAnimation.Unbind(actor);
            var hiddenActor = hidden.GetComponent<Status>(); if (hiddenActor != null) BoardActorAnimation.Unbind(hiddenActor);
            UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(hidden);
            UnityEngine.Random.state = randomBefore;
        }
        Debug.Log("[BoardActorAnimation] ALL PASSED " + checks);
    }
}
#endif
