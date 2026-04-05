using UnityEngine;

/// <summary>
/// ゲームの選択状態・ターン情報を管理する。
/// TurnGenerator の散在した状態フィールドを集約し、状態管理を明確化する。
/// </summary>
public class GameContext
{
    // ================================================================
    //  選択状態
    // ================================================================
    public Status SelectUnit { get; set; }
    public Vector3 OldCell { get; set; }
    public Vector3 NewCell { get; set; }

    // ================================================================
    //  ターン情報
    // ================================================================
    public int Turn { get; set; }

    // ================================================================
    //  カメラ
    // ================================================================
    public Transform CameraObject { get; set; }

    // ================================================================
    //  入力バッファ（毎フレーム ReadInputs() で更新）
    // ================================================================
    public Vector2 MoveInput { get; set; }
    public float ScrollInput { get; set; }
    public bool LeftClickDown { get; set; }
    public bool RightClickDown { get; set; }
    public bool TurnEndDown { get; set; }
    public bool SelectNormalDown { get; set; }
    public bool SelectSkillDown { get; set; }
    public bool ToggleNSDown { get; set; }

    /// <summary>選択状態をクリアする</summary>
    public void ClearSelection()
    {
        SelectUnit = null;
        OldCell = Vector3.zero;
        NewCell = Vector3.zero;
    }
}
