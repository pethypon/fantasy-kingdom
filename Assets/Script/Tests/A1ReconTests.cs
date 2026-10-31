#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Controlled observation contracts; these are not ordinary-match discovery/win evidence.</summary>
public static class A1ReconTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static int checks;
    static void Check(string name, bool ok)
    { if (!ok) throw new InvalidOperationException("[A1Recon] FAIL " + name); checks++; Debug.Log("[A1Recon] PASS " + name); }
    static void See(R2ConstructionEconomyTests.Fixture f, params Vector3Int[] cells)
    {
        var set = new HashSet<Vector3Int>(cells);
        typeof(VisionGenerator).GetField(f.Team == Team.Player ? "_playerVisionBox" : "_enemyVisionBox",Hidden).SetValue(f.Vision,set);
    }
    public static void Run(GameSystems live)
    {
        checks = 0;
        using var effects = BoardActorAnimation.SuppressEffects();
        foreach (var side in new[] { Team.Player, Team.Enemy })
        {
            using var f = new R2ConstructionEconomyTests.Fixture(live,side);
            var board = f.Board(); board.TerritorySystem = f.Territory;
            var actor = f.Engineer; var profile = actor.GrowthData.actionProfile;
            actor.kind = actor.GrowthData.kind = Kind.Knight; actor.ATK = 20;
            profile.actionType = ActorActionType.All; profile.attack.useCustom = true;
            profile.attack.directionIndependent = true; profile.attack.SetCell(4,3,true);
            string label = side + " ";
            var host = new GameObject("A1 controlled opposing objective");
            host.transform.SetParent(board.OpponentCrystalParent); host.transform.position = f.Sites[1];
            var objective = host.AddComponent<Status>(); objective.kind = Kind.SubCrystal;
            objective.type = Type.Building; objective.team = board.OpponentTeam; objective.HP = objective.MaxHP = 5000;
            if (side == Team.Player) f.Crystals.ECP = f.Sites[1]; else f.Crystals.PCP = f.Sites[1];
            See(f,f.Sites[0]); board.Refresh();
            Check(label + "CR01 unknown objective publishes no true coordinate or HP",
                board.MainCrystalKnowledge == AIBaseKnowledgeStage.Unknown && !board.PlayerCrystalVisible
                && board.PlayerCrystalPos == Vector3.zero && board.PlayerCrystalHP == -1 && !board.GetLastKnownPlayerCrystal().Valid);
            See(f,f.Sites[0],f.Sites[1],f.Sites[2],f.Sites[3]); board.Refresh();
            Check(label + "CR03 observed subcrystal is not a main crystal", !board.PlayerCrystalVisible && !board.GetLastKnownPlayerCrystal().Valid);
            var governor = new AIStrategicGovernor(); board.Governor = governor;
            f.Territory.GetTerritory(board.OpponentTeam).Add(f.Sites[1]);
            governor.TerritoryIntel.Observe(board);
            Check(label + "CR02 observed territory produces a lead without invented base", board.MainCrystalKnowledge == AIBaseKnowledgeStage.TerritoryLead
                && governor.TerritoryIntel.HasUsableTerritory && !board.GetLastKnownPlayerCrystal().Valid);
            typeof(EnemyTerritoryIntel).GetField("<HasWaypoint>k__BackingField",Hidden).SetValue(governor.TerritoryIntel,true);
            typeof(EnemyTerritoryIntel).GetField("<Waypoint>k__BackingField",Hidden).SetValue(governor.TerritoryIntel,f.Sites[1]);
            Check(label + "CR05 reconnaissance coexists with an army waypoint", governor.TerritoryIntel.TryGetReconTarget(board,out var focus)
                && board.IsTerrainKnown(focus));
            typeof(AIStrategicGovernor).GetField("<Mode>k__BackingField",Hidden).SetValue(governor,StrategicMode.EconomicRecovery);
            Check(label + "CR09 economic interruption retains recon intent without performing actions",
                governor.TerritoryIntel.TryGetReconTarget(board,out _) && f.State.GetAP(side) == board.EnemyAP);
            var saved = governor.TerritoryIntel.CaptureState();
            var restored = new EnemyTerritoryIntel(); restored.RestoreState(JsonUtility.FromJson<EnemyTerritoryIntelState>(JsonUtility.ToJson(saved)),side);
            Check(label + "IT02 observed territory survives scoped save roundtrip", restored.HasUsableTerritory
                && restored.FirstObservedTurn == saved.FirstObservedTurn && restored.HasWaypoint
                && EnemyTerritoryIntel.IsValidState(saved,live.MapCreate.maxX,live.MapCreate.maxZ,side));
            objective.kind = Kind.Crystal; See(f,f.Sites[0]); board.Refresh();
            Check(label + "AT01 hidden main crystal never becomes an attack target", !board.PlayerCrystalVisible
                && !board.GetAttackTargets(actor).Contains(objective));
            See(f,f.Sites[0],f.Sites[1]); board.Refresh();
            Check(label + "CR06 main crystal becomes observed only in official sight", board.MainCrystalKnowledge == AIBaseKnowledgeStage.Observed
                && board.PlayerCrystalVisible && board.PlayerCrystalPos == (Vector3)f.Sites[1] && board.PlayerCrystalHP == 5000);
            Check(label + "AT02 observed target within authored pattern generates an attack", board.GetAttackTargets(actor).Contains(objective));
            profile.attack.SetCell(4,3,false); board.Refresh();
            Check(label + "AT03 incompatible authored firing mask cannot attack a visible objective", !board.GetAttackTargets(actor).Contains(objective));
            profile.attack.SetCell(4,3,true);
            See(f,f.Sites[0]); objective.HP = 4321; objective.transform.position = f.Sites[2]; board.Refresh();
            Check(label + "CR07 unseen relocation and HP stay absent from remembered facts", board.MainCrystalKnowledge == AIBaseKnowledgeStage.LastKnown
                && board.PlayerCrystalPos == (Vector3)f.Sites[1] && board.PlayerCrystalHP == -1
                && !board.GetAttackTargets(actor).Contains(objective));
            See(f,f.Sites[0],f.Sites[1]); board.Refresh();
            Check(label + "CR08 observing the empty old cell disproves memory", board.MainCrystalKnowledge == AIBaseKnowledgeStage.Disproved
                && !board.GetLastKnownPlayerCrystal().Valid && board.PlayerCrystalPos == Vector3.zero && board.PlayerCrystalHP == -1);
            objective.HP = 0; See(f,f.Sites[0],f.Sites[2]); board.Refresh();
            Check(label + "CR08 observed destruction cannot create a live attack target", board.MainCrystalKnowledge == AIBaseKnowledgeStage.Destroyed
                && !board.PlayerCrystalVisible && !board.GetAttackTargets(actor).Contains(objective));
        }
        Debug.Log("[A1Recon] RESULT checks=" + checks);
    }
}
#endif
