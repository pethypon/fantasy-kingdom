using System;
using System.Collections.Generic;
using UnityEngine;

public static class SpecialSkillRuntimeTests
{
    static int checks;
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static void Check(bool condition, string name)
    { checks++; if (!condition) throw new Exception("[SpecialSkills] " + name); }
    static Status Actor(UnitSpecialSkill ability, Team team, int x, int z, int hp = 1000)
    {
        var go = new GameObject("SpecialSkillFixture"); owned.Add(go);
        var actor = go.AddComponent<Status>(); actor.kind = Kind.Archer; actor.type = Type.Unit; actor.team = team;
        actor.HP = actor.MaxHP = hp; actor.ATK = 20; actor.DEF = 0; actor.transform.position = new Vector3(x, 1, z);
        var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data); actor.GrowthData = data;
        var definition = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); owned.Add(definition);
        data.specialSkill = definition; definition.ability = ability; definition.apCost = 1; definition.cooldown = 0;
        definition.sacrificeLimit = 10; definition.lightningFactionMask = SpecialFactionMask.Hostile;
        definition.bombExplosionRadius = 1; definition.bombFactionMask = SpecialFactionMask.Hostile;
        definition.bombOnHolderDeathPolicy = BombDeathPolicy.Cancel;
        definition.sniperRecoilCurve = AnimationCurve.Linear(0,0,25,.15f);
        CombatRegistry.Register(actor); return actor;
    }
    static void See(Status observer, params Status[] actors)
    { observer.VisionCell.Clear(); foreach (var actor in actors) observer.VisionCell.Add(actor.GridPosition); }
    static void Cast(SpecialSkillCommand command, GameSystems systems)
    {
        systems.FactionState.ModifyAP(command.Caster.team, APSystem.MaximumAP);
        Check(SpecialSkillRuntime.TryExecute(command, systems, out var reason), "execute " + SpecialSkillRuntime.Definition(command.Caster).ability + ": " + reason);
    }
    static SaveSystem.UnitSaveData RoundTrip(Status actor)
        => JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(actor)));

    public static void PlayMode(GameSystems systems)
    {
        checks = 0;
        int playerAP = systems.FactionState.GetAP(Team.Player), enemyAP = systems.FactionState.GetAP(Team.Enemy);
        try
        {
            Check((int)Kind.Knight == 2 && (int)Kind.Bomber == 11 && (int)Kind.None == 16 && (int)Kind.WhiteMage == 17, "existing Kind values frozen");
            var costDefinition = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); owned.Add(costDefinition);
            int[] specifiedAP = { -1,14,0,22,14,20,8,18,10,18,20,12,15,0,13,5,12 };
            for(int ability = 1; ability < specifiedAP.Length; ability++)
            { costDefinition.ability = (UnitSpecialSkill)ability; costDefinition.ApplySpecifiedAPCost(); Check(costDefinition.apCost == specifiedAP[ability] && costDefinition.cooldown == -1, "specified AP and undecided cooldown " + ability); }
            costDefinition.ability = UnitSpecialSkill.WhiteMageHeal; costDefinition.cooldown = 0; costDefinition.apCost = APSystem.MaximumSkillAP + 1;
            Check(costDefinition.ConfigurationError != null, "active skill AP above twenty-five refuses use");
            costDefinition.apCost = APSystem.MaximumSkillAP;
            Check(costDefinition.ConfigurationError == null, "active skill AP at twenty-five is valid");
            var magic = Actor(UnitSpecialSkill.MagicRitual, Team.Player, 10000, 10000);
            var ally = Actor(UnitSpecialSkill.None, Team.Player, 10001, 10000, 200);
            var enemy = Actor(UnitSpecialSkill.None, Team.Enemy, 10002, 10000);
            enemy.DEF = 100000; See(magic, enemy);
            Cast(new SpecialSkillCommand(magic, enemy, new List<Status> { ally }), systems);
            Check(ally.HP == 170 && magic.Special.storedSacrificeDamage == 30, "ritual actual HP, maximumHP15 percent");
            Check(SpecialSkillRuntime.MovementBlocked(magic), "ritual locks movement");
            var savedMagic = RoundTrip(magic); var savedEnemy = RoundTrip(enemy);
            enemy.transform.position += Vector3.right * 100; magic.VisionCell.Clear();
            SpecialSkillRuntime.OnActorTurnStart(magic, systems, 9000);
            Check(enemy.HP == 970 && !magic.Special.ritualActive, "ritual ignores DEF, distance and lost vision");
            SaveGameApplier.ApplyStatusFields(magic, savedMagic); SaveGameApplier.ApplyStatusFields(enemy, savedEnemy);
            SpecialSkillRuntime.RestoreLinks(); SpecialSkillRuntime.OnActorTurnStart(magic, systems, 9000);
            Check(enemy.HP == 970, "ritual Save Load continuation identical");
            var assassin = Actor(UnitSpecialSkill.AssassinVisionSeal, Team.Player, 10100, 10100);
            var victim = Actor(UnitSpecialSkill.None, Team.Enemy, 10101, 10100); See(assassin, victim);
            Cast(new SpecialSkillCommand(assassin, victim), systems);
            Check(victim.Special.visionZeroTurns == 2 && assassin.Special.nextDamageDouble, "assassin seals view and permanent next-hit cost");
            var savedSeal = RoundTrip(victim); var savedAssassin = RoundTrip(assassin);
            SpecialSkillRuntime.OnActorTurnStart(victim, systems, 9001);
            SpecialSkillRuntime.OnActorTurnStart(victim, systems, 9002);
            Check(victim.Special.visionZeroTurns == 0, "vision seal expires after two owner turns");
            SaveGameApplier.ApplyStatusFields(victim, savedSeal); SaveGameApplier.ApplyStatusFields(assassin, savedAssassin);
            SpecialSkillRuntime.RestoreLinks(); SpecialSkillRuntime.OnActorTurnStart(victim, systems, 9001);
            Check(victim.Special.visionZeroTurns == 1 && assassin.Special.nextDamageDouble, "seal and vulnerability retain independent durations after load");
            SpecialSkillRuntime.OnActorTurnStart(victim, systems, 9002);
            Check(victim.Special.visionZeroTurns == 0, "loaded seal expires on the same owner turn");
            SpecialSkillRuntime.OnActorTurnStart(assassin, systems, 9001);
            SpecialSkillRuntime.OnActorTurnStart(assassin, systems, 9002);
            Check(assassin.Special.nextDamageDouble, "assassin vulnerability survives turns");
            assassin.Special.damageNullUnused = true; assassin.Special.damageNullTurns = 2;
            Check(SpecialSkillRuntime.ApplyHpChange(victim, assassin, 10, HpChangeKind.NormalDamage) == 0 && assassin.Special.nextDamageDouble, "null hit does not consume doubled damage");
            Check(SpecialSkillRuntime.ApplyHpChange(victim, assassin, 10, HpChangeKind.NormalDamage) == 20 && !assassin.Special.nextDamageDouble, "first actual hit consumes vulnerability");
            var scout = Actor(UnitSpecialSkill.ScoutVisionGuard, Team.Player, 10200, 10200);
            var atCast = Actor(UnitSpecialSkill.None, Team.Player, 10201, 10200);
            var later = Actor(UnitSpecialSkill.None, Team.Player, 10202, 10200); See(scout, scout, atCast);
            Cast(new SpecialSkillCommand(scout), systems); See(scout, scout, atCast, later);
            Check(atCast.Special.damageNullUnused && !later.Special.damageNullUnused && scout.Special.affectedUnitIds.Count == 2, "scout recipients fixed at activation");
            var savedGuard = RoundTrip(atCast);
            SpecialSkillRuntime.OnActorTurnStart(atCast, systems, 9001); SpecialSkillRuntime.OnActorTurnStart(atCast, systems, 9002);
            Check(!atCast.Special.damageNullUnused && atCast.Special.damageNullTurns == 0, "unused scout guard expires after two owner turns");
            SaveGameApplier.ApplyStatusFields(atCast, savedGuard); SpecialSkillRuntime.RestoreLinks();
            SpecialSkillRuntime.OnActorTurnStart(atCast, systems, 9001);
            Check(atCast.Special.damageNullUnused && atCast.Special.damageNullTurns == 1 && !later.Special.damageNullUnused, "loaded scout guard retains duration and fixed recipients");
            Check(SpecialSkillRuntime.ApplyHpChange(victim, atCast, 10, HpChangeKind.IgnoreDefenceDamage) == 0, "scout blocks defence-ignoring hit");
            atCast.Special.damageNullUnused = true; atCast.Special.damageNullTurns = 2; atCast.ShieldTurns = 5;
            Check(SpecialSkillRuntime.ApplyHpChange(scout, atCast, 15, HpChangeKind.DirectHpReduction) == 15 && atCast.Special.damageNullUnused, "direct HP cost bypasses all damage defenses");
            var guardian = Actor(UnitSpecialSkill.GuardianLifeLink, Team.Player, 10300, 10300);
            var ward = Actor(UnitSpecialSkill.None, Team.Player, 10301, 10300, 100); See(guardian, ward);
            Cast(new SpecialSkillCommand(guardian, ward), systems);
            Check(SpecialSkillRuntime.ApplyHpChange(victim, guardian, 20, HpChangeKind.NormalDamage) == 20 && ward.HP == 80 && guardian.HP == 1000, "guardian redirects once without loop");
            var savedGuardian = RoundTrip(guardian); var savedWard = RoundTrip(ward);
            guardian.Special.guardianPartnerId = ward.Special.guardianPartnerId = null;
            SaveGameApplier.ApplyStatusFields(guardian, savedGuardian); SaveGameApplier.ApplyStatusFields(ward, savedWard); SpecialSkillRuntime.RestoreLinks();
            Check(guardian.Special.guardianPartnerId == ward.ReflectionLifeId, "link restored by stable life ID");
            SpecialSkillRuntime.ApplyHpChange(victim, guardian, 90, HpChangeKind.NormalDamage);
            Check(!guardian.IsAlive && ward.HP == 80 && string.IsNullOrEmpty(ward.Special.guardianPartnerId), "lethal exchange spares recipient and unlinks");
            var crossbow = Actor(UnitSpecialSkill.CrossbowStakes, Team.Player, 10400, 10400);
            var a = Actor(UnitSpecialSkill.None, Team.Enemy, 10401, 10400);
            var b = Actor(UnitSpecialSkill.None, Team.Enemy, 10403, 10400);
            var c = Actor(UnitSpecialSkill.None, Team.Enemy, 10404, 10400); See(crossbow, a,b,c);
            Cast(new SpecialSkillCommand(crossbow,a), systems); crossbow.Special.ownerTurn++;
            Cast(new SpecialSkillCommand(crossbow,b), systems); int appliedB = crossbow.Special.stakeBAppliedTurn; crossbow.Special.ownerTurn++;
            Cast(new SpecialSkillCommand(crossbow,c), systems);
            Check(crossbow.Special.stakeAId == b.ReflectionLifeId && crossbow.Special.stakeBId == c.ReflectionLifeId && crossbow.Special.stakeAAppliedTurn == appliedB, "third stake FIFO preserves age");
            var savedStakes = RoundTrip(crossbow); SaveGameApplier.ApplyStatusFields(crossbow,savedStakes); SpecialSkillRuntime.RestoreLinks();
            Check(crossbow.Special.stakeAId == b.ReflectionLifeId && crossbow.Special.stakeAAppliedTurn == appliedB, "stakes survive load");
            var priest = Actor(UnitSpecialSkill.PriestPurification, Team.Player, 10402,10400); See(priest,priest);
            crossbow.ActiveEffects.Add(new ActiveEffect(BuffType.Offensive,2));
            Cast(new SpecialSkillCommand(priest),systems);
            Check(!priest.IsAlive && crossbow.ActiveEffects.Count == 0 && crossbow.Special.stakeAId == b.ReflectionLifeId, "purification kills priest, preserves special stakes");
            var sniper = Actor(UnitSpecialSkill.MagicSniperLock, Team.Player, 10500,10500); var distant = Actor(UnitSpecialSkill.None,Team.Enemy,10501,10500); See(sniper,distant);
            Cast(new SpecialSkillCommand(sniper,distant),systems);
            var savedSniper = RoundTrip(sniper);
            SpecialSkillRuntime.OnActorTurnStart(sniper,systems,9003); int sniperHPAfterRecoil = sniper.HP;
            SaveGameApplier.ApplyStatusFields(sniper,savedSniper); SpecialSkillRuntime.RestoreLinks();
            SpecialSkillRuntime.OnActorTurnStart(sniper,systems,9003);
            Check(sniper.HP == sniperHPAfterRecoil && sniper.HP < savedSniper.HP, "loaded sniper lock charges identical owner-turn recoil");
            SpecialSkillRuntime.OnActorTurnStart(sniper,systems,9003);
            Check(sniper.HP == sniperHPAfterRecoil, "sniper owner-turn recoil cannot run twice in one round");
            sniper.VisionCell.Clear(); distant.transform.position += Vector3.right*100;
            Check(SpecialSkillRuntime.TryGetSniperTarget(sniper,out var locked) && locked == distant && !SpecialSkillRuntime.CanNormalAttackTarget(sniper,enemy), "sniper exclusive legal lock outside vision");
            Check(sniper.GrowthData.specialSkill.SniperMultiplier(100) == 2.5f && sniper.GrowthData.specialSkill.SniperRecoil(100) == .15f, "sniper caps share definition");
            var bomb = Actor(UnitSpecialSkill.LifeBomb,Team.Player,10600,10600);
            var carrier = Actor(UnitSpecialSkill.None,Team.Player,10601,10600,100);
            var blastTarget = Actor(UnitSpecialSkill.None,Team.Enemy,10602,10600,1000); See(bomb,carrier);
            Cast(new SpecialSkillCommand(bomb,carrier),systems); var savedBomb = RoundTrip(carrier);
            carrier.Special = new SpecialState(); SaveGameApplier.ApplyStatusFields(carrier,savedBomb); SpecialSkillRuntime.RestoreLinks();
            int due = carrier.Special.bombDueRound;
            SpecialSkillRuntime.OnActorTurnStart(carrier,systems,due);
            Check(carrier.HP == 20 && blastTarget.HP == 920 && string.IsNullOrEmpty(carrier.Special.bombCasterId), "bomb maximumHP80 and delayed Load continuation");
            var archer = Actor(UnitSpecialSkill.ArcherStationary,Team.Player,10700,10700);
            archer.GrowthData.specialSkill.stationaryAttackCurve = AnimationCurve.Linear(0,1,3,2);
            SpecialSkillRuntime.OnActorTurnStart(archer,systems,9004); SpecialSkillRuntime.OnActorTurnStart(archer,systems,9005);
            Check(SpecialSkillRuntime.GetAttackMultiplier(archer,enemy)>1, "stationary charge grows");
            SpecialSkillRuntime.OnAttackLanded(archer); Check(archer.Special.stationaryTurns == 1, "attacking preserves stationary charge");
            SpecialSkillRuntime.OnMoved(archer,archer.transform.position,archer.transform.position+Vector3.right,systems);
            Check(archer.Special.stationaryTurns == 0 && archer.GrowthData.specialSkill.ArcherMultiplier(100) == 1.75f, "forced movement clears charge, cap applies");
            sniper.Special.sniperTargetId="missing-life"; crossbow.Special.stakeAId="missing-life"; SpecialSkillRuntime.RestoreLinks();
            Check(!sniper.Special.sniperActive && crossbow.Special.stakeAId == null, "missing load targets clear only dependent state");
            magic.GrowthData.specialSkill.apCost=-1;
            Check(!SpecialSkillRuntime.CanUse(magic,systems,out _), "undecided AP never acts free");
            var pooled = Actor(UnitSpecialSkill.None,Team.Player,10900,10900,100);
            string oldLife = pooled.ReflectionLifeId;
            SpecialSkillRuntime.ApplyHpChange(pooled,pooled,100,HpChangeKind.DirectHpReduction);
            pooled.HP=pooled.MaxHP=100; pooled.gameObject.SetActive(true);
            Check(pooled.ReflectionLifeId!=oldLife && SpecialSkillRuntime.FindActor(oldLife)==null,"direct-cost death cannot redirect an old lock into a pooled actor's next life");
            var randomBefore = UnityEngine.Random.state;
            try
            {
                UnityEngine.Random.InitState(9182); float expectedNext = UnityEngine.Random.value; UnityEngine.Random.InitState(9182);
                for(int repeat=0; repeat<10; repeat++)
                {
                    archer.AssignedSkillId=1; archer.specialAbility=SpecialAbility.SurvivalInstinct; archer.passiveskill=PassiveSkill.Impregnable;
                    archer.GrowthData.InitializeAbilities(archer); SkillData.AssignRandomSkill(archer); SpecialAbilityData.AssignRandom(archer); SkillData.OfferPlayerChoice(archer);
                    Check(archer.AssignedSkillId == -1 && archer.specialAbility == SpecialAbility.None && archer.passiveskill == PassiveSkill.None, "fixed identity never gains a random replacement " + repeat);
                }
                Check(UnityEngine.Random.value == expectedNext,"ability assignment consumes no random state");
                archer.Special.stationaryTurns=2;
                var fixedSave=RoundTrip(archer); fixedSave.AssignedSkillId=1; fixedSave.SpecialAbility="SurvivalInstinct";
                SaveGameApplier.ApplyStatusFields(archer,fixedSave);
                Check(archer.AssignedSkillId==-1 && archer.specialAbility==SpecialAbility.None && archer.Special.stationaryTurns==2,"old random save assignments cannot replace fixed identity or erase durable state");
                archer.GrowthData.specialSkill.enabledForPlay=false;
                Check(!AISpecialSkillKnowledge.NeedsSpecialStateModel(new AIAction{Unit=archer,ActionType=AIActionType.Move}),"disabled authoring draft retains ordinary minimax path");
                var disabledRage = Actor(UnitSpecialSkill.BerserkerRage,Team.Player,11000,11000);
                var rageDefinition = disabledRage.GrowthData.specialSkill;
                rageDefinition.berserkerAttackAPCurve=AnimationCurve.Linear(0,1,1,1);
                rageDefinition.berserkerDefenceCurve=AnimationCurve.Linear(0,.5f,5,.5f);
                rageDefinition.enabledForPlay=false; disabledRage.Special.berserkerStage=3;
                Check(SpecialSkillRuntime.AttackAPAdjustment(disabledRage,7)==7 && SpecialSkillRuntime.GetDEFModifier(disabledRage)==1,"disabled Berserker changes neither normal attack AP nor defence");
                var disabledSamurai = Actor(UnitSpecialSkill.SamuraiCounter,Team.Player,11001,11000);
                var samuraiDefinition=disabledSamurai.GrowthData.specialSkill;
                samuraiDefinition.samuraiGaugeMax=10; samuraiDefinition.samuraiGaugeGain=10; samuraiDefinition.enabledForPlay=false;
                SpecialSkillRuntime.OnAttackLanded(disabledSamurai);
                Check(disabledSamurai.Special.samuraiGauge==0,"disabled Samurai cannot gain counter gauge");
                disabledSamurai.Special.samuraiGauge=10; disabledSamurai.Special.samuraiMaxTurns=3;
                int sourceHP=victim.HP;
                SpecialSkillRuntime.ApplyHpChange(victim,disabledSamurai,10,HpChangeKind.NormalDamage);
                Check(victim.HP==sourceHP && disabledSamurai.HP==990,"disabled loaded Samurai gauge cannot trigger a counter");
                var refreshed = Actor(UnitSpecialSkill.ArcherStationary,Team.Player,11002,11000);
                refreshed.kind=Kind.None; refreshed.GrowthData.useAuthoredAbilities=true;
                refreshed.GrowthData.authoredSkillId=1; refreshed.GrowthData.authoredPassive=PassiveSkill.Impregnable;
                refreshed.GrowthData.authoredSpecialAbility=SpecialAbility.Tenacity;
                refreshed.GrowthData.specialSkill.enabledForPlay=false; refreshed.Special.stationaryTurns=2;
                systems.UnitSetting.RefreshRuntimeBalance();
                Check(refreshed.AssignedSkillId==-1 && refreshed.passiveskill==PassiveSkill.None && refreshed.specialAbility==SpecialAbility.None && refreshed.Special.stationaryTurns==2,"live studio refresh respects fixed draft identity without resetting special state");
            }
            finally { UnityEngine.Random.state=randomBefore; }
            Debug.Log("[SpecialSkills] " + checks + " runtime/save/damage checks passed");
        }
        finally
        {
            systems.FactionState.ModifyAP(Team.Player,playerAP-systems.FactionState.GetAP(Team.Player));
            systems.FactionState.ModifyAP(Team.Enemy,enemyAP-systems.FactionState.GetAP(Team.Enemy));
            foreach(var item in owned) if(item != null) UnityEngine.Object.DestroyImmediate(item); owned.Clear();
            systems.RefreshVision();
        }
    }
}
