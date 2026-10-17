using UnityEngine;

public sealed partial class FogChunkRenderer
{
    Layer exterior;
    public Mesh ExteriorMesh => exterior?.Mesh;

    void InitializeExterior(MapCreate map, GameObject prefab, Material material)
    {
        if (!map.showExteriorFog) return;
        exterior = AddLayer(new Chunk(), prefab, material, 0, "マップ外の霧");
        // Four non-overlapping strips leave the entire playable rectangle open.
        // Below the lowest terrain: side views cannot hide units along the board edge.
        float y = Mathf.Min(bottom, map.minY - .55f);
        float left = -.5f, right = width - .5f, near = -.5f, far = depth - .5f;
        float extent = Mathf.Max(64f, map.exteriorFogExtent, width * 2f, depth * 2f);
        vertices.Clear(); normals.Clear(); triangles.Clear();
        ExteriorStrip(left - extent, left, near - extent, far + extent, y);
        ExteriorStrip(right, right + extent, near - extent, far + extent, y);
        ExteriorStrip(left, right, near - extent, near, y);
        ExteriorStrip(left, right, far, far + extent, y);
        exterior.Mesh.SetVertices(vertices); exterior.Mesh.SetNormals(normals);
        exterior.Mesh.SetTriangles(triangles, 0); exterior.Renderer.enabled = true;

        // Shallow camera angles can see beyond the plane; keep that horizon in the fog palette.
        var camera = Camera.main;
        if (camera != null && exterior.Renderer.sharedMaterial != null && exterior.Renderer.sharedMaterial.HasProperty("_Color"))
        { camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = exterior.Renderer.sharedMaterial.GetColor("_Color"); }
    }

    void ExteriorStrip(float left, float right, float near, float far, float y)
        => Quad(new Vector3(left, y, near), new Vector3(left, y, far),
            new Vector3(right, y, far), new Vector3(right, y, near), Vector3.up);

    void ReleaseExterior()
    {
        if (exterior?.Mesh == null) return;
        if (Application.isPlaying) Destroy(exterior.Mesh); else DestroyImmediate(exterior.Mesh);
        exterior = null;
    }
}
