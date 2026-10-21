#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>Mesh budgets, exact prefab behaviour preservation, and conservative repeat installation.</summary>
public static class R1UnitVisualTests
{
    static int checks;
    static void Check(bool value, string name) { if (!value) throw new InvalidOperationException("[R1UnitVisualTests] FAIL " + name); checks++; }
    static string Sha(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))); }

    public static void InstallAndRun()
    {
        var before = new Dictionary<Kind, string>(); var actors = new Dictionary<Kind, string>(); var colliders = new Dictionary<Kind, string>();
        var guids = new Dictionary<Kind, string>();
        var protectedModels = new Dictionary<string, string>();
        foreach (string path in new[] { "Assets/Prefabs/Units/試験用/HighEnemy Variant.prefab", "Assets/Prefabs/Units/試験用/Player Variant Variant.prefab",
            "Assets/Prefabs/Units/試験用/Enemy Variant.prefab", "Assets/Prefabs/Units/Player/King.prefab", "Assets/Prefabs/Units/Enemy/StrangeKing.prefab",
            "Assets/Prefabs/GameSystem/GameGenerator 1.prefab" })
            if (File.Exists(path)) protectedModels[path] = Sha(path);
        foreach (var kind in R1UnitVisualInstaller.Kinds)
        {
            before[kind] = Sha(R1UnitVisualInstaller.DataPath(kind)); guids[kind] = AssetDatabase.AssetPathToGUID(R1UnitVisualInstaller.PrefabPath(kind));
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(R1UnitVisualInstaller.PrefabPath(kind));
            actors[kind] = EditorJsonUtility.ToJson(prefab.GetComponent<Status>());
            colliders[kind] = EditorJsonUtility.ToJson(prefab.GetComponent<SphereCollider>());
        }
        R1UnitVisualInstaller.Install();
        foreach (var kind in R1UnitVisualInstaller.Kinds)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(R1UnitVisualInstaller.PrefabPath(kind));
            if (before[kind] != Sha(R1UnitVisualInstaller.DataPath(kind)) || guids[kind] != AssetDatabase.AssetPathToGUID(R1UnitVisualInstaller.PrefabPath(kind))
                || actors[kind] != EditorJsonUtility.ToJson(prefab.GetComponent<Status>())
                || colliders[kind] != EditorJsonUtility.ToJson(prefab.GetComponent<SphereCollider>()))
                throw new InvalidOperationException("[R1UnitVisualTests] Existing stats, Status, collider, or prefab GUID changed: " + kind);
        }
        foreach (var pair in protectedModels) if (Sha(pair.Key) != pair.Value)
            throw new InvalidOperationException("[R1UnitVisualTests] Completed initial model or scene reference changed: " + pair.Key);
        R1UnitVisualInstaller.RenderPreview(); Run();
    }

    public static void Run()
    {
        checks = 0; R1UnitVisualInstaller.ValidateAssets();
        var distinct = new HashSet<string>(); var snapshots = new Dictionary<string, string>();
        var material = AssetDatabase.LoadAssetAtPath<Material>(R1UnitVisualInstaller.MaterialPath);
        var white = AssetDatabase.LoadAssetAtPath<Material>(BoardVisualThemeInstaller.ExistingWhiteMaterialPath);
        foreach (var kind in R1UnitVisualInstaller.Kinds)
        {
            string meshPath = R1UnitVisualInstaller.MeshPath(kind), prefabPath = R1UnitVisualInstaller.PrefabPath(kind);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath); var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Check(mesh != null && mesh.vertexCount > 100 && mesh.subMeshCount == 1 && mesh.triangles.Length / 3 <= 1800, "single bounded mesh " + kind);
            Check(distinct.Add(Sha(meshPath)), "distinct authored silhouette " + kind);
            var expectedMaterial = kind == Kind.HeavyWarrior || kind == Kind.DragonKnight ? white : material;
            Check(prefab.GetComponent<MeshFilter>().sharedMesh == mesh && prefab.GetComponent<MeshRenderer>().sharedMaterial == expectedMaterial,
                "playable prefab uses its original shared material " + kind);
            Check(prefab.GetComponentsInChildren<Renderer>(true).Length == 1 && prefab.GetComponentsInChildren<Collider>(true).Length == 1
                && prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 1 && prefab.GetComponent<Status>().kind == kind, "one renderer and original actor/collider only " + kind);
            Check(!R1UnitVisualInstaller.CanReplacePrimitivePrefab(prefab, kind), "generated visual is protected on repeat " + kind);
            snapshots[meshPath] = Sha(meshPath); snapshots[prefabPath] = Sha(prefabPath);
        }
        snapshots[R1UnitVisualInstaller.MaterialPath] = Sha(R1UnitVisualInstaller.MaterialPath);
        R1UnitVisualInstaller.Install();
        foreach (var pair in snapshots) Check(Sha(pair.Key) == pair.Value, "repeat leaves authored asset bytes unchanged " + pair.Key);
        CheckConservativeRecognition();
        Debug.Log("[R1UnitVisualTests] " + checks + " checks passed.");
    }
    static void CheckConservativeRecognition()
    {
        var fixture = GameObject.CreatePrimitive(PrimitiveType.Sphere); fixture.SetActive(false);
        Material customMaterial = null;
        try
        {
            var actor = fixture.AddComponent<Status>(); actor.kind = Kind.WhiteMage; actor.type = Type.Unit;
            var original = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath("d34b0ff78bdc5d846b5e910a255c76ce"));
            var renderer = fixture.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = original; renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; renderer.receiveShadows = true;
            Check(R1UnitVisualInstaller.CanReplacePrimitivePrefab(fixture, Kind.WhiteMage), "untouched primitive copy recognized");
            customMaterial = new Material(original); renderer.sharedMaterial = customMaterial;
            Check(!R1UnitVisualInstaller.CanReplacePrimitivePrefab(fixture, Kind.WhiteMage), "custom material preserved"); renderer.sharedMaterial = original;
            fixture.transform.localScale = Vector3.one * 1.1f; Check(!R1UnitVisualInstaller.CanReplacePrimitivePrefab(fixture, Kind.WhiteMage), "custom transform preserved"); fixture.transform.localScale = Vector3.one;
            fixture.GetComponent<SphereCollider>().radius = .45f; Check(!R1UnitVisualInstaller.CanReplacePrimitivePrefab(fixture, Kind.WhiteMage), "custom selection collider preserved"); fixture.GetComponent<SphereCollider>().radius = .5f;
            var child = new GameObject("author child"); child.transform.SetParent(fixture.transform, false);
            Check(!R1UnitVisualInstaller.CanReplacePrimitivePrefab(fixture, Kind.WhiteMage), "custom hierarchy preserved"); UnityEngine.Object.DestroyImmediate(child);
            fixture.AddComponent<BoxCollider>(); Check(!R1UnitVisualInstaller.CanReplacePrimitivePrefab(fixture, Kind.WhiteMage), "extra component preserved");
        }
        finally { UnityEngine.Object.DestroyImmediate(fixture); if (customMaterial != null) UnityEngine.Object.DestroyImmediate(customMaterial); }
    }
}
#endif
