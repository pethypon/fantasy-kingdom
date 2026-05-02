using UnityEngine;

/// <summary>
/// 旧資源 → 新資源の変換ユーティリティ（仕様3.4）。
///
/// 旧資源（既にコード上から削除済み）:
///   - Plank（板材）       → Wood に 70%吸収
///   - CutStone（切り石） → Stone に 70%吸収
///   - Coal（石炭）       → 廃棄
///   - IronOre（鉄鉱石）  → 廃棄（現行は Mine が直接 Iron を産出）
///
/// 用途:
///   - 旧バージョンのセーブデータをロードする際の自動移行
///   - 外部データ取り込み時の正規化
///
/// 変換結果はエディタログに出力（移行時のみ）。
/// </summary>
public static class LegacyCostConverter
{
    /// <summary>板材 → Wood の吸収率</summary>
    public const float PlankToWoodRatio = 0.7f;
    /// <summary>切り石 → Stone の吸収率</summary>
    public const float CutStoneToStoneRatio = 0.7f;

    /// <summary>
    /// 旧資源値を含むデータを新資源に正規化する。
    /// 各引数は legacy 値（呼び出し側でセーブから取得して渡す）、
    /// 戻り値は加算すべき (wood, stone) 増分。
    /// </summary>
    public static (int woodAdd, int stoneAdd) ConvertLegacy(
        int legacyPlank, int legacyCutStone, int legacyCoal, int legacyIronOre)
    {
        int woodAdd = Mathf.CeilToInt(legacyPlank * PlankToWoodRatio);
        int stoneAdd = Mathf.CeilToInt(legacyCutStone * CutStoneToStoneRatio);

        if (legacyPlank > 0 || legacyCutStone > 0 || legacyCoal > 0 || legacyIronOre > 0)
        {
            Debug.Log(
                $"[LegacyCostConverter] 旧資源を変換: " +
                $"Plank({legacyPlank})→Wood+{woodAdd}, " +
                $"CutStone({legacyCutStone})→Stone+{stoneAdd}, " +
                $"Coal({legacyCoal})→discard, IronOre({legacyIronOre})→discard");
        }

        return (woodAdd, stoneAdd);
    }

    /// <summary>
    /// FactionState.ResourceData に旧資源値を加算する形で適用する。
    /// </summary>
    public static void ApplyTo(FactionState.ResourceData target,
        int legacyPlank, int legacyCutStone, int legacyCoal, int legacyIronOre)
    {
        if (target == null) return;
        var (woodAdd, stoneAdd) = ConvertLegacy(legacyPlank, legacyCutStone, legacyCoal, legacyIronOre);
        target.Wood += woodAdd;
        target.Stone += stoneAdd;
    }
}
