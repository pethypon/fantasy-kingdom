using UnityEngine;

/// <summary>Limits unnecessary rendering; independent of game-time and turn rules.</summary>
[DisallowMultipleComponent]
public sealed class RuntimeFrameBudget : MonoBehaviour
{
    [SerializeField, Range(30, 144)] int activeFramesPerSecond = 60;
    [SerializeField, Range(5, 30)] int inactiveFramesPerSecond = 15;
    int previousFrameRate;
    int previousVSync;
    bool initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        if (Application.isBatchMode) return;
        var owner = new GameObject("RuntimeFrameBudget");
        DontDestroyOnLoad(owner);
        owner.AddComponent<RuntimeFrameBudget>();
    }

    void Awake()
    {
        previousFrameRate = Application.targetFrameRate;
        previousVSync = QualitySettings.vSyncCount;
        initialized = true;
        Apply(Application.isFocused);
    }

    void OnApplicationFocus(bool focused) => Apply(focused);
    void OnValidate()
    {
        activeFramesPerSecond = Mathf.Clamp(activeFramesPerSecond, 30, 144);
        inactiveFramesPerSecond = Mathf.Clamp(inactiveFramesPerSecond, 5, 30);
        if (initialized) Apply(Application.isFocused);
    }

    void Apply(bool focused)
    {
        // Desktop vSync otherwise overrides targetFrameRate, including on high-Hz monitors.
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = focused ? activeFramesPerSecond : inactiveFramesPerSecond;
    }

    void OnDestroy()
    {
        if (!initialized) return;
        Application.targetFrameRate = previousFrameRate;
        QualitySettings.vSyncCount = previousVSync;
    }
}
