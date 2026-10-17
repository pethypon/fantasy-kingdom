#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Real action and continuation checks, separate from UI and common damage fixtures.</summary>
public static class SpecialSkillActionTests
{
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static int passed;
    static void Check(string name, bool condition)
    { if (!condition) throw new InvalidOperationException("[SpecialSkillActions] FAIL " + name); passed++; Debug.Log("[SpecialSkillActions] PASS " + name); }
    static Status Actor(UnitSpecialSkill ability, Team team, int x, int z, int hp = 1000)
    {
        var go = new GameObject("Special action fixture"); owned.Add(go);
        var actor = go.AddComponent<Status>(); actor.type = Type.Unit; actor.team = team; actor.kind = Kind.Archer;
        actor.HP = actor.MaxHP = hp; actor.ATK = 20; actor.DEF = 0; actor.transform.position = new Vector3(x, 1, z);
        actor.VisionCell = new HashSet<Vector3Int>(); actor.Special = new SpecialState();
        var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data); actor.GrowthData = data;
        var d = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); owned.Add(d); data.specialSkill = d;
        d.ability = ability; d.apCost = 1; d.cooldown = 0; d.sacrificeLimit = 10;
        d.lightningFactionMask = SpecialFactionMask.Hostile;
        d.crossbowInitialAmmo = 0;
        d.bombExplosionRadius = 1; d.bombFactionMask = SpecialFactionMask.Hostile; d.bombOnHolderDeathPolicy = BombDeathPolicy.Cancel;
        CombatRegistry.Register(actor); return actor;
    }
    static void See(Status observer, params Status[] targets)
    { observer.VisionCell.Clear(); foreach (var target in targets) observer.VisionCell.Add(target.GridPosition); }
    static void Cast(GameSystems systems, Status caster, Status target = null, List<Status> sacrifices = null, int berserkerStage = 0)
    {
        systems.FactionState.SetAP(caster.team, APSystem.MaximumAP);
        Check("execute " + SpecialSkillRuntime.Definition(caster).ability,
            SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(caster, target, sacrifices, berserkerStage), systems, out _));
    }
    static SaveSystem.UnitSaveData Save(Status unit)
        => JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(unit)));
    static void Load(Status unit, SaveSystem.UnitSaveData saved)
    { SaveGameApplier.ApplyStatusFields(unit, saved); SpecialSkillRuntime.RestoreLinks(); }
    static void ShootStake(GameSystems systems, Status caster, Status target)
    {
        Cast(systems, caster);
        Check("stake round follows a successful normal shot", SpecialSkillRuntime.TryBeginNormalShot(caster, out var round) && round == CrossbowRoundType.Stake);
        SpecialSkillRuntime.CompleteNormalShot(caster, target, round, true);
    }

    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        int playerAP = systems.FactionState.GetAP(Team.Player), enemyAP = systems.FactionState.GetAP(Team.Enemy);
        var mapBefore = systems.MapCreate; var moveMapBefore = systems.MoveGenerator.mapcreate; var visionBefore = systems.VisionGenerator;
        try
        {
            var geometry = new GameObject("Special action deterministic map"); geometry.SetActive(false); owned.Add(geometry);
            var map = geometry.AddComponent<MapCreate>(); map.UseR1Terrain = false; map.SetPos = new List<Vector3>();
            for (int x = 40000; x <= 40300; x++) for (int z = 40000; z <= 40020; z++) map.SetPos.Add(new Vector3(x, 1, z));
            systems.MapCreate = map; systems.MoveGenerator.mapcreate = map; systems.VisionGenerator = null;
            var knight = Actor(UnitSpecialSkill.KnightBreakthrough, Team.Player, 40000, 40000); knight.kind = Kind.Knight; knight.direction = Direction.N;
            var front = Actor(UnitSpecialSkill.None, Team.Enemy, 40000, 40001);
            var second = Actor(UnitSpecialSkill.None, Team.Enemy, 40000, 40002);
            Cast(systems, knight);
            Check("Knight advances three cells and pushes a whole chain", knight.GridPosition.z == 40003 && front.GridPosition.z == 40004 && second.GridPosition.z == 40005);
            var wall = Actor(UnitSpecialSkill.None, Team.Enemy, 40000, 40006); wall.type = Type.Building;
            Check("ordinary buildings stop the entire chain before moving anyone", !SpecialSkillRuntime.IsLegalTarget(knight, knight, out _)
                && knight.GridPosition.z == 40003 && front.GridPosition.z == 40004 && second.GridPosition.z == 40005);
            wall.gameObject.SetActive(false);
            var allyBlock = Actor(UnitSpecialSkill.None, Team.Player, 40000, 40006);
            Check("allied unit behind hostile chain blocks breakthrough", !SpecialSkillRuntime.IsLegalTarget(knight, knight, out _));
            allyBlock.gameObject.SetActive(false);
            var deniedTile = new Vector3(40000, 1, 40006); map.SetPos.Remove(deniedTile);
            Check("missing tile behind chain blocks breakthrough", !SpecialSkillRuntime.IsLegalTarget(knight, knight, out _));
            map.SetPos.Add(deniedTile);

            var lineCaster = Actor(UnitSpecialSkill.CrossbowStakes, Team.Enemy, 40080, 40000);
            var a = Actor(UnitSpecialSkill.None, Team.Player, 40098, 40003);
            var b = Actor(UnitSpecialSkill.None, Team.Player, 40102, 40003);
            See(lineCaster, a, b); ShootStake(systems, lineCaster, a); ShootStake(systems, lineCaster, b);
            // Keep the same rear-attack relationship before/after crossing so this fixture
            // isolates one line hit, rather than also changing the normal damage formula.
            var runner = Actor(UnitSpecialSkill.KnightBreakthrough, Team.Player, 40100, 40001); runner.kind = Kind.Knight; runner.direction = Direction.N;
            int lineHit = Mathf.FloorToInt(DamageCalculator.CalcNormal(lineCaster, runner) * SpecialSkillRuntime.LightningMultiplier(lineCaster));
            Cast(systems, runner);
            Check("Knight's three-step action receives one hit when entering a given line"
                + $" (z={runner.GridPosition.z}, HP={runner.HP}/{runner.MaxHP}, expected loss={lineHit})",
                runner.GridPosition.z == 40004 && runner.HP == runner.MaxHP - lineHit);
            runner.HP = runner.MaxHP;
            int repeatedHit = Mathf.FloorToInt(DamageCalculator.CalcNormal(lineCaster, runner) * SpecialSkillRuntime.LightningMultiplier(lineCaster));
            SpecialSkillRuntime.OnMoved(runner, new Vector3(40100, 1, 40001), new Vector3(40100, 1, 40009), systems);
            Check("crossing many lightning cells in one move is one hit", runner.HP == runner.MaxHP - repeatedHit);
            Check("missing observed endpoint contributes no secret line", SpecialSkillRuntime.HazardDamageAt(runner, new Vector3(40100, 1, 40008), new List<Status> { lineCaster, a }) == 0);

            var mage = Actor(UnitSpecialSkill.MagicRitual, Team.Player, 40200, 40000);
            var offering = Actor(UnitSpecialSkill.None, Team.Player, 40201, 40000, 100);
            var ritualTarget = Actor(UnitSpecialSkill.None, Team.Enemy, 40202, 40000); See(mage, ritualTarget);
            Cast(systems, mage, ritualTarget, new List<Status> { offering });
            SpecialSkillRuntime.ApplyHpChange(ritualTarget, mage, mage.HP, HpChangeKind.ForcedDeath);
            SpecialSkillRuntime.OnActorTurnStart(mage, systems, 20001);
            Check("dead ritual caster cancels fire without refund", ritualTarget.HP == ritualTarget.MaxHP && offering.HP == 85);
            var mage2 = Actor(UnitSpecialSkill.MagicRitual, Team.Player, 40210, 40000);
            var offering2 = Actor(UnitSpecialSkill.None, Team.Player, 40211, 40000, 100);
            var deceasedTarget = Actor(UnitSpecialSkill.None, Team.Enemy, 40212, 40000); See(mage2, deceasedTarget);
            Cast(systems, mage2, deceasedTarget, new List<Status> { offering2 });
            SpecialSkillRuntime.ApplyHpChange(mage2, deceasedTarget, deceasedTarget.HP, HpChangeKind.ForcedDeath);
            SpecialSkillRuntime.OnActorTurnStart(mage2, systems, 20002);
            Check("dead ritual target never retargets another enemy", !mage2.Special.ritualActive && ritualTarget.HP == ritualTarget.MaxHP);

            var bomber = Actor(UnitSpecialSkill.LifeBomb, Team.Player, 40205, 40010);
            var fragileCarrier = Actor(UnitSpecialSkill.None, Team.Player, 40206, 40010, 100); fragileCarrier.HP = 30;
            var adjacentEnemy = Actor(UnitSpecialSkill.None, Team.Enemy, 40207, 40010);
            Cast(systems, bomber, fragileCarrier); var carrierBombSave = Save(fragileCarrier);
            SpecialSkillRuntime.ApplyHpChange(adjacentEnemy, bomber, bomber.HP, HpChangeKind.ForcedDeath);
            Load(fragileCarrier, carrierBombSave);
            SpecialSkillRuntime.OnActorTurnStart(fragileCarrier, systems, fragileCarrier.Special.bombDueRound);
            Check("a dead caster does not cancel the saved bomb and low current HP does not reduce its power", !fragileCarrier.IsAlive && adjacentEnemy.HP == 920);
            var deathBomber = Actor(UnitSpecialSkill.LifeBomb, Team.Player, 40215, 40010);
            deathBomber.GrowthData.specialSkill.bombOnHolderDeathPolicy = BombDeathPolicy.Explode;
            var deathCarrier = Actor(UnitSpecialSkill.None, Team.Player, 40216, 40010, 100);
            var deathNeighbor = Actor(UnitSpecialSkill.None, Team.Enemy, 40217, 40010);
            Cast(systems, deathBomber, deathCarrier);
            SpecialSkillRuntime.ApplyHpChange(deathNeighbor, deathCarrier, deathCarrier.HP, HpChangeKind.ForcedDeath);
            Check("configured holder-death explosion occurs once before pooled HP reset", !deathCarrier.IsAlive && deathNeighbor.HP == 920 && string.IsNullOrEmpty(deathCarrier.Special.bombCasterId));

            var white = Actor(UnitSpecialSkill.WhiteMageHeal, Team.Player, 40220, 40000);
            var patient = Actor(UnitSpecialSkill.None, Team.Player, 40221, 40000, 200); patient.HP = 50; See(white, patient);
            Cast(systems, white, patient);
            Check("White Mage heals thirty percent of maximum HP", patient.HP == 110);
            See(white);
            int ap = systems.FactionState.GetAP(white.team);
            Check("White Mage cannot heal an unseen ally or consume AP", !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(white, patient), systems, out _)
                && systems.FactionState.GetAP(white.team) == ap);

            var holy = Actor(UnitSpecialSkill.HolyKnightSanctuary, Team.Player, 40230, 40000, 100); holy.HP = 40;
            var sanctuary = holy.GrowthData.specialSkill;
            sanctuary.holyKnightArea = new[] { Vector2Int.zero, Vector2Int.right };
            sanctuary.holyKnightInterruptionConfigured = true;
            var nearby = Actor(UnitSpecialSkill.None, Team.Player, 40231, 40000, 100); nearby.HP = 20;
            Cast(systems, holy); var holySave = Save(holy); var nearbySave = Save(nearby);
            SpecialSkillRuntime.OnActorTurnStart(holy, systems, 20003);
            int healedHoly = holy.HP, healedNearby = nearby.HP;
            Check("Holy Knight heals configured shape and remains immobilized", healedHoly == 50 && healedNearby == 30 && SpecialSkillRuntime.MovementBlocked(holy) && SpecialSkillRuntime.AttackBlocked(holy));
            Load(holy, holySave); Load(nearby, nearbySave); SpecialSkillRuntime.OnActorTurnStart(holy, systems, 20003);
            Check("Holy Knight Save Load resumes the same scheduled healing", holy.HP == healedHoly && nearby.HP == healedNearby);
            SpecialSkillRuntime.OnActorTurnStart(holy, systems, 20004); SpecialSkillRuntime.OnActorTurnStart(holy, systems, 20005);
            Check("the third healing turn retains Holy Knight action and damage penalties", holy.HP == 70 && SpecialSkillRuntime.MovementBlocked(holy)
                && SpecialSkillRuntime.AttackBlocked(holy) && SpecialSkillRuntime.PreviewDamage(holy, 5) == 10);
            var finalSanctuarySave = Save(holy); SpecialSkillRuntime.OnActorTurnStart(holy, systems, 20006);
            Check("Holy Knight becomes mobile on the following turn without a fourth heal", holy.HP == 70 && !SpecialSkillRuntime.MovementBlocked(holy));
            Load(holy, finalSanctuarySave); SpecialSkillRuntime.OnActorTurnStart(holy, systems, 20006);
            Check("final sanctuary turn Save Load preserves the exact end boundary", holy.HP == 70 && !SpecialSkillRuntime.MovementBlocked(holy));

            var samurai = Actor(UnitSpecialSkill.SamuraiCounter, Team.Player, 40240, 40000); samurai.HP = 200;
            var samuraiDefinition = samurai.GrowthData.specialSkill; samuraiDefinition.samuraiGaugeGain = 1; samuraiDefinition.samuraiGaugeMax = 3;
            var assailant = Actor(UnitSpecialSkill.None, Team.Enemy, 40241, 40000);
            SpecialSkillRuntime.OnAttackLanded(samurai, false);
            Check("skill damage cannot fill Samurai's normal-attack gauge", samurai.Special.samuraiGauge == 0);
            SpecialSkillRuntime.OnAttackLanded(samurai); SpecialSkillRuntime.OnAttackLanded(samurai); SpecialSkillRuntime.OnAttackLanded(samurai);
            var samuraiSave = Save(samurai); var assailantSave = Save(assailant);
            SpecialSkillRuntime.ApplyHpChange(assailant, samurai, 20, HpChangeKind.NormalDamage);
            Check("Samurai receives attack then counters and heals actual damage", samurai.HP == 210 && assailant.HP == 970 && samurai.Special.samuraiGauge == 0);
            Load(samurai, samuraiSave); Load(assailant, assailantSave); SpecialSkillRuntime.ApplyHpChange(assailant, samurai, 20, HpChangeKind.NormalDamage);
            Check("Samurai gauge Save Load produces identical counter", samurai.HP == 210 && assailant.HP == 970);

            var ninja = Actor(UnitSpecialSkill.NinjaSwap, Team.Player, 40250, 40000);
            var carrier = Actor(UnitSpecialSkill.None, Team.Player, 40252, 40000); ninja.GrowthData.specialSkill.ninjaSwapRange = -1;
            ninja.GrowthData.specialSkill.ninjaChargeCurve = AnimationCurve.Linear(0, 1, 5, 2);
            carrier.Special.bombCasterId = ninja.ReflectionLifeId; carrier.Special.bombDueRound = 30000;
            carrier.Special.bombRadius = 1; carrier.Special.bombMask = SpecialFactionMask.Hostile; carrier.Special.bombDeathPolicy = BombDeathPolicy.Cancel; carrier.Special.bombCasterTeam = Team.Player;
            ninja.Special.ninjaCharge = 4; var ninjaSave = Save(ninja); var carrierSave = Save(carrier);
            Cast(systems, ninja, carrier);
            Check("Ninja exchanges only positions and transports the carrier's bomb", ninja.GridPosition.x == 40252 && carrier.GridPosition.x == 40250
                && carrier.Special.bombCasterId == ninja.ReflectionLifeId && ninja.Special.ninjaCharge == 4);
            ninja.transform.position = new Vector3(40250, 1, 40000); carrier.transform.position = new Vector3(40252, 1, 40000);
            Load(ninja, ninjaSave); Load(carrier, carrierSave); Cast(systems, ninja, carrier);
            Check("Ninja and bomb Save Load reproduce the same transport", ninja.GridPosition.x == 40252 && carrier.GridPosition.x == 40250 && carrier.Special.bombDueRound == 30000);
            Check("Ninja rejects enemy swap", !SpecialSkillRuntime.IsLegalTarget(ninja, assailant, out _));
            var remote = Actor(UnitSpecialSkill.None, Team.Player, 40300, 40020);
            Check("Ninja can swap an allied unit across the map without vision or a legacy range", !SpecialSkillRuntime.InVision(ninja, remote)
                && ninja.GrowthData.specialSkill.ConfigurationError == null && SpecialSkillRuntime.IsLegalTarget(ninja, remote, out _));
            Cast(systems, ninja, remote);
            Check("Ninja executes its map-wide allied swap", ninja.GridPosition.x == 40300 && remote.GridPosition.x == 40252);

            var berserker = Actor(UnitSpecialSkill.BerserkerRage, Team.Player, 40260, 40000);
            var rage = berserker.GrowthData.specialSkill;
            rage.berserkerAttackCurve = AnimationCurve.Linear(0, 1, 5, 2.5f);
            rage.berserkerDefenceCurve = AnimationCurve.Linear(0, 1, 5, .5f);
            rage.berserkerAttackAPCurve = AnimationCurve.Linear(0, 1, 1, 5);
            systems.FactionState.SetAP(Team.Player, APSystem.MaximumAP);
            int rageAPCap = systems.FactionState.GetAP(Team.Player);
            Check("Berserker cannot skip straight to third stage or spend AP", !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(berserker, berserkerStage: 3), systems, out _)
                && berserker.Special.berserkerStage == 0 && systems.FactionState.GetAP(Team.Player) == rageAPCap);
            Cast(systems, berserker);
            Check("Berserker first use reaches only stage one for five AP", berserker.Special.berserkerStage == 1
                && systems.FactionState.GetAP(Team.Player) == rageAPCap - 5 && SpecialSkillRuntime.GetAPCost(berserker) == 8);
            Cast(systems, berserker); Cast(systems, berserker);
            Check("Berserker repeated uses progress to stage three for eleven AP", berserker.Special.berserkerStage == 3
                && SpecialSkillRuntime.GetAPCost(berserker) == 15 && systems.FactionState.GetAP(Team.Player) == rageAPCap - 11);
            var rageSave = Save(berserker); Cast(systems, berserker, berserkerStage: 4); float fourthStage = SpecialSkillRuntime.GetAttackMultiplier(berserker, assailant);
            Load(berserker, rageSave); Cast(systems, berserker, berserkerStage: 4);
            Check("Berserker stage Save Load preserves authored strength", Mathf.Approximately(SpecialSkillRuntime.GetAttackMultiplier(berserker, assailant), fourthStage));
            systems.FactionState.SetAP(Team.Player, 19);
            Check("Berserker validates next-stage affordability", !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(berserker), systems, out _)
                && berserker.Special.berserkerStage == 4 && systems.FactionState.GetAP(Team.Player) == 19);
            Check("Berserker final enhancement uses specified twenty AP", SpecialSkillRuntime.GetAPCost(berserker, 5) == 20);
            Cast(systems, berserker, berserkerStage: 5);
            Check("Berserker maximum stage caps ATK and applies DEF/AP costs", SpecialSkillRuntime.GetAttackMultiplier(berserker, assailant) == 2.5f
                && SpecialSkillRuntime.GetDEFModifier(berserker) == .5f && SpecialSkillRuntime.AttackAPAdjustment(berserker, 2) == 6);
            systems.FactionState.SetAP(Team.Player, APSystem.MaximumAP);
            Check("maximum-stage Berserker cannot cast again or pay AP", !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(berserker), systems, out _)
                && berserker.Special.berserkerStage == 5 && systems.FactionState.GetAP(Team.Player) == rageAPCap);
            berserker.ActiveEffects.Add(new ActiveEffect(BuffType.Insight, 2)); SpecialSkillRuntime.OnAttackLanded(berserker, false);
            Check("non-normal damage preserves Berserker's next normal-attack enhancement", berserker.Special.berserkerStage == 5);
            SpecialSkillRuntime.OnAttackLanded(berserker);
            Check("one Berserker attack clears only its own rage", berserker.Special.berserkerStage == 0 && berserker.ActiveEffects.Count == 1);
            Cast(systems, berserker);
            Check("Berserker restarts at first stage after its normal attack", berserker.Special.berserkerStage == 1);
            systems.FactionState.SetAP(Team.Player, APSystem.MaximumAP);
            Check("stale Berserker command cannot replay its previous stage", !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(berserker, berserkerStage: 1), systems, out _)
                && berserker.Special.berserkerStage == 1 && systems.FactionState.GetAP(Team.Player) == rageAPCap);

            var navigator = Actor(UnitSpecialSkill.NavigatorPull, Team.Player, 40270, 40000); navigator.direction = Direction.N;
            navigator.GrowthData.specialSkill.navigatorAPDamageCurve = AnimationCurve.Linear(0, 1, APSystem.MaximumAP, 2);
            var pulled = Actor(UnitSpecialSkill.ArcherStationary, Team.Enemy, 40273, 40003); pulled.Special.stationaryTurns = 3; See(navigator, pulled);
            Cast(systems, navigator, pulled);
            Check("Navigator pulls to its one front cell and resets forced movement charge", pulled.GridPosition == new Vector3Int(40270, 0, 40001) && pulled.Special.stationaryTurns == 0 && pulled.HasMovedThisTurn);
            Check("Navigator cannot repull an already front target", !SpecialSkillRuntime.IsLegalTarget(navigator, pulled, out _));
            var navigation = navigator.GrowthData.specialSkill; navigation.navigatorAPDamageCurve = AnimationCurve.Linear(0, 1, APSystem.MaximumAP, 2);
            systems.FactionState.SetAP(Team.Player, APSystem.MaximumAP);
            Check("Navigator AP damage caps at one point five", SpecialSkillRuntime.GetAttackMultiplier(navigator, pulled) == 1.5f);
            Debug.Log("[SpecialSkillActions] SUMMARY passed=" + passed);
        }
        finally
        {
            systems.MapCreate = mapBefore; systems.MoveGenerator.mapcreate = moveMapBefore; systems.VisionGenerator = visionBefore;
            systems.FactionState.SetAP(Team.Player, playerAP); systems.FactionState.SetAP(Team.Enemy, enemyAP);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear(); systems.RefreshVision();
        }
    }
}
#endif
