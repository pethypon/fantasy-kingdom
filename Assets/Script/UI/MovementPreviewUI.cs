using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>Preview the same single legal move and AP calculation used by UnitClick.</summary>
public sealed class MovementPreviewUI : MonoBehaviour
{
    TurnGenerator turn;
    RectTransform panel, canvasRect;
    TextMeshProUGUI label;
    LineRenderer route;
    Material routeMaterial;
    int lastCost = -1, lastAP = -1;
    Vector3 routeFrom, routeTo;
    MapCreate routeMap;
    bool hasRoute;
    public void Bind(TurnGenerator value) { turn = value; }

    void Build()
    {
        var go = new GameObject("Movement preview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        go.transform.SetParent(transform, false);
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 190;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920,1080); scaler.matchWidthOrHeight = .5f;
        canvasRect = go.GetComponent<RectTransform>();
        var box = new GameObject("Move AP", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(go.transform,false);
        panel = box.GetComponent<RectTransform>(); panel.sizeDelta = new Vector2(380,108); panel.pivot = new Vector2(0,1);
        var bg = box.GetComponent<Image>(); bg.color = new Color(.025f,.045f,.07f,.96f); bg.raycastTarget = false;
        var text = new GameObject("Cost",typeof(RectTransform),typeof(TextMeshProUGUI));
        text.transform.SetParent(box.transform,false);
        label = text.GetComponent<TextMeshProUGUI>(); label.fontSize = 30; label.raycastTarget = false;
        label.font = UIFactory.LoadDefaultFont();
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
        label.rectTransform.offsetMin = new Vector2(16,8); label.rectTransform.offsetMax = new Vector2(-16,-8);
        var line = new GameObject("Planned move",typeof(LineRenderer)); line.transform.SetParent(transform,false);
        route = line.GetComponent<LineRenderer>(); route.useWorldSpace = true; route.widthMultiplier = .065f;
        route.numCapVertices = 4; route.numCornerVertices = 3;
        route.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; route.receiveShadows = false;
        routeMaterial = new Material(Resources.Load<Shader>("Shaders/MovementRoute")); route.sharedMaterial = routeMaterial;
        Hide();
    }
    void LateUpdate()
    {
        if (turn == null || UIBuilder.ScreenCanvas == null) { Hide(); return; }
        var unit = turn.Context.SelectUnit;
        if (!(turn.CurrentState is PlayerMove state) || !state.MenuSwitch || state.BuildMode || state.SummonMode
            || unit == null || !unit.IsAlive || unit.team != Team.Player || StatusEffectSystem.IsMovementBlocked(unit)
            || Mouse.current == null || Camera.main == null
            || (GameMenuUI.Instance != null && GameMenuUI.Instance.IsOpen)
            || (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())) { Hide(); return; }
        var mouse = Mouse.current.position.ReadValue();
        var moves = turn.Systems.MoveGenerator;
        if (moves == null || moves.mapcreate == null || turn.Systems.APSystem == null
            || !moves.TryGetMoveDestination(Camera.main.ScreenPointToRay(mouse),out _,out var to)) { Hide(); return; }
        var from = turn.Context.OldCell;
        int cost = turn.Systems.APSystem.CalcCost(APSystem.ActionType.Move,unit,from,to);
        int ap = turn.Systems.APSystem.GetAP(Team.Player);
        if (ap < cost) { Hide(); return; }
        if (panel == null) Build();
        if (cost != lastCost || ap != lastAP)
        {
            label.text = $"移動コスト <color=#80E7FF>{cost} AP</color>\n残りAP {ap} → <color=#B6F3A1>{ap-cost}</color>";
            lastCost = cost; lastAP = ap;
        }
        UpdatePanelPosition(mouse);
        if (!hasRoute || from != routeFrom || to != routeTo || routeMap != moves.mapcreate)
        {
            UpdateRoute(from, to, moves.mapcreate);
            routeFrom = from; routeTo = to; routeMap = moves.mapcreate; hasRoute = true;
        }
        if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
        route.enabled = true;
    }

    void UpdatePanelPosition(Vector2 mouse)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect,mouse,null,out var local);
        var bounds = canvasRect.rect;
        float x = local.x + 24, y = local.y + panel.rect.height + 24;
        if (x + panel.rect.width > bounds.xMax) x = local.x - panel.rect.width - 24;
        x = Mathf.Clamp(x,bounds.xMin,bounds.xMax-panel.rect.width);
        y = Mathf.Clamp(y,bounds.yMin+panel.rect.height,bounds.yMax);
        panel.anchoredPosition = new Vector2(x,y);
    }

    void UpdateRoute(Vector3 from, Vector3 to, MapCreate map)
    {
        // Movement is a single chess-pattern action, not a chain of walking actions.
        // Sample its direct trajectory so the line follows intervening terrain heights.
        int samples = Mathf.Max(2,Mathf.CeilToInt(Vector3.Distance(from,to)*8)+1);
        route.positionCount = samples;
        for(int i=0;i<samples;i++)
        {
            var p = Vector3.Lerp(from,to,i/(float)(samples-1));
            int xCell = Mathf.RoundToInt(p.x), zCell = Mathf.RoundToInt(p.z);
            p.y = Mathf.Max(p.y-.4f,map.SurfaceTop(xCell,zCell)+.07f);
            route.SetPosition(i,p);
        }
    }
    void Hide()
    {
        if(panel && panel.gameObject.activeSelf) panel.gameObject.SetActive(false);
        if(route) route.enabled=false;
        hasRoute = false;
    }
    void OnDisable() { Hide(); }
    void OnDestroy() { if(routeMaterial) Destroy(routeMaterial); if(canvasRect) Destroy(canvasRect.gameObject); if(route) Destroy(route.gameObject); }
}
