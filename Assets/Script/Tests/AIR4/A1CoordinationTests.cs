#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

/// <summary>Manually callable Editor contracts. No battle is launched and no match result
/// is asserted. Coordinator fixtures inject formal target witnesses; AttackGenerator/Fog
/// integration and tactical win-rate cases require a separate native playtest.</summary>
public static class A1CoordinationTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static void Check(string id, bool ok)
    { if (!ok) throw new InvalidOperationException("[A1Coordination] FAIL " + id); Debug.Log("[A1Coordination] PASS " + id); }
    static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static T Read<T>(object target, string name) => (T)target.GetType().GetField(name, Private).GetValue(target);
    static void Property(object target, string name, object value) => Field(target, "<" + name + ">k__BackingField", value);
    public static void Run()
    {
        CapabilityContracts(); DefaultRoleContracts(); DebuffContracts(); FormalCostContracts(); CoordinatorContracts();
    }

    static void CapabilityContracts()
    {
        var go = new GameObject("A1-capability-fixture");
        var data = ScriptableObject.CreateInstance<UnitData>();
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        var aura = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        try
        {
            data.kind = Kind.Knight; data.baseHP = 30; data.baseATK = 10; data.actionProfile = profile;
            data.useAuthoredAbilities = true; data.authoredSkillId = -1;
            var actor = go.AddComponent<Status>(); actor.team = Team.Enemy; actor.type = Type.Unit;
            actor.kind = data.kind; actor.GrowthData = data; actor.HP = actor.MaxHP = 30; actor.AssignedSkillId = -1;
            profile.attack.useCustom = true; profile.attack.SetCell(3, 6, true);
            Check("RL02-custom-long-range-Knight-has-real-reach", AICapabilityRoles.For(actor).MinimumAttackReach == 3
                && AICapabilityRoles.For(actor).MaximumAttackReach == 3
                && AIOperationManager.SquadRole(actor) == AIOperationSquadRole.Ranged
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) == "ranged");
            profile.attack.cells = 0; profile.attack.SetCell(3, 4, true);
            Check("RL02-same-Kind-melee-mask-changes-role", AIOperationManager.SquadRole(actor) == AIOperationSquadRole.Frontline
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) == "frontline");
            profile.attack.cells = 0;
            Check("empty-custom-mask-does-not-fall-back-to-Kind", !AICapabilityRoles.For(actor).CanAttack
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) == null);
            profile.attack.originX = profile.attack.originZ = 0; profile.attack.SetCell(7, 0, true);
            Check("full-eight-cell-origin-range-is-recognized", AICapabilityRoles.For(actor).MaximumAttackReach == 7);
            profile.actionType = ActorActionType.Support;
            Check("support-action-type-disables-a-populated-attack-mask", !AICapabilityRoles.For(actor).CanAttack);
            profile.actionType = ActorActionType.All;
            profile.movement.useCustom = profile.vision.useCustom = true;
            profile.movement.directionIndependent = profile.vision.directionIndependent = true;
            profile.movement.SetCell(3, 5, true); profile.movement.SetCell(3, 1, true);
            for (int x = 1; x <= 5; x++) for (int z = 1; z <= 5; z++) profile.vision.SetCell(x, z, true);
            Check("broad-mobile-ranged-keeps-its-fire-role-without-a-vision-advantage", !AICapabilityRoles.For(actor).Reconnaissance
                && AIOperationManager.SquadRole(actor) == AIOperationSquadRole.Ranged);
            profile.attack.cells = 0; profile.attack.originX = profile.attack.originZ = 3; profile.attack.SetCell(3, 4, true);
            Check("RL04-custom-broad-mobile-observer-can-scout-without-Scout-Kind", AICapabilityRoles.For(actor).Reconnaissance
                && AICapabilityRoles.For(actor).MovementReach == 2 && AICapabilityRoles.For(actor).VisionFootprint == 25
                && AIOperationManager.SquadRole(actor) == AIOperationSquadRole.Scout
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) == "scout");
            actor.kind = data.kind = Kind.Scout; profile.movement.cells = 0; profile.movement.SetCell(3, 4, true);
            Check("Scout-name-alone-cannot-invent-mobility", !AICapabilityRoles.For(actor).Reconnaissance
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) != "scout");
            actor.kind = data.kind = Kind.Priest; profile.actionType = ActorActionType.Passive;
            Check("RL01-no-healing-Priest-is-not-healer", !AICapabilityRoles.For(actor).Healing);
            aura.ability = UnitSpecialSkill.PriestPurification; aura.enabledForPlay = true; aura.apCost = 8; aura.cooldown = 0;
            data.specialSkill = aura;
            Check("passive-aura-fixture-has-complete-formal-configuration", aura.ConfigurationError == null && data.IsValidForAuthoring);
            Check("RL01-real-passive-aura-remains-healer", AICapabilityRoles.For(actor).Healing
                && AIOperationManager.SquadRole(actor) == AIOperationSquadRole.Healer
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) == "healer");
            aura.ability = UnitSpecialSkill.WhiteMageHeal; aura.apCost = 12;
            Check("passive-action-profile-does-not-invent-an-active-heal", !AICapabilityRoles.For(actor).Healing
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) != "healer");
            profile.actionType = ActorActionType.Support;
            Check("support-profile-retains-a-valid-active-heal", AICapabilityRoles.For(actor).Healing
                && AIOperationManager.SquadRole(actor) == AIOperationSquadRole.Healer
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) == "healer");
            actor.IsPrimaryCommander = true;
            Check("RL08-commander-never-fills-a-disposable-recruitment-role", AICapabilityRoles.RecruitmentRole(actor) == null);
            Check("RL09-save-role-enum-values-remain-stable", (int)AIOperationSquadRole.Frontline == 0
                && (int)AIOperationSquadRole.Ranged == 1 && (int)AIOperationSquadRole.Healer == 2 && (int)AIOperationSquadRole.Scout == 3);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(data);
            UnityEngine.Object.DestroyImmediate(profile); UnityEngine.Object.DestroyImmediate(aura); }
    }

    static void DefaultRoleContracts()
    {
        var desired = typeof(AIOperationManager).GetMethod("DesiredRoleCount", Private | BindingFlags.Static);
        Check("squad-role-budget-contract-is-available", desired != null);
        for (int quota = 1; quota <= 16; quota++)
        {
            int total = 0;
            foreach (AIOperationSquadRole role in Enum.GetValues(typeof(AIOperationSquadRole)))
                total += (int)desired.Invoke(null, new object[] { role, quota, false });
            Check("squad-role-budget-accounts-for-each-slot-" + quota, total == quota);
            if (quota >= 5) Check("partial-second-squad-retains-healing-role-" + quota,
                (int)desired.Invoke(null, new object[] { AIOperationSquadRole.Healer, quota, false }) >= 1);
        }
        var baseline = new[]
        {
            ("Knight", "frontline"), ("Assassin", "frontline"), ("Guardian", "frontline"),
            ("Archer", "ranged"), ("Magic", "ranged"), ("Crossbow", "ranged"),
            ("Magic Sniper", "ranged"), ("Bomber", "ranged"), ("Scout", "scout")
        };
        int scouts = 0, ranged = 0, frontline = 0;
        foreach (var entry in baseline)
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>("Assets/Data/Unit/" + entry.Item1 + ".asset");
            Check("baseline-definition-exists-" + entry.Item1, definition != null);
            string role = AIStrategicGovernor.RecruitmentRole(definition, definition.kind);
            Check("RL02-baseline-role-" + entry.Item1, role == entry.Item2);
            if (role == "scout") scouts++; else if (role == "ranged") ranged++; else if (role == "frontline") frontline++;
        }
        Check("RL03-baseline-composition-keeps-distinct-shields-fire-and-recon", scouts == 1 && ranged == 5 && frontline == 3);
        foreach (var entry in new[] { ("Ninja", "ranged"), ("Navigator", "frontline"), ("Berserker", "frontline"),
            ("HeavyWarrior", "frontline"), ("DragonKnight", "frontline"), ("Samurai", "frontline") })
        {
            var definition = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitData>("Assets/_FantasyKingdom/GameContent/R1Production/Units/" + entry.Item1 + "/Stats.asset");
            Check("production-definition-exists-" + entry.Item1, definition != null);
            Check("RL02-authored-production-role-" + entry.Item1, AIStrategicGovernor.RecruitmentRole(definition, definition.kind) == entry.Item2);
        }
    }

    static void DebuffContracts()
    {
        var go = new GameObject("A1-debuff-fixture");
        try
        {
            var target = go.AddComponent<Status>(); target.kind = Kind.Knight; target.type = Type.Unit; target.HP = target.MaxHP = 100;
            var skill = new SkillData { InflictDebuff = StatusEffectType.Mark, DebuffChance = 1 };
            var action = new AIAction { ActionType = AIActionType.SkillUse, TargetUnit = target, Skill = skill, APCost = 5 };
            Check("CQ02-guaranteed-new-Mark-can-prepare-following-attacks", AIOperationManager.UsefulCoordinationDebuff(action, target));
            skill.DebuffChance = .99f;
            Check("CQ03-probable-control-is-not-guaranteed", !AIOperationManager.UsefulCoordinationDebuff(action, target));
            skill.DebuffChance = float.NaN;
            Check("nonfinite-control-chance-is-rejected", !AIOperationManager.UsefulCoordinationDebuff(action, target));
            skill.DebuffChance = 1; target.kind = Kind.Crystal;
            Check("CQ03-main-crystal-immunity-prevents-invented-synergy", !AIOperationManager.UsefulCoordinationDebuff(action, target));
            target.kind = Kind.Knight; target.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Mark, 1));
            Check("already-active-debuff-is-not-extra-damage", !AIOperationManager.UsefulCoordinationDebuff(action, target));
            target.ActiveEffects.Clear(); target.specialAbility = SpecialAbility.IndomitableWill;
            Check("unused-formal-debuff-nullification-is-respected", !AIOperationManager.UsefulCoordinationDebuff(action, target));
            target.DebuffNullifiedThisTurn = true;
            Check("spent-nullification-does-not-grant-permanent-immunity", AIOperationManager.UsefulCoordinationDebuff(action, target));
            target.specialAbility = SpecialAbility.None; skill.InflictDebuff = StatusEffectType.Poison;
            Check("CQ03-poison-is-not-immediate-normal-attack-synergy", !AIOperationManager.UsefulCoordinationDebuff(action, target));
            skill.InflictDebuff = StatusEffectType.ArmorBreak; target.DEF = 10;
            Check("R2-no-ordinary-DEF-means-no-ArmorBreak-synergy", AIOperationManager.UsefulCoordinationDebuff(action, target) == DamageCalculator.UsesLegacyDefence);
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    static void FormalCostContracts()
    {
        var go = new GameObject("A1-cost-fixture");
        try
        {
            var actor = go.AddComponent<Status>(); actor.type = Type.Unit; actor.HP = actor.MaxHP = 100;
            var ap = go.AddComponent<APSystem>();
            var board = (AIBoardState)FormatterServices.GetUninitializedObject(typeof(AIBoardState)); Field(board, "_apSystem", ap);
            Property(board, "EnemyAP", 40);
            foreach (var kind in new[] { Kind.Knight, Kind.Assassin, Kind.Guardian })
                foreach (int fatigue in new[] { 0, 3, 12 })
                {
                    actor.kind = kind; actor.Fatigue = fatigue;
                    int projected = board.CoordinationAttackCost(actor, true);
                    actor.Fatigue += 1 + GameConstants.GetExtraFatiguePerAction(kind);
                    Check("CQ05-follow-up-matches-formal-fatigue-quote-" + kind + "-" + fatigue,
                        projected == ap.CalcCost(APSystem.ActionType.Attack, actor));
                    actor.Fatigue = fatigue;
                    _ = board.CoordinationAttackCost(actor, true); _ = board.CoordinationReturnCost(actor, Vector3.forward, Vector3.zero);
                    Check("preview-does-not-spend-AP-or-mutate-fatigue-" + kind + "-" + fatigue, actor.Fatigue == fatigue && board.EnemyAP == 40);
                }
        }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    sealed class Fixture : IDisposable
    {
        public readonly GameObject Root = new GameObject("A1-current-observation-fixture");
        public readonly AIBoardState Board = (AIBoardState)FormatterServices.GetUninitializedObject(typeof(AIBoardState));
        public readonly AIOperationManager Manager = new AIOperationManager();
        public readonly List<AIAction> Actions = new List<AIAction>();
        public readonly HashSet<Vector3Int> Vision = new HashSet<Vector3Int>();
        public readonly Status First, Second, Target;
        public Fixture()
        {
            var ap = Root.AddComponent<APSystem>();
            var vision = Root.AddComponent<VisionGenerator>(); Field(vision, "_enemyVisionBox", Vision);
            Field(vision, "_enemyExplored", new HashSet<Vector3Int>());
            var move = Root.AddComponent<MoveGenerator>(); move.mapcreate = Root.AddComponent<MapCreate>();
            Field(Board, "_apSystem", ap); Field(Board, "_visionGen", vision); Field(Board, "_moveGen", move);
            Field(Board, "knownCells", new HashSet<Vector3Int>()); Field(Board, "Enemies", new ObservedUnitIndex());
            Field(Board, "CounterCache", new Dictionary<(Vector3, Status, int), int>());
            Property(Board, "ActorTeam", Team.Enemy); Property(Board, "Generation", 1); Property(Board, "TurnCount", 1);
            Board.RemainingPlannedActionSlots = 5; Board.ReconThreatLevel = 1;
            First = Unit("a", Team.Enemy, new Vector3(1, 0, 1)); Second = Unit("b", Team.Enemy, new Vector3(2, 0, 1));
            Target = Unit("target", Team.Player, new Vector3(1, 0, 2));
            Vision.Add(GridHelper.ToGrid(Target.transform.position));
            Property(Board, "AliveEnemyUnits", new List<Status> { First, Second }); Property(Board, "AlivePlayerUnits", new List<Status> { Target });
            Field(Board, "specialObservedActors", new List<Status> { First, Second, Target }); Field(Board, "specialObservedGeneration", 1);
            Target.Barrier = 0;
            int raw = AIActionEvaluator.EstimateSpecialAwareAttack(First, Target, Board);
            Target.HP = Target.MaxHP = Mathf.Max(2, raw * 2);
            foreach (var actor in new[] { First, Second })
            {
                actor.Fatigue = Mathf.Max(0, 8 - APSystem.BaseAttackCost);
                Actions.Add(new AIAction { Unit = actor, TargetUnit = Target, ActionType = AIActionType.Attack, APCost = Board.CalcAttackCost(actor) });
            }
            Property(Board, "EnemyAP", Actions[0].APCost + Actions[1].APCost);
            Manager.BeginBattle(Team.Enemy, "a1-contract");
            var plan = new AIOperationPlan { OperationId = 1, Status = AIOperationStatus.Active, PrimaryGoal = AIOperationGoal.BreakEnemyDefense,
                TargetLifeId = Target.ReflectionLifeId, AssignedUnitLifeIds = new List<string> { First.ReflectionLifeId, Second.ReflectionLifeId } };
            Manager.State.ActiveOperations.Add(plan);
            var squad = new AIOperationSquad { SquadId = 1, OperationId = 1, Phase = AIOperationSquadPhase.Engaged };
            Manager.State.Squads.Add(squad);
            var own = Read<Dictionary<string, Status>>(Read<AIOperationFeatureExtractor>(Manager, "features"), "own");
            var squads = Read<Dictionary<string, AIOperationSquad>>(Manager, "squadByActor");
            var members = Read<Dictionary<string, AIOperationSquadMember>>(Manager, "squadMembers");
            var targets = Read<Dictionary<Status, List<Status>>>(Manager, "squadAttackTargets");
            foreach (var actor in new[] { First, Second })
            {
                var member = new AIOperationSquadMember { ActorLifeId = actor.ReflectionLifeId, Role = AIOperationSquadRole.Frontline };
                squad.Members.Add(member); own.Add(member.ActorLifeId, actor); squads.Add(member.ActorLifeId, squad); members.Add(member.ActorLifeId, member);
                targets.Add(actor, new List<Status> { Target });
            }
            Field(Manager, "squadBoard", Board); Field(Manager, "squadGeneration", 1);
            Field(Manager, "squadAttackBoard", Board); Field(Manager, "squadAttackGeneration", 1);
        }
        Status Unit(string id, Team team, Vector3 position)
        {
            var go = new GameObject(id); go.transform.SetParent(Root.transform); go.transform.position = position;
            var actor = go.AddComponent<Status>(); actor.kind = Kind.Knight; actor.type = Type.Unit; actor.team = team;
            actor.HP = actor.MaxHP = 100; actor.ATK = 10; actor.RestoreReflectionLifeId(id); actor.AssignedSkillId = -1; return actor;
        }
        public bool Current => (bool)typeof(AIOperationManager).GetMethod("CurrentCoordination", Private).Invoke(Manager, new object[] { Board });
        public void Dispose() => UnityEngine.Object.DestroyImmediate(Root);
    }

    static void CoordinatorContracts()
    {
        AITurnBudget.Begin(5000);
        using (var f = new Fixture())
        {
            f.Board.ReconThreatLevel = 15;
            f.Manager.UpdateTacticalCoordination(f.Actions, f.Board);
            Check("CQ11-complete-safe-kill-chain-has-an-executable-head", f.Manager.IsCoordinatedKillHead(f.Actions[0], f.Board)
                || f.Manager.IsCoordinatedKillHead(f.Actions[1], f.Board));
            Check("CQ01-two-current-formal-attacks-compose-a-bounded-kill", f.Manager.CoordinationSteps.Count == 2 && f.Current);
            Check("CQ05-real-budget-above-stale-15-is-usable", f.Board.EnemyAP > 15 && f.Board.EnemyAP <= APSystem.MaximumAP);
            f.Board.ReconThreatLevel = 1;
            Check("CQ11-low-threat-keeps-normal-selection", !f.Manager.IsCoordinatedKillHead(f.Actions[0], f.Board)
                && !f.Manager.IsCoordinatedKillHead(f.Actions[1], f.Board));
            f.Board.ReconThreatLevel = 15;
            int ap = f.Board.EnemyAP; Property(f.Board, "EnemyAP", ap - 1);
            Check("AT08-other-squad-AP-spend-invalidates-old-sequence", !f.Current); Property(f.Board, "EnemyAP", ap);
            f.Target.HP--;
            Check("CQ04-target-HP-change-requires-regeneration", !f.Current); f.Target.HP++;
            if (f.Target.MaxBarrier > 0)
            {
                f.Target.Barrier++;
                Check("changed-finite-protection-requires-regeneration", !f.Current); f.Target.Barrier--;
            }
            f.Second.Fatigue++;
            Check("CQ05-changed-official-action-cost-invalidates-sequence", !f.Current); f.Second.Fatigue--;
            f.Target.transform.position += Vector3.right;
            Check("CQ04-target-movement-requires-new-formal-target-witnesses", !f.Current); f.Target.transform.position -= Vector3.right;
            f.Second.direction = f.Second.direction == Direction.N ? Direction.S : Direction.N;
            Check("CQ06-ally-facing-change-requires-revalidation", !f.Current);
            f.Second.direction = f.Second.direction == Direction.N ? Direction.S : Direction.N;
            f.Second.IsPrimaryCommander = true;
            Check("CQ10-commander-cannot-become-a-cached-disposable-step", !f.Current); f.Second.IsPrimaryCommander = false;
            f.Target.HP = 0;
            Check("CQ04-target-death-invalidates-old-sequence", !f.Current); f.Target.HP = f.Target.MaxHP;
            f.Vision.Clear();
            Check("CQ09-target-visibility-loss-invalidates-sequence", !f.Current);
            f.Manager.UpdateTacticalCoordination(f.Actions, f.Board);
            Check("CQ09-live-hidden-target-reference-cannot-rebuild-a-chain", f.Manager.CoordinationSteps.Count == 0);
            f.Vision.Add(GridHelper.ToGrid(f.Target.transform.position));
            f.Manager.UpdateTacticalCoordination(new List<AIAction> { f.Actions[0], f.Actions[0] }, f.Board);
            Check("CQ05-one-actor-cannot-be-counted-twice-at-current-fatigue", f.Manager.CoordinationSteps.Count == 0);
            f.Actions[1].APCost++;
            f.Manager.UpdateTacticalCoordination(f.Actions, f.Board);
            Check("stale-advertised-AP-cannot-form-a-legal-chain", f.Manager.CoordinationSteps.Count == 0); f.Actions[1].APCost--;
            f.Manager.UpdateTacticalCoordination(f.Actions, f.Board); Property(f.Board, "Generation", 2);
            Check("CQ04-generation-change-cannot-replay-current-plan", !f.Current);
        }
    }
}
#endif
