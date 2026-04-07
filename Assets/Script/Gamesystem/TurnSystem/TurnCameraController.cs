using UnityEngine;

/// <summary>
/// TurnGenerator から分離されたカメラ操作コンポーネント。
/// Move 入力でカメラを平行移動、Scroll 入力で FOV ズームする。
/// ステートを問わず常時有効。
/// </summary>
[DisallowMultipleComponent]
public class TurnCameraController : MonoBehaviour
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
        var ctx = _turn.Context;

        Vector2 move = ctx.MoveInput;
        Transform cam = ctx.CameraObject;
        if (move != Vector2.zero && cam != null)
        {
            Vector3 moveDir = new Vector3(move.x, 0f, move.y).normalized;
            cam.Translate(moveDir * GameConstants.CameraMoveSpeed * Time.deltaTime, Space.World);

            Vector3 pos = cam.position;
            var mc = _turn.Systems.MapCreate;
            if (mc != null)
            {
                pos.x = Mathf.Clamp(pos.x, 0f, mc.maxX - 10);
                pos.z = Mathf.Clamp(pos.z, 0f, mc.maxZ - 10);
            }
            cam.position = pos;
        }

        float scroll = ctx.ScrollInput;
        if (scroll != 0f && Camera.main != null)
        {
            float fov = Camera.main.fieldOfView - scroll * GameConstants.CameraScrollSpeed;
            Camera.main.fieldOfView = Mathf.Clamp(fov, GameConstants.CameraFOVMin, GameConstants.CameraFOVMax);
        }
    }
}
