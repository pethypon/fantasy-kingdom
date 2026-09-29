using System;
using UnityEditor;
using UnityEngine;
public static class TerrainVariantsRegressionTests
{
    static void Check(bool value, string message) { if (!value) throw new Exception(message); Debug.Log("[TerrainValidation] PASS " + message); }
    public static void Run()
    {
        try
        {
            TerrainVariantsEditor.Create();
            var c = Resources.Load<TerrainVariantCatalog>("Terrain/TerrainVariants");
            for (int level=0; level<3; level++)
            {
                var seen = new System.Collections.Generic.HashSet<GameObject>();
                for (int x=0; x<100; x++) {
                    var a=c.Select(level,x,5,12,34,null);
                    Check(a != null && a==c.Select(level,x,5,12,34,null), "deterministic variant " + level + ":" + x);
                    if (a.GetComponent<MeshRenderer>().sharedMaterial.mainTexture == null)
                        throw new Exception("Missing texture: " + a.name);
                    seen.Add(a);
                }
                Check(seen.Count == 3, "all three variants used at height " + level);
            }
            UnityEngine.Random.InitState(42); float expected=UnityEngine.Random.value;
            UnityEngine.Random.InitState(42); c.Select(1,2,3,4,5,null);
            Check(UnityEngine.Random.value==expected,"gameplay random stream unchanged");
            var empty=ScriptableObject.CreateInstance<TerrainVariantCatalog>();
            empty.grass = new GameObject[] { null, c.grass[0], null };
            Check(empty.Select(0,1,2,3,4,null)==c.grass[0],"null variant slots ignored");
            empty.grass=null; Check(empty.Select(0,1,2,3,4,c.grass[0])==c.grass[0],"empty catalog falls back");
            UnityEngine.Object.DestroyImmediate(empty);

            HeadHPRegressionTests.Run();
            foreach (string name in new[] { "UnknownMist", "ExploredMist" }) {
                var mat=Resources.Load<Material>("Terrain/"+name);
                Check(mat != null && mat.mainTexture != null && mat.GetTexture("_DetailTex") != null,"both mist textures " + name);
                Check(!ShaderUtil.ShaderHasError(mat.shader),"mist shader compiled " + name);
            }
            Debug.Log("[TerrainVariantsRegressionTests] ALL PASSED");
        }
        catch(Exception e) { Debug.LogException(e); throw; }
    }
}
