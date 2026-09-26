#if UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using UnityEngine;

public static class MaterialLifetimeRegressionTests
{
    static void Check(string name, bool value)
    {
        if (!value) throw new Exception("[MaterialLifetime] FAIL " + name);
        Debug.Log("[MaterialLifetime] PASS " + name);
    }
    public static void Run()
    {
        var palette = (IDictionary)typeof(PrimitiveMaterialBinding).GetField("palette", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        int before = palette.Count;
        var root = new GameObject("Material lifetime fixture");
        try
        {
            var red = new Color(.9123f,.1234f,.3456f,1);
            var blue = new Color(.1234f,.5678f,.9123f,1);
            Renderer first = null, second = null;
            for (int i=0;i<100;i++)
            {
                var go = new GameObject("Primitive fixture",typeof(MeshRenderer));
                go.transform.SetParent(root.transform);
                var r = go.GetComponent<Renderer>();
                PrimitiveMaterialBinding.Apply(r,red);
                if (i==0) first=r; if (i==1) second=r;
            }
            Check("100 same-colour objects use one material", palette.Count==before+1 && first.sharedMaterial==second.sharedMaterial);
            var shared = first.sharedMaterial;
            PrimitiveMaterialBinding.Apply(first,red);
            Check("repeat apply preserves existing material",first.sharedMaterial==shared && palette.Count==before+1);
            PrimitiveMaterialBinding.Apply(first,blue);
            Check("recolour does not change other objects",first.sharedMaterial!=shared && second.sharedMaterial.color==red && palette.Count==before+2);
            first.gameObject.SetActive(false);
            Check("disabled last user releases palette entry",palette.Count==before+1);
            first.gameObject.SetActive(true);
            Check("reenable reacquires correct colour",first.sharedMaterial!=null && first.sharedMaterial.color==blue && palette.Count==before+2);
            UnityEngine.Object.DestroyImmediate(first.gameObject);
            Check("destroyed user preserves other users material",second.sharedMaterial==shared && palette.Count==before+1);
            root.SetActive(false);
            Check("all disabled releases all fixture entries",palette.Count==before);
            root.SetActive(true);
            Check("all reenabled still share one entry",palette.Count==before+1);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        Check("destroy leaves no fixture entries",palette.Count==before);
    }
}
#endif
