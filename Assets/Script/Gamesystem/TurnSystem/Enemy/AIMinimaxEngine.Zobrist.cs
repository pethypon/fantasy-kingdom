using UnityEngine;

// =====================================================================
//  AIMinimaxEngine.Zobrist — Zobristハッシュ (トランスポジションテーブル用)
//
//  事前生成した乱数テーブルでユニットごとにXOR合成する。
//  旧実装: 乗算チェーン（順序依存・衝突率高）
//  新実装: XOR合成（順序非依存・衝突率低）
// =====================================================================
public partial class AIMinimaxEngine
{
    static readonly long[,,] ZobristPosition;  // [x, z, team*kindCount+kind]
    static readonly long[,,] ZobristCommanderPosition; // [x, z, team], separate from ordinary actors of the same kind
    static readonly long[] ZobristHP;           // [hp] (0-999)
    static readonly long[] ZobristAP;           // [ap] (0-99)
    const int ZobristBoardSize = 64;
    const int ZobristKindCount = 32;
    const int ZobristHPRange = 1000;
    const int ZobristAPRange = 100;

    static AIMinimaxEngine()
    {
        var rng = new System.Random(42); // 固定シードで決定論的
        ZobristPosition = new long[ZobristBoardSize, ZobristBoardSize, 2 * ZobristKindCount];
        for (int x = 0; x < ZobristBoardSize; x++)
            for (int z = 0; z < ZobristBoardSize; z++)
                for (int tk = 0; tk < 2 * ZobristKindCount; tk++)
                    ZobristPosition[x, z, tk] = NextLong(rng);

        ZobristHP = new long[ZobristHPRange];
        for (int i = 0; i < ZobristHPRange; i++)
            ZobristHP[i] = NextLong(rng);

        ZobristAP = new long[ZobristAPRange];
        for (int i = 0; i < ZobristAPRange; i++)
            ZobristAP[i] = NextLong(rng);

        ZobristCommanderPosition = new long[ZobristBoardSize, ZobristBoardSize, 2];
        for (int x = 0; x < ZobristBoardSize; x++)
            for (int z = 0; z < ZobristBoardSize; z++)
                for (int team = 0; team < 2; team++)
                    ZobristCommanderPosition[x, z, team] = NextLong(rng);
    }

    static long NextLong(System.Random rng)
    {
        byte[] buf = new byte[8];
        rng.NextBytes(buf);
        return System.BitConverter.ToInt64(buf, 0);
    }

    /// <summary>盤面のZobristハッシュ（順序非依存XOR合成）</summary>
    static long BoardHash(SimBoardState board)
    {
        long h = 0;
        for (int i = 0; i < board.Units.Count; i++)
        {
            var u = board.Units[i];
            if (!u.IsAlive) continue;

            int x = Mathf.Clamp(u.Position.x, 0, ZobristBoardSize - 1);
            int z = Mathf.Clamp(u.Position.z, 0, ZobristBoardSize - 1);
            int teamIdx = u.Team == Team.Enemy ? 0 : 1;
            int kindIdx = Mathf.Clamp((int)u.Kind, 0, ZobristKindCount - 1);
            int tk = teamIdx * ZobristKindCount + kindIdx;

            h ^= ZobristPosition[x, z, tk];
            h ^= ZobristHP[Mathf.Clamp(u.HP, 0, ZobristHPRange - 1)];
            if (u.IsPrimaryCommander) h ^= ZobristCommanderPosition[x, z, teamIdx];
            if (u.WildBossProtected) h ^= RecoveryHash((ulong)(uint)(x + z * ZobristBoardSize + tk * 4096) ^ 0xB5AD4ECEDA1CE2A9UL);
            if (u.Type == Type.Unit)
            {
                ulong slot = (ulong)(uint)x | (ulong)(uint)z << 6 | (ulong)(uint)tk << 12;
                ulong barrierState = (ulong)(uint)u.Barrier | (ulong)(uint)u.MaxBarrier << 32;
                h ^= RecoveryHash(barrierState ^ (slot * 0x9E3779B97F4A7C15UL));
                ulong barrierRecovery = (ulong)(uint)Mathf.Clamp(u.BarrierSafeTurns, 0, 4)
                    | (u.BarrierDamaged ? 8UL : 0) | (u.BarrierLastRound == board.TurnCount ? 16UL : 0)
                    | (u.BarrierHasRecovered ? 32UL : 0) | (u.BarrierHasOwnTurnBaseline ? 64UL : 0);
                ulong damageAge = u.BarrierLastDamagedRound < 0 ? 5UL
                    : (ulong)System.Math.Min(4,System.Math.Max(0,(long)board.TurnCount-u.BarrierLastDamagedRound));
                h ^= RecoveryHash((slot << 10) ^ (damageAge << 7) ^ barrierRecovery ^ 0xD1B54A32D192ED03UL);
                ulong recovery = (ulong)(uint)Mathf.Clamp(u.NaturalRecoverySafeTurns, 0, NaturalRecoverySystem.SafeTurnThreshold)
                    | (u.NaturalRecoveryHasBaseline ? 8UL : 0) | (u.NaturalRecoveryHpLost ? 16UL : 0)
                    | (u.NaturalRecoveryLastRound == board.TurnCount ? 32UL : 0);
                if (recovery != 0)
                {
                    // Recovery changes future HP even when current HP/position match another node.
                    // Mix its bounded semantic state with the actor slot, without a large random table.
                    h ^= RecoveryHash((slot << 6) | recovery);
                }
            }
        }
        h ^= ZobristAP[Mathf.Clamp(board.EnemyAP, 0, ZobristAPRange - 1)];
        h ^= ZobristAP[Mathf.Clamp(board.PlayerAP, 0, ZobristAPRange - 1)] << 1;
        return h;
    }

    static long RecoveryHash(ulong value)
    {
        unchecked
        {
            value += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return (long)(value ^ (value >> 31));
        }
    }
}
