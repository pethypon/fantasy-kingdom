#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using ChessMesh = R1UnitVisualInstaller.ChessMesh;

public static partial class R3UnitVisualInstaller
{
    const string UnitsFolder = "Assets/_FantasyKingdom/GameContent/R3Production/Units";

    // The original prefab used one of these stock meshes as a placeholder.
    static readonly (Kind kind, Kind placeholder)[] Roster = {
        (Kind.WindSpirit, Kind.Scout), (Kind.LightSpirit, Kind.Magic),
        (Kind.PurifyingSpirit, Kind.WhiteMage), (Kind.Martyr, Kind.Priest),
        (Kind.BladeSpirit, Kind.Assassin), (Kind.RockGolem, Kind.HeavyWarrior),
        (Kind.CrystalSpirit, Kind.Guardian), (Kind.Thunderbird, Kind.DragonKnight),
        (Kind.CurseSpirit, Kind.Magicsniper), (Kind.ThunderSpirit, Kind.Magic),
        (Kind.LifeSpirit, Kind.WhiteMage), (Kind.IceSpirit, Kind.Magic),
        (Kind.Swordsman, Kind.Knight), (Kind.Spearman, Kind.HolyKnight),
        (Kind.Javelineer, Kind.Crossbow), (Kind.Shieldbearer, Kind.Guardian),
        (Kind.Cavalry, Kind.Knight)
    };

    static string MeshPath(Kind kind) => $"{UnitsFolder}/{kind}/Sculpture.asset";
    static string PrefabPath(Kind kind) => $"{UnitsFolder}/{kind}/Visual.prefab";

    [MenuItem("Fantasy Kingdom/開発スタジオ/駒の見た目/R3の専用モデルを導入")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("ゲームを停止してから駒のモデルを導入してください。");

        var marble = AssetDatabase.LoadAssetAtPath<Material>(BoardVisualThemeInstaller.ExistingWhiteMaterialPath);
        var placeholderMaterial = AssetDatabase.LoadAssetAtPath<Material>(R1UnitVisualInstaller.MaterialPath);
        if (marble == null || placeholderMaterial == null) throw new InvalidOperationException("駒の共有マテリアルが見つかりません。");

        int installed = 0;
        foreach (var (kind, placeholder) in Roster)
        {
            string path = PrefabPath(kind);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || !prefab.TryGetComponent<Status>(out var actor) || actor.kind != kind)
                throw new InvalidOperationException($"兵種 {kind} のPrefabを確認してください。");
            var filter = prefab.GetComponent<MeshFilter>();
            var renderer = prefab.GetComponent<MeshRenderer>();
            var stock = AssetDatabase.LoadAssetAtPath<Mesh>(R1UnitVisualInstaller.MeshPath(placeholder));
            if (filter == null || renderer == null || stock == null || filter.sharedMesh != stock ||
                renderer.sharedMaterials.Length != 1 || (renderer.sharedMaterial != placeholderMaterial && renderer.sharedMaterial != marble))
                continue;

            string meshPath = MeshPath(kind);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = Sculpt(kind);
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            using (var editing = new PrefabUtility.EditPrefabContentsScope(path))
            {
                editing.prefabContentsRoot.GetComponent<MeshFilter>().sharedMesh = mesh;
                editing.prefabContentsRoot.GetComponent<MeshRenderer>().sharedMaterial = marble;
            }
            installed++;
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"[R3UnitVisuals] Installed {installed}/{Roster.Length} sculptures.");
    }

    public static void Review()
    {
        var marble = AssetDatabase.LoadAssetAtPath<Material>(BoardVisualThemeInstaller.ExistingWhiteMaterialPath);
        var models = new List<Kind>();
        var meshes = new HashSet<Mesh>();
        foreach (var (kind, _) in Roster)
        {
            models.Add(kind);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath(kind));
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath(kind));
            if (mesh == null || !meshes.Add(mesh) || mesh.vertexCount < 100 || mesh.subMeshCount != 1 ||
                mesh.GetIndexCount(0) > 5400 || mesh.bounds.size.x > 1f || mesh.bounds.size.z > 1f ||
                Mathf.Abs(mesh.bounds.min.y + .5f) > .001f ||
                prefab.GetComponent<MeshFilter>().sharedMesh != mesh || prefab.GetComponent<MeshRenderer>().sharedMaterial != marble ||
                prefab.GetComponentsInChildren<Renderer>(true).Length != 1 || prefab.GetComponentsInChildren<Collider>(true).Length != 1)
                throw new InvalidOperationException($"兵種 {kind} のモデル、サイズ、またはPrefabの設定が不正です。");
            Debug.Log($"[R3UnitVisuals] PASS {kind}: {mesh.GetIndexCount(0) / 3} triangles, {mesh.bounds.size}");
        }
        var output = Environment.GetEnvironmentVariable("FK_UNIT_MODEL_REVIEW");
        if (!string.IsNullOrEmpty(output))
        {
            Directory.CreateDirectory(output);
            models.Add(Kind.Knight);
            string ResolveMesh(Kind kind) => kind == Kind.Knight ? R1UnitVisualInstaller.MeshPath(kind) : MeshPath(kind);
            R1UnitVisualInstaller.RenderRosterPreview(models, ResolveMesh, Path.Combine(output, "models.png"), new Vector3(1.6f, 1.55f, -3.4f), 6);
            R1UnitVisualInstaller.RenderRosterPreview(models, ResolveMesh, Path.Combine(output, "board-view.png"), new Vector3(1.4f, 3.8f, -3.1f), 6);
        }
    }

    public static void BakeForReview()
    {
        Install();
        Review();
        EditorApplication.Exit(0);
    }
}
#endif
