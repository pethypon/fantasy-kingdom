#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Replacement Assassin passive through shared AP, vision, own-turn HP, persistence and death rules.</summary>
public static class AssassinAbilityTests
{
    const int Origin = 180000;
    static readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
    static int checks;
    static void Check(bool value, string message)
    { if (!value) throw new InvalidOperationException("[AssassinAbility] FAIL " + message); checks++; }
    static Vector3 At(int x, int z) => new Vector3(Origin + x, 1, Origin + z);
    static T Data<T>() where T : ScriptableObject
    { var data = ScriptableObject.CreateInstance<T>(); owned.Add(data); return data; }
    static Status Actor(Team team, int x, int z, bool assassin = false, int hp = 101)
    {
        var go = new GameObject("Assassin passive fixture"); owned.Add(go);
        var actor = go.AddComponent<Status>(); actor.team = team; actor.type = Type.Unit;
        actor.kind = assassin ? Kind.Assassin : Kind.Knight; actor.direction = Direction.N;
        actor.transform.position = At(x, z); actor.HP = actor.MaxHP = hp; actor.Level = 100;
        actor.ATK = 20; actor.DEF = 0;
        var data = Data<UnitData>(); data.kind = actor.kind; data.baseHP = hp; data.baseATK = 20;
        actor.GrowthData = data;
        if (assassin)
        {
            var definition = Data<SpecialSkillDefinition>(); definition.ability = UnitSpecialSkill.AssassinVisionSeal;
            definition.ApplySpecifiedAPCost(); definition.cooldown = 0; data.specialSkill = definition;
        }
        CombatRegistry.Register(actor); return actor;
    }
    static BoardActionProfile Profile(params Vector2Int[] offsets)
    {
        var profile = Data<BoardActionProfile>(); profile.vision.useCustom = true;
        foreach (var offset in offsets) profile.vision.SetCell(3 + offset.x, 3 + offset.y, true);
        return profile;
    }
    static void End(GameSystems systems, Status actor, int round) => SpecialSkillRuntime.OnOwnerTurnEnd(systems, actor.team, round);

    public static void PlayMode(GameSystems live)
    {
        checks = 0;
        var originals = CombatRegistry.Snapshot();
        int playerAP = live.FactionState.GetAP(Team.Player), enemyAP = live.FactionState.GetAP(Team.Enemy);
        var random = UnityEngine.Random.state;
        try
        {
            foreach (var original in originals) if (original != null) original.gameObject.SetActive(false);
            var geometry = new GameObject("Assassin isolated rules map"); owned.Add(geometry);
            var map = geometry.AddComponent<MapCreate>(); map.UseR1Terrain = false;
            map.maxX = map.maxZ = Origin + 40; map.minY = 0; map.maxY = 4; map.SetPos = new List<Vector3>();
            for (int x = 0; x < 35; x++) for (int z = 0; z < 35; z++) map.SetPos.Add(At(x, z));
            var vision = geometry.AddComponent<VisionGenerator>();
            var rules = new GameSystems { MapCreate = map, VisionGenerator = vision, CrystalSystem = live.CrystalSystem };
            var assassin = Actor(Team.Player, 10, 10, true);
            assassin.Fatigue = 80; assassin.specialAbility = SpecialAbility.SwiftPosture;
            assassin.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Chill, 2));
            var ap = geometry.AddComponent<APSystem>(); ap.Init(live.FactionState, map);
            Check(ap.CalcCost(APSystem.ActionType.Move, assassin, At(10, 10), At(12, 11) + Vector3.up) == 3,
                "fatigue, uphill, chill and SwiftPosture cannot change fixed three movement AP");
            var scout = Actor(Team.Player, 3, 3); var scoutRule = Data<SpecialSkillDefinition>();
            scoutRule.ability = UnitSpecialSkill.ScoutVisionGuard; scoutRule.scoutFreeMoveScope = AbilityResetScope.OwnerTurn;
            scout.GrowthData.specialSkill = scoutRule; scout.VisionCell.Add(assassin.GridPosition); SpecialSkillRuntime.InvalidateRules();
            Check(SpecialSkillRuntime.HasFreeMoveAP(assassin) && ap.CalcCost(APSystem.ActionType.Move, assassin) == 3,
                "Scout free-move allowance cannot override fixed three AP");
            live.FactionState.SetAP(Team.Player, 9); ap.Consume(Team.Player, APSystem.ActionType.Move, assassin);
            Check(live.FactionState.GetAP(Team.Player) == 6 && !assassin.Special.scoutFreeMoveUsed,
                "actual movement charges three and does not consume a bypassed free token");
            scout.gameObject.SetActive(false);

            var catalog = Data<TerrainEnvironmentCatalog>(); catalog.enabledForPlay = true;
            foreach (var kind in new[] { TerrainEnvironmentKind.Mud, TerrainEnvironmentKind.Road, TerrainEnvironmentKind.Cave })
            {
                var definition = Data<TerrainEnvironmentDefinition>(); definition.definitionId = "assassin-test-" + kind;
                definition.kind = kind; definition.enabledForPlay = true; definition.caveCooldownTurns = 1;
                definition.moveAPAdjustment = kind == TerrainEnvironmentKind.Mud ? 7 : kind == TerrainEnvironmentKind.Road ? -2 : 0;
                catalog.definitions.Add(definition);
                int x = kind == TerrainEnvironmentKind.Mud ? 11 : kind == TerrainEnvironmentKind.Road ? 12 : 13;
                catalog.placements.Add(new TerrainEnvironmentPlacement { definition = definition, cell = new Vector2Int(Origin + x, Origin + 10),
                    hasCaveExit = kind == TerrainEnvironmentKind.Cave, caveExit = new Vector2Int(Origin + 14, Origin + 10) });
                if (kind == TerrainEnvironmentKind.Cave)
                    catalog.placements.Add(new TerrainEnvironmentPlacement { definition = definition, cell = new Vector2Int(Origin + 14, Origin + 10),
                        hasCaveExit = true, caveExit = new Vector2Int(Origin + 13, Origin + 10) });
            }
            map.Environment.Initialize(catalog);
            Check(map.Environment.Count == 4 && map.Environment.IsCaveConnection(At(13, 10), At(14, 10)), "real terrain and cave fixtures are active");
            Check(ap.CalcCost(APSystem.ActionType.Move, assassin, At(10, 10), At(11, 10)) == 3
                && ap.CalcCost(APSystem.ActionType.Move, assassin, At(10, 10), At(12, 10)) == 3
                && ap.CalcCost(APSystem.ActionType.Move, assassin, At(13, 10), At(14, 10)) == 3,
                "mud, road and cave movement all charge three AP");
            assassin.ActiveEffects.Clear();
            assassin.Special.assassinBoostTurns = 10; assassin.Special.assassinBoostStacks = 3;
            Check(Mathf.Approximately(SpecialSkillRuntime.GetATKModifier(assassin), 1)
                && !SpecialSkillRuntime.IsAssassinAllyTarget(assassin, scout) && !SpecialSkillRuntime.WouldAssassinate(assassin, scout),
                "saved former execution and attack boost states have no passive effect");
            Check(SpecialSkillPresentation.PassiveDescription(SpecialSkillRuntime.Definition(assassin)).Contains("3AP")
                && !SpecialSkillPresentation.PassiveDescription(SpecialSkillRuntime.Definition(assassin)).Contains("即死"), "passive tooltip describes replacement ability");
            assassin.passiveskill = PassiveSkill.Assassination; // Simulate stale saved metadata before identity normalization.
            Check(SpecialSkillPresentation.Details(assassin, 96000).Contains("通常移動は常に3AP")
                && !SpecialSkillPresentation.Details(assassin, 96000).Contains("背面攻撃の追加倍率"),
                "fixed Assassin details override a stale legacy passive label");
            Check(SummonUnitTooltip.Description(assassin.GrowthData).Contains("通常移動は常に3AP")
                && !SummonUnitTooltip.Description(assassin.GrowthData).Contains("背面攻撃の追加倍率"),
                "Assassin summon tooltip uses its fixed ability instead of legacy metadata");
            assassin.passiveskill = PassiveSkill.None;

            SpecialSkillRuntime.OnActorTurnStart(assassin, rules, 96000);
            SpecialSkillRuntime.OnOwnerTurnEnd(rules, Team.Enemy, 96000);
            Check(assassin.HP == 101, "own turn start and opponent turn end do not inflict isolation damage");
            assassin.NaturalRecovery = new NaturalRecoveryState { HasTurnBaseline = true, ConsecutiveSafeTurns = 5 };
            assassin.ShieldTurns = 2; assassin.Special.damageNullUnused = true; assassin.Special.damageNullTurns = 2;
            assassin.Special.nextDamageDouble = true;
            End(rules, assassin, 96000);
            Check(assassin.HP == 91 && assassin.NaturalRecovery.ConsecutiveSafeTurns == 0,
                "self vision cannot prevent floor ten-percent HP loss and recovery clock resets");
            Check(SpecialSkillRuntime.LastHpChange.ActualHpLoss == 10 && SpecialSkillRuntime.LastHpChange.Kind == HpChangeKind.DirectHpReduction
                && SpecialSkillRuntime.LastHpChange.Cause == HpChangeCause.AssassinIsolation
                && assassin.Special.damageNullUnused && assassin.Special.nextDamageDouble,
                "isolation uses direct HP loss, bypassing DEF, shield, nullification and vulnerability multipliers");
            End(rules, assassin, 96000); Check(assassin.HP == 91, "same owner-turn end is idempotent");
            var saved = JsonUtility.FromJson<SaveSystem.UnitSaveData>(JsonUtility.ToJson(SaveSystem.CaptureUnit(assassin)));
            SaveGameApplier.ApplyStatusFields(assassin, saved); End(rules, assassin, 96000);
            Check(assassin.HP == 91, "save/load preserves already processed own-turn boundary");
            assassin.ShieldTurns = 0;

            var ally = Actor(Team.Player, 8, 10); ally.GrowthData.actionProfile = Profile(new Vector2Int(2, 0));
            End(rules, assassin, 96001); Check(assassin.HP == 91, "exact custom allied mask prevents damage");
            ally.Special.visionZeroTurns = 1; End(rules, assassin, 96002);
            Check(assassin.HP == 81, "blind-skill zero vision cannot provide support");
            ally.Special.visionZeroTurns = 0; ally.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Blind, 2));
            End(rules, assassin, 96003); Check(assassin.HP == 71, "ordinary blindness narrows support instead of keeping stale mask");
            ally.ActiveEffects.Clear(); assassin.Special.archerEscapeTurns = 2; assassin.Special.archerSnapshotIds.Add(ally.ReflectionLifeId);
            End(rules, assassin, 96004); Check(assassin.HP == 61, "observer-specific stealth excludes blocked observer");
            assassin.Special.archerEscapeTurns = 0; assassin.Special.archerSnapshotIds.Clear();
            ally.team = Team.Enemy; End(rules, assassin, 96005); Check(assassin.HP == 51, "hostile sight does not count as support");
            ally.team = Team.Player; ally.HP = 0; End(rules, assassin, 96006); Check(assassin.HP == 41, "dead ally cannot provide support");
            ally.gameObject.SetActive(false);

            var building = Actor(Team.Player, 8, 10); building.type = Type.Building; building.kind = Kind.None;
            building.AuthoredFacility = Data<FacilityDefinitionData>(); building.AuthoredFacility.actionProfile = Profile(new Vector2Int(2, 0));
            End(rules, assassin, 96007); Check(assassin.HP == 31, "allied authored building vision cannot replace another friendly unit");
            building.gameObject.SetActive(false);
            var crystal = Actor(Team.Player, 8, 10); crystal.kind = Kind.Crystal; crystal.type = Type.Building;
            End(rules, assassin, 96008); Check(assassin.HP == 21, "main crystal vision cannot replace another friendly unit");
            crystal.kind = Kind.SubCrystal; crystal.type = Type.Building; crystal.facilityKind = FacilityKind.SubCrystal;
            End(rules, assassin, 96009); Check(assassin.HP == 11, "subcrystal vision cannot replace another friendly unit");
            crystal.gameObject.SetActive(false);

            var blocker = Data<TerrainEnvironmentDefinition>(); blocker.definitionId = "assassin-test-vision-block";
            blocker.kind = TerrainEnvironmentKind.Rock; blocker.enabledForPlay = true; blocker.blocksVision = true;
            catalog.placements.Clear(); catalog.placements.Add(new TerrainEnvironmentPlacement { definition = blocker, cell = new Vector2Int(Origin + 9, Origin + 10) });
            map.Environment.Initialize(catalog); ally.HP = 101; ally.gameObject.SetActive(true);
            End(rules, assassin, 96010); Check(assassin.HP == 1, "terrain line of sight blocks allied support");
            ally.gameObject.SetActive(false);

            var third = Actor(Team.Monster, 20, 20, true); End(rules, third, 96011);
            Check(third.HP == 91, "third faction receives its own end-turn isolation loss");
            var otherThird = Actor(Team.Intruder, 18, 20); otherThird.GrowthData.actionProfile = Profile(new Vector2Int(2, 0));
            End(rules, third, 96012); Check(third.HP == 81, "different third faction does not count as a friendly observer");
            otherThird.team = Team.Monster; End(rules, third, 96013);
            Check(third.HP == 81, "same third faction observation prevents isolation");
            var tiny = Actor(Team.Enemy, 30, 30, true, 5); tiny.HP = 1;
            Check(SpecialSkillRuntime.AssassinIsolationDamage(tiny) == 1, "positive small maximum HP still loses at least one");
            End(rules, tiny, 96014); Check(tiny.HP == 0 && !tiny.gameObject.activeSelf, "lethal isolation completes normal death cleanup");
            Simulation();
            Debug.Log("[AssassinAbility] " + checks + " replacement-passive checks passed");
        }
        finally
        {
            live.FactionState.SetAP(Team.Player, playerAP); live.FactionState.SetAP(Team.Enemy, enemyAP);
            UnityEngine.Random.state = random;
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
            foreach (var original in originals) if (original != null) original.gameObject.SetActive(true);
            SpecialSkillRuntime.InvalidateRules(); live.RefreshVision();
        }
    }

    static void Simulation()
    {
        var assassin = new SimUnit { Id = 1, Team = Team.Enemy, Type = Type.Unit, Kind = Kind.Assassin,
            AssassinPassive = true, MaxHP = 101, HP = 101, Position = new Vector3Int(2, 0, 2),
            Fatigue = 80, ShieldTurns = 9, NaturalRecoveryHasBaseline = true, NaturalRecoverySafeTurns = 5 };
        var board = new SimBoardState { Units = new List<SimUnit> { assassin }, MapTiles = new HashSet<Vector3Int>() };
        for (int x = 0; x < 6; x++) for (int z = 0; z < 6; z++) board.MapTiles.Add(new Vector3Int(x, 0, z));
        Check(board.CalcMoveCost(assassin) == 3, "search uses the same fixed movement cost");
        board.SimulateTurnTransition(Team.Player);
        Check(assassin.HP == 91 && assassin.NaturalRecoverySafeTurns == 0,
            "search self isolation bypasses shields and interrupts recovery before outgoing turn ends");
        var observer = new SimUnit { Id = 2, Team = Team.Enemy, Type = Type.Building, Kind = Kind.None,
            HP = 100, MaxHP = 100, Position = new Vector3Int(0, 0, 2),
            ActionProfile = Profile(new Vector2Int(2, 0)), CapturedVisionCells = new[] { assassin.Position } };
        board.Units.Add(observer); board.SimulateTurnTransition(Team.Enemy); board.SimulateTurnTransition(Team.Player);
        Check(assassin.HP == 81, "search rejects captured owned building observation as unit support");
        observer.Type = Type.Unit; observer.CapturedVisionCells = null; observer.ActionProfile = Profile(new Vector2Int(2, 0));
        observer.Position = new Vector3Int(0, 0, 2);
        Check(board.HasOtherAllyVision(assassin), "search projected observer uses authored vision mask");
        observer.VisionZeroTurns = 2;
        Check(!board.HasOtherAllyVision(assassin), "search blinded observer cannot support assassin");
        var copy = assassin.Clone();
        Check(copy.AssassinPassive && copy.AssassinLastEndRound == assassin.AssassinLastEndRound
            && copy.HP == assassin.HP, "search clone retains passive identity and processed turn boundary");
        SimBoardPool.ReturnUnit(copy);
    }
}
#endif
