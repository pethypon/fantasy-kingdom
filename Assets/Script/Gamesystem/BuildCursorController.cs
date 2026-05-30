using UnityEngine;
using UnityEngine.InputSystem;

// =====================================================================
//  BuildCursorController — 配置モードの汎用カーソル描画・追従・可視制御
//
//  BuildSystem / SummonSystem から共用される。
//  カーソルの生成/破棄、マウスRay取得、半透明マテリアル管理を担当する。
// =====================================================================
public class BuildCursorController
{
    // ---- カーソルオブジェクト ----
    GameObject _cursorObj;
    Renderer _cursorRenderer;
    Material _cursorMaterial;

    // ---- 状態 ----
    bool _visible;
    Vector3Int _lastPos;

    // ---- 色定義 ----
    readonly Color _colorValid;
    readonly Color _colorInvalid;

    // ---- 形状設定 ----
    readonly PrimitiveType _shape;
    readonly Vector3 _scale;
    readonly string _name;

    // ---- Raycast ----
    readonly int _blockLayerMask;

    public Vector3Int LastPosition => _lastPos;
    public bool IsVisible => _visible;

    /// <summary>建築カーソル用のデフォルトコンストラクタ</summary>
    public BuildCursorController()
        : this(PrimitiveType.Cube, new Vector3(0.95f, 0.95f, 0.95f), "BuildCursor",
               BrandGuide.CursorBuildValid, BrandGuide.CursorBuildInvalid) { }

    /// <summary>汎用コンストラクタ（形状・スケール・色を指定）</summary>
    public BuildCursorController(PrimitiveType shape, Vector3 scale, string name,
                                  Color colorValid, Color colorInvalid)
    {
        _shape = shape;
        _scale = scale;
        _name = name;
        _colorValid = colorValid;
        _colorInvalid = colorInvalid;
        _blockLayerMask = LayerMask.GetMask("Block");
    }

    // ================================================================
    //  カーソル生成
    // ================================================================
    public void Create()
    {
        Destroy();

        _cursorObj = GameObject.CreatePrimitive(_shape);
        _cursorObj.name = _name;
        _cursorObj.transform.localScale = _scale;

        var col = _cursorObj.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        _cursorRenderer = _cursorObj.GetComponent<Renderer>();
        _cursorMaterial = CreateTransparentMaterial(_colorValid);
        _cursorRenderer.material = _cursorMaterial;

        SetVisible(false);
    }

    // ================================================================
    //  カーソル破棄
    // ================================================================
    public void Destroy()
    {
        if (_cursorObj != null)
        {
            Object.Destroy(_cursorMaterial);
            Object.Destroy(_cursorObj);
            _cursorObj = null;
            _cursorRenderer = null;
            _cursorMaterial = null;
        }
        _visible = false;
    }

    // ================================================================
    //  可視性
    // ================================================================
    public void SetVisible(bool visible)
    {
        _visible = visible;
        if (_cursorObj != null)
            _cursorObj.SetActive(visible);
    }

    // ================================================================
    //  カーソル位置の更新
    // ================================================================

    /// <summary>
    /// マウスRaycastでグリッド位置を取得する。
    /// 成功時は gridPos に値を設定し true を返す。
    /// </summary>
    public bool TryGetGridPosition(out Vector3Int gridPos)
    {
        gridPos = default;
        if (_cursorObj == null) return false;

        if (!TryGetMouseRay(out Ray ray)) return false;
        if (!Physics.Raycast(ray, out RaycastHit hit, GameConstants.DefaultRayDistance, _blockLayerMask)) return false;

        gridPos = GridHelper.ToGrid(hit.point);
        return true;
    }

    /// <summary>カーソルを指定位置に移動し、設置可否に応じた色を設定する</summary>
    public void UpdatePosition(Vector3Int pos, bool canPlace)
    {
        _lastPos = pos;
        if (_cursorObj != null)
        {
            _cursorObj.transform.position = new Vector3(pos.x, pos.y, pos.z);
            SetVisible(true);
            _cursorMaterial.color = canPlace ? _colorValid : _colorInvalid;
        }
    }

    // ================================================================
    //  内部: 半透明マテリアル生成
    // ================================================================
    static Material CreateTransparentMaterial(Color color)
    {
        var mat = new Material(Shader.Find("Standard"));
        mat.SetFloat("_Mode", 3); // Transparent
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.renderQueue = 3000;
        mat.color = color;
        return mat;
    }

    // ================================================================
    //  内部: マウス Ray 取得
    // ================================================================
    static bool TryGetMouseRay(out Ray ray)
    {
        ray = default;
        if (Mouse.current == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        ray = Camera.main.ScreenPointToRay(mousePos);
        return true;
    }
}
