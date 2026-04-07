using UnityEngine;

/// <summary>
/// TurnGenerator から分離された入力読み取りコンポーネント。
/// GameAction の値を毎フレーム GameContext にコピーする。
/// </summary>
[DisallowMultipleComponent]
public class TurnInputHandler : MonoBehaviour
{
    private TurnGenerator _turn;

    public void Bind(TurnGenerator turn)
    {
        _turn = turn;
    }

    /// <summary>TurnGenerator.Update から呼ばれる</summary>
    public void Tick()
    {
        if (_turn == null) return;
        var gameaction = _turn.GameAction;
        if (gameaction == null) return;

        var ctx = _turn.Context;
        ctx.MoveInput        = gameaction.GamePlay.Move.ReadValue<Vector2>();
        ctx.ScrollInput      = gameaction.GamePlay.Scroll.ReadValue<float>();
        ctx.LeftClickDown    = gameaction.GamePlay.LeftClick.WasPressedThisFrame();
        ctx.RightClickDown   = gameaction.GamePlay.RightClick.WasPressedThisFrame();
        ctx.TurnEndDown      = gameaction.GamePlay.TurnEnd.WasPressedThisFrame();
        ctx.SelectNormalDown = gameaction.GamePlay.SelectNormal.WasPressedThisFrame();
        ctx.SelectSkillDown  = gameaction.GamePlay.SelectSkill.WasPressedThisFrame();
        ctx.ToggleNSDown     = gameaction.GamePlay.ToggleNS.WasPressedThisFrame();
    }
}
