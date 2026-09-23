using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One mesh per faction. Only outside edges are outlined; no per-cell objects or colliders.</summary>
public sealed class TerritoryRegionOverlay : MonoBehaviour
{
    readonly Dictionary<Vector2Int,float> cells=new Dictionary<Vector2Int,float>();
    readonly HashSet<Vector3Int> visible=new HashSet<Vector3Int>();
    readonly List<Vector3> vertices=new List<Vector3>();
    readonly List<Color> colors=new List<Color>();
    readonly List<int> triangles=new List<int>();
    Mesh mesh;
    Material material;
    Color tint;
    bool restrictVisibility;
    readonly List<Vector2> uv=new List<Vector2>();
    readonly List<Vector4> edgeFlags=new List<Vector4>();

    public void SetCells(IReadOnlyList<Vector3> positions, Vector3 crystal, Color color, bool hiddenUntilSeen)
    {
        cells.Clear(); tint=color; restrictVisibility=hiddenUntilSeen;
        if(positions!=null) foreach(var p in positions) cells[new Vector2Int(Mathf.RoundToInt(p.x),Mathf.RoundToInt(p.z))]=p.y-0.48f;
        // The gameplay list omits the occupied main crystal. Visually it belongs inside the region.
        cells[new Vector2Int(Mathf.RoundToInt(crystal.x),Mathf.RoundToInt(crystal.z))]=crystal.y-0.48f;
        EnsureRenderer(); Rebuild();
    }
    public void SetVisibleCells(HashSet<Vector3Int> current)
    {
        if(!restrictVisibility || visible.SetEquals(current)) return;
        visible.Clear(); visible.UnionWith(current); Rebuild();
    }
    void EnsureRenderer()
    {
        if(mesh!=null) return;
        mesh=new Mesh { name="Territory continuous surface", indexFormat=IndexFormat.UInt32 };
        mesh.MarkDynamic();
        gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        var renderer=gameObject.AddComponent<MeshRenderer>();
        material=new Material(Resources.Load<Shader>("Shaders/TerritoryRegion"));
        renderer.sharedMaterial=material;
        renderer.shadowCastingMode=ShadowCastingMode.Off; renderer.receiveShadows=false;
        renderer.lightProbeUsage=LightProbeUsage.Off; renderer.reflectionProbeUsage=ReflectionProbeUsage.Off;
    }
    void Quad(float x0,float z0,float x1,float z1,float y,Color c,Vector4 edges)
    {
        int start=vertices.Count;
        vertices.Add(new Vector3(x0,y,z0)); vertices.Add(new Vector3(x0,y,z1));
        vertices.Add(new Vector3(x1,y,z1)); vertices.Add(new Vector3(x1,y,z0));
        uv.Add(new Vector2(0,0)); uv.Add(new Vector2(0,1)); uv.Add(new Vector2(1,1)); uv.Add(new Vector2(1,0));
        for(int i=0;i<4;i++) { colors.Add(c); edgeFlags.Add(edges); }
        triangles.Add(start);triangles.Add(start+1);triangles.Add(start+2);
        triangles.Add(start);triangles.Add(start+2);triangles.Add(start+3);
    }
    void Rebuild()
    {
        vertices.Clear(); colors.Clear(); triangles.Clear(); uv.Clear(); edgeFlags.Clear();
        var fill=new Color(tint.r,tint.g,tint.b,0.055f);

        foreach(var pair in cells)
        {
            var p=pair.Key;
            if(restrictVisibility && !visible.Contains(new Vector3Int(p.x,0,p.y))) continue;
            float x=p.x,z=p.y,y=pair.Value;
            // The shader draws one continuous inset border; corners never have overlapping strips.
            // Use all cells for adjacency, even where enemy vision clips the rendered surface.
            var edges=new Vector4(cells.ContainsKey(p+Vector2Int.left)?0:1,
                cells.ContainsKey(p+Vector2Int.right)?0:1,
                cells.ContainsKey(p+Vector2Int.down)?0:1,
                cells.ContainsKey(p+Vector2Int.up)?0:1);
            Quad(x-.5f,z-.5f,x+.5f,z+.5f,y,fill,edges);
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetUVs(0,uv); mesh.SetUVs(1,edgeFlags); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
    }
    void OnDestroy()
    {
        if(mesh) { if(Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh); }
        if(material) { if(Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
    }
}

