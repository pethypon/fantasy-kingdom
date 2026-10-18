using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Presentation assets only. Gameplay identity, costs, masks and saved state stay in their existing definitions.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/見た目/陣営の駒・建物と大理石")]
public sealed class BoardVisualThemeCatalog : ScriptableObject
{
    public const string ResourcePath = "GameContent/BoardVisualTheme";
    [Serializable] public sealed class UnitStyle
    {
        [InspectorName("駒の種類")] public Kind kind;
        [InspectorName("味方のモデル")] public GameObject playerVisual;
        [InspectorName("敵のモデル")] public GameObject enemyVisual;
    }
    [Serializable] public sealed class BuildingStyle
    {
        [InspectorName("建物の種類")] public FacilityKind kind;
        [InspectorName("味方のモデル")] public GameObject playerVisual;
        [InspectorName("敵のモデル")] public GameObject enemyVisual;
    }
    [Serializable] public sealed class BossStyle
    {
        [InspectorName("強敵の種類")] public WildBossArchetype kind;
        [InspectorName("モデル")] public GameObject visual;
    }
    [InspectorName("陣営の見た目を有効化")] public bool enabledForPlay = true;
    [Header("駒: 白大理石 / 黒 / 赤")]
    [InspectorName("味方の駒（白）")] public Material playerUnitMaterial;
    [InspectorName("敵の駒（黒）")] public Material enemyUnitMaterial;
    [InspectorName("強敵（赤）")] public Material bossMaterial;
    [Header("建物: 白大理石 / 黒")]
    [InspectorName("味方の建物（白）")] public Material playerBuildingMaterial;
    [InspectorName("敵の建物（黒）")] public Material enemyBuildingMaterial;
    [Header("既存の完成モデル: UVに依存しない大理石")]
    [InspectorName("味方の完成モデル（白）")] public Material playerPlainMaterial;
    [InspectorName("敵の完成モデル（黒）")] public Material enemyPlainMaterial;
    [InspectorName("強敵の完成モデル（赤）")] public Material bossPlainMaterial;
    [InspectorName("駒のモデル一覧")] public List<UnitStyle> units = new List<UnitStyle>();
    [Tooltip("メインクリスタル・サブクリスタルは元Prefabのモデルと材質を使い、この一覧の差し替え対象から除外します。")]
    [InspectorName("建物のモデル一覧")] public List<BuildingStyle> buildings = new List<BuildingStyle>();
    [InspectorName("強敵のモデル一覧")] public List<BossStyle> bosses = new List<BossStyle>();
    [Tooltip("標準PrefabのMeshのみ。独自制作したモデルを無断で差し替えません。")]
    [InspectorName("差し替え対象の標準メッシュ")] public List<Mesh> managedSourceMeshes = new List<Mesh>();
    [Tooltip("標準コンテンツの定義ID。独自の駒・建物は既存の見た目を保ちます。")]
    [InspectorName("差し替え対象の標準定義ID")] public List<string> managedDefinitionIds = new List<string>();
    static BoardVisualThemeCatalog loaded;
    static bool attempted;
    public static BoardVisualThemeCatalog Active
    { get { if (!attempted) { attempted = true; loaded = Resources.Load<BoardVisualThemeCatalog>(ResourcePath); } return loaded; } }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetCache() { loaded = null; attempted = false; }
    void OnValidate() => GameContentRevision.NotifyChanged();
    public bool ManagesDefinition(string id) => string.IsNullOrEmpty(id) || managedDefinitionIds.Contains(id);
    public bool ManagesMesh(Mesh mesh) => mesh != null && managedSourceMeshes.Contains(mesh);
    public UnitStyle Unit(Kind kind)
    {
        if (kind == Kind.Boss) kind = Kind.King;
        return units.Find(style => style != null && style.kind == kind);
    }
    public BuildingStyle Building(FacilityKind kind) => buildings.Find(style => style != null && style.kind == kind);
    public BossStyle Boss(WildBossArchetype kind) => bosses.Find(style => style != null && style.kind == kind);
}
