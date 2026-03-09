using UnityEngine;

/// <summary>
/// 毎ターン開始時に設置済み建築物を走査し、資源の生産・消費を処理する。
/// 兵舎の AP ボーナスもここで計算する。
/// </summary>
public class EconomySystem : MonoBehaviour
{
    private BuildSystem buildSystem;
    private FactionState factionState;

    // ---- 兵舎 AP ボーナス定数 ----
    const int BarracksAPBonus = 2;

    // 前ターンに適用した兵舎ボーナス（毎ターン差し替え用）
    private int prevPlayerBarracksBonus;
    private int prevEnemyBarracksBonus;

    public void Init(BuildSystem buildSystem, FactionState factionState)
    {
        this.buildSystem = buildSystem;
        this.factionState = factionState;
    }

    /// <summary>
    /// 指定チームの全建築物を走査し、生産処理と AP ボーナスを適用する。
    /// PlayerStart.Entry() で ResetAP() の前に呼ぶ。
    /// </summary>
    public void ProcessTurn(Team team)
    {
        if (buildSystem == null || factionState == null) return;
        if (buildSystem.BuildingParent == null) return;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        var apData = team == Team.Player ? factionState.PlayerAP : factionState.EnemyAP;

        int barracksCount = 0;
        int producedCount = 0;
        int skippedCount = 0;

        foreach (Transform child in buildSystem.BuildingParent)
        {
            var status = child.GetComponent<Status>();
            if (status == null) continue;
            if (status.team != team) continue;
            if (status.HP <= 0) continue;

            // 兵舎カウント
            if (status.facilityKind == FacilityKind.Barracks)
            {
                barracksCount++;
                continue;
            }

            // 生産レシピ取得
            if (!FacilityData.Table.TryGetValue(status.facilityKind, out var info)) continue;
            if (!info.Production.HasProduction) continue;

            // 入力資源チェック
            if (!info.Production.Input.IsEmpty &&
                !FacilityData.CanAffordProduction(res, info.Production.Input))
            {
                skippedCount++;
                Debug.Log($"[EconomySystem] {info.DisplayName}: 入力資源不足でスキップ");
                continue;
            }

            // 入力消費 → 出力生産
            if (!info.Production.Input.IsEmpty)
                FacilityData.ConsumeProduction(res, info.Production.Input);
            FacilityData.AddProduction(res, info.Production.Output);
            producedCount++;
        }

        // 兵舎 AP ボーナス（前ターンの値を差し替え）
        int prevBonus = team == Team.Player ? prevPlayerBarracksBonus : prevEnemyBarracksBonus;
        int newBonus = barracksCount * BarracksAPBonus;
        apData.Plus += newBonus - prevBonus;
        if (team == Team.Player) prevPlayerBarracksBonus = newBonus;
        else prevEnemyBarracksBonus = newBonus;

        if (newBonus > 0)
            Debug.Log($"[EconomySystem] 兵舎×{barracksCount} → AP+{newBonus}");

        Debug.Log($"[EconomySystem] {team} 生産完了: {producedCount}施設稼働, {skippedCount}施設スキップ");
    }
}
