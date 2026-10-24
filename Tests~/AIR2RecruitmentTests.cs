#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

/// <summary>Controlled native C/D scenarios. Real paid building, citizen growth and SummonSystem;
/// this is not a complete commander turn or an ordinary-match victory claim.</summary>
public static class AIR2RecruitmentTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string label, bool value)
    {
        if (!value) throw new InvalidOperationException("[AIR2Recruitment] FAIL " + label);
        passed++; Debug.Log("[AIR2Recruitment] PASS " + label);
    }
    public static void Run(GameSystems live, TurnGenerator turn)
    {
        passed = 0;
        string player = JsonUtility.ToJson(live.FactionState.PlayerNation), enemy = JsonUtility.ToJson(live.FactionState.EnemyNation);
        var random = UnityEngine.Random.state;
        using (BoardActorAnimation.SuppressEffects()) foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            HousingToRecruit(live, turn, team);
            SafeSurplus(live, turn, team);
            UnsafeSupply(live, turn, team);
            OrderedPlanAndFog(live, turn, team);
        }
        Check("live nation/AP/resources preserved", player == JsonUtility.ToJson(live.FactionState.PlayerNation)
            && enemy == JsonUtility.ToJson(live.FactionState.EnemyNation));
        Check("gameplay random state preserved", random.Equals(UnityEngine.Random.state));
        Debug.Log("[AIR2Recruitment] ALL PASSED " + passed);
    }

    static void HousingToRecruit(GameSystems live, TurnGenerator turn, Team team)
    {
        using var f = new Fixture(live, turn, team, 6);
        var board = f.Board(); var governor = board.Governor;
        Check(team + " T05 missing healer creates a real ordered citizen request", governor.RecruitmentPlan.Count == 1
            && governor.RecruitmentPlan[0].Definition == f.Healer && governor.RecruitmentCitizenDemand == 6);
        var before = governor.ForecastRecruitment(board);
        Check(team + " T05 existing capacity cannot fund the planned recruit and protected citizens", before.Scheduled == 0
            && before.Reason == "recruit_capacity_shortage");
        var house = f.EconomyFixture.BuildAction(f.House, board);
        var assessment = governor.HousingAssessment(house, board);
        Check(team + " T05 real housing request passes sustainable food/recipe checks", assessment.Needed && assessment.CapacityBlocked);
        var forecast = governor.ForecastRecruitment(board, house);
        Check(team + " T05 forecast schedules the real recruit only after growth", forecast.Scheduled == 1
            && forecast.FirstRecruitTurn > 0 && forecast.Reason == null);
        int citizens = f.Resources.Citizen, wood = f.Resources.Wood, ap = f.State.GetAP(team), cap = f.State.GetCitizenCap(team);
        var candidates = AIActionEvaluator.EvaluateAll(f.Commander.Personality, board, null, TurnStrategy.Balanced);
        var generated = candidates.FirstOrDefault(a => a.ActionType == AIActionType.Build && a.FacilityDefinition == f.House);
        Check(team + " T05 official generator retains the requested housing candidate", generated != null && generated.StrategicPriority == 3);
        var chosen = f.Select(board, new List<AIAction> { generated, new AIAction { ActionType = AIActionType.Wait, Score = 10000 } });
        Check(team + " T05 commander-selected housing is really built and charged", chosen == generated && f.Executor.Execute(chosen, board)
            && f.Resources.Wood == wood - f.House.buildCost.Wood && f.State.GetAP(team) == ap - f.House.buildAP);
        Check(team + " T05 housing adds capacity without inventing citizens", f.State.GetCitizenCap(team) == cap + 8 && f.Resources.Citizen == citizens);
        for (int i = 0; i < 4; i++)
        {
            f.EconomyFixture.Economy.ProcessTurn(team); f.State.GetNation(team).TurnsAlive++;
            Check(team + " T05 real citizen growth " + (i + 1), f.Resources.Citizen == citizens + i + 1);
        }
        f.State.SetAP(team, 40); board = f.Board(24);
        Check(team + " T05 planned recruit becomes legal after real growth", f.Summon.CanSummon(team, f.Healer));
        f.ExecutePlannedRecruit(board, "T05");
        board = f.Board(25);
        Check(team + " T05 completed role request does not create endless housing or recruitment", board.Governor.RecruitmentPlan.Count == 0
            && !board.Governor.HousingAssessment(f.EconomyFixture.BuildAction(f.House, board), board).Needed);
    }

    static void SafeSurplus(GameSystems live, TurnGenerator turn, Team team)
    {
        using var f = new Fixture(live, turn, team, 1);
        var board = f.Board();
        string snapshot = JsonUtility.ToJson(f.Resources);
        var plan = board.Governor.ForecastRecruitment(board);
        Check(team + " T07 safely funded role shortage is forecast without spending", plan.Scheduled == 1 && plan.Reason == null
            && snapshot == JsonUtility.ToJson(f.Resources));
        f.ExecutePlannedRecruit(board, "T07");
        for (int i = 0; i < 5; i++) f.EconomyFixture.Economy.ProcessTurn(team);
        Check(team + " T07 actual upkeep after investment stays paid", f.State.GetNation(team).StarvationCounter == 0
            && f.OwnActors().All(a => a.UpkeepUnpaidTurns == 0));
    }

    static void UnsafeSupply(GameSystems live, TurnGenerator turn, Team team)
    {
        using var f = new Fixture(live, turn, team, 1);
        f.Healer.useAuthoredUpkeep = true; f.Healer.upkeepBread = 1000; GameContentRevision.NotifyChanged();
        var board = f.Board(); var action = new AIAction { ActionType = AIActionType.Summon, SummonKind = Kind.Priest,
            SummonDefinition = f.Healer, APCost = f.Healer.costAP, TargetPos = board.SummonablePositions[0] };
        var forecast = board.Governor.ForecastRecruitment(board);
        Check(team + " T08 surplus cannot hide unaffordable recurring upkeep", forecast.Reason == "recruit_bread_forecast_fail"
            && board.Governor.RecruitmentBlockReason(action, board) == "recruit_bread_forecast_fail");
        int units = f.OwnActors().Count, ap = f.State.GetAP(team); string stock = JsonUtility.ToJson(f.Resources);
        Check(team + " T08 real execution gate preserves resources/AP and army", !f.Executor.Execute(action, board)
            && f.OwnActors().Count == units && ap == f.State.GetAP(team) && stock == JsonUtility.ToJson(f.Resources));
        f.Resources.Bread = 1; board = f.Board(21);
        Check(team + " T06 food recovery precedes unnecessary houses", !board.Governor.HousingAssessment(
            f.EconomyFixture.BuildAction(f.House, board), board).Needed);
    }

    static void OrderedPlanAndFog(GameSystems live, TurnGenerator turn, Team team)
    {
        using var f = new Fixture(live, turn, team, 1);
        foreach (var actor in f.OwnActors())
        { UnitRegistry.Instance?.Unregister(actor); UnityEngine.Object.DestroyImmediate(actor.gameObject); }
        foreach (var definition in f.EconomyFixture.Units.UnitDataMap.Values) definition.costCitizen = 2;
        var board = f.Board(); var order = board.Governor.RecruitmentPlan;
        Check(team + " ordered plan has three distinct missing roles, not three copies of maximum ATK", order.Count == 3
            && order[0].Role == "frontline" && order[1].Role == "scout" && order[2].Role == "ranged");
        var forecast = board.Governor.ForecastRecruitment(board);
        Check(team + " ordered forecast really funds all three purchases with growth between them", forecast.Scheduled == 3
            && forecast.FirstRecruitTurn == 0 && forecast.Reason == null);
        Check(team + " sequential citizen demand does not manufacture simultaneous housing demand", board.Governor.RecruitmentCitizenDemand == 6
            && !board.Governor.HousingAssessment(f.EconomyFixture.BuildAction(f.House, board), board).Needed);
        string kinds = string.Join(",", order.Select(p => p.Definition.kind));
        var foe = f.EconomyFixture.Opponent(live.MapCreate.SetPos[0], 100);
        board = f.Board(21);
        Check(team + " unobserved hostiles cannot manufacture army expansion", !board.AlivePlayerUnits.Contains(foe)
            && string.Join(",", board.Governor.RecruitmentPlan.Select(p => p.Definition.kind)) == kinds);
        foe.transform.position += Vector3.right * 20; board = f.Board(22);
        Check(team + " hidden hostile movement leaves ordered role demand unchanged", string.Join(",",
            board.Governor.RecruitmentPlan.Select(p => p.Definition.kind)) == kinds && board.Governor.RecruitmentPlan.Count <= 3);
    }

    sealed class Fixture : IDisposable
    {
        public readonly EconomyR2Tests.Fixture EconomyFixture;
        readonly GameSystems live;
        readonly GameObject root, neutral;
        readonly MoveGenerator moves;
        readonly VisionGenerator vision;
        readonly TerritorySystem territory;
        readonly TurnGenerator hub;
        readonly Team team;
        readonly AIStrategicGovernor governor = new AIStrategicGovernor();
        public readonly UnitData Healer;
        public readonly FacilityDefinitionData House;
        public readonly AICommander Commander;
        public readonly AIActionExecutor Executor;
        public readonly SummonSystem Summon;
        public FactionState State => EconomyFixture.State;
        public FactionState.ResourceData Resources => EconomyFixture.Resources;
        public Fixture(GameSystems live, TurnGenerator turn, Team team, int healerCitizens)
        {
            this.live = live; this.team = team;
            EconomyFixture = new EconomyR2Tests.Fixture(live, team);
            root = new GameObject("R2 recruitment isolated hub"); root.SetActive(false);
            neutral = new GameObject("R2 recruitment empty neutral parent"); neutral.transform.SetParent(root.transform);
            EconomyFixture.Rules.aiEconomy.plannedDemandWeight = 0;
            EconomyFixture.Food(100);
            EconomyFixture.Building(EconomyFixture.Definition(FacilityKind.LoggingCamp, wood: 30));
            EconomyFixture.Building(EconomyFixture.Definition(FacilityKind.Quarry, stone: 30));
            EconomyFixture.Building(EconomyFixture.Definition(FacilityKind.Mine, iron: 30, magic: 30));
            House = EconomyFixture.Definition(FacilityKind.House); var recipe = House.levels[0]; recipe.SpecialValue = 8; House.levels[0] = recipe;
            House.buildCost = new FacilityData.ResourceCost(wood: 30, stone: 20); House.buildAP = 2;
            EconomyFixture.Units.UnitDataMap.Clear();
            AddDefinition(Kind.Knight, 1, 7); AddDefinition(Kind.Archer, 1, 7); AddDefinition(Kind.Scout, 1, 7);
            Healer = AddDefinition(Kind.Priest, healerCitizens, 14);
            for (int i = 0; i < 3; i++)
            {
                var actor = EconomyFixture.Unit(); actor.kind = i == 2 ? Kind.Archer : Kind.Knight;
                actor.GrowthData = EconomyFixture.Units.UnitDataMap[actor.kind]; actor.HP = 50; actor.MaxHP = 100; actor.HasMovedThisTurn = true;
            }
            var scout = EconomyFixture.Unit(); scout.kind = Kind.Scout; scout.GrowthData = EconomyFixture.Units.UnitDataMap[Kind.Scout]; scout.HasMovedThisTurn = true;
            hub = root.AddComponent<TurnGenerator>(); hub.MarkDeveloperMatch();
            moves = root.AddComponent<MoveGenerator>(); moves.mapcreate = live.MapCreate; moves.turnGenerator = hub; moves.BuildSystemRef = EconomyFixture.Builder;
            Set(moves, "crystalsystem", EconomyFixture.Crystals); Set(moves, "unitsetting", EconomyFixture.Units);
            Set(moves, "PlayerUnit", EconomyFixture.Units.PlayerUnit); Set(moves, "EnemyUnit", EconomyFixture.Units.EnemyUnit); moves.NeutralParent = neutral.transform;
            territory = root.AddComponent<TerritorySystem>(); territory.PTSetPos = new List<Vector3>(); territory.ETSetPos = new List<Vector3>();
            foreach (var cell in live.MapCreate.SetPos.Take(20)) territory.GetTerritory(team).Add(cell);
            vision = root.AddComponent<VisionGenerator>();
            Set(vision, "PlayerUnit", EconomyFixture.Units.PlayerUnit); Set(vision, "EnemyUnit", EconomyFixture.Units.EnemyUnit);
            Set(vision, "territorysystem", territory); vision.NeutralParent = neutral.transform;
            foreach (var field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" }) Set(vision, field, new HashSet<Vector3Int>());
            hub.Systems.MapCreate = live.MapCreate; hub.Systems.MoveGenerator = moves; hub.Systems.UnitSetting = EconomyFixture.Units;
            hub.Systems.CrystalSystem = EconomyFixture.Crystals; hub.Systems.BuildSystem = EconomyFixture.Builder;
            hub.Systems.FactionState = State; hub.Systems.APSystem = EconomyFixture.AP; hub.Systems.VisionGenerator = vision;
            Summon = root.AddComponent<SummonSystem>(); Summon.Init(hub, territory, EconomyFixture.AP, State, moves, live.MapCreate, EconomyFixture.Units, vision);
            hub.Systems.SummonSystem = Summon;
            Commander = new AICommander(hub, moves, live.AttackGenerator, live.BattleSystem, vision, EconomyFixture.AP,
                EconomyFixture.Units, EconomyFixture.Crystals, live.MapCreate, MajorPersonality.Growth, EconomyFixture.Builder,
                Summon, State, live.SkillSystem, null, 30, 64517, team);
            Executor = new AIActionExecutor(hub, moves, live.AttackGenerator, live.BattleSystem, EconomyFixture.AP,
                live.SkillSystem, null, EconomyFixture.Builder, Summon, null, team);
        }
        UnitData AddDefinition(Kind kind, int citizens, int ap)
        {
            var data = EconomyFixture.UnitDefinition(); data.kind = kind; data.baseATK = 10;
            data.costCitizen = citizens; data.costAP = ap; data.costWood = 30; data.costStone = 20; data.costBread = 8;
            EconomyFixture.Units.UnitDataMap[kind] = data; return data;
        }
        public AIBoardState Board(int turn = 20)
        {
            var board = new AIBoardState(moves, live.AttackGenerator, EconomyFixture.AP, EconomyFixture.Units,
                EconomyFixture.Crystals, vision, EconomyFixture.Builder, Summon, State, null, turn, null, team);
            board.MapCreate = live.MapCreate; board.ReconThreatLevel = 30; board.Governor = governor;
            governor.Evaluate(board); return board;
        }
        public List<Status> OwnActors() => (team == Team.Player ? EconomyFixture.Units.PlayerUnit : EconomyFixture.Units.EnemyUnit)
            .GetComponentsInChildren<Status>().Where(a => a.IsAlive).ToList();
        public AIAction Select(AIBoardState board, List<AIAction> actions)
        {
            Set(Commander, "_board", board);
            return (AIAction)typeof(AICommander).GetMethod("SelectBestAction", Hidden).Invoke(Commander,
                new object[] { actions, new HashSet<string>(), null });
        }
        public void ExecutePlannedRecruit(AIBoardState board, string phase)
        {
            var candidates = AIActionEvaluator.EvaluateAll(Commander.Personality, board, null, TurnStrategy.Balanced);
            var planned = candidates.FirstOrDefault(a => a.ActionType == AIActionType.Summon && a.SummonDefinition == Healer);
            Check(team + " " + phase + " actual generated missing-role summon reaches P3", planned != null && planned.StrategicPriority == 3);
            var chosen = Select(board, candidates);
            if (chosen != planned && chosen?.SummonDefinition != Healer)
                Debug.Log("[AIR2RecruitmentDebug] selected=" + chosen?.ActionType + " kind=" + chosen?.SummonKind
                    + " score=" + chosen?.Score + " P=" + chosen?.StrategicPriority + " planned=" + planned?.Score
                    + " mode=" + board.Governor.Mode + " reason=" + board.Governor.RecruitmentBlockReason(planned, board));
            int citizens = Resources.Citizen, wood = Resources.Wood, bread = Resources.Bread, ap = State.GetAP(team), count = OwnActors().Count;
            Check(team + " " + phase + " real commander selects the planned missing role", chosen?.SummonDefinition == Healer);
            Check(team + " " + phase + " real executor pays and creates the required unit", Executor.Execute(chosen, board)
                && OwnActors().Count == count + 1 && OwnActors().Any(a => a.kind == Kind.Priest && a.GrowthData == Healer)
                && Resources.Citizen == citizens - Healer.costCitizen && Resources.Wood == wood - Healer.costWood
                && Resources.Bread == bread - Healer.costBread && State.GetAP(team) == ap - Healer.costAP);
        }
        public void Dispose()
        {
            Commander?.ReleaseRuntimeReferences();
            UnityEngine.Object.DestroyImmediate(root); EconomyFixture.Dispose();
        }
    }
    static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
}
#endif
