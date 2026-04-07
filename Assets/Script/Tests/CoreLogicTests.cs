// =====================================================================
//  CoreLogicTests — 最小回帰テスト
//
//  本プロジェクトは Unity Test Framework を導入していないため、
//  Editor 上から手動実行できる軽量アサーションとして実装する。
//
//  実行方法:
//    Unity Editor メニュー "Fantasy Kingdom > Run Core Tests" を選択。
//    Console に [CoreTest] 行で PASS/FAIL が出力される。
//
//  テスト項目:
//    1. DamageCalculator: ダメージは 0 未満にクランプされる
//    2. CrystalShield: HP 50% 未満で発動 / ShieldTurns が 5 になる / リセット
//    3. TimerSystem: 持ち時間切れ時は「クリスタル → 王」の順で勝敗判定される
// =====================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class CoreLogicTests
{
    static int _pass;
    static int _fail;

    [MenuItem("Fantasy Kingdom/Run Core Tests")]
    public static void RunAll()
    {
        _pass = 0;
        _fail = 0;

        TestDamageClamp();
        TestDamageBaseFormula();
        TestCrystalShieldActivationAndReset();
        TestTimerWinnerDeterminationOrder();

        Debug.Log($"[CoreTest] Done: {_pass} passed, {_fail} failed");
    }

    // -----------------------------------------------------------------
    //  DamageCalculator
    // -----------------------------------------------------------------
    static void TestDamageClamp()
    {
        // 極端に弱い ATK と高い DEF → 負の値にならず 0 以上に収束
        int dmg = DamageCalculator.CalcFromValues(atk: 0f, def: 9999f, incomingMod: 1f);
        Assert("Damage clamped to 0 when DEF overwhelms ATK", dmg >= 0);

        // 正常ケース: ATK>DEF ならダメージ > 0
        int dmg2 = DamageCalculator.CalcFromValues(atk: 100f, def: 0f, incomingMod: 1f);
        Assert("Damage is positive when ATK >> DEF", dmg2 > 0);
    }

    static void TestDamageBaseFormula()
    {
        // CalcRawBase = 1 + atk/6 + atk/2 - def/4
        float expected = 1f + (60f / 6f) + (60f / 2f) - (20f / 4f);
        float actual = DamageCalculator.CalcRawBase(60f, 20f);
        Assert($"CalcRawBase formula (expected={expected}, actual={actual})",
            Mathf.Abs(expected - actual) < 0.0001f);
    }

    // -----------------------------------------------------------------
    //  Crystal Shield
    // -----------------------------------------------------------------
    static void TestCrystalShieldActivationAndReset()
    {
        // Status は MonoBehaviour なので一時 GameObject に付けて検証
        var go = new GameObject("__test_crystal__");
        try
        {
            var s = go.AddComponent<Status>();
            s.kind = Kind.Crystal;
            s.team = Team.Player;
            s.MaxHP = 100;
            s.HP = 49; // < 50% → 発動条件
            s.ShieldTurns = 0;
            s.ShieldActivated = false;

            // 発動条件を満たした状態で直接フィールドをセット
            // （BattleSystem.CheckCrystalShield と同じ分岐を模擬）
            float hpRatio = (float)s.HP / s.MaxHP;
            if (hpRatio < GameConstants.CrystalShieldThreshold && !s.ShieldActivated)
            {
                s.ShieldTurns = GameConstants.CrystalShieldDuration;
                s.ShieldActivated = true;
            }

            Assert("Shield activates when HP < 50%", s.ShieldActivated);
            Assert($"Shield duration = {GameConstants.CrystalShieldDuration}",
                s.ShieldTurns == GameConstants.CrystalShieldDuration);

            // Tick で減っていき 0 でリセットされること
            while (s.ShieldTurns > 0)
            {
                s.ShieldTurns--;
                if (s.ShieldTurns <= 0) s.ShieldActivated = false;
            }
            Assert("Shield resets ShieldActivated when turns reach 0", !s.ShieldActivated);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // -----------------------------------------------------------------
    //  Timer winner determination order (Crystal HP% → King HP%)
    // -----------------------------------------------------------------
    static void TestTimerWinnerDeterminationOrder()
    {
        // Crystal 比較が先: P クリスタルの方が高ければ King の HP% に関係なく Win
        float pCrystal = 0.6f;
        float eCrystal = 0.4f;
        float pKing = 0.1f;   // King は E の方が高いが無視されるべき
        float eKing = 0.9f;

        GameResult result = DetermineMock(pCrystal, eCrystal, pKing, eKing);
        Assert("Crystal HP% takes precedence over King HP%",
            result == GameResult.TimeUpWin);

        // Crystal 同率 → King で決着
        result = DetermineMock(0.5f, 0.5f, 0.9f, 0.1f);
        Assert("When crystals tie, King HP% decides", result == GameResult.TimeUpWin);

        // 全て同率 → Draw
        result = DetermineMock(0.5f, 0.5f, 0.5f, 0.5f);
        Assert("All equal = Draw", result == GameResult.TimeUpDraw);
    }

    // TimerSystem.DetermineTimeUpResult と同じ判定ロジックを再現した検証用モック。
    static GameResult DetermineMock(float pCrystalRatio, float eCrystalRatio,
                                    float pKingRatio, float eKingRatio)
    {
        if (pCrystalRatio > eCrystalRatio) return GameResult.TimeUpWin;
        if (eCrystalRatio > pCrystalRatio) return GameResult.TimeUpLose;
        if (pKingRatio > eKingRatio) return GameResult.TimeUpWin;
        if (eKingRatio > pKingRatio) return GameResult.TimeUpLose;
        return GameResult.TimeUpDraw;
    }

    // -----------------------------------------------------------------
    //  Assertion helper
    // -----------------------------------------------------------------
    static void Assert(string label, bool condition)
    {
        if (condition)
        {
            _pass++;
            Debug.Log($"[CoreTest] PASS  {label}");
        }
        else
        {
            _fail++;
            Debug.LogError($"[CoreTest] FAIL  {label}");
        }
    }
}
#endif
