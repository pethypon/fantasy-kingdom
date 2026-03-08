using System.Collections.Generic;
using UnityEngine;

public class APSystem : MonoBehaviour
{
    public enum ActionType { Move, Attack, Build }

    // ������ �R�X�g��` ������������������������������������������������������������������������������������������������������
    static readonly Dictionary<ActionType, int> BaseCost =
        new Dictionary<ActionType, int>
        {
            { ActionType.Move,   3 },
            { ActionType.Attack, 2 },
        };

    static readonly HashSet<Kind> HeightCostExempt =
        new HashSet<Kind> { Kind.Assassin };

    const int HeightCost = 2;

    // ������ GameGenerater.Awake() �Œ�������� ����������������������������������������������������
    private FactionState _factionState;

    public void Init(FactionState factionState)
    {
        _factionState = factionState;
    }

    // ������ �R�X�g�v�Z ������������������������������������������������������������������������������������������������������
    public int CalcCost(ActionType action, Status obj,
                        Vector3 from = default, Vector3 to = default)
    {
        int cost = BaseCost[action];
        cost += obj.Fatigue;
        if (action == ActionType.Move)
            cost += HeightBonus(obj.kind, from, to);
        return cost;
    }

    // ������ AP ���� ������������������������������������������������������������������������������������������������������������
    public bool CanAct(Team team, ActionType action, Status obj,
                       Vector3 from = default, Vector3 to = default)
        => _factionState.GetAP(team) >= CalcCost(action, obj, from, to);

    // ������ AP ���� + ��J�X�V ��������������������������������������������������������������������������������������
    public void Consume(Team team, ActionType action, Status obj,
                        Vector3 from = default, Vector3 to = default)
    {
        int cost = CalcCost(action, obj, from, to);
        _factionState.ModifyAP(team, -cost);
        obj.Fatigue++;
        Debug.Log($"[APSystem] {team} / {action}  �R�X�g:{cost}  �cAP:{_factionState.GetAP(team)}  ��J:{obj.Fatigue}");
    }

    // ������ �^�[���J�n�� AP ���Z�b�g ��������������������������������������������������������������������������
    public void ResetAP(Team team)
    {
        _factionState.ResetAPForTurn(team);
        Debug.Log($"[APSystem] {team} AP ���Z�b�g �� {_factionState.GetAP(team)}");
    }

    // ������ ��J���Z�b�g ��������������������������������������������������������������������������������������������������
    public void ResetFatigue(Transform unitParent)
    {
        foreach (Status s in unitParent.GetComponentsInChildren<Status>())
        {
            if (s.type == Type.Unit) s.Fatigue = 0;
        }
    }

    // ������ UI �\���ȂǂɎg�p ����������������������������������������������������������������������������������������
    public int GetAP(Team team) => _factionState.GetAP(team);

    // ������ �����w���p�[ ��������������������������������������������������������������������������������������������������
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
