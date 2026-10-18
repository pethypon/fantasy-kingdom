using UnityEngine;

/// <summary>
/// Bounded, cosmetic board feedback. The caller verifies event visibility before Emit;
/// active slots also disappear immediately when their cells leave the presentation mask.
/// Shared meshes/materials are scene owned. Idle scenes have no enabled frame callback.
/// </summary>
public static class BoardCombatEffects
{
    const int MaximumSlots = 16;
    static readonly int ColorId = Shader.PropertyToID("_Color");
    sealed class Slot
    {
        public GameObject Object;
        public MeshFilter Filter;
        public MeshRenderer Renderer;
        public readonly MaterialPropertyBlock Properties = new MaterialPropertyBlock();
        public BoardFeedbackKind Kind;
        public Vector3 Position, Direction;
        public Color Color;
        public BoardFeedbackSettings Settings;
        public double Started;
        public float Duration, Scale;
        public bool Active, HasEndCell;
        public Vector3 EndCell;
    }

    static readonly Slot[] slots = new Slot[MaximumSlots];
    static BoardCombatEffectsDriver driver;
    static VisionGenerator vision;
    static Mesh ring, arc, beam, star, rune;
    static Material fallbackMaterial;
    static int activeCount, poolCount, cursor;
    public static int ActiveCount => activeCount;
    public static int PoolCount => poolCount;
    public static int MeshCount => (ring != null ? 1 : 0) + (arc != null ? 1 : 0)
        + (beam != null ? 1 : 0) + (star != null ? 1 : 0) + (rune != null ? 1 : 0);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        if (driver != null)
        {
            var oldOwner = driver;
            DriverDestroyed(oldOwner);
            Object.Destroy(oldOwner.gameObject);
        }
        for (int i = 0; i < slots.Length; i++) slots[i] = null;
        driver = null; vision = null;
        ring = arc = beam = star = rune = null; fallbackMaterial = null;
        activeCount = poolCount = cursor = 0;
    }

    public static void Emit(BoardFeedbackKind kind, Vector3 position, Vector3 direction,
        Color color, BoardFeedbackSettings settings)
    {
        if (!Application.isPlaying || settings == null || !settings.effectsEnabled
            || kind == BoardFeedbackKind.UiClick || !Finite(position) || !Finite(direction)) return;
        int limit = Mathf.Clamp(settings.maxVisualEffects, 0, MaximumSlots);
        float scale = float.IsNaN(settings.effectScale) || float.IsInfinity(settings.effectScale)
            ? 1 : Mathf.Clamp(settings.effectScale, .1f, 3);
        if (limit == 0) return;
        Material material = settings.effectMaterial;
        if (material == null)
        {
            if (fallbackMaterial == null)
            {
                var shader = Shader.Find("Fantasy Kingdom/BoardFeedback");
                if (shader == null) return;
                fallbackMaterial = new Material(shader) { name = "Board feedback (shared runtime)", hideFlags = HideFlags.HideAndDontSave };
            }
            material = fallbackMaterial;
        }
        EnsureDriverAndMeshes();
        // A lowered budget takes effect immediately; inactive slots remain bounded
        // and reusable instead of repeatedly allocating/destroying native objects.
        for (int i = limit; i < poolCount; i++) Hide(slots[i]);
        int selected = -1;
        for (int i = 0; i < limit; i++)
        {
            int candidate = (cursor + i) % limit;
            if (slots[candidate] == null || !slots[candidate].Active) { selected = candidate; break; }
        }
        if (selected < 0) selected = cursor % limit;
        cursor = (selected + 1) % limit;
        var slot = slots[selected] ?? (slots[selected] = CreateSlot(selected));
        if (!slot.Active) activeCount++;
        slot.Active = true; slot.Kind = kind; slot.Position = position; slot.Direction = direction;
        slot.Color = color; slot.Settings = settings;
        slot.Started = Time.unscaledTimeAsDouble; slot.Duration = Duration(kind); slot.Scale = scale;
        slot.HasEndCell = kind == BoardFeedbackKind.Projectile;
        slot.EndCell = position + direction;
        slot.Filter.sharedMesh = MeshFor(kind);
        slot.Renderer.sharedMaterial = material;
        slot.Object.SetActive(true);
        ApplyPose(slot, 0);
        driver.enabled = true;
    }

    static Slot CreateSlot(int index)
    {
        var go = new GameObject("Board feedback " + index, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.SetParent(driver.transform, false);
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        poolCount = Mathf.Max(poolCount, index + 1);
        return new Slot { Object = go, Filter = go.GetComponent<MeshFilter>(), Renderer = renderer };
    }

    static void EnsureDriverAndMeshes()
    {
        if (driver != null) return;
        driver = new GameObject("Board combat effects (shared)").AddComponent<BoardCombatEffectsDriver>();
        driver.enabled = false;
        vision = Object.FindFirstObjectByType<VisionGenerator>();
        ring = CreateBand("Board feedback ring", .88f, 1, 0, 360, 32);
        arc = CreateBand("Board feedback sword arc", .72f, 1, -68, 136, 18);
        beam = CreateBeam();
        star = CreateStar();
        rune = CreateRune();
    }

    internal static void Tick(double now)
    {
        for (int i = 0; i < poolCount; i++)
        {
            var slot = slots[i];
            if (slot == null || !slot.Active) continue;
            float t = Mathf.Clamp01((float)((now - slot.Started) / slot.Duration));
            if (t >= 1 || slot.Object == null || slot.Settings == null || !slot.Settings.effectsEnabled
                || i >= Mathf.Clamp(slot.Settings.maxVisualEffects, 0, MaximumSlots)
                || !StillVisible(slot))
            { Hide(slot); continue; }
            ApplyPose(slot, t);
        }
        if (activeCount == 0 && driver != null) driver.enabled = false;
    }

    static bool StillVisible(Slot slot)
    {
        if (vision == null) return true; // Emit's caller has already verified visibility.
        if (!vision.IsPresentationCellVisible(slot.Position)) return false;
        if (!slot.HasEndCell) return true;
        for (int i = 1; i <= 8; i++)
            if (!vision.IsPresentationCellVisible(Vector3.Lerp(slot.Position, slot.EndCell, i / 8f))) return false;
        return true;
    }

    static void ApplyPose(Slot slot, float t)
    {
        float ease = 1 - (1 - t) * (1 - t);
        Vector3 forward = slot.Direction; forward.y = 0;
        if (forward.sqrMagnitude < .0001f) forward = Vector3.forward;
        forward.Normalize();
        Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up);
        Vector3 position = slot.Position + Vector3.up * .075f;
        float size = Mathf.Lerp(.36f, .67f, ease) * slot.Scale;
        Vector3 dimensions = new Vector3(size, 1, size);
        switch (slot.Kind)
        {
            case BoardFeedbackKind.Move:
                size = Mathf.Lerp(.30f, .48f, ease) * slot.Scale;
                dimensions = new Vector3(size, 1, size); break;
            case BoardFeedbackKind.Attack:
                position += Vector3.up * .32f + forward * .26f;
                rotation *= Quaternion.Euler(0, Mathf.Lerp(-32, 32, ease), 0);
                size = Mathf.Lerp(.32f, .58f, ease) * slot.Scale;
                dimensions = new Vector3(size, 1, size); break;
            case BoardFeedbackKind.Projectile:
                position += Vector3.up * .42f;
                float length = Mathf.Clamp(slot.Direction.magnitude, .25f, 8);
                dimensions = new Vector3(slot.Scale, 1, length);
                break;
            case BoardFeedbackKind.Hit:
                position += Vector3.up * .42f;
                rotation *= Quaternion.Euler(0, 23, 0);
                size = Mathf.Lerp(.16f, .38f, ease) * slot.Scale;
                dimensions = new Vector3(size, 1, size); break;
            case BoardFeedbackKind.Heal:
                position += Vector3.up * (.08f + .25f * ease); break;
            case BoardFeedbackKind.Shield:
                position += Vector3.up * .28f; break;
            case BoardFeedbackKind.Build:
                rotation = Quaternion.identity;
                size = Mathf.Lerp(.46f, .62f, ease) * slot.Scale;
                dimensions = new Vector3(size, 1, size); break;
            case BoardFeedbackKind.Summon:
                rotation = Quaternion.Euler(0, ease * 28, 0);
                position += Vector3.up * (.03f + ease * .12f); break;
            case BoardFeedbackKind.Magic:
            case BoardFeedbackKind.Skill:
                rotation *= Quaternion.Euler(0, ease * 24, 0);
                position += Vector3.up * .12f; break;
            case BoardFeedbackKind.Victory:
            case BoardFeedbackKind.Defeat:
                dimensions *= 1.35f; break;
        }
        slot.Object.transform.SetPositionAndRotation(position, rotation);
        slot.Object.transform.localScale = dimensions;
        Color color = slot.Color;
        float attack = Mathf.Clamp01(t * 10 + .35f);
        color.a *= attack * (1 - t) * .82f;
        slot.Properties.SetColor(ColorId, color);
        slot.Renderer.SetPropertyBlock(slot.Properties);
    }

    static float Duration(BoardFeedbackKind kind)
    {
        switch (kind)
        {
            case BoardFeedbackKind.Move: return .18f;
            case BoardFeedbackKind.Attack: return .22f;
            case BoardFeedbackKind.Projectile: return .17f;
            case BoardFeedbackKind.Hit: return .18f;
            case BoardFeedbackKind.Heal: return .4f;
            case BoardFeedbackKind.Summon: return .45f;
            case BoardFeedbackKind.Build: return .38f;
            default: return .32f;
        }
    }
    static Mesh MeshFor(BoardFeedbackKind kind)
    {
        switch (kind)
        {
            case BoardFeedbackKind.Attack: return arc;
            case BoardFeedbackKind.Projectile: return beam;
            case BoardFeedbackKind.Hit: return star;
            case BoardFeedbackKind.Move:
            case BoardFeedbackKind.Shield: return ring;
            default: return rune;
        }
    }
    static void Hide(Slot slot)
    {
        if (slot == null || !slot.Active) return;
        slot.Active = false; activeCount = Mathf.Max(0, activeCount - 1);
        if (slot.Object != null) slot.Object.SetActive(false);
    }
    internal static void DriverDestroyed(BoardCombatEffectsDriver owner)
    {
        if (driver != owner) return;
        driver = null; vision = null;
        for (int i = 0; i < slots.Length; i++) slots[i] = null;
        activeCount = poolCount = cursor = 0;
        Release(ring); Release(arc); Release(beam); Release(star); Release(rune); Release(fallbackMaterial);
        ring = arc = beam = star = rune = null; fallbackMaterial = null;
    }
    static void Release(Object owned)
    { if (owned != null) { if (Application.isPlaying) Object.Destroy(owned); else Object.DestroyImmediate(owned); } }
    static bool Finite(Vector3 value) => !float.IsNaN(value.sqrMagnitude) && !float.IsInfinity(value.sqrMagnitude);

    static Mesh CreateBand(string name, float inner, float outer, float startDegrees, float sweepDegrees, int segments)
    {
        var vertices = new Vector3[(segments + 1) * 2];
        var triangles = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = (startDegrees + sweepDegrees * i / segments) * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            vertices[i * 2] = direction * inner; vertices[i * 2 + 1] = direction * outer;
            if (i == segments) continue;
            int v = i * 2, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        return Mesh(name, vertices, triangles);
    }
    static Mesh CreateBeam() => Mesh("Board feedback projectile",
        new[] { new Vector3(-.025f, 0, 0), new Vector3(.025f, 0, 0), new Vector3(-.012f, 0, .9f),
            new Vector3(.012f, 0, .9f), new Vector3(-.07f, 0, .86f), new Vector3(.07f, 0, .86f), new Vector3(0, 0, 1) },
        new[] { 0, 2, 1, 1, 2, 3, 4, 6, 5 });
    static Mesh CreateStar()
    {
        const int points = 16;
        var vertices = new Vector3[points + 1]; var triangles = new int[points * 3];
        for (int i = 0; i < points; i++)
        {
            float a = i * Mathf.PI * 2 / points, radius = i % 2 == 0 ? 1 : .25f;
            vertices[i + 1] = new Vector3(Mathf.Sin(a) * radius, 0, Mathf.Cos(a) * radius);
            triangles[i * 3] = 0; triangles[i * 3 + 1] = i + 1; triangles[i * 3 + 2] = (i + 1) % points + 1;
        }
        return Mesh("Board feedback impact", vertices, triangles);
    }
    static Mesh CreateRune()
    {
        // Ring with four short, detached cardinal strokes; intentionally no filled
        // disk so the board and the piece remain readable through the effect.
        const int segments = 24;
        var vertices = new Vector3[(segments + 1) * 2 + 16];
        var triangles = new int[segments * 6 + 24];
        for (int i = 0; i <= segments; i++)
        {
            float a = i * Mathf.PI * 2 / segments;
            Vector3 p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            vertices[i * 2] = p * .7f; vertices[i * 2 + 1] = p * .76f;
            if (i == segments) continue;
            int v = i * 2, t = i * 6;
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        int offset = (segments + 1) * 2;
        for (int i = 0; i < 4; i++)
        {
            Quaternion rotation = Quaternion.Euler(0, i * 90, 0); int v = offset + i * 4, t = segments * 6 + i * 6;
            vertices[v] = rotation * new Vector3(-.035f, 0, .82f); vertices[v + 1] = rotation * new Vector3(.035f, 0, .82f);
            vertices[v + 2] = rotation * new Vector3(-.035f, 0, 1); vertices[v + 3] = rotation * new Vector3(.035f, 0, 1);
            triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
            triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
        }
        return Mesh("Board feedback rune", vertices, triangles);
    }
    static Mesh Mesh(string name, Vector3[] vertices, int[] triangles)
    {
        var result = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
        result.vertices = vertices; result.triangles = triangles;
        result.RecalculateNormals(); result.RecalculateBounds();
        return result;
    }
}
