#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;

/// <summary>Read original copies; all save/load writes target a fresh OS temporary directory.</summary>
public static class R2ExistingSaveCopies
{
    public static void Run()
    {
        if(new DirectoryInfo(Path.GetDirectoryName(Application.dataPath)).Name!="UnityValidation")
            throw new InvalidOperationException("Isolated clone required.");
        string copies=Environment.GetEnvironmentVariable("FK_R2_SAVE_BACKUPS");
        if(string.IsNullOrEmpty(copies)||!Directory.Exists(copies))throw new InvalidOperationException("Read-only save-copy folder required.");
        string temp=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"FK_R2_SaveCopies_"+Guid.NewGuid().ToString("N")));
        if(!temp.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Temporary path escaped.");
        Directory.CreateDirectory(Path.Combine(temp,"saves"));
        var method=typeof(SaveSystem).GetMethod("UseTestStorageRoot",BindingFlags.Static|BindingFlags.NonPublic);
        using var scope=(IDisposable)method.Invoke(null,new object[]{temp});
        int passed=0;
        foreach(string path in Directory.GetFiles(copies,"save_*.json"))
        {
            byte[] bytes=File.ReadAllBytes(path); string json=File.ReadAllText(path);
            var before=JsonUtility.FromJson<SaveSystem.GameSaveData>(json);
            File.WriteAllText(Path.Combine(temp,"saves","save_0.json"),json);
            if(!SaveSystem.TryLoadGame(0,out var migrated,out var reason))
                throw new InvalidOperationException("[R2RealSave] FAIL "+Path.GetFileName(path)+" "+reason);
            if(!SaveSystem.TrySaveGame(0,migrated,out reason)||!SaveSystem.TryLoadGame(0,out var twice,out reason))
                throw new InvalidOperationException("[R2RealSave] FAIL roundtrip "+reason);
            string a=JsonUtility.ToJson(migrated),b=JsonUtility.ToJson(twice);
            // SaveDate/LastSavedVersion are writer metadata, not gameplay state.
            twice.SaveDate=migrated.SaveDate;twice.GameVersion=migrated.GameVersion;
            twice.LastSavedVersion=migrated.LastSavedVersion;twice.CreatedVersion=migrated.CreatedVersion;
            if(a!=JsonUtility.ToJson(twice)||migrated.MapWidth!=before.MapWidth||migrated.MapDepth!=before.MapDepth)
                throw new InvalidOperationException("[R2RealSave] FAIL non-idempotent game state");
            using var sha=SHA256.Create();
            if(Convert.ToBase64String(sha.ComputeHash(bytes))!=Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))))
                throw new InvalidOperationException("[R2RealSave] FAIL read-only copy changed");
            int deferred=0;foreach(var unit in migrated.Units)if(unit.FixedStatsMigrationDeferred)deferred++;
            Debug.Log("[R2RealSave] PASS "+Path.GetFileName(path)+" version="+before.Version+"->"+migrated.Version
                +" units="+migrated.Units.Count+" deferred="+deferred+" map="+migrated.MapWidth+"x"+migrated.MapDepth);
            passed++;
        }
        if(passed==0)throw new InvalidOperationException("No existing save copies executed.");
        Debug.Log("[R2RealSave] COMPLETE copies="+passed+" temporaryEvidence="+temp);
    }
}
#endif
