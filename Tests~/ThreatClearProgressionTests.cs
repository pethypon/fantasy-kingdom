#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>Progression and old checkpoints use isolated temporary profile/save storage only.</summary>
public static class ThreatClearProgressionTests
{
    static int passed;
    const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;
    static void Check(string label, bool ok)
    { if (!ok) throw new InvalidOperationException("[ThreatClear] FAIL " + label); passed++; Debug.Log("[ThreatClear] PASS " + label); }
    static string Id() => Guid.NewGuid().ToString("N");
    static SaveSystem.GameSaveData Save() => new SaveSystem.GameSaveData
    {
        Turn = 8, ThreatLevel = 8, MapWidth = 35, MapDepth = 35,
        PCPx = 2, PCPy = 1, PCPz = 2, ECPx = 30, ECPy = 1, ECPz = 30,
        MapSeedX = 161803, MapSeedZ = 271828, HasExplicitMapSnapshot = true,
        PlayerAP = new SaveSystem.APSaveData { Current = 12, Reset = 30 },
        EnemyAP = new SaveSystem.APSaveData { Current = 8, Reset = 30 },
        Timer = new SaveSystem.TimerSaveData { TurnTimeRemaining = 43, TurnTimeLimit = 180, PlayerTotalTime = 36000, EnemyTotalTime = 35000 },
        AI = new SaveSystem.AISaveData { HasDeterministicSeed = true, RngSeed = 56192, AITurnCount = 7 }
    };

    public static void Run()
    {
        passed = 0;
        string root = Path.Combine(Path.GetTempPath(), "FK_ThreatClear_" + Id());
        Directory.CreateDirectory(root);
        try
        {
            using var isolated = (IDisposable)typeof(SaveSystem).GetMethod("UseTestStorageRoot", Private).Invoke(null, new object[] { root });
            Check("isolated profile", SaveSystem.TrySaveProfile(new SaveSystem.ProfileData { ThreatLevel = 8 }, out _));
            string win = Id();
            Check("normal victory advances exactly one", SaveSystem.TryRecordMatchResult(win, GameResult.Win, false, out var p, out _)
                && p.Recorded && p.Increased && p.PreviousThreatLevel == 8 && p.ThreatLevel == 9);
            var profile = SaveSystem.LoadProfile();
            Check("victory and identity committed together", profile.TotalWins == 1 && profile.CompletedMatchIds.Contains(win));
            Check("same victory after profile reload cannot award twice", SaveSystem.TryRecordMatchResult(win, GameResult.TimeUpWin, false, out p, out _)
                && p.AlreadyRecorded && !p.Recorded && !p.Increased && p.ThreatLevel == 9 && SaveSystem.LoadProfile().TotalWins == 1);
            Check("time-up victory also advances exactly one", SaveSystem.TryRecordMatchResult(Id(), GameResult.TimeUpWin, false, out p, out _)
                && p.ThreatLevel == 10 && p.Increased);
            string loss = Id();
            Check("defeat records without increasing", SaveSystem.TryRecordMatchResult(loss, GameResult.Lose, false, out p, out _)
                && p.Recorded && p.ThreatLevel == 10 && !p.Increased && SaveSystem.LoadProfile().TotalLosses == 1);
            Check("repeated defeat is idempotent", SaveSystem.TryRecordMatchResult(loss, GameResult.TimeUpLose, false, out p, out _)
                && p.AlreadyRecorded && SaveSystem.LoadProfile().TotalLosses == 1);
            string draw = Id();
            Check("draw never increases threat", SaveSystem.TryRecordMatchResult(draw, GameResult.TimeUpDraw, false, out p, out _)
                && p.Recorded && p.ThreatLevel == 10 && SaveSystem.LoadProfile().TotalWins == 2);
            Check("different ending from the same completed checkpoint is not another match", SaveSystem.TryRecordMatchResult(draw, GameResult.Win, false, out p, out _)
                && p.AlreadyRecorded && p.ThreatLevel == 10);
            string profilePath = Path.Combine(root, "saves", "profile.json");
            string before = File.ReadAllText(profilePath);
            Check("developer autoplay does not record progress", SaveSystem.TryRecordMatchResult(Id(), GameResult.Win, true, out p, out _)
                && p.DeveloperTest && !p.Recorded && p.ThreatLevel == 10 && File.ReadAllText(profilePath) == before);
            Check("invalid identity cannot modify profile", !SaveSystem.TryRecordMatchResult("invalid", GameResult.Win, false, out _, out _)
                && File.ReadAllText(profilePath) == before);

            var saved = Save();
            Check("checkpoint saves match identity", SaveSystem.TrySaveGame(0, saved, out _) && SaveSystem.TryLoadGame(0, out var loaded, out _)
                && loaded.MatchId == saved.MatchId);
            Check("same checkpoint in another slot keeps identity", SaveSystem.TrySaveGame(1, saved, out _) && SaveSystem.TryLoadGame(1, out loaded, out _)
                && loaded.MatchId == saved.MatchId);
            Check("new game snapshots get different identities", Save().MatchId != saved.MatchId);

            saved.Version = saved.SchemaVersion = 8;
            saved.MatchId = null;
            string oldJson = JsonUtility.ToJson(saved);
            string oldPath = Path.Combine(root, "saves", "save_2.json");
            File.WriteAllText(oldPath, oldJson);
            Check("v8 snapshot migrates with stable identity", SaveSystem.TryLoadGame(2, out loaded, out _)
                && loaded.Version == 9 && SaveSystem.IsValidMatchId(loaded.MatchId));
            string legacyId = loaded.MatchId;
            Check("reloading unmodified old save reuses identity", SaveSystem.TryLoadGame(2, out loaded, out _) && loaded.MatchId == legacyId
                && File.ReadAllText(oldPath) == oldJson);
            saved.Turn = 20; saved.SaveDate = "changed";
            File.WriteAllText(oldPath, JsonUtility.ToJson(saved));
            Check("legacy identity ignores turn and save date", SaveSystem.TryLoadGame(2, out loaded, out _) && loaded.MatchId == legacyId);
            Check("legacy completion also awards only once", SaveSystem.TryRecordMatchResult(legacyId, GameResult.Win, false, out p, out _)
                && p.ThreatLevel == 11 && SaveSystem.TryRecordMatchResult(legacyId, GameResult.Win, false, out p, out _)
                && p.AlreadyRecorded && SaveSystem.LoadProfile().ThreatLevel == 11);

            Check("near cap profile", SaveSystem.TrySaveProfile(new SaveSystem.ProfileData { ThreatLevel = 99 }, out _));
            Check("99 advances to maximum 100", SaveSystem.TryRecordMatchResult(Id(), GameResult.Win, false, out p, out _)
                && p.ThreatLevel == 100 && p.Increased);
            string capId = Id();
            Check("maximum remains 100 and victory still counts", SaveSystem.TryRecordMatchResult(capId, GameResult.Win, false, out p, out _)
                && p.ThreatLevel == 100 && !p.Increased && SaveSystem.LoadProfile().TotalWins == 2);
            Check("maximum victory is not counted twice", SaveSystem.TryRecordMatchResult(capId, GameResult.Win, false, out p, out _)
                && p.AlreadyRecorded && SaveSystem.LoadProfile().TotalWins == 2);

            File.WriteAllText(profilePath, "{\"SchemaVersion\":1,\"ThreatLevel\":15,\"TotalWins\":5,\"MatchHistory\":[]}");
            Check("old profile preserves progression", SaveSystem.TryLoadProfile(out profile, out _)
                && profile.ThreatLevel == 15 && profile.TotalWins == 5 && profile.CompletedMatchIds.Count == 0);
            Check("old profile can commit new result", SaveSystem.TryRecordMatchResult(Id(), GameResult.Win, false, out p, out _)
                && p.ThreatLevel == 16 && SaveSystem.LoadProfile().SchemaVersion == SaveSystem.CurrentProfileVersion);
            string future = "{\"SchemaVersion\":99,\"ThreatLevel\":70}";
            File.WriteAllText(profilePath, future);
            Check("future profile is protected from progression write", !SaveSystem.TryRecordMatchResult(Id(), GameResult.Win, false, out p, out _)
                && !p.Recorded && File.ReadAllText(profilePath) == future);

            foreach (int turns in new[] { 1, 9, 19, 100 })
            {
                var threat = new AIThreatLevel(15);
                threat.RecordMatchResult(true, new MatchAnalysis { TurnsPlayed = turns, PrimaryFailure = FailureReason.EconomyCollapse });
                Check("AI in-memory progression agrees +1 at turn " + turns, threat.Level == 16);
                threat.RecordMatchResult(false, new MatchAnalysis { TurnsPlayed = turns });
                Check("AI loss holds threat at turn " + turns, threat.Level == 16);
            }
            var capped = new AIThreatLevel(100); capped.RecordMatchResult(true, new MatchAnalysis { TurnsPlayed = 1 });
            Check("AI in-memory cap", capped.Level == 100);
        }
        finally
        {
            string full = Path.GetFullPath(root), temporary = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (full.StartsWith(temporary, StringComparison.OrdinalIgnoreCase)) Directory.Delete(full, true);
        }
        Debug.Log("[ThreatClear] " + passed + " checks passed");
    }
}
#endif
