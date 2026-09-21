using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Fog logic remains in VisionGenerator; only changed 16x16 render chunks are rebuilt.</summary>
public sealed class FogChunkRenderer : MonoBehaviour
{
    const int Size = 16;
    sealed class Layer
    {
        public Mesh Mesh; public MeshRenderer Renderer;
        public Vector3[] Vertices, Normals; public Vector2[] UV; public int[] Triangles;
        public Vector3 Scale; public float Y; public byte State; public bool Lower;
    }
    sealed class Chunk { public int X, Z; public bool Dirty = true; public readonly List<Layer> Layers = new List<Layer>(); }
    readonly List<Chunk> chunks = new List<Chunk>();
    readonly List<Vector3> vertices = new List<Vector3>(8192), normals = new List<Vector3>(8192);
    readonly List<Vector2> uv = new List<Vector2>(8192);
    readonly List<int> triangles = new List<int>(12288);
    byte[,] states;
    int width, depth;
    MapCreate terrain;
    public int RenderObjectCount { get; private set; }
    public int LastRebuiltChunks { get; private set; }

    public void Initialize(MapCreate map, GameObject fog, GameObject explored, GameObject board, GameObject exploredBoard)
    {
        terrain = map;
        width = map.maxX; depth = map.maxZ; states = new byte[width, depth];
        for (int x = 0; x < width; x++) for (int z = 0; z < depth; z++) states[x,z] = 255;
        for (int x = 0; x < width; x += Size) for (int z = 0; z < depth; z += Size)
        {
            var chunk = new Chunk { X = x, Z = z }; chunks.Add(chunk);
            AddLayer(chunk, fog, map.maxY - 1f, 0);
            AddLayer(chunk, explored, map.maxY - 1f, 1);
            // Extend the lower fog volume to the foundation, closing the visible gap at map edges.
            AddLayer(chunk, fog, (map.maxY + map.minY - 2f) / 2f, 0, map.maxY - map.minY - 1f, true);
            AddLayer(chunk, explored, (map.maxY + map.minY - 2f) / 2f, 1, map.maxY - map.minY - 1f, true);
            AddLayer(chunk, board, map.maxY - 0.4f, 0);
            AddLayer(chunk, exploredBoard, map.maxY - 0.4f, 1);
        }
    }
    void AddLayer(Chunk chunk, GameObject prefab, float y, byte state, float stretch = 1f, bool lower = false)
    {
        var source = prefab.GetComponent<MeshFilter>().sharedMesh;
        var go = new GameObject($"Fog {chunk.X},{chunk.Z} {state}", typeof(MeshFilter), typeof(MeshRenderer));
        go.layer = prefab.layer; go.transform.SetParent(transform, false);
        var mesh = new Mesh { name = go.name }; mesh.MarkDynamic();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = prefab.GetComponent<MeshRenderer>().sharedMaterials;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        var scale = prefab.transform.localScale; scale.y *= Mathf.Max(1f, stretch);
        chunk.Layers.Add(new Layer { Mesh = mesh, Renderer = renderer, Vertices = source.vertices,
            Normals = source.normals, UV = source.uv, Triangles = source.triangles, Scale = scale, Y = y, State = state, Lower = lower });
        RenderObjectCount++;
    }
    void DirtyAt(int x,int z) {
        if(x>=0&&z>=0&&x<width&&z<depth) chunks[(x/Size)*((depth+Size-1)/Size)+z/Size].Dirty=true;
    }
    bool SameState(int x,int z,byte state) => x>=0&&z>=0&&x<width&&z<depth&&states[x,z]==state;
    public byte GetState(int x, int z) => states[x,z];
    public void Refresh(HashSet<Vector3Int> visible, HashSet<Vector3Int> explored)
    {
        for (int x = 0; x < width; x++) for (int z = 0; z < depth; z++)
        {
            var cell = new Vector3Int(x,0,z);
            byte value = visible.Contains(cell) ? (byte)2 : explored.Contains(cell) ? (byte)1 : (byte)0;
            if (states[x,z] == value) continue;
            states[x,z] = value;
            DirtyAt(x,z);DirtyAt(x-1,z);DirtyAt(x+1,z);DirtyAt(x,z-1);DirtyAt(x,z+1);
        }
        LastRebuiltChunks = 0;
        foreach (var chunk in chunks)
        {
            if (!chunk.Dirty) continue;
            chunk.Dirty = false; LastRebuiltChunks++;
            foreach (var layer in chunk.Layers)
            {
                vertices.Clear(); normals.Clear(); uv.Clear(); triangles.Clear();
                for (int x = chunk.X; x < Mathf.Min(width, chunk.X + Size); x++)
                for (int z = chunk.Z; z < Mathf.Min(depth, chunk.Z + Size); z++)
                {
                    if (states[x,z] != layer.State) continue;
                    var scale = layer.Scale;float centerY=layer.Y;
                    if(layer.Lower) {
                        float top=layer.Y+layer.Scale.y*0.5f;
                        float bottom=terrain.SurfaceTop(x,z)+0.002f;
                        if(bottom>=top) continue;
                        scale.y=top-bottom;centerY=(top+bottom)*0.5f;
                    }
                    int start = vertices.Count;
                    for (int i = 0; i < layer.Vertices.Length; i++)
                    {
                        vertices.Add(Vector3.Scale(layer.Vertices[i], scale) + new Vector3(x,centerY,z));
                        normals.Add(layer.Normals[i]); uv.Add(layer.UV[i]);
                    }
                    for(int t=0;t<layer.Triangles.Length;t+=3) {
                        var n=layer.Normals[layer.Triangles[t]];
                        if(Mathf.Abs(n.x)>0.9f&&SameState(x+(n.x>0?1:-1),z,layer.State)) continue;
                        if(Mathf.Abs(n.z)>0.9f&&SameState(x,z+(n.z>0?1:-1),layer.State)) continue;
                        triangles.Add(start+layer.Triangles[t]);triangles.Add(start+layer.Triangles[t+1]);triangles.Add(start+layer.Triangles[t+2]);
                    }
                }
                layer.Mesh.Clear(); layer.Renderer.enabled = vertices.Count > 0;
                layer.Mesh.SetVertices(vertices); layer.Mesh.SetNormals(normals); layer.Mesh.SetUVs(0,uv);
                layer.Mesh.SetTriangles(triangles,0); layer.Mesh.RecalculateBounds();
            }
        }
    }
    void OnDestroy() { foreach (var chunk in chunks) foreach (var layer in chunk.Layers) if (layer.Mesh != null) Destroy(layer.Mesh); }
}
