using UnityEngine;

/// <summary>Only watches active short sounds. Disables its frame callback once every voice is idle.</summary>
[AddComponentMenu("")]
public sealed class BoardSoundEffectsDriver : MonoBehaviour
{
    void Update() => BoardSoundEffects.Tick();
    void OnDestroy() => BoardSoundEffects.DriverDestroyed(this);
}
