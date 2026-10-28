#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

/// <summary>Explicit isolated native Editor entry point; does not schedule itself or alter production assets.</summary>
public static class AIPlaytestFocusedContracts
{
    public static void Run()
    {
        if (new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name != "UnityValidation")
            throw new InvalidOperationException("Focused playtest regressions require the isolated validation project.");
        AIR3MaterialCapacityTests.Run();
        AIPlaytestExplorationTests.Run();
        Debug.Log("[AIPlaytestFocusedContracts] PASS material capacity and exploration return contracts");
    }
}
#endif
