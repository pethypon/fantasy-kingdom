using UnityEngine;

// Responsibility: restore the exact camera and controller state saved before one view-mode toggle.
// Lifetime/Owner: one TurnCameraController, while top-down is active. Maximum retained items: one value.
// Cleanup timing: restore, camera replacement, scene change, or owner destruction.
// Failure behavior: a different/destroyed camera or scene cannot consume the snapshot.
public struct CameraStateSnapshot
{
    public bool IsValid { get; private set; }
    public int CameraInstanceId { get; private set; }
    public int SceneHandle { get; private set; }
    public Vector3 Position { get; private set; }
    public Quaternion Rotation { get; private set; }
    public bool Orthographic { get; private set; }
    public float FieldOfView { get; private set; }
    public float OrthographicSize { get; private set; }
    public float NearClipPlane { get; private set; }
    public float FarClipPlane { get; private set; }
    public bool CameraEnabled { get; private set; }
    public bool ControllerEnabled { get; private set; }
    public bool Dragging { get; private set; }
    public Vector2 PreviousPointer { get; private set; }

    /// <summary>Capture once on entering top-down. No new snapshot is taken on leaving it.</summary>
    public static CameraStateSnapshot Capture(Camera camera, bool controllerEnabled, bool dragging, Vector2 pointer)
    {
        if (camera == null) return default;
        return new CameraStateSnapshot
        {
            IsValid = true, CameraInstanceId = camera.GetInstanceID(), SceneHandle = camera.gameObject.scene.handle,
            Position = camera.transform.position, Rotation = camera.transform.rotation,
            Orthographic = camera.orthographic, FieldOfView = camera.fieldOfView,
            OrthographicSize = camera.orthographicSize, NearClipPlane = camera.nearClipPlane,
            FarClipPlane = camera.farClipPlane, CameraEnabled = camera.enabled,
            ControllerEnabled = controllerEnabled, Dragging = dragging, PreviousPointer = pointer
        };
    }

    public bool CanRestore(Camera camera) => IsValid && camera != null
        && camera.GetInstanceID() == CameraInstanceId && camera.gameObject.scene.handle == SceneHandle;

    /// <summary>Refuse stale snapshots instead of moving the camera belonging to a new scene.</summary>
    public bool Restore(Camera camera)
    {
        if (!CanRestore(camera)) return false;
        camera.transform.SetPositionAndRotation(Position, Rotation);
        camera.orthographic = Orthographic;
        camera.fieldOfView = FieldOfView;
        camera.orthographicSize = OrthographicSize;
        camera.nearClipPlane = NearClipPlane;
        camera.farClipPlane = FarClipPlane;
        camera.enabled = CameraEnabled;
        return true;
    }
}
