#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Real Play Mode contracts for the production feedback facade, bounded pools and shared assets.</summary>
public static class BoardFeedbackTests
{
    static int checks;
    static readonly MethodInfo EffectTick = typeof(BoardCombatEffects).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);
    static readonly MethodInfo SoundTick = typeof(BoardSoundEffects).GetMethod("Tick", BindingFlags.Static | BindingFlags.NonPublic);
    static void Check(bool value, string message)
    { checks++; if (!value) throw new InvalidOperationException("[BoardFeedbackTests] " + message); }
    static void TickEffects(double now) => EffectTick.Invoke(null, new object[] { now });
    static void TickSounds() => SoundTick.Invoke(null, null);

    public static void Assets()
    {
        checks = 0; ValidateAssets();
        Debug.Log("[BoardFeedbackTests] ASSETS PASSED " + checks);
    }
    static BoardFeedbackSettings ValidateAssets()
    {
        var settings = Resources.Load<BoardFeedbackSettings>(BoardFeedbackSettings.ResourcePath);
        Check(settings != null, "Production Resources/GameContent/BoardFeedback settings exists");
        Check(settings.effectsEnabled && settings.soundsEnabled, "Production effects and sounds are enabled");
        Check(settings.effectMaterial != null && settings.effectMaterial.shader != null, "Production effect has a shared shader/material");
        Check(settings.maxVisualEffects > 0 && settings.maxVisualEffects <= 16, "Production visual cap fits the bounded runtime pool");
        Check(settings.maxAudioVoices > 0 && settings.maxAudioVoices <= 8, "Production sound cap fits the bounded voice pool");
        Check(settings.masterVolume > 0 && settings.masterVolume <= 1, "Production master volume is audible and finite");
        var kinds = new HashSet<BoardFeedbackKind>(); var clips = new HashSet<AudioClip>();
        long bytes = 0;
        foreach (var entry in settings.sounds)
        {
            Check(entry != null && kinds.Add(entry.kind), "Each sound kind has one unambiguous entry");
            Check(entry.clip != null && clips.Add(entry.clip), "Each sound kind has its own imported clip");
            Check(entry.volume > 0 && entry.volume <= 1, "Every effect has a finite nonzero volume");
            Check(entry.clip.samples > 0 && entry.clip.frequency >= 16000 && entry.clip.frequency <= 48000,
                "Each imported sound has valid PCM samples and a modest sample rate");
            Check(entry.clip.channels >= 1 && entry.clip.channels <= 2 && entry.clip.length >= .02f && entry.clip.length <= 3,
                "Each imported sound is short mono/stereo audio");
            string path = AssetDatabase.GetAssetPath(entry.clip);
            Check(path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) && File.Exists(path), "The clip references a checked-in WAV");
            var data = File.ReadAllBytes(path); bytes += data.Length;
            Check(data.Length > 44 && System.Text.Encoding.ASCII.GetString(data, 0, 4) == "RIFF"
                && System.Text.Encoding.ASCII.GetString(data, 8, 4) == "WAVE", "WAV has a valid RIFF/WAVE container");
        }
        foreach (BoardFeedbackKind kind in Enum.GetValues(typeof(BoardFeedbackKind)))
            Check(kinds.Contains(kind), "Production sound exists for " + kind);
        Check(kinds.Count == 13 && bytes <= 2 * 1024 * 1024, "Thirteen short sounds fit a two MiB uncompressed source budget");
        return settings;
    }

    public static void PlayMode(GameSystems systems)
    {
        if (!Application.isPlaying || systems?.VisionGenerator == null || systems.FactionState == null)
            throw new InvalidOperationException("Actual started Play Mode scene required.");
        checks = 0;
        var settings = ValidateAssets();
        Check(EffectTick != null && SoundTick != null, "Production pool callbacks are available for deterministic sampling");
        int playerAP = systems.FactionState.GetAP(Team.Player), enemyAP = systems.FactionState.GetAP(Team.Enemy);
        int listeners = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length;
        var random = UnityEngine.Random.state;
        bool oldEffects = settings.effectsEnabled, oldSounds = settings.soundsEnabled;
        int oldVisualCap = settings.maxVisualEffects, oldVoiceCap = settings.maxAudioVoices;
        float oldVolume = settings.masterVolume, oldInterval = settings.minimumSoundInterval;
        var objects = new List<GameObject>();
        Vector3 cell = VisibleCell(systems.VisionGenerator);
        Status actor = null, hidden = null;
        try
        {
            ResetEffects(); ResetSounds();
            using (BoardActorAnimation.SuppressEffects())
            {
                actor = Fixture("Feedback authority fixture", Team.Player, cell, objects);
                hidden = Fixture("Feedback hidden fixture", Team.Enemy, new Vector3(-9876, 0, -9876), objects);
            }
            var body = actor.GetComponent<BoxCollider>();
            var position = actor.transform.position; var rotation = actor.transform.rotation; var scale = actor.transform.localScale;
            var bounds = body.bounds;
            settings.effectsEnabled = true; settings.soundsEnabled = true; settings.masterVolume = .2f;

            // The facade is the save/initialization visibility boundary. Direct low-level Emit/Play deliberately do not own actor rules.
            using (BoardActorAnimation.SuppressEffects())
            {
                AllActorEvents(actor, cell);
                BoardActionFeedback.UI(); BoardActionFeedback.Result(GameResult.Win); BoardActionFeedback.Result(GameResult.Lose);
                actor.HP = 39; actor.HP = 40;
                Check(BoardCombatEffects.ActiveCount == 0 && BoardSoundEffects.AcceptedPlayCount == 0,
                    "Load/initialization suppression prevents actor, HP, UI and result feedback");
            }
            AllActorEvents(hidden, hidden.transform.position + Vector3.forward);
            Check(BoardCombatEffects.ActiveCount == 0 && BoardSoundEffects.AcceptedPlayCount == 0,
                "Unknown enemy events reveal neither effects nor sounds");
            Check(!systems.VisionGenerator.IsPresentationCellVisible(hidden.transform.position), "Hidden fixture really lies outside the display mask");

            BoardActionFeedback.Result(GameResult.TimeUpDraw);
            Check(BoardSoundEffects.AcceptedPlayCount == 0, "A draw does not play the defeat cue");
            BoardActionFeedback.Attack(actor, cell);
            Check(BoardCombatEffects.ActiveCount > 0, "Observed normal attack reaches the production effects pool");
            Check(BoardSoundEffects.AcceptedPlayCount > 0, "Observed normal attack submits a real AudioSource.Play request");
            long attackEmissions = BoardActionFeedback.EmissionCount;
            int attackSlots = BoardCombatEffects.ActiveCount;
            BoardActionFeedback.Attack(actor, cell);
            Check(BoardActionFeedback.EmissionCount == attackEmissions && BoardCombatEffects.ActiveCount == attackSlots,
                "Duplicate same-frame attack hooks do not double the effect");
            BoardActionFeedback.Move(actor, cell); BoardActionFeedback.Hit(actor); BoardActionFeedback.Heal(actor);
            BoardActionFeedback.Shield(actor); BoardActionFeedback.Summon(actor); BoardActionFeedback.Build(actor);
            Check(BoardActionFeedback.EmissionCount == attackEmissions + 6, "Move, hit, heal, shield, summon and build reach the shared gate");
            Status caster;
            using (BoardActorAnimation.SuppressEffects()) caster = Fixture("Feedback magic fixture", Team.Player, cell, objects);
            caster.kind = Kind.Magic;
            long beforeSkill = BoardActionFeedback.EmissionCount;
            BoardActionFeedback.Skill(caster); BoardActionFeedback.Attack(caster, cell);
            Check(BoardActionFeedback.EmissionCount == beforeSkill + 2, "Standalone skill and magic attack have production feedback");
            Status archer;
            using (BoardActorAnimation.SuppressEffects()) archer = Fixture("Feedback projectile fixture", Team.Player, cell, objects);
            archer.kind = Kind.Archer;
            long beforeProjectile = BoardActionFeedback.EmissionCount;
            BoardActionFeedback.Attack(archer, cell);
            Check(BoardActionFeedback.EmissionCount == beforeProjectile + 1, "Ranged attack reaches the production projectile branch");
            Check(actor.transform.position == position && actor.transform.rotation == rotation && actor.transform.localScale == scale,
                "Attack feedback never moves, rotates or scales gameplay authority");
            Check(body.bounds == bounds && body.enabled, "Feedback preserves the click collider");
            Check(actor.HP == 40 && actor.ATK == 13 && actor.DEF == 9 && actor.Level == 3,
                "Feedback preserves combat statistics");
            TickEffects(Time.unscaledTimeAsDouble + 1);
            Check(BoardCombatEffects.ActiveCount == 0, "Every short effect expires after its production lifetime");
            var effectDriver = UnityEngine.Object.FindFirstObjectByType<BoardCombatEffectsDriver>();
            Check(effectDriver != null && !effectDriver.enabled, "Idle visual pool disables its frame callback");
            Check(BoardCombatEffects.MeshCount == 5, "Visual pool shares five meshes across all events");

            settings.maxVisualEffects = 4;
            for (int i = 0; i < 250; i++)
                BoardCombatEffects.Emit((BoardFeedbackKind)(i % 10), cell, Vector3.forward, Color.cyan, settings);
            Check(BoardCombatEffects.ActiveCount <= 4 && BoardCombatEffects.PoolCount <= 16, "Burst effects obey the configured active cap and fixed pool bound");
            Check(BoardCombatEffects.MeshCount == 5, "Burst effects never duplicate shared meshes");
            Check(UnityEngine.Object.FindObjectsByType<BoardCombatEffectsDriver>(FindObjectsSortMode.None).Length == 1,
                "Effects use one scene driver");
            foreach (var renderer in effectDriver.GetComponentsInChildren<MeshRenderer>(true))
            {
                Check(renderer.sharedMaterial == settings.effectMaterial, "Every slot reuses the author material");
                Check(renderer.GetComponents<Collider>().Length == 0 && renderer.GetComponents<Animator>().Length == 0,
                    "Effect slots add no colliders or Animators");
            }

            // Remove one display-only cached mask entry for one synchronous callback, then restore it even on failure.
            // No game turn/yield occurs here; explored/real faction vision and map data remain untouched.
            var field = typeof(VisionGenerator).GetField("_reusableVisionXZ", BindingFlags.Instance | BindingFlags.NonPublic);
            var displayMask = field?.GetValue(systems.VisionGenerator) as HashSet<Vector3Int>;
            Check(displayMask != null, "Actual display visibility mask can be exercised");
            Vector3Int key = GridHelper.ToGridXZ(cell);
            bool wasVisible = displayMask.Remove(key);
            try
            {
                Check(wasVisible, "Fog fixture starts in an actually observed cell");
                TickEffects(Time.unscaledTimeAsDouble + .01);
                Check(BoardCombatEffects.ActiveCount == 0 && !effectDriver.enabled, "Existing effects disappear immediately when Fog hides their cell");
            }
            finally { if (wasVisible) displayMask.Add(key); }
            Check(systems.VisionGenerator.IsPresentationCellVisible(cell), "Display mask is restored after the Fog fixture");

            BoardCombatEffects.Emit(BoardFeedbackKind.Projectile, cell, hidden.transform.position - cell, Color.yellow, settings);
            TickEffects(Time.unscaledTimeAsDouble + .01);
            Check(BoardCombatEffects.ActiveCount == 0, "A projectile crossing unobserved terrain never remains visible");
            BoardCombatEffects.Emit(BoardFeedbackKind.Heal, cell, Vector3.forward, Color.green, settings);
            settings.effectsEnabled = false; TickEffects(Time.unscaledTimeAsDouble + .01);
            Check(BoardCombatEffects.ActiveCount == 0 && !effectDriver.enabled, "Disabling effects clears live slots and the callback");
            BoardCombatEffects.Emit(BoardFeedbackKind.Hit, cell, Vector3.forward, Color.red, settings);
            Check(BoardCombatEffects.ActiveCount == 0, "Disabled effects reject new requests");
            settings.effectsEnabled = true;
            BoardCombatEffects.Emit(BoardFeedbackKind.Hit, new Vector3(float.NaN, 0, 0), Vector3.zero, Color.red, settings);
            Check(BoardCombatEffects.ActiveCount == 0, "Invalid coordinates cannot poison the shared visual pool");

            ResetSounds(); settings.maxAudioVoices = 2; settings.minimumSoundInterval = 0;
            BoardSoundEffects.Play(BoardFeedbackKind.Attack, settings);
            long accepted = BoardSoundEffects.AcceptedPlayCount;
            Check(accepted == 1, "One enabled clip reaches AudioSource.Play regardless of device availability");
            for (int i = 0; i < 250; i++) BoardSoundEffects.Play(BoardFeedbackKind.Attack, settings);
            Check(BoardSoundEffects.AcceptedPlayCount == accepted, "Same-kind same-frame bursts are rejected even with zero minimum interval");
            foreach (BoardFeedbackKind kind in Enum.GetValues(typeof(BoardFeedbackKind))) BoardSoundEffects.Play(kind, settings);
            Check(BoardSoundEffects.VoiceCount <= 2 && BoardSoundEffects.PlayingCount <= 2, "Different simultaneous sounds obey the configured voice cap");
            var soundDriver = UnityEngine.Object.FindFirstObjectByType<BoardSoundEffectsDriver>();
            Check(soundDriver != null && UnityEngine.Object.FindObjectsByType<BoardSoundEffectsDriver>(FindObjectsSortMode.None).Length == 1,
                "Audio uses one scene driver");
            foreach (var source in soundDriver.GetComponents<AudioSource>())
                Check(!source.playOnAwake && !source.loop && source.spatialBlend == 0 && source.dopplerLevel == 0,
                    "Short sound voices never auto-play, loop or add positional Doppler");
            int observedNativeVoices = BoardSoundEffects.PlayingCount;
            accepted = BoardSoundEffects.AcceptedPlayCount; settings.masterVolume = 0;
            BoardSoundEffects.Play(BoardFeedbackKind.Heal, settings);
            Check(BoardSoundEffects.AcceptedPlayCount == accepted, "Zero master volume rejects playback requests");
            settings.masterVolume = .2f; settings.soundsEnabled = false;
            BoardSoundEffects.Play(BoardFeedbackKind.Magic, settings); TickSounds();
            Check(BoardSoundEffects.AcceptedPlayCount == accepted && BoardSoundEffects.PlayingCount == 0 && !soundDriver.enabled,
                "Disabling sounds stops voices and the idle frame callback");
            foreach (var source in soundDriver.GetComponents<AudioSource>()) Check(source.clip == null, "Stopped sources release clip references");
            settings.soundsEnabled = true;
            BoardSoundEffects.StopAll(); TickSounds();
            Check(!soundDriver.enabled, "An empty voice pool has no enabled callback");
            Check(UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None).Length == listeners,
                "Feedback adds no AudioListener");
            Check(systems.FactionState.GetAP(Team.Player) == playerAP && systems.FactionState.GetAP(Team.Enemy) == enemyAP,
                "No cosmetic event consumes either faction's AP");
            Check(UnityEngine.Random.state.Equals(random), "All feedback leaves gameplay Random unchanged");
            ResetEffects(); ResetSounds();
            Check(BoardCombatEffects.ActiveCount == 0 && BoardCombatEffects.PoolCount == 0 && BoardCombatEffects.MeshCount == 0,
                "Scene driver teardown releases visual slots and owned mesh references");
            Check(BoardSoundEffects.VoiceCount == 0 && BoardSoundEffects.PlayingCount == 0 && BoardSoundEffects.AcceptedPlayCount == 0,
                "Scene driver teardown releases voices and resets counters");
            Debug.Log("[BoardFeedbackTests] NATIVE_AUDIO_OBSERVATION playingVoices=" + observedNativeVoices
                + "; request acceptance/caps validated; this test does not certify speaker audibility.");
        }
        finally
        {
            settings.effectsEnabled = oldEffects; settings.soundsEnabled = oldSounds;
            settings.maxVisualEffects = oldVisualCap; settings.maxAudioVoices = oldVoiceCap;
            settings.masterVolume = oldVolume; settings.minimumSoundInterval = oldInterval;
            ResetEffects(); ResetSounds();
            foreach (var obj in objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            UnityEngine.Random.state = random;
        }
        Debug.Log("[BoardFeedbackTests] ALL PASSED " + checks);
    }
    static Status Fixture(string name, Team team, Vector3 cell, List<GameObject> owned)
    {
        var root = new GameObject(name); owned.Add(root); root.transform.position = cell;
        root.AddComponent<BoxCollider>().size = Vector3.one * .6f;
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube); visual.transform.SetParent(root.transform, false);
        UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
        visual.transform.localScale = new Vector3(.4f, .8f, .4f);
        var actor = root.AddComponent<Status>();
        actor.kind = Kind.Knight; actor.type = Type.Unit; actor.team = team; actor.MaxHP = actor.HP = 40;
        actor.ATK = 13; actor.DEF = 9; actor.Level = 3;
        return actor;
    }
    static void AllActorEvents(Status actor, Vector3 target)
    {
        BoardActionFeedback.Move(actor, actor.transform.position);
        BoardActionFeedback.Attack(actor, target); BoardActionFeedback.Hit(actor); BoardActionFeedback.Skill(actor);
        BoardActionFeedback.Heal(actor); BoardActionFeedback.Shield(actor); BoardActionFeedback.Summon(actor); BoardActionFeedback.Build(actor);
    }
    static Vector3 VisibleCell(VisionGenerator vision)
    {
        foreach (var cell in vision.PlayerVisionBox) if (vision.IsPresentationCellVisible(cell)) return cell;
        throw new InvalidOperationException("At least one observed cell is required.");
    }
    static void ResetEffects()
    {
        foreach (var driver in UnityEngine.Object.FindObjectsByType<BoardCombatEffectsDriver>(FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(driver.gameObject);
    }
    static void ResetSounds()
    {
        BoardSoundEffects.StopAll();
        foreach (var driver in UnityEngine.Object.FindObjectsByType<BoardSoundEffectsDriver>(FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(driver.gameObject);
    }
}
#endif
