#if UNITY_EDITOR
using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

/// <summary>Real scene camera/input regression fixtures; never change AI decisions or map tiles.</summary>
public static class TopDownCameraTests
{
    static int passed;
    const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    static void Check(string name, bool value)
    {
        if (!value) throw new Exception("[TopDownCamera] FAIL " + name);
        passed++;
        Debug.Log("[TopDownCamera] PASS " + name);
    }
    static FieldInfo Field(string name) => typeof(TurnCameraController).GetField(name, PrivateInstance);
    static void PermitSameFrameTick(TurnCameraController controller) => Field("viewChangedFrame").SetValue(controller, -1);

    public static void Run(GameSystems systems, TurnGenerator turn)
    {
        passed = 0;
        var controller = turn.GetComponent<TurnCameraController>();
        var input = turn.GetComponent<TurnInputHandler>();
        var camera = turn.Context.CameraObject.GetComponent<Camera>();
        var map = systems.MapCreate;
        var original = CameraStateSnapshot.Capture(camera, controller.enabled,
            (bool)Field("dragging").GetValue(controller), (Vector2)Field("previousPointer").GetValue(controller));
        var settings = controller.Settings;
        int width = map.maxX, depth = map.maxZ;
        float aspect = camera.aspect;
        Vector2 move = turn.Context.MoveInput;
        float scroll = turn.Context.ScrollInput;
        bool orthographic = settings.UseOrthographic, pan = settings.AllowPan, zoom = settings.AllowZoom;
        try
        {
            Chords();
            settings.UseOrthographic = true;
            Field("dragging").SetValue(controller, true);
            Field("previousPointer").SetValue(controller, new Vector2(163, 257));
            var normal = CameraStateSnapshot.Capture(camera, controller.enabled, true, new Vector2(163, 257));
            for (int i = 0; i < 100; i++)
            {
                controller.ToggleTopDown();
                if (!controller.IsTopDown) throw new Exception("TopDown did not enter at toggle " + i);
                camera.transform.position += new Vector3(2, 0, -3);
                camera.orthographicSize *= .7f;
                controller.ToggleTopDown();
                if (controller.IsTopDown || Vector3.Distance(camera.transform.position, normal.Position) > .001f
                    || Quaternion.Angle(camera.transform.rotation, normal.Rotation) > .01f)
                    throw new Exception("Camera drift at toggle " + i);
            }
            Check("100 enter/exit cycles preserve position and rotation", !controller.IsTopDown);
            Check("projection/FOV/size/clips restore exactly", camera.orthographic == normal.Orthographic
                && camera.fieldOfView == normal.FieldOfView && camera.orthographicSize == normal.OrthographicSize
                && camera.nearClipPlane == normal.NearClipPlane && camera.farClipPlane == normal.FarClipPlane);
            Check("controller enabled/drag/pointer restore", controller.enabled == normal.ControllerEnabled
                && (bool)Field("dragging").GetValue(controller) == normal.Dragging
                && (Vector2)Field("previousPointer").GetValue(controller) == normal.PreviousPointer);
            controller.enabled = false; camera.enabled = false;
            controller.ToggleTopDown(); controller.ToggleTopDown();
            Check("disabled camera/controller state also restores", !camera.enabled && !controller.enabled);
            camera.enabled = original.CameraEnabled; controller.enabled = original.ControllerEnabled;
            int sceneSubscriptions = SceneSubscriptions(controller);
            for (int i = 0; i < 10; i++) { controller.enabled = false; controller.enabled = true; }
            Check("ten enable/disable cycles retain one scene subscription", sceneSubscriptions == 1
                && SceneSubscriptions(controller) == sceneSubscriptions);

            foreach (var size in new[] { new Vector2Int(1, 1), new Vector2Int(7, 9), new Vector2Int(35, 35),
                new Vector2Int(256, 128), new Vector2Int(128, 256) })
            foreach (float ratio in new[] { 16f / 9f, 16f / 10f, 21f / 9f, 9f / 16f })
            {
                map.maxX = size.x; map.maxZ = size.y; camera.aspect = ratio;
                controller.ToggleTopDown();
                Check("map " + size + " aspect " + ratio.ToString("F3") + " fully framed",
                    ContainsCorners(camera, TurnCameraController.MapBounds(map)));
                controller.ToggleTopDown();
            }
            map.maxX = width; map.maxZ = depth; camera.aspect = aspect;
            settings.UseOrthographic = false;
            controller.ToggleTopDown();
            Check("optional perspective mode also frames top and bottom corners", !camera.orthographic
                && ContainsCorners(camera, TurnCameraController.MapBounds(map)));
            controller.ToggleTopDown();
            settings.UseOrthographic = true;
            controller.ToggleTopDown();
            PermitSameFrameTick(controller);
            settings.AllowPan = true; settings.AllowZoom = true;
            turn.Context.MoveInput = Vector2.right; turn.Context.ScrollInput = 120;
            float sizeBefore = camera.orthographicSize;
            var positionBefore = camera.transform.position;
            controller.Tick();
            Check("top-down wheel zoom remains available", camera.orthographicSize < sizeBefore);
            Check("top-down pan remains available", Time.deltaTime == 0f || camera.transform.position.x > positionBefore.x);
            settings.AllowPan = false; settings.AllowZoom = false;
            positionBefore = camera.transform.position; sizeBefore = camera.orthographicSize;
            controller.Tick();
            Check("inspectable pan/zoom switches are respected", camera.transform.position == positionBefore
                && camera.orthographicSize == sizeBefore);
            controller.ToggleTopDown();
            turn.Context.MoveInput = Vector2.zero; turn.Context.ScrollInput = 0;
            ActualInput(input, controller, turn);
            CameraReplacementAndScene(controller, camera, turn);
            MissingCamera(controller, turn);
            Check("invalid aspect/padding cannot make framing NaN", !float.IsNaN(
                TurnCameraController.OrthographicSizeForBounds(TurnCameraController.MapBounds(map), float.NaN, float.NaN)));
            Debug.Log("[TopDownCamera] ALL PASSED count=" + passed);
        }
        finally
        {
            AIDiaryUI.Close();
            controller.InvalidateSnapshot();
            turn.Context.CameraObject = camera.transform;
            original.Restore(camera);
            controller.enabled = original.ControllerEnabled;
            Field("dragging").SetValue(controller, original.Dragging);
            Field("previousPointer").SetValue(controller, original.PreviousPointer);
            Field("resetPointerOnResume").SetValue(controller, false);
            map.maxX = width; map.maxZ = depth; camera.aspect = aspect;
            settings.UseOrthographic = orthographic; settings.AllowPan = pan; settings.AllowZoom = zoom;
            turn.Context.MoveInput = move; turn.Context.ScrollInput = scroll;
            turn.Context.ConsumeUICommand();
        }
    }

    static void Chords()
    {
        var none = TurnInputHandler.HiddenCommand.None;
        Check("LeftShift+F7", TurnInputHandler.EvaluateHiddenCommands(true, false, true, false, false, false)
            == TurnInputHandler.HiddenCommand.ToggleDiary);
        Check("RightShift+F7", TurnInputHandler.EvaluateHiddenCommands(false, true, true, false, false, false)
            == TurnInputHandler.HiddenCommand.ToggleDiary);
        Check("F7 alone does nothing", TurnInputHandler.EvaluateHiddenCommands(false, false, true, false, false, false) == none);
        Check("Shift alone/held F7 does not repeat", TurnInputHandler.EvaluateHiddenCommands(true, false, false, false, false, false) == none);
        Check("LeftCtrl+Space", TurnInputHandler.EvaluateHiddenCommands(false, false, false, true, false, true)
            == TurnInputHandler.HiddenCommand.ToggleTopDown);
        Check("RightCtrl+Space", TurnInputHandler.EvaluateHiddenCommands(false, false, false, false, true, true)
            == TurnInputHandler.HiddenCommand.ToggleTopDown);
        Check("Space alone preserves its existing action", TurnInputHandler.EvaluateHiddenCommands(false, false, false, false, false, true) == none);
        Check("Ctrl alone/held Space does not repeat", TurnInputHandler.EvaluateHiddenCommands(false, false, false, true, false, false) == none);
    }

    static bool ContainsCorners(Camera camera, Bounds bounds)
    {
        foreach (float x in new[] { bounds.min.x, bounds.max.x })
        foreach (float y in new[] { bounds.min.y, bounds.max.y })
        foreach (float z in new[] { bounds.min.z, bounds.max.z })
        {
            Vector3 p = camera.WorldToViewportPoint(new Vector3(x, y, z));
            if (p.x < -.0001f || p.x > 1.0001f || p.y < -.0001f || p.y > 1.0001f
                || p.z <= camera.nearClipPlane || p.z >= camera.farClipPlane) return false;
        }
        return true;
    }

    static int SceneSubscriptions(TurnCameraController controller)
    {
        var sceneEvent = typeof(SceneManager).GetField("activeSceneChanged", BindingFlags.Static | BindingFlags.NonPublic);
        if (sceneEvent == null) throw new Exception("Cannot inspect SceneManager subscriber retention");
        var listeners = sceneEvent.GetValue(null) as Delegate;
        int count = 0;
        if (listeners != null) foreach (var listener in listeners.GetInvocationList())
            if (ReferenceEquals(listener.Target, controller)) count++;
        return count;
    }

    static void ActualInput(TurnInputHandler input, TurnCameraController controller, TurnGenerator turn)
    {
        var settings = InputSystem.settings;
        var previousBackground = settings.backgroundBehavior;
        var previousEditorRouting = settings.editorInputBehaviorInPlayMode;
        bool previousRunInBackground = Application.runInBackground;
        // SceneSmoke runs from EditorApplication.update in a headless editor without a focused Game View.
        // The package otherwise routes synthetic events into editor state buffers, not GameAction.
        settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
        settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        Application.runInBackground = true;
        var keyboard = InputSystem.AddDevice<Keyboard>();
        InputSystem.EnableDevice(keyboard);
        keyboard.MakeCurrent();
        var turnEnd = turn.GameAction.GamePlay.TurnEnd;
        bool wasEnabled = turnEnd.enabled;
        int binding = turnEnd.bindings.Count;
        turnEnd.Disable(); turnEnd.AddBinding("<Keyboard>/space"); turnEnd.Enable();
        try
        {
            Send(input, keyboard, Key.Space);
            Check("synthetic keyboard reaches the gameplay action", keyboard.enabled && keyboard.spaceKey.isPressed
                && keyboard.spaceKey.wasPressedThisFrame && turnEnd.WasPressedThisFrame());
            Check("actual Space binding works without Ctrl", !controller.IsTopDown
                && !input.SpaceConsumedThisFrame && turn.Context.TurnEndDown);
            Send(input, keyboard);
            Send(input, keyboard, Key.LeftCtrl, Key.Space);
            Check("actual left Ctrl chord consumes Space action", controller.IsTopDown
                && input.SpaceConsumedThisFrame && !turn.Context.TurnEndDown);
            Send(input, keyboard, Key.LeftCtrl, Key.Space);
            Check("actual held chord does not repeat", controller.IsTopDown && !input.SpaceConsumedThisFrame);
            Send(input, keyboard);
            Send(input, keyboard, Key.RightCtrl, Key.Space);
            Check("actual right Ctrl chord restores camera", !controller.IsTopDown && input.SpaceConsumedThisFrame);
            Send(input, keyboard);
            Send(input, keyboard, Key.F7);
            Check("actual F7 alone never creates diary", !AIDiaryUI.IsOpen);
            Send(input, keyboard);
            Send(input, keyboard, Key.LeftShift, Key.F7);
            Check("actual left Shift chord opens diary", AIDiaryUI.IsOpen);
            Send(input, keyboard, Key.LeftShift, Key.F7);
            Check("actual held diary chord does not repeat", AIDiaryUI.IsOpen);
            Send(input, keyboard);
            Send(input, keyboard, Key.RightShift, Key.F7);
            Check("actual right Shift chord closes diary", !AIDiaryUI.IsOpen);
        }
        finally
        {
            Send(input, keyboard);
            turnEnd.Disable(); turnEnd.ChangeBinding(binding).Erase();
            if (wasEnabled) turnEnd.Enable();
            InputSystem.RemoveDevice(keyboard);
            if (controller.IsTopDown) controller.ToggleTopDown();
            AIDiaryUI.Close();
            settings.backgroundBehavior = previousBackground;
            settings.editorInputBehaviorInPlayMode = previousEditorRouting;
            Application.runInBackground = previousRunInBackground;
        }
    }

    static void Send(TurnInputHandler input, Keyboard keyboard, params Key[] keys)
    {
        InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
        InputSystem.Update();
        input.Tick();
    }

    static void CameraReplacementAndScene(TurnCameraController controller, Camera camera, TurnGenerator turn)
    {
        var previousScene = SceneManager.GetActiveScene();
        var nextScene = SceneManager.CreateScene("TopDownCameraRegression_" + Guid.NewGuid().ToString("N"));
        var newObject = new GameObject("Next scene camera");
        SceneManager.MoveGameObjectToScene(newObject, nextScene);
        var nextCamera = newObject.AddComponent<Camera>();
        nextCamera.transform.SetPositionAndRotation(new Vector3(11, 28, -5), Quaternion.Euler(54, 7, 2));
        var next = CameraStateSnapshot.Capture(nextCamera, true, false, default);
        var before = CameraStateSnapshot.Capture(camera, controller.enabled, false, default);
        try
        {
            controller.ToggleTopDown();
            SceneManager.SetActiveScene(nextScene);
            Check("active scene change invalidates top-down snapshot", !controller.IsTopDown);
            turn.Context.CameraObject = nextCamera.transform;
            controller.ToggleTopDown(); controller.ToggleTopDown();
            Check("new scene restores its own camera, never the previous camera", nextCamera.transform.position == next.Position
                && Quaternion.Angle(nextCamera.transform.rotation, next.Rotation) < .01f);
            Check("snapshot refuses a different camera", !before.Restore(nextCamera));
        }
        finally
        {
            SceneManager.SetActiveScene(previousScene);
            turn.Context.CameraObject = camera.transform;
            controller.InvalidateSnapshot(); before.Restore(camera);
            UnityEngine.Object.DestroyImmediate(newObject);
            SceneManager.UnloadSceneAsync(nextScene);
        }
    }

    static void MissingCamera(TurnCameraController controller, TurnGenerator turn)
    {
        var transform = turn.Context.CameraObject;
        bool warned = (bool)Field("missingCameraWarned").GetValue(controller);
        int warnings = 0;
        Application.LogCallback listener = (text, stack, type) =>
        { if (type == LogType.Warning && text.StartsWith("[Camera]")) warnings++; };
        try
        {
            turn.Context.CameraObject = null;
            Field("missingCameraWarned").SetValue(controller, false);
            Application.logMessageReceived += listener;
            for (int i = 0; i < 10; i++) controller.ToggleTopDown();
            Check("missing camera warns once without crashing", warnings == 1 && !controller.IsTopDown);
        }
        finally
        {
            Application.logMessageReceived -= listener;
            Field("missingCameraWarned").SetValue(controller, warned);
            turn.Context.CameraObject = transform;
            controller.InvalidateSnapshot();
        }
    }
}
#endif
