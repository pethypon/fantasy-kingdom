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

    public void FocusPlayerBase()
    {
        var camera = _turn?.Context.CameraObject;
        var crystal = _turn?.Systems.CrystalSystem;
        if (camera == null || crystal == null) return;
        camera.position = FocusPosition(camera.position, camera.forward, crystal.PCP);
    }

    public static Vector3 FocusPosition(Vector3 position, Vector3 forward, Vector3 target)
    {
        if (forward.y >= -0.001f) return position;
        float distance = (target.y - position.y) / forward.y;
        return distance > 0f ? target - forward * distance : position;
    }

    public static Vector3 ClampPosition(Vector3 position, Vector3 forward, int width, int depth)
    {
        if (forward.y >= -0.001f) return position;
        Vector3 focus = position + forward * (-position.y / forward.y);
        Vector3 clamped = new Vector3(Mathf.Clamp(focus.x, 0, Mathf.Max(0, width - 1)), 0,
            Mathf.Clamp(focus.z, 0, Mathf.Max(0, depth - 1)));
        return position + clamped - focus;
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

            var map = _turn.Systems.MapCreate;
            if (map != null) cam.position = ClampPosition(cam.position, cam.forward, map.maxX, map.maxZ);
        }

        float scroll = ctx.ScrollInput;
        if (scroll != 0f && Camera.main != null)
        {
            float fov = Camera.main.fieldOfView - scroll * GameConstants.CameraScrollSpeed;
            Camera.main.fieldOfView = Mathf.Clamp(fov, GameConstants.CameraFOVMin, GameConstants.CameraFOVMax);
        }
    }
}
