#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Authoritative ammunition/normal attack, deterministic continuation, and moving-line regression.</summary>
public static class CrossbowR1Tests
{
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static int checks;
    static void Check(string name, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[CrossbowR1] FAIL " + name);
        checks++; Debug.Log("[CrossbowR1] PASS " + name);
    }
    static Status Actor(Team team, int x, int z, int initialAmmo = -1)
    {
        var go = new GameObject("Crossbow R1 fixture"); owned.Add(go);
        var actor = go.AddComponent<Status>(); actor.type = Type.Unit; actor.team = team;
        actor.kind = initialAmmo >= 0 ? Kind.Crossbow : Kind.Archer;
        actor.HP = actor.MaxHP = 10000; actor.ATK = 20; actor.DEF = 0;
        actor.Level = 100; // Ammunition assertions are independent of XP growth.
        actor.transform.position = new Vector3(x, 1, z); actor.VisionCell = new HashSet<Vector3Int>();
        var data = ScriptableObject.CreateInstance<UnitData>(); owned.Add(data);
        data.definitionId = "crossbow-r1-fixture"; actor.GrowthData = data;
        if (initialAmmo >= 0)
        {
            var definition = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); owned.Add(definition);
            data.specialSkill = definition; definition.ability = UnitSpecialSkill.CrossbowStakes;
            definition.apCost = 10; definition.cooldown = 0; definition.crossbowInitialAmmo = initialAmmo;
            definition.lightningFactionMask = SpecialFactionMask.Hostile;
        }
        CombatRegistry.Register(actor); return actor;
    }
    static void LoadStake(Status actor, GameSystems systems)
    {
        systems.FactionState.SetAP(actor.team, APSystem.MaximumAP);
        Check("stake loading uses the shared AP action", SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(actor), systems, out _));
    }
    static void Shot(Status attacker, Status target, GameSystems systems)
    {
        systems.TurnGenerator.Context.SelectUnit = attacker;
        systems.BattleSystem.SetTarget(target);
        systems.BattleSystem.ProcessDamage(systems.TurnGenerator);
    }
    static string Magazine(Status actor) => string.Join(",", SpecialSkillRuntime.CrossbowMagazine(actor));
    static void AttachForLine(Status caster, Status target, GameSystems systems)
    {
        LoadStake(caster, systems);
        Check("line setup consumes a loaded stake", SpecialSkillRuntime.TryBeginNormalShot(caster, out var round) && round == CrossbowRoundType.Stake);
        SpecialSkillRuntime.CompleteNormalShot(caster, target, round, true);
    }

    public static void Run(GameSystems systems)
    {
        checks = 0;
        var previousMap = systems.MapCreate; var previousVision = systems.VisionGenerator;
        var previousSelection = systems.TurnGenerator.Context.SelectUnit;
        var previousTarget = systems.BattleSystem.Target;
        int playerAP = systems.FactionState.GetAP(Team.Player), enemyAP = systems.FactionState.GetAP(Team.Enemy);
        var unityRandom = UnityEngine.Random.state;
        try
        {
            systems.MapCreate = null; systems.VisionGenerator = null;
            var bow = Actor(Team.Enemy, 110000, 110000, 0);
            var target = Actor(Team.Player, 110001, 110000);
            Check("empty magazine cannot attack", SpecialSkillRuntime.CrossbowAmmoCount(bow) == 0
                && !SpecialSkillRuntime.CanNormalAttackTarget(bow, target));
            int before = target.HP; Shot(bow, target, systems);
            Check("authoritative normal attack refuses empty ammunition", target.HP == before);
            LoadStake(bow, systems);
            Check("skill loads one stake without attaching to an enemy", SpecialSkillRuntime.CrossbowAmmoCount(bow) == 1
                && SpecialSkillRuntime.CrossbowPeekRound(bow) == CrossbowRoundType.Stake && string.IsNullOrEmpty(bow.Special.stakeAId));
            int ap = systems.FactionState.GetAP(bow.team);
            Check("old direct-target skill is rejected without payment", !SpecialSkillRuntime.TryExecute(new SpecialSkillCommand(bow, target), systems, out _)
                && systems.FactionState.GetAP(bow.team) == ap);
            Shot(bow, target, systems);
            Check("a real successful normal attack expends and attaches a stake", target.HP < before
                && SpecialSkillRuntime.CrossbowAmmoCount(bow) == 0 && bow.Special.stakeAId == target.ReflectionLifeId);
            var blocked = Actor(Team.Player, 110002, 110000); blocked.ShieldTurns = 1;
            LoadStake(bow, systems); Shot(bow, blocked, systems);
            Check("fully blocked stake round is expended but does not attach", blocked.HP == blocked.MaxHP
                && SpecialSkillRuntime.CrossbowAmmoCount(bow) == 0 && string.IsNullOrEmpty(bow.Special.stakeBId));
            SpecialSkillRuntime.CompleteNormalShot(bow, blocked, CrossbowRoundType.Stake, true);
            Check("completing a shot twice cannot forge a stake", string.IsNullOrEmpty(bow.Special.stakeBId));

            var recharge = Actor(Team.Enemy, 110010, 110000, 0);
            SpecialSkillRuntime.OnActorTurnStart(recharge, systems, 81000);
            Check("first lifecycle boundary does not invent an elapsed no-attack turn", SpecialSkillRuntime.CrossbowAmmoCount(recharge) == 0);
            SpecialSkillRuntime.OnActorTurnStart(recharge, systems, 81001);
            Check("no normal attack replenishes one normal round", SpecialSkillRuntime.CrossbowAmmoCount(recharge) == 1
                && SpecialSkillRuntime.CrossbowPeekRound(recharge) == CrossbowRoundType.Normal);
            SpecialSkillRuntime.OnActorTurnStart(recharge, systems, 81001);
            Check("repeated owner boundary cannot replenish twice", SpecialSkillRuntime.CrossbowAmmoCount(recharge) == 1);
            LoadStake(recharge, systems);
            var rounds = SpecialSkillRuntime.CrossbowMagazine(recharge);
            Check("rounds retain FIFO ordering", rounds.Count == 2 && rounds[0] == CrossbowRoundType.Normal && rounds[1] == CrossbowRoundType.Stake);
            string display = SpecialSkillRuntime.CrossbowMagazineText(recharge);
            Check("UI reverses FIFO so the oldest round is shown firing from the right", display.IndexOf("杭", StringComparison.Ordinal)
                < display.IndexOf("通常", StringComparison.Ordinal) && display.Contains("通常 →発射"));
            Check("normal round fires before the newer stake", SpecialSkillRuntime.TryBeginNormalShot(recharge, out var fired)
                && fired == CrossbowRoundType.Normal && SpecialSkillRuntime.CrossbowPeekRound(recharge) == CrossbowRoundType.Stake);
            SpecialSkillRuntime.CompleteNormalShot(recharge, target, fired, true);
            SpecialSkillRuntime.OnActorTurnStart(recharge, systems, 81002);
            Check("a turn with a fired normal attack does not replenish", SpecialSkillRuntime.CrossbowAmmoCount(recharge) == 1);
            SpecialSkillRuntime.OnAttackLanded(recharge, false);
            SpecialSkillRuntime.OnActorTurnStart(recharge, systems, 81003);
            Check("non-normal attacks do not suppress the normal-ammo replenishment", SpecialSkillRuntime.CrossbowAmmoCount(recharge) == 2);
            SpecialSkillRuntime.OnActorTurnStart(recharge, systems, 81004);
            SpecialSkillRuntime.OnActorTurnStart(recharge, systems, 81005);
            Check("replenishment is capped at three", SpecialSkillRuntime.CrossbowAmmoCount(recharge) == 3);

            var full = Actor(Team.Enemy, 110020, 110000, 3);
            string beforeRead = JsonUtility.ToJson(full.Special);
            Check("authored full initial magazine has three ordinary rounds", SpecialSkillRuntime.CrossbowAmmoCount(full) == 3
                && Magazine(full) == "Normal,Normal,Normal");
            SpecialSkillRuntime.CrossbowCanFire(full); SpecialSkillRuntime.CrossbowPeekRound(full);
            Check("UI and AI ammunition inspection never mutate live state", JsonUtility.ToJson(full.Special) == beforeRead);
            var saved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(full)));
            UnityEngine.Random.InitState(9227); float nextExpected = UnityEngine.Random.value; UnityEngine.Random.InitState(9227);
            LoadStake(full, systems); string converted = Magazine(full);
            Check("full-magazine conversion never grows beyond three", SpecialSkillRuntime.CrossbowAmmoCount(full) == 3);
            int stakes = 0; foreach (var round in SpecialSkillRuntime.CrossbowMagazine(full)) if (round == CrossbowRoundType.Stake) stakes++;
            Check("a full normal magazine converts exactly one existing round", stakes == 1);
            Check("stake conversion consumes no Unity random state", UnityEngine.Random.value == nextExpected);
            full.Special = SpecialSkillRuntime.CopyState(saved.Special); LoadStake(full, systems);
            Check("same saved seed/turn/unit/action sequence replays the same conversion", Magazine(full) == converted);
            var equivalent = Actor(Team.Enemy, 110020, 110000, 3); LoadStake(equivalent, systems);
            Check("fresh equivalent gameplay identity does not depend on GUID or Unity instance order", Magazine(equivalent) == converted);
            var loaded = SpecialSkillRuntime.CopyState(full.Special); loaded.crossbowMagazine[0] = CrossbowRoundType.None;
            Check("save copies own their ammunition collections", full.Special.crossbowMagazine[0] != CrossbowRoundType.None);
            loaded.crossbowMagazine.Add(CrossbowRoundType.Stake); loaded.crossbowMagazine.Add(CrossbowRoundType.Stake);
            loaded = SpecialSkillRuntime.CopyState(loaded);
            Check("malformed persisted ammunition is normalized and bounded", loaded.crossbowMagazine.Count <= 3
                && !loaded.crossbowMagazine.Contains(CrossbowRoundType.None));
            var undecided = ScriptableObject.CreateInstance<SpecialSkillDefinition>(); owned.Add(undecided);
            undecided.ability = UnitSpecialSkill.CrossbowStakes; undecided.apCost = 10; undecided.cooldown = 0;
            undecided.lightningFactionMask = SpecialFactionMask.Hostile;
            Check("unspecified starting ammunition remains explicit rather than a guessed balance value", undecided.ConfigurationError != null
                && undecided.ConfigurationError.Contains("UNRESOLVED_BALANCE"));

            var line = Actor(Team.Enemy, 110030, 110000, 0);
            var a = Actor(Team.Player, 110040, 110000); var b = Actor(Team.Player, 110044, 110000);
            AttachForLine(line, a, systems); line.Special.ownerTurn++;
            AttachForLine(line, b, systems); int bAge = line.Special.stakeBAppliedTurn;
            var c = Actor(Team.Player, 110046, 110000); line.Special.ownerTurn++;
            AttachForLine(line, c, systems);
            Check("third landed stake promotes B with its original age", line.Special.stakeAId == b.ReflectionLifeId
                && line.Special.stakeBId == c.ReflectionLifeId && line.Special.stakeAAppliedTurn == bAge);
            line.Special.stakeAId = a.ReflectionLifeId; line.Special.stakeBId = b.ReflectionLifeId;
            line.Special.ownerTurn = line.Special.stakeAAppliedTurn = line.Special.stakeBAppliedTurn = 1;
            Check("line age one has the specified 1.25 multiplier", SpecialSkillRuntime.LightningMultiplier(line) == 1.25f);
            line.Special.ownerTurn = 2; Check("line age two has the specified 1.50 multiplier", SpecialSkillRuntime.LightningMultiplier(line) == 1.5f);
            line.Special.ownerTurn = 3; Check("line age three has the specified 2.00 multiplier", SpecialSkillRuntime.LightningMultiplier(line) == 2f);
            var runner = Actor(Team.Player, 110042, 110000);
            int hp = runner.HP;
            var tick = typeof(SpecialSkillRuntime).GetMethod("TickActor", BindingFlags.Static | BindingFlags.NonPublic);
            tick.Invoke(null, new object[] { runner, systems, 82000 });
            int afterTick = runner.HP;
            tick.Invoke(null, new object[] { runner, systems, 82000 });
            Check("a live line applies standing damage only once per target and round", afterTick < hp && runner.HP == afterTick);
            var observed = new List<Status> { line, a, b, runner };
            Check("line follows both moving endpoints", SpecialSkillRuntime.HazardDamageAt(runner, new Vector3(110042, 1, 110002), observed) == 0);
            a.transform.position += Vector3.forward * 2; b.transform.position += Vector3.forward * 2;
            Check("moving endpoint geometry changes the actual crossing hazard", SpecialSkillRuntime.HazardDamageAt(runner, new Vector3(110042, 1, 110003), observed) > 0);
            hp = runner.HP;
            SpecialSkillRuntime.OnMoved(runner, new Vector3(110042, 1, 109999), new Vector3(110042, 1, 110003), systems);
            Check("entering a line by forced movement triggers shared hazard damage", runner.HP < hp);
            hp = runner.HP;
            SpecialSkillRuntime.OnMoved(runner, new Vector3(110042, 1, 110002), new Vector3(110042, 1, 110003), systems);
            Check("leaving an occupied line is not a fresh entry", runner.HP == hp);
            Debug.Log("[CrossbowR1] SUMMARY passed=" + checks);
        }
        finally
        {
            UnityEngine.Random.state = unityRandom;
            systems.MapCreate = previousMap; systems.VisionGenerator = previousVision;
            systems.TurnGenerator.Context.SelectUnit = previousSelection; systems.BattleSystem.SetTarget(previousTarget);
            systems.FactionState.SetAP(Team.Player, playerAP); systems.FactionState.SetAP(Team.Enemy, enemyAP);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear(); systems.RefreshVision();
        }
    }
}
#endif
