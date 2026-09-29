using System;
using UnityEditor;
using UnityEngine;

/// <summary>Idempotent setup: existing materials, prefabs and user lists are preserved.</summary>
public static class TerrainVariantsEditor
{
    const string Root = "Assets/Resources/Terrain/";
    [MenuItem("Fantasy Kingdom/Terrain/地形バリエーション設定を選択")]
    public static void SelectCatalog()
    {
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<TerrainVariantCatalog>(Root + "TerrainVariants.asset");
        EditorGUIUtility.PingObject(Selection.activeObject);
    }
    [MenuItem("Fantasy Kingdom/Terrain/画像ブロックと霧を作成")]
    public static void Create()
    {
        string[] names = { "GrassMeadow", "GrassClover", "LowMountainSparse", "LowMountainGreen", "HighMountainDark", "HighMountainGrey" };
        var blocks = new GameObject[names.Length];
        for (int i = 0; i < names.Length; i++) blocks[i] = MakeBlock(names[i]);
        var catalog = AssetDatabase.LoadAssetAtPath<TerrainVariantCatalog>(Root + "TerrainVariants.asset");
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<TerrainVariantCatalog>();
            catalog.grass = new[] { LoadBlock("GrassBlock"), blocks[0], blocks[1] };
            catalog.lowMountain = new[] { LoadBlock("RockyGrassBlock"), blocks[2], blocks[3] };
            catalog.highMountain = new[] { LoadBlock("HighMountainBlock"), blocks[4], blocks[5] };
            AssetDatabase.CreateAsset(catalog, Root + "TerrainVariants.asset");
        }
        MakeMist("UnknownMist", new Color(.055f,.075f,.105f,1), new Color(.16f,.21f,.26f,1), true);
        MakeMist("ExploredMist", new Color(.075f,.10f,.14f,.55f), new Color(.22f,.28f,.32f,1), false);
        AssetDatabase.SaveAssets();
    }
    static GameObject LoadBlock(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Root + name + ".prefab");
    static GameObject MakeBlock(string name)
    {
        string texturePath = Root + "Textures/" + name + ".png";
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (texture == null) throw new InvalidOperationException("Missing terrain image: " + texturePath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType = TextureImporterType.Default; importer.sRGBTexture = true;
        importer.mipmapEnabled = true; importer.wrapMode = TextureWrapMode.Repeat;
        importer.maxTextureSize = 2048; importer.anisoLevel = 4; importer.isReadable = false;
        importer.SaveAndReimport();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(Root + name + ".mat");
        if (mat == null)
        {
            mat = new Material(Shader.Find("Standard")) { name = name, mainTexture = texture, color = Color.white, enableInstancing = true };
            mat.SetFloat("_Glossiness", .12f); mat.SetFloat("_Metallic", 0);
            AssetDatabase.CreateAsset(mat, Root + name + ".mat");
        }
        var prefab = LoadBlock(name);
        if (prefab != null) return prefab;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            go.name = name; go.layer = 6;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return PrefabUtility.SaveAsPrefabAsset(go, Root + name + ".prefab");
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }
    static void MakeMist(string name, Color color, Color light, bool depth)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(Root + name + ".mat");
        if (existing != null && existing.mainTexture != null) return;
        var shader = Shader.Find("Fantasy Kingdom/Cartographic Mist");
        if (shader == null) throw new InvalidOperationException("Mist shader is missing.");
        var mat = existing != null ? existing : new Material(shader) { name = name };
        mat.SetColor("_Color",color); mat.SetColor("_MistColor",light); mat.SetFloat("_ZWrite",depth ? 1 : 0);
        mat.SetTexture("_MainTex", LoadMistTexture("MistCloudA"));
        mat.SetTexture("_DetailTex", LoadMistTexture("MistCloudB"));
        mat.SetFloat("_Scale", .09f);
        mat.SetFloat("_SrcBlend", depth ? 1 : 5); mat.SetFloat("_DstBlend", depth ? 0 : 10);
        mat.renderQueue = depth ? 2000 : 3000;
        if (existing == null) AssetDatabase.CreateAsset(mat, Root + name + ".mat");
        else EditorUtility.SetDirty(mat);
    }
    static Texture2D LoadMistTexture(string name)
    {
        string path = Root + "Textures/" + name + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.wrapMode = TextureWrapMode.Mirror; importer.mipmapEnabled = true;
        importer.sRGBTexture = false; importer.maxTextureSize = 2048; importer.isReadable = false;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }
}
