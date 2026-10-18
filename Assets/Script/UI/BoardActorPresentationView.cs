using System.Collections.Generic;
using UnityEngine;

/// <summary>Cached references only; no Update, Animator or gameplay state.</summary>
[AddComponentMenu("")]
[DisallowMultipleComponent]
public sealed class BoardActorPresentationView : MonoBehaviour
{
    sealed class Original
    { public MeshFilter Filter; public MeshRenderer Renderer; public bool ForcedOff; }
    readonly List<Original> originals = new List<Original>();
    public GameObject SourceRoot { get; private set; }
    public Transform VisualRoot { get; private set; }
    public WildBossArchetype? StrongEnemyArchetype { get; internal set; }
    GameObject templateUsed;
    Material materialUsed;
    Status owner;
    public bool HasOriginalMesh(Mesh mesh)
    { foreach (var original in originals) if (original.Filter != null && original.Filter.sharedMesh == mesh) return true; return false; }
    public void Configure(Status actor, GameObject sourceRoot, GameObject template, Material material)
    {
        owner = actor;
        if (SourceRoot == null)
        {
            SourceRoot = sourceRoot;
            foreach (var filter in SourceRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null && filter.sharedMesh != null && filter.GetComponentInParent<Canvas>() == null)
                    originals.Add(new Original { Filter = filter, Renderer = renderer, ForcedOff = renderer.forceRenderingOff });
            }
        }
        if (VisualRoot != null && templateUsed == template && materialUsed == material)
        {
            VisualRoot.gameObject.SetActive(true); HideOriginals(); BoardActorAnimation.Bind(actor, VisualRoot); return;
        }
        BoardActorAnimation.Unbind(actor);
        if (VisualRoot != null) { VisualRoot.gameObject.SetActive(false); Destroy(VisualRoot.gameObject); }
        var visual = new GameObject("ActorVisual");
        VisualRoot = visual.transform; VisualRoot.SetParent(actor.transform, false);
        VisualRoot.localRotation = actor.direction == Direction.S ? Quaternion.Euler(0, 180, 0) : Quaternion.identity;
        if (template != null)
        {
            foreach (var filter in template.GetComponentsInChildren<MeshFilter>(true))
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                if (renderer != null && filter.sharedMesh != null)
                    AddPart(filter, renderer, template.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix, material,
                        actor.type == Type.Unit && actor.team == Team.Player && !actor.isWildBoss);
            }
        }
        else
        {
            foreach (var original in originals)
                if (original.Filter != null && original.Renderer != null)
                    AddPart(original.Filter, original.Renderer, actor.transform.worldToLocalMatrix * original.Filter.transform.localToWorldMatrix, material, false);
        }
        templateUsed = template; materialUsed = material;
        HideOriginals(); BoardActorAnimation.Bind(actor, VisualRoot);
    }
    void AddPart(MeshFilter source, MeshRenderer original, Matrix4x4 relative, Material material, bool turnFront)
    {
        var part = new GameObject(source.name + " Visual"); part.layer = gameObject.layer;
        var t = part.transform; t.SetParent(VisualRoot, false);
        t.localPosition = relative.GetColumn(3); t.localRotation = relative.rotation; t.localScale = relative.lossyScale;
        // R1 chess model faces -Z; board-facing North is +Z.
        if (turnFront) t.localRotation = Quaternion.Euler(0, 180, 0) * t.localRotation;
        part.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
        var renderer = part.AddComponent<MeshRenderer>();
        int count = Mathf.Max(1, source.sharedMesh.subMeshCount); var materials = new Material[count];
        for (int i = 0; i < count; i++) materials[i] = material;
        renderer.sharedMaterials = materials; renderer.shadowCastingMode = original.shadowCastingMode;
        renderer.receiveShadows = original.receiveShadows; renderer.lightProbeUsage = original.lightProbeUsage;
        renderer.reflectionProbeUsage = original.reflectionProbeUsage;
        renderer.enabled = owner.team == Team.Player || owner.RuntimeSystems?.VisionGenerator?.IsPresentationCellVisible(owner.transform.position) == true;
    }
    void HideOriginals()
    {
        // Fog owns Renderer.enabled. forceRenderingOff keeps source meshes hidden even after a visibility refresh.
        foreach (var original in originals) if (original.Renderer != null) original.Renderer.forceRenderingOff = true;
    }
    public void RestoreOriginal()
    {
        if (owner != null) BoardActorAnimation.Unbind(owner);
        if (VisualRoot != null) VisualRoot.gameObject.SetActive(false);
        foreach (var original in originals) if (original.Renderer != null) original.Renderer.forceRenderingOff = original.ForcedOff;
    }
}
