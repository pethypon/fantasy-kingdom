#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

/// <summary>Explicit Editor contracts. Does not start a battle or claim a playtest result.</summary>
public static class AIHealerRoleRecruitmentTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[HealerRole] FAIL " + id); Debug.Log("[HealerRole] PASS " + id); }
    static void Field(object target, string name, object value) => target.GetType().GetField(name, Private).SetValue(target, value);
    static void Property(object target, string name, object value) => Field(target, "<" + name + ">k__BackingField", value);
    static AIBoardState EmptyBoard()
    {
        // Deliberately no running scene, turn driver, fog changes or resource grants to a real game.
        var board = (AIBoardState)FormatterServices.GetUninitializedObject(typeof(AIBoardState));
        Property(board, "ActorTeam", Team.Enemy); Property(board, "TurnCount", 1);
        Property(board, "AliveEnemyUnits", new List<Status>()); Property(board, "AlivePlayerUnits", new List<Status>());
        Property(board, "SummonablePositions", new List<Vector3Int> { Vector3Int.zero });
        Field(board, "HealerCache", new Dictionary<(Vector3,float),bool>());
        Field(board, "Allies", new ObservedUnitIndex());
        return board;
    }
    public static void Run()
    {
        HealingIdentityAndRange();
        OrderedPaidRecruitment();
    }
    static void HealingIdentityAndRange()
    {
        var go = new GameObject("healer-role-contract");
        var data = ScriptableObject.CreateInstance<UnitData>();
        var special = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        var profile = ScriptableObject.CreateInstance<BoardActionProfile>();
        try
        {
            data.kind = Kind.Priest; data.baseHP = 28; data.baseATK = 3; data.specialSkill = special; data.actionProfile = profile;
            special.ability = UnitSpecialSkill.PriestPurification; special.enabledForPlay = true; special.apCost = 8; special.cooldown = 0;
            profile.actionType = ActorActionType.All;
            var actor = go.AddComponent<Status>(); actor.team = Team.Enemy; actor.type = Type.Unit; actor.kind = data.kind;
            actor.GrowthData = data; actor.HP = actor.MaxHP = 28; actor.AssignedSkillId = -1;
            Check("priest-actual-aura-is-a-shared-healer", AIHealingCapability.HasHealing(actor)
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) == "healer"
                && AIOperationManager.SquadRole(actor) == AIOperationSquadRole.Healer);
            Check("aura-uses-3D-grid-diagonal", AIHealingCapability.CanHealAt(actor, new Vector3(1, 1, 1), 4));
            Check("aura-does-not-expand-to-recovery-search-radius", !AIHealingCapability.CanHealAt(actor, new Vector3(2, 0, 0), 4)
                && !AIHealingCapability.CanHealAt(actor, new Vector3(0, 2, 0), 4));
            var board = EmptyBoard(); board.AliveEnemyUnits.Add(actor); board.Allies.Rebuild(board.AliveEnemyUnits);
            Check("recovery-query-sees-adjacent-aura-only", board.HasHealerInRange(new Vector3(1, 1, 1), 4)
                && !board.HasHealerInRange(new Vector3(2, 0, 0), 4));
            var assigner = new AIRoleAssigner(); assigner.AssignRoles(board, TurnStrategy.Assault, new AIPersonality(MajorPersonality.Intellect, 1));
            Check("legacy-role-assigner-shares-real-support-capability", assigner.GetRole(actor) == UnitRole.Support);
            profile.actionType = ActorActionType.Passive;
            Check("passive-profile-does-not-delete-real-owner-turn-aura", AIHealingCapability.HasHealing(actor)
                && AIOperationManager.SquadRole(actor) == AIOperationSquadRole.Healer);
            special.enabledForPlay = false;
            Check("disabled-ability-and-kind-alone-cannot-invent-healing", !AIHealingCapability.HasHealing(actor)
                && !AIHealingCapability.HasHealing(data) && AIOperationManager.SquadRole(actor) != AIOperationSquadRole.Healer
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) != "healer");
            special.enabledForPlay = true; special.ability = UnitSpecialSkill.ScoutVisionGuard; special.apCost = 20;
            Check("custom-nonhealing-priest-is-not-a-healer", !AIHealingCapability.HasHealing(actor) && !AIHealingCapability.HasHealing(data));
            special.ability = UnitSpecialSkill.WhiteMageHeal; special.apCost = -1;
            Check("invalid-active-definition-is-not-healing-evidence", !AIHealingCapability.HasHealing(actor) && !AIHealingCapability.HasHealing(data));
            special.apCost = 12; profile.actionType = ActorActionType.All; data.kind = actor.kind = Kind.Knight;
            Check("custom-real-healing-is-not-restricted-to-priest-kind", AIHealingCapability.HasHealing(actor)
                && AIStrategicGovernor.RecruitmentRole(data, data.kind) == "healer");
            data.specialSkill = null; data.useAuthoredAbilities = true;
            int healingId = -1; foreach (var entry in SkillData.Table) if (entry.Value.FixedHeal > 0) { healingId = entry.Key; break; }
            Check("normal-healing-fixture-exists", healingId >= 0);
            data.authoredSkillId = actor.AssignedSkillId = healingId;
            Check("authored-normal-healing-remains-supported", AIHealingCapability.HasHealing(data) && AIHealingCapability.HasHealing(actor));
            profile.actionType = ActorActionType.Passive;
            Check("passive-profile-cannot-use-an-active-normal-heal", !AIHealingCapability.HasHealing(data) && !AIHealingCapability.HasHealing(actor));
            data.specialSkill = special; special.enabledForPlay = false;
            Check("disabled-fixed-special-does-not-fall-back-to-a-historical-authored-skill", !AIHealingCapability.HasHealing(data));
        }
        finally { UnityEngine.Object.DestroyImmediate(go); UnityEngine.Object.DestroyImmediate(data);
            UnityEngine.Object.DestroyImmediate(special); UnityEngine.Object.DestroyImmediate(profile); }
    }

    sealed class Fixture : IDisposable
    {
        public readonly GameObject Root = new GameObject("paid-role-replacement-contract");
        public readonly UnitData Preferred = ScriptableObject.CreateInstance<UnitData>();
        public readonly UnitData Alternative = ScriptableObject.CreateInstance<UnitData>();
        readonly SpecialSkillDefinition aura = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        readonly SpecialSkillDefinition heal = ScriptableObject.CreateInstance<SpecialSkillDefinition>();
        public readonly AIBoardState Board = EmptyBoard();
        public readonly FactionState.ResourceData Resources = new FactionState.ResourceData
            { Wood = 500, Stone = 500, Iron = 500, MagicOre = 500, Water = 150, Wheat = 500, Bread = 500, Citizen = 5 };
        public Fixture()
        {
            aura.ability = UnitSpecialSkill.PriestPurification; aura.enabledForPlay = true; aura.apCost = 8; aura.cooldown = 0;
            heal.ability = UnitSpecialSkill.WhiteMageHeal; heal.enabledForPlay = true; heal.apCost = 12; heal.cooldown = 0;
            Preferred.kind = Kind.Priest; Alternative.kind = Kind.WhiteMage;
            foreach (var data in new[] { Preferred, Alternative })
            { data.baseHP = 28; data.baseATK = 3; data.costCitizen = 1; data.costAP = 14; data.useAuthoredUpkeep = true; data.upkeepBread = 1; }
            Preferred.specialSkill = aura; Alternative.specialSkill = heal; Preferred.costWater = 150; Alternative.costWater = 100;
            var nation = Root.AddComponent<NationState>(); nation.Resources = Resources; nation.AP.Current = 37;
            var faction = Root.AddComponent<FactionState>(); faction.EnemyNation = nation; faction.PlayerNation = nation;
            var summon = Root.AddComponent<SummonSystem>(); Field(summon, "factionState", faction);
            Field(Board, "_summonSystem", summon); Property(Board, "EnemyResources", Resources); Property(Board, "EnemyAP", 37);
        }
        public AIAction Action(UnitData data) => new AIAction { ActionType = AIActionType.Summon, SummonKind = data.kind,
            SummonDefinition = data, APCost = data.costAP, TargetPos = Vector3.zero };
        public AIStrategicGovernor Governor(bool need = true)
        {
            var governor = new AIStrategicGovernor(); Field(governor, "recruitmentBoard", Board);
            var plan = (List<AIRecruitmentNeed>)typeof(AIStrategicGovernor).GetField("recruitmentPlan", Private).GetValue(governor);
            if (need) plan.Add(new AIRecruitmentNeed(Preferred, "healer", "operation_squad_role_missing"));
            var snapshots = new List<BasicResourceSnapshot>();
            for (int i = 0; i < 8; i++) snapshots.Add(new BasicResourceSnapshot { ResourceType = (ResourceKind)i,
                CurrentStock = i == (int)ResourceKind.Water ? Resources.Water : i == (int)ResourceKind.Citizen ? Resources.Citizen : 500,
                ReserveTarget = i == (int)ResourceKind.Water ? 40 : i == (int)ResourceKind.Citizen ? 3 : 0 });
            governor.BasicResources.EvaluateSnapshots(snapshots, 1);
            return governor;
        }
        public int Priority(AIStrategicGovernor governor, AIAction action) => (int)typeof(AIStrategicGovernor)
            .GetMethod("Priority", Private).Invoke(governor, new object[] { action, Board });
        public void Dispose() { UnityEngine.Object.DestroyImmediate(Root); UnityEngine.Object.DestroyImmediate(Preferred);
            UnityEngine.Object.DestroyImmediate(Alternative); UnityEngine.Object.DestroyImmediate(aura); UnityEngine.Object.DestroyImmediate(heal); }
    }
    static void OrderedPaidRecruitment()
    {
        using (var f = new Fixture())
        {
            var action = f.Action(f.Alternative); var governor = f.Governor();
            Check("preferred-is-actually-unpayable", governor.RecruitmentBlockReason(f.Action(f.Preferred), f.Board) != null);
            Check("same-role-alternative-passes-official-gates-and-enters-P3", governor.RecruitmentBlockReason(action, f.Board) == null
                && governor.RecruitmentScore(action, f.Board) == 30 && f.Priority(governor, action) == 3);
            Property(governor, "Mode", StrategicMode.EconomicRecovery);
            Check("recovery-mode-never-promotes-a-replacement", governor.RecruitmentScore(action, f.Board) == 0);
            Property(governor, "Mode", StrategicMode.EmergencyDefense);
            Check("emergency-mode-never-promotes-a-replacement", governor.RecruitmentScore(action, f.Board) == 0);
            Property(governor, "Mode", StrategicMode.Normal);
            Check("selection-does-not-pay-or-grow-the-order", f.Resources.Water == 150 && f.Resources.Citizen == 5
                && f.Board.EnemyAP == 37 && governor.RecruitmentPlan.Count == 1);
            Check("no-current-need-means-no-priority-promotion", f.Governor(false).RecruitmentScore(action, f.Board) == 0);
            f.Preferred.costWater = 100; governor = f.Governor();
            Check("executable-preferred-definition-retains-its-order", governor.RecruitmentScore(action, f.Board) == 0
                && governor.RecruitmentScore(f.Action(f.Preferred), f.Board) == 30);
            f.Preferred.costWater = 150;
            // R2's four-turn Water upkeep consumes20. Leave35: enough to pay it,
            // but below the explicit40 reserve, so this still isolates the reserve gate.
            f.Alternative.costWater = 115; governor = f.Governor();
            Check("raw-affordability-cannot-bypass-reserve", governor.RecruitmentBlockReason(f.Action(f.Alternative), f.Board) == "basic_resource_reserve_Water"
                && governor.RecruitmentScore(f.Action(f.Alternative), f.Board) == 0);
            f.Alternative.costWater = 100; f.Alternative.upkeepMagic = 1000; governor = f.Governor();
            Check("alternative-upkeep-forecast-must-still-pass", governor.RecruitmentBlockReason(f.Action(f.Alternative), f.Board) == "recruit_upkeep_forecast_fail"
                && governor.RecruitmentScore(f.Action(f.Alternative), f.Board) == 0);
            f.Alternative.upkeepMagic = 0; f.Alternative.costAP = 38; governor = f.Governor();
            Check("alternative-never-bypasses-AP", governor.RecruitmentScore(f.Action(f.Alternative), f.Board) == 0);
            f.Alternative.costAP = 14; f.Alternative.availableToEnemy = false; governor = f.Governor();
            Check("foreign-definition-is-not-an-alternative", governor.RecruitmentScore(f.Action(f.Alternative), f.Board) == 0);
            f.Alternative.availableToEnemy = true; f.Alternative.specialSkill.enabledForPlay = false; governor = f.Governor();
            Check("same-kind-with-no-real-healing-cannot-fill-the-role", governor.RecruitmentScore(f.Action(f.Alternative), f.Board) == 0);
            f.Alternative.kind = Kind.Knight; governor = f.Governor();
            Check("unrelated-frontline-cannot-fill-a-blocked-healer-request", governor.RecruitmentScore(f.Action(f.Alternative), f.Board) == 0);
            f.Alternative.kind = Kind.WhiteMage; f.Alternative.specialSkill.enabledForPlay = true;
            for (int i = 0; i < AIActionGenerator.GetMaxUnitCount(f.Alternative.kind); i++)
            { var actor = new GameObject("existing-healer").AddComponent<Status>(); actor.transform.SetParent(f.Root.transform);
                actor.kind = f.Alternative.kind; actor.team = f.Board.ActorTeam; actor.type = Type.Unit;
                actor.HP = actor.MaxHP = 1; f.Board.AliveEnemyUnits.Add(actor); }
            governor = f.Governor();
            Check("same-role-replacement-still-respects-kind-cap", governor.RecruitmentBlockReason(f.Action(f.Alternative), f.Board) == "recruit_kind_cap"
                && governor.RecruitmentScore(f.Action(f.Alternative), f.Board) == 0);
        }
    }
}
#endif
