using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene camera pan/zoom and reversible top-down view; consumes central input only.
/// </summary>
// Owner: TurnGenerator. Lifetime: scene. Maximum retained items: one camera cache and one value snapshot.
// Cleanup timing: camera replacement, active scene change, owner destruction. Failure: missing camera warns once.
[DisallowMultipleComponent]
public class TurnCameraController : MonoBehaviour
{
    private TurnGenerator _turn;
    [Header("見下ろしカメラ")]
    [SerializeField] private TopDownCameraSettings topDown = new TopDownCameraSettings();
    private TurnInputHandler input;
    private Transform cameraTransform;
    private Camera cachedCamera;
    private CameraStateSnapshot snapshot;
    private bool dragging, missingCameraWarned, resetPointerOnResume, cameraResolved;
    private Vector2 previousPointer;
    private float framedSize, framedAspect;
    private int viewChangedFrame = -1;
    public bool IsTopDown { get; private set; }
    public TopDownCameraSettings Settings => topDown;

    public void Bind(TurnGenerator turn)
    {
        InvalidateSnapshot();
        _turn = turn;
        input = turn == null ? null : turn.GetComponent<TurnInputHandler>();
    }

    private void OnEnable() => SceneManager.activeSceneChanged += OnActiveSceneChanged;
    private void OnDisable() => SceneManager.activeSceneChanged -= OnActiveSceneChanged;
    private void OnDestroy() => InvalidateSnapshot();
    private void OnActiveSceneChanged(Scene before, Scene after) => InvalidateSnapshot();

    /// <summary>Drop scene/camera-owned state without ever restoring it to another camera.</summary>
    public void InvalidateSnapshot()
    {
        snapshot = default;
        IsTopDown = false;
        dragging = resetPointerOnResume = false;
        cameraTransform = null;
        cachedCamera = null;
        cameraResolved = false;
        framedAspect = framedSize = 0f;
    }

    private Camera ResolveCamera(bool retryMissing = false)
    {
        Transform current = _turn == null ? null : _turn.Context.CameraObject;
        if (current != cameraTransform || !cameraResolved || (retryMissing && cachedCamera == null))
        {
            InvalidateSnapshot();
            cameraTransform = current;
            cachedCamera = current == null ? null : current.GetComponent<Camera>();
            cameraResolved = true;
        }
        if (cachedCamera == null && IsTopDown) { snapshot = default; IsTopDown = false; }
        return cachedCamera;
    }

    public void FocusPlayerBase(bool frameStartingArea = false)
    {
        var camera = ResolveCamera();
        var crystal = _turn?.Systems.CrystalSystem;
        if (camera == null || crystal == null) return;
        if (frameStartingArea && !IsTopDown)
        {
            var bounds = new Bounds(crystal.PCP + Vector3.up, new Vector3(8f, 3f, 8f));
            var units = _turn.Systems.UnitSetting?.PlayerUnit;
            if (units != null) foreach (var unit in units.GetComponentsInChildren<Status>())
                if (unit.IsAlive && unit.type == Type.Unit && unit.team == Team.Player)
                    bounds.Encapsulate(new Bounds(unit.transform.position + Vector3.up, new Vector3(2f, 3f, 2f)));
            if (camera.orthographic)
                camera.orthographicSize = StartingOrthographicSize(bounds, camera.transform.rotation, camera.aspect);
            else camera.fieldOfView = 45f;
            float distance = StartingViewDistance(bounds, camera.transform.rotation, camera.fieldOfView, camera.aspect);
            float offset = camera.orthographic ? camera.orthographicSize * .1f
                : distance * Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad) * .1f;
            camera.transform.position = bounds.center - camera.transform.forward * distance - camera.transform.up * offset;
            return;
        }
        camera.transform.position = FocusPosition(camera.transform.position, camera.transform.forward, crystal.PCP);
    }

    /// <summary>Frame only the starting party, with space for the top HUD, rather than the whole fog-covered map.</summary>
    public static float StartingViewDistance(Bounds bounds, Quaternion rotation, float fieldOfView, float aspect)
    {
        if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0f) aspect = 1f;
        if (float.IsNaN(fieldOfView) || float.IsInfinity(fieldOfView)) fieldOfView = 45f;
        float tangent = Mathf.Tan(Mathf.Clamp(fieldOfView, 10f, 90f) * .5f * Mathf.Deg2Rad);
        float distance = 8f;
        var inverse = Quaternion.Inverse(rotation);
        foreach (float x in new[] { -bounds.extents.x, bounds.extents.x })
        foreach (float y in new[] { -bounds.extents.y, bounds.extents.y })
        foreach (float z in new[] { -bounds.extents.z, bounds.extents.z })
        {
            var local = inverse * new Vector3(x, y, z);
            distance = Mathf.Max(distance, Mathf.Max(Mathf.Abs(local.x) / (tangent * aspect * .8f),
                Mathf.Abs(local.y) / (tangent * .7f)) - local.z);
        }
        return distance;
    }

    public static float StartingOrthographicSize(Bounds bounds, Quaternion rotation, float aspect)
    {
        if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0f) aspect = 1f;
        float size = .5f;
        var inverse = Quaternion.Inverse(rotation);
        foreach (float x in new[] { -bounds.extents.x, bounds.extents.x })
        foreach (float y in new[] { -bounds.extents.y, bounds.extents.y })
        foreach (float z in new[] { -bounds.extents.z, bounds.extents.z })
        {
            var local = inverse * new Vector3(x, y, z);
            size = Mathf.Max(size, Mathf.Max(Mathf.Abs(local.x) / (aspect * .8f), Mathf.Abs(local.y) / .7f));
        }
        return size;
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

    /// <summary>MapCreate generates tiles centered on XZ integers, each spanning one world unit.</summary>
    public static Bounds MapBounds(MapCreate map)
    {
        int width = Mathf.Max(1, map.maxX), depth = Mathf.Max(1, map.maxZ);
        float bottom = Mathf.Min(-.5f, map.minY - .5f), top = Mathf.Max(bottom + 1f, map.maxY + .5f);
        return new Bounds(new Vector3((width - 1) * .5f, (bottom + top) * .5f, (depth - 1) * .5f),
            new Vector3(width, top - bottom, depth));
    }

    public static float OrthographicSizeForBounds(Bounds bounds, float aspect, float padding)
    {
        if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0f) aspect = 1f;
        if (float.IsNaN(padding) || float.IsInfinity(padding)) padding = 1.10f;
        return Mathf.Max(.5f, Mathf.Max(bounds.size.z * .5f, bounds.size.x * .5f / aspect)
            * Mathf.Clamp(padding, 1f, 4f));
    }

    /// <summary>Save the normal view once, then frame the current map or restore that exact saved state.</summary>
    public void ToggleTopDown()
    {
        Camera camera = ResolveCamera(true);
        if (camera == null)
        {
            if (!missingCameraWarned)
            {
                missingCameraWarned = true;
                Debug.LogWarning("[Camera] 見下ろし切替に必要なカメラがありません。", this);
            }
            return;
        }
        if (IsTopDown)
        {
            if (snapshot.Restore(camera))
            {
                enabled = snapshot.ControllerEnabled;
                dragging = snapshot.Dragging;
                previousPointer = snapshot.PreviousPointer;
                resetPointerOnResume = dragging;
            }
            snapshot = default;
            IsTopDown = false;
            framedAspect = framedSize = 0f;
        }
        else
        {
            var map = _turn.Systems.MapCreate;
            if (map == null) return;
            snapshot = CameraStateSnapshot.Capture(camera, enabled, dragging, previousPointer);
            var bounds = MapBounds(map);
            framedAspect = camera.aspect;
            framedSize = OrthographicSizeForBounds(bounds, framedAspect, topDown.SafePadding);
            // Height and clip distance come from map extent, not a fixed (0,100,0) viewpoint.
            float distance = bounds.extents.y + Mathf.Max(bounds.size.x, bounds.size.z, 1f);
            if (!topDown.UseOrthographic)
                distance = Mathf.Max(distance, bounds.extents.y
                    + framedSize / Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad));
            camera.transform.SetPositionAndRotation(bounds.center + Vector3.up * distance, Quaternion.Euler(90, 0, 0));
            camera.orthographic = topDown.UseOrthographic;
            camera.orthographicSize = framedSize;
            camera.nearClipPlane = .01f;
            camera.farClipPlane = Mathf.Max(snapshot.FarClipPlane, distance + bounds.size.y + 1f);
            dragging = false;
            IsTopDown = true;
        }
        viewChangedFrame = Time.frameCount;
    }

    /// <summary>Pan and zoom remain available on any turn. No device/keyboard polling occurs here.</summary>
    public void Tick()
    {
        Camera camera = ResolveCamera();
        if (_turn == null || camera == null || !enabled || viewChangedFrame == Time.frameCount) return;
        if (IsTopDown && !snapshot.CanRestore(camera)) { InvalidateSnapshot(); return; }
        var ctx = _turn.Context;
        var map = _turn.Systems.MapCreate;
        Transform cam = camera.transform;
        bool allowPan = !IsTopDown || topDown.AllowPan;
        bool allowZoom = !IsTopDown || topDown.AllowZoom;
        if (IsTopDown && camera.orthographic && map != null && !Mathf.Approximately(camera.aspect, framedAspect))
        {
            float newSize = OrthographicSizeForBounds(MapBounds(map), camera.aspect, topDown.SafePadding);
            camera.orthographicSize *= newSize / Mathf.Max(.5f, framedSize);
            framedSize = newSize; framedAspect = camera.aspect;
        }
        if (allowPan && ctx.MoveInput != Vector2.zero)
            cam.Translate(new Vector3(ctx.MoveInput.x, 0f, ctx.MoveInput.y).normalized
                * GameConstants.CameraMoveSpeed * Time.deltaTime, Space.World);
        if (input != null && input.HasPointer && allowPan)
        {
            Vector2 pointer = input.PointerPosition;
            if (resetPointerOnResume) { previousPointer = pointer; resetPointerOnResume = false; }
            if (input.MiddleButtonDown) { dragging = true; previousPointer = pointer; }
            if (!input.MiddleButtonHeld) dragging = false;
            if (dragging)
            {
                var plane = new Plane(Vector3.up, Vector3.zero);
                var previousRay = camera.ScreenPointToRay(previousPointer);
                var currentRay = camera.ScreenPointToRay(pointer);
                if (plane.Raycast(previousRay, out float a) && plane.Raycast(currentRay, out float b))
                    cam.position += previousRay.GetPoint(a) - currentRay.GetPoint(b);
                previousPointer = pointer;
            }
        }
        else dragging = false;
        if (allowPan && map != null && (ctx.MoveInput != Vector2.zero || dragging))
            cam.position = ClampPosition(cam.position, cam.forward, map.maxX, map.maxZ);
        float scroll = ctx.ScrollInput;
        if (allowZoom && scroll != 0f)
        {
            if (camera.orthographic)
                camera.orthographicSize = Mathf.Clamp(camera.orthographicSize
                    * Mathf.Exp(Mathf.Clamp(-scroll * GameConstants.CameraScrollSpeed * .0002f, -1f, 1f)),
                    .5f, Mathf.Max(100f, framedSize * 4f));
            else camera.fieldOfView = Mathf.Clamp(camera.fieldOfView - scroll * GameConstants.CameraScrollSpeed,
                GameConstants.CameraFOVMin, GameConstants.CameraFOVMax);
        }
    }
}
