using System.Collections.Generic;
using UnityEngine;

public class APSystem : MonoBehaviour
{
    public enum ActionType { Move, Attack, Build }
    public static int BaseMoveCost => Mathf.Clamp(GameAuthoringRules.Active?.moveAP ?? GameConstants.BaseMoveAPCost, 1, GameConstants.MaxAP);
    public static int BaseAttackCost => Mathf.Clamp(GameAuthoringRules.Active?.attackAP ?? GameConstants.BaseAttackAPCost, 1, GameConstants.MaxAP);

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

    // ==== GameGenerator.Awake() で初期化される ====
    private FactionState _factionState;

    public event System.Action<Team> OnNonMoveAction;

    public void Init(FactionState factionState)
    {
        _factionState = factionState;
    }

    // ==== コスト計算 ====
    public int CalcCost(ActionType action, Status obj,
                        Vector3 from = default, Vector3 to = default)
    {
        int cost = action == ActionType.Move ? BaseMoveCost : action == ActionType.Attack ? BaseAttackCost : BaseCost[action];
        cost += obj.Fatigue;
        if (action == ActionType.Move)
        {
            cost += HeightBonus(obj.kind, from, to);
            // 鈍足・冷気による移動AP追加コスト
            cost += StatusEffectSystem.GetMoveAPCostBonus(obj);
            // Special Ability: 迅速体勢（未移動なら移動コスト-1）
            cost -= SpecialAbilitySystem.GetMoveAPReduction(obj);
        }
        return Mathf.Max(1, cost);
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
        if (action != ActionType.Move) OnNonMoveAction?.Invoke(team);
        _factionState.ModifyAP(team, -cost);
        obj.Fatigue += 1 + GameConstants.GetExtraFatiguePerAction(obj.kind);
        Debug.Log($"[APSystem] {team} / {action}  コスト:{cost}  残AP:{_factionState.GetAP(team)}  疲労:{obj.Fatigue}");
    }

    // ==== ターン開始時 AP リセット ====
    public void ResetAP(Team team)
    {
        var apData = _factionState.GetAPData(team);
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

    // ==== スキルAPコスト計算（Special Ability: 省力化対応） ====
    public int CalcSkillCost(int baseCost, Status obj)
    {
        int reduction = SpecialAbilitySystem.GetSkillAPReduction(obj);
        return Mathf.Max(1, baseCost - reduction);
    }

    // ==== スキルAP消費 + 疲労更新 ====
    public void ConsumeSkill(Team team, int apCost, Status obj)
    {
        // Special Ability: 省力化（最初のスキルのAP消費-1）
        int actualCost = CalcSkillCost(apCost, obj);
        OnNonMoveAction?.Invoke(team);
        _factionState.ModifyAP(team, -actualCost);
        obj.Fatigue += 1 + GameConstants.GetExtraFatiguePerAction(obj.kind);
        obj.FirstSkillUsedThisTurn = true;
        Debug.Log($"[APSystem] {team} / Skill  コスト:{actualCost}(元:{apCost})  残AP:{_factionState.GetAP(team)}  疲労:{obj.Fatigue}");
    }

    // ==== AP返還（undo用） ====
    public void RefundAP(Team team, int amount)
    {
        _factionState.ModifyAP(team, amount);
        Debug.Log($"[APSystem] {team} AP返還: +{amount}  残AP:{_factionState.GetAP(team)}");
    }

    // ==== UI 表示などに使用 ====
    public int GetAP(Team team) => _factionState.GetAP(team);

    /// <summary>ターン開始時に回復する最大AP（UI表示用）</summary>
    public int GetMaxAP(Team team)
    {
        if (_factionState == null) return 0;
        var ap = _factionState.GetAPData(team);
        if (ap == null) return 0;
        return ap.Maximum;
    }

    // ==== 内部ヘルパー ====
    // ---- 建築の実行可否（AP + リソース）----
    public bool CanBuild(Team team, FacilityKind facility, FactionState factionState)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info)) return false;
        if (_factionState.GetAP(team) < info.APCost) return false;

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        return FacilityData.CanAfford(res, info.BuildCost);
    }

    public bool CanBuild(Team team, FacilityDefinitionData definition, FactionState factionState)
    {
        if (definition == null || !definition.IsAvailable(team) || factionState == null) return false;
        var info = definition.GetInfo();
        return factionState.GetAP(team) >= info.APCost && FacilityData.CanAfford(factionState.GetResources(team), info.BuildCost);
    }

    public void ConsumeBuild(Team team, FacilityDefinitionData definition, FactionState factionState)
    {
        if (definition == null || factionState == null) return;
        var info = definition.GetInfo();
        OnNonMoveAction?.Invoke(team);
        factionState.ModifyAP(team, -info.APCost);
        FacilityData.Consume(factionState.GetResources(team), info.BuildCost);
    }

    // ---- 建築の AP + リソース消費 ----
    public void ConsumeBuild(Team team, FacilityKind facility, FactionState factionState)
    {
        if (!FacilityData.Table.TryGetValue(facility, out var info)) return;

        OnNonMoveAction?.Invoke(team);
        _factionState.ModifyAP(team, -info.APCost);

        var res = team == Team.Player ? factionState.PlayerResources : factionState.EnemyResources;
        FacilityData.Consume(res, info.BuildCost);

        Debug.Log($"[APSystem] {team} / Build({facility})  AP:{info.APCost}  残AP:{_factionState.GetAP(team)}");
    }

    // ---- 内部ヘルパー ----
    private int HeightBonus(Kind kind, Vector3 from, Vector3 to)
    {
        if (HeightCostExempt.Contains(kind)) return 0;
        int dy = GridHelper.ToGrid(to).y - GridHelper.ToGrid(from).y;
        return dy == 1 ? Mathf.Clamp(GameAuthoringRules.Active?.heightAP ?? HeightCost, 0, GameConstants.MaxAP) : 0;
    }
}
