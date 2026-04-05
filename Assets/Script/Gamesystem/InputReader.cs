using UnityEngine;

/// <summary>
/// 入力の読み取りを担当する。
/// GameAction (InputSystem) から毎フレーム入力を読み込み、GameContext に書き込む。
/// TurnGenerater から分離された単一責任クラス。
/// </summary>
public class InputReader
{
    private readonly GameAction _gameAction;
    private readonly GameContext _context;

    public InputReader(GameAction gameAction, GameContext context)
    {
        _gameAction = gameAction;
        _context = context;
    }

    /// <summary>毎フレーム呼び出して入力値を更新する</summary>
    public void ReadInputs()
    {
        _context.MoveInput = _gameAction.GamePlay.Move.ReadValue<Vector2>();
        _context.ScrollInput = _gameAction.GamePlay.Scroll.ReadValue<float>();
        _context.LeftClickDown = _gameAction.GamePlay.LeftClick.WasPressedThisFrame();
        _context.RightClickDown = _gameAction.GamePlay.RightClick.WasPressedThisFrame();
        _context.TurnEndDown = _gameAction.GamePlay.TurnEnd.WasPressedThisFrame();
        _context.SelectNormalDown = _gameAction.GamePlay.SelectNormal.WasPressedThisFrame();
        _context.SelectSkillDown = _gameAction.GamePlay.SelectSkill.WasPressedThisFrame();
        _context.ToggleNSDown = _gameAction.GamePlay.ToggleNS.WasPressedThisFrame();
    }

    public void Enable() => _gameAction.Enable();
    public void Disable() => _gameAction.Disable();
    public void Dispose() => _gameAction.Dispose();
}
