using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Small versioned tendency profile. Never serializes units, coordinates, HP or unseen information.</summary>
public sealed partial class AIPlayerModel
{
    [Serializable]
    public sealed partial class Profile
    {
        public int Version = 1;
        public string GameVersion, CreatedVersion, LastSavedVersion;
        public int Matches;
        public float Aggression, Economy, Scout, Ranged, Retreat, Skill, Flank, Turtle;
    }
    readonly string path;
    Profile history;
    int lastTurn = -1, observations;
    float aggression, economy, scout, ranged;
    float retreat, skill, flank, turtle;
    int responseSamples, retreatSamples;
    struct Seen { public Vector3 Position; public float HP, Distance; public int Turn, Cooldown; public bool AssassinExposed; }
    readonly Dictionary<int, Seen> seen = new Dictionary<int, Seen>();
    readonly List<int> expiredSamples=new List<int>();
    bool completed;
    public Profile Snapshot { get { var copy = new Profile { Matches = history.Matches, Aggression = history.Aggression,
        GameVersion = history.GameVersion, CreatedVersion = history.CreatedVersion, LastSavedVersion = history.LastSavedVersion,
        Economy = history.Economy, Scout = history.Scout, Ranged = history.Ranged,
        Retreat = history.Retreat, Skill = history.Skill, Flank = history.Flank, Turtle = history.Turtle };
        CopySpecialHistory(copy);return copy; } }

    public AIPlayerModel(string storagePath = null)
    {
        path = storagePath ?? Path.Combine(Application.persistentDataPath, "AIPlayerModel.v1.json");
        history = AtomicSaveStorage.TryRead(path, 4096, ValidateProfile, out string json, out _, out _)
            ? JsonUtility.FromJson<Profile>(json) : new Profile();
        NormalizeSpecialHistory();
    }
    static bool Valid(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && f >= 0 && f <= 1;
    static AtomicSaveStorage.Validation ValidateProfile(string json, out string reason)
    {
        var p = JsonUtility.FromJson<Profile>(json);
        reason = "プレイヤー傾向の保存形式が不正です";
        if (p != null && p.Version > 1)
        { reason = "新しい形式のプレイヤー傾向を以前の版では上書きしません"; return AtomicSaveStorage.Validation.FutureSchema; }
        if (HasFutureSpecialHistory(p))
        { reason = "新しい固有能力の学習データを以前の版では上書きしません"; return AtomicSaveStorage.Validation.FutureSchema; }
        bool valid = p != null && p.Version == 1 && p.Matches >= 0 && p.Matches <= 1000000
            && Valid(p.Aggression) && Valid(p.Economy) && Valid(p.Scout) && Valid(p.Ranged)
            && Valid(p.Retreat) && Valid(p.Skill) && Valid(p.Flank) && Valid(p.Turtle) && ValidSpecialHistory(p);
        if (valid) reason = "";
        return valid ? AtomicSaveStorage.Validation.Valid : AtomicSaveStorage.Validation.Invalid;
    }

    public void Observe(AIBoardState board)
    {
        if (completed || lastTurn == board.TurnCount) return;
        lastTurn = board.TurnCount;
        int count = 0, aggressive = 0, buildings = 0, scouts = 0, archers = 0;
        foreach (var unit in board.AlivePlayerUnits)
        {
            if (unit == null || unit.team != Team.Player || !unit.IsAlive || !board.IsVisibleToEnemy(unit.transform.position)) continue;
            count++;
            int id = unit.GetInstanceID();
            float distance = GridHelper.ChebyshevDistance(unit.transform.position, board.EnemyCrystalPos);
            if (seen.TryGetValue(id, out var previous) && previous.Turn == board.TurnCount - 1)
            {
                responseSamples++;
                if (previous.HP <= .3f) { retreatSamples++; if (distance > previous.Distance + .5f) retreat++; }
                if (previous.Cooldown == 0 && unit.SkillCooldown > 0) skill++;
                Vector3 delta = unit.transform.position - previous.Position;
                if (Mathf.Abs(delta.x) > Mathf.Abs(delta.z) && delta.sqrMagnitude > .5f) flank++;
                if (distance > previous.Distance + .5f) turtle++;
                if (previous.AssassinExposed) RecordSpecialSample(UnitSpecialSkill.AssassinVisionSeal,true,
                    distance > previous.Distance + .5f ? 1 : 0);
            }
            // This match-local position sample is never serialized into the cross-match profile.
            seen[id] = new Seen { Position=unit.transform.position, HP=(float)unit.HP/Mathf.Max(1,unit.MaxHP),
                Distance=distance, Turn=board.TurnCount, Cooldown=unit.SkillCooldown,
                AssassinExposed=SpecialSkillRuntime.Definition(unit)?.ability==UnitSpecialSkill.AssassinVisionSeal&&unit.Special?.nextDamageDouble==true };
            ObserveSpecialState(unit);
            if (unit.type != Type.Unit) buildings++;
            if (unit.kind == Kind.Scout) scouts++;
            if (IsRanged(unit.kind)) archers++;
            if (GridHelper.ChebyshevDistance(unit.transform.position, board.EnemyCrystalPos) <= 6) aggressive++;
        }
        expiredSamples.Clear();
        foreach(var sample in seen)if(sample.Value.Turn<board.TurnCount-1)expiredSamples.Add(sample.Key);
        foreach(int key in expiredSamples)seen.Remove(key);
        if (count > 0) RecordObservation((float)aggressive/count, (float)buildings/count, (float)scouts/count, (float)archers/count);
    }

    // Accepts already aggregated observations; also allows isolated persistence regression tests.
    public void RecordObservation(float attack, float build, float exploration, float range)
    {
        if (completed || !Valid(attack) || !Valid(build) || !Valid(exploration) || !Valid(range)) return;
        observations++; aggression += attack; economy += build; scout += exploration; ranged += range;
    }
    float Blend(float prior, float current, int level)
    {
        if (level < 10) return 0;
        if (observations == 0) return prior;
        float weight = level < 21 ? .2f : .6f;
        return Mathf.Lerp(prior, current / observations, weight);
    }
    public static bool IsRanged(Kind kind) => kind == Kind.Archer || kind == Kind.Crossbow || kind == Kind.Magic || kind == Kind.Magicsniper || kind == Kind.Ninja;
    public PlayerResponseModel CreateResponseModel(int level, PlayerProfiler.PlayerProfile observedProfile = null)
    {
        if (level < 10) return null;
        float currentWeight = level < 21 ? .2f : .6f;
        float RetreatRatio = retreatSamples > 0 ? Mathf.Lerp(history.Retreat, retreat / retreatSamples, currentWeight) : history.Retreat;
        var profile = new PlayerProfiler.PlayerProfile {
            AggressionScore = Blend(history.Aggression, aggression, level),
            EconomyFocus = Blend(history.Economy, economy, level),
            PreferredAttackRange = Blend(history.Ranged, ranged, level) * 5,
            SkillReliance = responseSamples > 0 ? Mathf.Lerp(history.Skill, skill / responseSamples, currentWeight) : history.Skill,
            FlankPreference = responseSamples > 0 ? Mathf.Lerp(history.Flank, flank / responseSamples, currentWeight) : history.Flank,
            TurtleTendency = responseSamples > 0 ? Mathf.Lerp(history.Turtle, turtle / responseSamples, currentWeight) : history.Turtle,
            TotalObservations = Mathf.Min(1000000, observations + history.Matches * 8), MatchesObserved = history.Matches
        };
        // The existing profiler receives visibility-filtered action events. Its aggregate data
        // supplements the inexpensive turn observations, without transferring positions or units.
        if (observedProfile != null && observedProfile.Confidence > 0)
        {
            float weight = observedProfile.Confidence;
            profile.AggressionScore = Mathf.Lerp(profile.AggressionScore, observedProfile.AggressionScore, weight);
            profile.EconomyFocus = Mathf.Lerp(profile.EconomyFocus, observedProfile.EconomyFocus, weight);
            profile.PreferredAttackRange = Mathf.Lerp(profile.PreferredAttackRange, observedProfile.PreferredAttackRange, weight);
            profile.SkillReliance = Mathf.Lerp(profile.SkillReliance, observedProfile.SkillReliance, weight);
            profile.FlankPreference = Mathf.Lerp(profile.FlankPreference, observedProfile.FlankPreference, weight);
            profile.TurtleTendency = Mathf.Lerp(profile.TurtleTendency, observedProfile.TurtleTendency, weight);
            profile.RushTendency = observedProfile.RushTendency;
            profile.TotalObservations = Mathf.Max(profile.TotalObservations, observedProfile.TotalObservations);
        }
        return profile.TotalObservations > 0 ? PlayerResponseModel.FromProfiler(profile, level, RetreatRatio) : null;
    }

    public float Bonus(AIAction action, int level,AIBoardState board=null)
    {
        if (level < 10) return 0;
        float special=SpecialBonus(action,level,board);
        float scale = level == 10 ? .3f : level <= 20 ? .6f : 1f;
        if (action.ActionType == AIActionType.DefenseRepos) return Blend(history.Aggression, aggression, level) * 12 * scale+special;
        if (action.ActionType == AIActionType.Attack && action.TargetUnit != null && action.TargetUnit.type != Type.Unit)
            return Blend(history.Economy, economy, level) * 15 * scale+special;
        if (action.ActionType == AIActionType.Summon && (action.SummonKind == Kind.Assassin || action.SummonKind == Kind.Knight))
            return Blend(history.Ranged, ranged, level) * 12 * scale+special;
        if (action.ActionType == AIActionType.Summon && action.SummonKind == Kind.Scout)
            return Blend(history.Scout, scout, level) * 8 * scale+special;
        return special;
    }
    public bool CompleteMatch()
    {
        if (completed) return true;
        if (observations == 0&&!HasSpecialObservations) { completed = true; return true; }
        var next = Snapshot;
        next.GameVersion = next.LastSavedVersion = FKBuildInfo.Current.GameVersion;
        if (string.IsNullOrEmpty(next.CreatedVersion)) next.CreatedVersion = string.IsNullOrEmpty(history.GameVersion) ? "legacy" : history.GameVersion;
        next.Matches = Mathf.Min(1000000, next.Matches + 1);
        if(observations>0)
        {
            next.Aggression = Mathf.Lerp(history.Aggression, aggression/observations, .2f);
            next.Economy = Mathf.Lerp(history.Economy, economy/observations, .2f);
            next.Scout = Mathf.Lerp(history.Scout, scout/observations, .2f);
            next.Ranged = Mathf.Lerp(history.Ranged, ranged/observations, .2f);
        }
        CompleteSpecialHistory(next);
        if (retreatSamples > 0) next.Retreat = Mathf.Lerp(history.Retreat, retreat / retreatSamples, .2f);
        if (responseSamples > 0)
        {
            next.Skill = Mathf.Lerp(history.Skill, skill / responseSamples, .2f);
            next.Flank = Mathf.Lerp(history.Flank, flank / responseSamples, .2f);
            next.Turtle = Mathf.Lerp(history.Turtle, turtle / responseSamples, .2f);
        }
        try
        {
            if (!AtomicSaveStorage.TryWrite(path, JsonUtility.ToJson(next, true), 4096, ValidateProfile, out string reason))
            { Debug.LogWarning("[AIPlayerModel] " + reason); return false; }
            history = next; completed = true; return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        { Debug.LogWarning("[AIPlayerModel] 学習保存に失敗。ゲーム進行は継続します: " + ex.Message); return false; }
    }
}
