#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Observed-board regressions for modest offensive commitment without bypassing survival/supply.</summary>
public static class AIClosingTests
{
    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static int passed;
    static void Check(string label, bool condition)
    {
        if (!condition) throw new InvalidOperationException("[AIClosing] FAIL " + label);
        passed++; Debug.Log("[AIClosing] PASS " + label);
    }
    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        {
            Competition(systems, team);
            FundedWarning(systems, team);
            Recovery(systems, team);
            Defense(systems, team);
            PrincipalCrystal(systems, team);
        }
        Debug.Log("[AIClosing] " + passed + " faction/priority/strategy checks passed");
    }

    static void Competition(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        var board = f.Board(); var governor = board.Governor;
        Check(team + " stable supply and observed force advantage select Assault",
            EconomyHelper.IsEconomySufficient(board) && board.GetAdvantageRatio() > .25f
            && new TurnStrategyPlanner().DecideStrategy(board, f.Economy.Personality, new AIThreatLevel(15), 60).Strategy == TurnStrategy.Assault);
        var attack = f.Attack();
        var explore = new AIAction { ActionType = AIActionType.Move, Unit = f.Scout, TargetPos = f.Cells[2], APCost = 2, Score = 5 };
        var actions = new List<AIAction> { explore, attack };
        string stocks = JsonUtility.ToJson(f.Economy.Resources); int ap = f.Economy.State.GetAP(team);
        governor.Filter(actions, board);
        Check(team + " fixture has an executable persistent exploration step", governor.Exploration.IsAssignedUnit(f.Scout)
            && governor.Exploration.IsObjectiveProgress(f.Scout, explore.TargetPos, board) && explore.StrategicPriority == 3);
        Check(team + " effective safe attack competes in the same tier as exploration",
            actions.Contains(attack) && attack.StrategicPriority == 3 && attack.StrategicPriority == explore.StrategicPriority);
        actions.Sort(AIAction.ComparePriorityThenScore);
        Check(team + " tactical attack score can win instead of being discarded by scout priority", actions[0] == attack);
        Check(team + " priority inspection changes neither AP nor stocks", ap == f.Economy.State.GetAP(team)
            && stocks == JsonUtility.ToJson(f.Economy.Resources));

        f.Actor.AssignedSkillId = 7; f.Actor.SkillCooldown = 0;
        // Production R1 content intentionally need not keep legacy random-skill
        // recipes. Exercise the scheduler with a local, deterministic recipe;
        // ID 7 uses the existing surrounding-cell shape and never enters Table.
        var fixtureSkill = new SkillData { Id = 7, Name = "AI closing fixture strike", APCost = 4,
            Target = SkillTarget.EnemySingle, Area = SkillAreaShape.Single, Multiplier = 1 };
        var skill = new AIAction { ActionType = AIActionType.SkillUse, Unit = f.Actor, TargetUnit = f.Foe,
            TargetPos = f.Foe.transform.position, APCost = 4, Skill = fixtureSkill, Score = 90 };
        actions = new List<AIAction> { skill }; governor.Filter(actions, board);
        Check(team + " effective paid offensive skill also competes at P3", skill.StrategicPriority == 3);

        f.Foe.ShieldTurns = 1; board = f.Board(); attack = f.Attack(); skill.TargetUnit = f.Foe;
        actions = new List<AIAction> { attack, skill }; board.Governor.Filter(actions, board);
        Check(team + " shielded attacks and skills receive no offensive promotion", attack.StrategicPriority == 4 && skill.StrategicPriority == 4);
        f.Foe.ShieldTurns = 0; f.Foe.Special.damageNullUnused = true; f.Foe.Special.damageNullTurns = 1;
        board = f.Board(); attack = f.Attack(); actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " zero actual damage receives no offensive promotion", attack.StrategicPriority == 4);
        f.Foe.Special.damageNullUnused = false; f.Foe.Special.damageNullTurns = 0;
        f.Foe.Special.guardianPartnerId = "unobserved-guardian";
        board = f.Board(); attack = f.Attack(); actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " unknown redirect recipient cannot earn offensive priority", attack.StrategicPriority == 4);
        f.Foe.Special.guardianPartnerId = null;

        f.Actor.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Stun, 1)); board = f.Board(); attack = f.Attack();
        actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " stunned actor receives no offensive promotion", attack.StrategicPriority == 4);
        f.Actor.ActiveEffects.Clear(); f.Economy.State.SetAP(team, 0); board = f.Board(); attack = f.Attack();
        actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " unaffordable attack cannot claim offensive priority", attack.StrategicPriority == 4);
        f.Economy.State.SetAP(team, 40);

        f.SetVisible(false); board = f.Board(); attack = f.Attack();
        actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " real but unobserved opponent is never promoted", !board.AlivePlayerUnits.Contains(f.Foe)
            && attack.StrategicPriority == 4);
        f.SetVisible(true); f.Foe.transform.position = f.Cells[0]; board = f.Board(); attack = f.Attack();
        actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " visible out-of-range target is never promoted", attack.StrategicPriority == 4);
        f.Foe.transform.position = f.Cells[5]; f.Foe.ATK = 9999;
        board = f.Board(); attack = f.Attack(); actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " ordinary attacks with observed lethal retaliation are not promoted",
            board.Governor.Mode != StrategicMode.EmergencyDefense && attack.StrategicPriority == 4);
        f.Foe.ATK = 1; f.Foe.team = team;
        board = f.Board(); attack = f.Attack(); actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " friendly target never earns offensive priority", attack.StrategicPriority == 4);
        f.Foe.team = team == Team.Player ? Team.Enemy : Team.Player; f.Foe.HP = 1; f.Foe.kind = Kind.King;
        board = f.Board(); attack = f.Attack(); actions = new List<AIAction> { attack }; board.Governor.Filter(actions, board);
        Check(team + " an estimated royal kill never bypasses emergency tiers", attack.StrategicPriority == 3);
    }

    static void FundedWarning(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        f.Economy.Resources.MagicOre = 14;
        f.Actor.GrowthData.useAuthoredUpkeep = true; f.Actor.GrowthData.upkeepMagic = 1;
        var mine = f.Mine.levels[0]; mine.Output.MagicOre = 0; mine.BonusChance1 = .5f; mine.BonusOutput1.MagicOre = 4;
        f.Mine.levels[0] = mine; GameContentRevision.NotifyChanged();
        var board = f.Board(); var governor = board.Governor;
        var decision = new TurnStrategyPlanner().DecideStrategy(board, f.Economy.Personality, new AIThreatLevel(15), 60);
        Check(team + " funded uncertain supply keeps its real warning without compulsory recovery",
            governor.Economy.State == EconomicState.Warning && !EconomyHelper.IsEconomySufficient(board)
            && governor.Mode == StrategicMode.StrategicAttack && governor.GetRecoveryReserveAP(board) == 0);
        Check(team + " funded warning remains ContactEngage rather than forcing full Assault or EconomyBuild",
            board.HasPrincipalOpponentContact && decision.Strategy == TurnStrategy.ContactEngage && decision.Budget.ReservedAP == 0);
    }

    static void Recovery(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        foreach (var building in f.Economy.Buildings)
            if (building.facilityKind == FacilityKind.Bakery)
            { var level = building.AuthoredFacility.levels[0]; level.Output.Bread = 0; building.AuthoredFacility.levels[0] = level; }
        f.Economy.Resources.Bread = 0;
        var bakery = f.Economy.Definition(FacilityKind.Bakery, bread: 100);
        GameContentRevision.NotifyChanged(); var board = f.Board();
        var recovery = f.Economy.BuildAction(bakery, board); var attack = f.Attack();
        var actions = new List<AIAction> { attack, recovery }; board.Governor.Filter(actions, board);
        Check(team + " obligatory supply recovery still precedes offensive commitment",
            board.Governor.Mode == StrategicMode.EconomicRecovery && actions.Contains(recovery)
            && recovery.StrategicPriority <= 2 && attack.StrategicPriority == 4);
        var decision = new TurnStrategyPlanner().DecideStrategy(board, f.Economy.Personality, new AIThreatLevel(15), 60);
        Check(team + " genuine executable recovery retains EconomyBuild", decision.Strategy == TurnStrategy.EconomyBuild
            && decision.Budget.ReservedAP > 0);
    }

    static void Defense(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        f.Economy.Crystal.transform.position = f.Cells[2]; f.Economy.SetOwnCrystalPosition(f.Cells[2]);
        f.Economy.Crystal.HP = 10; f.Economy.Crystal.DEF = 0;
        f.Foe.transform.position = f.Cells[3]; f.Foe.ATK = 9999;
        f.Scout.transform.position = f.Cells[0];
        var king = f.Economy.Unit(); king.kind = Kind.King; king.transform.position = f.Cells[1]; king.DEF = 0;
        var board = f.Board(); var escape = new AIAction { ActionType = AIActionType.Move, Unit = king, TargetPos = f.Cells[0] };
        var doomed = new AIAction { ActionType = AIActionType.Move, Unit = king, TargetPos = f.Cells[2] };
        var attack = f.Attack(); var actions = new List<AIAction> { attack, escape, doomed }; board.Governor.Filter(actions, board);
        Check(team + " observed lethal king danger retains P0 escape and rejects known lethal alternative",
            board.Governor.Mode == StrategicMode.EmergencyDefense && actions.Contains(escape) && escape.StrategicPriority == 0
            && !actions.Contains(doomed) && doomed.StrategicRejectReason == "king_known_lethal");
        Check(team + " defending against a real crystal threat retains P1", actions.Contains(attack) && attack.StrategicPriority == 1);
        Check(team + " emergency still selects CrystalDefense", new TurnStrategyPlanner().DecideStrategy(board,
            f.Economy.Personality, new AIThreatLevel(15), 60).Strategy == TurnStrategy.CrystalDefense);
    }

    static void PrincipalCrystal(GameSystems systems, Team team)
    {
        using var f = new Fixture(systems, team);
        var oppositeParent = team == Team.Player ? f.Economy.Crystals.Enemycrystal : f.Economy.Crystals.Playercrystal;
        f.Foe.transform.SetParent(oppositeParent); f.Foe.kind = Kind.Crystal; f.Foe.type = Type.Building; f.Foe.ATK = 0;
        if (team == Team.Player) f.Economy.Crystals.ECP = f.Cells[5]; else f.Economy.Crystals.PCP = f.Cells[5];
        var board = f.Board(); var decision = new TurnStrategyPlanner().DecideStrategy(board, f.Economy.Personality, new AIThreatLevel(15), 60);
        Check(team + " undefended visible main crystal remains an offensive target after hostile troops are gone",
            board.AlivePlayerUnits.Count == 0 && board.PlayerCrystalVisible && board.PlayerCrystalHP > 0
            && decision.Strategy == TurnStrategy.Assault);
    }

    sealed class Fixture : IDisposable
    {
        public readonly EconomyR2Tests.Fixture Economy;
        public readonly Status Actor, Scout, Foe;
        public readonly Vector3[] Cells;
        public readonly FacilityDefinitionData Mine;
        readonly GameSystems live;
        readonly VisionGenerator vision;
        readonly GameObject visionObject;
        readonly BoardActionProfile attackProfile;
        public Fixture(GameSystems systems, Team team)
        {
            live = systems; Economy = new EconomyR2Tests.Fixture(systems, team);
            try
            {
                Economy.Rules.aiEconomy.plannedDemandWeight = 0; Economy.Rules.aiEconomy.forecastTurns = Economy.Rules.aiEconomy.reserveTurns = 4;
                Economy.Resources.Citizen = 0;
                Economy.Food(100); Economy.Building(Economy.Definition(FacilityKind.LoggingCamp, wood: 20));
                Economy.Building(Economy.Definition(FacilityKind.Quarry, stone: 20));
                Mine = Economy.Definition(FacilityKind.Mine, iron: 20, magic: 20); Economy.Building(Mine);
                Cells = FlatCells(systems.MapCreate);
                Economy.Crystal.transform.position = Cells[0]; Economy.SetOwnCrystalPosition(Cells[0]);
                Actor = Economy.Unit(); Actor.transform.position = Cells[4]; Actor.ATK = 45; Actor.DEF = 2;
                Scout = Economy.Unit(); Scout.kind = Kind.Scout; Scout.transform.position = Cells[1]; Scout.ATK = 20;
                attackProfile = ScriptableObject.CreateInstance<BoardActionProfile>();
                attackProfile.attack.useCustom = true; attackProfile.attack.directionIndependent = true;
                attackProfile.attack.SetCell(2, 3, true); attackProfile.attack.SetCell(4, 3, true);
                Actor.GrowthData.actionProfile = attackProfile;
                Foe = Economy.Opponent(Cells[5], 1);
                visionObject = new GameObject("AI closing isolated observed cells"); visionObject.SetActive(false);
                visionObject.transform.SetParent(Economy.Crystal.transform);
                vision = visionObject.AddComponent<VisionGenerator>(); SetVisible(true);
            }
            catch { Dispose(); throw; }
        }
        public void SetVisible(bool showOpponent)
        {
            var known = new HashSet<Vector3Int>();
            for (int i = 0; i < Cells.Length; i++) if (showOpponent || i != 5) known.Add(GridHelper.ToGridXZ(Cells[i]));
            foreach (string field in new[] { "_playerVisionBox", "_enemyVisionBox", "_playerExplored", "_enemyExplored" })
                typeof(VisionGenerator).GetField(field, Private).SetValue(vision, new HashSet<Vector3Int>(known));
        }
        public AIBoardState Board()
        {
            var board = new AIBoardState(live.MoveGenerator, live.AttackGenerator, Economy.AP, Economy.Units,
                Economy.Crystals, vision, Economy.Builder, null, Economy.State, null, 60, null, Economy.Team)
                { MapCreate = live.MapCreate, ReconThreatLevel = 15, Governor = new AIStrategicGovernor() };
            board.Governor.Evaluate(board); return board;
        }
        public AIAction Attack() => new AIAction { ActionType = AIActionType.Attack, Unit = Actor, TargetUnit = Foe,
            TargetPos = Foe.transform.position, APCost = 4, Score = 100 };
        public void Dispose()
        { if (attackProfile != null) UnityEngine.Object.DestroyImmediate(attackProfile); Economy?.Dispose(); }
        static Vector3[] FlatCells(MapCreate map)
        {
            int regionSize = Mathf.Clamp(ExplorationAISettings.Active.RegionSize, 2, 128);
            foreach (var cell in map.SetPos)
            {
                // Keep this short corridor in one frontier region, so its far end
                // is a genuine persistent exploration target rather than a tie.
                if (regionSize >= 6 && (int)cell.x / regionSize != ((int)cell.x + 5) / regionSize) continue;
                bool valid = true; var cells = new Vector3[6]; cells[0] = cell;
                for (int i = 1; i < cells.Length; i++)
                {
                    if (!map.TryGetHeight((int)cell.x + i, (int)cell.z, out float height) || Mathf.Abs(height - cell.y) > .01f)
                    { valid = false; break; }
                    cells[i] = cell + Vector3.right * i;
                }
                if (valid) return cells;
            }
            throw new InvalidOperationException("AI closing fixture needs six adjacent level board cells");
        }
    }
}
#endif
