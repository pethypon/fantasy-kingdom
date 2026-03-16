using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 両チームの NationState を束ねる薄いラッパー。
/// 既存コードの Team パラメータ付き API をそのまま維持する。
/// </summary>
public class FactionState : MonoBehaviour
{
    // ==== 共有型定義（既存コードとの互換性維持） ====
    [System.Serializable]
    public class APData
    {
        [Header("現在の AP")] public int Current = 15;
        [Header("リセット値")] public int Reset = 15;
        [Header("ボーナス")] public int Plus = 0;
        [Header("ペナルティ")] public int Minus = 0;

        public void ResetForTurn()
        {
            int raw = Reset + Plus - Minus;
            Current = Mathf.Max(raw, 3);
        }
    }

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

    // ==== 各国への参照 ====
    [Header("国")]
    public NationState PlayerNation;
    public NationState EnemyNation;

    /// <summary>Team から NationState を取得</summary>
    public NationState GetNation(Team team) => team == Team.Player ? PlayerNation : EnemyNation;

    // ==== 後方互換プロパティ ====
    public APData PlayerAP => PlayerNation.AP;
    public APData EnemyAP => EnemyNation.AP;
    public ResourceData PlayerResources => PlayerNation.Resources;
    public ResourceData EnemyResources => EnemyNation.Resources;

    public int PlayerSubCrystals
    {
        get => PlayerNation.SubCrystals;
        set => PlayerNation.SubCrystals = value;
    }
    public int EnemySubCrystals
    {
        get => EnemyNation.SubCrystals;
        set => EnemyNation.SubCrystals = value;
    }

    public List<int> PlayerPendingReturns => PlayerNation.PendingReturns;
    public List<int> EnemyPendingReturns => EnemyNation.PendingReturns;

    // ==== 経済ステータス（後方互換） ====
    public int PlayerCitizenCapacity
    {
        get => PlayerNation.CitizenCapacity;
        set => PlayerNation.CitizenCapacity = value;
    }
    public int PlayerResourceCapacity
    {
        get => PlayerNation.ResourceCapacity;
        set => PlayerNation.ResourceCapacity = value;
    }
    public int PlayerBarracksXP
    {
        get => PlayerNation.BarracksXP;
        set => PlayerNation.BarracksXP = value;
    }
    public int EnemyCitizenCapacity
    {
        get => EnemyNation.CitizenCapacity;
        set => EnemyNation.CitizenCapacity = value;
    }
    public int EnemyResourceCapacity
    {
        get => EnemyNation.ResourceCapacity;
        set => EnemyNation.ResourceCapacity = value;
    }
    public int EnemyBarracksXP
    {
        get => EnemyNation.BarracksXP;
        set => EnemyNation.BarracksXP = value;
    }

    // ==== AP 取得 / 設定 ====
    public int GetAP(Team team) => GetNation(team).AP.Current;
    public void SetAP(Team team, int value) => GetNation(team).AP.Current = value;
    public void ModifyAP(Team team, int delta) => GetNation(team).AP.Current += delta;
    public void ResetAPForTurn(Team team) => GetNation(team).AP.ResetForTurn();

    // ==== 資源取得 ====
    public ResourceData GetResources(Team team) => GetNation(team).Resources;

    // ==== 定数 ====
    public const int BaseResourceCap = 200;
    public const int BaseCitizenCap = 5;

    // ==== サブクリスタル ====
    public int GetSubCrystals(Team team) => GetNation(team).SubCrystals;
    public void ModifySubCrystals(Team team, int delta) => GetNation(team).SubCrystals += delta;

    public void AddPendingReturn(Team team, int turns)
    {
        var nation = GetNation(team);
        nation.PendingReturns.Add(turns);
        Debug.Log($"[FactionState] AddPendingReturn: {team} {turns}ターン後に返却予定 (リスト件数={nation.PendingReturns.Count})");
    }

    public int GetMinPendingReturn(Team team)
    {
        var list = GetNation(team).PendingReturns;
        if (list.Count == 0) return 0;
        int min = int.MaxValue;
        foreach (var t in list)
            if (t < min) min = t;
        return min;
    }

    public int GetPendingReturnCount(Team team) => GetNation(team).PendingReturns.Count;

    public void TickPendingReturns(Team team)
    {
        var list = GetNation(team).PendingReturns;
        if (list == null)
        {
            Debug.LogError($"[FactionState] TickPendingReturns: {team} のリストが null です");
            GetNation(team).PendingReturns = new List<int>();
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

    // ==== 実効上限 ====
    public int GetResourceCap(Team team) => BaseResourceCap + GetNation(team).ResourceCapacity;
    public int GetCitizenCap(Team team) => BaseCitizenCap + GetNation(team).CitizenCapacity;
    public int GetBarracksXP(Team team) => GetNation(team).BarracksXP;
}
