#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Isolated-clone rendered evidence. Debug views expose geometry only; they never change vision or environment state.</summary>
[InitializeOnLoad]
public static class EnvironmentStartupVisualSmoke
{
    const string Running = "FK.EnvironmentStartupVisualSmoke.Running";
    const int Width = 1600, Height = 900, Seed = 56192;
    static bool clicked;
    static int clickedFrame;
    static double started;
    static UnityEngine.Random.State randomBefore;
    static bool ownsRandom;
    static readonly Vector2Int[] Neighbours = { Vector2Int.right, Vector2Int.up, Vector2Int.left, Vector2Int.down };

    static EnvironmentStartupVisualSmoke() { EditorApplication.update += Tick; }

    // Copy this optional fixture into an Editor folder in an isolated UnityValidation clone, then call Run without -nographics.
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated UnityValidation clone only");
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new InvalidOperationException("Environment screenshots require a graphics device; omit -nographics.");
        clicked = false; clickedFrame = 0; started = 0; ownsRandom = false;
        SessionState.SetBool(Running, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (started == 0) started = EditorApplication.timeSinceStartup;
            if (EditorApplication.timeSinceStartup - started > 120) throw new TimeoutException("Environment screenshot startup exceeded two minutes");
            if (!clicked)
            {
                if (UIBuilder.ScreenCanvas == null || TitleScreenUI.Instance == null) return;
                var button = UIBuilder.ScreenCanvas.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null || !button.interactable) throw new InvalidOperationException("Actual title New Game button is unavailable");
                randomBefore = UnityEngine.Random.state; ownsRandom = true;
                UnityEngine.Random.InitState(Seed); button.onClick.Invoke(); clicked = true; clickedFrame = Time.frameCount;
                return;
            }
            var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (turn == null || !(turn.CurrentState is PlayerMove) || Time.frameCount - clickedFrame < 20) return;
            if (turn.Systems.MapCreate?.Environment == null || !turn.Systems.MapCreate.Environment.GeneratedAtStart
                || turn.Systems.MapCreate.Environment.Count == 0)
                throw new InvalidOperationException("Actual title New Game did not generate startup environments");
            CaptureAll(turn.Systems);
            Finish(0);
        }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }

    static void Finish(int exitCode)
    {
        if (ownsRandom) { UnityEngine.Random.state = randomBefore; ownsRandom = false; }
        SessionState.SetBool(Running, false);
        EditorApplication.Exit(exitCode);
    }

    static string OutputFolder()
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../environment-startup-20261018"));
        Directory.CreateDirectory(folder); return folder;
    }

    sealed class CanvasState
    {
        public Canvas Canvas; public bool Enabled; public RenderMode Mode; public Camera Camera; public float Plane;
        public CanvasState(Canvas canvas)
        { Canvas = canvas; Enabled = canvas.enabled; Mode = canvas.renderMode; Camera = canvas.worldCamera; Plane = canvas.planeDistance; }
        public void Restore()
        {
            if (Canvas == null) return;
            Canvas.renderMode = Mode; Canvas.worldCamera = Camera; Canvas.planeDistance = Plane; Canvas.enabled = Enabled;
        }
    }
    sealed class RendererState
    {
        public Renderer Renderer; public bool Enabled; public Material[] Materials;
        public RendererState(Renderer renderer) { Renderer = renderer; Enabled = renderer.enabled; Materials = renderer.sharedMaterials; }
        public void Restore()
        {
            if (Renderer == null) return;
            Renderer.enabled = Enabled;
            // No material is changed for a capture. Keep the original shared references even if a rendering callback intervenes.
            if (!Renderer.sharedMaterials.SequenceEqual(Materials)) Renderer.sharedMaterials = Materials;
        }
    }
    sealed class ObjectState
    {
        public GameObject Object; public bool Active;
        public ObjectState(GameObject value) { Object = value; Active = value.activeSelf; }
        public void Restore() { if (Object != null && Object.activeSelf != Active) Object.SetActive(Active); }
    }
    sealed class CameraState
    {
        readonly Camera camera; readonly Vector3 position; readonly Quaternion rotation;
        readonly bool orthographic; readonly float size, fov, near, far, aspect;
        readonly RenderTexture target; readonly Rect rect; readonly CameraClearFlags clear; readonly Color background;
        readonly Matrix4x4 projection;
        public CameraState(Camera value)
        {
            camera = value; position = value.transform.position; rotation = value.transform.rotation;
            orthographic = value.orthographic; size = value.orthographicSize; fov = value.fieldOfView;
            near = value.nearClipPlane; far = value.farClipPlane; aspect = value.aspect;
            target = value.targetTexture; rect = value.rect; clear = value.clearFlags; background = value.backgroundColor;
            projection = value.projectionMatrix;
        }
        public void Restore()
        {
            if (camera == null) return;
            camera.transform.SetPositionAndRotation(position, rotation);
            camera.targetTexture = target; camera.rect = rect; camera.aspect = aspect;
            camera.orthographic = orthographic; camera.orthographicSize = size; camera.fieldOfView = fov;
            camera.nearClipPlane = near; camera.farClipPlane = far; camera.clearFlags = clear; camera.backgroundColor = background;
            camera.projectionMatrix = projection;
        }
    }

    static void CaptureAll(GameSystems systems)
    {
        var camera = Camera.main;
        if (camera == null) throw new InvalidOperationException("Actual main camera is unavailable");
        var map = systems.MapCreate;
        var environmentRoot = map.transform.Find("EnvironmentObjects");
        if (environmentRoot == null) throw new InvalidOperationException("Generated environment visual root is missing");
        if (map.FogChunks == null) throw new InvalidOperationException("Production fog renderer is unavailable");
        string environmentBefore = JsonUtility.ToJson(map.Environment.Capture());
        var cameraState = new CameraState(camera);
        var oldActive = RenderTexture.active;
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(value => new CanvasState(value)).ToArray();
        var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Select(value => new RendererState(value)).ToArray();
        var fog = map.FogChunks.GetComponentsInChildren<Renderer>(true);
        var objects = new List<ObjectState>();
        foreach (Transform child in environmentRoot) objects.Add(new ObjectState(child.gameObject));
        var cameraController = systems.TurnGenerator.GetComponent<TurnCameraController>();
        bool controllerEnabled = cameraController != null && cameraController.enabled;
        RenderTexture target = null; Texture2D readback = null;
        try
        {
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "EnvironmentCapture_Temporary", antiAliasing = 1 };
            target.Create(); readback = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            // Render real overlay HUD through the real camera into this review target, then restore its original mode.
            foreach (var entry in canvases)
            {
                if (entry.Canvas == null || !entry.Canvas.isRootCanvas || entry.Mode != RenderMode.ScreenSpaceOverlay) continue;
                entry.Canvas.renderMode = RenderMode.ScreenSpaceCamera; entry.Canvas.worldCamera = camera;
                entry.Canvas.planeDistance = camera.nearClipPlane + .1f;
            }
            Canvas.ForceUpdateCanvases();
            Capture(camera, target, readback, "environment-startup.png");
            Debug.Log("[EnvironmentStartupVisual] Production fog/HUD retained; captured after " + (Time.frameCount - clickedFrame) + " actual frames");

            // The following three images are debug fixtures, not the player's visibility. No gameplay discovery API is invoked.
            if (cameraController != null) cameraController.enabled = false;
            foreach (var entry in canvases) if (entry.Canvas != null) entry.Canvas.enabled = false;
            Canvas.ForceUpdateCanvases();
            void ShowDebugGeometry()
            {
                foreach (var renderer in fog) if (renderer != null) renderer.enabled = false;
                foreach (var entry in objects) if (entry.Object != null) entry.Object.SetActive(true);
            }
            camera.rect = new Rect(0, 0, 1, 1); camera.aspect = (float)Width / Height;
            camera.orthographic = true; camera.nearClipPlane = .1f; camera.farClipPlane = 250;
            var overview = new Bounds(new Vector3((map.maxX - 1) * .5f, (map.minY + map.maxY) * .5f, (map.maxZ - 1) * .5f),
                new Vector3(map.maxX + 1f, map.maxY - map.minY + 1f, map.maxZ + 1f));
            Frame(camera, overview, new Vector3(0, 1, -.72f), 1.06f);
            ShowDebugGeometry(); Capture(camera, target, readback, "overview-debug.png");
            var forest = FindCluster(map, TerrainEnvironmentKind.Forest);
            Frame(camera, ClusterBounds(map, forest), new Vector3(1.2f, 1.7f, -1.6f), 1.3f);
            ShowDebugGeometry(); Capture(camera, target, readback, "forest-closeup-debug.png");
            var bush = FindCluster(map, TerrainEnvironmentKind.Bush);
            Frame(camera, ClusterBounds(map, bush), new Vector3(1.2f, 1.7f, -1.6f), 1.3f);
            ShowDebugGeometry(); Capture(camera, target, readback, "bush-closeup-debug.png");
            WriteReadme(map, forest, bush);
        }
        finally
        {
            foreach (var entry in objects) entry.Restore();
            foreach (var entry in renderers) entry.Restore();
            cameraState.Restore();
            foreach (var entry in canvases) entry.Restore();
            if (cameraController != null) cameraController.enabled = controllerEnabled;
            RenderTexture.active = oldActive;
            Canvas.ForceUpdateCanvases();
            if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
            if (readback != null) UnityEngine.Object.DestroyImmediate(readback);
        }
        if (environmentBefore != JsonUtility.ToJson(map.Environment.Capture()))
            throw new InvalidOperationException("Screenshot fixture changed gameplay environment state");
        if (objects.Any(entry => entry.Object != null && entry.Object.activeSelf != entry.Active)
            || renderers.Any(entry => entry.Renderer != null && (entry.Renderer.enabled != entry.Enabled || !entry.Renderer.sharedMaterials.SequenceEqual(entry.Materials))))
            throw new InvalidOperationException("Screenshot fixture did not restore visibility/material references");
        Debug.Log("[EnvironmentStartupVisual] ALL CAPTURED; camera/canvas/fog/environment/materials restored; " + OutputFolder());
    }

    static void Capture(Camera camera, RenderTexture target, Texture2D readback, string filename)
    {
        camera.Render(); RenderTexture.active = target;
        readback.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); readback.Apply();
        File.WriteAllBytes(Path.Combine(OutputFolder(), filename), readback.EncodeToPNG());
        Debug.Log("[EnvironmentStartupVisual] CAPTURED " + filename);
    }
    static void Frame(Camera camera, Bounds bounds, Vector3 offsetDirection, float padding)
    {
        camera.transform.position = bounds.center + offsetDirection.normalized * 100;
        camera.transform.LookAt(bounds.center);
        float halfWidth = 0, halfHeight = 0;
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
        {
            var offset = Vector3.Scale(bounds.extents, new Vector3(x, y, z));
            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(offset, camera.transform.right)));
            halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(offset, camera.transform.up)));
        }
        camera.orthographicSize = Mathf.Max(.7f, Mathf.Max(halfHeight, halfWidth / camera.aspect) * padding);
        camera.ResetProjectionMatrix();
    }
    static List<Vector2Int> FindCluster(MapCreate map, TerrainEnvironmentKind kind)
    {
        var cells = new HashSet<Vector2Int>(map.Environment.Capture().cells.Where(cell => !cell.burned
            && map.Environment.DefinitionAt(new Vector3(cell.cell.x, 0, cell.cell.y))?.kind == kind).Select(cell => cell.cell));
        var visited = new HashSet<Vector2Int>();
        foreach (var origin in cells.OrderBy(cell => cell.x).ThenBy(cell => cell.y))
        {
            if (!visited.Add(origin)) continue;
            var cluster = new List<Vector2Int>(); var pending = new Queue<Vector2Int>(); pending.Enqueue(origin);
            while (pending.Count != 0)
            {
                var current = pending.Dequeue(); cluster.Add(current);
                foreach (var direction in Neighbours)
                {
                    var next = current + direction;
                    if (cells.Contains(next) && visited.Add(next)) pending.Enqueue(next);
                }
            }
            if (cluster.Count == 3 || cluster.Count == 4) return cluster;
        }
        throw new InvalidOperationException("No naturally generated connected 3–4-cell " + kind + " cluster was found");
    }
    static Bounds ClusterBounds(MapCreate map, List<Vector2Int> cluster)
    {
        var bounds = new Bounds(); bool first = true;
        foreach (var cell in cluster)
        {
            if (!map.TryGetHeight(cell.x, cell.y, out float y)) throw new InvalidOperationException("Cluster cell has no terrain height");
            var tile = new Bounds(new Vector3(cell.x, y - .08f, cell.y), new Vector3(1.12f, 1.1f, 1.12f));
            if (first) { bounds = tile; first = false; } else bounds.Encapsulate(tile);
        }
        return bounds;
    }
    static void WriteReadme(MapCreate map, List<Vector2Int> forest, List<Vector2Int> bush)
    {
        var notes = new StringBuilder();
        notes.AppendLine("# 環境・新規ゲーム開始時の画面検証");
        notes.AppendLine();
        notes.AppendLine("実SampleScene、実タイトルの「はじめから」ボタン、本番環境カタログを使用。開始から20実フレーム以上待機し、実カメラで1600×900 PNGを取得しました。素材や生成・戦闘ルールは変更していません。");
        notes.AppendLine();
        notes.AppendLine("- environment-startup.png: 通常プレイのカメラ位置・投影と実HUD。本番Fogと環境の表示状態を保持しています。HUDをRenderTextureへ含めるため、取得中だけOverlay Canvasを同じカメラへ接続し、その後元に戻しています。");
        notes.AppendLine("- overview-debug.png: 全盤面の生成配置を調べるためのdebug fixture。通常プレイの視界ではありません。");
        notes.AppendLine("- forest-closeup-debug.png / bush-closeup-debug.png: 開幕に自然生成された連結3〜4マスの森・草むらを拡大したdebug fixture。通常プレイの視界ではありません。");
        notes.AppendLine();
        notes.AppendLine("debug画像はFog（盤外Fogを含む）のRendererだけを一時停止し、生成済み環境Prefabのrootを一時表示、HUDを一時非表示にしています。未発見の間欠泉等も見えます。探索・発見・視界データ・セーブ状態を変更していません。取得後はカメラ・Canvas・Fog Renderer・環境activeSelf・共有材質参照を復元し、RenderTexture/Texture2Dを解放しています。");
        notes.AppendLine();
        notes.AppendLine("seed=" + Seed + "; environmentSeed=" + map.Environment.Capture().generationSeed + "; map=" + map.maxX + "×" + map.maxZ + "; environmentCells=" + map.Environment.Count);
        notes.AppendLine("森 " + forest.Count + "マス: " + string.Join(", ", forest.Select(cell => "(" + cell.x + "," + cell.y + ")")));
        notes.AppendLine("草むら " + bush.Count + "マス: " + string.Join(", ", bush.Select(cell => "(" + cell.x + "," + cell.y + ")")));
        File.WriteAllText(Path.Combine(OutputFolder(), "README.md"), notes.ToString());
    }
}
#endif
