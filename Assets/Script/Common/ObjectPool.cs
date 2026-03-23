using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 汎用オブジェクトプール。MovePoint/AttackPoint の頻繁な Instantiate/Destroy を
/// プールによる Activate/Deactivate に置き換えることでGC負荷を大幅に削減する。
/// </summary>
public class ObjectPool : MonoBehaviour
{
    public static ObjectPool Instance { get; private set; }

    private readonly Dictionary<GameObject, Queue<GameObject>> _pools
        = new Dictionary<GameObject, Queue<GameObject>>();

    private readonly Dictionary<GameObject, GameObject> _prefabLookup
        = new Dictionary<GameObject, GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>
    /// プールからオブジェクトを取得する。プールが空なら新規生成。
    /// </summary>
    public GameObject Get(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
    {
        if (!_pools.TryGetValue(prefab, out var pool))
        {
            pool = new Queue<GameObject>();
            _pools[prefab] = pool;
        }

        GameObject obj;
        if (pool.Count > 0)
        {
            obj = pool.Dequeue();
            if (obj == null)
            {
                // プール内のオブジェクトが破棄されていた場合は新規生成
                obj = Instantiate(prefab, position, rotation, parent);
                _prefabLookup[obj] = prefab;
                return obj;
            }
            obj.transform.SetParent(parent);
            obj.transform.position = position;
            obj.transform.rotation = rotation;
            obj.SetActive(true);
        }
        else
        {
            obj = Instantiate(prefab, position, rotation, parent);
            _prefabLookup[obj] = prefab;
        }
        return obj;
    }

    /// <summary>
    /// オブジェクトをプールに返却する（Destroyの代わり）。
    /// </summary>
    public void Return(GameObject obj)
    {
        if (obj == null) return;

        obj.SetActive(false);

        if (_prefabLookup.TryGetValue(obj, out var prefab))
        {
            if (!_pools.TryGetValue(prefab, out var pool))
            {
                pool = new Queue<GameObject>();
                _pools[prefab] = pool;
            }
            pool.Enqueue(obj);
        }
        else
        {
            // プール管理外のオブジェクト → 通常のDestroy
            Destroy(obj);
        }
    }

    /// <summary>
    /// 指定親の全子オブジェクトをプールに返却する（MoveReset/AtkpDestroy用）。
    /// </summary>
    public void ReturnAllChildren(Transform parent)
    {
        if (parent == null) return;
        // 逆順で走査（子を削除しても安全）
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            Return(child.gameObject);
        }
    }

    /// <summary>
    /// プレウォーム: 事前に指定数のオブジェクトを生成してプールに入れておく。
    /// </summary>
    public void Prewarm(GameObject prefab, int count, Transform parent)
    {
        if (!_pools.TryGetValue(prefab, out var pool))
        {
            pool = new Queue<GameObject>();
            _pools[prefab] = pool;
        }

        for (int i = 0; i < count; i++)
        {
            var obj = Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
            obj.SetActive(false);
            _prefabLookup[obj] = prefab;
            pool.Enqueue(obj);
        }
    }

    /// <summary>全プールをクリアし、プール内のオブジェクトを全て破棄する。</summary>
    public void ClearAll()
    {
        foreach (var kvp in _pools)
        {
            while (kvp.Value.Count > 0)
            {
                var obj = kvp.Value.Dequeue();
                if (obj != null) Destroy(obj);
            }
        }
        _pools.Clear();
        _prefabLookup.Clear();
    }
}
