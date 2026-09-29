#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
public static class CrystalPrefabEditor
{
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
