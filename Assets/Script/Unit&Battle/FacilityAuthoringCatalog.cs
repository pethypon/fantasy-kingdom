using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Fantasy Kingdom/ゲーム制作/建物一覧")]
public sealed class FacilityAuthoringCatalog : ScriptableObject
{
    public const string ResourcesPath = "GameContent/FacilityCatalog";
    public List<FacilityDefinitionData> buildings = new List<FacilityDefinitionData>();
    static FacilityAuthoringCatalog loaded;
    static bool didLoad;
    public static FacilityAuthoringCatalog Loaded
    {
        get { if (!didLoad) { loaded = Resources.Load<FacilityAuthoringCatalog>(ResourcesPath); didLoad = true; } return loaded; }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetCache() { loaded = null; didLoad = false; }
    void OnValidate() => ResetCache();
    public FacilityDefinitionData Find(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || buildings == null) return null;
        FacilityDefinitionData found = null;
        foreach (var building in buildings)
        {
            if (building == null || !string.Equals(building.definitionId, id, StringComparison.Ordinal)) continue;
            if (found != null) { Debug.LogError("[建物設定] ID が重複しています: " + id); return null; }
            found = building;
        }
        return found != null && found.IsValid ? found : null;
    }
}
