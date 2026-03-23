using UnityEngine;

/// <summary>
/// 選択中のユニットに視覚的なハイライト（下部に光るリング）を表示する。
/// TurnGenerater.SelectUnit の変化を毎フレーム監視し、自動で表示/非表示を切り替える。
/// </summary>
public class UnitSelectionHighlight : MonoBehaviour
{
    private TurnGenerater _turnGenerater;
    private GameObject _ringObj;
    private MeshRenderer _ringRenderer;
    private Status _currentTarget;

    // ---- 設定 ----
    private const float RingScale = 0.9f;
    private const float RingYOffset = 0.05f;
    private const float PulseSpeed = 3f;
    private const float PulseMin = 0.5f;
    private const float PulseMax = 1.0f;
    private static readonly Color PlayerColor = new Color(0.3f, 0.7f, 1f, 0.7f);
    private static readonly Color EnemyColor = new Color(1f, 0.3f, 0.3f, 0.5f);

    public void Init(TurnGenerater turnGenerater)
    {
        _turnGenerater = turnGenerater;
        CreateRing();
    }

    private void CreateRing()
    {
        // 薄いシリンダーでリングを表現
        _ringObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _ringObj.name = "SelectionRing";
        _ringObj.transform.SetParent(transform);
        _ringObj.transform.localScale = new Vector3(RingScale, 0.02f, RingScale);

        // コリジョン除去
        var col = _ringObj.GetComponent<Collider>();
        if (col != null) Destroy(col);

        _ringRenderer = _ringObj.GetComponent<MeshRenderer>();

        // 半透明マテリアル
        var mat = new Material(Shader.Find("Sprites/Default"));
        mat.color = PlayerColor;
        _ringRenderer.material = mat;

        _ringObj.SetActive(false);
    }

    private void Update()
    {
        if (_turnGenerater == null) return;

        Status selected = _turnGenerater.SelectUnit;

        if (selected != _currentTarget)
        {
            _currentTarget = selected;

            if (_currentTarget != null && _currentTarget.type == Type.Unit)
            {
                _ringObj.SetActive(true);
                Color c = _currentTarget.team == Team.Player ? PlayerColor : EnemyColor;
                _ringRenderer.material.color = c;
            }
            else
            {
                _ringObj.SetActive(false);
            }
        }

        // 位置追従 + パルスアニメーション
        if (_ringObj.activeSelf && _currentTarget != null)
        {
            Vector3 pos = _currentTarget.transform.position;
            pos.y = RingYOffset;
            _ringObj.transform.position = pos;

            // パルス（明滅）
            float pulse = Mathf.Lerp(PulseMin, PulseMax,
                (Mathf.Sin(Time.time * PulseSpeed) + 1f) * 0.5f);
            Color c = _ringRenderer.material.color;
            c.a = pulse * 0.7f;
            _ringRenderer.material.color = c;
        }
    }

    private void OnDestroy()
    {
        if (_ringObj != null) Destroy(_ringObj);
    }
}
