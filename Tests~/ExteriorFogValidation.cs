#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

[InitializeOnLoad]
public static class ExteriorFogValidation
{
    const string Running = "FK.ExteriorFogValidation";
    static int frames, checks;
    static bool clicked;
    static ExteriorFogValidation() { EditorApplication.update += Tick; }
    public static void Run()
    {
        if (!Application.dataPath.Contains("UnityValidation")) throw new InvalidOperationException("Isolated clone only");
        PlayerSettings.companyName = "CodexValidation"; PlayerSettings.productName = "ExteriorFogValidation";
        SessionState.SetBool(Running, true);
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity"); EditorApplication.EnterPlaymode();
    }
    static void Check(bool value, string message)
    { if (!value) throw new Exception("[ExteriorFog] " + message); checks++; }
    static void Tick()
    {
        if (!SessionState.GetBool(Running, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
        try
        {
            if (!clicked)
            {
                if (UIBuilder.ScreenCanvas == null || TitleScreenUI.Instance == null) return;
                var button = UIBuilder.ScreenCanvas.transform.Find("TitleScreenOverlay/ButtonArea/はじめからBtn")?.GetComponent<Button>();
                if (button == null) throw new InvalidOperationException("Actual title button missing");
                UnityEngine.Random.InitState(56192); button.onClick.Invoke(); clicked = true; return;
            }
            var turn = UnityEngine.Object.FindFirstObjectByType<TurnGenerator>();
            if (turn == null || !(turn.CurrentState is PlayerMove) || ++frames < 20) return;
            var map = turn.Systems.MapCreate; var fog = map.FogChunks; var mesh = fog.ExteriorMesh;
            Check(mesh != null && mesh.vertexCount == 16 && mesh.triangles.Length == 24, "one exterior mesh with only eight triangles");
            var renderer = Array.Find(fog.GetComponentsInChildren<MeshFilter>(), f => f.sharedMesh == mesh).GetComponent<MeshRenderer>();
            Check(renderer.enabled && renderer.sharedMaterial == Resources.Load<Material>("Terrain/UnknownMist"), "existing fog material is shared");
            Check(renderer.GetComponent<Collider>() == null, "exterior never intercepts unit clicks");
            Check(renderer.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off && !renderer.receiveShadows, "no exterior shadows");
            var vertices = mesh.vertices; var indices = mesh.triangles;
            for (int i = 0; i < indices.Length; i += 3)
            {
                var center = (vertices[indices[i]] + vertices[indices[i + 1]] + vertices[indices[i + 2]]) / 3f;
                Check(center.y < map.minY && (center.x <= -.5f || center.x >= map.maxX - .5f || center.z <= -.5f || center.z >= map.maxZ - .5f), "exterior stays below and outside playable terrain");
            }
            int id = mesh.GetInstanceID(); var visible = new HashSet<Vector3Int>();
            for (int x = 0; x < map.maxX; x++) for (int z = 0; z < map.maxZ; z++) visible.Add(new Vector3Int(x, 0, z));
            fog.Refresh(visible, visible);
            Check(renderer.enabled && fog.ExteriorMesh.GetInstanceID() == id, "revealing every map cell preserves the exterior without rebuilding it");
            fog.Refresh(visible, visible);
            Check(fog.LastRebuiltChunks == 0 && fog.ExteriorMesh.GetInstanceID() == id, "unchanged visibility performs no mesh rebuild");
            turn.Systems.VisionGenerator.MarkVisionDirty(); turn.Systems.RefreshVision();
            var camera = Camera.main; var centerMap = new Vector3((map.maxX - 1) * .5f, 0, (map.maxZ - 1) * .5f);
            camera.orthographic = false; camera.fieldOfView = 60;
            camera.transform.position = centerMap + new Vector3(0, 36, -32); camera.transform.LookAt(centerMap);
            Capture("exterior-fog-perspective.png");
            camera.fieldOfView = 65; camera.transform.position = centerMap + new Vector3(0, 16, -34); camera.transform.LookAt(centerMap);
            Capture("exterior-fog-side.png");
            Debug.Log("[ExteriorFog] ALL PASSED checks=" + checks + " addedRenderers=1 triangles=8 map=" + map.maxX + "x" + map.maxZ);
            SessionState.SetBool(Running, false); EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); SessionState.SetBool(Running, false); EditorApplication.Exit(1); }
    }
    static void Capture(string name)
    {
        var camera = Camera.main;
        var canvases = new List<(Canvas canvas, RenderMode mode, Camera camera, float plane)>();
        try
        {
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
                { canvases.Add((canvas, canvas.renderMode, canvas.worldCamera, canvas.planeDistance)); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; }
            Canvas.ForceUpdateCanvases(); VisualRegressionTests.CaptureHUD(name);
        }
        finally { foreach (var item in canvases) { item.canvas.renderMode = item.mode; item.canvas.worldCamera = item.camera; item.canvas.planeDistance = item.plane; } }
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../exterior-fog-20261017")); Directory.CreateDirectory(folder);
        File.Copy(Path.GetFullPath(Path.Combine(Application.dataPath, "../../visual-captures", name)), Path.Combine(folder, name), true);
    }
}
#endif
