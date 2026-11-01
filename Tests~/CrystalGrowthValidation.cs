#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Controlled native Play Mode contracts, never a natural match outcome.</summary>
public static class CrystalGrowthValidation
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string label, bool value)
    { if (!value) throw new InvalidOperationException("[CrystalGrowth] FAIL " + label); Debug.Log("[CrystalGrowth] PASS " + label); }

    public static void Run(GameSystems live)
    {
        bool baseline = Environment.GetEnvironmentVariable("FK_EXPECT_CRYSTAL_REGEN") == "1";
        foreach (var side in new[] { Team.Player, Team.Enemy })
        {
            var parent = side == Team.Player ? live.CrystalSystem.Playercrystal : live.CrystalSystem.Enemycrystal;
            Status main = null;
            foreach (var actor in parent.GetComponentsInChildren<Status>()) if (actor.kind == Kind.Crystal) { main = actor; break; }
            Check(side + " actual-production-main-crystal-found", main != null);
            int hp = main.HP, maximum = main.MaxHP;
            var recovery = NaturalRecoverySystem.CopyState(main.NaturalRecovery);
            try
            {
                main.HP = Math.Max(1, main.MaxHP - 500);
                int wounded = main.HP;
                for (int round = 10000; round < 10007; round++) NaturalRecoverySystem.ProcessActorEnd(main, round);
                Debug.Log("[CrystalGrowth] OBSERVED " + side + " productionType=" + main.type + " woundedHP=" + wounded + " afterSevenOwnerEnds=" + main.HP + " maximum=" + main.MaxHP);
                Check(side + (baseline ? " baseline-reproduces-unintended-natural-heal" : " main-crystal-never-receives-unit-natural-heal"),
                    baseline ? main.HP > wounded : main.HP == wounded);
            }
            finally { main.MaxHP = maximum; main.HP = hp; main.NaturalRecovery = recovery; }
            if (!baseline) RewardCycle(live, side);
        }
        if (!baseline)
        {
            using var f = new R2ConstructionEconomyTests.Fixture(live, Team.Player);
            var unit = f.Engineer; unit.kind = Kind.Knight; unit.MaxHP = 100; unit.HP = 70;
            for (int round = 100; round < 107; round++) NaturalRecoverySystem.ProcessActorEnd(unit, round);
            Check("ordinary-unit-natural-recovery-is-still-active", unit.HP > 70 && unit.MaxHP == 100);
        }
    }

    static void RewardCycle(GameSystems live, Team side)
    {
        using var f = new R2ConstructionEconomyTests.Fixture(live, side);
        Status Main(Team team)
        {
            var go = new GameObject("controlled-main-crystal"); go.SetActive(false);
            go.transform.SetParent(team == Team.Player ? f.Crystals.Playercrystal : f.Crystals.Enemycrystal);
            var actor = go.AddComponent<Status>(); actor.team = team; actor.type = Type.Unit; actor.kind = Kind.Crystal;
            actor.MaxHP = actor.HP = MainCrystalRules.MaximumHP; go.SetActive(true); return actor;
        }
        var own = Main(side); var other = Main(side == Team.Player ? Team.Enemy : Team.Player);
        var sub = f.Builder.PlaceBuildingForLoad(f.Sites[0], FacilityKind.SubCrystal, side);
        Check(side + " actual-subcrystal-created", sub != null && sub.HP == FacilityData.GetLevel(FacilityKind.SubCrystal, 1).HP);
        int subHP = sub.HP, subMax = sub.MaxHP;
        var host = new GameObject("controlled-dungeon-cycle"); host.transform.SetParent(f.Builder.transform);
        var dungeon = host.AddComponent<DungeonSystem>();
        dungeon.Init(live.MapCreate, f.Crystals, f.Territory, f.State, f.Units, f.Builder, f.Turn);
        var list = (List<DungeonSystem.DungeonInfo>)typeof(DungeonSystem).GetField("_dungeons", Hidden).GetValue(dungeon);
        list.Add(new DungeonSystem.DungeonInfo { Position = f.Sites[0], SharedRemainingTurns = 1, Reward = DungeonSystem.Artifact.CrystalShard });
        dungeon.ProcessTurn(Team.Player, 1);
        Check(side + " real-shard-reward-adds-exactly-1000", own.HP == MainCrystalRules.MaximumHP + 1000 && own.MaxHP == MainCrystalRules.MaximumHP + 1000);
        Check(side + " shard-does-not-grow-sub-or-opposing-crystal", sub.HP == subHP && sub.MaxHP == subMax && other.MaxHP == MainCrystalRules.MaximumHP);
        dungeon.ProcessTurn(Team.Player, 1); dungeon.ProcessTurn(Team.Enemy, 2); dungeon.ProcessTurn(Team.Player, 2);
        Check(side + " same-round-and-nonreward-ticks-do-not-repeat-shard", own.HP == MainCrystalRules.MaximumHP + 1000 && own.MaxHP == MainCrystalRules.MaximumHP + 1000);
        own.HP -= 300; int woundedHP = own.HP;
        for (int round = 100; round < 107; round++) NaturalRecoverySystem.ProcessActorEnd(own, round);
        Check(side + " earned-maximum-is-preserved-without-passive-heal", own.HP == woundedHP && own.MaxHP == MainCrystalRules.MaximumHP + 1000);
    }
}
#endif
