using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Two range colours in one mesh; no per-tile objects, colliders or update loop.</summary>
public sealed class SkillTargetRangeView : MonoBehaviour
{
    Mesh mesh;
    Material material;
    MeshRenderer rangeRenderer;
    readonly List<Vector3> vertices = new List<Vector3>(512);
    readonly List<Color> colors = new List<Color>(512);
    readonly List<int> indices = new List<int>(768);
    public void Show(IReadOnlyList<Vector3> range, IReadOnlyList<Vector3> effect)
    {
        if (mesh == null)
        {
            var shader = Resources.Load<Shader>("Shaders/InspectionRange");
            if (shader == null) return;
            mesh = new Mesh { name = "Skill target and effect ranges", indexFormat = IndexFormat.UInt32 }; mesh.MarkDynamic();
            material = new Material(shader);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            rangeRenderer = gameObject.AddComponent<MeshRenderer>(); rangeRenderer.sharedMaterial = material;
            rangeRenderer.shadowCastingMode = ShadowCastingMode.Off; rangeRenderer.receiveShadows = false;
            rangeRenderer.lightProbeUsage = LightProbeUsage.Off; rangeRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }
        vertices.Clear(); colors.Clear(); indices.Clear();
        foreach (var cell in range) Quad(cell, .44f, new Color(.65f, .3f, 1, .32f));
        foreach (var cell in effect) Quad(cell + Vector3.up * .012f, .39f, new Color(1, .62f, .18f, .62f));
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0); mesh.RecalculateBounds();
        rangeRenderer.enabled = indices.Count > 0;
    }
    void Quad(Vector3 cell, float half, Color color)
    {
        var p = cell - Vector3.up * (GameConstants.MovePointYOffset - .025f);
        int start = vertices.Count;
        vertices.Add(p + new Vector3(-half,0,-half)); vertices.Add(p + new Vector3(-half,0,half));
        vertices.Add(p + new Vector3(half,0,half)); vertices.Add(p + new Vector3(half,0,-half));
        for (int i = 0; i < 4; i++) colors.Add(color);
        indices.Add(start); indices.Add(start+1); indices.Add(start+2); indices.Add(start); indices.Add(start+2); indices.Add(start+3);
    }
    public void Hide() { if (rangeRenderer != null) rangeRenderer.enabled = false; }
    void OnDestroy() { if (mesh != null) Destroy(mesh); if (material != null) Destroy(material); }
}
