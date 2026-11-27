#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEngine;

public static class R3SoulRosterTests
{
    static void Check(bool result, string label)
    {
        if (!result) throw new InvalidOperationException("[SoulRoster] FAIL " + label);
        Debug.Log("[SoulRoster] PASS " + label);
    }

    static void CheckCost(int actual, int expected, Kind kind, int column)
    {
        if (actual != expected) throw new InvalidOperationException($"[SoulRoster] {kind} cost #{column} expected {expected}, actual {actual}");
    }

    public static void Run(GameSystems systems)
    {
        var units = systems.UnitSetting;
        var rewards = systems.FactionState.Rewards;
        var expected = new (Kind kind, int hp, int atk, int barrier, int soul, int ap, int move, int attack, int vision)[]
        {
            (Kind.WindSpirit, 7, 0, 0, 1, 3, 24, 0, 24),
            (Kind.LightSpirit, 7, 0, 0, 1, 3, 4, 0, 48),
            (Kind.PurifyingSpirit, 7, 0, 0, 1, 3, 8, 0, 24),
            (Kind.Martyr, 14, 0, 0, 1, 3, 4, 0, 24),
            (Kind.BladeSpirit, 1, 49, 0, 2, 5, 4, 3, 8),
            (Kind.RockGolem, 84, 0, 0, 2, 5, 4, 0, 8),
            (Kind.CrystalSpirit, 7, 0, 70, 2, 5, 4, 0, 8),
            (Kind.Thunderbird, 7, 14, 0, 2, 5, 8, 48, 48),
            (Kind.CurseSpirit, 7, 0, 0, 2, 5, 8, 0, 24),
            (Kind.ThunderSpirit, 7, 0, 0, 2, 5, 4, 0, 24),
            (Kind.LifeSpirit, 7, 0, 0, 2, 5, 8, 0, 24),
            (Kind.IceSpirit, 7, 0, 0, 2, 5, 4, 0, 24),
            (Kind.Swordsman, 42, 18, 25, 3, 6, 4, 3, 24),
            (Kind.Spearman, 35, 18, 20, 3, 6, 4, 2, 24),
            (Kind.Javelineer, 28, 21, 15, 3, 7, 4, 4, 48),
            (Kind.Shieldbearer, 42, 9, 65, 4, 8, 4, 1, 8),
            (Kind.Cavalry, 49, 21, 30, 4, 9, 8, 3, 24),
        };
        Check(UnitRoster.SummonableKinds.Distinct().Count() == 35 && UnitRoster.SummonableKinds.Contains(Kind.Knight), "35 distinct roles including Knight");
        // Wood, stone, iron, magic, water, bread and citizens at recruitment, then the six upkeep resources.
        int[,] economy =
        {
            { 0, 0, 0, 2, 4, 0, 0, 0, 0, 0, 0, 2, 0 },
            { 0, 0, 0, 3, 4, 0, 0, 0, 0, 0, 2, 0, 0 },
            { 0, 0, 0, 3, 4, 0, 0, 0, 0, 0, 2, 0, 0 },
            { 5, 0, 2, 0, 4, 5, 1, 0, 0, 0, 0, 2, 2 },
            { 0, 0, 5, 4, 0, 0, 0, 0, 0, 2, 0, 0, 0 },
            { 0, 30, 0, 3, 0, 0, 0, 0, 2, 0, 0, 0, 0 },
            { 0, 10, 0, 6, 0, 0, 0, 0, 0, 0, 2, 0, 0 },
            { 0, 0, 0, 4, 6, 5, 0, 0, 0, 0, 0, 2, 2 },
            { 0, 0, 0, 5, 0, 0, 0, 0, 0, 0, 2, 0, 0 },
            { 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 2, 0, 0 },
            { 5, 0, 0, 5, 8, 0, 0, 0, 0, 0, 0, 4, 0 },
            { 0, 0, 0, 4, 10, 0, 0, 0, 0, 0, 0, 4, 0 },
            { 15, 10, 12, 0, 10, 10, 1, 0, 0, 2, 0, 2, 2 },
            { 20, 10, 10, 0, 10, 10, 1, 2, 0, 2, 0, 2, 2 },
            { 25, 5, 12, 0, 10, 10, 1, 2, 0, 2, 0, 2, 2 },
            { 15, 20, 20, 0, 12, 12, 1, 0, 0, 4, 0, 2, 2 },
            { 20, 10, 18, 0, 15, 20, 1, 0, 0, 2, 0, 2, 4 },
        };
        int index = 0;
        foreach (var row in expected)
        {
            var data = units.UnitDataMap[row.kind];
            var upkeep = data.GetFixedUpkeep();
            var actual = new[] { data.costWood, data.costStone, data.costIron, data.costMagic, data.costWater, data.costBread,
                data.costCitizen, upkeep.Wood, upkeep.Stone, upkeep.Iron, upkeep.MagicOre, upkeep.Water, upkeep.Bread };
            for (int column = 0; column < actual.Length; column++) CheckCost(actual[column], economy[index,column], row.kind, column);
            index++;
            var profile = data.actionProfile;
            Check(data.availableToPlayer && !data.availableToEnemy && data.prefab.GetComponent<Status>().kind == row.kind &&
                data.FixedMaxHP == row.hp && data.FixedATK == row.atk && data.baseBarrier == row.barrier &&
                rewards.Cost(data) == row.soul && data.costAP == row.ap && profile.movement.Offsets.Count == row.move &&
                profile.attack.Offsets.Count == row.attack && profile.CanAttack == (row.attack > 0) &&
                profile.vision.Offsets.Count == row.vision && (data.specialSkill == null || data.specialSkill.ConfigurationError == null),
                data.displayName + " production stats, price, faction, prefab, masks and skill");
        }
        Check(rewards.Cost(units.UnitDataMap[Kind.Knight]) >= 3, "ordinary Knight costs at least three souls");
        Status Spawn(Kind kind, Team team, int x, int z)
        {
            var data = units.UnitDataMap[kind];
            var parent = team == Team.Player ? units.PlayerUnit : units.EnemyUnit;
            return units.SpawnUnit(data, new Vector3(x,1,z), parent, initialTeam:team).GetComponent<Status>();
        }
        bool Cast(Status caster, Status target)
        {
            if (target != null) caster.VisionCell.Add(target.GridPosition);
            return SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(caster,target), systems, out _);
        }
        var cells = systems.SummonSystem.AIGetSummonablePositions(Team.Player);
        var blade = units.UnitDataMap[Kind.BladeSpirit];
        Check(cells.Count > 0 && !systems.SummonSystem.CanSummon(Team.Enemy, blade), "new recipes restricted to player");
        int before = rewards.player.souls;
        Check(systems.SummonSystem.AISummonUnit(cells[0],blade,Team.Player) && rewards.player.souls == before-2, "live summon debits exactly two souls");
        var bladeActor = units.PlayerUnit.GetComponentsInChildren<Status>().First(x=>x.kind == Kind.BladeSpirit);
        var bladeMesh = bladeActor.GetComponentInChildren<MeshFilter>().sharedMesh;
        Check(bladeMesh == UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(
            "Assets/_FantasyKingdom/GameContent/R3Production/Units/BladeSpirit/Sculpture.asset") &&
            bladeActor.GetComponentInChildren<MeshRenderer>().sharedMaterial == blade.prefab.GetComponent<MeshRenderer>().sharedMaterial,
            "live recruitment uses the carved blade and shared marble");
        var thunder = Spawn(Kind.ThunderSpirit,Team.Enemy,6,6);
        int enemyBefore = rewards.enemy.souls;
        SpecialSkillRuntime.ApplyAbilityDamage(thunder,bladeActor,1,HpChangeKind.NormalDamage);
        SpecialSkillRuntime.ApplyAbilityDamage(thunder,bladeActor,1,HpChangeKind.NormalDamage);
        Check(!bladeActor.IsAlive && rewards.enemy.souls == enemyBefore+1, "one point kills blade and duplicate damage awards no second soul");
        var tank = Spawn(Kind.CrystalSpirit,Team.Player,6,7);
        systems.FactionState.SetAP(Team.Enemy,0);
        Check(!Cast(thunder,tank) && thunder.IsAlive && tank.Barrier == 70, "failed strike preserves actor and barrier");
        systems.FactionState.SetAP(Team.Enemy,60);
        Check(Cast(thunder,tank) && !thunder.IsAlive && tank.Barrier == 30 && tank.HP == 7 && rewards.player.souls == before-2, "40-point strike, barrier first, sacrifice without kill reward");
        var bird = Spawn(Kind.Thunderbird,Team.Enemy,7,7);
        bird.VisionCell.Clear();
        Check(!SpecialSkillRuntime.CanNormalAttackTarget(bird,tank), "bird rejects unseen target");
        bird.VisionCell.Add(tank.GridPosition);
        Check(SpecialSkillRuntime.CanNormalAttackTarget(bird,tank) && !SpecialSkillRuntime.CanNormalAttackTarget(tank,bird), "bird sees target and non-attacker cannot attack");
        var ice = Spawn(Kind.IceSpirit,Team.Player,7,8);
        systems.FactionState.SetAP(Team.Player,60);
        Check(Cast(ice,bird) && SpecialSkillRuntime.MovementBlocked(bird) && !SpecialSkillRuntime.AttackBlocked(bird), "ice blocks movement only");
        var curse = Spawn(Kind.CurseSpirit,Team.Player,7,8);
        Check(Cast(curse,bird) && bird.Special.deathCurseTurns == 2, "curse applies for two owner turns");
        var cleaner = Spawn(Kind.PurifyingSpirit,Team.Enemy,7,8);
        Check(Cast(cleaner,bird) && bird.Special.deathCurseTurns == 0 && !SpecialSkillRuntime.MovementBlocked(bird), "purification clears both debuffs");
        var guard = Spawn(Kind.Shieldbearer,Team.Player,6,8);
        var ward = Spawn(Kind.Martyr,Team.Player,5,8);
        Check(Cast(ward,guard) && guard.Special.martyrWardTurns == 2, "adjacent ward applied");
        guard.direction = Direction.S;
        bird.transform.position = new Vector3(6,1,7);
        Check(Cast(guard,guard) && guard.Special.cooldown == 2, "shield stance costs a cooldown");
        var snapshot = SaveSystem.CollectGameState(systems.TurnGenerator, systems.FactionState, systems.TimerSystem,
            systems.VisionGenerator, systems.AICommander);
        Check(SaveSystem.TrySaveGame(0, snapshot, out string reason), "save live R3 roster: " + reason);
        Check(SaveSystem.TryLoadGame(0, out var loaded, out reason)
            && loaded.Campaign.player.souls == rewards.player.souls
            && loaded.Units.Any(entry => entry.Kind == StableSaveKeys.Key(Kind.Shieldbearer)
                && entry.Special.shieldStance && entry.Special.martyrWardTurns == 2 && entry.Special.cooldown == 2),
            "restore shieldbearer, ward, cooldown and soul stock: " + reason);
        SpecialSkillRuntime.ApplyHpChange(bird,guard,10,HpChangeKind.NormalDamage,cause:HpChangeCause.NormalAttack);
        Check(guard.Barrier == 65 && guard.Special.martyrWardTurns == 0, "ward absorbs one normal attack");
        SpecialSkillRuntime.ApplyHpChange(bird,guard,10,HpChangeKind.NormalDamage,cause:HpChangeCause.NormalAttack);
        Check(guard.Barrier == 58, "frontal stance reduces next normal hit by 30 percent");
        guard.HP = 10;
        var life = Spawn(Kind.LifeSpirit,Team.Player,5,8);
        Check(Cast(life,guard) && !life.IsAlive && guard.HP == 31 && guard.Barrier == 58, "life transfer restores 21 HP only");
        var secondIce = Spawn(Kind.IceSpirit,Team.Player,7,8);
        var secondCurse = Spawn(Kind.CurseSpirit,Team.Player,8,8);
        systems.FactionState.SetAP(Team.Player,60);
        Check(Cast(secondIce,bird) && Cast(secondCurse,bird), "reapply debuffs after purification");
        int round = systems.TurnGenerator.Context.Turn;
        SpecialSkillRuntime.OnOwnerTurnEnd(systems,Team.Enemy,round);
        SpecialSkillRuntime.OnOwnerTurnEnd(systems,Team.Enemy,round);
        Check(!guard.Special.shieldStance && bird.Special.iceBindTurns == 0 && bird.Special.deathCurseTurns == 1,
            "enemy end releases stance and bind, curse only ticks once");
        SpecialSkillRuntime.OnOwnerTurnEnd(systems,Team.Enemy,round+1);
        Check(bird.Special.deathCurseTurns == 0, "curse expires on second enemy turn end");
    }
}
#endif
