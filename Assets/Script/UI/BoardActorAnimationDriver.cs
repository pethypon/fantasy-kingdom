using UnityEngine;

/// <summary>Enabled only during movement, attacks or hit reactions; idle actors have no frame callback.</summary>
[AddComponentMenu("")]
public sealed class BoardActorAnimationDriver : MonoBehaviour
{
    void LateUpdate() => BoardActorAnimation.Tick(Time.unscaledTimeAsDouble);
    void OnDestroy() => BoardActorAnimation.DriverDestroyed(this);
}
