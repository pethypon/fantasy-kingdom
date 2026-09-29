using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Optional authored overrides. Existing scenes keep their original defaults.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/Unit Catalog")]
public sealed class UnitAuthoringCatalog : ScriptableObject
{
    public const string ResourcePath = "GameContent/UnitCatalog";
    [Serializable] public sealed class Entry
    {
        public Kind kind;
        public GameObject prefab;
        public UnitData data;
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
            if (entry == null || entry.data == null || entry.prefab == null || entry.data.kind != entry.kind
                || !entry.data.IsValidForAuthoring || entry.prefab.GetComponentInChildren<Status>(true) == null) continue;
            if (!seen.Add(entry.kind)) { Debug.LogWarning($"[UnitCatalog] Duplicate kind: {entry.kind}", this); continue; }
            if (data != null) data[entry.kind] = entry.data;
            if (prefabs != null) prefabs[entry.kind] = entry.prefab;
        }
    }
}
