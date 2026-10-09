using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class GameDevelopmentStudioWindow
{
    int productionSourceIndex, productionSourceLevel = 1;

    void DrawBuildingProductionBalance(SerializedProperty policy)
    {
        if (policy == null) return;
        policy.isExpanded = EditorGUILayout.Foldout(policy.isExpanded, "建物の生産量・生産周期", true);
        if (!policy.isExpanded) return;
        EditorGUILayout.HelpBox("削減値はまだ未確定です。調整を有効にしない限り、現在の生産量を維持します。入力資材・維持費・クリスタル収入は変更しません。同じ設定を実際の生産とAI予測に使います。", MessageType.Info);
        Field(policy.FindPropertyRelative("applyAdjustments"));
        var rows = policy.FindPropertyRelative("adjustments");
        var sources = new List<(FacilityKind kind, FacilityDefinitionData definition, string name)>();
        foreach (var item in FacilityData.Table)
            if (FacilityData.GetLevel(item.Key, 1).HasProduction)
                sources.Add((item.Key, null, "標準：" + item.Value.DisplayName));
        foreach (var item in buildings)
            if (item != null && item.GetLevel(1).HasProduction && item.behaviourKind != FacilityKind.SubCrystal)
                sources.Add((item.behaviourKind, item, "登録建物：" + item.displayName));
        if (sources.Count > 0)
        {
            var names = new string[sources.Count];
            for (int i = 0; i < sources.Count; i++) names[i] = sources[i].name;
            productionSourceIndex = EditorGUILayout.Popup("調整を追加する建物", Mathf.Clamp(productionSourceIndex, 0, names.Length - 1), names);
            var source = sources[productionSourceIndex];
            int maximum = source.definition != null ? source.definition.GetInfo().MaxLevel : FacilityData.GetMaxLevel(source.kind);
            productionSourceLevel = EditorGUILayout.IntSlider("対象のレベル", productionSourceLevel, 1, maximum);
            if (GUILayout.Button("現在の生産値をコピーして調整を追加"))
            {
                var recipe = source.definition != null ? source.definition.GetLevel(productionSourceLevel) : FacilityData.GetLevel(source.kind, productionSourceLevel);
                int index = FindProductionAdjustment(rows, source.kind, source.definition?.definitionId, productionSourceLevel);
                if (index < 0) { index = rows.arraySize; rows.InsertArrayElementAtIndex(index); }
                var entry = rows.GetArrayElementAtIndex(index);
                entry.FindPropertyRelative("enabled").boolValue = true;
                entry.FindPropertyRelative("buildingType").intValue = (int)source.kind;
                entry.FindPropertyRelative("definitionId").stringValue = source.definition?.definitionId ?? string.Empty;
                entry.FindPropertyRelative("level").intValue = productionSourceLevel;
                entry.FindPropertyRelative("intervalTurns").intValue = FacilityData.ProductionInterval(recipe);
                SetProductionBundle(entry.FindPropertyRelative("output"), recipe.Output);
                SetProductionBundle(entry.FindPropertyRelative("bonusOutput1"), recipe.BonusOutput1);
                SetProductionBundle(entry.FindPropertyRelative("bonusOutput2"), recipe.BonusOutput2);
                entry.isExpanded = true;
            }
        }
        for (int i = 0; i < rows.arraySize; i++)
        {
            var entry = rows.GetArrayElementAtIndex(i);
            var kind = (FacilityKind)entry.FindPropertyRelative("buildingType").intValue;
            string title = FacilityData.Table.TryGetValue(kind, out var info) ? info.DisplayName : "未設定";
            string id = entry.FindPropertyRelative("definitionId").stringValue;
            if (!string.IsNullOrEmpty(id)) foreach (var item in buildings)
                if (item != null && item.definitionId == id) { title = item.displayName; break; }
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.BeginHorizontal();
            entry.isExpanded = EditorGUILayout.Foldout(entry.isExpanded, title + " Lv" + entry.FindPropertyRelative("level").intValue, true);
            bool remove = GUILayout.Button("削除", GUILayout.Width(55));
            EditorGUILayout.EndHorizontal();
            if (entry.isExpanded)
            {
                foreach (string name in new[] { "enabled", "buildingType", "definitionId", "level", "intervalTurns" }) Field(entry.FindPropertyRelative(name));
                DrawBundle(entry.FindPropertyRelative("output"), "１回の確定生産量");
                DrawBundle(entry.FindPropertyRelative("bonusOutput1"), "確率生産１の量（発生率は元の定義）");
                DrawBundle(entry.FindPropertyRelative("bonusOutput2"), "確率生産２の量（発生率は元の定義）");
            }
            EditorGUILayout.EndVertical();
            if (remove) { rows.DeleteArrayElementAtIndex(i); break; }
        }
    }

    static int FindProductionAdjustment(SerializedProperty rows, FacilityKind kind, string id, int level)
    {
        for (int i = 0; i < rows.arraySize; i++)
        {
            var row = rows.GetArrayElementAtIndex(i);
            if (row.FindPropertyRelative("buildingType").intValue == (int)kind
                && row.FindPropertyRelative("definitionId").stringValue == (id ?? string.Empty)
                && row.FindPropertyRelative("level").intValue == level) return i;
        }
        return -1;
    }

    static void SetProductionBundle(SerializedProperty row, FacilityData.ProductionBundle value)
    {
        var names = new[] { "Wood", "Stone", "Iron", "MagicOre", "Wheat", "Bread", "Water", "Citizen" };
        var amounts = new[] { value.Wood, value.Stone, value.Iron, value.MagicOre, value.Wheat, value.Bread, value.Water, value.Citizen };
        for (int i = 0; i < names.Length; i++) row.FindPropertyRelative(names[i]).intValue = amounts[i];
    }
}
