#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
public static class TerrainPrefabEditor
{
    [MenuItem("Fantasy Kingdom/Create Terrain Prefabs")]
    public static void Create()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets","Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/Terrain")) AssetDatabase.CreateFolder("Assets/Resources","Terrain");
        Make("WaterBlock", new Color(0.08f,0.42f,0.68f),0.65f);
        Make("HighMountainBlock", new Color(0.38f,0.4f,0.45f),0.05f);
        AssetDatabase.SaveAssets();
    }
    static void Make(string name, Color color, float smoothness)
    {
        string path = "Assets/Resources/Terrain/" + name;
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path + ".prefab") != null) return;
        var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Map/土ブロック.prefab");
        var obj = Object.Instantiate(source); obj.name = name; obj.transform.position = Vector3.zero;
        var renderer = obj.GetComponent<MeshRenderer>();
        var material = new Material(renderer.sharedMaterial) { name = name };
        material.SetColor("_Color",color); material.SetColor("_BaseColor",color);
        material.SetFloat("_Glossiness",smoothness); material.SetFloat("_Smoothness",smoothness);
        AssetDatabase.CreateAsset(material,path + ".mat"); renderer.sharedMaterial = material;
        PrefabUtility.SaveAsPrefabAsset(obj,path + ".prefab"); Object.DestroyImmediate(obj);
    }
}
#endif
