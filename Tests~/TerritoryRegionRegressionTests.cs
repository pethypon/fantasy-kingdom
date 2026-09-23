using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
public static class TerritoryRegionRegressionTests {
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);Debug.Log("[TerritoryRegression] PASS "+name);}
    public static void Run()
    {
        var root=new GameObject("Territory regression temporary");
        try {
            var o=root.AddComponent<TerritoryRegionOverlay>();
            var cells=new List<Vector3>{new Vector3(0,1,0),new Vector3(1,1,0)};
            o.SetCells(cells,cells[0],Color.cyan,false);
            var mesh=root.GetComponent<MeshFilter>().sharedMesh;
            var flags=new List<Vector4>(); mesh.GetUVs(1,flags);
            Check(mesh.vertexCount==8 && flags[0].y==0 && flags[4].x==0,"adjacent cells have no interior border");
            Check(root.GetComponentsInChildren<Renderer>().Length==1,"one renderer for region");
            Check(root.GetComponentsInChildren<Collider>().Length==0,"no input-blocking collider");
            o.SetCells(cells,cells[0],Color.red,true);
            Check(mesh.vertexCount==0,"enemy starts hidden");
            o.SetVisibleCells(new HashSet<Vector3Int>{Vector3Int.zero});
            mesh.GetUVs(1,flags);
            Check(mesh.vertexCount==4 && flags[0].y==0,"partial enemy vision excludes hidden cell and false boundary");
            o.SetVisibleCells(new HashSet<Vector3Int>{Vector3Int.zero,Vector3Int.right});
            Check(mesh.vertexCount==8,"vision reveals full region");
            o.SetCells(new List<Vector3>{cells[0]},cells[0],Color.red,true);
            mesh.GetUVs(1,flags);
            Check(mesh.vertexCount==4 && flags[0]==Vector4.one,"shrinking region rebuilds outside edge");
            Check(!ShaderUtil.ShaderHasError(root.GetComponent<MeshRenderer>().sharedMaterial.shader),"overlay shader compiled");
        }finally{UnityEngine.Object.DestroyImmediate(root);}
    }
}

