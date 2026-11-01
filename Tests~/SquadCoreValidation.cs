#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public static class SquadCoreValidation
{
    public static void Run()
    {
        if (new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name != "UnityValidation") throw new InvalidOperationException("Isolated clone required");
        A1CoreContracts.Run();
        R2CoreContracts.Run();
        CoreLogicTests.RunAll();
        AIAdaptivePolicyTests.Run();
        Debug.Log("[SquadCore] COMPLETE");
    }
}
#endif
