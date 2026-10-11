using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Fantasy Kingdom/ゲーム制作/建物一覧")]
public sealed class FacilityAuthoringCatalog : ScriptableObject
{
    public const string ResourcesPath = "GameContent/FacilityCatalog";
    public List<FacilityDefinitionData> buildings = new List<FacilityDefinitionData>();
    [Tooltip("標準建物の値を置き換える正式データ。通常の建築メニューへ重複追加しません。")]
    public List<FacilityDefinitionData> standardOverrides = new List<FacilityDefinitionData>();
    [HideInInspector] public List<FacilityDefinitionData> retiredDefinitions = new List<FacilityDefinitionData>();
    static FacilityAuthoringCatalog loaded;
    static bool didLoad;
    readonly Dictionary<FacilityKind, FacilityDefinitionData> standardByKind = new Dictionary<FacilityKind, FacilityDefinitionData>();
    int standardRevision = -1;
    public static FacilityAuthoringCatalog Loaded
    {
        get { if (!didLoad) { loaded = Resources.Load<FacilityAuthoringCatalog>(ResourcesPath); didLoad = true; } return loaded; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetCache() { loaded = null; didLoad = false; }
    void OnValidate() { ResetCache(); GameContentRevision.NotifyChanged(); }
    public FacilityDefinitionData FindStandard(FacilityKind kind)
    {
        if (standardRevision != GameContentRevision.Revision)
        {
            standardByKind.Clear();
            if (standardOverrides != null) foreach (var definition in standardOverrides)
            {
                if (definition == null || !definition.IsValid) continue;
                if (standardByKind.ContainsKey(definition.behaviourKind)) standardByKind[definition.behaviourKind] = null;
                else standardByKind[definition.behaviourKind] = definition;
            }
            standardRevision = GameContentRevision.Revision;
        }
        return standardByKind.TryGetValue(kind, out var found) ? found : null;
    }
    public FacilityDefinitionData Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        FacilityDefinitionData found = null;
        if (!FindIn(buildings, id, ref found) || !FindIn(standardOverrides, id, ref found) || !FindIn(retiredDefinitions, id, ref found)) return null;
        return found != null && found.IsValid ? found : null;
    }
    static bool FindIn(List<FacilityDefinitionData> list, string id, ref FacilityDefinitionData found)
    {
        if (list == null) return true;
        foreach (var building in list)
        {
            if (building == null || !string.Equals(building.definitionId, id, StringComparison.Ordinal)) continue;
            if (found != null && found != building) return false;
            found = building;
        }
        return true;
    }
}
