using System.Collections.Generic;
using UnityEngine;

public class FactionState : MonoBehaviour
{
    // ==== AP データ ====
    [System.Serializable]
    public class APData
    {
        [Header("現在の AP")] public int Current = 15;
        [Header("リセット値")] public int Reset = 15;
        [Header("ボーナス")] public int Plus = 0;
        [Header("ペナルティ")] public int Minus = 0;

        /// <summary>
        /// ターン開始時に AP をリセット。最低保証 3 AP（経済崩壊時のデッドロック防止）。
        /// </summary>
        public void ResetForTurn()
        {
            int raw = Reset + Plus - Minus;
            Current = Mathf.Max(raw, 3);
        }
    }

    // ==== 資源データ ====
    [System.Serializable]
    public class ResourceData
    {
        [Header("基本資源")]
        public int Wood;
        public int Stone;
        public int Water;

        [Header("採掘資源")]
        public int Coal;
        public int IronOre;
        public int MagicOre;

        [Header("加工資源")]
        public int Plank;
        public int CutStone;
        public int Iron;

        [Header("食料")]
        public int Wheat;
        public int Bread;

        [Header("人口")]
        public int Citizen;
    }

    // ==== Inspector 設定 ====
    [Header("Player")]
    [SerializeField] public APData PlayerAP = new APData();
    [SerializeField] public ResourceData PlayerResources = new ResourceData();

    [Header("Enemy")]
    [SerializeField] public APData EnemyAP = new APData();
    [SerializeField] public ResourceData EnemyResources = new ResourceData();

    // ==== AP 取得 / 設定 ====
    private APData GetAPData(Team team) => team == Team.Player ? PlayerAP : EnemyAP;

    public int GetAP(Team team) => GetAPData(team).Current;
    public void SetAP(Team team, int value) => GetAPData(team).Current = value;
    public void ModifyAP(Team team, int delta) => GetAPData(team).Current += delta;

    // ==== ターン開始時 AP リセット ====
    public void ResetAPForTurn(Team team) => GetAPData(team).ResetForTurn();

    // ==== 資源上限の基本値（倉庫なしの場合の上限） ====
    public const int BaseResourceCap = 200;

    // ==== 市民の基本収容上限（家なしの場合） ====
    public const int BaseCitizenCap = 5;

    // ==== サブクリスタル資源 ====
    [Header("サブクリスタル")]
    [SerializeField] public int PlayerSubCrystals = 2;
    [SerializeField] public int EnemySubCrystals = 2;

    // ==== サブクリスタル返却待ちリスト（各要素は残りターン数） ====
    [HideInInspector] public List<int> PlayerPendingReturns = new List<int>();
    [HideInInspector] public List<int> EnemyPendingReturns = new List<int>();

    public int GetSubCrystals(Team team) => team == Team.Player ? PlayerSubCrystals : EnemySubCrystals;
    public void ModifySubCrystals(Team team, int delta)
    {
        if (team == Team.Player) PlayerSubCrystals += delta;
        else EnemySubCrystals += delta;
    }

    /// <summary>破壊後の返却待ちを追加（5ターン後に返却）</summary>
    public void AddPendingReturn(Team team, int turns)
    {
        var list = team == Team.Player ? PlayerPendingReturns : EnemyPendingReturns;
        list.Add(turns);
        Debug.Log($"[FactionState] AddPendingReturn: {team} {turns}ターン後に返却予定 (リスト件数={list.Count})");
    }

    /// <summary>返却待ちの中で最も早い残りターン数を取得（なければ0）</summary>
    public int GetMinPendingReturn(Team team)
    {
        var list = team == Team.Player ? PlayerPendingReturns : EnemyPendingReturns;
        if (list.Count == 0) return 0;
        int min = int.MaxValue;
        foreach (var t in list)
            if (t < min) min = t;
        return min;
    }

    /// <summary>返却待ちの個数を取得</summary>
    public int GetPendingReturnCount(Team team)
    {
        var list = team == Team.Player ? PlayerPendingReturns : EnemyPendingReturns;
        return list.Count;
    }

    /// <summary>毎ターン呼ばれ、タイマーを減らし、0になったら資源を返却する</summary>
    public void TickPendingReturns(Team team)
    {
        var list = team == Team.Player ? PlayerPendingReturns : EnemyPendingReturns;
        if (list == null)
        {
            Debug.LogError($"[FactionState] TickPendingReturns: {team} のリストが null です");
            if (team == Team.Player) PlayerPendingReturns = new List<int>();
            else EnemyPendingReturns = new List<int>();
            return;
        }
        for (int i = list.Count - 1; i >= 0; i--)
        {
            list[i]--;
            Debug.Log($"[FactionState] {team} PendingReturn[{i}]: {list[i]+1}→{list[i]}");
            if (list[i] <= 0)
            {
                ModifySubCrystals(team, 1);
                list.RemoveAt(i);
                Debug.Log($"[FactionState] {team} サブクリスタル返却！ 現在={GetSubCrystals(team)} (残り待ち: {list.Count})");
            }
        }
    }

    // ==== 経済システムが毎ターン書き込む値 ====
    [Header("Player 経済ステータス")]
    public int PlayerCitizenCapacity;
    public int PlayerResourceCapacity;
    public int PlayerBarracksXP;

    [Header("Enemy 経済ステータス")]
    public int EnemyCitizenCapacity;
    public int EnemyResourceCapacity;
    public int EnemyBarracksXP;

    // ==== 実効資源上限 ====
    public int GetResourceCap(Team team)
    {
        int bonus = team == Team.Player ? PlayerResourceCapacity : EnemyResourceCapacity;
        return BaseResourceCap + bonus;
    }

    // ==== 実効市民収容 ====
    public int GetCitizenCap(Team team)
    {
        int bonus = team == Team.Player ? PlayerCitizenCapacity : EnemyCitizenCapacity;
        return BaseCitizenCap + bonus;
    }

    // ==== 兵舎経験値ボーナス% ====
    public int GetBarracksXP(Team team)
    {
        return team == Team.Player ? PlayerBarracksXP : EnemyBarracksXP;
    }
}
