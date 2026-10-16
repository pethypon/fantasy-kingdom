#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class ObservedDragonDangerTests
{
    static int passed;
    static void Check(string name, bool value)
    { if (!value) throw new InvalidOperationException("[ObservedDragon] FAIL " + name); passed++; Debug.Log("[ObservedDragon] PASS " + name); }
    static AIStrategicGovernor Governor(AIBoardState board)
    { var governor = new AIStrategicGovernor(); board.Governor = governor; governor.Evaluate(board); return governor; }
    static int Damage(AIBoardState board, Status self, Vector3 destination)
        => Mathf.RoundToInt(Governor(board).IncomingDamage(board, self, destination, null));
    static int CounterDamage(AIBoardState board, Status self, Vector3 destination)
    {
        // These fixtures change observed actors between checks, as a real board refresh would.
        board.Enemies.Rebuild(board.AlivePlayerUnits);
        ((System.Collections.IDictionary)typeof(AIBoardState).GetField("CounterCache", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(board)).Clear();
        return board.EstimateCounterDamageAt(destination, self);
    }

    public static void PlayMode(GameSystems systems)
    {
        passed = 0;
        foreach (var team in new[] { Team.Player, Team.Enemy })
        using (var f = new EconomyR2Tests.Fixture(systems, team))
        {
            var geometry = new GameObject("Observed dragon terrain fixture"); geometry.SetActive(false);
            try
            {
                var map = geometry.AddComponent<MapCreate>(); map.maxX = map.maxZ = 24; map.UseR1Terrain = true;
                var heights = new int[24, 24];
                typeof(MapCreate).GetField("topY", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, heights);
                typeof(MapCreate).GetField("rivers", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(map, new bool[24, 24]);
                f.Food(20);
                var king = f.Unit(); king.kind = Kind.King; king.IsPrimaryCommander = true;
                king.HP = 10; king.MaxHP = 100; king.DEF = 0; king.passiveskill = PassiveSkill.None;
                king.specialAbility = SpecialAbility.None; king.direction = Direction.S;
                king.transform.position = new Vector3(8, 1, 4);
                var dragon = f.Opponent(new Vector3(4, 1, 4), 32);
                dragon.team = Team.Obstacle; dragon.kind = Kind.Boss; dragon.isWildBoss = true;
                dragon.wildBossArchetype = WildBossArchetype.Dragon;
                dragon.wildBossTerritoryCenter = new Vector3Int(4, 0, 4); dragon.wildBossTerritoryRadius = 6;
                dragon.HP = dragon.MaxHP = 8200; dragon.wildBossMaxAP = 20; dragon.wildBossAP = 0;
                dragon.wildBossCurrentThreat = 25; dragon.wildBossTurnCounter = 3;
                dragon.passiveskill = PassiveSkill.StrangeKingAura; dragon.specialAbility = SpecialAbility.None;
                dragon.direction = Direction.N;
                var board = f.Board(); board.MapCreate = map;
                board.AlivePlayerUnits.Clear(); board.AlivePlayerUnits.Add(dragon);
                var known = (HashSet<Vector3Int>)board.ObservedTerrainCells;
                for (int x = 0; x < 24; x++) for (int z = 0; z < 24; z++) known.Add(new Vector3Int(x, 0, z));
                int normal = DamageCalculator.FinalizeHitDamage(DamageCalculator.CalcRawBase(32, 0));
                Check(team + " observed scheduled breath reaches beyond normal patterns with refilled AP", Damage(board, king, king.transform.position) == normal);
                Check(team + " observed breath triggers commander emergency", board.Governor.Mode == StrategicMode.EmergencyDefense);
                var inside = new AIAction { ActionType = AIActionType.Move, Unit = king, TargetPos = new Vector3(7, 1, 4) };
                var escape = new AIAction { ActionType = AIActionType.Move, Unit = king, TargetPos = new Vector3(11, 1, 4) };
                var actions = new List<AIAction> { inside, escape };
                board.Governor.Filter(actions, board);
                Check(team + " commander evacuates instead of another lethal territory move", !actions.Contains(inside)
                    && inside.StrategicRejectReason == "king_known_lethal" && actions.Contains(escape) && escape.StrategicPriority == 0);
                king.transform.position = escape.TargetPos;
                inside.StrategicRejectReason = null; escape.StrategicRejectReason = null;
                Governor(board); actions = new List<AIAction> { inside, escape }; board.Governor.Filter(actions, board);
                Check(team + " commander avoids entering next breath from safety", !actions.Contains(inside) && actions.Contains(escape));
                king.transform.position = new Vector3(8, 1, 4);

                board.AlivePlayerUnits.Clear();
                Check(team + " hidden dragon contributes no threat", Damage(board, king, king.transform.position) == 0);
                board.AlivePlayerUnits.Add(dragon); dragon.isWildBoss = false;
                Check(team + " ordinary Boss does not acquire territory-wide attacks", Damage(board, king, king.transform.position) == 0);
                dragon.isWildBoss = true; dragon.wildBossTurnCounter = 2;
                Check(team + " low-threat nonbreath turn does not invent guaranteed breath", Damage(board, king, king.transform.position) == 0);
                dragon.wildBossTurnCounter = 3; dragon.wildBossMaxAP = 9;
                Check(team + " insufficient maximum AP cannot fund breath", Damage(board, king, king.transform.position) == 0);
                dragon.wildBossMaxAP = 20;
                dragon.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Stun, 2));
                Check(team + " custom dragon turn ignores ordinary stun gate", Damage(board, king, king.transform.position) == normal);
                dragon.ActiveEffects.Clear(); dragon.wildBossAtkBuffTurns = 1;
                Check(team + " roar expiring before next breath gives no multiplier", Damage(board, king, king.transform.position) == normal);
                dragon.wildBossAtkBuffTurns = 2;
                Check(team + " retained roar applies after rounded normal damage", Damage(board, king, king.transform.position) == Mathf.RoundToInt(normal * 1.25f));
                dragon.wildBossAtkBuffTurns = 0; dragon.wildBossTurnCounter = 0; dragon.wildBossCurrentThreat = 75;
                Check(team + " high threat breath ignores the four-turn period", Damage(board, king, king.transform.position) == normal);
                dragon.wildBossCurrentThreat = 25; dragon.HP = 4000;
                int raging = DamageCalculator.FinalizeHitDamage(DamageCalculator.CalcRawBase(32, 0) * GameConstants.StrangeKingRageATKBonus);
                Check(team + " observed half HP predicts enrage and aura damage", Damage(board, king, king.transform.position) == raging);
                dragon.HP = 8200; dragon.wildBossCurrentThreat = 75;
                king.HP = 100;
                king.transform.position = new Vector3(4, 1, 6);
                Check(team + " high-threat breath and front-line hit are summed", Damage(board, king, king.transform.position) == normal * 2);
                var blocker = f.Unit(); blocker.HP = blocker.MaxHP = 1000; blocker.DEF = 0;
                blocker.specialAbility = SpecialAbility.None; blocker.passiveskill = PassiveSkill.None;
                blocker.transform.position = new Vector3(4, 1, 5); board.AliveEnemyUnits.Add(blocker);
                Check(team + " surviving known nearer ally takes the frontline follow-up", Damage(board, king, king.transform.position) == normal);
                blocker.HP = 1;
                Check(team + " blocker killed by breath cannot shelter commander from follow-up", Damage(board, king, king.transform.position) == normal * 2);
                board.AliveEnemyUnits.Remove(blocker); blocker.gameObject.SetActive(false);
                king.HP = 10;
                dragon.wildBossCurrentThreat = 25; dragon.wildBossTurnCounter = 3;
                king.transform.position = new Vector3(8, 1, 4);
                heights[6, 4] = 2;
                Check(team + " known mountain blocks scheduled breath like live terrain line", !map.CanAttackAcrossTerrain(dragon, king.transform.position)
                    && Damage(board, king, king.transform.position) == 0);
                known.Remove(new Vector3Int(6, 0, 4));
                Check(team + " hidden terrain cannot supply foreknown cover", Damage(board, king, king.transform.position) == normal);
                known.Add(new Vector3Int(6, 0, 4)); heights[6, 4] = 0;
                dragon.wildBossTerritoryRadius = 2; dragon.transform.position = new Vector3(7, 1, 4);
                Check(team + " territory remains tied to observed center instead of dragon position", ObservedDragonThreat.CanBreatheAt(board, dragon, new Vector3(5, 1, 4))
                    && !ObservedDragonThreat.CanBreatheAt(board, dragon, new Vector3(7, 1, 5)));
                dragon.transform.position = new Vector3(4, 2, 4); dragon.wildBossTerritoryRadius = 6;
                int downhill = DamageCalculator.FinalizeHitDamage(DamageCalculator.CalcRawBase(32, 0) * GameConstants.HighToLowAttackBonus);
                Check(team + " prospective elevation uses normal high-ground damage", Damage(board, king, king.transform.position) == downhill);
                dragon.transform.position = new Vector3(4, 1, 4);
                dragon.ActiveEffects.Add(new ActiveEffect(StatusEffectType.Weaken, 2));
                int weakened = DamageCalculator.FinalizeHitDamage(DamageCalculator.CalcRawBase(32 * StatusEffectSystem.GetATKModifier(dragon), 0));
                Check(team + " observed attack debuff affects normal breath damage", Damage(board, king, king.transform.position) == weakened);
                dragon.ActiveEffects.Clear(); king.ShieldTurns = 1;
                Check(team + " active shield cancels breath damage", Damage(board, king, king.transform.position) == 0);
                king.ShieldTurns = 0; king.IsPrimaryCommander = false; king.kind = Kind.Knight;
                Governor(board);
                Check(team + " ordinary troops remain legal in a known dangerous territory", board.Governor.AllowExecution(
                    new AIAction { ActionType = AIActionType.Move, Unit = king, TargetPos = new Vector3(7, 1, 4) }, board));

                // Opposite territory corners are farther apart than the shared query's generic Near(12).
                dragon.transform.position = new Vector3(1, 1, 1);
                dragon.wildBossTerritoryCenter = new Vector3Int(9, 0, 9); dragon.wildBossTerritoryRadius = 8;
                var farInside = new Vector3(17, 1, 17); var farExit = new Vector3(18, 1, 17);
                king.transform.position = farInside;
                int ordinaryDamage = ObservedDragonThreat.EstimateBreathDamage(board, dragon, king, farInside);
                bool locallyIndexed = false;
                board.Enemies.Rebuild(board.AlivePlayerUnits);
                foreach (var hostile in board.Enemies.Near(farInside, 12f)) if (hostile == dragon) locallyIndexed = true;
                Check(team + " shared query sees observed breath beyond generic range and local index", !locallyIndexed && ordinaryDamage > 0
                    && CounterDamage(board, king, farInside) == ordinaryDamage);
                Check(team + " shared query recognizes an exit outside fixed territory", CounterDamage(board, king, farExit) == 0);
                var ordinaryInside = new AIAction { ActionType = AIActionType.Move, Unit = king, TargetPos = farInside, Score = 100 };
                var ordinaryEscape = new AIAction { ActionType = AIActionType.Move, Unit = king, TargetPos = farExit, Score = 100 };
                var ordinaryMoves = new List<AIAction> { ordinaryInside, ordinaryEscape };
                AIActionModifiers.ApplyCounterDangerPenalty(ordinaryMoves, board, f.Personality);
                Check(team + " ordinary movement ranks safe exit higher without banning territory", ordinaryInside.Score < ordinaryEscape.Score
                    && ordinaryEscape.Score == 100 && ordinaryMoves.Contains(ordinaryInside)
                    && board.Governor.AllowExecution(ordinaryInside, board));
                board.AlivePlayerUnits.Clear();
                Check(team + " shared query ignores a hidden dragon", CounterDamage(board, king, farInside) == 0);
                board.AlivePlayerUnits.Add(dragon); dragon.wildBossTurnCounter = 2;
                Check(team + " shared query respects off-cycle breath even next to dragon", CounterDamage(board, king, new Vector3(2, 1, 1)) == 0);
                dragon.wildBossTurnCounter = 3;
                heights[9, 9] = 2;
                Check(team + " shared query respects known terrain blocking breath", CounterDamage(board, king, farInside) == 0);
                heights[9, 9] = 0; dragon.isWildBoss = false;
                Check(team + " ordinary Boss keeps generic local attack instead of territory breath", CounterDamage(board, king, farInside) == 0
                    && CounterDamage(board, king, new Vector3(2, 1, 1)) > 0);
                dragon.isWildBoss = true;
                var secondDragon = f.Opponent(new Vector3(2, 1, 1), 32);
                secondDragon.team = Team.Obstacle; secondDragon.kind = Kind.Boss; secondDragon.isWildBoss = true;
                secondDragon.wildBossArchetype = WildBossArchetype.Dragon;
                secondDragon.wildBossTerritoryCenter = dragon.wildBossTerritoryCenter; secondDragon.wildBossTerritoryRadius = 8;
                secondDragon.HP = secondDragon.MaxHP = 8200; secondDragon.wildBossMaxAP = 20;
                secondDragon.wildBossCurrentThreat = 25; secondDragon.wildBossTurnCounter = 3;
                secondDragon.passiveskill = PassiveSkill.None; secondDragon.specialAbility = SpecialAbility.None;
                board.AlivePlayerUnits.Add(secondDragon);
                Check(team + " shared query sums observed dragons without counting generic attacks twice", CounterDamage(board, king, new Vector3(2, 1, 2))
                    == ObservedDragonThreat.EstimateBreathDamage(board, dragon, king, new Vector3(2, 1, 2))
                    + ObservedDragonThreat.EstimateBreathDamage(board, secondDragon, king, new Vector3(2, 1, 2)));
                dragon.ATK = secondDragon.ATK = int.MaxValue;
                Check(team + " observed dragon sum saturates instead of overflowing", CounterDamage(board, king, farInside) == int.MaxValue);
                dragon.isWildBoss = secondDragon.isWildBoss = false;
                Check(team + " generic counter damage sum also saturates", CounterDamage(board, king, new Vector3(2, 1, 2)) == int.MaxValue);
            }
            finally { UnityEngine.Object.DestroyImmediate(geometry); }
        }
        Debug.Log("[ObservedDragon] " + passed + " checks passed");
    }
}
#endif
