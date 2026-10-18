using UnityEngine;

/// <summary>One bounded board effect updater, disabled when every effect has expired.</summary>
[AddComponentMenu("")]
public sealed class BoardCombatEffectsDriver : MonoBehaviour
{
    void LateUpdate() => BoardCombatEffects.Tick(Time.unscaledTimeAsDouble);
    void OnDestroy() => BoardCombatEffects.DriverDestroyed(this);
}
