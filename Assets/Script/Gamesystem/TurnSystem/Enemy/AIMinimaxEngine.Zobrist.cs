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

            // Old independent HP XORs aliased swapped HP between actors; horizontal position
            // alone also lost height, facing and skill state. Bind semantic state to this actor.
            h ^= SemanticUnitHash(u);

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
                    | (u.BarrierHasRecovered ? 32UL : 0) | (u.BarrierHasOwnTurnBaseline ? 64UL : 0)
                    | (u.BarrierFullHPObserved ? 128UL : 0)
                    | (u.BarrierRuleRevision == BarrierSystem.RecoveryRuleRevision ? 256UL : 0)
                    | (u.EngineerBarrierConfigured ? 512UL : 0);
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
        h ^= RecoveryHash((ulong)(uint)board.TurnCount ^ ((ulong)(uint)board.RealActorTeam << 32));
        h ^= RecoveryHash((ulong)(uint)board.EnemyAPReset | (ulong)(uint)board.PlayerAPReset << 32);
        return h;
    }

    // Side-to-act is a search-node property, not a map property. Keep BoardHash's old
    // one-argument diagnostic/test API while separating Min and Max transpositions.
    static long SearchBoardHash(SimBoardState board, Team toAct)
        => BoardHash(board) ^ RecoveryHash((ulong)(uint)toAct ^ 0xB74EAE936AF46E21UL);

    static void MixSemantic(ref ulong hash, int value)
    { unchecked { hash = (ulong)RecoveryHash(hash ^ (uint)value); } }
    static long SemanticUnitHash(SimUnit u)
    {
        ulong h = 0xDEADBEEF9312B51FUL;
        MixSemantic(ref h, u.Id); MixSemantic(ref h, (int)u.Team); MixSemantic(ref h, (int)u.Kind);
        MixSemantic(ref h, (int)u.Type); MixSemantic(ref h, (int)u.Facility);
        MixSemantic(ref h, u.Position.x); MixSemantic(ref h, u.Position.y); MixSemantic(ref h, u.Position.z);
        MixSemantic(ref h, u.HP); MixSemantic(ref h, u.MaxHP); MixSemantic(ref h, u.ATK); MixSemantic(ref h, u.DEF);
        MixSemantic(ref h, (int)u.Direction); MixSemantic(ref h, u.Barrier); MixSemantic(ref h, u.MaxBarrier);
        MixSemantic(ref h, u.AssignedSkillId); MixSemantic(ref h, u.SkillCooldown); MixSemantic(ref h, u.Fatigue);
        MixSemantic(ref h, u.ShieldTurns); MixSemantic(ref h, u.ShieldActivated ? 1 : 0); MixSemantic(ref h, (int)u.Passive);
        MixSemantic(ref h, u.IsBoss ? 1 : 0); MixSemantic(ref h, u.IsPrimaryCommander ? 1 : 0);
        MixSemantic(ref h, u.IsWildBoss ? 1 : 0); MixSemantic(ref h, u.WildBossProtected ? 1 : 0);
        MixSemantic(ref h, u.AssassinPassive ? 1 : 0); MixSemantic(ref h, u.AssassinLastEndRound);
        MixSemantic(ref h, u.VisionZeroTurns); MixSemantic(ref h, u.ScoutVisionPenaltyTurns); MixSemantic(ref h, u.InnateVisionBonus);
        long effects = 0;
        foreach (var effect in u.Effects)
        {
            ulong e = (uint)effect.Debuff | (ulong)(uint)effect.Buff << 32;
            effects = unchecked(effects + RecoveryHash(e ^ (ulong)(uint)effect.RemainingTurns * 0x9E3779B97F4A7C15UL));
        }
        h ^= (ulong)effects;
        if (u.CapturedVisionCells != null)
            foreach (var cell in u.CapturedVisionCells)
            {
                ulong v = 0xA3B54F09112DB57FUL;
                MixSemantic(ref v, cell.x); MixSemantic(ref v, cell.y); MixSemantic(ref v, cell.z);
                h ^= v;
            }
        if (u.CrystalCounter != null)
        {
            MixSemantic(ref h, u.CrystalCounter.PlayerPending); MixSemantic(ref h, u.CrystalCounter.EnemyPending);
            MixSemantic(ref h, u.CrystalCounter.MonsterPending); MixSemantic(ref h, u.CrystalCounter.IntruderPending);
            MixSemantic(ref h, u.CrystalCounter.PlayerConsumed); MixSemantic(ref h, u.CrystalCounter.EnemyConsumed);
            MixSemantic(ref h, u.CrystalCounter.MonsterConsumed); MixSemantic(ref h, u.CrystalCounter.IntruderConsumed);
        }
        return RecoveryHash(h);
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
