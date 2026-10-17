using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>FIFO ammunition shares the actor's existing durable special state and normal attack path.</summary>
public static partial class SpecialSkillRuntime
{
    static readonly IReadOnlyList<CrossbowRoundType>[] initialCrossbowMagazines =
    {
        Array.Empty<CrossbowRoundType>(),
        Array.AsReadOnly(new[] { CrossbowRoundType.Normal }),
        Array.AsReadOnly(new[] { CrossbowRoundType.Normal, CrossbowRoundType.Normal }),
        Array.AsReadOnly(new[] { CrossbowRoundType.Normal, CrossbowRoundType.Normal, CrossbowRoundType.Normal })
    };
    static bool UsesCrossbowMagazine(Status actor)
    {
        var definition = Definition(actor);
        return actor != null && definition != null && definition.enabledForPlay
            && definition.ability == UnitSpecialSkill.CrossbowStakes;
    }

    static bool EnsureCrossbowMagazine(Status actor)
    {
        if (!UsesCrossbowMagazine(actor)) return false;
        var definition = Definition(actor);
        if (definition.CrossbowConfigurationError != null) return false;
        var state = State(actor);
        if (state.crossbowMagazineInitialized) return true;
        state.crossbowMagazine = new List<CrossbowRoundType>(SpecialSkillDefinition.CrossbowMagazineCapacity);
        for (int i = 0; i < definition.crossbowInitialAmmo; i++) state.crossbowMagazine.Add(CrossbowRoundType.Normal);
        // A stable gameplay key, rather than a GUID, Unity instance ID, or enumeration index.
        uint key = 2166136261u;
        string definitionId = actor.unitDefinitionId ?? actor.GrowthData?.definitionId ?? string.Empty;
        for (int i = 0; i < definitionId.Length; i++) key = CrossbowMix(key, definitionId[i]);
        key = CrossbowMix(key, (uint)actor.team);
        key = CrossbowMix(key, unchecked((uint)actor.GridPosition.x));
        key = CrossbowMix(key, unchecked((uint)actor.GridPosition.z));
        state.crossbowReplayKey = unchecked((int)key);
        state.crossbowMagazineInitialized = true;
        return true;
    }

    public static bool CrossbowCanFire(Status actor)
        => !UsesCrossbowMagazine(actor) || CrossbowAmmoCount(actor) > 0;

    public static int CrossbowAmmoCount(Status actor)
        => CrossbowMagazine(actor).Count;

    public static IReadOnlyList<CrossbowRoundType> CrossbowMagazine(Status actor)
    {
        if (!UsesCrossbowMagazine(actor)) return initialCrossbowMagazines[0];
        var definition = Definition(actor);
        if (definition.CrossbowConfigurationError != null) return initialCrossbowMagazines[0];
        // Inspection and candidate evaluation are pure. Only committed actions/turn hooks initialize state.
        var state = actor.Special;
        if (state != null && state.crossbowMagazineInitialized)
            return (IReadOnlyList<CrossbowRoundType>)state.crossbowMagazine ?? initialCrossbowMagazines[0];
        return initialCrossbowMagazines[definition.crossbowInitialAmmo];
    }

    public static CrossbowRoundType CrossbowPeekRound(Status actor)
    {
        var magazine = CrossbowMagazine(actor);
        return magazine.Count > 0 ? magazine[0] : CrossbowRoundType.None;
    }

    public static string CrossbowMagazineText(Status actor)
    {
        var rounds = CrossbowMagazine(actor);
        var text = new StringBuilder("弾倉 ").Append(rounds.Count).Append("/3： [ ");
        for (int i = rounds.Count - 1; i >= 0; i--)
        {
            if (i != rounds.Count - 1) text.Append(" | ");
            text.Append(rounds[i] == CrossbowRoundType.Stake ? "杭" : "通常");
            if (i == 0) text.Append(" →発射");
        }
        return text.Append(" ]（左が新／右が古）").ToString();
    }

    /// <summary>Call once after normal attack validation. A miss or block still expends a fired round.</summary>
    public static bool TryBeginNormalShot(Status attacker, out CrossbowRoundType round)
    {
        round = CrossbowRoundType.Normal;
        if (!UsesCrossbowMagazine(attacker)) return true;
        if (!EnsureCrossbowMagazine(attacker) || State(attacker).crossbowMagazine.Count == 0) return false;
        var state = State(attacker);
        round = state.crossbowMagazine[0];
        state.crossbowMagazine.RemoveAt(0);
        state.crossbowNormalAttacked = true;
        state.crossbowShotSequence = state.crossbowShotSequence == int.MaxValue ? 0 : state.crossbowShotSequence + 1;
        state.crossbowPendingRound = round;
        CombatRuleTelemetry.Record("crossbow_fire", attacker);
        return true;
    }

    /// <summary>Only the round consumed by the authoritative normal attack can create a stake.</summary>
    public static void CompleteNormalShot(Status attacker, Status target, CrossbowRoundType round, bool hit)
    {
        if (!UsesCrossbowMagazine(attacker)) return;
        var state = State(attacker);
        bool fired = state.crossbowPendingRound == round && round != CrossbowRoundType.None;
        state.crossbowPendingRound = CrossbowRoundType.None;
        if (fired && hit && round == CrossbowRoundType.Stake && target != null && target.IsAlive
            && OrdinaryUnit(target) && NeutralFactionSystem.AreHostile(attacker, target)) AddStake(attacker, target);
    }

    static bool LoadCrossbowStake(Status actor, GameSystems systems, int round, out string reason)
    {
        reason = null;
        if (!EnsureCrossbowMagazine(actor)) return Reject(out reason, Definition(actor)?.CrossbowConfigurationError ?? "弾倉を使用できません");
        var state = State(actor);
        var magazine = state.crossbowMagazine;
        if (magazine.Count < SpecialSkillDefinition.CrossbowMagazineCapacity) magazine.Add(CrossbowRoundType.Stake);
        else
        {
            uint seed = unchecked((uint)state.crossbowReplayKey);
            var map = systems?.MapCreate;
            seed = CrossbowMix(seed, unchecked((uint)Mathf.RoundToInt((map?.SeedX ?? 0) * 1000)));
            seed = CrossbowMix(seed, unchecked((uint)Mathf.RoundToInt((map?.SeedZ ?? 0) * 1000)));
            seed = CrossbowMix(seed, unchecked((uint)round));
            seed = CrossbowMix(seed, unchecked((uint)state.ownerTurn));
            seed = CrossbowMix(seed, unchecked((uint)state.crossbowLoadSequence));
            seed = CrossbowMix(seed, unchecked((uint)state.crossbowShotSequence));
            magazine[(int)(seed % (uint)magazine.Count)] = CrossbowRoundType.Stake;
        }
        CombatRuleTelemetry.Record("crossbow_ammo_add", actor, cause: "Stake");
        state.crossbowLoadSequence = state.crossbowLoadSequence == int.MaxValue ? 0 : state.crossbowLoadSequence + 1;
        return true;
    }

    static uint CrossbowMix(uint hash, uint value)
    {
        unchecked
        {
            // Mix every byte explicitly so behavior is independent of string hash randomization.
            for (int shift = 0; shift < 32; shift += 8) hash = (hash ^ ((value >> shift) & 255u)) * 16777619u;
            return hash;
        }
    }

    static void OnCrossbowOwnerTurnStart(Status actor, int previousOwnerTurn)
    {
        if (!EnsureCrossbowMagazine(actor)) return;
        var state = State(actor);
        if (previousOwnerTurn > 0 && !state.crossbowNormalAttacked && state.crossbowMagazine.Count < SpecialSkillDefinition.CrossbowMagazineCapacity)
        {
            state.crossbowMagazine.Add(CrossbowRoundType.Normal);
            CombatRuleTelemetry.Record("crossbow_ammo_add", actor, cause: "Normal");
        }
        state.crossbowNormalAttacked = false;
        state.crossbowPendingRound = CrossbowRoundType.None;
    }

    internal static void NormalizeCrossbowState(SpecialState state)
    {
        if (state == null) return;
        state.crossbowMagazine = state.crossbowMagazine ?? new List<CrossbowRoundType>();
        state.crossbowMagazine.RemoveAll(round => round != CrossbowRoundType.Normal && round != CrossbowRoundType.Stake);
        if (state.crossbowMagazine.Count > SpecialSkillDefinition.CrossbowMagazineCapacity)
            state.crossbowMagazine.RemoveRange(SpecialSkillDefinition.CrossbowMagazineCapacity, state.crossbowMagazine.Count - SpecialSkillDefinition.CrossbowMagazineCapacity);
        state.crossbowLoadSequence = Math.Max(0, state.crossbowLoadSequence);
        state.crossbowShotSequence = Math.Max(0, state.crossbowShotSequence);
        state.crossbowPendingRound = CrossbowRoundType.None;
        state.crossbowStayHits = state.crossbowStayHits ?? new List<CrossbowLineStayStamp>();
        state.crossbowStayHits.RemoveAll(stamp => stamp == null || string.IsNullOrEmpty(stamp.targetId) || stamp.targetId.Length > 64);
        if (state.crossbowStayHits.Count > 1024) state.crossbowStayHits.RemoveRange(0, state.crossbowStayHits.Count - 1024);
    }

    static bool MarkCrossbowStayHit(Status caster, Status target, int round)
    {
        var state = State(caster);
        var stamps = state.crossbowStayHits;
        if (stamps == null) state.crossbowStayHits = stamps = new List<CrossbowLineStayStamp>();
        string id = target.ReflectionLifeId;
        for (int i = stamps.Count - 1; i >= 0; i--)
        {
            var stamp = stamps[i];
            if (stamp == null || stamp.round != round) { stamps.RemoveAt(i); continue; }
            if (stamp.targetId == id && stamp.stakeAId == state.stakeAId && stamp.stakeBId == state.stakeBId
                && stamp.stakeAAppliedTurn == state.stakeAAppliedTurn && stamp.stakeBAppliedTurn == state.stakeBAppliedTurn) return false;
        }
        if (stamps.Count >= 1024) stamps.RemoveAt(0);
        stamps.Add(new CrossbowLineStayStamp { targetId = id, round = round,
            stakeAId = state.stakeAId, stakeBId = state.stakeBId,
            stakeAAppliedTurn = state.stakeAAppliedTurn, stakeBAppliedTurn = state.stakeBAppliedTurn });
        return true;
    }
}
