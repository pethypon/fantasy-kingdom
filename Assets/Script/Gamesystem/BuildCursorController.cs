using UnityEngine;
using UnityEngine.InputSystem;

// =====================================================================
//  BuildCursorController — 建築モードのカーソル描画・追従・可視制御
//
//  BuildSystem からカーソル関連のロジックを分離。
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
    static readonly Color ColorValid   = new Color(0.5f, 1f, 0.5f, 0.5f);
    static readonly Color ColorInvalid = new Color(1f, 0.3f, 0.3f, 0.5f);

    // ---- Raycast ----
    readonly int _blockLayerMask;

    public Vector3Int LastPosition => _lastPos;
    public bool IsVisible => _visible;

    public BuildCursorController()
    {
        _blockLayerMask = LayerMask.GetMask("Block");
    }

    // ================================================================
    //  カーソル生成
    // ================================================================
    public void Create()
    {
        Destroy();

        _cursorObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _cursorObj.name = "BuildCursor";
        _cursorObj.transform.localScale = new Vector3(0.95f, 0.95f, 0.95f);

        // コライダーを無効化（Raycast に干渉しないように）
        var col = _cursorObj.GetComponent<Collider>();
        if (col != null) col.enabled = false;

        // 半透明マテリアル
        _cursorRenderer = _cursorObj.GetComponent<Renderer>();
        _cursorMaterial = new Material(Shader.Find("Standard"));
        _cursorMaterial.SetFloat("_Mode", 3); // Transparent
        _cursorMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _cursorMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _cursorMaterial.SetInt("_ZWrite", 0);
        _cursorMaterial.DisableKeyword("_ALPHATEST_ON");
        _cursorMaterial.EnableKeyword("_ALPHABLEND_ON");
        _cursorMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        _cursorMaterial.renderQueue = 3000;
        _cursorMaterial.color = ColorValid;
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
        if (!Physics.Raycast(ray, out RaycastHit hit, 100f, _blockLayerMask)) return false;

        gridPos = new Vector3Int(
            Mathf.RoundToInt(hit.point.x),
            Mathf.RoundToInt(hit.point.y),
            Mathf.RoundToInt(hit.point.z)
        );
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
            _cursorMaterial.color = canPlace ? ColorValid : ColorInvalid;
        }
    }

    // ================================================================
    //  内部: マウス Ray 取得
    // ================================================================
    bool TryGetMouseRay(out Ray ray)
    {
        ray = default;
        if (Mouse.current == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        ray = Camera.main.ScreenPointToRay(mousePos);
        return true;
    }
}
