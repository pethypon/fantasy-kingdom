#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class CrystalPrefabEditor
{
    [MenuItem("Fantasy Kingdom/ルール/クリスタルのHP設定を更新")]
    public static void ApplyMainCrystalRules()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (string path in new[] { "Assets/Prefabs/Map/プレイヤー　クリスタル Variant.prefab", "Assets/Prefabs/Map/敵　クリスタル Variant.prefab", "Assets/Prefabs/Facilities/SubCrystal.prefab" })
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null || EditorUtility.IsDirty(prefab)) continue;
            var openStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (openStage != null && openStage.assetPath == path && openStage.scene.isDirty) continue;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                bool changed = false;
                foreach (var actor in root.GetComponentsInChildren<Status>(true))
                {
                    if (actor.kind != Kind.Crystal && actor.kind != Kind.SubCrystal) continue;
                    int hp = actor.kind == Kind.Crystal ? MainCrystalRules.MaximumHP : FacilityData.SubCrystalMaximumHP;
                    var serialized = new SerializedObject(actor);
                    changed |= SetInt(serialized, "hitPoints", hp);
                    changed |= SetInt(serialized, "MaxHP", hp);
                    changed |= SetInt(serialized, "ShieldTurns", 0);
                    foreach (string name in new[] { "ShieldActivated", "ShieldEverActivated" })
                    {
                        var value = serialized.FindProperty(name);
                        if (value != null && value.boolValue) { value.boolValue = false; changed = true; }
                    }
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        const string standardSub = "Assets/_FantasyKingdom/GameContent/Buildings/既存の建物データ/SubCrystal/Building.asset";
        var data = AssetDatabase.LoadAssetAtPath<FacilityDefinitionData>(standardSub);
        if (data != null && !EditorUtility.IsDirty(data) && data.levels != null && data.levels.Length > 0
            && data.levels[0].HP == 1000 && FacilityData.SubCrystalMaximumHP == 500)
        {
            // One-time migration of the old stock recipe. Custom authored values are preserved.
            var serialized = new SerializedObject(data);
            serialized.FindProperty("levels").GetArrayElementAtIndex(0).FindPropertyRelative("HP").intValue = 500;
            serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(data);
        }
    }
    static bool SetInt(SerializedObject actor, string name, int next)
    {
        var value = actor.FindProperty(name);
        if (value == null || value.intValue == next) return false;
        value.intValue = next; return true;
    }

    [MenuItem("Fantasy Kingdom/Terrain/クリスタルの陣営と判定を整える")]
    public static void Configure()
    {
        ConfigureOne("Assets/Prefabs/Map/プレイヤー　クリスタル Variant.prefab", Team.Player);
        ConfigureOne("Assets/Prefabs/Map/敵　クリスタル Variant.prefab", Team.Enemy);
        AssetDatabase.SaveAssets();
    }
    static void ConfigureOne(string path, Team team)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
        var root = PrefabUtility.LoadPrefabContents(path);
        try { CrystalSystem.ConfigureCrystal(root, team); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
}
#endif
