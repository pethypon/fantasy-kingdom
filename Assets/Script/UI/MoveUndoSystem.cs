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
        public int FatigueBefore;
        public bool HadMovedBefore;
    }

    readonly Stack<MoveRecord> _history = new Stack<MoveRecord>();
    readonly APSystem _apSystem;
    readonly MoveGenerator _moveGen;

    public MoveUndoSystem(APSystem apSystem, MoveGenerator moveGen)
    {
        _apSystem = apSystem;
        _moveGen = moveGen;
        _apSystem.OnNonMoveAction += team => { if (team == Team.Player) Clear(); };
    }

    /// <summary>移動を記録する</summary>
    public void Record(Status unit, Vector3 from, Vector3 to, int apCost)
    {
        _history.Push(new MoveRecord
        {
            Unit = unit,
            FromPos = from,
            ToPos = to,
            APCost = apCost,
            FatigueBefore = unit.Fatigue,
            HadMovedBefore = unit.HasMovedThisTurn
        });
    }

    /// <summary>巻き戻し可能か</summary>
    public bool CanUndo => _history.Count > 0;

    /// <summary>直前の移動を巻き戻す</summary>
    public bool Undo(TurnGenerator turnGen, PlayerMove playerMove, VisionGenerator visionGen,
        MapCreate mapCreate, CrystalSystem crystalSystem)
    {
        if (_history.Count == 0) return false;

        var record = _history.Peek();
        if (record.Unit == null || !record.Unit.IsAlive || !record.Unit.gameObject.activeInHierarchy
            || record.Unit.transform.position != record.ToPos)
        { Clear(); return false; }
        _moveGen.UnitPointCore();
        if (_moveGen.IsOccupied(_moveGen.Cell(record.FromPos))) return false;
        _history.Pop();

        // ユニットを元の位置に戻す
        record.Unit.transform.position = record.FromPos;

        // 占有セルを更新
        _moveGen.MoveUpdate(record.ToPos, record.FromPos);

        // AP返還
        _apSystem.RefundAP(Team.Player, record.APCost);

        // 駒固有の追加疲労や初回移動割引も、記録時点へ正確に戻す。
        record.Unit.Fatigue = record.FatigueBefore;
        record.Unit.HasMovedThisTurn = record.HadMovedBefore;

        // 移動範囲リセット
        _moveGen.MoveReset();

        // 選択状態をリセット
        turnGen.Context.SelectUnit = null;
        playerMove.MenuSwitch = false;
        playerMove.SelectedUnit = null;
        playerMove.ClickedUnit = null;

        // 視界再計算（駒が移動して盤面が変化したため同フレームキャッシュを無効化）
        visionGen.MarkVisionDirty();
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
