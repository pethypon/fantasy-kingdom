using UnityEngine;

// =====================================================================
//  AIPersonality — BOSS性格の生成・保持
//  大きい性格(MajorPersonality) + 細かい性格(PersonalityTraits, 合計300pt)
// =====================================================================
public class AIPersonality
{
    public MajorPersonality Major { get; private set; }
    public PersonalityTraits Traits { get; private set; }

    // 正規化済みの性格倍率（0〜1）— 評価関数で使用
    public float CautionRate   => Traits.Caution   / 300f;
    public float CommandRate   => Traits.Command    / 300f;
    public float ObsessionRate => Traits.Obsession  / 300f;
    public float DefenseRate   => Traits.Defense    / 300f;
    public float TacticsRate   => Traits.Tactics    / 300f;
    public float DevelopRate   => Traits.Development / 300f;

    // ---- 生成 ----
    public AIPersonality(MajorPersonality major)
    {
        Major = major;
        Traits = GenerateTraits();
        Debug.Log($"[AIPersonality] 大きい性格={Major}  " +
                  $"慎重={Traits.Caution} 指揮={Traits.Command} 執着={Traits.Obsession} " +
                  $"防衛={Traits.Defense} 戦術={Traits.Tactics} 発展={Traits.Development} " +
                  $"合計={Traits.Total}");
    }

    // ---- 300ポイント完全ランダム配分 ----
    private static PersonalityTraits GenerateTraits()
    {
        // 6項目に300ptをランダム配分（各項目最低10pt保証で極端な0を防ぐ）
        const int total = 300;
        const int minPerTrait = 10;
        const int traitCount = 6;
        int remaining = total - minPerTrait * traitCount; // 240

        int[] values = new int[traitCount];
        for (int i = 0; i < traitCount; i++)
            values[i] = minPerTrait;

        // 残り240ptをランダムに振り分け
        for (int i = 0; i < remaining; i++)
        {
            values[Random.Range(0, traitCount)]++;
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

    // ---- 大きい性格のランダム決定 ----
    public static MajorPersonality RandomMajor()
    {
        int r = Random.Range(0, 4);
        return (MajorPersonality)r;
    }
}
