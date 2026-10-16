using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>One thin line per observed live stake pair; hidden endpoints never reveal their positions.</summary>
public sealed class SpecialSkillWorldView : MonoBehaviour
{
    sealed class View
    {
        public GameObject Root;
        public LineRenderer Line;
        public TextMeshPro A, B;
    }
    readonly Dictionary<Status, View> views = new Dictionary<Status, View>();
    readonly List<Status> actors = new List<Status>();
    readonly List<Status> stale = new List<Status>();
    readonly HashSet<Status> active = new HashSet<Status>();
    Material lineMaterial;
    VisionGenerator vision;
    float nextRefresh;

    void OnDestroy()
    {
        foreach (var entry in views) if (entry.Value.Root != null) Destroy(entry.Value.Root);
        if (lineMaterial != null) Destroy(lineMaterial);
    }

    void LateUpdate()
    {
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .1f;
        if (vision == null) vision = Object.FindFirstObjectByType<VisionGenerator>();
        CombatRegistry.Collect(actors); active.Clear();
        var camera = Camera.main;
        foreach (var caster in actors)
        {
            var definition = SpecialSkillRuntime.Definition(caster);
            var state = caster.Special;
            if (definition == null || !definition.enabledForPlay || definition.ability != UnitSpecialSkill.CrossbowStakes
                || state == null || state.ownerTurn - state.stakeAAppliedTurn >= SpecialSkillDefinition.StakeLifetime
                || state.ownerTurn - state.stakeBAppliedTurn >= SpecialSkillDefinition.StakeLifetime
                || !CanObserve(caster) || !SpecialSkillPresentation.TryFind(state.stakeAId, out var a)
                || !SpecialSkillPresentation.TryFind(state.stakeBId, out var b) || !CanObserve(a) || !CanObserve(b)) continue;
            active.Add(caster);
            if (!views.TryGetValue(caster, out var view))
            { view = CreateView(); views.Add(caster, view); }
            if (view.Line == null) continue;
            Vector3 first = a.transform.position + Vector3.up * .18f;
            Vector3 second = b.transform.position + Vector3.up * .18f;
            view.Line.SetPosition(0, first); view.Line.SetPosition(1, second);
            view.A.transform.position = first + Vector3.up * .45f;
            view.B.transform.position = second + Vector3.up * .45f;
            if (camera != null)
            { view.A.transform.rotation = camera.transform.rotation; view.B.transform.rotation = camera.transform.rotation; }
        }
        stale.Clear();
        foreach (var entry in views) if (!active.Contains(entry.Key)) stale.Add(entry.Key);
        foreach (var actor in stale)
        { var view = views[actor]; if (view.Root != null) Destroy(view.Root); views.Remove(actor); }
    }

    bool CanObserve(Status actor) => actor != null && actor.IsAlive && (actor.team == Team.Player
        || vision != null && UnitSelectionPicker.IsVisibleActor(actor, vision));

    View CreateView()
    {
        if (lineMaterial == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            if (shader != null)
            {
                lineMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
                if (lineMaterial.HasProperty("_BaseColor")) lineMaterial.SetColor("_BaseColor", new Color(.45f, .9f, 1));
                if (lineMaterial.HasProperty("_Color")) lineMaterial.SetColor("_Color", new Color(.45f, .9f, 1));
            }
        }
        var root = new GameObject("Observed lightning stakes");
        var line = root.AddComponent<LineRenderer>();
        line.sharedMaterial = lineMaterial; line.positionCount = 2; line.useWorldSpace = true;
        line.startWidth = line.endWidth = .045f; line.numCapVertices = 2;
        line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
        line.startColor = line.endColor = new Color(.45f, .9f, 1);
        return new View { Root = root, Line = line, A = StakeLabel(root.transform, "A"), B = StakeLabel(root.transform, "B") };
    }
    static TextMeshPro StakeLabel(Transform parent, string text)
    {
        var root = new GameObject("Stake " + text); root.transform.SetParent(parent, false);
        var label = root.AddComponent<TextMeshPro>(); label.text = text;
        label.font = UIFactory.LoadDefaultFont(); label.fontSize = 3; label.color = new Color(.7f, .95f, 1);
        label.alignment = TextAlignmentOptions.Center; label.rectTransform.sizeDelta = new Vector2(1, .6f);
        label.renderer.shadowCastingMode = ShadowCastingMode.Off; label.renderer.receiveShadows = false;
        return label;
    }
}
