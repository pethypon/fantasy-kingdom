using UnityEngine;

/// <summary>
/// カメラ操作（WASD移動・スクロールズーム）を担当する。
/// 全ステートで常時有効。TurnGenerater から分離された単一責任クラス。
/// </summary>
public class CameraController
{
    private readonly GameContext _context;
    private readonly GameSystems _systems;

    public CameraController(GameContext context, GameSystems systems)
    {
        _context = context;
        _systems = systems;
    }

    /// <summary>毎フレーム呼び出してカメラを更新する</summary>
    public void UpdateCamera()
    {
        UpdateMove();
        UpdateZoom();
    }

    private void UpdateMove()
    {
        Vector2 move = _context.MoveInput;
        Transform cam = _context.CameraObject;
        if (move == Vector2.zero || cam == null) return;

        Vector3 moveDir = new Vector3(move.x, 0f, move.y).normalized;
        cam.Translate(moveDir * GameConstants.CameraMoveSpeed * Time.deltaTime, Space.World);

        Vector3 pos = cam.position;
        var mc = _systems.MapCreate;
        if (mc != null)
        {
            pos.x = Mathf.Clamp(pos.x, 0f, mc.maxX - 10);
            pos.z = Mathf.Clamp(pos.z, 0f, mc.maxZ - 10);
        }
        cam.position = pos;
    }

    private void UpdateZoom()
    {
        float scroll = _context.ScrollInput;
        if (scroll == 0f || Camera.main == null) return;

        float fov = Camera.main.fieldOfView - scroll * GameConstants.CameraScrollSpeed;
        Camera.main.fieldOfView = Mathf.Clamp(fov, GameConstants.CameraFOVMin, GameConstants.CameraFOVMax);
    }
}
