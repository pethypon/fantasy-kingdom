using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Editor-only soft-deletion journal. Assets and stable IDs remain recoverable.</summary>
public sealed class StudioArchiveData : ScriptableObject
{
    public enum Registration { Draft, Unit, StandardUnit, Building, StandardBuilding, ActionProfile }
    [Serializable] public sealed class Entry
    {
        public UnityEngine.Object asset;
        public string reservedId;
        public bool deleted;
        public bool previouslyRetired;
        public Registration previousRegistration;
        public GameObject previousPrefab;
        public Kind previousKind;
    }
    public List<Entry> entries = new List<Entry>();
    public Entry Find(UnityEngine.Object asset) => entries?.Find(item => item != null && item.asset == asset);
    public bool IsDeleted(UnityEngine.Object asset) => asset != null && Find(asset)?.deleted == true;
    public bool IsIdReservedByOther(string id, UnityEngine.Object owner)
        => !string.IsNullOrEmpty(id) && entries != null && entries.Exists(item => item != null && item.asset != owner
            && string.Equals(item.reservedId, id, StringComparison.Ordinal));
}
