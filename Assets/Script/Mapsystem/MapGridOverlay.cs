using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One static surface mesh for the whole map, including neutral land, water and mountains.</summary>
public sealed class MapGridOverlay : MonoBehaviour
{
    [SerializeField, Range(0.002f, 0.05f)] float lineWidth = 0.012f;
    [SerializeField] Color lineColor = new Color(0.07f, 0.12f, 0.16f, 0.48f);
    Mesh mesh;
    Material material;
    GameObject surface;

    public void Rebuild(MapCreate map)
    {
        if (surface == null)
        {
            surface = new GameObject("Map cell boundaries", typeof(MeshFilter), typeof(MeshRenderer));
            surface.transform.SetParent(transform, false);
            mesh = new Mesh { name = "Map cell boundaries", indexFormat = IndexFormat.UInt32 };
            material = new Material(Resources.Load<Shader>("Shaders/MapGrid"));
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        var vertices = new List<Vector3>(map.maxX * map.maxZ * 4);
        var uv = new List<Vector2>(vertices.Capacity);
        var triangles = new List<int>(map.maxX * map.maxZ * 6);
        for (int x = 0; x < map.maxX; x++)
        for (int z = 0; z < map.maxZ; z++)
        {
            float y = map.SurfaceTop(x, z) + 0.006f;
            int start = vertices.Count;
            vertices.Add(surface.transform.InverseTransformPoint(new Vector3(x - .5f, y, z - .5f)));
            vertices.Add(surface.transform.InverseTransformPoint(new Vector3(x - .5f, y, z + .5f)));
            vertices.Add(surface.transform.InverseTransformPoint(new Vector3(x + .5f, y, z + .5f)));
            vertices.Add(surface.transform.InverseTransformPoint(new Vector3(x + .5f, y, z - .5f)));
            uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(0, 1));
            uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0));
            triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
            triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 3);
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetUVs(0, uv);
        mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
        OnValidate();
    }

    void OnValidate()
    {
        if (material == null) return;
        material.SetColor("_Color", lineColor);
        material.SetFloat("_Width", lineWidth);
    }
    void OnEnable() { if (surface) surface.SetActive(true); }
    void OnDisable() { if (surface) surface.SetActive(false); }
    void OnDestroy()
    {
        Release(mesh); Release(material); Release(surface);
    }
    static void Release(Object obj)
    {
        if (!obj) return;
        if (Application.isPlaying) Destroy(obj); else DestroyImmediate(obj);
    }
}
