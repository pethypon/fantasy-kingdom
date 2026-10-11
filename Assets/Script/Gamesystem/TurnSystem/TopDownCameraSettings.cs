using System;
using UnityEngine;

// Responsibility: inspectable map framing and view-only controls.
// Lifetime/Owner: one TurnCameraController for its scene. Maximum retained items: no collections.
// Cleanup timing: owner destruction. Failure behavior: invalid numeric values use safe framing defaults.
[Serializable]
public sealed class TopDownCameraSettings
{
    [Tooltip("見下ろし時は平行投影にする。OFFなら遠近投影のまま全体を表示")]
    public bool UseOrthographic = true;
    [Tooltip("マップ全体表示の余白。1.10 は10%の余白")]
    [Min(1f)] public float MapViewPadding = 1.10f;
    [Tooltip("見下ろし中もWASD・中ボタンドラッグでカメラ移動")]
    public bool AllowPan = true;
    [Tooltip("見下ろし中もホイールでズーム")]
    public bool AllowZoom = true;

    public float SafePadding => float.IsNaN(MapViewPadding) || float.IsInfinity(MapViewPadding)
        ? 1.10f : Mathf.Clamp(MapViewPadding, 1f, 4f);
}
