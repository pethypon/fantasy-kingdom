using System.Collections.Generic;
using UnityEngine;

/// <summary>Own-economy forecasts in common resource-value units. No player resource balance is queried.</summary>
public sealed class AIInvestmentPlanner
{
    readonly List<Status> buildings = new List<Status>();
    readonly float[] production = new float[8], required = new float[8], stock = new float[8], price = new float[8];
    readonly Dictionary<Kind,int> availableUnits=new Dictionary<Kind,int>();
    int generation = -1, turn = -1;
    AIPlan plan;
    public int EstimatedRemainingTurns { get; private set; }
    public float NeededIron => required[2];
    public float NeededBread => required[5];
    public float NeededWater => required[6];

    public void Prepare(AIBoardState board, AIPlan objective)
    {
        if (generation == board.Generation && turn == board.TurnCount && plan == objective) return;
        generation = board.Generation; turn = board.TurnCount; plan = objective;
        System.Array.Clear(production, 0, 8); System.Array.Clear(required, 0, 8);
        EstimatedRemainingTurns = AIPlanManager.Emergency(board) ? 4
            : board.PlayerCrystalVisible && board.PlayerCrystalHP < board.EnemyCrystalMaxHP * .25f ? 6
            : Mathf.Clamp(24 - board.TurnCount / 4, 8, 24);
        var r = board.EnemyResources;
        if (r == null) { System.Array.Clear(stock, 0, 8); return; }
        stock[0]=r.Wood; stock[1]=r.Stone; stock[2]=r.Iron; stock[3]=r.MagicOre;
        stock[4]=r.Wheat; stock[5]=r.Bread; stock[6]=r.Water; stock[7]=r.Citizen;
        board.CollectOwnBuildings(buildings);
        foreach (var building in buildings)
        {
            if (building == null || !building.IsAlive) continue;
            Add(production, FacilityData.GetLevel(building.facilityKind, building.Level), 1);
        }
        // The operation specifies the future army. Without one, maintain a two-unit combat reserve.
        if (objective != null && objective.Active && objective.RequiredUnits != null)
        {
            var available = availableUnits;available.Clear();
            foreach (var unit in board.AliveEnemyUnits)
                if (unit != null && unit.IsAlive && unit.type == Type.Unit)
                { available.TryGetValue(unit.kind, out var count); available[unit.kind] = count + 1; }
            foreach (var kind in objective.RequiredUnits)
            {
                available.TryGetValue(kind, out int count);
                if (count > 0) { available[kind] = count - 1; continue; }
                AddUnitRequirement(kind);
            }
        }
        else { AddUnitRequirement(Kind.Knight); AddUnitRequirement(Kind.Bomber); }
        for (int i = 0; i < 8; i++)
        {
            required[i] = Mathf.Max(0, required[i] - stock[i] - Mathf.Max(0, production[i]) * 5);
            float baseline = i == 3 ? 4 : i == 2 || i == 5 ? 2 : i == 7 ? 8 : 1;
            price[i] = baseline * (1 + Mathf.Min(2, required[i] / 20));
        }
        // Planned Bread requires upstream Wheat and Water as well as a Bakery.
        if (required[5] > 0) { price[4] = Mathf.Max(price[4], 2); price[6] = Mathf.Max(price[6], 2); }
    }

    void AddUnitRequirement(Kind kind)
    {
        if (!UnitStaticData.Table.TryGetValue(kind, out var u)) return;
        required[0]+=u.CostWood; required[1]+=u.CostStone; required[2]+=u.CostIron; required[3]+=u.CostMagicOre;
        required[5]+=u.CostBread; required[6]+=u.CostWater; required[7]+=u.CostCitizen;
    }
    static void Add(float[] values, FacilityData.ProductionBundle b, float scale)
    { values[0]+=b.Wood*scale; values[1]+=b.Stone*scale; values[2]+=b.Iron*scale; values[3]+=b.MagicOre*scale;
      values[4]+=b.Wheat*scale; values[5]+=b.Bread*scale; values[6]+=b.Water*scale; values[7]+=b.Citizen*scale; }
    static void Add(float[] values, FacilityData.FacilityLevelData d, float scale)
    { Add(values,d.Output,scale); Add(values,d.Input,-scale); Add(values,d.BonusOutput1,scale*d.BonusChance1); Add(values,d.BonusOutput2,scale*d.BonusChance2); }

    // Reused evaluation buffer, so candidate evaluation does not allocate.
    readonly float[] delta = new float[8];
    public float PaybackTurns(FacilityKind facility)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info)) return float.PositiveInfinity;
        System.Array.Clear(delta, 0, 8); Add(delta, FacilityData.GetLevel(facility, 1), 1);
        float income = 0; for (int i=0;i<8;i++) income += delta[i] * price[i];
        var c=info.BuildCost;
        float cost=c.Wood*price[0]+c.Stone*price[1]+c.Iron*price[2]+c.MagicOre*price[3]+c.Water*price[6]+c.Citizen*price[7]+info.APCost*2;
        return income > .01f ? cost / income : float.PositiveInfinity;
    }
    public float Bonus(AIAction action, AIBoardState board)
    {
        if (board.ReconThreatLevel < 10 || action.ActionType != AIActionType.Build) return 0;
        var level=FacilityData.GetLevel(action.Facility,1);
        if (!level.HasProduction) return 0;
        float payback=PaybackTurns(action.Facility);
        float score=float.IsInfinity(payback) ? -15 : Mathf.Clamp(EstimatedRemainingTurns-payback,-20,12);
        // Invest to cover the projected five-turn recruitment deficit.
        System.Array.Clear(delta,0,8); Add(delta,level.Output,1);
        for(int i=0;i<8;i++) if(required[i]>0 && delta[i]>0) score += Mathf.Min(18, required[i] / Mathf.Max(1, delta[i]) * 3);
        if (required[5]>0 && (action.Facility==FacilityKind.Field || action.Facility==FacilityKind.Well)
            && production[action.Facility==FacilityKind.Field ? 4 : 6]<=0) score+=20;
        // A processing plant cannot realize its advertised return without the inputs.
        System.Array.Clear(delta,0,8); Add(delta,level.Input,1);
        for(int i=0;i<8;i++) if(delta[i]>0 && stock[i]<delta[i] && production[i]<=0) score-=18;
        return Mathf.Clamp(score,-40,40);
    }
}
