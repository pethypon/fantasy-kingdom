using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// プレイヤーの移動を記録し、Zキーで1手ずつ巻き戻す機能を提供する。
/// ターン開始時にリセットされる。攻撃は巻き戻せない（不可逆）。
/// </summary>
public class MoveUndoSystem
{
    public struct MoveRecord
    {
        public Status Unit;
        public Vector3 FromPos;
        public Vector3 ToPos;
        public int APCost;
    }

    readonly Stack<MoveRecord> _history = new Stack<MoveRecord>();
    readonly APSystem _apSystem;
    readonly MoveGererater _moveGen;

    public MoveUndoSystem(APSystem apSystem, MoveGererater moveGen)
    {
        _apSystem = apSystem;
        _moveGen = moveGen;
    }

    /// <summary>移動を記録する</summary>
    public void Record(Status unit, Vector3 from, Vector3 to, int apCost)
    {
        _history.Push(new MoveRecord
        {
            Unit = unit,
            FromPos = from,
            ToPos = to,
            APCost = apCost
        });
    }

    /// <summary>巻き戻し可能か</summary>
    public bool CanUndo => _history.Count > 0;

    /// <summary>直前の移動を巻き戻す</summary>
    public bool Undo(TurnGenerater turnGen, PlayerMove playerMove, VisionGenerater visionGen,
        MapCreate mapCreate, CrystalSystem crystalSystem)
    {
        if (_history.Count == 0) return false;

        var record = _history.Pop();
        if (record.Unit == null || !record.Unit.gameObject.activeInHierarchy) return false;

        // ユニットを元の位置に戻す
        record.Unit.transform.position = record.FromPos;

        // 占有セルを更新
        _moveGen.MoveUpdate(record.ToPos, record.FromPos);

        // AP返還
        _apSystem.RefundAP(Team.Player, record.APCost);

        // 疲労を1減らす（移動で+1されたため）
        if (record.Unit.Fatigue > 0)
            record.Unit.Fatigue--;

        // 移動範囲リセット
        _moveGen.MoveReset();

        // 選択状態をリセット
        turnGen.SelectUnit = null;
        playerMove.MenuSwitch = false;
        playerMove.Obj = null;
        playerMove.MP = null;

        // 視界再計算
        visionGen.VisionPoint(mapCreate, _moveGen, crystalSystem);

        ToastMessageUI.Show("移動を取り消しました", ToastMessageUI.MessageType.Info);
        return true;
    }

    /// <summary>ターン開始時にリセット</summary>
    public void Clear()
    {
        _history.Clear();
    }

    public int Count => _history.Count;
}
