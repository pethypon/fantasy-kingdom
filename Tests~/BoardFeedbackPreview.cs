#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Captures the real production event facade/effect callbacks on disposable observed board fixtures.
/// This is an effect demonstration, not a simulated battle, damage transaction or performance benchmark.
/// </summary>
public static class BoardFeedbackPreview
{
    const int Layer = 30, Width = 960, Height = 420, FPS = 30, FramesPerPhase = 18;
    const string Output = "Docs/PlaytestScreenshots/board-feedback-20261019";
    static readonly string[] Phases = { "Melee", "Projectile", "Magic", "Heal", "Summon", "Build" };
    static readonly MethodInfo EffectTick = typeof(BoardCombatEffects).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly MethodInfo BodyTick = typeof(BoardActorAnimation).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);
    [Serializable] sealed class Recording
    {
        public string source = "Actual BoardActorAnimation.NotifyAttack -> BoardActionFeedback and BoardCombatEffects runtime; cosmetic demonstration, not a battle replay.";
        public int width = Width, height = Height, framesPerSecond = FPS, framesPerPhase = FramesPerPhase, frames = FramesPerPhase * 6;
        public string[] phases = Phases;
        public string audio = "Production sound requests are issued by each facade event. PNG/GIF frames do not contain audio.";
    }
    sealed class Fixture
    {
        public Status Actor;
        public GameObject Visual;
    }
    public static void Capture(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.VisionGenerator == null || systems.FactionState == null || EffectTick == null || BodyTick == null)
            throw new InvalidOperationException("Feedback capture requires the actual started Play Mode scene.");
        if (new DirectoryInfo(Application.dataPath).Parent.Name != "UnityValidation")
            throw new InvalidOperationException("Capture only in the isolated UnityValidation project.");
        var catalog = BoardVisualThemeCatalog.Active;
        if (catalog == null || BoardFeedbackSettings.Active == null) throw new InvalidOperationException("Production art/feedback settings are missing.");
        var random = UnityEngine.Random.state;
        int playerAP = systems.FactionState.GetAP(Team.Player), enemyAP = systems.FactionState.GetAP(Team.Enemy);
        int boundBefore = BoardActorAnimation.BoundCount;
        var owned = new List<GameObject>(); var actors = new List<Fixture>();
        var oldEffectLayers = new Dictionary<Transform, int>();
        var previousEffectDriver = UnityEngine.Object.FindFirstObjectByType<BoardCombatEffectsDriver>();
        int defaultEffectLayer = previousEffectDriver != null ? previousEffectDriver.gameObject.layer : 0;
        if (previousEffectDriver != null)
            foreach (var item in previousEffectDriver.GetComponentsInChildren<Transform>(true)) oldEffectLayers[item] = item.gameObject.layer;
        var oldTarget = RenderTexture.active;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        var texture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
        Material floorMaterial = null; Camera camera = null;
        try
        {
            string folder = Path.Combine(Directory.GetParent(Application.dataPath).FullName, Output);
            Directory.CreateDirectory(Path.Combine(folder, "frames"));
            Vector3[] cells = ChooseCells(systems.VisionGenerator);
            var parent = Own(new GameObject("Disposable feedback preview"), owned);
            Fixture knight, archer, mage, healer, enemy, building;
            using (BoardActorAnimation.SuppressEffects())
            {
                knight = Unit(catalog, Kind.Knight, Team.Player, cells[0], parent.transform, actors);
                archer = Unit(catalog, Kind.Archer, Team.Player, cells[0], parent.transform, actors);
                mage = Unit(catalog, Kind.Magic, Team.Player, cells[0], parent.transform, actors);
                healer = Unit(catalog, Kind.WhiteMage, Team.Player, cells[0], parent.transform, actors);
                enemy = Unit(catalog, Kind.Knight, Team.Enemy, cells[2], parent.transform, actors);
                building = Make(catalog.Building(FacilityKind.Bakery)?.playerVisual, Kind.None, Team.Player,
                    Type.Building, cells[0], parent.transform, actors);
            }
            Vector3 center = (cells[0] + cells[2]) * .5f;
            var cameraObject = Own(new GameObject("Feedback capture camera"), owned); camera = cameraObject.AddComponent<Camera>();
            camera.cullingMask = 1 << Layer; camera.enabled = false; camera.orthographic = true;
            camera.nearClipPlane = .01f; camera.farClipPlane = 50; camera.allowHDR = false;
            camera.backgroundColor = new Color32(21, 27, 36, 255); camera.clearFlags = CameraClearFlags.SolidColor;
            camera.targetTexture = target;
            camera.transform.position = center + new Vector3(1.5f, 4.2f, -7);
            camera.transform.LookAt(center + Vector3.up * .23f);
            float halfHeight = 1.7f;
            foreach (var cell in cells)
            {
                var local = camera.transform.InverseTransformPoint(cell + Vector3.up * .45f);
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(local.y) + 1.15f, (Mathf.Abs(local.x) + 1.2f) * Height / Width);
            }
            camera.orthographicSize = halfHeight;
            var floorShader = Shader.Find("Unlit/Color");
            if (floorShader == null) throw new InvalidOperationException("Preview floor shader is missing.");
            floorMaterial = new Material(floorShader) { color = new Color32(40, 46, 57, 255), hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x <= 4; x++)
            {
                var floor = Own(GameObject.CreatePrimitive(PrimitiveType.Cube), owned); floor.name = "Preview board tile"; floor.layer = Layer;
                UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
                floor.transform.position = cells[0] + Vector3.right * x - Vector3.up * .53f;
                floor.transform.localScale = new Vector3(.97f, .06f, 1.65f);
                floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
            }
            AddLight("Feedback key", new Color(1, .93f, .84f), 1.15f, new Vector3(42, -25, 0), owned);
            AddLight("Feedback rim", new Color(.57f, .77f, 1), .6f, new Vector3(24, 145, 0), owned);
            var phaseActors = new[] { knight, archer, mage, healer, knight, building };
            int frame = 0;
            for (int phase = 0; phase < Phases.Length; phase++)
            {
                EffectTick.Invoke(null, new object[] { Time.unscaledTimeAsDouble + 1 });
                BodyTick.Invoke(null, new object[] { Time.unscaledTimeAsDouble + 1 });
                foreach (var fixture in actors)
                {
                    BoardActorAnimation.SetVisible(fixture.Actor, false);
                    fixture.Visual.SetActive(false);
                }
                var hero = phaseActors[phase]; hero.Visual.SetActive(true); BoardActorAnimation.SetVisible(hero.Actor, true);
                if (phase < 3) { enemy.Visual.SetActive(true); BoardActorAnimation.SetVisible(enemy.Actor, true); }
                double now = Time.unscaledTimeAsDouble;
                if (phase < 3)
                {
                    BoardActorAnimation.NotifyAttack(hero.Actor, enemy.Actor.transform.position);
                    BoardActorAnimation.NotifyHit(enemy.Actor);
                }
                else if (phase == 3) BoardActionFeedback.Heal(hero.Actor);
                else if (phase == 4) BoardActionFeedback.Summon(hero.Actor);
                else BoardActionFeedback.Build(hero.Actor);
                if (BoardCombatEffects.ActiveCount == 0) throw new InvalidOperationException("Production facade rejected preview phase: " + Phases[phase]);
                var effectDriver = UnityEngine.Object.FindFirstObjectByType<BoardCombatEffectsDriver>();
                if (effectDriver == null) throw new InvalidOperationException("Production effect driver did not initialize.");
                SetLayer(effectDriver.transform, Layer);
                for (int sample = 0; sample < FramesPerPhase; sample++, frame++)
                {
                    EffectTick.Invoke(null, new object[] { now + sample / (double)FPS });
                    BodyTick.Invoke(null, new object[] { now + sample / (double)FPS });
                    foreach (var fixture in actors)
                        if (fixture.Actor.transform.position != (fixture == enemy ? cells[2] : cells[0]))
                            throw new InvalidOperationException("Cosmetic feedback moved gameplay authority.");
                    camera.Render(); RenderTexture.active = target;
                    texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); texture.Apply(false);
                    File.WriteAllBytes(Path.Combine(folder, "frames", "frame_" + frame.ToString("D3") + ".png"), texture.EncodeToPNG());
                }
                if (BoardCombatEffects.ActiveCount != 0) throw new InvalidOperationException("Preview phase left a long-lived effect.");
            }
            if (systems.FactionState.GetAP(Team.Player) != playerAP || systems.FactionState.GetAP(Team.Enemy) != enemyAP)
                throw new InvalidOperationException("Feedback capture consumed AP.");
            if (!UnityEngine.Random.state.Equals(random)) throw new InvalidOperationException("Feedback capture consumed gameplay Random.");
            File.WriteAllText(Path.Combine(folder, "recording.json"), JsonUtility.ToJson(new Recording(), true));
            Debug.Log("[BoardFeedbackPreview] ALL PASSED frames=" + frame + " fps=" + FPS + " output=" + folder);
        }
        finally
        {
            EffectTick.Invoke(null, new object[] { Time.unscaledTimeAsDouble + 1 });
            var effectDriver = UnityEngine.Object.FindFirstObjectByType<BoardCombatEffectsDriver>();
            if (effectDriver != null)
                foreach (var item in effectDriver.GetComponentsInChildren<Transform>(true))
                    item.gameObject.layer = oldEffectLayers.TryGetValue(item, out int originalLayer) ? originalLayer : defaultEffectLayer;
            foreach (var fixture in actors) if (fixture.Actor != null) BoardActorAnimation.Unbind(fixture.Actor);
            BoardSoundEffects.StopAll();
            for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            RenderTexture.active = oldTarget;
            if (camera != null) camera.targetTexture = null;
            target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(texture);
            if (floorMaterial != null) UnityEngine.Object.DestroyImmediate(floorMaterial);
            UnityEngine.Random.state = random;
        }
        if (BoardActorAnimation.BoundCount != boundBefore) throw new InvalidOperationException("Preview retained actor bindings.");
    }
    static Fixture Unit(BoardVisualThemeCatalog catalog, Kind kind, Team team, Vector3 cell, Transform parent, List<Fixture> actors)
    {
        var style = catalog.Unit(kind);
        return Make(team == Team.Player ? style?.playerVisual : style?.enemyVisual, kind, team, Type.Unit, cell, parent, actors);
    }
    static Fixture Make(GameObject source, Kind kind, Team team, Type type, Vector3 cell, Transform parent, List<Fixture> actors)
    {
        if (source == null) throw new InvalidOperationException("Production preview appearance is missing: " + kind + "/" + type);
        var root = new GameObject("Preview " + kind + " " + type); root.transform.SetParent(parent, false); root.transform.position = cell;
        var visual = UnityEngine.Object.Instantiate(source, root.transform, false); visual.name = "Production appearance";
        // Player canonical geometry points toward -Z; board presentation normalizes it to +Z before event facing.
        if (type == Type.Unit && team == Team.Player) visual.transform.localRotation = Quaternion.Euler(0, 180, 0) * visual.transform.localRotation;
        var actor = root.AddComponent<Status>(); actor.kind = kind; actor.type = type; actor.team = team;
        actor.MaxHP = actor.HP = 40; actor.direction = Direction.N;
        BoardActorAnimation.Bind(actor, visual.transform); BoardActorAnimation.SetVisible(actor, true);
        SetLayer(root.transform, Layer);
        var result = new Fixture { Actor = actor, Visual = visual }; actors.Add(result); return result;
    }
    static Vector3[] ChooseCells(VisionGenerator vision)
    {
        var cells = new Dictionary<Vector2Int, Vector3>();
        foreach (var cell in vision.PlayerVisionBox)
            if (vision.IsPresentationCellVisible(cell)) cells[new Vector2Int(cell.x, cell.z)] = cell;
        foreach (var pair in cells)
        {
            if (!cells.TryGetValue(pair.Key + Vector2Int.left * 2, out var left)
                || !cells.TryGetValue(pair.Key + Vector2Int.right * 2, out var right)
                || Mathf.Abs(left.y - pair.Value.y) > .01f || Mathf.Abs(right.y - pair.Value.y) > .01f) continue;
            bool observed = true;
            for (int step = 0; step <= 8; step++) if (!vision.IsPresentationCellVisible(Vector3.Lerp(left, right, step / 8f))) observed = false;
            if (observed) return new[] { left, pair.Value, right };
        }
        throw new InvalidOperationException("Feedback preview needs a five-cell-wide observed, level row.");
    }
    static GameObject Own(GameObject obj, List<GameObject> owned) { owned.Add(obj); return obj; }
    static void SetLayer(Transform root, int layer)
    { root.gameObject.layer = layer; for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer); }
    static void AddLight(string name, Color color, float intensity, Vector3 angles, List<GameObject> owned)
    {
        var obj = Own(new GameObject(name), owned); obj.layer = Layer; var light = obj.AddComponent<Light>();
        light.type = LightType.Directional; light.color = color; light.intensity = intensity; light.cullingMask = 1 << Layer;
        light.shadows = LightShadows.None; obj.transform.rotation = Quaternion.Euler(angles);
    }
}
#endif
