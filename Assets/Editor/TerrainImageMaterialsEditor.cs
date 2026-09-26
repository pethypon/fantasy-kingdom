using UnityEditor;
using UnityEngine;

/// <summary>Creates editable terrain materials and default prefabs from the supplied images.</summary>
public static class TerrainImageMaterialsEditor
{
    [MenuItem("Fantasy Kingdom/Create Image Terrain Materials")]
    public static void Create()
    {
        Make("Water", "WaterBlock", .7f);
        Make("Grass", "GrassBlock", .12f);
        Make("RockyGrass", "RockyGrassBlock", .18f);
        Make("Rock", "HighMountainBlock", .22f);
        AssetDatabase.SaveAssets();
    }
    static void Make(string textureName, string prefabName, float smoothness)
    {
        const string root = "Assets/Resources/Terrain/";
        string texturePath = root + "Textures/" + textureName + ".png";
        var importer = (TextureImporter)AssetImporter.GetAtPath(texturePath);
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true; importer.mipmapEnabled = true;
        importer.wrapMode = TextureWrapMode.Repeat; importer.maxTextureSize = 2048;
        importer.anisoLevel = 4; importer.SaveAndReimport();
        string materialPath = root + textureName + "Image.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard"));
            AssetDatabase.CreateAsset(material, materialPath);
        }
        material.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        material.color = Color.white; material.SetFloat("_Metallic",0); material.SetFloat("_Glossiness",smoothness);
        EditorUtility.SetDirty(material);
        string prefabPath = root + prefabName + ".prefab";
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null;
        var obj = exists ? PrefabUtility.LoadPrefabContents(prefabPath) : GameObject.CreatePrimitive(PrimitiveType.Cube);
        try
        {
            obj.name = prefabName;
            if (!exists) obj.layer = 6; // Same terrain layer as WaterBlock / HighMountainBlock.
            foreach (var renderer in obj.GetComponentsInChildren<MeshRenderer>(true)) renderer.sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(obj,prefabPath);
        }
        finally { if(exists) PrefabUtility.UnloadPrefabContents(obj); else Object.DestroyImmediate(obj); }
    }
}
