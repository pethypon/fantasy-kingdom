using System;
using System.IO;
using UnityEngine;

/// <summary>Small versioned tendency profile. Never serializes units, coordinates, HP or unseen information.</summary>
public sealed class AIPlayerModel
{
    [Serializable]
    public sealed class Profile
    {
        public int Version = 1;
        public int Matches;
        public float Aggression, Economy, Scout, Ranged;
    }
    readonly string path;
    Profile history;
    int lastTurn = -1, observations;
    float aggression, economy, scout, ranged;
    bool completed;
    public Profile Snapshot => new Profile { Matches = history.Matches, Aggression = history.Aggression,
        Economy = history.Economy, Scout = history.Scout, Ranged = history.Ranged };

    public AIPlayerModel(string storagePath = null)
    {
        path = storagePath ?? Path.Combine(Application.persistentDataPath, "AIPlayerModel.v1.json");
        history = Load(path) ?? Load(path + ".bak") ?? new Profile();
    }
    static bool Valid(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && f >= 0 && f <= 1;
    static Profile Load(string file)
    {
        try
        {
            if (!File.Exists(file) || new FileInfo(file).Length > 4096) return null;
            var p = JsonUtility.FromJson<Profile>(File.ReadAllText(file));
            return p != null && p.Version == 1 && p.Matches >= 0 && p.Matches <= 1000000
                && Valid(p.Aggression) && Valid(p.Economy) && Valid(p.Scout) && Valid(p.Ranged) ? p : null;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        { return null; }
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
            if (unit.type != Type.Unit) buildings++;
            if (unit.kind == Kind.Scout) scouts++;
            if (IsRanged(unit.kind)) archers++;
            if (GridHelper.ChebyshevDistance(unit.transform.position, board.EnemyCrystalPos) <= 6) aggressive++;
        }
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
    public static bool IsRanged(Kind kind) => kind == Kind.Archer || kind == Kind.Crossbow || kind == Kind.Magic || kind == Kind.Magicsniper;
    public float Bonus(AIAction action, int level)
    {
        if (level < 10) return 0;
        float scale = level == 10 ? .3f : level <= 20 ? .6f : 1f;
        if (action.ActionType == AIActionType.DefenseRepos) return Blend(history.Aggression, aggression, level) * 12 * scale;
        if (action.ActionType == AIActionType.Attack && action.TargetUnit != null && action.TargetUnit.type != Type.Unit)
            return Blend(history.Economy, economy, level) * 15 * scale;
        if (action.ActionType == AIActionType.Summon && (action.SummonKind == Kind.Assassin || action.SummonKind == Kind.Knight))
            return Blend(history.Ranged, ranged, level) * 12 * scale;
        if (action.ActionType == AIActionType.Summon && action.SummonKind == Kind.Scout)
            return Blend(history.Scout, scout, level) * 8 * scale;
        return 0;
    }
    public bool CompleteMatch()
    {
        if (completed) return true;
        if (observations == 0) { completed = true; return true; }
        var next = Snapshot;
        next.Matches = Mathf.Min(1000000, next.Matches + 1);
        next.Aggression = Mathf.Lerp(history.Aggression, aggression/observations, .2f);
        next.Economy = Mathf.Lerp(history.Economy, economy/observations, .2f);
        next.Scout = Mathf.Lerp(history.Scout, scout/observations, .2f);
        next.Ranged = Mathf.Lerp(history.Ranged, ranged/observations, .2f);
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path + ".tmp", JsonUtility.ToJson(next, true));
            if (File.Exists(path)) File.Replace(path + ".tmp", path, path + ".bak");
            else File.Move(path + ".tmp", path);
            history = next; completed = true; return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        { Debug.LogWarning("[AIPlayerModel] 学習保存に失敗。ゲーム進行は継続します: " + ex.Message); return false; }
    }
}
