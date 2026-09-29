using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One closed shell per fog state/chunk. Coplanar faces are merged; only dirty chunks rebuild.</summary>
public sealed class FogChunkRenderer : MonoBehaviour
{
    const int Size = 16;
    sealed class Layer { public Mesh Mesh; public MeshRenderer Renderer; public byte State; }
    sealed class Chunk { public int X, Z; public bool Dirty = true; public readonly Layer[] Layers = new Layer[2]; }
    readonly List<Chunk> chunks = new List<Chunk>();
    readonly List<Vector3> vertices = new List<Vector3>(8192), normals = new List<Vector3>(8192);
    readonly List<int> triangles = new List<int>(12288);
    readonly bool[] merged = new bool[Size * Size];
    byte[,] states;
    int width, depth, chunksZ;
    float bottom, top;
    public int RenderObjectCount { get; private set; }
    public int LastRebuiltChunks { get; private set; }

    // Keep the original call contract; prefab geometry is replaced by the merged shell.
    public void Initialize(MapCreate map, GameObject fog, GameObject explored, GameObject board, GameObject exploredBoard)
    {
        width = map.maxX; depth = map.maxZ; chunksZ = (depth + Size - 1) / Size;
        bottom = map.minY - .52f; top = map.maxY - .4f;
        states = new byte[width, depth];
        for (int x = 0; x < width; x++) for (int z = 0; z < depth; z++) states[x,z] = 255;
        var unknown = Resources.Load<Material>("Terrain/UnknownMist");
        var known = Resources.Load<Material>("Terrain/ExploredMist");
        for (int x = 0; x < width; x += Size) for (int z = 0; z < depth; z += Size)
        {
            var chunk = new Chunk { X = x, Z = z }; chunks.Add(chunk);
            chunk.Layers[0] = AddLayer(chunk, fog, unknown, 0);
            chunk.Layers[1] = AddLayer(chunk, explored, known, 1);
        }
    }
    Layer AddLayer(Chunk chunk, GameObject prefab, Material material, byte state)
    {
        var go = new GameObject($"Fog {chunk.X},{chunk.Z} {state}", typeof(MeshFilter), typeof(MeshRenderer));
        go.layer = prefab != null ? prefab.layer : gameObject.layer;
        go.transform.SetParent(transform, false);
        var mesh = new Mesh { name = go.name }; mesh.MarkDynamic();
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material != null ? material : prefab.GetComponent<MeshRenderer>().sharedMaterial;
        renderer.enabled = false;
        renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        RenderObjectCount++;
        return new Layer { Mesh = mesh, Renderer = renderer, State = state };
    }
    void DirtyAt(int x, int z)
    {
        if (x >= 0 && z >= 0 && x < width && z < depth) chunks[(x / Size) * chunksZ + z / Size].Dirty = true;
    }
    bool SameState(int x, int z, byte state) => x >= 0 && z >= 0 && x < width && z < depth && states[x,z] == state;
    public byte GetState(int x, int z) => states[x,z];
    public void Refresh(HashSet<Vector3Int> visible, HashSet<Vector3Int> explored)
    {
        for (int x = 0; x < width; x++) for (int z = 0; z < depth; z++)
        {
            var cell = new Vector3Int(x,0,z);
            byte value = visible.Contains(cell) ? (byte)2 : explored.Contains(cell) ? (byte)1 : (byte)0;
            if (states[x,z] == value) continue;
            states[x,z] = value;
            DirtyAt(x,z); DirtyAt(x-1,z); DirtyAt(x+1,z); DirtyAt(x,z-1); DirtyAt(x,z+1);
        }
        LastRebuiltChunks = 0;
        foreach (var chunk in chunks)
        {
            if (!chunk.Dirty) continue;
            chunk.Dirty = false; LastRebuiltChunks++;
            foreach (var layer in chunk.Layers) Rebuild(chunk, layer);
        }
    }
    void Rebuild(Chunk chunk, Layer layer)
    {
        vertices.Clear(); normals.Clear(); triangles.Clear();
        System.Array.Clear(merged, 0, merged.Length);
        int endX = Mathf.Min(width, chunk.X + Size), endZ = Mathf.Min(depth, chunk.Z + Size);
        // Greedy rectangles preserve holes without allocating per-cell meshes or UV arrays.
        for (int z = chunk.Z; z < endZ; z++) for (int x = chunk.X; x < endX; x++)
        {
            if (!Available(chunk,x,z,layer.State)) continue;
            int rx = x + 1;
            while (rx < endX && Available(chunk,rx,z,layer.State)) rx++;
            int rz = z + 1;
            while (rz < endZ)
            {
                bool full = true;
                for (int xx = x; xx < rx; xx++) if (!Available(chunk,xx,rz,layer.State)) { full = false; break; }
                if (!full) break;
                rz++;
            }
            for (int zz = z; zz < rz; zz++) for (int xx = x; xx < rx; xx++) merged[(zz-chunk.Z)*Size+xx-chunk.X] = true;
            float left=x-.5f, right=rx-.5f, near=z-.5f, far=rz-.5f;
            Quad(new Vector3(left,top,near),new Vector3(left,top,far),new Vector3(right,top,far),new Vector3(right,top,near),Vector3.up);
            Quad(new Vector3(left,bottom,near),new Vector3(right,bottom,near),new Vector3(right,bottom,far),new Vector3(left,bottom,far),Vector3.down);
        }
        // Merge consecutive boundary faces. Never emit walls between equal states, including chunk seams.
        for (int x = chunk.X; x < endX; x++) for (int sign = -1; sign <= 1; sign += 2)
        {
            int z = chunk.Z;
            while (z < endZ)
            {
                if (!SameState(x,z,layer.State) || SameState(x+sign,z,layer.State)) { z++; continue; }
                int start = z++;
                while (z < endZ && SameState(x,z,layer.State) && !SameState(x+sign,z,layer.State)) z++;
                float xx=x+sign*.5f, a=start-.5f, b=z-.5f;
                if (sign > 0) Quad(new Vector3(xx,bottom,a),new Vector3(xx,top,a),new Vector3(xx,top,b),new Vector3(xx,bottom,b),Vector3.right);
                else Quad(new Vector3(xx,bottom,b),new Vector3(xx,top,b),new Vector3(xx,top,a),new Vector3(xx,bottom,a),Vector3.left);
            }
        }
        for (int z = chunk.Z; z < endZ; z++) for (int sign = -1; sign <= 1; sign += 2)
        {
            int x = chunk.X;
            while (x < endX)
            {
                if (!SameState(x,z,layer.State) || SameState(x,z+sign,layer.State)) { x++; continue; }
                int start = x++;
                while (x < endX && SameState(x,z,layer.State) && !SameState(x,z+sign,layer.State)) x++;
                float zz=z+sign*.5f, a=start-.5f, b=x-.5f;
                if (sign > 0) Quad(new Vector3(b,bottom,zz),new Vector3(b,top,zz),new Vector3(a,top,zz),new Vector3(a,bottom,zz),Vector3.forward);
                else Quad(new Vector3(a,bottom,zz),new Vector3(a,top,zz),new Vector3(b,top,zz),new Vector3(b,bottom,zz),Vector3.back);
            }
        }
        layer.Mesh.Clear(); layer.Renderer.enabled = vertices.Count != 0;
        layer.Mesh.SetVertices(vertices); layer.Mesh.SetNormals(normals);
        layer.Mesh.SetTriangles(triangles,0,false);
        layer.Mesh.bounds = new Bounds(new Vector3((chunk.X+endX-1)*.5f,(bottom+top)*.5f,(chunk.Z+endZ-1)*.5f),new Vector3(endX-chunk.X,top-bottom,endZ-chunk.Z));
    }
    bool Available(Chunk chunk,int x,int z,byte state) => !merged[(z-chunk.Z)*Size+x-chunk.X] && states[x,z] == state;
    void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
    {
        int start=vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        triangles.Add(start); triangles.Add(start+1); triangles.Add(start+2);
        triangles.Add(start); triangles.Add(start+2); triangles.Add(start+3);
    }
    void OnDestroy()
    {
        foreach (var chunk in chunks) foreach (var layer in chunk.Layers)
            if (layer.Mesh != null) { if (Application.isPlaying) Destroy(layer.Mesh); else DestroyImmediate(layer.Mesh); }
    }
}
