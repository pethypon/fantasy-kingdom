using System.Collections.Generic;
using UnityEngine;

/// <summary>One-time visual binding. Root transforms, hitboxes, gameplay definitions and save data are untouched.</summary>
public static class BoardActorPresentation
{
    static readonly List<Status> actors = new List<Status>(128);
    public static void ApplyAll()
    {
        CombatRegistry.Collect(actors);
        foreach (var actor in actors) Apply(actor);
        actors.Clear();
    }
    public static bool Apply(Status actor, GameObject spawnRoot = null)
        => ApplyCore(actor, spawnRoot, null);

    /// <summary>Owned procedural boss followers share their leader's red art without acquiring boss gameplay rules.</summary>
    public static bool ApplyStrongEnemyFollower(Status actor, WildBossArchetype archetype, GameObject spawnRoot = null)
        => ApplyCore(actor, spawnRoot, archetype);

    static bool ApplyCore(Status actor, GameObject spawnRoot, WildBossArchetype? followerArchetype)
    {
        if (!Application.isPlaying || actor == null || actor.type != Type.Unit && actor.type != Type.Building && actor.type != Type.Wall) return false;
        var view = actor.GetComponent<BoardActorPresentationView>();
        // Crystals keep the material and model authored on their original prefab,
        // including after balance refresh, loading and repeated presentation setup.
        if (actor.kind == Kind.Crystal || (actor.type == Type.Building || actor.type == Type.Wall) && actor.facilityKind == FacilityKind.SubCrystal)
        { if (view != null) view.RestoreOriginal(); return false; }
        var catalog = BoardVisualThemeCatalog.Active;
        if (catalog == null || !catalog.enabledForPlay) { if (view != null) view.RestoreOriginal(); return false; }
        var root = spawnRoot != null ? spawnRoot : view != null && view.SourceRoot != null ? view.SourceRoot : actor.gameObject;
        var strongArchetype = actor.isWildBoss ? actor.wildBossArchetype : followerArchetype ?? view?.StrongEnemyArchetype;
        string definition = actor.type == Type.Unit ? actor.unitDefinitionId : actor.authoredFacilityId;
        if (!strongArchetype.HasValue && !catalog.ManagesDefinition(definition)) { if (view != null) view.RestoreOriginal(); return false; }
        if (view == null)
        {
            bool managed = strongArchetype.HasValue;
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
                if (catalog.ManagesMesh(filter.sharedMesh)) { managed = true; break; }
            if (!managed) return false;
        }
        bool player = actor.team == Team.Player;
        GameObject template = null;
        Material material, plain;
        if (strongArchetype.HasValue)
        {
            template = catalog.Boss(strongArchetype.Value)?.visual;
            material = catalog.bossMaterial; plain = catalog.bossPlainMaterial;
        }
        else if (actor.type == Type.Building || actor.type == Type.Wall)
        {
            var style = catalog.Building(actor.facilityKind);
            template = player ? style?.playerVisual : style?.enemyVisual;
            material = player ? catalog.playerBuildingMaterial : catalog.enemyBuildingMaterial;
            plain = player ? catalog.playerPlainMaterial : catalog.enemyPlainMaterial;
        }
        else
        {
            var style = catalog.Unit(actor.kind);
            template = player ? style?.playerVisual : style?.enemyVisual;
            material = player ? catalog.playerUnitMaterial : catalog.enemyUnitMaterial;
            plain = player ? catalog.playerPlainMaterial : catalog.enemyPlainMaterial;
            // Completed initial player models keep their geometry; the stock summoned models use the palette mesh.
            if (player && template != null && !SourceContainsMesh(root, view, template.GetComponentInChildren<MeshFilter>(true)?.sharedMesh))
                template = null;
        }
        if (material == null || plain == null) return false;
        if (view == null) view = actor.gameObject.AddComponent<BoardActorPresentationView>();
        if (followerArchetype.HasValue) view.StrongEnemyArchetype = followerArchetype;
        view.Configure(actor, root, template, template != null ? material : plain);
        return true;
    }
    static bool SourceContainsMesh(GameObject root, BoardActorPresentationView view, Mesh mesh)
    {
        if (mesh == null) return false;
        if (view != null) return view.HasOriginalMesh(mesh);
        foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true)) if (filter.sharedMesh == mesh) return true;
        return false;
    }
    public static Transform VisualRoot(Status actor) => actor != null ? actor.GetComponent<BoardActorPresentationView>()?.VisualRoot : null;
    public static void Reactivate(Status actor)
    {
        if (actor == null) return;
        var view = actor.GetComponent<BoardActorPresentationView>();
        if (view != null && view.VisualRoot != null && view.VisualRoot.gameObject.activeSelf) BoardActorAnimation.Bind(actor, view.VisualRoot);
    }
}

