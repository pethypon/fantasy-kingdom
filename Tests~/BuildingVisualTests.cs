#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Checks the authored visual assets and actual BuildSystem mapping, rather than mirrored construction math.</summary>
public static class BuildingVisualTests
{
    public static void Run(GameSystems live = null)
    {
        const string art = "Assets/_FantasyKingdom/Art/Buildings";
        const string stock = "Assets/Prefabs/Facilities";
        const string authored = "Assets/_FantasyKingdom/GameContent/Buildings/既存の建物データ";
        string[] names = { "Field", "Bakery", "LoggingCamp", "Quarry", "Mine", "Barracks", "House", "LuxuryHouse", "Well", "Warehouse", "WoodWall", "StoneWall", "Mortar", "Cannon", "RestraintTrap", "Spiketrap", "Hero'sSword", "SubCrystal" };
        var shared = AssetDatabase.LoadAssetAtPath<Material>(art + "/BuildingPalette.mat");
        if (shared == null) throw new InvalidOperationException("[BuildingVisualTests] Building assets have not been generated.");
        int checks = 0;
        void Check(bool pass, string label) { if (!pass) throw new InvalidOperationException("[BuildingVisualTests] " + label); checks++; }
        Check(shared.enableInstancing, "shared palette supports GPU instancing");
        Check(shared.shader != null && shared.shader.isSupported, "palette shader supported");
        Check(shared.mainTexture != null && shared.mainTexture.width <= 32 && shared.mainTexture.height <= 32, "tiny palette texture budget");
        var meshes = new HashSet<Mesh>();
        for (int i = 0; i < names.Length; i++)
        {
            var value = AssetDatabase.LoadAssetAtPath<GameObject>(stock + "/" + names[i] + ".prefab");
            Check(value != null, names[i] + " exists");
            Check(!PrefabUtility.IsPartOfVariantPrefab(value), names[i] + " does not depend on an unrelated/deleted building shell");
            var filter = value.GetComponent<MeshFilter>();
            var mesh = filter == null ? null : filter.sharedMesh;
            Check(mesh != null && mesh.subMeshCount == 1 && mesh.vertexCount > 30, names[i] + " baked geometry");
            Check(meshes.Add(mesh), names[i] + " distinct silhouette");
            Check(mesh.triangles.Length / 3 <= 2500, names[i] + " triangle budget");
            var renderer = value.GetComponentsInChildren<Renderer>(true);
            Check(renderer.Length == 1 && renderer[0].sharedMaterials.Length == 1 && renderer[0].sharedMaterial == shared, names[i] + " one renderer and shared material");
            Check(value.GetComponentsInChildren<Collider>(true).Length == 1 && value.GetComponent<BoxCollider>() != null, names[i] + " one simple collider");
            Check(value.GetComponent<Animator>() == null && value.GetComponent<ParticleSystem>() == null, names[i] + " no perpetual animation/VFX cost");
            Check(value.transform.localScale == Vector3.one, names[i] + " scale independent from inherited cube");
            Check(mesh.bounds.max.x <= .51f && mesh.bounds.min.x >= -.51f && mesh.bounds.max.z <= .51f && mesh.bounds.min.z >= -.51f, names[i] + " cell footprint");
            Check(Mathf.Abs(mesh.bounds.min.y + .5f) < .0001f && mesh.bounds.max.y <= .9f, names[i] + " base aligned with terrain top, not actor center");
            var box = value.GetComponent<BoxCollider>();
            Check((box.center - mesh.bounds.center).sqrMagnitude < .000001f, names[i] + " collider center follows grounded mesh");
            Check((box.size - mesh.bounds.size - new Vector3(.025f, .025f, .025f)).sqrMagnitude < .000001f, names[i] + " collider size encloses mesh with small tolerance");
            Check(box.center.x - box.size.x * .5f >= -.51f && box.center.x + box.size.x * .5f <= .51f
                && box.center.z - box.size.z * .5f >= -.51f && box.center.z + box.size.z * .5f <= .51f, names[i] + " collider remains in one cell");
            // Exercise the same prefab position/rotation contract used by
            // BuildSystem, without charging resources or mutating the live map.
            var instance = UnityEngine.Object.Instantiate(value);
            try
            {
                int testedCells = 0;
                if (live != null && live.MapCreate != null) foreach (var cell in live.MapCreate.SetPos)
                {
                    int x = Mathf.RoundToInt(cell.x), z = Mathf.RoundToInt(cell.z);
                    if (!live.MapCreate.TryGetHeight(x, z, out float y)) continue;
                    instance.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.identity);
                    Physics.SyncTransforms();
                    var actualRenderer = instance.GetComponent<Renderer>();
                    Check(Mathf.Abs(actualRenderer.bounds.min.y - live.MapCreate.SurfaceTop(x, z)) < .001f, names[i] + " actual map grounding");
                    var actualBox = instance.GetComponent<BoxCollider>();
                    Check(actualBox.bounds.min.x >= x - .51f && actualBox.bounds.max.x <= x + .51f
                        && actualBox.bounds.min.z >= z - .51f && actualBox.bounds.max.z <= z + .51f, names[i] + " actual map collider footprint");
                    Check(actualBox.Raycast(new Ray(new Vector3(x, y + 2, z), Vector3.down), out _, 4), names[i] + " center click hits own building");
                    Check(!actualBox.Raycast(new Ray(new Vector3(x + 1, y + 2, z), Vector3.down), out _, 4), names[i] + " adjacent cell click does not hit building");
                    if (++testedCells == 3) break;
                }
                else
                {
                    foreach (int height in new[] { 1, 2, 16 })
                    {
                        instance.transform.SetPositionAndRotation(new Vector3(7, height, 9), Quaternion.identity);
                        Check(Mathf.Abs(instance.GetComponent<Renderer>().bounds.min.y - (height - .5f)) < .001f, names[i] + " map center/terrain height contract");
                    }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
            var copy = AssetDatabase.LoadAssetAtPath<GameObject>(authored + "/" + (FacilityKind)i + "/Visual.prefab");
            if (copy != null)
            {
                Check(copy.GetComponent<Status>() != null, names[i] + " authored gameplay status retained");
                var copyFilter = copy.GetComponent<MeshFilter>();
                Check(copyFilter != null && copyFilter.sharedMesh == mesh, names[i] + " studio/default art consistent");
                Check(copy.GetComponent<Status>().AuthoredFacility != null, names[i] + " authored definition reference retained");
                var authoredBox = copy.GetComponent<BoxCollider>();
                Check(authoredBox != null && (authoredBox.center - box.center).sqrMagnitude < .000001f
                    && (authoredBox.size - box.size).sqrMagnitude < .000001f, names[i] + " studio collider matches stock art");
            }
        }
        var systemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/GameSystem/GameGenerator 1.prefab");
        var build = systemPrefab == null ? null : systemPrefab.GetComponentInChildren<BuildSystem>(true);
        Check(build != null, "BuildSystem exists");
        var mapping = new SerializedObject(build).FindProperty("facilityPrefabs");
        Check(mapping.arraySize == names.Length, "mapping exactly covers current 18 kinds");
        for (int i = 0; i < mapping.arraySize; i++)
        {
            var row = mapping.GetArrayElementAtIndex(i);
            Check(row.FindPropertyRelative("kind").intValue == i, "mapping kind " + i);
            Check(row.FindPropertyRelative("prefab").objectReferenceValue == AssetDatabase.LoadAssetAtPath<GameObject>(stock + "/" + names[i] + ".prefab"), "mapping art " + i);
        }
        Debug.Log("[BuildingVisualTests] ALL PASSED checks=" + checks + " buildings=" + names.Length + " sharedMaterial=1");
    }
}
#endif
