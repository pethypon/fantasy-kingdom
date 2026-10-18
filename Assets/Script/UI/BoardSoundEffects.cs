using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Short, shared 2D board sounds. The caller owns visibility and save-restore checks.
/// One scene-owned pool has at most eight voices; repeated/AoE events never allocate audio objects.
/// </summary>
public static class BoardSoundEffects
{
    public const int MaximumVoices = 8;

    struct SoundStamp
    {
        public double Time;
        public int Frame;
    }

    static readonly AudioSource[] voices = new AudioSource[MaximumVoices];
    static readonly double[] startedAt = new double[MaximumVoices];
    static readonly bool[] importantVoice = new bool[MaximumVoices];
    static readonly Dictionary<BoardFeedbackKind, SoundStamp> recent = new Dictionary<BoardFeedbackKind, SoundStamp>(16);
    static BoardSoundEffectsDriver driver;
    static BoardFeedbackSettings activeSettings;
    static int voiceCount;

    public static int VoiceCount => voiceCount;
    /// <summary>Accepted playback requests, independent of an audio device's availability in batch tests.</summary>
    public static long AcceptedPlayCount { get; private set; }
    public static int PlayingCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < voiceCount; i++)
                if (voices[i] != null && voices[i].isPlaying) count++;
            return count;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetStatics()
    {
        // Enter Play Mode may preserve the scene while resetting static state.
        // Release the old pool before forgetting it, so it cannot become an orphan.
        if (driver != null)
        {
            for (int i = 0; i < voiceCount; i++) if (voices[i] != null) voices[i].Stop();
            driver.enabled = false;
            Object.Destroy(driver.gameObject);
        }
        ClearState();
    }

    public static void Play(BoardFeedbackKind kind, BoardFeedbackSettings settings)
    {
        if (!Application.isPlaying || settings == null || !settings.soundsEnabled)
        {
            if (settings != null && !settings.soundsEnabled) StopAll();
            return;
        }
        float master = FiniteVolume(settings.masterVolume);
        if (master <= 0 || settings.sounds == null) return;
        AudioClip clip = null;
        float volume = 0;
        foreach (var entry in settings.sounds)
        {
            if (entry == null || entry.kind != kind || entry.clip == null) continue;
            clip = entry.clip;
            volume = master * FiniteVolume(entry.volume);
            break;
        }
        if (clip == null || volume <= 0) return;

        double now = Time.unscaledTimeAsDouble;
        float interval = settings.minimumSoundInterval;
        if (float.IsNaN(interval) || float.IsInfinity(interval)) interval = .045f;
        interval = Mathf.Clamp(interval, 0, 1);
        if (recent.TryGetValue(kind, out var stamp)
            && (stamp.Frame == Time.frameCount || now - stamp.Time < interval)) return;

        EnsureDriver();
        activeSettings = settings;
        int limit = Mathf.Clamp(settings.maxAudioVoices, 1, MaximumVoices);
        // Changing the Inspector cap also bounds already-playing voices immediately.
        for (int i = limit; i < voiceCount; i++)
            if (voices[i] != null && voices[i].isPlaying) voices[i].Stop();

        bool important = kind == BoardFeedbackKind.Victory || kind == BoardFeedbackKind.Defeat;
        int slot = FreeVoice(limit);
        if (slot < 0 && important) slot = OldestVoice(limit);
        if (slot < 0) return;

        var source = voices[slot];
        source.Stop();
        source.clip = clip;
        source.volume = volume;
        source.priority = important ? 32 : 128;
        startedAt[slot] = now;
        importantVoice[slot] = important;
        recent[kind] = new SoundStamp { Time = now, Frame = Time.frameCount };
        source.Play();
        AcceptedPlayCount++;
        driver.enabled = true;
    }

    static float FiniteVolume(float value)
        => float.IsNaN(value) || float.IsInfinity(value) ? 0 : Mathf.Clamp01(value);

    static int FreeVoice(int limit)
    {
        for (int i = 0; i < voiceCount && i < limit; i++)
            if (voices[i] != null && !voices[i].isPlaying) return i;
        if (voiceCount >= limit) return -1;
        int slot = voiceCount++;
        var source = driver.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 0;
        source.dopplerLevel = 0;
        source.pitch = 1;
        source.panStereo = 0;
        voices[slot] = source;
        return slot;
    }

    static int OldestVoice(int limit)
    {
        int oldest = -1;
        // Finish cues replace ordinary effects first, preserving a previous result cue when possible.
        for (int pass = 0; pass < 2 && oldest < 0; pass++)
            for (int i = 0; i < voiceCount && i < limit; i++)
            {
                if (voices[i] == null || pass == 0 && importantVoice[i]) continue;
                if (oldest < 0 || startedAt[i] < startedAt[oldest]) oldest = i;
            }
        return oldest;
    }

    static void EnsureDriver()
    {
        if (driver != null) return;
        var owner = new GameObject("Board Sound Effects");
        driver = owner.AddComponent<BoardSoundEffectsDriver>();
        driver.enabled = false;
        // Intentionally scene-owned. No extra AudioListener and no DontDestroyOnLoad object.
    }

    public static void StopAll()
    {
        for (int i = 0; i < voiceCount; i++)
            if (voices[i] != null) { voices[i].Stop(); voices[i].clip = null; }
        if (driver != null) driver.enabled = false;
    }

    internal static void Tick()
    {
        if (activeSettings == null || !activeSettings.soundsEnabled || FiniteVolume(activeSettings.masterVolume) <= 0)
        {
            StopAll();
            return;
        }
        int limit = Mathf.Clamp(activeSettings.maxAudioVoices, 1, MaximumVoices);
        bool playing = false;
        for (int i = 0; i < voiceCount; i++)
        {
            var source = voices[i];
            if (source == null) continue;
            if (i >= limit) source.Stop();
            if (source.isPlaying) playing = true;
            else { source.clip = null; importantVoice[i] = false; }
        }
        if (!playing && driver != null) driver.enabled = false;
    }

    internal static void DriverDestroyed(BoardSoundEffectsDriver owner)
    {
        if (driver == owner) ClearState();
    }

    static void ClearState()
    {
        for (int i = 0; i < MaximumVoices; i++)
        {
            voices[i] = null;
            startedAt[i] = 0;
            importantVoice[i] = false;
        }
        voiceCount = 0;
        driver = null;
        activeSettings = null;
        AcceptedPlayCount = 0;
        recent.Clear();
    }
}
