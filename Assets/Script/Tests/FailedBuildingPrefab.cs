#if UNITY_EDITOR
using UnityEngine;

/// <summary>Real invalid-prefab lifecycle for paid-building regression tests; omitted from player builds.</summary>
public sealed class FailedBuildingPrefab : MonoBehaviour
{
    public bool destroyCloneOnAwake;
    void Awake()
    {
        if (destroyCloneOnAwake) DestroyImmediate(gameObject);
    }
}
#endif
