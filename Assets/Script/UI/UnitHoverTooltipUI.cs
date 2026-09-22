using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

/// <summary>選択パネルをホバー情報にも使用する。選択中は表示対象を固定する。</summary>
public class UnitHoverTooltipUI : MonoBehaviour
{
    public static UnitHoverTooltipUI Instance { get; private set; }
    private UnitPanelUI panel;
    private TurnGenerator turn;
    private float nextProbeTime;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }
    private void OnDestroy() { if (Instance == this) Instance = null; }

    private void Update()
    {
        if (panel == null)
        {
            if (UIBuilder.ScreenCanvas == null) return;
            panel = UIBuilder.ScreenCanvas.GetComponentInChildren<UnitPanelUI>();
            if (panel == null) return;
        }
        if (panel.HasSelection) return;
        if (Time.unscaledTime < nextProbeTime) return;
        nextProbeTime = Time.unscaledTime + 0.1f;
        if (Mouse.current == null || Camera.main == null || EnemyTurnBannerUI.IsShowing
            || (GameMenuUI.Instance != null && GameMenuUI.Instance.IsOpen)
            || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()))
        {
            panel.Preview(null);
            return;
        }
        if (turn == null) turn = Object.FindFirstObjectByType<TurnGenerator>();
        Status hovered = null;
        var ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        if (Physics.Raycast(ray, out var hit, GameConstants.DefaultRayDistance))
        {
            var status = hit.transform.GetComponentInParent<Status>();
            if (status != null && status.IsAlive
                && (status.team == Team.Player || (turn != null && turn.Systems.VisionGenerator != null
                    && turn.Systems.VisionGenerator.IsInVisionXZ(Team.Player, status.transform.position)))
                && (status.type == Type.Unit || status.type == Type.Building || status.type == Type.Wall))
                hovered = status;
        }
        panel.Preview(hovered);
    }
}
