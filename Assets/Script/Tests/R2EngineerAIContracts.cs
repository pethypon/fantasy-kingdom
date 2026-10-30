#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Controlled live-board AI contracts; test-owned definitions are not production balance choices.</summary>
public static class R2EngineerAIContracts
{
    static void Check(string label, bool ok)
    { if(!ok)throw new InvalidOperationException("[R2EngineerAI] FAIL "+label); Debug.Log("[R2EngineerAI] PASS "+label); }
    public static void Run(GameSystems live)
    {
        using var effects=BoardActorAnimation.SuppressEffects();
        foreach(var team in new[]{Team.Player,Team.Enemy})
        {
            EngineerRecruitmentBootstrap(live, team);
            using var f=new R2ConstructionEconomyTests.Fixture(live,team);
            f.Resources.Iron=0;f.Resources.MagicOre=10;
            var board=f.Board();var position=f.Sites[1];
            var forecast = new StrategicEconomyForecast { ForecastTurns = 1 };
            var unpaid = forecast.Simulate(board);
            Check(team+" UP10 forecast predicts20HP unpaid loss without altering live state",
                unpaid.ExpectedUnitHPDamage==20&&unpaid.ExpectedUnitDeaths==0&&f.Engineer.HP==100&&f.Resources.Iron==0);
            f.Resources.Iron=10;
            var action=new AIAction{ActionType=AIActionType.Build,Unit=f.Engineer,Facility=FacilityKind.Mine,
                FacilityDefinition=f.Mine,TargetPos=position,APCost=f.Builder.GetEngineerBuildAP(f.Mine)};
            string label=team+" ";
            Check(label+"D03 fixed maintenance creates a real outside-ore supply gap",AIActionGenerator.HasRareOreSupplyGap(board));
            Check(label+"D04 EN18 planned engineer price equals the live quote",AIConstructionEconomy.BuildCost(action).Wood==140
                &&AIConstructionEconomy.BuildCost(action).Citizen==1&&action.APCost==3);
            Check(label+"MN11 neutral personally visible candidate predicts ore",AIConstructionEconomy.BuildRecipe(action,board).Output.Iron==3);
            f.Territory.GetTerritory(team).Add(position);
            Check(label+"MN11 own territory candidate cannot promise ore",AIConstructionEconomy.BuildRecipe(action,board).Output.Iron==0);
            f.Territory.GetTerritory(team).Clear();
            f.Engineer.VisionCell.Remove(position);
            Check(label+"EN05 shared board sight alone cannot promise remote mining",AIConstructionEconomy.BuildRecipe(action,board).Output.Iron==0);
            board=f.Board();
            AITurnBudget.Begin(5000);
            var generated=new List<AIAction>();
            typeof(AIActionGenerator).GetMethod("AddEngineerFacilityCandidates",BindingFlags.Static|BindingFlags.NonPublic)
                .Invoke(null,new object[]{f.Engineer,board,generated,FacilityKind.Mine,f.Mine,0});
            Check(label+"EN18 legal demanded engineer mine survives real generation",generated.Count>0&&generated.Count<=2);
            foreach(var candidate in generated)
                Check(label+"EN18 generated cells pass the shared placement validator",f.Builder.GetEngineerPlacementFailure(f.Engineer,
                    GridHelper.ToGrid(candidate.TargetPos),f.Mine)==null);
            int wood=f.Resources.Wood,stone=f.Resources.Stone,ap=f.State.GetAP(team);
            var executor=new AIActionExecutor(f.Turn,f.Moves,live.AttackGenerator,live.BattleSystem,f.AP,
                live.SkillSystem,null,f.Builder,null,null,team);
            Check(label+"EN18 AI executor pays the same formal command",executor.Execute(action,board)
                &&f.Resources.Wood==wood-140&&f.Resources.Stone==stone-220&&f.State.GetAP(team)==ap-3);
            var building=f.Builder.GetLastPlacedBuilding()?.GetComponent<Status>();
            Check(label+"MN11 owned outside mine forecast matches actual recipe",building!=null
                &&AIConstructionEconomy.ExistingRecipe(building,1,board).Output.Iron==FacilityData.GetProductionLevel(building,1,f.Territory).Output.Iron);
            f.Territory.GetTerritory(team).Add(position);
            Check(label+"MN09 MN11 owned mine forecast changes with territory",AIConstructionEconomy.ExistingRecipe(building,1,board).Output.Iron==0);
            Check(label+"EN10 duplicate AI command never charges twice",!executor.Execute(action,board)&&f.Resources.Wood==wood-140);
        }
        Debug.Log("[R2EngineerAI] COMPLETE");
    }

    static void EngineerRecruitmentBootstrap(GameSystems live, Team team)
    {
        using var f = new R2ConstructionEconomyTests.Fixture(live, team);
        var engineer = RecruitDefinition(Kind.Engineer);
        var ordinary = RecruitDefinition(Kind.Archer);
        try
        {
            // The existing military unit already misses its iron upkeep. Test-owned support
            // stats create no production balance decision and do not erase the inherited loss.
            var military = f.Engineer;
            military.kind = military.GrowthData.kind = Kind.Knight;
            military.GrowthData.useAuthoredUpkeep = true;
            military.GrowthData.upkeepIron = 1;
            military.GrowthData.upkeepBread = 0;
            military.GrowthData.upkeepWater = 0;
            engineer.actionProfile = military.GrowthData.actionProfile;
            f.Units.UnitDataMap.Clear();
            f.Units.UnitDataMap[Kind.Knight] = military.GrowthData;
            f.Units.UnitDataMap[Kind.Engineer] = engineer;
            f.Units.UnitDataMap[Kind.Archer] = ordinary;
            f.Resources.Iron = 0;
            f.Territory.GetTerritory(team).Add(f.Sites[1]);
            var host = new GameObject("R2 engineer recruit command fixture");
            host.transform.SetParent(f.Builder.transform.parent);
            var summon = host.AddComponent<SummonSystem>();
            summon.Init(f.Turn, f.Territory, f.AP, f.State, f.Moves, live.MapCreate, f.Units, f.Vision);
            var board = f.Board();
            typeof(AIBoardState).GetField("_summonSystem", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(board, summon);
            board.Refresh();
            var action = RecruitAction(engineer, f.Sites[1]);
            var governor = NewGovernor(board);
            var forecast = new StrategicEconomyForecast();
            var baseline = forecast.Simulate(board);
            var withEngineer = forecast.Simulate(board, action);
            string label = team + " ";
            Check(label + "EN19 existing military has an inherited ore payment failure",
                baseline.UpkeepPaymentFailed && baseline.ExpectedUnitHPDamage > 0
                && governor.Mode == StrategicMode.EconomicRecovery && AIActionGenerator.HasRareOreSupplyGap(board));
            Check(label + "EN19 affordable engineer adds no unpaid HP damage or deaths",
                board.CanSummonDefinition(engineer) && withEngineer.UpkeepPaymentFailed
                && withEngineer.ExpectedUnitHPDamage == baseline.ExpectedUnitHPDamage
                && withEngineer.ExpectedUnitDeaths == baseline.ExpectedUnitDeaths);
            string stock = JsonUtility.ToJson(f.Resources); int ap = f.State.GetAP(team);
            var candidates = new List<AIAction> { action };
            governor.Filter(candidates, board);
            Check(label + "EN19 real governor filter and execution gate admit the recovery engineer",
                candidates.Count == 1 && action.StrategicPriority == 2 && governor.AllowExecution(action, board)
                && governor.RecruitmentBlockReason(action, board) == null && governor.RecruitmentScore(action, board) == 30);
            Check(label + "EN19 planning never pays or forgives the military loss",
                stock == JsonUtility.ToJson(f.Resources) && ap == f.State.GetAP(team) && military.HP == 100);

            var fighter = RecruitAction(ordinary, f.Sites[1]);
            candidates = new List<AIAction> { fighter };
            governor.Filter(candidates, board);
            Check(label + "EN20 ordinary recruits retain the unpaid-upkeep veto",
                candidates.Count == 0 && fighter.StrategicRejectReason == "upkeep_forecast_fail"
                && governor.RecruitmentBlockReason(fighter, board) == "recruit_upkeep_forecast_fail");

            engineer.upkeepIron = 100;
            board.Refresh(); governor = NewGovernor(board); action = RecruitAction(engineer, f.Sites[1]);
            candidates = new List<AIAction> { action };
            governor.Filter(candidates, board);
            Check(label + "EN21 added unaffordable engineer upkeep retains the veto",
                candidates.Count == 0 && action.StrategicRejectReason == "upkeep_forecast_fail"
                && governor.RecruitmentBlockReason(action, board) == "recruit_upkeep_forecast_fail");

            engineer.upkeepIron = 0;
            // Global extra upkeep still applies to both units. Leave enough Water for
            // every forecast payment, but less than the selected purchase's capital reserve.
            f.Resources.Water = 200; engineer.costWater = 150;
            board.Refresh(); governor = NewGovernor(board); action = RecruitAction(engineer, f.Sites[1]);
            forecast = new StrategicEconomyForecast();
            baseline = forecast.Simulate(board); withEngineer = forecast.Simulate(board, action);
            Check(label + "EN22 reserve probe is affordable and leaves inherited ore losses unchanged"
                + " baseline=" + baseline.MandatoryFailureMask + "/" + baseline.ExpectedUnitHPDamage + "/" + baseline.ExpectedUnitDeaths
                + " proposed=" + withEngineer.MandatoryFailureMask + "/" + withEngineer.ExpectedUnitHPDamage + "/" + withEngineer.ExpectedUnitDeaths,
                board.CanSummonDefinition(engineer) && f.Resources.Water - engineer.costWater == 50
                && withEngineer.MandatoryFailureMask == baseline.MandatoryFailureMask
                && withEngineer.ExpectedUnitHPDamage == baseline.ExpectedUnitHPDamage
                && withEngineer.ExpectedUnitDeaths == baseline.ExpectedUnitDeaths);
            bool maySpend = governor.BasicResources.AllowSpend(action, false, out string reserveReason);
            bool protectsWater = reserveReason == "basic_resource_reserve_Water"
                || reserveReason != null && reserveReason.StartsWith("mandatory_supply_capital_", StringComparison.Ordinal)
                    && reserveReason.EndsWith("_Water", StringComparison.Ordinal);
            bool allowed = governor.AllowExecution(action, board);
            Check(label + "EN22 engineer recovery retains ordinary material reserves spend=" + reserveReason
                + " execution=" + action.StrategicRejectReason, !maySpend && protectsWater && !allowed);
            engineer.costWater = 10; engineer.costAP = f.State.GetAP(team) + 1;
            board.Refresh(); governor = NewGovernor(board); action = RecruitAction(engineer, f.Sites[1]);
            Check(label + "EN23 engineer recovery cannot bypass AP legality",
                !governor.AllowExecution(action, board) && governor.RecruitmentBlockReason(action, board) == "recruit_ap_shortage");
            engineer.costAP = 3; engineer.costCitizen = f.Resources.Citizen + 1;
            board.Refresh(); governor = NewGovernor(board); action = RecruitAction(engineer, f.Sites[1]);
            Check(label + "EN24 engineer recovery cannot bypass citizen legality",
                !board.CanSummonDefinition(engineer) && !governor.AllowExecution(action, board)
                && governor.RecruitmentBlockReason(action, board) == "recruit_citizen_shortage");
            engineer.costCitizen = 1; engineer.costIron = 1;
            board.Refresh(); governor = NewGovernor(board); action = RecruitAction(engineer, f.Sites[1]);
            Check(label + "EN25 engineer recovery cannot bypass actual ore purchase cost",
                !board.CanSummonDefinition(engineer) && !governor.AllowExecution(action, board)
                && governor.RecruitmentBlockReason(action, board) == "recruit_resources_or_legality");
        }
        finally { UnityEngine.Object.DestroyImmediate(engineer); UnityEngine.Object.DestroyImmediate(ordinary); }
    }

    static UnitData RecruitDefinition(Kind kind)
    {
        var definition = ScriptableObject.CreateInstance<UnitData>();
        definition.kind = kind; definition.baseHP = 100; definition.baseATK = 10;
        definition.useAuthoredUpkeep = true;
        definition.costWood = definition.costStone = definition.costWater = 10;
        definition.costBread = 5; definition.costCitizen = 1; definition.costAP = 3;
        return definition;
    }
    static AIAction RecruitAction(UnitData definition, Vector3Int position)
        => new AIAction { ActionType = AIActionType.Summon, SummonKind = definition.kind,
            SummonDefinition = definition, TargetPos = position, APCost = definition.costAP };
    static AIStrategicGovernor NewGovernor(AIBoardState board)
    {
        var governor = new AIStrategicGovernor(); board.Governor = governor;
        AITurnBudget.Begin(5000); governor.Evaluate(board); return governor;
    }
}
#endif
