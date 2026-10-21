#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>R1 contracts run through shared actions, normal battle, AP, lifecycle, and save state.</summary>
public static class AbilityR1Tests
{
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static int passed;
    static void Check(string name, bool condition)
    { if (!condition) throw new InvalidOperationException("[AbilityR1] FAIL " + name); passed++; Debug.Log("[AbilityR1] PASS " + name); }
    static Status Actor(UnitSpecialSkill ability, Team team, int x, int z, int hp = 1000, Kind kind = Kind.Archer)
    {
        var go = new GameObject("R1 ability fixture"); owned.Add(go);
        var actor = go.AddComponent<Status>(); actor.team = team; actor.type = Type.Unit; actor.kind = kind;
        actor.HP = actor.MaxHP = hp; actor.ATK = 20; actor.DEF = 0; actor.transform.position = new Vector3(x, 1, z);
        actor.Level = 100; // Keep battle-earned XP from changing the explicit HP/ATK fixtures.
        actor.VisionCell = new HashSet<Vector3Int>();
        var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data);
        data.baseHP = hp; data.baseATK = 20; data.baseDEF = 0; actor.GrowthData = data;
        if (ability != UnitSpecialSkill.None)
        {
            var definition = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); owned.Add(definition);
            definition.ability = ability; definition.apCost = 1; definition.cooldown = 0;
            definition.stationaryAttackCurve = AnimationCurve.Linear(0, 1, 3, 1.75f);
            definition.sniperRecoilCurve = AnimationCurve.Linear(0, 0, 25, .15f);
            definition.sacrificeLimit = 4; definition.samuraiGaugeGain = 1; definition.samuraiGaugeMax = 3;
            definition.holyKnightArea = new[] { Vector2Int.zero }; definition.holyKnightInterruptionConfigured = true;
            definition.assassinBoostDuration = 2; definition.assassinBoostStackRule = AbilityStackRule.Strongest;
            data.specialSkill = definition;
        }
        CombatRegistry.Register(actor); return actor;
    }
    static void See(Status actor, params Status[] targets)
    { actor.VisionCell.Clear(); foreach (var target in targets) actor.VisionCell.Add(target.GridPosition); }
    static void Cast(GameSystems systems, Status caster, Status target = null)
    {
        systems.FactionState.SetAP(caster.team, APSystem.MaximumAP);
        Check("authorized " + SpecialSkillRuntime.Definition(caster).ability,
            SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(caster, target), systems, out var reason));
    }
    static void Shoot(GameSystems systems, Status attacker, Status target)
    { systems.TurnGenerator.Context.SelectUnit = attacker; systems.BattleSystem.SetTarget(target); systems.BattleSystem.ProcessDamage(systems.TurnGenerator); }
    static SaveSystem.UnitSaveData Save(Status actor)
        => JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));

    public static void Run(GameSystems systems)
    {
        passed = 0;
        var originalActors = CombatRegistry.Snapshot();
        var originalMap = systems.MapCreate; var moveMap = systems.MoveGenerator.mapcreate;
        var originalSelection = systems.TurnGenerator.Context.SelectUnit; var originalTarget = systems.BattleSystem.Target;
        int playerAP = systems.FactionState.GetAP(Team.Player), enemyAP = systems.FactionState.GetAP(Team.Enemy);
        var randomBefore = UnityEngine.Random.state;
        try
        {
            // Protect the running scene's actors from global turn hooks in this focused fixture.
            foreach (var actor in originalActors) if (actor != null) actor.gameObject.SetActive(false);
            var geometry = new GameObject("R1 ability flat fixture map"); geometry.SetActive(false); owned.Add(geometry);
            var map = geometry.AddComponent<MapCreate>(); map.UseR1Terrain = false; map.SetPos = new List<Vector3>();
            for (int x = 120000; x <= 120200; x++) for (int z = 120000; z <= 120030; z++) map.SetPos.Add(new Vector3(x, 1, z));
            systems.MapCreate = map; systems.MoveGenerator.mapcreate = map;

            var archer = Actor(UnitSpecialSkill.ArcherStationary, Team.Enemy, 120000, 120000);
            var snapshot = Actor(UnitSpecialSkill.None, Team.Player, 120001, 120000);
            var outside = Actor(UnitSpecialSkill.None, Team.Player, 120004, 120000);
            Cast(systems, archer);
            Check("Archer snapshots only enemies within two cells", archer.Special.archerSnapshotIds.Count == 1
                && archer.Special.archerSnapshotIds.Contains(snapshot.ReflectionLifeId));
            Check("Archer grants two-turn poison and a real enemy ATK boost", StatusEffectSystem.HasDebuff(snapshot, StatusEffectType.Poison)
                && Mathf.Approximately(SpecialSkillRuntime.GetATKModifier(snapshot), 1.2f));
            Check("snapshot enemies cannot target the escaped Archer", SpecialSkillRuntime.IsHiddenFrom(snapshot, archer)
                && !SpecialSkillRuntime.CanNormalAttackTarget(snapshot, archer));
            Check("Archer is attack locked but remains able to move", SpecialSkillRuntime.AttackBlocked(archer) && !SpecialSkillRuntime.MovementBlocked(archer));
            snapshot.transform.position = new Vector3(120010, 1, 120000); outside.transform.position = new Vector3(120001, 1, 120001);
            Check("snapshot survives movement and does not add a later entrant", SpecialSkillRuntime.IsHiddenFrom(snapshot, archer)
                && !SpecialSkillRuntime.IsHiddenFrom(outside, archer) && Mathf.Approximately(SpecialSkillRuntime.GetATKModifier(snapshot), 1.2f));
            var archerSave = Save(archer); SaveGameApplier.ApplyStatusFields(archer, archerSave); SpecialSkillRuntime.RestoreLinks();
            Check("Archer snapshot survives save and load", SpecialSkillRuntime.IsHiddenFrom(snapshot, archer));
            SpecialSkillRuntime.OnActorTurnStart(archer, systems, 90000); SpecialSkillRuntime.OnActorTurnStart(archer, systems, 90001);
            Check("Archer attack lock and enemy boost expire together", !SpecialSkillRuntime.AttackBlocked(archer)
                && !SpecialSkillRuntime.IsHiddenFrom(snapshot, archer) && Mathf.Approximately(SpecialSkillRuntime.GetATKModifier(snapshot), 1));

            var magic = Actor(UnitSpecialSkill.MagicRitual, Team.Enemy, 120020, 120000);
            var ordinary = Actor(UnitSpecialSkill.None, Team.Player, 120022, 120000);
            var low = Actor(UnitSpecialSkill.None, Team.Player, 120021, 120000); low.HP = 250;
            var strong = Actor(UnitSpecialSkill.None, Team.Player, 120023, 120000, kind: Kind.Boss);
            Check("Magician low-HP aura distinguishes strong and ordinary targets", Mathf.Approximately(SpecialSkillRuntime.GetAttackMultiplier(magic, ordinary), .75f)
                && Mathf.Approximately(SpecialSkillRuntime.GetAttackMultiplier(magic, strong), 1.5f));
            low.HP = 251;
            Check("Magician aura does not activate above twenty-five percent HP", Mathf.Approximately(SpecialSkillRuntime.GetAttackMultiplier(magic, ordinary), 1));
            low.HP = 200; low.transform.position += Vector3.up * 3;
            Check("Magician adjacency respects vertical distance", Mathf.Approximately(SpecialSkillRuntime.GetAttackMultiplier(magic, ordinary), 1));

            var assassin = Actor(UnitSpecialSkill.AssassinVisionSeal, Team.Enemy, 120030, 120000, kind: Kind.Assassin);
            var ally = Actor(UnitSpecialSkill.None, Team.Enemy, 120031, 120000); ally.HP = 200;
            ally.specialAbility = SpecialAbility.SurvivalInstinct;
            Check("replacement Assassin cannot select allied normal attacks", !SpecialSkillRuntime.CanNormalAttackTarget(assassin, ally));
            Shoot(systems, assassin, ally);
            Check("former allied execution cannot damage allies", ally.IsAlive && ally.HP == 200);
            assassin.Special.assassinBoostTurns = 2; assassin.Special.assassinBoostStacks = 1;
            Check("old saved Assassin boost has no combat effect", Mathf.Approximately(SpecialSkillRuntime.GetATKModifier(assassin), 1)
                && assassin.HP == assassin.MaxHP);
            var healthyAlly = Actor(UnitSpecialSkill.None, Team.Enemy, 120032, 120000); healthyAlly.HP = 900;
            Shoot(systems, assassin, healthyAlly);
            Check("healthy allied target is also protected", healthyAlly.IsAlive && healthyAlly.HP == 900);
            var hostile = Actor(UnitSpecialSkill.None, Team.Player, 120033, 120000); hostile.specialAbility = SpecialAbility.SurvivalInstinct;
            Shoot(systems, assassin, hostile);
            Check("ordinary enemies take ordinary damage without execution or self HP-one cost", hostile.IsAlive && hostile.HP < hostile.MaxHP && assassin.HP == assassin.MaxHP);
            Check("ordinary attack has no former execution self reduction", !assassin.NaturalRecovery.HpLostSinceLastOwnTurn);
            Check("all former execution targets are disabled", !SpecialSkillRuntime.WouldAssassinate(assassin, strong) && !SpecialSkillRuntime.WouldAssassinate(assassin, hostile));

            var scout = Actor(UnitSpecialSkill.ScoutVisionGuard, Team.Enemy, 120040, 120000);
            var scoutDef = SpecialSkillRuntime.Definition(scout); scoutDef.scoutFreeMoveScope = scoutDef.scoutFreeAttackScope = AbilityResetScope.OwnerTurn;
            var friendly = Actor(UnitSpecialSkill.None, Team.Enemy, 120041, 120000);
            var foe = Actor(UnitSpecialSkill.None, Team.Player, 120042, 120000); See(scout, friendly, foe);
            foreach (var actor in new[] { friendly, foe })
            {
                Check(actor.team + " Scout grants cost-stage free movement", systems.APSystem.CalcCost(APSystem.ActionType.Move, actor) == 0);
                int before = systems.FactionState.GetAP(actor.team); systems.APSystem.Consume(actor.team, APSystem.ActionType.Move, actor);
                Check(actor.team + " free movement spends no AP and is consumed once", systems.FactionState.GetAP(actor.team) == before
                    && systems.APSystem.CalcCost(APSystem.ActionType.Move, actor) > 0);
                Check(actor.team + " attack is independently free once", systems.APSystem.CalcCost(APSystem.ActionType.Attack, actor) == 0);
                systems.APSystem.Consume(actor.team, APSystem.ActionType.Attack, actor);
                Check(actor.team + " free attack does not recur", systems.APSystem.CalcCost(APSystem.ActionType.Attack, actor) > 0);
                SpecialSkillRuntime.OnActorTurnStart(actor, systems, 90100);
                Check(actor.team + " authored owner-turn scope restores both allowances", systems.APSystem.CalcCost(APSystem.ActionType.Move, actor) == 0
                    && systems.APSystem.CalcCost(APSystem.ActionType.Attack, actor) == 0);
            }
            scoutDef.scoutFreeMoveScope = scoutDef.scoutFreeAttackScope = AbilityResetScope.Lifetime;
            systems.APSystem.Consume(foe.team, APSystem.ActionType.Move, foe); SpecialSkillRuntime.OnActorTurnStart(foe, systems, 90101);
            Check("lifetime allowance remains consumed across turns", systems.APSystem.CalcCost(APSystem.ActionType.Move, foe) > 0);

            var priest = Actor(UnitSpecialSkill.PriestPurification, Team.Enemy, 120050, 120000); priest.HP = 500;
            var healAlly = Actor(UnitSpecialSkill.None, Team.Enemy, 120051, 120000); healAlly.HP = 400;
            var healFoe = Actor(UnitSpecialSkill.None, Team.Player, 120050, 120001); healFoe.HP = 300;
            var far = Actor(UnitSpecialSkill.None, Team.Player, 120052, 120000); far.HP = 200;
            SpecialSkillRuntime.OnActorTurnStart(priest, systems, 90200);
            Check("Priest aura heals itself, allies, and enemies by maximum HP five percent", priest.HP == 550 && healAlly.HP == 450 && healFoe.HP == 350);
            Check("Priest aura excludes actors beyond one cell", far.HP == 200);

            var guardian = Actor(UnitSpecialSkill.GuardianLifeLink, Team.Enemy, 120060, 120000);
            var ward = Actor(UnitSpecialSkill.None, Team.Enemy, 120061, 120000); ward.HP = 80;
            Cast(systems, guardian, ward);
            var guardianSave = Save(guardian); var wardSave = Save(ward);
            guardian.Special.guardianPartnerId = ward.Special.guardianPartnerId = null;
            SaveGameApplier.ApplyStatusFields(guardian, guardianSave); SaveGameApplier.ApplyStatusFields(ward, wardSave); SpecialSkillRuntime.RestoreLinks();
            Check("Guardian reciprocal link restores by saved life identity", guardian.Special.guardianPartnerId == ward.ReflectionLifeId
                && ward.Special.guardianPartnerId == guardian.ReflectionLifeId);
            SpecialSkillRuntime.ApplyHpChange(ordinary, ward, 90, HpChangeKind.NormalDamage);
            Check("lethal intended victim is saved while its linked partner dies", ward.IsAlive && ward.HP == 80 && !guardian.IsAlive
                && string.IsNullOrEmpty(ward.Special.guardianPartnerId));
            var guardAP = Actor(UnitSpecialSkill.GuardianLifeLink, Team.Enemy, 120062, 120000);
            var guardDef = SpecialSkillRuntime.Definition(guardAP); guardDef.guardianAttackCostReset = AbilityResetScope.OwnerTurn;
            var crystal = Actor(UnitSpecialSkill.None, Team.Player, 120063, 120000, kind: Kind.Crystal);
            Check("Guardian crystal damage uses the specified multiplier", Mathf.Approximately(SpecialSkillRuntime.GetAttackMultiplier(guardAP, crystal), 2));
            Check("Guardian starts without extra attack AP", SpecialSkillRuntime.AttackAPAdjustment(guardAP, 2) == 2);
            Shoot(systems, guardAP, ordinary); Shoot(systems, guardAP, ordinary);
            Check("real Guardian attacks accrue one AP each", SpecialSkillRuntime.AttackAPAdjustment(guardAP, 2) == 4);
            SpecialSkillRuntime.OnActorTurnStart(guardAP, systems, 90300);
            Check("Guardian follows authored attack-AP reset scope", SpecialSkillRuntime.AttackAPAdjustment(guardAP, 2) == 2);

            var sniper = Actor(UnitSpecialSkill.MagicSniperLock, Team.Enemy, 120070, 120000, kind: Kind.Magicsniper);
            var sniperTarget = Actor(UnitSpecialSkill.None, Team.Player, 120071, 120000); sniperTarget.HP = 500;
            Check("Sniper recognizes a target with lower HP ratio", Mathf.Approximately(SpecialSkillRuntime.GetAttackMultiplier(sniper, sniperTarget), 1.5f));
            Shoot(systems, sniper, sniperTarget);
            Check("ordinary Sniper shot charges twenty percent self HP exactly once", sniper.HP == 800);
            sniper.ATK = 20; See(sniper, sniperTarget); Cast(systems, sniper, sniperTarget);
            SpecialSkillRuntime.OnActorTurnStart(sniper, systems, 90400);
            Check("resolving the lock turn itself does not charge shot recoil", sniper.HP == 800);
            sniperTarget.transform.position = new Vector3(120095, 1, 120000); sniper.VisionCell.Clear();
            Check("ready Sniper lock ignores range and lost vision", SpecialSkillRuntime.TryGetSniperTarget(sniper, out var tracked) && tracked == sniperTarget
                && !SpecialSkillRuntime.CanNormalAttackTarget(sniper, ordinary));
            Shoot(systems, sniper, sniperTarget);
            Check("locked shot charges twenty plus fifteen percent recoil and clears reservation", sniper.HP == 450 && !sniper.Special.sniperActive);

            var protectedSniper = Actor(UnitSpecialSkill.MagicSniperLock, Team.Enemy, 120075, 120010, kind: Kind.Magicsniper);
            var recoilTarget = Actor(UnitSpecialSkill.None, Team.Player, 120076, 120010);
            void PrepareRecoilProtection(bool shield)
            {
                protectedSniper.HP = protectedSniper.MaxHP;
                protectedSniper.Special.damageNullUnused = true; protectedSniper.Special.damageNullTurns = 2;
                protectedSniper.ShieldTurns = shield ? 1 : 0;
                protectedSniper.NaturalRecovery = new NaturalRecoveryState { ConsecutiveSafeTurns = 3, HasTurnBaseline = true };
            }
            PrepareRecoilProtection(false); Shoot(systems, protectedSniper, recoilTarget);
            Check("one Scout nullification blocks ordinary twenty-percent recoil without resetting the safe clock",
                protectedSniper.HP == 1000 && !protectedSniper.Special.damageNullUnused
                && protectedSniper.NaturalRecovery.ConsecutiveSafeTurns == 3 && !protectedSniper.NaturalRecovery.HpLostSinceLastOwnTurn);
            See(protectedSniper, recoilTarget); Cast(systems, protectedSniper, recoilTarget);
            SpecialSkillRuntime.OnActorTurnStart(protectedSniper, systems, 90410);
            recoilTarget.transform.position = new Vector3(120100, 1, 120010); PrepareRecoilProtection(false);
            Shoot(systems, protectedSniper, recoilTarget);
            Check("a locked shot has two recoil events so one nullification blocks twenty percent but not distance fifteen percent",
                protectedSniper.HP == 850 && !protectedSniper.Special.damageNullUnused
                && protectedSniper.NaturalRecovery.ConsecutiveSafeTurns == 0 && protectedSniper.NaturalRecovery.HpLostSinceLastOwnTurn);
            See(protectedSniper, recoilTarget); Cast(systems, protectedSniper, recoilTarget);
            SpecialSkillRuntime.OnActorTurnStart(protectedSniper, systems, 90411); PrepareRecoilProtection(true);
            Shoot(systems, protectedSniper, recoilTarget);
            Check("shield blocks both recoil events while preserving the Scout token and natural-recovery clock",
                protectedSniper.HP == 1000 && protectedSniper.Special.damageNullUnused
                && protectedSniper.NaturalRecovery.ConsecutiveSafeTurns == 3 && !protectedSniper.NaturalRecovery.HpLostSinceLastOwnTurn);
            protectedSniper.ShieldTurns = 0; protectedSniper.Special.damageNullUnused = false;
            var recoilGuardian = Actor(UnitSpecialSkill.GuardianLifeLink, Team.Enemy, 120074, 120010);
            Cast(systems, recoilGuardian, protectedSniper);
            See(protectedSniper, recoilTarget); Cast(systems, protectedSniper, recoilTarget);
            SpecialSkillRuntime.OnActorTurnStart(protectedSniper, systems, 90412);
            protectedSniper.NaturalRecovery = new NaturalRecoveryState { ConsecutiveSafeTurns = 3, HasTurnBaseline = true };
            Shoot(systems, protectedSniper, recoilTarget);
            Check("Guardian receives both normal recoil events and only actual HP loss resets recovery",
                protectedSniper.HP == 1000 && recoilGuardian.HP == 650
                && protectedSniper.NaturalRecovery.ConsecutiveSafeTurns == 3 && recoilGuardian.NaturalRecovery.HpLostSinceLastOwnTurn);

            var bomber = Actor(UnitSpecialSkill.LifeBomb, Team.Enemy, 120100, 120000, hp: 100);
            SpecialSkillRuntime.Definition(bomber).bomberAutoExplosionRadius = 1;
            var bombAlly = Actor(UnitSpecialSkill.None, Team.Enemy, 120101, 120000);
            var bombEnemy = Actor(UnitSpecialSkill.None, Team.Player, 120100, 120001);
            var bombCrystal = Actor(UnitSpecialSkill.None, Team.Player, 120099, 120000, kind: Kind.Crystal);
            // Keep the test crystal durable so this is damage/friendly-fire verification, not match termination.
            bombCrystal.MaxHP = bombCrystal.HP = 100000;
            for (int round = 90500; round < 90509; round++) SpecialSkillRuntime.OnOwnerTurnEnd(systems, Team.Enemy, round);
            Check("idle Bomber remains alive for nine non-damaging turns", bomber.IsAlive && bomber.Special.bomberIdleTurns == 9);
            int allyBefore = bombAlly.HP, enemyBefore = bombEnemy.HP, crystalBefore = bombCrystal.HP;
            SpecialSkillRuntime.OnOwnerTurnEnd(systems, Team.Enemy, 90509);
            Check("tenth idle turn detonates and kills Bomber", !bomber.IsAlive);
            Check("automatic explosion applies friendly fire, enemy, and crystal damage", bombAlly.HP < allyBefore && bombEnemy.HP < enemyBefore && bombCrystal.HP < crystalBefore);

            var paladin = Actor(UnitSpecialSkill.HolyKnightSanctuary, Team.Enemy, 120120, 120000, kind: Kind.HolyKnight);
            var stolenFrom = Actor(UnitSpecialSkill.None, Team.Player, 120121, 120000); stolenFrom.ATK = 100;
            paladin.ATK = 40; Shoot(systems, paladin, stolenFrom);
            Check("successful Paladin hit steals twenty-five percent ATK immediately from victim", SpecialSkillRuntime.GetATKFlatAdjustment(stolenFrom) == -25
                && SpecialSkillRuntime.GetATKFlatAdjustment(paladin) == 0);
            SpecialSkillRuntime.OnActorTurnStart(paladin, systems, 90600);
            Check("Paladin gains stolen ATK on its following turn", SpecialSkillRuntime.GetATKFlatAdjustment(paladin) == 25);
            int paladinCurrentATK = Mathf.FloorToInt((paladin.ATK + 25) * StatusEffectSystem.GetATKModifier(paladin));
            SpecialSkillRuntime.OnActorTurnStart(paladin, systems, 90601);
            Check("two-turn return adds eighty percent of current Paladin ATK to original enemy", SpecialSkillRuntime.GetATKFlatAdjustment(stolenFrom)
                == Mathf.FloorToInt(paladinCurrentATK * .8f) && SpecialSkillRuntime.GetATKFlatAdjustment(paladin) == 0);
            SpecialSkillRuntime.OnOwnerTurnEnd(systems, Team.Enemy, 90601);
            Check("Paladin return modifier ends at that turn's end", SpecialSkillRuntime.GetATKFlatAdjustment(stolenFrom) == 0 && paladin.Special.paladinCycles.Count == 0);

            var samurai = Actor(UnitSpecialSkill.SamuraiCounter, Team.Enemy, 120140, 120000, kind: Kind.Samurai);
            var challenged = Actor(UnitSpecialSkill.None, Team.Player, 120141, 120000);
            var alternative = Actor(UnitSpecialSkill.None, Team.Enemy, 120142, 120000); See(samurai, challenged); Cast(systems, samurai, challenged);
            Check("Samurai taunt restricts normal attack candidates to caster", SpecialSkillRuntime.CanNormalAttackTarget(challenged, samurai)
                && !SpecialSkillRuntime.CanNormalAttackTarget(challenged, alternative));
            var challengedSave = Save(challenged); SaveGameApplier.ApplyStatusFields(challenged, challengedSave); SpecialSkillRuntime.RestoreLinks();
            Check("taunt target and duration survive save/load", challenged.Special.tauntedById == samurai.ReflectionLifeId && challenged.Special.tauntTurns == 3);
            for (int round = 90700; round < 90703; round++)
            {
                SpecialSkillRuntime.OnActorTurnStart(challenged, systems, round);
                Check("taunt remains effective throughout challenged owner turn " + (round - 90699),
                    SpecialSkillRuntime.CanNormalAttackTarget(challenged, samurai) && !SpecialSkillRuntime.CanNormalAttackTarget(challenged, alternative));
                SpecialSkillRuntime.OnOwnerTurnEnd(systems, Team.Player, round);
            }
            Check("taunt expires after three completed challenged owner turns", challenged.Special.tauntTurns == 0 && SpecialSkillRuntime.CanNormalAttackTarget(challenged, alternative));

            var recovery = Actor(UnitSpecialSkill.None, Team.Enemy, 120160, 120000); recovery.HP = 400; recovery.NaturalRecovery = new NaturalRecoveryState();
            for (int round = 90800; round < 90804; round++) NaturalRecoverySystem.ProcessActorEnd(recovery, round);
            Check("four completed safe turns have no natural heal", recovery.HP == 400 && recovery.NaturalRecovery.ConsecutiveSafeTurns == 4);
            NaturalRecoverySystem.ProcessActorEnd(recovery, 90804);
            Check("fifth safe turn heals at its end without a sixth-turn wait", recovery.HP == 500 && recovery.NaturalRecovery.ConsecutiveSafeTurns == 5);
            NaturalRecoverySystem.ProcessActorEnd(recovery, 90804);
            Check("same turn-end cannot heal twice", recovery.HP == 500);
            recovery.Special.damageNullUnused = true; recovery.Special.damageNullTurns = 2;
            SpecialSkillRuntime.ApplyHpChange(challenged, recovery, 100, HpChangeKind.NormalDamage);
            NaturalRecoverySystem.ProcessActorEnd(recovery, 90805);
            Check("zero actual HP loss preserves natural recovery and subsequent heal", recovery.HP == 600 && recovery.NaturalRecovery.ConsecutiveSafeTurns == 5);
            SpecialSkillRuntime.ApplyHpChange(recovery, recovery, 1, HpChangeKind.DirectHpReduction);
            NaturalRecoverySystem.ProcessActorEnd(recovery, 90806);
            Check("one direct HP loss resets natural recovery at end", recovery.HP == 599 && recovery.NaturalRecovery.ConsecutiveSafeTurns == 0);
            Debug.Log("[AbilityR1] SUMMARY passed=" + passed);
        }
        finally
        {
            systems.MapCreate = originalMap; systems.MoveGenerator.mapcreate = moveMap;
            systems.TurnGenerator.Context.SelectUnit = originalSelection; systems.BattleSystem.SetTarget(originalTarget);
            systems.FactionState.SetAP(Team.Player, playerAP); systems.FactionState.SetAP(Team.Enemy, enemyAP);
            UnityEngine.Random.state = randomBefore;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
            foreach (var actor in originalActors) if (actor != null) actor.gameObject.SetActive(true);
            SpecialSkillRuntime.InvalidateRules(); systems.RefreshVision();
        }
    }
}
#endif
