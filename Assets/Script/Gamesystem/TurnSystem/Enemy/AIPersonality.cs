using System.Collections.Generic;
using System.Linq;
using UnityEngine;

// =====================================================================
//  AIPersonality — BOSS性格の生成・保持
//  大きい性格(MajorPersonality) + 細かい性格(PersonalityTraits, 合計300pt)
//
//  仕様:
//  ・大きい性格は BOSS駒(Kind.Boss)のみに適用
//  ・通常駒は BOSS の方針を参照して行動に反映（指揮影響）
//  ・BOSS がいない場合は大きい性格補正なし（細かい性格のみで行動）
// =====================================================================
public class AIPersonality
{
    public MajorPersonality Major { get; private set; }
    public PersonalityTraits Traits { get; private set; }

    // BOSS駒への参照（毎ターン更新）
    public Status BossUnit { get; set; }

    // 正規化済みの性格倍率（0〜1）— 評価関数で使用
    public float CautionRate   => Traits.Caution   / 300f;
    public float CommandRate   => Traits.Command    / 300f;
    public float ObsessionRate => Traits.Obsession  / 300f;
    public float DefenseRate   => Traits.Defense    / 300f;
    public float TacticsRate   => Traits.Tactics    / 300f;
    public float DevelopRate   => Traits.Development / 300f;

    // ---- 生成 ----
    public AIPersonality(MajorPersonality major, int seed = -1)
    {
        Major = major;
        Traits = GenerateTraits(seed >= 0 ? seed : System.Environment.TickCount);
        Debug.Log($"[AIPersonality] 大きい性格={Major}  " +
                  $"慎重={Traits.Caution} 指揮={Traits.Command} 執着={Traits.Obsession} " +
                  $"防衛={Traits.Defense} 戦術={Traits.Tactics} 発展={Traits.Development} " +
                  $"合計={Traits.Total}");
    }

    /// <summary>セーブデータから性格を復元する</summary>
    public AIPersonality(MajorPersonality major, PersonalityTraits traits)
    {
        Major = major;
        Traits = traits;
        Debug.Log($"[AIPersonality] セーブから復元: 大きい性格={Major}  合計={Traits.Total}pt");
    }

    // ---- BOSSが生存しているかチェック ----
    public bool HasBoss => BossUnit != null && BossUnit.gameObject.activeInHierarchy && BossUnit.HP > 0;

    // ---- 大きい性格補正を適用すべきか ----
    // 仕様: BOSS駒が生存している場合のみ適用
    public bool ShouldApplyMajorBonus => HasBoss;

    // ---- 指揮影響度 (0〜1) ----
    // BOSS駒からの距離で指揮影響を計算
    // BOSSに近いほど指揮影響が強い（1.0）、遠いほど弱い（0.0）
    public float GetCommandInfluence(Status unit)
    {
        if (!HasBoss || unit == null) return 0f;
        if (unit.IsBoss) return 1f; // BOSS自身は常に最大

        float dist = Vector3.Distance(unit.transform.position, BossUnit.transform.position);
        const float maxRange = 10f;
        return Mathf.Clamp01(1f - (dist / maxRange));
    }

    // ---- BOSS自身の前線参加評価係数 ----
    // 戦闘型=高い、知性型=低い、変動型=局面依存、成長型=中間
    public float BossFrontlineRate
    {
        get
        {
            switch (Major)
            {
                case MajorPersonality.Combat:   return 0.8f;
                case MajorPersonality.Intellect: return 0.2f;
                case MajorPersonality.Adaptive:  return 0.5f;
                case MajorPersonality.Growth:    return 0.5f;
                default: return 0.5f;
            }
        }
    }

    // ---- BOSSの生存ユニットを全敵駒から探す ----
    public void UpdateBossReference(List<Status> aliveEnemyUnits)
    {
        BossUnit = null;
        foreach (var unit in aliveEnemyUnits)
        {
            if (unit != null && unit.gameObject.activeInHierarchy && unit.IsBoss)
            {
                BossUnit = unit;
                break;
            }
        }
    }

    // ---- 300ポイント完全ランダム配分（シード指定で決定論的） ----
    private static PersonalityTraits GenerateTraits(int seed)
    {
        const int total = 300;
        const int minPerTrait = 10;
        const int traitCount = 6;
        int remaining = total - minPerTrait * traitCount;

        var rng = new System.Random(seed);
        int[] values = new int[traitCount];
        for (int i = 0; i < traitCount; i++)
            values[i] = minPerTrait;

        for (int i = 0; i < remaining; i++)
        {
            values[rng.Next(traitCount)]++;
        }

        return new PersonalityTraits
        {
            Caution     = values[0],
            Command     = values[1],
            Obsession   = values[2],
            Defense     = values[3],
            Tactics     = values[4],
            Development = values[5]
        };
    }

    // ---- 大きい性格のランダム決定（シード指定で決定論的） ----
    public static MajorPersonality RandomMajor(int seed = -1)
    {
        int r = seed >= 0 ? new System.Random(seed).Next(4) : Random.Range(0, 4);
        return (MajorPersonality)r;
    }
}
