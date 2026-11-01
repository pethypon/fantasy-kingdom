#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;

/// <summary>R1 config contracts only. Source added without execution; no game/Editor acceptance is implied.</summary>
public static class AIIntegratedR1ConfigTests
{
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[R1Config] FAIL " + id); Debug.Log("[R1Config] PASS " + id); }

    public static void Run()
    {
        ValidateRanges(); OwnershipOnReset();
    }
    static void ValidateRanges()
    {
        var config = ScriptableObject.CreateInstance<AIOperationSquadConfig>();
        try
        {
            Check("SQ-default-policy", config.Enabled && config.Size == 4 && config.Limit == 16
                && config.CohesionRadius == 4 && config.AssemblyGraceTurns == 2
                && config.MaximumChainActions == 5 && config.MaximumChainTargets == 8
                && config.MinimumChainStepBudgetMs == 50 && config.MaximumScoreBonus == 2);
            config.StandardSize = int.MinValue; config.MaximumSquads = int.MaxValue;
            config.CohesionRadius = -1; config.AssemblyGraceTurns = 999;
            config.MaximumChainActions = 0; config.MaximumChainTargets = 99;
            config.MinimumChainStepBudgetMs = -5; config.MaximumScoreBonus = float.NaN;
            config.Validate();
            Check("SQ-all-bounds", config.StandardSize == 3 && config.MaximumSquads == 16
                && config.CohesionRadius == 2 && config.AssemblyGraceTurns == 5
                && config.MaximumChainActions == 2 && config.MaximumChainTargets == 8
                && config.MinimumChainStepBudgetMs == 1 && config.MaximumScoreBonus == 2);
            foreach (float value in new[] { float.PositiveInfinity, float.NegativeInfinity, float.NaN })
            {
                config.MaximumScoreBonus = value; config.Validate();
                Check("SQ-nonfinite-safe-default", config.MaximumScoreBonus == 2);
            }
            config.StandardSize = int.MaxValue; config.MaximumSquads = -1;
            config.CohesionRadius = int.MaxValue; config.AssemblyGraceTurns = int.MinValue;
            config.MaximumChainActions = 999; config.MaximumChainTargets = -99;
            config.MinimumChainStepBudgetMs = 9999; config.MaximumScoreBonus = -100;
            config.Validate();
            Check("SQ-opposite-bounds", config.Size == 5 && config.Limit == 1
                && config.CohesionRadius == 8 && config.AssemblyGraceTurns == 1
                && config.MaximumChainActions == 5 && config.MaximumChainTargets == 1
                && config.MinimumChainStepBudgetMs == 250 && config.MaximumScoreBonus == 0);
            config.MaximumScoreBonus = 100; config.Validate();
            Check("SQ-maximum-bonus", config.MaximumScoreBonus == 3);
        }
        finally { UnityEngine.Object.DestroyImmediate(config); }
    }

    static void OwnershipOnReset()
    {
        var type = typeof(AIOperationSquadConfig);
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var active = type.GetField("active", flags); var owns = type.GetField("ownsActive", flags);
        var reset = type.GetMethod("ResetRuntimeCache", flags);
        Check("SQ-runtime-cache-reset-exists-without-Unity-Reset-collision", reset != null);
        var oldActive = (AIOperationSquadConfig)active.GetValue(null); bool oldOwns = (bool)owns.GetValue(null);
        AIOperationSquadConfig generated = null, externallyOwned = null;
        try
        {
            generated = ScriptableObject.CreateInstance<AIOperationSquadConfig>();
            generated.hideFlags = HideFlags.HideAndDontSave;
            active.SetValue(null, generated); owns.SetValue(null, true); reset.Invoke(null, null);
            Check("SQ-generated-fallback-disposed", generated == null && active.GetValue(null) == null && !(bool)owns.GetValue(null));
            externallyOwned = ScriptableObject.CreateInstance<AIOperationSquadConfig>();
            active.SetValue(null, externallyOwned); owns.SetValue(null, false); reset.Invoke(null, null);
            Check("SQ-loaded-resource-not-destroyed", externallyOwned != null && active.GetValue(null) == null && !(bool)owns.GetValue(null));
            reset.Invoke(null, null);
            Check("SQ-repeat-reset-idempotent", active.GetValue(null) == null && !(bool)owns.GetValue(null));
        }
        finally
        {
            if (generated != null) UnityEngine.Object.DestroyImmediate(generated);
            if (externallyOwned != null) UnityEngine.Object.DestroyImmediate(externallyOwned);
            active.SetValue(null, oldActive); owns.SetValue(null, oldOwns);
        }
    }
}
#endif
