#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Current-rule Play Mode contracts: ordinary paid home mines, actual
/// production and retirement of engineer commands. Not a campaign balance result.</summary>
public static class OrdinaryMineValidation
{
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[OrdinaryMine] FAIL " + name); Debug.Log("[OrdinaryMine] PASS " + name); }
    public static void Run(GameSystems live)
    {
        R2ConstructionEconomyTests.Run(live);
        Check("engineer-absent-from-production-roster", Array.IndexOf(UnitRoster.SummonableKinds, Kind.Engineer) < 0);
        Check("home-mine-permitted-regardless-of-legacy-setting", !R2EconomyRules.DisallowNewHomeMines);
        Check("retired-definition-not-in-shared-catalog", UnitAuthoringCatalog.Load()?.GetById("r2.unit.engineer") == null);
        foreach (var side in new[] { Team.Player, Team.Enemy })
        {
            using var f = new R2ConstructionEconomyTests.Fixture(live, side);
            string prefix = side + " ";
            var site = f.Sites[1]; f.Territory.GetTerritory(side).Add(site);
            var stockBefore = JsonUtility.ToJson(f.Resources); int apBefore = f.State.GetAP(side);
            Check(prefix + "engineer-definition-unavailable", !f.Engineer.GrowthData.AvailableFor(side) && !f.Engineer.GrowthData.IsValidForAuthoring);
            Check(prefix + "old-engineer-order-rejected-without-payment", !f.Builder.TryEngineerPlace(f.Engineer, site, f.Mine)
                && JsonUtility.ToJson(f.Resources) == stockBefore && f.State.GetAP(side) == apBefore);
            Check(prefix + "old-engineer-cannot-be-respawned", live.SummonSystem.SpawnUnitForLoad(Kind.Engineer, side, site) == null);
            var cost = f.Mine.GetInfo().BuildCost;
            Check(prefix + "ordinary-home-mine-paid-placement", f.Builder.TryPlaceDefinition(site, f.Mine, side));
            Check(prefix + "normal-material-and-AP-cost", f.Resources.Wood == 5000 - cost.Wood
                && f.Resources.Stone == 5000 - cost.Stone && f.State.GetAP(side) == apBefore - f.Mine.GetInfo().APCost);
            var building = f.Builder.GetLastPlacedBuilding()?.GetComponent<Status>();
            var board = f.Board(); board.TerritorySystem = f.Territory;
            var recipe = FacilityData.GetProductionLevel(building, 1, f.Territory);
            var action = new AIAction { ActionType = AIActionType.Build, Facility = FacilityKind.Mine, FacilityDefinition = f.Mine, TargetPos = site };
            Check(prefix + "home-iron-and-magic-recipe-preserved", recipe.Output.Iron == 3
                && recipe.BonusOutput1.MagicOre == 1 && Mathf.Approximately(recipe.BonusChance1, .35f));
            Check(prefix + "AI-planned-production-matches-actual", AIConstructionEconomy.BuildRecipe(action, board).Output.Iron == recipe.Output.Iron
                && AIConstructionEconomy.ExistingRecipe(building, 1, board).BonusOutput1.MagicOre == recipe.BonusOutput1.MagicOre);
            f.Resources.Iron = 0; f.Economy.ProcessTurn(side);
            Check(prefix + "actual-economic-turn-produces-home-iron", f.Resources.Iron == 3);
            f.Territory.GetTerritory(side).Clear(); f.Resources.Iron = 0; f.Economy.ProcessTurn(side);
            Check(prefix + "existing-mine-retained-after-territory-change", building.IsAlive && f.Resources.Iron == 3);
            var candidates = new List<AIAction>(); AIActionGenerator.GenerateEngineerBuildCandidates(board, candidates);
            Check(prefix + "no-engineer-AI-construction-or-recruitment", candidates.Count == 0 && !AIActionGenerator.HasRareOreSupplyGap(board));
        }
        Debug.Log("[OrdinaryMine] COMPLETE");
    }
}
#endif
