using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns one reference to an immutable, colour-keyed Standard material for generated primitives.
/// Apply colours through this class; callers must not mutate or destroy sharedMaterial.
/// Disabling releases its reference; enabling reacquires it, including after a script reload.
/// </summary>
[DisallowMultipleComponent]
public sealed class PrimitiveMaterialBinding : MonoBehaviour
{
    sealed class Entry { public Material Material; public int References; }
    static readonly Dictionary<Color, Entry> palette = new Dictionary<Color, Entry>();
    [SerializeField] Color color;
    [SerializeField] bool assigned;
    Entry entry;
    Renderer target;

    public static void Apply(Renderer renderer, Color value)
    {
        if (renderer == null) return;
        var binding = renderer.GetComponent<PrimitiveMaterialBinding>();
        if (binding == null) binding = renderer.gameObject.AddComponent<PrimitiveMaterialBinding>();
        if (binding.entry != null && binding.color == value) return;
        binding.Release();
        binding.target = renderer; binding.color = value; binding.assigned = true;
        if (binding.isActiveAndEnabled) binding.Acquire();
    }

    void OnEnable() { if (assigned && entry == null) Acquire(); }
    void OnDisable() { Release(); }
    void Acquire()
    {
        if (target == null) target = GetComponent<Renderer>();
        if (target == null) return;
        if (!palette.TryGetValue(color, out entry) || entry.Material == null)
        {
            var shader = Shader.Find("Standard");
            if (shader == null) { Debug.LogError("Standard shader is required for generated primitives.", this); return; }
            entry = new Entry { Material = new Material(shader) { color = color, enableInstancing = true, name = "Generated primitive " + color } };
            palette[color] = entry;
        }
        entry.References++;
        target.sharedMaterial = entry.Material;
    }
    void OnDestroy() { Release(); }
    void Release()
    {
        if (entry == null) return;
        if (--entry.References == 0)
        {
            if (palette.TryGetValue(color, out var current) && ReferenceEquals(current, entry)) palette.Remove(color);
            if (entry.Material != null)
            {
                if (Application.isPlaying) Destroy(entry.Material); else DestroyImmediate(entry.Material);
            }
        }
        entry = null;
    }
}
