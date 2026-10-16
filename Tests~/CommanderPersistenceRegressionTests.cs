#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Exercises real initial spawning, catalog refresh, JSON slot matching and search snapshots.</summary>
public static class CommanderPersistenceRegressionTests
{
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string name, bool result)
    {
        if (!result) throw new InvalidOperationException("[CommanderPersistence] FAIL " + name);
        passed++; Debug.Log("[CommanderPersistence] PASS " + name);
    }
    static T JsonCopy<T>(T value) => JsonUtility.FromJson<T>(JsonUtility.ToJson(value));

    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        using (var fixture = new Fixture(systems))
        {
            Settings(fixture);
            SaveMatching(fixture);
            TacticalImportance(fixture, systems);
            SearchHashes(systems);
        }
        Debug.Log("[CommanderPersistence] " + passed + " checks passed");
    }

    static void Settings(Fixture f)
    {
        f.SetRoleReplacement(false);
        var parent = f.Group("Commander settings actors");
        var initial = f.Initial(parent);
        var custom = f.Specific(f.Custom, parent);
        Check("initial default uses dedicated 500HP instead of global Boss fallback", initial.GrowthData == f.Dedicated
            && initial.MaxHP == 500 && f.Setting.UnitDataMap[Kind.Boss] != f.Dedicated);
        Check("ordinary authored Boss starts without the commander role", !custom.IsPrimaryCommander && custom.GrowthData == f.Custom);

        f.SetRoleReplacement(true);
        var replacement = f.Initial(parent);
        Check("initial slot uses the catalog replacement data and model", replacement.GrowthData == f.Role
            && replacement.MaxHP == 700 && replacement.name.StartsWith("Commander replacement model"));
        f.Setting.RefreshRuntimeBalance();
        Check("live initial slot follows a newly registered replacement", initial.GrowthData == f.Role
            && initial.MaxHP == 700 && initial.unitDefinitionId == f.Role.definitionId);
        Check("replacement refresh preserves independent custom Boss identity", custom.GrowthData == f.Custom
            && custom.MaxHP == 650 && !custom.IsPrimaryCommander);

        f.Role.baseHP = 750;
        f.Setting.RefreshRuntimeBalance();
        Check("editing replacement data refreshes the commander", initial.MaxHP == 750 && replacement.MaxHP == 750);
        f.Role.baseHP = 700;
        f.Setting.RefreshRuntimeBalance();
        f.SetRoleReplacement(false);
        f.Setting.RefreshRuntimeBalance();
        Check("removing a replacement restores the dedicated initial default", initial.GrowthData == f.Dedicated
            && initial.MaxHP == 500 && string.IsNullOrEmpty(initial.unitDefinitionId));
        Check("removing a role replacement leaves custom Boss data intact", custom.GrowthData == f.Custom && custom.MaxHP == 650);
        f.SetRoleReplacement(true);
        f.Setting.RefreshRuntimeBalance();
    }

    static void SaveMatching(Fixture f)
    {
        var source = f.Specific(f.RetiredRole, f.Group("Historical commander source"), 3);
        source.IsPrimaryCommander = true;
        source.MaxHP += 50; source.HP = source.MaxHP / 2;
        source.ATK += 7; source.DEF += 4; source.Experience = 11; source.SkillCooldown = 2;
        var historical = JsonCopy(SaveSystem.CaptureUnit(source));
        source.gameObject.SetActive(false);
        var parent = f.Group("Commander save slots");
        // Ordinary hierarchy order deliberately precedes the initial slot.
        var ordinary = f.Specific(f.Custom, parent);
        var initial = f.Initial(parent);
        var ordinarySave = JsonCopy(SaveSystem.CaptureUnit(ordinary));
        var data = JsonCopy(new SaveSystem.GameSaveData
        { Units = new List<SaveSystem.UnitSaveData> { ordinarySave, historical } });
        var used = new HashSet<int>();
        SaveGameApplier.ApplyUnitLoadData(data, parent, used);
        Check("explicit role matches a retired definition before ordinary actors", used.SetEquals(new[] { 0, 1 })
            && initial.ReflectionLifeId == historical.ReflectionLifeId && ordinary.ReflectionLifeId == ordinarySave.ReflectionLifeId);
        Check("matched slot follows current commander definition", initial.GrowthData == f.Role && initial.unitDefinitionId == f.Role.definitionId);
        Check("replacement retains rewards and half damage", initial.MaxHP == 820 && initial.HP == 410
            && initial.ATK == 41 && initial.DEF == 28);
        Check("replacement preserves level experience and cooldown", initial.Level == 3 && initial.Experience == 11 && initial.SkillCooldown == 2);
        Check("all records are consumed without creating another commander", parent.GetComponentsInChildren<Status>(true).Length == 2
            && parent.GetComponentsInChildren<Status>(true).Count(u => u.IsPrimaryCommander) == 1 && !ordinary.IsPrimaryCommander);

        data = JsonCopy(new SaveSystem.GameSaveData { Units = new List<SaveSystem.UnitSaveData>
        { SaveSystem.CaptureUnit(ordinary), SaveSystem.CaptureUnit(initial) } });
        used.Clear(); SaveGameApplier.ApplyUnitLoadData(data, parent, used);
        Check("repeated save resume does not multiply reward deltas", initial.MaxHP == 820 && initial.HP == 410 && initial.ATK == 41);

        var legacy = JsonCopy(historical); legacy.HasPrimaryCommanderRole = legacy.IsPrimaryCommander = false;
        var legacyCustom = JsonCopy(ordinarySave); legacyCustom.HasPrimaryCommanderRole = false;
        data.Units = new List<SaveSystem.UnitSaveData> { legacy, legacyCustom };
        used.Clear(); SaveGameApplier.ApplyUnitLoadData(data, parent, used);
        Check("legacy first initial record resolves a retired role definition", used.SetEquals(new[] { 0, 1 })
            && initial.IsPrimaryCommander && initial.ReflectionLifeId == historical.ReflectionLifeId && initial.MaxHP == 820);
        Check("legacy standalone Boss is never promoted", !ordinary.IsPrimaryCommander && ordinary.GrowthData == f.Custom);

        legacy.HasDefinitionStatSnapshot = false;
        used.Clear(); SaveGameApplier.ApplyUnitLoadData(data, parent, used);
        Check("legacy saves without a baseline retain exact earned stats", initial.MaxHP == historical.MaxHP
            && initial.HP == historical.HP && initial.ATK == historical.ATK && initial.DEF == historical.DEF);
        legacy.HasDefinitionStatSnapshot = true; legacy.HP = 0; legacy.IsActive = false;
        used.Clear(); SaveGameApplier.ApplyUnitLoadData(data, parent, used);
        Check("inactive legacy commander matches without revival", used.Contains(0) && initial.IsPrimaryCommander
            && initial.HP == 0 && !initial.gameObject.activeSelf && ordinary.gameObject.activeSelf);

        var unrelatedParent = f.Group("Standalone only legacy fixture");
        var untouched = f.Initial(unrelatedParent); string life = untouched.ReflectionLifeId;
        used.Clear(); SaveGameApplier.ApplyUnitLoadData(new SaveSystem.GameSaveData
            { Units = new List<SaveSystem.UnitSaveData> { legacyCustom } }, unrelatedParent, used);
        Check("legacy standalone-only record cannot consume an initial slot", used.Count == 0
            && untouched.ReflectionLifeId == life && untouched.GrowthData == f.Role);
    }

    static void TacticalImportance(Fixture f, GameSystems systems)
    {
        var group = f.Group("Commander tactical priority");
        var target = f.Specific(f.Custom, group);
        var attacker = f.Specific(f.Custom, group); attacker.team = Team.Player;
        var board = new AIBoardState(systems.MoveGenerator, systems.AttackGenerator, systems.APSystem, systems.UnitSetting,
            systems.CrystalSystem, systems.VisionGenerator, systems.BuildSystem, systems.SummonSystem,
            systems.FactionState, systems.SubCrystalSystem, 20);
        var score = typeof(AIBoardState).Assembly.GetType("AICombatEvaluator")
            .GetMethod("CalcAttackBaseScore", BindingFlags.Static | BindingFlags.NonPublic);
        var action = new AIAction { ActionType = AIActionType.Attack, Unit = attacker, TargetUnit = target };
        float ordinary = (float)score.Invoke(null, new object[] { action, board });
        target.IsPrimaryCommander = true;
        Check("actual tactical attack values a primary Boss as the commander", (float)score.Invoke(null, new object[] { action, board }) == ordinary + 35f);
        Check("third faction King or primary flag is never a main commander", !MatchObjectiveRules.IsCommander(Kind.King, Team.Monster, true));
    }

    static void SearchHashes(GameSystems systems)
    {
        var hashMethod = typeof(AIMinimaxEngine).GetMethod("BoardHash", BindingFlags.Static | BindingFlags.NonPublic);
        Check("search hash method is available", hashMethod != null);
        foreach (var actorTeam in new[] { Team.Enemy, Team.Player })
        {
            var real = new AIBoardState(systems.MoveGenerator, systems.AttackGenerator, systems.APSystem, systems.UnitSetting,
                systems.CrystalSystem, systems.VisionGenerator, systems.BuildSystem, systems.SummonSystem,
                systems.FactionState, systems.SubCrystalSystem, 20, null, actorTeam);
            real.Refresh();
            var snapshot = SimBoardState.CreateFromGame(real, systems.MoveGenerator, systems.UnitSetting, systems.CrystalSystem, systems.APSystem);
            try
            {
                var commander = snapshot.Units.Single(u => u.Team == Team.Enemy && u.IsPrimaryCommander);
                Check(actorTeam + " real snapshot preserves normalized primary identity", commander.IsAlive);
                // The real snapshot correctly excludes unseen opponents. Add a synthetic
                // attacker only for this isolated score comparison; do not relax Fog.
                var striker = commander.Clone(); striker.Team = Team.Player; striker.IsPrimaryCommander = false;
                striker.Id = snapshot.Units.Max(u => u.Id) + 1; snapshot.Units.Add(striker);
                int priorHP = commander.HP, priorShield = commander.ShieldTurns, priorAttack = striker.ATK;
                Kind priorKind = commander.Kind;
                commander.Kind = Kind.Boss; commander.HP = 1; commander.ShieldTurns = 0; striker.ATK = 100000;
                var strike = new SimAction { Type = SimActionType.Attack, ActorTeam = Team.Player, UnitId = striker.Id, TargetUnitId = commander.Id };
                commander.IsPrimaryCommander = false;
                float ordinaryScore = SimActionGenerator.QuickScore(strike, snapshot);
                commander.IsPrimaryCommander = true;
                Check(actorTeam + " simulated lethal primary attack receives commander priority", Mathf.Abs(SimActionGenerator.QuickScore(strike, snapshot) - ordinaryScore - AIConstants.QS_Kill_King) < .001f);
                commander.Kind = priorKind; commander.HP = priorHP; commander.ShieldTurns = priorShield; striker.ATK = priorAttack;
                var ordinary = commander.Clone(); ordinary.IsPrimaryCommander = false; ordinary.Id = snapshot.Units.Max(u => u.Id) + 1;
                ordinary.Position += Vector3Int.right * 2; snapshot.Units.Add(ordinary);
                long before = (long)hashMethod.Invoke(null, new object[] { snapshot });
                var first = commander.Position; commander.Position = ordinary.Position; ordinary.Position = first;
                long after = (long)hashMethod.Invoke(null, new object[] { snapshot });
                Check(actorTeam + " swapped equal-stat actors retain distinct commander positions in hash", before != after);
                snapshot.Units.Reverse();
                Check(actorTeam + " commander-aware hash remains actor-order independent", after == (long)hashMethod.Invoke(null, new object[] { snapshot }));
                var pooled = commander.Clone(); SimBoardPool.ReturnUnit(pooled);
                pooled = ordinary.Clone();
                Check(actorTeam + " ordinary clone clears a reused commander role", !pooled.IsPrimaryCommander);
                SimBoardPool.ReturnUnit(pooled);
            }
            finally { SimBoardPool.ReturnBoard(snapshot); }
        }
    }

    sealed class Fixture : IDisposable
    {
        readonly GameSystems systems;
        readonly UnitAuthoringCatalog catalog;
        readonly List<UnitAuthoringCatalog.Entry> previousUnits, previousRetired;
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        readonly GameObject previousStrange;
        readonly Status liveCommander;
        readonly SaveSystem.UnitSaveData liveSave;
        readonly UnityEngine.Random.State randomState;
        readonly GameObject root;
        readonly UnitAuthoringCatalog.Entry roleEntry;
        public readonly UnitSetting Setting;
        public readonly UnitData Dedicated, Role, Custom, RetiredRole;
        readonly Vector3 position;

        public Fixture(GameSystems systems)
        {
            this.systems = systems; Setting = systems.UnitSetting;
            randomState = UnityEngine.Random.state;
            catalog = UnitAuthoringCatalog.Load();
            if (catalog == null) throw new InvalidOperationException("Commander regression requires the initialized unit catalog");
            previousUnits = catalog.units; previousRetired = catalog.retiredUnits;
            previousStrange = (GameObject)typeof(UnitSetting).GetField("StrangePiece", PrivateInstance).GetValue(Setting);
            Dedicated = (UnitData)typeof(UnitSetting).GetField("initialEnemyCommanderData", PrivateInstance).GetValue(Setting);
            liveCommander = Setting.EnemyUnit.GetComponentsInChildren<Status>(true).Single(u => u.IsPrimaryCommander);
            liveSave = JsonCopy(SaveSystem.CaptureUnit(liveCommander));
            position = systems.MapCreate.SetPos[0];
            root = new GameObject("Commander persistence regression"); owned.Add(root);
            var defaultModel = Model("Commander default model"); var replacementModel = Model("Commander replacement model");
            typeof(UnitSetting).GetField("StrangePiece", PrivateInstance).SetValue(Setting, defaultModel);
            Role = Definition("commander-regression-current", 700, 30, 20, replacementModel);
            Custom = Definition("commander-regression-custom", 650, 30, 20, defaultModel);
            RetiredRole = Definition("commander-regression-retired", 400, 20, 10, defaultModel);
            roleEntry = new UnitAuthoringCatalog.Entry { kind = Kind.Boss, data = Role, prefab = replacementModel };
            catalog.units = previousUnits.Where(e => e == null || e.kind != Kind.Boss || e.standaloneDefinition).ToList();
            catalog.units.Add(new UnitAuthoringCatalog.Entry { kind = Kind.Boss, data = Custom, prefab = defaultModel, standaloneDefinition = true });
            catalog.retiredUnits = new List<UnitAuthoringCatalog.Entry>(previousRetired ?? new List<UnitAuthoringCatalog.Entry>());
            catalog.retiredUnits.Add(new UnitAuthoringCatalog.Entry { kind = Kind.Boss, data = RetiredRole, prefab = defaultModel });
        }

        GameObject Model(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule); go.name = name; go.transform.SetParent(root.transform);
            var actor = go.AddComponent<Status>(); actor.type = Type.Unit; actor.kind = Kind.Boss; actor.team = Team.Obstacle;
            go.SetActive(false); return go;
        }
        UnitData Definition(string id, int hp, int atk, int def, GameObject model)
        {
            var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data);
            data.definitionId = id; data.kind = Kind.Boss; data.baseHP = hp; data.baseATK = atk; data.baseDEF = def;
            data.hpGrowth = .05f; data.atkGrowth = .03f; data.defGrowth = .02f; data.prefab = model;
            return data;
        }
        public Transform Group(string name)
        {
            var go = new GameObject(name); go.transform.SetParent(root.transform); return go.transform;
        }
        public Status Initial(Transform parent)
        {
            var method = typeof(UnitSetting).GetMethod("SpawnInitialCommander", PrivateInstance);
            var go = (GameObject)method.Invoke(Setting, new object[] { Team.Enemy, position, parent });
            go.SetActive(true); return go.GetComponentInChildren<Status>(true);
        }
        public Status Specific(UnitData data, Transform parent, int level = 1)
        {
            var go = Setting.SpawnUnit(data, position, parent, level, Team.Enemy);
            go.SetActive(true); return go.GetComponentInChildren<Status>(true);
        }
        public void SetRoleReplacement(bool enabled)
        {
            catalog.units.Remove(roleEntry); if (enabled) catalog.units.Add(roleEntry);
            Setting.ReloadAuthoringDefinitions();
        }
        public void Dispose()
        {
            // Hide fixtures before the final balance/vision refresh and release their data afterwards.
            root.SetActive(false);
            catalog.units = previousUnits; catalog.retiredUnits = previousRetired;
            typeof(UnitSetting).GetField("StrangePiece", PrivateInstance).SetValue(Setting, previousStrange);
            Setting.RefreshRuntimeBalance();
            SaveGameApplier.ApplyStatusFields(liveCommander, liveSave);
            foreach (var obj in owned) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            UnityEngine.Random.state = randomState;
            systems.MoveGenerator.UnitPointCore(); systems.RefreshVision();
        }
    }
}
#endif
