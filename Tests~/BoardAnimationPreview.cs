#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Samples the production cosmetic animation in a disposable, observed Play Mode fixture.
/// It does not execute combat, consume AP or change the real board's actors/camera/materials.
/// The internal production Tick is sampled directly, as in BoardActorAnimationTests; no pose is recreated here.
/// </summary>
public static class BoardAnimationPreview
{
    const int PreviewLayer = 30, Width = 960, Height = 420, FramesPerPhase = 20, FPS = 30;
    const string Output = "Docs/PlaytestScreenshots/board-animation-event-only-20261018";
    static readonly string[] Phases = { "Idle", "Move", "Attack", "Hit" };
    static readonly MethodInfo Sample = typeof(BoardActorAnimation).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);

    [Serializable]
    sealed class Recording
    {
        public string source = "BoardActorAnimation production runtime; cosmetic notification preview, not a battle replay";
        public int width = Width, height = Height, framesPerSecond = FPS, framesPerPhase = FramesPerPhase, frames = FramesPerPhase * 4;
        public string[] phases = Phases;
        public string[] actors = { "Player Knight / existing white marble", "Enemy Knight / existing black marble", "Wild Dragon / existing red marble" };
    }

    public static void Capture(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.VisionGenerator == null || systems.UnitSetting == null || Sample == null)
            throw new InvalidOperationException("Board animation preview requires the actual started Play Mode scene.");
        if (!new DirectoryInfo(Application.dataPath).Parent.Name.Equals("UnityValidation", StringComparison.Ordinal))
            throw new InvalidOperationException("Animation capture runs only in the isolated UnityValidation project.");

        int playerAP = systems.FactionState.GetAP(Team.Player), enemyAP = systems.FactionState.GetAP(Team.Enemy);
        var random = UnityEngine.Random.state;
        int boundBefore = BoardActorAnimation.BoundCount;
        var owned = new List<GameObject>(); var actors = new List<Status>();
        var oldTarget = RenderTexture.active;
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        Material floorMaterial = null; Camera camera = null;
        try
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Output);
            Directory.CreateDirectory(Path.Combine(folder, "frames"));
            var cells = ChooseCells(systems.VisionGenerator);
            var parent = Own(new GameObject("Disposable animation preview"), owned);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(R1UnitVisualInstaller.PrefabPath(Kind.Knight));
            if (source == null) throw new InvalidOperationException("Production Knight prefab is missing.");
            for (int i = 0; i < 3; i++)
            {
                var obj = systems.UnitSetting.SpawnUnit(source, cells[i], parent.transform, initialKind: Kind.Knight,
                    initialTeam: i == 0 ? Team.Player : Team.Enemy, authoredData: systems.UnitSetting.UnitDataMap[Kind.Knight], runtimeSpawn: true);
                if (obj == null) throw new InvalidOperationException("Could not create production animation fixture.");
                var actor = obj.GetComponentInChildren<Status>(); actors.Add(actor);
                if (i == 2)
                {
                    actor.isWildBoss = true; actor.team = Team.Obstacle; actor.wildBossArchetype = WildBossArchetype.Dragon;
                    if (!BoardActorPresentation.Apply(actor)) throw new InvalidOperationException("Production Dragon presentation is missing.");
                }
                SetLayer(obj.transform, PreviewLayer);
                if (BoardActorPresentation.VisualRoot(actor) == null) throw new InvalidOperationException("Production visual container is missing.");
                BoardActorAnimation.SetVisible(actor, true);
            }

            Vector3 center = (cells[0] + cells[1] + cells[2]) / 3;
            var cameraObject = Own(new GameObject("Animation recording camera"), owned); camera = cameraObject.AddComponent<Camera>();
            camera.cullingMask = 1 << PreviewLayer; camera.enabled = false; camera.orthographic = true;
            camera.nearClipPlane = .01f; camera.farClipPlane = 50; camera.allowHDR = false;
            camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(24, 29, 38, 255);
            camera.targetTexture = target;
            camera.transform.position = center + new Vector3(1.6f, 4.2f, -6.5f);
            camera.transform.LookAt(center + Vector3.up * .25f);
            float requiredHalfHeight = 0;
            foreach (var cell in cells)
            {
                var local = camera.transform.InverseTransformPoint(cell + Vector3.up * .55f);
                requiredHalfHeight = Mathf.Max(requiredHalfHeight, Mathf.Abs(local.y) + 1.2f, (Mathf.Abs(local.x) + 1.1f) * Height / Width);
            }
            camera.orthographicSize = Mathf.Max(1.75f, requiredHalfHeight);

            var shader = Shader.Find("Unlit/Color");
            if (shader == null) throw new InvalidOperationException("Preview floor shader unavailable.");
            floorMaterial = new Material(shader) { color = new Color32(41, 46, 55, 255), hideFlags = HideFlags.HideAndDontSave };
            for (int i = 0; i < cells.Length; i++)
            {
                var floor = Own(GameObject.CreatePrimitive(PrimitiveType.Cube), owned); floor.name = "Preview floor"; floor.layer = PreviewLayer;
                UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
                // Board actor coordinates sit half a cell above the standing surface.
                floor.transform.position = cells[i] - Vector3.up * .53f; floor.transform.localScale = new Vector3(1.75f, .06f, 1.75f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            }
            AddLight("Recording key", new Color(1, .92f, .81f), 1.3f, new Vector3(42, -25, 0), owned);
            AddLight("Recording rim", new Color(.55f, .75f, 1), .65f, new Vector3(24, 145, 0), owned);

            int frame = 0;
            var idlePositions = new Vector3[actors.Count];
            var idleRotations = new Quaternion[actors.Count];
            var idleScales = new Vector3[actors.Count];
            for (int i = 0; i < actors.Count; i++)
            {
                var visual = BoardActorPresentation.VisualRoot(actors[i]);
                idlePositions[i] = visual.localPosition; idleRotations[i] = visual.localRotation; idleScales[i] = visual.localScale;
            }
            for (int phase = 0; phase < Phases.Length; phase++)
            {
                // Every event begins at the production unscaled clock. Only that implementation calculates its visual pose.
                double now = Time.unscaledTimeAsDouble;
                foreach (var actor in actors)
                {
                    BoardActorAnimation.SetVisible(actor, false); BoardActorAnimation.SetVisible(actor, true);
                    if (phase == 1)
                    {
                        var destination = actor.transform.position;
                        Vector3 from = ObservedNeighbour(systems.VisionGenerator, destination);
                        actor.transform.position = from;
                        actor.transform.position = destination; // Board authority moves first, exactly as the runtime action executor.
                        BoardActorAnimation.NotifyMove(actor, from);
                    }
                    else if (phase == 2) BoardActorAnimation.NotifyAttack(actor, ObservedNeighbour(systems.VisionGenerator, actor.transform.position));
                    else if (phase == 3) BoardActorAnimation.NotifyHit(actor);
                }
                for (int sample = 0; sample < FramesPerPhase; sample++, frame++)
                {
                    Sample.Invoke(null, new object[] { now + sample / (double)FPS });
                    for (int i = 0; i < actors.Count; i++)
                    {
                        if (actors[i].transform.position != cells[i]) throw new InvalidOperationException("Animation changed the logical position.");
                        var visual = BoardActorPresentation.VisualRoot(actors[i]);
                        if (phase == 0 && (visual.localPosition != idlePositions[i]
                            || Quaternion.Angle(visual.localRotation, idleRotations[i]) > .001f || visual.localScale != idleScales[i]))
                            throw new InvalidOperationException("Idle preview is not exactly stationary.");
                    }
                    camera.Render(); RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply(false);
                    File.WriteAllBytes(Path.Combine(folder, "frames", "frame_" + frame.ToString("D3") + ".png"), texture.EncodeToPNG());
                }
            }
            Sample.Invoke(null, new object[] { Time.unscaledTimeAsDouble + 1 });
            if (BoardActorAnimation.ActiveDeathCount != 0 || BoardActorAnimation.DeathPoolCount != 0)
                throw new InvalidOperationException("Event-only animation must not allocate detached death meshes.");
            if (systems.FactionState.GetAP(Team.Player) != playerAP || systems.FactionState.GetAP(Team.Enemy) != enemyAP)
                throw new InvalidOperationException("Cosmetic preview changed faction AP.");
            if (!UnityEngine.Random.state.Equals(random)) throw new InvalidOperationException("Cosmetic preview consumed gameplay Random.");
            File.WriteAllText(Path.Combine(folder, "recording.json"), JsonUtility.ToJson(new Recording(), true));
            Debug.Log("[BoardAnimationPreview] ALL PASSED frames=" + frame + " fps=" + FPS + " output=" + folder);
        }
        finally
        {
            foreach (var actor in actors) if (actor != null) BoardActorAnimation.Unbind(actor);
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            RenderTexture.active = oldTarget;
            if (camera != null) camera.targetTexture = null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(texture);
            if (floorMaterial != null) UnityEngine.Object.DestroyImmediate(floorMaterial);
            UnityEngine.Random.state = random;
        }
        if (BoardActorAnimation.BoundCount != boundBefore) throw new InvalidOperationException("Animation preview retained fixture bindings.");
    }

    static GameObject Own(GameObject obj, List<GameObject> owned) { owned.Add(obj); return obj; }
    static void SetLayer(Transform root, int layer)
    { root.gameObject.layer = layer; for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer); }
    static void AddLight(string name, Color color, float intensity, Vector3 angles, List<GameObject> owned)
    {
        var obj = Own(new GameObject(name), owned); obj.layer = PreviewLayer; var light = obj.AddComponent<Light>();
        light.type = LightType.Directional; light.color = color; light.intensity = intensity; light.cullingMask = 1 << PreviewLayer;
        light.shadows = LightShadows.None; obj.transform.rotation = Quaternion.Euler(angles);
    }
    static Vector3[] ChooseCells(VisionGenerator vision)
    {
        var cells = new Dictionary<Vector2Int, Vector3>();
        foreach (var cell in vision.PlayerVisionBox)
            if (vision.IsPresentationCellVisible(cell)) cells[new Vector2Int(Mathf.RoundToInt(cell.x), Mathf.RoundToInt(cell.z))] = cell;
        foreach (var pair in cells)
            if (cells.TryGetValue(pair.Key + Vector2Int.left * 2, out var left)
                && cells.TryGetValue(pair.Key + Vector2Int.right * 2, out var right)
                && Mathf.Abs(left.y - pair.Value.y) < .01f && Mathf.Abs(right.y - pair.Value.y) < .01f)
                return new[] { left, pair.Value, right };
        throw new InvalidOperationException("Preview requires three equally elevated observed terrain cells in a five-cell-wide row.");
    }
    static Vector3 ObservedNeighbour(VisionGenerator vision, Vector3 destination)
    {
        foreach (var direction in new[] { Vector3.back, Vector3.forward, Vector3.left, Vector3.right })
            if (vision.IsPresentationCellVisible(destination + direction)) return destination + direction;
        return destination; // The production arrival dip safely handles a wholly unobserved incoming route.
    }
}
#endif
