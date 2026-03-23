using System.Collections.Generic;
using UnityEngine;

public class APSystem : MonoBehaviour
{
    public enum ActionType { Move, Attack, Build }

    // ==== コスト定義 ====
    static readonly Dictionary<ActionType, int> BaseCost =
        new Dictionary<ActionType, int>
        {
            { ActionType.Move,   3 },
            { ActionType.Attack, 2 },
        };

    static readonly HashSet<Kind> HeightCostExempt =
        new HashSet<Kind> { Kind.Assassin };

    const int HeightCost = 2;

    // ==== GameGenerater.Awake() で初期化される ====
    private FactionState _factionState;

    public void Init(FactionState factionState)
    {
        _factionState = factionState;
    }

    // ==== コスト計算 ====
    public int CalcCost(ActionType action, Status obj,
                        Vector3 from = default, Vector3 to = default)
    {
        int cost = BaseCost[action];
        cost += obj.Fatigue;
        if (action == ActionType.Move)
        {
            cost += HeightBonus(obj.kind, from, to);
            // 鈍足・冷気による移動AP追加コスト
            cost += StatusEffectSystem.GetMoveAPCostBonus(obj);
        }
        return cost;
    }

    // ==== AP 判定 ====
    public bool CanAct(Team team, ActionType action, Status obj,
                       Vector3 from = default, Vector3 to = default)
        => _factionState.GetAP(team) >= CalcCost(action, obj, from, to);

    // ==== AP 消費 + 疲労更新 ====
    public void Consume(Team team, ActionType action, Status obj,
                        Vector3 from = default, Vector3 to = default)
    {
        int cost = CalcCost(action, obj, from, to);
        _factionState.ModifyAP(team, -cost);
        obj.Fatigue++;
        Debug.Log($"[APSystem] {team} / {action}  コスト:{cost}  残AP:{_factionState.GetAP(team)}  疲労:{obj.Fatigue}");
    }

    // ==== ターン開始時 AP リセット ====
    public void ResetAP(Team team)
    {
        var apData = team == Team.Player ? _factionState.PlayerAP : _factionState.EnemyAP;
        int prevCurrent = apData.Current;
        _factionState.ResetAPForTurn(team);
        Debug.Log($"[APSystem] {team} AP リセット: Reset={apData.Reset} Plus={apData.Plus} Minus={apData.Minus} → {apData.Current} (前ターン残={prevCurrent})");
    }

    // ==== 疲労リセット ====
    public void ResetFatigue(Transform unitParent)
    {
        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s.type == Type.Unit) s.Fatigue = 0;
        }
    }

    // ==== スキルAP判定 ====
    public bool CanUseSkill(Team team, int apCost)
        => _factionState.GetAP(team) >= apCost;

    // ==== スキルAP消費 + 疲労更新 ====
    public void ConsumeSkill(Team team, int apCost, Status obj)
    {
        _factionState.ModifyAP(team, -apCost);
        obj.Fatigue++;
        Debug.Log($"[APSystem] {team} / Skill  コスト:{apCost}  残AP:{_factionState.GetAP(team)}  疲労:{obj.Fatigue}");
    }

    // ==== AP返還（undo用） ====
    public void RefundAP(Team team, int amount)
    {
        _factionState.ModifyAP(team, amount);
        Debug.Log($"[APSystem] {team} AP返還: +{amount}  残AP:{_factionState.GetAP(team)}");
    }

    // ==== UI 表示などに使用 ====
    public int GetAP(Team team) => _factionState.GetAP(team);

    // ==== 内部ヘルパー ====
    // ---- 建築の実行可否（AP + リソース）----
    public bool CanBuild(Team team, FacilityKind facility, FactionState factionState)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info)) return false;
        if (_factionState.GetAP(team) < info.APCost) return false;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        return FacilityData.CanAfford(res, info.BuildCost);
    }

    // ---- 建築の AP + リソース消費 ----
    public void ConsumeBuild(Team team, FacilityKind facility, FactionState factionState)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info)) return;

        _factionState.ModifyAP(team, -info.APCost);

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        FacilityData.Consume(res, info.BuildCost);

        Debug.Log($"[APSystem] {team} / Build({facility})  AP:{info.APCost}  残AP:{_factionState.GetAP(team)}");
    }

    // ---- 内部ヘルパー ----
    private int HeightBonus(Kind kind, Vector3 from, Vector3 to)
    {
        if (HeightCostExempt.Contains(kind)) return 0;
        return Mathf.RoundToInt(to.y - from.y) == 1 ? HeightCost : 0;
    }
}
