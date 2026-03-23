using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dキーを押している間、敵ユニットの攻撃範囲をオーバーレイ表示する。
/// 赤い半透明のマーカーで危険な位置を可視化する。
/// </summary>
public class DangerZoneUI : MonoBehaviour
{
    public static DangerZoneUI Instance { get; private set; }

    TurnGenerater _turnGen;
    UnitSetting _unitSet;
    MoveGererater _moveGen;
    VisionGenerater _visionGen;

    readonly List<GameObject> _markers = new List<GameObject>();
    Material _dangerMat;
    bool _showing;

    public void Init(TurnGenerater turnGen, UnitSetting unitSet, MoveGererater moveGen, VisionGenerater visionGen)
    {
        _turnGen = turnGen;
        _unitSet = unitSet;
        _moveGen = moveGen;
        _visionGen = visionGen;
    }

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;

        // 半透明赤のマテリアル
        _dangerMat = new Material(Shader.Find("Sprites/Default"));
        _dangerMat.color = new Color(0.9f, 0.15f, 0.1f, 0.25f);
    }

    void Update()
    {
        if (_turnGen == null || _unitSet == null) return;
        if (UnityEngine.InputSystem.Keyboard.current == null) return;

        bool dPressed = UnityEngine.InputSystem.Keyboard.current.dKey.isPressed;

        if (dPressed && !_showing)
        {
            _showing = true;
            ShowDangerZone();
        }
        else if (!dPressed && _showing)
        {
            _showing = false;
            ClearMarkers();
        }
    }

    void ShowDangerZone()
    {
        ClearMarkers();

        var dangerCells = new HashSet<Vector3Int>();
        var enemyParent = _unitSet.EnemyUnit;
        if (enemyParent == null) return;

        foreach (Status enemy in enemyParent.GetComponentsInChildren<Status>())
        {
            if (enemy == null || !enemy.gameObject.activeInHierarchy) continue;
            if (enemy.type != Type.Unit) continue;
            if (enemy.HP <= 0) continue;

            // 視界外の敵はスキップ
            if (_visionGen != null && _visionGen.PlayerVisionBox != null)
            {
                var enemyCell = new Vector3Int(
                    Mathf.RoundToInt(enemy.transform.position.x), 0,
                    Mathf.RoundToInt(enemy.transform.position.z));
                if (!_visionGen.PlayerVisionBox.Contains(enemyCell)) continue;
            }

            // 攻撃範囲を取得（AttackPatterns使用）
            if (!AttackPatterns.NormalMap.TryGetValue(enemy.kind, out var predicate)) continue;
            bool dirIndep = AttackPatterns.DirectionIndependent.Contains(enemy.kind);
            int dirZ = MovePatterns.DirZ(enemy.direction);

            // 移動範囲 + 攻撃範囲の組み合わせで脅威セルを計算
            // まず攻撃範囲のみ（現在地からの攻撃）
            AddAttackCells(enemy, predicate, dirIndep, dirZ, enemy.transform.position, dangerCells);

            // 移動可能先からの攻撃も考慮（1手先の脅威）
            if (MovePatterns.Map.TryGetValue(enemy.kind, out var movePred))
            {
                bool moveIndep = MovePatterns.DirectionIndependent.Contains(enemy.kind);
                var ePos = enemy.transform.position;
                foreach (var tile in _moveGen.mapcreate.SetPos)
                {
                    float dx = tile.x - ePos.x;
                    float dz = tile.z - ePos.z;
                    float dzAdj = moveIndep ? dz : dz * dirZ;
                    if (!movePred(dx, dzAdj)) continue;

                    // 移動先からの攻撃範囲
                    AddAttackCells(enemy, predicate, dirIndep, dirZ, tile, dangerCells);
                }
            }
        }

        // マーカー生成
        foreach (var cell in dangerCells)
        {
            var marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = "DangerMarker";
            marker.transform.position = new Vector3(cell.x, 0.52f, cell.z);
            marker.transform.rotation = Quaternion.Euler(90, 0, 0);
            marker.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

            var renderer = marker.GetComponent<Renderer>();
            renderer.material = _dangerMat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // コリジョン不要
            var col = marker.GetComponent<Collider>();
            if (col != null) Destroy(col);

            _markers.Add(marker);
        }
    }

    void AddAttackCells(Status enemy, System.Func<float, float, bool> predicate,
        bool dirIndep, int dirZ, Vector3 fromPos, HashSet<Vector3Int> result)
    {
        foreach (var tile in _moveGen.mapcreate.SetPos)
        {
            float dx = tile.x - fromPos.x;
            float dz = tile.z - fromPos.z;
            float dzAdj = dirIndep ? dz : dz * dirZ;
            if (predicate(dx, dzAdj))
            {
                result.Add(new Vector3Int(
                    Mathf.RoundToInt(tile.x), 0,
                    Mathf.RoundToInt(tile.z)));
            }
        }
    }

    void ClearMarkers()
    {
        for (int i = 0; i < _markers.Count; i++)
        {
            if (_markers[i] != null) Destroy(_markers[i]);
        }
        _markers.Clear();
    }

    void OnDestroy()
    {
        ClearMarkers();
        if (_dangerMat != null) Destroy(_dangerMat);
    }
}
