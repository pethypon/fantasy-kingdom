#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

public static partial class BalanceMatchStudy
{
    // Read the cached decision snapshot; never rerun or mutate the AI's forecast for observation.
    [Serializable] sealed class Diagnosis
    {
        public int ObservedRound;
        public string Phase;
        public SideDiagnosis Player, Enemy;
    }
    [Serializable] sealed class SideDiagnosis
    {
        public string Team, Mode, Economy, Foundation, Bottleneck, BreadCoverage;
        public bool HasCriticalDeficit, HasWarningDeficit, BreadFailed, UpkeepFailed, ProductionStopped;
        public int MandatoryFailureMask, InputFailureMask, FirstMandatoryFailureMask, FirstInputFailureMask;
        public ResourceDiagnosis[] Resources;
        public string[] RecommendedFacilities;
        public ActorDiagnosis[] Units;
        public BuildingDiagnosis[] Buildings;
    }
    [Serializable] sealed class ResourceDiagnosis
    {
        public string Resource;
        public float StockAtDiagnosis, Production, MandatoryDemand, PlannedDemand, Net, Reserve, Projected, Target, Deficit, Urgency;
        public float PermanentProduction, PotentialProduction, TemporaryIncome;
        public int ProducerCount, PotentialSourceCount;
        public string FoundationResourceState;
        public float FoundationPlanned, FoundationReserve, FoundationCoverage, FoundationIncomeGap, FoundationPressure;
    }
    [Serializable] sealed class ActorDiagnosis
    {
        public string Kind;
        public int Level, HP, MaxHP, ATK, DEF, Unpaid;
    }
    [Serializable] sealed class BuildingDiagnosis
    {
        public string Facility, Definition;
        public int Level, HP;
    }
    static void WriteDiagnosis(string phase)
    {
        var snapshot = new Diagnosis { ObservedRound = turn.Context.Turn, Phase = phase,
            Player = ObserveSide(Team.Player, turn.PlayerAI.Commander), Enemy = ObserveSide(Team.Enemy, turn.Systems.AICommander) };
        string path = Path.Combine(folder, "economy-" + turn.Context.Turn + "-" + phase + ".json");
        File.WriteAllText(path, JsonUtility.ToJson(snapshot, true));
    }
    static SideDiagnosis ObserveSide(Team team, AICommander commander)
    {
        var g = commander.StrategicGovernor; var d = g.ProductionDemand; var f = g.Economy;
        var parent = team == Team.Player ? turn.Systems.UnitSetting.PlayerUnit : turn.Systems.UnitSetting.EnemyUnit;
        return new SideDiagnosis { Team = team.ToString(), Mode = g.Mode.ToString(), Economy = f.State.ToString(),
            Foundation = g.BasicResources.FoundationState.ToString(), Bottleneck = g.BasicResources.PrimaryBottleneck.ToString(),
            BreadCoverage = f.BreadCoverageTurns.ToString(CultureInfo.InvariantCulture),
            HasCriticalDeficit = d != null && d.HasCriticalDeficit, HasWarningDeficit = d != null && d.HasWarningDeficit,
            BreadFailed = f.BreadPaymentFailed, UpkeepFailed = f.UpkeepPaymentFailed, ProductionStopped = f.ImportantProductionStopped,
            MandatoryFailureMask = f.MandatoryFailureMask, InputFailureMask = f.InputFailureMask,
            FirstMandatoryFailureMask = f.FirstTurnMandatoryFailureMask, FirstInputFailureMask = f.FirstTurnInputFailureMask,
            RecommendedFacilities = d?.RecommendedFacilities.Select(kind => kind.ToString()).ToArray(),
            Resources = d?.Resources.Select(r => new ResourceDiagnosis { Resource = r.Resource.ToString(), StockAtDiagnosis = r.Stock,
                Production = r.Production, MandatoryDemand = r.Mandatory, PlannedDemand = r.Planned, Net = r.Net,
                Reserve = r.Reserve, Projected = r.Projected, Target = r.Target, Deficit = r.Deficit, Urgency = r.Urgency,
                PermanentProduction = d.PermanentProduction(r.Resource), PotentialProduction = d.PotentialProduction(r.Resource),
                TemporaryIncome = d.TemporaryIncome(r.Resource), ProducerCount = d.ProducerCount(r.Resource),
                PotentialSourceCount = d.PotentialSourceCount(r.Resource),
                FoundationResourceState = g.BasicResources.Get(r.Resource)?.State.ToString(),
                FoundationPlanned = g.BasicResources.Get(r.Resource)?.PlannedDemand ?? 0,
                FoundationReserve = g.BasicResources.Get(r.Resource)?.ReserveTarget ?? 0,
                FoundationCoverage = g.BasicResources.Get(r.Resource)?.Coverage ?? 0,
                FoundationIncomeGap = g.BasicResources.Get(r.Resource)?.IncomeGap ?? 0,
                FoundationPressure = g.BasicResources.Get(r.Resource)?.PressureScore ?? 0 }).ToArray(),
            Buildings = turn.Systems.BuildSystem.GetBuildingParent(team).GetComponentsInChildren<Status>()
                .Where(u => u.IsAlive).Select(u => new BuildingDiagnosis { Facility = u.facilityKind.ToString(),
                    Definition = u.authoredFacilityId, Level = u.Level, HP = u.HP }).ToArray(),
            Units = parent.GetComponentsInChildren<Status>().Where(u => u.IsAlive && u.type == Type.Unit)
                .Select(u => new ActorDiagnosis { Kind = u.kind.ToString(), Level = u.Level, HP = u.HP, MaxHP = u.MaxHP,
                    ATK = u.ATK, DEF = u.DEF, Unpaid = u.UpkeepUnpaidTurns }).ToArray() };
    }
}
#endif
