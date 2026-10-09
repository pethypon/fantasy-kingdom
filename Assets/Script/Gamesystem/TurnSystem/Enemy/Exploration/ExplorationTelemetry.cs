using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

[Serializable]
public sealed class ExplorationCandidateScore
{
    public int RegionId;
    public float Score;
    public float UnknownGain;
    public float StaleIntel;
    public float DirectionDiversity;
    public float StrategicRoute;
    public float DistanceFromRecentArea;
    public float TravelCost;
    public float DangerCost;
    public float RecentVisit;
    public float RepeatVisit;
    public float OtherScout;
    public float LoopArea;
    public string RejectReason;
}

[Serializable]
public sealed class ExplorationDecisionRecord
{
    public int Turn;
    public Team ActorTeam;
    public int Explorer;
    public string ActorLifeId;
    public int Objective;
    public int ObjectiveAge;
    public int FrontierRegion;
    public Vector3Int TargetFrontier;
    public float PreviousDistance;
    public float DistanceToTarget;
    public int NewlyRevealedLast3T;
    public int RecentUniqueCells;
    public bool LoopSuspected;
    public string Reason;
    public ObjectiveState State;
    public List<ExplorationCandidateScore> Candidates = new List<ExplorationCandidateScore>();
}

/// <summary>Opt-in bounded JSONL writes. A partial buffer is retained between turns and explicitly flushed at save/end.</summary>
public sealed class ExplorationTelemetry
{
    readonly ExplorationAISettings settings;
    readonly List<ExplorationDecisionRecord> records = new List<ExplorationDecisionRecord>();
    readonly List<string> pending = new List<string>();
    bool failed;
    public IReadOnlyList<ExplorationDecisionRecord> Records => records;
    public string FilePath { get; private set; }
    public ExplorationTelemetry(ExplorationAISettings settings) { this.settings = settings; }
    public void Record(ExplorationDecisionRecord record)
    {
        int cap = Mathf.Clamp(settings.TelemetryMaxRecords, 8, 2048);
        if (records.Count >= cap) records.RemoveAt(0);
        records.Add(record);
        if (!settings.EnableJsonlTelemetry || failed) return;
        if (FilePath == null) FilePath = Path.Combine(Application.persistentDataPath, "FantasyKingdom", "AI", "Exploration", "exploration-" + record.ActorTeam + ".jsonl");
        pending.Add(JsonUtility.ToJson(record));
        if (pending.Count >= Mathf.Clamp(settings.TelemetryBufferRecords, 1, 256)) Flush();
    }
    public void Flush()
    {
        if (pending.Count == 0 || failed) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            long limit = Mathf.Clamp(settings.TelemetryMaxFileBytes, 65536, 64 * 1024 * 1024);
            if (File.Exists(FilePath) && new FileInfo(FilePath).Length >= limit)
            {
                string previous = FilePath + ".previous";
                if (File.Exists(previous)) File.Delete(previous);
                File.Move(FilePath, previous);
            }
            using (var writer = new StreamWriter(FilePath, true, new UTF8Encoding(false)))
                for (int i = 0; i < pending.Count; i++) writer.WriteLine(pending[i]);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
        { failed = true; Debug.LogWarning("探索ログを書き込めません: " + ex.Message); }
        finally { pending.Clear(); }
    }
}
