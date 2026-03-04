using UnityEngine;

[CreateAssetMenu(menuName = "GameData/UnitData")]
public class UnitData : ScriptableObject
{
    [Header("種類")]
    public Kind kind;

    [Header("基礎ステータス（Lv1の値）")]
    public int baseHP;
    public int baseATK;
    public int baseDEF;

    [Header("成長率（0.0〜1.0 / Lv）例：0.10f = 10%/Lv")]
    public float hpGrowth;
    public float atkGrowth;
    public float defGrowth;

    [Header("維持費（Lv6〜発生。Lv15ごとに加算）")]
    public int upkeepIron;
    public int upkeepMagic;
    public int upkeepBread;
    public int upkeepWood;
    public int upkeepStone;

    [Header("制作コスト（木/石/鉄/魔/水/木板/石材/パン/市民/AP）")]
    public int costWood;
    public int costStone;
    public int costIron;
    public int costMagic;
    public int costWater;
    public int costPlank;
    public int costCutStone;
    public int costBread;
    public int costCitizen;
    public int costAP;

    // ─────────────────────────────────────────────────────────────────
    //  成長計算（GameReference準拠の線形式）
    //  staticにする理由：UnitDataインスタンスなしでUI等から呼べるようにするため
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// 指定レベルのステータス値を返す（GameReference準拠の線形式）。
    /// 式：baseStat + (baseStat × growth × (level - 1))
    /// 例：CalcStat(10, 0.10f, 10) = 10 + 10×0.10×9 = 19
    /// </summary>
    public static int CalcStat(int baseStat, float growth, int level)
    {
        return Mathf.RoundToInt(baseStat + baseStat * growth * (level - 1));
    }

    // ─────────────────────────────────────────────────────────────────
    //  Status への一括適用
    //  「自分のデータをStatusにどう適用するか」はUnitData自身が知っているべき
    //  ゲーム開始時・駒の生成時・レベルアップ後の3タイミングからこのメソッドを呼ぶ
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Status に Level・ATK・HP・DEF をセットする。
    /// このメソッド以外でステータスの計算式を書かない（原則4）。
    /// </summary>
    public void ApplyToStatus(Status status, int level)
    {
        status.Level = level;
        status.ATK = CalcStat(baseATK, atkGrowth, level);
        status.HP = CalcStat(baseHP, hpGrowth, level);
        status.DEF = CalcStat(baseDEF, defGrowth, level);
    }
}
