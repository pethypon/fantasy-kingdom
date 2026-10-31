#if UNITY_EDITOR
using System;
using System.IO;
using UnityEngine;

public static class R2CoreContracts
{
    public static void Run()
    {
        if(new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name!="UnityValidation")
            throw new InvalidOperationException("R2 checks require the isolated validation clone.");
        R2FixedUnitRulesTests.Run();
        R2SaveMigrationTests.Run();
        R2BarrierRecoveryTests.Run();
        R2TimerUIRulesTests.Run();
        Debug.Log("[R2CoreContracts] COMPLETE");
    }
}
#endif
