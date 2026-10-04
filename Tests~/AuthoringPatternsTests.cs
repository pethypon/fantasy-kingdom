#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Definition-specific tile rules must agree across gameplay, previews and minimax.</summary>
public static class AuthoringPatternsTests
{
    static void Check(string name, bool ok)
    { if (!ok) throw new Exception("[AuthoringPatterns] FAIL " + name); Debug.Log("[AuthoringPatterns] PASS " + name); }
    static bool Has(IReadOnlyList<Vector3Int> offsets, int x, int z)
    { for (int i = 0; i < offsets.Count; i++) if (offsets[i].x == x && offsets[i].z == z) return true; return false; }

    public static void EditMode()
    {
        var a = ScriptableObject.CreateInstance<BoardActionProfile>();
        var b = ScriptableObject.CreateInstance<BoardActionProfile>();
        var data = ScriptableObject.CreateInstance<UnitData>();
        var facility = ScriptableObject.CreateInstance<FacilityDefinitionData>();
        var actorObject = new GameObject("Authored pattern test");
        var actor = actorObject.AddComponent<Status>();
        actor.kind = Kind.Knight; actor.direction = Direction.N; actor.GrowthData = data;
        data.actionProfile = a;
        try
        {
            var mask = a.movement; mask.useCustom = true;
            mask.SetCell(7, 7, true);
            Check("all 64 mask bits including signed bit 63 survive JSON",
                JsonUtility.FromJson<BoardTilePattern>(JsonUtility.ToJson(mask)).IsCellSet(7, 7));
            long original = mask.cells; mask.SetCell(-1, 1, true); mask.SetCell(8, 0, true);
            Check("out-of-board clicks never mutate mask", mask.cells == original);
            mask.cells = 0; mask.SetCell(5, 4, true); // East 2, north 1.
            Check("authored move replaces shared AI role", MovePatterns.CanMove(actor, Direction.N, 2, 1)
                && !MovePatterns.CanMove(actor, Direction.N, 1, 0));
            Check("south-facing mirrors forward only", MovePatterns.CanMove(actor, Direction.S, 2, -1)
                && !MovePatterns.CanMove(actor, Direction.S, -2, -1));
            Check("fractional destinations rejected", !MovePatterns.CanMove(actor, Direction.N, 2.1f, 1));
            mask.directionIndependent = true;
            Check("direction-independent authored movement", MovePatterns.CanMove(actor, Direction.S, 2, 1));
            var cached = mask.Offsets;
            Check("repeated mask queries reuse cached offsets", ReferenceEquals(cached, mask.Offsets));
            mask.SetCell(4, 3, true);
            Check("cell edit invalidates offsets immediately", mask.Offsets.Count == 2 && !ReferenceEquals(cached, mask.Offsets));
            b.movement.useCustom = true; b.movement.SetCell(2, 3, true);
            data.actionProfile = b;
            Check("definitions sharing Kind never share custom moves", MovePatterns.CanMove(actor, Direction.N, -1, 0)
                && !MovePatterns.CanMove(actor, Direction.N, 2, 1));
            data.actionProfile = a;
            a.attack.useCustom = true; a.attack.SetCell(3, 6, true);
            Check("normal attack uses authored cells", AttackPatterns.CanAttack(actor, Direction.N, 0, 3)
                && !AttackPatterns.CanAttack(actor, Direction.N, 0, 1));
            a.vision.useCustom = true; a.vision.SetCell(3, 3, true); a.vision.SetCell(5, 4, true);
            Check("vision follows same custom orientation", Has(VisionGenerator.BaseVisionOffsets(actor), 2, 1));
            actor.direction = Direction.S;
            Check("south vision mirrors authored forward without horizontal flip", Has(VisionGenerator.BaseVisionOffsets(actor), 2, -1));
            a.vision.SetCell(2, 5, true);
            Check("vision cache observes edited mask", Has(VisionGenerator.BaseVisionOffsets(actor), -1, -2));
            facility.actionProfile = b; b.vision.useCustom = true; b.vision.SetCell(7, 3, true);
            actor.AuthoredFacility = facility;
            Check("building definition controls its own vision", Has(VisionGenerator.BaseVisionOffsets(actor), 4, 0)
                && !Has(VisionGenerator.BaseVisionOffsets(actor), 2, -1));
            actor.AuthoredFacility = null;
            a.actionType = ActorActionType.Stationary;
            Check("stationary actors cannot move", !MovePatterns.CanMove(actor, Direction.N, 2, 1) && MovePatterns.Offsets(actor).Count == 0);
            a.actionType = ActorActionType.Support;
            Check("support actors retain skills and suppress normal attacks", AttackPatterns.CanUseSkills(actor)
                && !AttackPatterns.CanAttack(actor, Direction.N, 0, 3));
            a.actionType = ActorActionType.Passive;
            Check("passive actors cannot move attack or use skills", MovePatterns.Offsets(actor).Count == 0
                && !AttackPatterns.CanAttack(actor, Direction.N, 0, 3) && !AttackPatterns.CanUseSkills(actor));
            a.actionType = ActorActionType.All;
            a.movement.useCustom = a.attack.useCustom = a.vision.useCustom = false;
            bool same = true;
            foreach (Kind kind in MovePatterns.Map.Keys)
            {
                actor.kind = kind;
                foreach (Direction direction in new[] { Direction.N, Direction.S })
                for (int x = -8; x <= 8; x++) for (int z = -8; z <= 8; z++)
                    same &= MovePatterns.CanMove(actor, direction, x, z) == MovePatterns.CanMove(kind, direction, x, z)
                        && AttackPatterns.CanAttack(actor, direction, x, z) == AttackPatterns.CanAttack(kind, direction, x, z);
            }
            Check("disabled custom masks preserve every legacy movement and normal attack", same);
            Simulate(a, actor);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(actorObject);
            UnityEngine.Object.DestroyImmediate(facility);
            UnityEngine.Object.DestroyImmediate(data);
            UnityEngine.Object.DestroyImmediate(a); UnityEngine.Object.DestroyImmediate(b);
        }
    }

    static void Simulate(BoardActionProfile profile, Status real)
    {
        profile.actionType = ActorActionType.All;
        profile.movement.cells = 0; profile.movement.useCustom = true; profile.movement.SetCell(5, 3, true);
        profile.movement.directionIndependent = false;
        profile.attack.cells = 0; profile.attack.useCustom = true; profile.attack.SetCell(3, 6, true);
        profile.vision.cells = 0; profile.vision.useCustom = true; profile.vision.SetCell(3, 3, true); profile.vision.SetCell(5, 3, true);
        real.kind = Kind.Knight; real.direction = Direction.N;
        real.HP = real.MaxHP = 40; real.ATK = 10; real.DEF = 2;
        var capture = typeof(SimBoardState).GetMethod("CaptureUnit", BindingFlags.Static | BindingFlags.NonPublic);
        var captured = (SimUnit)capture.Invoke(null, new object[] { real, 1, Team.Enemy });
        Check("simulation captures exact live action profile", ReferenceEquals(captured.ActionProfile, profile));
        var unit = new SimUnit { Id = 1, Team = Team.Enemy, Kind = Kind.Knight, Type = Type.Unit,
            Position = new Vector3Int(3, 0, 3), Direction = Direction.N, HP = 40, MaxHP = 40, ATK = 10, DEF = 2,
            AssignedSkillId = 1, ActionProfile = profile };
        var target = new SimUnit { Id = 2, Team = Team.Player, Kind = Kind.Knight, Type = Type.Unit,
            Position = new Vector3Int(3, 0, 6), Direction = Direction.S, HP = 30, MaxHP = 30, ATK = 6, DEF = 2, AssignedSkillId = -1 };
        var board = new SimBoardState { Units = new List<SimUnit> { unit, target }, MapTiles = new HashSet<Vector3Int>(),
            EnemyBuildingCounts = new Dictionary<FacilityKind, int>(), PlayerBuildingCounts = new Dictionary<FacilityKind, int>(), EnemyAP = 30, PlayerAP = 30 };
        for (int x = 0; x < 8; x++) for (int z = 0; z < 8; z++) board.MapTiles.Add(new Vector3Int(x, 0, z));
        board.RebuildOccupied();
        var actions = SimActionGenerator.GenerateAllActions(board, Team.Enemy);
        int moves = 0, attacks = 0; bool correct = true;
        foreach (var action in actions)
        {
            if (action.Type == SimActionType.Move) { moves++; correct &= action.TargetPos == new Vector3Int(5, 0, 3); }
            if (action.Type == SimActionType.Attack) { attacks++; correct &= action.TargetUnitId == target.Id; }
        }
        Check("minimax candidates honor custom move and attack cells", correct && moves == 1 && attacks == 1);
        Check("mobility and target counts agree with generated candidates", SimActionGenerator.CountMoves(board, unit) == 1
            && SimActionGenerator.CountAttackTargets(board, unit, Team.Player) == 1);
        Check("simulation custom vision uses selected tiles", board.EstimateVisionCells(Team.Enemy) == 2);
        var clone = board.Clone();
        Check("search clone retains authored profiles", ReferenceEquals(clone.Units[0].ActionProfile, profile)
            && clone.EstimateVisionCells(Team.Enemy) == 2);
        var pooled = clone.Units[0]; SimBoardPool.ReturnBoard(clone);
        Check("returned simulation pool does not retain authored assets", pooled.ActionProfile == null);
        int ap = board.EnemyAP;
        Check("invalid authored move cannot be applied or consume AP", !board.ApplyAction(new SimAction {
            Type = SimActionType.Move, ActorTeam = Team.Enemy, UnitId = 1, TargetPos = new Vector3Int(4, 0, 3), APCost = 1 })
            && board.EnemyAP == ap && unit.Position == new Vector3Int(3, 0, 3));
        board.Mountains.Add(new Vector3Int(4, 0, 3));
        Check("custom movement evaluation respects blocking terrain", SimActionGenerator.CountMoves(board, unit) == 0);
        Check("terrain-blocked custom move cannot consume AP", !board.ApplyAction(new SimAction {
            Type = SimActionType.Move, ActorTeam = Team.Enemy, UnitId = 1, TargetPos = new Vector3Int(5, 0, 3), APCost = 1 }) && board.EnemyAP == ap);
        board.Mountains.Clear();
        profile.actionType = ActorActionType.Passive;
        actions = SimActionGenerator.GenerateAllActions(board, Team.Enemy);
        bool passive = true;
        foreach (var action in actions)
            if (action.Type == SimActionType.Move || action.Type == SimActionType.Attack || action.Type == SimActionType.SkillUse) passive = false;
        Check("simulation passive action restriction matches gameplay", passive && !AttackPatterns.CanUseSkills(unit));
        Check("applied skill restriction cannot bypass candidate generation", !board.ApplyAction(new SimAction {
            Type = SimActionType.SkillUse, ActorTeam = Team.Enemy, UnitId = 1, SkillId = 1, TargetUnitId = 1, APCost = 1 }) && board.EnemyAP == ap);
    }
}
#endif
