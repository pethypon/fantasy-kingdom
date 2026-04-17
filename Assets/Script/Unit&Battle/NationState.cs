using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 1チーム（国）分の資源・AP・経済ステータスを保持する。
/// PlayerCrystal / EnemyCrystal の子オブジェクトに配置して
/// Inspector で各国の状態をリアルタイムに確認できる。
/// </summary>
public class NationState : MonoBehaviour
{
    // ==== Inspector 設定 ====
    [Header("AP")]
    public FactionState.APData AP = new FactionState.APData();

    [Header("資源")]
    public FactionState.ResourceData Resources = new FactionState.ResourceData();

    [Header("サブクリスタル")]
    public int SubCrystals = 2;

    [Header("経済ステータス")]
    public int CitizenCapacity;
    public int ResourceCapacity;
    public int BarracksXP;

    // ==== サブクリスタル返却待ちリスト ====
    [HideInInspector] public List<int> PendingReturns = new List<int>();

    /// <summary>パン不足が連続しているターン数（10ターン連続で市民減少）</summary>
    [HideInInspector] public int StarvationCounter = 0;

    /// <summary>この国が生存しているターン数（クリスタル序盤収入の逓減に使用）</summary>
    [HideInInspector] public int TurnsAlive = 0;
}
