#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

/// <summary>Source-only R4 calibration/save/config contracts. NOT RUN; this is not match or learning-quality evidence.</summary>
public static class AIR4CausalExperienceTests
{
    const string Knowledge = "CH-10", Operators = "ASSEMBLE,FOCUS_FIRE", Context = "combat:balanced";
    const AIOperationGoal Goal = AIOperationGoal.EliminateEnemyForce;
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[R4CausalExperience] FAIL " + id); Debug.Log("[R4CausalExperience] PASS " + id); }

    public static void Run()
    {
        ReceiptAndCalibration(); Capacity(); Validation(); Config(); Persistence();
    }

    static void ReceiptAndCalibration()
    {
        var memory = new AICausalExperienceMemory(Team.Enemy);
        Check("L02-first-completion-receipt", Record(memory, "completed-1", 1));
        Check("L02-no-duplicate-credit", !Record(memory, "completed-1", 1)
            && memory.Profile.Entries[0].Samples == 1 && memory.Profile.Sequence == 1);
        Check("L-minimum-two-samples", memory.Calibration(Knowledge, Operators, Context, Goal) == 0);
        Check("L-two-sample-outcome", Record(memory, "completed-2", 1)
            && memory.Calibration(Knowledge, Operators, Context, Goal) > 0);
        Check("L-caller-cannot-bypass-two-sample-threshold", SingleSample().Calibration(Knowledge, Operators, Context, Goal, 0) == 0);
        Check("L-minimum-samples-can-be-higher", memory.Calibration(Knowledge, Operators, Context, Goal, 3) == 0);
        for (int i = 3; i <= 40; i++) Record(memory, "completed-" + i, 999);
        float calibration = memory.Calibration(Knowledge, Operators, Context, Goal);
        Check("L-calibration-bounded", calibration >= 0 && calibration <= .1f && memory.Profile.Entries[0].MeanOutcome <= 1);

        var failed = new AICausalExperienceMemory(Team.Enemy);
        Record(failed, "failed-1", -999); Record(failed, "failed-2", -999);
        Check("L-negative-calibration-bounded", failed.Calibration(Knowledge, Operators, Context, Goal) < 0
            && failed.Calibration(Knowledge, Operators, Context, Goal) >= -.1f && failed.Profile.Entries[0].MeanOutcome >= -1);
        var uncertain = new AICausalExperienceMemory(Team.Enemy);
        Record(uncertain, "uncertain-1", 1, error: 99); Record(uncertain, "uncertain-2", 1, error: 99);
        Check("L-prediction-error-reduces-confidence-use", uncertain.Calibration(Knowledge, Operators, Context, Goal) == 0
            && uncertain.Profile.Entries[0].PredictionErrorAverage == 1);
        var aborted = new AICausalExperienceMemory(Team.Enemy);
        Check("L-strategic-abort-is-observation", Record(aborted, "defense-interrupt-1", -1, abort: true));
        Check("L-strategic-abort-not-automatic-failure", aborted.Profile.Entries[0].NeutralOrAbort == 1
            && aborted.Profile.Entries[0].Failures == 0 && aborted.Profile.Entries[0].MeanOutcome == 0);
        Check("L-dirty-after-real-result", memory.Dirty);
        memory.MarkSaved(); Check("L-mark-saved-clears-dirty", !memory.Dirty);

        // The receipt may contain battle/actor IDs for deduplication, but the persisted profile must contain only its bounded bit filter.
        string privateReceipt = "privateBattle_f460e18b_actor_91c9d5c2_action_17";
        var privateMemory = new AICausalExperienceMemory(Team.Player);
        Check("L-private-receipt-accepted", Record(privateMemory, privateReceipt, 1));
        string json = JsonUtility.ToJson(privateMemory.Profile);
        Check("L-no-battle-or-actor-id-in-generalized-json", !json.Contains(privateReceipt)
            && !json.Contains("f460e18b") && !json.Contains("91c9d5c2"));
        var restored = new AICausalExperienceMemory(Team.Player);
        Check("L-receipt-dedup-survives-json-roundtrip", restored.Restore(JsonUtility.FromJson<AICausalExperienceProfile>(json))
            && !Record(restored, privateReceipt, 1));
    }

    static void Capacity()
    {
        var memory = new AICausalExperienceMemory(Team.Enemy, int.MaxValue); int accepted = 0;
        // The fixed duplicate filter can conservatively reject a new receipt through a hash collision.
        // Capacity is checked after accepted receipts, not by assuming every arbitrary receipt must be accepted.
        for (int attempt = 0; attempt < 10000 && accepted < 513; attempt++)
            if (Record(memory, "capacity-receipt-" + attempt, 1, context: "capacity:" + attempt)) accepted++;
        Check("L-512-generalized-entry-cap", accepted >= 513 && memory.Profile.Entries.Count == 512
            && memory.Profile.CompletionBits.Length == 128);
        Check("L-capped-profile-is-valid", AICausalExperienceMemory.Valid(memory.Profile, Team.Enemy));
        var small = new AICausalExperienceMemory(Team.Enemy, 2);
        Record(small, "small-1", 1, context: "small:1"); Record(small, "small-2", 1, context: "small:2");
        Record(small, "small-3", 1, context: "small:3");
        bool oldPresent = false; foreach (var entry in small.Profile.Entries) oldPresent |= entry.ContextSignature == "small:1";
        Check("L-oldest-context-evicted-under-cap", small.Profile.Entries.Count == 2 && !oldPresent);
    }

    static void Validation()
    {
        var memory = new AICausalExperienceMemory(Team.Enemy);
        Check("L-rejects-NaN-outcome", !Record(memory, "invalid-nan", float.NaN));
        Check("L-rejects-Infinity-error", !Record(memory, "invalid-inf", 1, error: float.PositiveInfinity));
        Check("L-rejects-unknown-knowledge-id", !memory.Record("invalid-knowledge", "CH-99", Operators, Context, Goal, 1, 0, false, "None"));
        Check("L-rejects-unknown-goal", !memory.Record("invalid-goal", Knowledge, Operators, Context, (AIOperationGoal)9999, 1, 0, false, "None"));
        Check("L-rejects-oversized-context", !Record(memory, "invalid-context", 1, context: new string('X', 129)));
        Check("L-invalid-results-do-not-create-rewards", memory.Profile.Entries.Count == 0 && memory.Profile.Sequence == 0 && !memory.Dirty);
        var valid = SingleSample().Profile;
        var future = Copy(valid); future.SchemaVersion = 2;
        Check("SV-future-causal-schema-rejected", !memory.Restore(future));
        var foreign = Copy(valid); foreign.Owner = Team.Player;
        Check("SV-foreign-causal-owner-rejected", !memory.Restore(foreign));
        var invalidFloat = Copy(valid); invalidFloat.Entries[0].Confidence = float.NaN;
        Check("SV-nonfinite-causal-confidence-rejected", !memory.Restore(invalidFloat));
        var invalidCounts = Copy(valid); invalidCounts.Entries[0].Samples = 7;
        Check("SV-inconsistent-causal-counts-rejected", !memory.Restore(invalidCounts));
        var futureSequence = Copy(valid); futureSequence.Entries[0].LastUseSequence = futureSequence.Sequence + 1;
        Check("SV-future-use-sequence-rejected", !memory.Restore(futureSequence));
        var duplicates = Copy(valid); duplicates.Entries.Add(duplicates.Entries[0]);
        Check("SV-duplicate-causal-context-rejected", !memory.Restore(duplicates));
        var oversized = Copy(valid);
        while (oversized.Entries.Count <= 512) oversized.Entries.Add(oversized.Entries[0]);
        Check("SV-oversized-causal-list-rejected", !memory.Restore(oversized));
        var invalidFilter = Copy(valid); invalidFilter.CompletionBits = new int[129];
        Check("SV-unbounded-receipt-filter-rejected", !memory.Restore(invalidFilter));
        Check("SV-invalid-restore-preserves-valid-memory", memory.Profile.Entries.Count == 0 && memory.Profile.Sequence == 0);
    }

    static void Config()
    {
        var config = ScriptableObject.CreateInstance<AICognitiveAIConfig>();
        try
        {
            Check("R4-default-disabled-and-shadow", !config.Enabled && config.ShadowMode && !config.MayInfluenceActions
                && !config.EnablePlanExecutionHints && !config.EnableGeneralizedExperience);
            config.Enabled = true; config.EnablePlanExecutionHints = true;
            Check("R4-shadow-prevents-selection-change", !config.MayInfluenceActions);
            config.ShadowMode = false;
            Check("R4-explicitly-approved-hint-mode", config.MayInfluenceActions);
            config.EnableWorldModel = false;
            Check("R4-no-world-model-no-execution", !config.MayInfluenceActions);
            config.EnableWorldModel = true; config.EnableAutonomousPlanning = false;
            Check("R4-no-planner-no-execution", !config.MayInfluenceActions);
            config.MaxKnowledgeMatches = int.MaxValue; config.MaxPlansPerGoal = 0; config.MaxPlansTotal = int.MaxValue;
            config.MaxOperatorsPerPlan = -1; config.MaxEnemyResponses = int.MaxValue; config.BeamWidth = int.MinValue;
            config.SwitchMargin = float.NaN; config.MinMeaningfulSamples = -5; config.MaxCrossMatchEntries = int.MaxValue;
            config.MaxTraceEntries = int.MaxValue; config.Validate();
            Check("R4-bounded-planner-config", config.MaxKnowledgeMatches == 12 && config.MaxPlansPerGoal == 1
                && config.MaxPlansTotal == 8 && config.MaxOperatorsPerPlan == 1 && config.MaxEnemyResponses == 3 && config.BeamWidth == 1);
            Check("R4-finite-learning-and-trace-config", config.SwitchMargin == .18f && config.MinMeaningfulSamples == 2
                && config.MaxCrossMatchEntries == 512 && config.MaxTraceEntries == 128);
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    static void Persistence()
    {
        string parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string scratch = Path.GetFullPath(Path.Combine(parent, "FantasyKingdom_R4Experience_" + Guid.NewGuid().ToString("N")));
        try
        {
            var storage = new AICausalExperiencePersistence(scratch, Team.Enemy);
            var future = SingleSample().Profile; future.SchemaVersion = 99;
            Directory.CreateDirectory(Path.GetDirectoryName(storage.ProfilePath));
            string futureJson = JsonUtility.ToJson(future); File.WriteAllText(storage.ProfilePath, futureJson);
            Check("SV-future-json-is-not-loaded", storage.Load() == null);
            Check("SV-loaded-future-json-is-not-overwritten", !storage.Save(SingleSample().Profile)
                && File.ReadAllText(storage.ProfilePath) == futureJson);
            // The atomic boundary must also protect a future file even if the caller never loads it first.
            var freshStorage = new AICausalExperiencePersistence(scratch, Team.Enemy);
            Check("SV-unloaded-future-json-is-not-overwritten", !freshStorage.Save(SingleSample().Profile)
                && File.ReadAllText(storage.ProfilePath) == futureJson);
        }
        finally
        {
            // Only the unique directory created by this fixture may be recursively removed.
            var info = new DirectoryInfo(scratch);
            if (Directory.Exists(scratch) && info.Parent != null && string.Equals(info.Parent.FullName.TrimEnd(Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar), parent, StringComparison.OrdinalIgnoreCase)
                && info.Name.StartsWith("FantasyKingdom_R4Experience_", StringComparison.Ordinal)) Directory.Delete(scratch, true);
        }
    }

    static AICausalExperienceMemory SingleSample()
    { var memory = new AICausalExperienceMemory(Team.Enemy); Record(memory, "fixture-first-completion", 1); return memory; }
    static AICausalExperienceProfile Copy(AICausalExperienceProfile value)
        => JsonUtility.FromJson<AICausalExperienceProfile>(JsonUtility.ToJson(value));
    static bool Record(AICausalExperienceMemory memory, string receipt, float outcome,
        float error = 0, bool abort = false, string context = Context)
        => memory.Record(receipt, Knowledge, Operators, context, Goal, outcome, error, abort, abort ? "StrategicAbort" : "None");
}
#endif
