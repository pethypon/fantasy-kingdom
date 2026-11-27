#if UNITY_EDITOR
using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public static class CampaignRewardTests
{
    static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("[CampaignV2] FAIL " + message);
        Debug.Log("[CampaignV2] PASS " + message);
    }

    public static IEnumerator Run(TurnGenerator turn, string output)
    {
        var s = turn.Systems;
        var rewards = s.FactionState.Rewards;
        int size = GameAuthoringRules.Active.tacticalMapSize;
        int area = size * MapCreate.HomelandDepth;
        Check(s.MapCreate.FixedHomelands && s.MapCreate.maxX == size && s.MapCreate.maxZ == size, size + " by " + size);
        foreach (var team in new[] {Team.Player, Team.Enemy})
        {
            var owned = s.TerritorySystem.GetTerritory(team);
            var parent = team == Team.Player ? s.UnitSetting.PlayerUnit : s.UnitSetting.EnemyUnit;
            var crystal = team == Team.Player ? s.CrystalSystem.PCP : s.CrystalSystem.ECP;
            Check(owned.Distinct().Count() == area && owned.All(p => s.MapCreate.InHomeRows((int)p.z, team)) &&
                s.MapCreate.InHomeRows((int)crystal.z,team) && parent.GetComponentsInChildren<Status>()
                .All(actor => s.MapCreate.InHomeRows(actor.GridPosition.z, team)), team + " home, crystal and entire opening roster");
            var data = s.UnitSetting.UnitDataMap[Kind.Scout];
            var side = rewards.Account(team);
            int original = side.souls;
            side.souls = 0;
            Check(!s.SummonSystem.CanSummon(team, data), team + " cannot summon with zero souls");
            side.souls = original;
            int ap = s.FactionState.GetAP(team);
            Check(!s.SummonSystem.AISummonUnit(new Vector3Int(-1,0,-1), data, team) &&
                side.souls == original && s.FactionState.GetAP(team) == ap, team + " rejected tile spends nothing");
            var cells = s.SummonSystem.AIGetSummonablePositions(team);
            Check(cells.Count > 0 && cells.All(p => s.MapCreate.InHomeRows(p.z, team)), team + " legal recruitment strip");
            Check(s.SummonSystem.AISummonUnit(cells[0], data, team) && side.souls == original - rewards.Cost(data)
                && s.SummonSystem.RemainingSummons(team) == 0 && s.FactionState.GetAP(team) == ap - data.costAP,
                team + " pays once, shares single recruitment slot");
        }
        var probe = new GameObject("test outpost");
        probe.transform.position = new Vector3(7,1,7);
        s.SubCrystalSystem.ExpandTerritory(probe,Team.Player);
        Check(s.TerritorySystem.PTSetPos.Count == area && !s.TerritorySystem.IsInAnyTerritory(7,7), "outpost does not annex neutral territory");
        s.SubCrystalSystem.OnSubCrystalDestroyed(probe, Team.Player);
        UnityEngine.Object.Destroy(probe);

        while (turn.Context.Turn < 5 && !turn.IsGameOver)
        {
            if (turn.CurrentState is PlayerMove move) move.ExecuteTurnEnd();
            yield return null;
        }
        Check(turn.Context.Turn == 5 && turn.CurrentState is CampaignRewardTurn, "round five waits for human choice");
        var buttons = UIBuilder.ScreenCanvas.transform.Find("CampaignRewards").GetComponentsInChildren<Button>();
        Check(buttons.Length == 3 && rewards.player.pending.Distinct().Count() == 3, "three distinct clickable choices");
        var report = new UIQualityAudit.Report();
        yield return UIQualityAudit.Capture(UIBuilder.ScreenCanvas,"reward",new Vector2Int(1280,800),output,report);
        yield return UIQualityAudit.Capture(UIBuilder.ScreenCanvas,"reward",new Vector2Int(960,540),output,report);
        Check(SaveCheckpoint.CanCapture(turn,out _), "choice is a valid checkpoint");
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            var explored = team == Team.Player ? s.VisionGenerator.PlayerExplored : s.VisionGenerator.EnemyExplored;
            var cell = explored.First();
            int beforeCount = explored.Count;
            s.VisionGenerator.AddExplored(team, cell + Vector3Int.up);
            s.VisionGenerator.AddExploredRange(team, new[] { cell + Vector3Int.down });
            Check(explored.Count == beforeCount && explored.All(tile => tile.y == 0) &&
                s.VisionGenerator.IsExplored(team, cell + Vector3Int.up * 2), team + " elevation does not duplicate explored tiles");
        }
        var save = SaveSystem.CollectGameState(turn,s.FactionState,s.TimerSystem,s.VisionGenerator,s.AICommander);
        Check(SaveSystem.TrySaveGame(0,save,out string reason), "pending offer saved: " + reason);
        Check(SaveSystem.TryLoadGame(0,out var restored,out reason) && restored.FixedHomelands && restored.Campaign.Valid(5)
            && restored.MapWidth == size && restored.MapDepth == size
            && restored.Campaign.player.pending.SequenceEqual(rewards.player.pending), "disk roundtrip preserves board size and exact offer: " + reason);
        int before = rewards.player.souls;
        buttons[0].onClick.Invoke();
        buttons[0].onClick.Invoke();
        Check(turn.CurrentState is PlayerMove && rewards.player.souls == Mathf.Min(rewards.settings.soulCap,before + rewards.settings.soulReward)
            && !rewards.Prepare(Team.Player,5,991), "double click / resumed round cannot collect twice");
        Check(save.Campaign.player.pending.Length == 3, "checkpoint detached from later actions");
        var r = CampaignRewards.Start();
        for (int round = 5; round <= 20; round += 5)
        {
            r.Prepare(Team.Player,round,99); r.player.pending[1] = CampaignRewardKind.Attack;
            Check(r.Claim(s.FactionState,Team.Player,round,1), "claim strength round " + round);
        }
        Check(r.player.attackStacks == r.settings.stacks && r.enemy.attackStacks == 0, "bounded faction-local stacking");
        r.Prepare(Team.Player,20 + r.settings.duration,77);
        Check(r.player.attackStacks == 0, "buff expires on its owner turn");
        ((PlayerMove)turn.CurrentState).ExecuteTurnEnd();
        while (!turn.IsGameOver && turn.Context.Turn < 6) yield return null;
        Check(rewards.enemy.lastOfferRound == 5 && rewards.enemy.pending.Length == 0, "enemy chooses before acting");
        SaveCompatibilityTests.RunAll();
    }
}
#endif
