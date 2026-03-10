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

        public void ResetForTurn() => Current = Reset + Plus - Minus;
    }

    // ==== 資源データ ====
    [System.Serializable]
    public class ResourceData
    {
        public int Wood;
        public int Stone;
        public int Coal;
        public int IronOre;
        public int Iron;
        public int MagicOre;
        public int Wheat;
        public int Bread;
        public int Water;
        public int Plank;       // 追加（GameReference 準拠配布資源）
        public int CutStone;    // 追加（GameReference 準拠配布資源）
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
        for (int i = list.Count - 1; i >= 0; i--)
        {
            list[i]--;
            if (list[i] <= 0)
            {
                ModifySubCrystals(team, 1);
                list.RemoveAt(i);
                Debug.Log($"[FactionState] {team} サブクリスタル返却 (残り待ち: {list.Count})");
            }
        }
    }

    // ==== 経済システムが毎ターン書き込む値 ====
    [HideInInspector] public int PlayerCitizenCapacity;
    [HideInInspector] public int PlayerResourceCapacity;
    [HideInInspector] public int PlayerBarracksXP;

    [HideInInspector] public int EnemyCitizenCapacity;
    [HideInInspector] public int EnemyResourceCapacity;
    [HideInInspector] public int EnemyBarracksXP;

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
