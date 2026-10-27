#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>Semantic TT-key fixtures. Source only / NOT RUN. These are not terrain parity or match-performance evidence.</summary>
public static class AIR4SearchKeyTests
{
    static readonly MethodInfo BoardKey = typeof(AIMinimaxEngine).GetMethod("BoardHash", BindingFlags.Static | BindingFlags.NonPublic,
        null, new[] { typeof(SimBoardState) }, null);
    static readonly MethodInfo SearchKey = typeof(AIMinimaxEngine).GetMethod("SearchBoardHash", BindingFlags.Static | BindingFlags.NonPublic,
        null, new[] { typeof(SimBoardState), typeof(Team) }, null);
    static void Check(string id, bool condition)
    { if (!condition) throw new InvalidOperationException("[R4SearchKey] FAIL " + id); Debug.Log("[R4SearchKey] PASS " + id); }
    static long Hash(SimBoardState board) => (long)BoardKey.Invoke(null, new object[] { board });
    static long Hash(SimBoardState board, Team side) => (long)SearchKey.Invoke(null, new object[] { board, side });

    public static void Run()
    {
        Check("Z-key-api-available", BoardKey != null && SearchKey != null);
        var origin = Fixture(); var corner = Fixture(); corner.Units[0].Position = new Vector3Int(39, 0, 39);
        Check("Z01-origin-vs-corner", Hash(origin) != Hash(corner));
        var xBoundary = Fixture(); xBoundary.Units[0].Position = new Vector3Int(39, 0, 0);
        var zBoundary = Fixture(); zBoundary.Units[0].Position = new Vector3Int(0, 0, 39);
        Check("Z02-distinct-horizontal-boundaries", Hash(origin) != Hash(xBoundary)
            && Hash(origin) != Hash(zBoundary) && Hash(xBoundary) != Hash(zBoundary));
        var elevated = Fixture(); elevated.Units[0].Position += Vector3Int.up * 3;
        Check("Z04-key-only-height-difference", Hash(origin) != Hash(elevated));
        // Both fixtures start at zero HP before assignment. Their recovery flags are identical;
        // only which stable individual owns each HP value differs.
        var originalHp = Fixture(10, 20); var swappedHp = Fixture(20, 10);
        Check("Z05-swapped-hp-is-bound-to-individual", Hash(originalHp) != Hash(swappedHp)
            && originalHp.Units[0].NaturalRecoveryHpLost == swappedHp.Units[0].NaturalRecoveryHpLost
            && originalHp.Units[1].NaturalRecoveryHpLost == swappedHp.Units[1].NaturalRecoveryHpLost);
        var reordered = Fixture(); reordered.Units.Reverse();
        Check("Z06-unit-enumeration-order-invariant", Hash(origin) == Hash(reordered));
        Check("Z07-repeatable-equal-value-board", Hash(origin) == Hash(Fixture())
            && Hash(origin, Team.Enemy) == Hash(Fixture(), Team.Enemy));

        Different("Z08-facing", b => b.Units[0].Direction = Direction.S);
        Different("Z08-skill-cooldown", b => b.Units[0].SkillCooldown = 2);
        Different("Z08-skill-identity", b => b.Units[0].AssignedSkillId = 9);
        Different("Z08-fatigue", b => b.Units[0].Fatigue = 3);
        Different("Z08-shield-turn", b => b.Units[0].ShieldTurns = 1);
        Different("Z08-shield-history", b => b.Units[0].ShieldActivated = true);
        Different("Z08-barrier-maximum", b => b.Units[0].MaxBarrier = 21);
        Different("Z08-commander-status", b => b.Units[0].IsPrimaryCommander = true);
        Different("Z08-vision-disabled", b => b.Units[0].VisionZeroTurns = 1);
        Different("Z08-buff-future", b => b.Units[0].Effects.Add(new SimEffect(BuffType.Offensive, 2)));
        var buffOne = Fixture(); buffOne.Units[0].Effects.Add(new SimEffect(BuffType.Offensive, 1));
        var buffTwo = Fixture(); buffTwo.Units[0].Effects.Add(new SimEffect(BuffType.Offensive, 2));
        Check("Z08-buff-duration", Hash(buffOne) != Hash(buffTwo));
        var debuffOne = Fixture(); debuffOne.Units[0].Effects.Add(new SimEffect(StatusEffectType.Freeze, 1));
        var debuffTwo = Fixture(); debuffTwo.Units[0].Effects.Add(new SimEffect(StatusEffectType.Freeze, 2));
        Check("Z08-debuff-duration", Hash(debuffOne) != Hash(debuffTwo));
        var effectOrderA = Fixture(); effectOrderA.Units[0].Effects.Add(new SimEffect(BuffType.Offensive, 1));
        effectOrderA.Units[0].Effects.Add(new SimEffect(StatusEffectType.Freeze, 2));
        var effectOrderB = Fixture(); effectOrderB.Units[0].Effects.Add(new SimEffect(StatusEffectType.Freeze, 2));
        effectOrderB.Units[0].Effects.Add(new SimEffect(BuffType.Offensive, 1));
        Check("Z06-effects-enumeration-order-invariant", Hash(effectOrderA) == Hash(effectOrderB));
        var crystalIdle = CrystalFixture(); var crystalPending = CrystalFixture(); crystalPending.Units[0].CrystalCounter.EnemyPending = 7;
        var crystalConsumed = CrystalFixture(); crystalConsumed.Units[0].CrystalCounter.EnemyConsumed = 7;
        Check("Z08-crystal-counter-future", Hash(crystalIdle) != Hash(crystalPending)
            && Hash(crystalIdle) != Hash(crystalConsumed) && Hash(crystalPending) != Hash(crystalConsumed));
        var highHpA = CrystalFixture(); highHpA.Units[0].HP = 1500;
        var highHpB = CrystalFixture(); highHpB.Units[0].HP = 2000;
        Check("Z08-full-crystal-hp-not-clamped-key", Hash(highHpA) != Hash(highHpB));
        Check("Z08-side-to-act", Hash(origin, Team.Player) != Hash(origin, Team.Enemy));
        Different("Z08-own-ap", b => b.EnemyAP = 29);
        Different("Z08-opponent-ap", b => b.PlayerAP = 29);
        Different("Z08-ap-reset", b => b.EnemyAPReset = 45);
        Different("Z08-global-round", b => b.TurnCount = 8);
        Different("Z08-normalized-observer", b => b.RealActorTeam = Team.Player);
    }

    static void Different(string id, Action<SimBoardState> change)
    { var original = Fixture(); var altered = Fixture(); change(altered); Check(id, Hash(original) != Hash(altered)); }
    static SimBoardState Fixture(int firstHp = 20, int secondHp = 20)
    {
        var board = new SimBoardState { TurnCount = 7, EnemyAP = 30, PlayerAP = 30,
            EnemyAPReset = 40, PlayerAPReset = 40, RealActorTeam = Team.Enemy, Units = new List<SimUnit>() };
        board.Units.Add(Unit(1, firstHp, Vector3Int.zero, Team.Enemy));
        board.Units.Add(Unit(2, secondHp, new Vector3Int(5, 0, 5), Team.Player)); return board;
    }
    static SimUnit Unit(int id, int hp, Vector3Int position, Team team)
        => new SimUnit { Id = id, Team = team, Kind = Kind.Knight, Type = Type.Unit,
            Position = position, Direction = Direction.N, MaxHP = 100, HP = hp, MaxBarrier = 20,
            Barrier = 20, ATK = 8, AssignedSkillId = 1, SimulationRound = 7 };
    static SimBoardState CrystalFixture()
    {
        var board = Fixture(); board.Units.Clear();
        board.Units.Add(new SimUnit { Id = 9, Team = Team.Player, Kind = Kind.Crystal, Type = Type.Building,
            Position = new Vector3Int(20, 1, 20), MaxHP = 5000, HP = 5000, CrystalCounter = new MainCrystalCounterState() });
        return board;
    }
}
#endif
