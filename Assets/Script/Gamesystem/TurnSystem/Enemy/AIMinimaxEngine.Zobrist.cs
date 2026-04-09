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
    static readonly long[] ZobristHP;           // [hp] (0-999)
    static readonly long[] ZobristAP;           // [ap] (0-99)
    const int ZobristBoardSize = 64;
    const int ZobristKindCount = 16;
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
        }
        h ^= ZobristAP[Mathf.Clamp(board.EnemyAP, 0, ZobristAPRange - 1)];
        h ^= ZobristAP[Mathf.Clamp(board.PlayerAP, 0, ZobristAPRange - 1)] << 1;
        return h;
    }
}
