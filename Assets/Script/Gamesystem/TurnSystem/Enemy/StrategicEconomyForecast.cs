using System;
using System.Collections.Generic;
using UnityEngine;

public enum EconomicState { Healthy, Warning, Crisis, Collapse }
public readonly struct EconomyForecastResult
{
    public readonly EconomicState State;
    public readonly float NetBreadPerTurn, BreadCoverageTurns, MinimumBread;
    public readonly bool BreadPaymentFailed, UpkeepPaymentFailed, ImportantProductionStopped;
    public EconomyForecastResult(EconomicState state,float net,float coverage,float minimum,bool bread,bool upkeep,bool stopped)
    {State=state;NetBreadPerTurn=net;BreadCoverageTurns=coverage;MinimumBread=minimum;BreadPaymentFailed=bread;UpkeepPaymentFailed=upkeep;ImportantProductionStopped=stopped;}
}

/// <summary>Own-side, conservative, bounded forecast. Failed payments are explicit even though stocks never go negative.</summary>
public sealed class StrategicEconomyForecast
{
    readonly List<Status> buildings=new List<Status>(32);
    readonly double[] stock=new double[8];
    readonly List<FacilityData.FacilityLevelData> recipes=new List<FacilityData.FacilityLevelData>(32);
    readonly List<FacilityData.ProductionBundle> upkeeps=new List<FacilityData.ProductionBundle>(32);
    AIBoardState preparedBoard;int generation=-1;
    public int ReserveTurns=3, ForecastTurns=3;
    public void Prepare(AIBoardState board)
    {
        if(preparedBoard==board&&generation==board.Generation)return;
        preparedBoard=board;generation=board.Generation;recipes.Clear();upkeeps.Clear();board.CollectOwnBuildings(buildings);
        foreach(var actor in buildings)if(actor!=null&&actor.IsAlive)recipes.Add(FacilityData.GetLevel(actor,actor.Level));
        foreach(var actor in board.AliveEnemyUnits)
        {
            if(actor==null||!actor.IsAlive||actor.type!=Type.Unit)continue;
            var definition=actor.GrowthData??board.ResolveUnitDefinition(actor.kind);
            if(definition!=null)upkeeps.Add(definition.GetUpkeep(actor.Level));
        }
    }
    public EconomyForecastResult Simulate(AIBoardState board,AIAction candidate=null)
    {
        Prepare(board);var resources=board.EnemyResources;
        if(resources==null)return new EconomyForecastResult(EconomicState.Healthy,0,float.PositiveInfinity,0,false,false,false);
        Read(resources,stock);int citizens=Mathf.Max(0,resources.Citizen);
        UnitData additionalUnit=null;FacilityData.FacilityLevelData additionalRecipe=default;bool hasRecipe=false;
        if(candidate?.ActionType==AIActionType.Summon)
        {
            additionalUnit=candidate.SummonDefinition??board.ResolveUnitDefinition(candidate.SummonKind);
            if(additionalUnit!=null)
            { stock[0]-=additionalUnit.costWood;stock[1]-=additionalUnit.costStone;stock[2]-=additionalUnit.costIron;stock[3]-=additionalUnit.costMagic;
              stock[5]-=additionalUnit.costBread;stock[6]-=additionalUnit.costWater;citizens=Mathf.Max(0,citizens-additionalUnit.costCitizen); }
        }
        if(candidate?.ActionType==AIActionType.Build)
        {
            var info=candidate.FacilityDefinition!=null?candidate.FacilityDefinition.GetInfo():FacilityData.Table[candidate.Facility];
            var cost=info.BuildCost;stock[0]-=cost.Wood;stock[1]-=cost.Stone;stock[2]-=cost.Iron;stock[3]-=cost.MagicOre;stock[6]-=cost.Water;citizens=Mathf.Max(0,citizens-cost.Citizen);
            additionalRecipe=candidate.FacilityDefinition!=null?candidate.FacilityDefinition.GetLevel(1):FacilityData.GetLevel(candidate.Facility,1);hasRecipe=true;
        }
        stock[7]=citizens;
        bool breadFail=stock[5]<0,upkeepFail=false,stopped=false,failedFirstTurn=false;
        double initialBread=Math.Max(0,stock[5]),minimum=initialBread;
        int turns=Mathf.Clamp(ForecastTurns,1,8),capacity=board.CitizenCapacity;
        if(hasRecipe&&candidate!=null&&(candidate.Facility==FacilityKind.House||candidate.Facility==FacilityKind.LuxuryHouse))capacity+=additionalRecipe.SpecialValue;
        for(int t=0;t<turns;t++)
        {
            if(board.EnemyCrystalHP>0)
            {
                float income=Mathf.Clamp01(1-(board.NationTurnsAlive+t)*.1f);
                stock[0]+=Mathf.RoundToInt(20*income);stock[1]+=Mathf.RoundToInt(20*income);stock[2]+=Mathf.RoundToInt(5*income);
                stock[5]+=Mathf.RoundToInt(10*income);stock[6]+=Mathf.RoundToInt(10*income);
            }
            foreach(var recipe in recipes)Process(recipe,ref breadFail,ref upkeepFail,ref stopped);
            if(hasRecipe)Process(additionalRecipe,ref breadFail,ref upkeepFail,ref stopped);
            citizens=(int)Math.Min(int.MaxValue,Math.Max(0,stock[7]));
            if(stock[5]>0&&citizens<capacity){stock[5]-=1;citizens++;stock[7]=citizens;}
            foreach(var upkeep in upkeeps)Pay(upkeep,ref breadFail,ref upkeepFail);
            if(additionalUnit!=null)Pay(additionalUnit.GetUpkeep(1),ref breadFail,ref upkeepFail);
            double food=stock[7]*EconomySystem.CitizenBreadCost;
            if(stock[5]<food){breadFail=true;stock[5]=0;}else stock[5]-=food;
            minimum=Math.Min(minimum,stock[5]);if(t==0)failedFirstTurn=breadFail||upkeepFail||stopped;
        }
        float net=(float)((stock[5]-initialBread)/turns);
        // Exhausted bread does not conceal the unfulfilled mandatory consumption.
        float coverage=net<0?(float)(initialBread/Math.Max(.001,-net)):float.PositiveInfinity;
        if(breadFail)coverage=Mathf.Min(coverage,turns-1);
        EconomicState state=failedFirstTurn?EconomicState.Collapse:breadFail||upkeepFail||stopped?EconomicState.Crisis
            :coverage<Mathf.Clamp(ReserveTurns,1,8)+1?EconomicState.Warning:EconomicState.Healthy;
        return new EconomyForecastResult(state,net,coverage,(float)minimum,breadFail,upkeepFail,stopped);
    }
    void Process(FacilityData.FacilityLevelData recipe,ref bool breadFail,ref bool upkeepFail,ref bool stopped)
    {
        if(!CanPay(recipe.Maintenance))
        {if(recipe.Maintenance.Bread>stock[5])breadFail=true;upkeepFail=true;if(recipe.Output.Bread>0)stopped=true;return;}
        Add(recipe.Maintenance,-1);
        if(!recipe.HasProduction)return;
        if(!CanPay(recipe.Input))
        {if(recipe.Input.Bread>stock[5])breadFail=true;if(recipe.Output.Bread>0||recipe.Input.Bread>0)stopped=true;return;}
        Add(recipe.Input,-1);Add(recipe.Output,1);
        // Chance-only income is deliberately not relied on to pay mandatory upkeep.
        if(recipe.BonusChance1>=1)Add(recipe.BonusOutput1,1);
        if(recipe.BonusChance2>=1)Add(recipe.BonusOutput2,1);
    }
    void Pay(FacilityData.ProductionBundle cost,ref bool breadFail,ref bool upkeepFail)
    {if(!CanPay(cost)){upkeepFail=true;if(cost.Bread>stock[5])breadFail=true;}else Add(cost,-1);}
    bool CanPay(FacilityData.ProductionBundle b)=>stock[0]>=b.Wood&&stock[1]>=b.Stone&&stock[2]>=b.Iron&&stock[3]>=b.MagicOre
        &&stock[4]>=b.Wheat&&stock[5]>=b.Bread&&stock[6]>=b.Water&&stock[7]>=b.Citizen;
    void Add(FacilityData.ProductionBundle b,int sign)
    {stock[0]+=b.Wood*sign;stock[1]+=b.Stone*sign;stock[2]+=b.Iron*sign;stock[3]+=b.MagicOre*sign;stock[4]+=b.Wheat*sign;stock[5]+=b.Bread*sign;stock[6]+=b.Water*sign;stock[7]+=b.Citizen*sign;}
    static void Read(FactionState.ResourceData r,double[] s)
    {s[0]=Math.Max(0,r.Wood);s[1]=Math.Max(0,r.Stone);s[2]=Math.Max(0,r.Iron);s[3]=Math.Max(0,r.MagicOre);s[4]=Math.Max(0,r.Wheat);s[5]=Math.Max(0,r.Bread);s[6]=Math.Max(0,r.Water);s[7]=Math.Max(0,r.Citizen);}
    public bool ImprovesFoodSupply(AIAction action,AIBoardState board)
    {
        if(action.ActionType!=AIActionType.Build)return false;
        var recipe=action.FacilityDefinition!=null?action.FacilityDefinition.GetLevel(1):FacilityData.GetLevel(action.Facility,1);
        if(recipe.Output.Bread>recipe.Input.Bread)return true;
        // Find an actual bread recipe waiting for inputs; do not hard-code Bakery, Field or Well.
        foreach(var current in recipes)
        {
            if(current.Output.Bread<=current.Input.Bread)continue;
            if(recipe.Output.Wheat>recipe.Input.Wheat&&current.Input.Wheat>board.EnemyResources.Wheat)return true;
            if(recipe.Output.Water>recipe.Input.Water&&current.Input.Water>board.EnemyResources.Water)return true;
        }
        return false;
    }
}
