using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional authored overrides. Existing scenes keep their original defaults.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/駒と建物/駒一覧")]
public sealed class UnitAuthoringCatalog : ScriptableObject
{
    public const string ResourcePath = "GameContent/UnitCatalog";
    [Serializable] public sealed class Entry
    {
        public Kind kind;
        public GameObject prefab;
        public UnitData data;
        [Tooltip("独立した駒として追加。既存兵種を上書きしません。")]
        public bool standaloneDefinition;
    }
    public List<Entry> units = new List<Entry>();
    public static UnitAuthoringCatalog Load() => Resources.Load<UnitAuthoringCatalog>(ResourcePath);

    public void Apply(Dictionary<Kind, UnitData> data, Dictionary<Kind, GameObject> prefabs)
    {
        if (units == null) return;
        var seen = new HashSet<Kind>();
        foreach (var entry in units)
        {
            // Invalid entries never replace a working legacy configuration.
            if (entry == null || entry.standaloneDefinition || entry.data == null || entry.prefab == null || entry.data.kind != entry.kind
                || !entry.data.IsValidForAuthoring || entry.prefab.GetComponentInChildren<Status>(true) == null) continue;
            if (!seen.Add(entry.kind)) { Debug.LogWarning($"[UnitCatalog] Duplicate kind: {entry.kind}", this); continue; }
            if (data != null) data[entry.kind] = entry.data;
            if (prefabs != null) prefabs[entry.kind] = entry.prefab;
        }
    }

    static bool ValidDefinition(Entry entry) => entry != null && entry.data != null
        && entry.data.IsValidForAuthoring && entry.kind == entry.data.kind
        && !string.IsNullOrWhiteSpace(entry.data.definitionId)
        && (entry.data.prefab != null || entry.prefab != null);

    public UnitData GetById(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || units == null) return null;
        foreach (var entry in units)
            if (ValidDefinition(entry) && string.Equals(entry.data.definitionId, id, StringComparison.Ordinal)) return entry.data;
        return null;
    }

    public bool TryGetPrefab(UnitData data, out GameObject prefab)
    {
        prefab = data != null ? data.prefab : null;
        if (prefab != null) return true;
        if (data == null || units == null) return false;
        foreach (var entry in units)
            if (entry != null && entry.data == data && entry.prefab != null) { prefab = entry.prefab; return true; }
        return false;
    }

    /// <summary>Independent IDs permit multiple definitions using the same AI role.</summary>
    public IEnumerable<UnitData> EnumerateStandaloneDefinitions(Team? team = null)
    {
        if (units == null) yield break;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in units)
        {
            if (!ValidDefinition(entry) || !ids.Add(entry.data.definitionId) || !entry.standaloneDefinition) continue;
            if (team.HasValue && !entry.data.AvailableFor(team.Value)) continue;
            yield return entry.data;
        }
    }
}
