#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class BoardThemePresentationTests
{
    static int checks;
    static void Check(bool ok, string message) { checks++; if (!ok) throw new InvalidOperationException("[BoardThemeLive] " + message); }
    public static void PlayMode(GameSystems systems)
    {
        checks = 0;
        var catalog = BoardVisualThemeCatalog.Active;
        Check(catalog != null && catalog.enabledForPlay, "production catalog is active");
        int apPlayer = systems.FactionState.GetAP(Team.Player), apEnemy = systems.FactionState.GetAP(Team.Enemy);
        var random = UnityEngine.Random.state;
        var objects = new List<GameObject>();
        GameObject sampleObject = null;
        int boundBefore = BoardActorAnimation.BoundCount;
        try
        {
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                var crystalSource = new SerializedObject(systems.CrystalSystem).FindProperty(team == Team.Player ? "PlayerCrystal" : "EnemyCrystal").objectReferenceValue as GameObject;
                var crystal = UnityEngine.Object.Instantiate(crystalSource); objects.Add(crystal);
                var renderers = crystal.GetComponentsInChildren<Renderer>(true);
                var originalMaterials = new List<Material[]>();
                foreach (var renderer in renderers) originalMaterials.Add(renderer.sharedMaterials);
                CrystalSystem.ConfigureCrystal(crystal, team);
                var actor = crystal.GetComponentInChildren<Status>();
                Check(!BoardActorPresentation.Apply(actor, crystal) && BoardActorPresentation.VisualRoot(actor) == null,
                    "main crystal is excluded from faction material replacement " + team);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var actual = renderers[i].sharedMaterials; var expected = originalMaterials[i];
                    Check(actual.Length == expected.Length, "original crystal material slots retained " + team);
                    for (int j = 0; j < actual.Length; j++) Check(actual[j] == expected[j], "original crystal shared material retained " + team);
                }
            }
            var subSource = AssetDatabase.LoadAssetAtPath<GameObject>(BuildingVisualGenerator.PrefabPath(FacilityKind.SubCrystal));
            var sub = UnityEngine.Object.Instantiate(subSource); objects.Add(sub);
            var subMaterial = sub.GetComponent<MeshRenderer>().sharedMaterial;
            var subMesh = sub.GetComponent<MeshFilter>().sharedMesh;
            var subActor = sub.AddComponent<Status>(); subActor.type = Type.Building; subActor.facilityKind = FacilityKind.SubCrystal;
            subActor.HP = subActor.MaxHP = 100;
            foreach (var team in new[] { Team.Player, Team.Enemy })
            {
                subActor.team = team;
                Check(!BoardActorPresentation.Apply(subActor, sub) && BoardActorPresentation.VisualRoot(subActor) == null
                    && sub.GetComponent<MeshRenderer>().sharedMaterial == subMaterial && sub.GetComponent<MeshFilter>().sharedMesh == subMesh,
                    "sub crystal keeps its original model and material " + team);
            }
            foreach (var team in new[] { Team.Player, Team.Enemy })
            foreach (var kind in UnitRoster.SummonableKinds)
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(R1UnitVisualInstaller.PrefabPath(kind));
                var data = systems.UnitSetting.UnitDataMap[kind];
                var parent = team == Team.Player ? systems.UnitSetting.PlayerUnit : systems.UnitSetting.EnemyUnit;
                var position = new Vector3(72000 + objects.Count, 1, 72000);
                var obj = systems.UnitSetting.SpawnUnit(source, position, parent, initialKind: kind, initialTeam: team, authoredData: data, runtimeSpawn: true);
                objects.Add(obj); var actor = obj.GetComponentInChildren<Status>();
                if (team == Team.Player && kind == Kind.Knight) sampleObject = obj;
                var root = BoardActorPresentation.VisualRoot(actor);
                Check(root != null && root.IsChildOf(actor.transform) && root != actor.transform, "dedicated visual child " + team + "/" + kind);
                var renderer = root.GetComponentInChildren<MeshRenderer>(); var mesh = root.GetComponentInChildren<MeshFilter>().sharedMesh;
                var style = catalog.Unit(kind); var expected = team == Team.Player ? style.playerVisual : style.enemyVisual;
                Check(mesh == expected.GetComponent<MeshFilter>().sharedMesh, "actual spawn selects faction geometry " + team + "/" + kind);
                Check(renderer.sharedMaterial == (team == Team.Player ? catalog.playerUnitMaterial : catalog.enemyUnitMaterial), "shared faction material " + team + "/" + kind);
                Check(actor.HP == data.baseHP && actor.MaxHP == data.baseHP && actor.ATK == data.baseATK && actor.DEF == data.baseDEF
                    && actor.GrowthData == data && actor.kind == kind && actor.team == team, "model preserves authored combat identity " + team + "/" + kind);
                Check(obj.transform.position == position && obj.GetComponentsInChildren<Collider>(true).Length == source.GetComponentsInChildren<Collider>(true).Length,
                    "logical position and click colliders retained " + team + "/" + kind);
                Check(obj.GetComponent<MeshRenderer>().forceRenderingOff, "source renderer cannot reappear after fog update " + team + "/" + kind);
                if (team == Team.Enemy) Check(!renderer.enabled, "unobserved enemy is hidden on creation " + kind);
                int children = obj.GetComponentsInChildren<Transform>(true).Length;
                for (int i = 0; i < 15; i++) BoardActorPresentation.Apply(actor);
                Check(obj.GetComponentsInChildren<Transform>(true).Length == children && BoardActorPresentation.VisualRoot(actor) == root,
                    "reapplying style reuses visual binding " + team + "/" + kind);
            }
            var bossSource = AssetDatabase.LoadAssetAtPath<GameObject>(R1UnitVisualInstaller.PrefabPath(Kind.Knight));
            foreach (var style in catalog.bosses)
            {
                var obj = UnityEngine.Object.Instantiate(bossSource); objects.Add(obj);
                var actor = obj.GetComponentInChildren<Status>(); actor.isWildBoss = true; actor.team = Team.Obstacle; actor.wildBossArchetype = style.kind;
                Check(BoardActorPresentation.Apply(actor), "strong enemy binding " + style.kind);
                var root = BoardActorPresentation.VisualRoot(actor);
                Check(root.GetComponentInChildren<MeshFilter>().sharedMesh == style.visual.GetComponent<MeshFilter>().sharedMesh
                    && root.GetComponentInChildren<MeshRenderer>().sharedMaterial == catalog.bossMaterial, "red dedicated strong enemy model " + style.kind);
            }
            foreach (var archetype in new[] { WildBossArchetype.GhostKing, WildBossArchetype.RebelKnight })
            {
                var obj = GameObject.CreatePrimitive(PrimitiveType.Capsule); objects.Add(obj);
                var actor = obj.AddComponent<Status>(); actor.team = Team.Obstacle; actor.type = Type.Unit;
                actor.kind = archetype == WildBossArchetype.GhostKing ? Kind.King : Kind.Knight;
                actor.HP = actor.MaxHP = 40;
                Check(BoardActorPresentation.ApplyStrongEnemyFollower(actor, archetype, obj), "owned boss follower receives art " + archetype);
                var visual = BoardActorPresentation.VisualRoot(actor);
                Check(visual.GetComponentInChildren<MeshFilter>().sharedMesh == catalog.Boss(archetype).visual.GetComponent<MeshFilter>().sharedMesh
                    && visual.GetComponentInChildren<MeshRenderer>().sharedMaterial == catalog.bossMaterial,
                    "boss follower shares existing red material and leader geometry " + archetype);
                for (int i = 0; i < 5; i++) BoardActorPresentation.Apply(actor);
                Check(BoardActorPresentation.VisualRoot(actor) == visual && !actor.isWildBoss && actor.HP == 40,
                    "refresh retains follower art without acquiring boss gameplay flags " + archetype);
            }
            var staging = new GameObject("Neutral visual transaction staging"); staging.SetActive(false); objects.Add(staging);
            int stagedBoundBefore = BoardActorAnimation.BoundCount;
            var encounter = new R1ContentCatalog.Encounter { Id = "presentation.fixture", Prefab = bossSource,
                Stats = systems.UnitSetting.UnitDataMap[Kind.Knight] };
            var staged = systems.NeutralFactionSystem.StageSpawn(encounter, Team.Monster, -1,
                new Vector3(75000, 1, 75000), staging.transform);
            var stagedRoot = BoardActorPresentation.VisualRoot(staged);
            Check(stagedRoot != null && stagedRoot.GetComponentInChildren<MeshRenderer>().sharedMaterial == catalog.enemyUnitMaterial,
                "third-faction staging receives standard black art");
            Check(BoardActorAnimation.BoundCount == stagedBoundBefore, "inactive transactional actors do not retain animation records before commit");
            var rolledBack = systems.NeutralFactionSystem.StageSpawn(encounter, Team.Monster, -1,
                new Vector3(75001, 1, 75000), staging.transform);
            UnityEngine.Object.DestroyImmediate(rolledBack.gameObject);
            Check(BoardActorAnimation.BoundCount == stagedBoundBefore, "rollback of a never-enabled staged actor leaves no static registration");
            var stagedObject = staged.gameObject;
            stagedObject.transform.SetParent(systems.NeutralFactionSystem.UnitParent, true); objects.Add(stagedObject);
            Check(BoardActorAnimation.BoundCount == stagedBoundBefore + 1, "committed staged actor binds through Status OnEnable");
            staged.isWildBoss = true; staged.wildBossArchetype = WildBossArchetype.RebelKnight;
            BoardActorPresentation.Apply(staged, stagedObject);
            Check(BoardActorPresentation.VisualRoot(staged).GetComponentInChildren<MeshRenderer>().sharedMaterial == catalog.bossMaterial,
                "strong-enemy event identity finalized after staging selects red art");
            var sample = sampleObject.GetComponent<Status>();
            var sourceRenderer = sampleObject.GetComponent<MeshRenderer>();
            catalog.enabledForPlay = false; BoardActorPresentation.Apply(sample);
            Check(!sourceRenderer.forceRenderingOff && !BoardActorPresentation.VisualRoot(sample).gameObject.activeSelf, "disabling presentation restores source without gameplay changes");
            catalog.enabledForPlay = true; BoardActorPresentation.Apply(sample);
            Check(sourceRenderer.forceRenderingOff && BoardActorPresentation.VisualRoot(sample).gameObject.activeSelf, "presentation can be re-enabled without another visual tree");
            var custom = new GameObject("Custom author model is authoritative"); objects.Add(custom);
            var customStatus = custom.AddComponent<Status>(); customStatus.kind = Kind.Knight; customStatus.team = Team.Enemy;
            customStatus.HP = customStatus.MaxHP = 50; customStatus.unitDefinitionId = "author.unit.custom.design";
            custom.AddComponent<MeshFilter>().sharedMesh = sampleObject.GetComponent<MeshFilter>().sharedMesh;
            custom.AddComponent<MeshRenderer>().sharedMaterial = catalog.playerUnitMaterial;
            Check(!BoardActorPresentation.Apply(customStatus) && BoardActorPresentation.VisualRoot(customStatus) == null,
                "custom definition with a reused stock mesh is never silently replaced");
            var recycledObject = UnityEngine.Object.Instantiate(bossSource); objects.Add(recycledObject);
            var recycled = recycledObject.GetComponent<Status>(); recycled.team = Team.Player;
            Check(BoardActorPresentation.Apply(recycled, recycledObject), "fixture initially has a standard presentation");
            recycled.kind = Kind.Crystal;
            Check(!BoardActorPresentation.Apply(recycled) && !BoardActorPresentation.VisualRoot(recycled).gameObject.activeSelf
                && !recycledObject.GetComponent<MeshRenderer>().forceRenderingOff,
                "refreshing a formerly overridden crystal restores the original renderer");
            Check(systems.FactionState.GetAP(Team.Player) == apPlayer && systems.FactionState.GetAP(Team.Enemy) == apEnemy, "visual creation does not spend faction AP");
            Check(UnityEngine.Random.state.Equals(random), "visual creation does not consume game randomness");
        }
        finally
        {
            catalog.enabledForPlay = true;
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            UnityEngine.Random.state = random;
        }
        Check(BoardActorAnimation.BoundCount == boundBefore, "destroyed runtime actors leave no animation registrations");
        Debug.Log("[BoardThemeLive] ALL PASSED " + checks);
    }
}
#endif
