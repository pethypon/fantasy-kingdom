using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ユニットの Status コンポーネントをキャッシュし、
/// GetComponentsInChildren の頻繁な呼び出しを排除するレジストリ。
/// ユニットの追加・削除時に Register/Unregister を呼ぶ。
/// </summary>
public class UnitRegistry : MonoBehaviour
{
    public static UnitRegistry Instance { get; private set; }

    private readonly List<Status> _playerUnits = new List<Status>();
    private readonly List<Status> _enemyUnits = new List<Status>();
    private readonly List<Status> _playerBuildings = new List<Status>();
    private readonly List<Status> _enemyBuildings = new List<Status>();

    // 読み取り専用アクセス
    public IReadOnlyList<Status> PlayerUnits => _playerUnits;
    public IReadOnlyList<Status> EnemyUnits => _enemyUnits;
    public IReadOnlyList<Status> PlayerBuildings => _playerBuildings;
    public IReadOnlyList<Status> EnemyBuildings => _enemyBuildings;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>ユニットを登録する</summary>
    public void Register(Status unit)
    {
        if (unit == null) return;
        var list = GetList(unit.team, unit.type);
        if (list != null && !list.Contains(unit))
            list.Add(unit);
    }

    /// <summary>ユニットを登録解除する</summary>
    public void Unregister(Status unit)
    {
        if (unit == null) return;
        var list = GetList(unit.team, unit.type);
        list?.Remove(unit);
    }

    /// <summary>初期化時に Transform 以下を走査して全て登録する</summary>
    public void ScanAndRegister(Transform playerUnitParent, Transform enemyUnitParent,
                                 Transform playerBuildParent = null, Transform enemyBuildParent = null)
    {
        _playerUnits.Clear();
        _enemyUnits.Clear();
        _playerBuildings.Clear();
        _enemyBuildings.Clear();

        ScanTransform(playerUnitParent);
        ScanTransform(enemyUnitParent);
        if (playerBuildParent != null) ScanTransform(playerBuildParent);
        if (enemyBuildParent != null) ScanTransform(enemyBuildParent);
    }

    private void ScanTransform(Transform parent)
    {
        if (parent == null) return;
        foreach (Transform child in parent)
        {
            if (child == null || !child.gameObject.activeInHierarchy) continue;
            Status s = child.GetComponent<Status>();
            if (s == null) s = child.GetComponentInChildren<Status>();
            if (s != null) Register(s);
        }
    }

    /// <summary>指定チーム・タイプのアクティブなユニット数を返す</summary>
    public int CountActive(Team team, Type type)
    {
        var list = GetList(team, type);
        if (list == null) return 0;
        int count = 0;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && list[i].gameObject.activeInHierarchy)
                count++;
        }
        return count;
    }

    /// <summary>指定チームのアクティブなユニットを列挙する（GC-free的にListを返す）</summary>
    public List<Status> GetActiveUnits(Team team)
    {
        var source = team == Team.Player ? _playerUnits : _enemyUnits;
        var result = new List<Status>(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] != null && source[i].gameObject.activeInHierarchy)
                result.Add(source[i]);
        }
        return result;
    }

    /// <summary>非アクティブになったエントリを除去する定期メンテナンス</summary>
    public void CleanUp()
    {
        CleanList(_playerUnits);
        CleanList(_enemyUnits);
        CleanList(_playerBuildings);
        CleanList(_enemyBuildings);
    }

    private static void CleanList(List<Status> list)
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null || !list[i].gameObject.activeInHierarchy)
                list.RemoveAt(i);
        }
    }

    private List<Status> GetList(Team team, Type type)
    {
        if (type == Type.Building || type == Type.Wall)
        {
            return team == Team.Player ? _playerBuildings : _enemyBuildings;
        }
        if (type == Type.Unit)
        {
            return team == Team.Player ? _playerUnits : _enemyUnits;
        }
        return null;
    }
}
