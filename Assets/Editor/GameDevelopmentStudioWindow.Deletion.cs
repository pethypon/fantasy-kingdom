using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class GameDevelopmentStudioWindow
{
    bool showDeleted;
    int restoreProfileIndex;
    void DrawDeletionButtons()
    {
        UnityEngine.Object selected = tab == 0 ? (UnityEngine.Object)selectedUnit : selectedBuilding;
        if (selected == null) return;
        bool deleted = StudioDeletion.IsDeleted(selected);
        if (GUILayout.Button(deleted ? "削除した設定を復元" : "登録解除して削除（復元可能）", GUILayout.Height(30)))
        {
            var archive = StudioDeletion.GetArchive(); string reason; bool success;
            if (deleted) success = StudioDeletion.TryRestore(selected, UnitWorkshopWindow.GetCatalog(), GetBuildingCatalog(), archive, out reason);
            else if (selected is UnitData unit) success = StudioDeletion.TryDeleteUnit(unit, UnitWorkshopWindow.GetCatalog(), archive, out reason);
            else success = StudioDeletion.TryDeleteBuilding((FacilityDefinitionData)selected, GetBuildingCatalog(), archive, out reason);
            notice = success ? deleted ? "設定を復元しました。「すべて保存」で保存してください。"
                : "ゲームと一覧から削除しました。元データは保管されています。Ctrl+Z、または「削除・保管した設定を表示」から復元できます。「すべて保存」で保存してください。" : reason;
            if (success && !showDeleted) { selectedUnit = null; selectedBuilding = null; }
            RefreshLists();
        }
        EditorGUILayout.LabelField("元データ・共有Prefabは消去しません。古いセーブのIDも維持します。", EditorStyles.wordWrappedMiniLabel);
    }
    void DrawProfileDeletion(BoardActionProfile profile, UnityEngine.Object owner, Action<BoardActionProfile> assign)
    {
        if (profile != null)
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("行動設定の割り当てを解除"))
            {
                Undo.RecordObject(owner, "行動設定の割り当て解除"); assign(null); EditorUtility.SetDirty(owner); GameContentRevision.NotifyChanged();
                notice = "割り当てを解除しました。行動設定データは保持され、既存役割のマス設定へ戻ります。";
            }
            if (GUILayout.Button("行動設定を削除（復元可能）"))
            {
                bool ok = StudioDeletion.TryDeleteProfile(profile, owner, StudioDeletion.GetArchive(), out string reason);
                notice = ok ? "行動設定を削除・保管しました。共有されていないデータのみ保管できます。Ctrl+Zか下の復元で取り消せます。" : reason;
            }
            EditorGUILayout.EndHorizontal();
        }
        var archive = StudioDeletion.LoadArchive(); if (archive?.entries == null) return;
        var profiles = new List<BoardActionProfile>();
        foreach (var item in archive.entries) if (item?.deleted == true && item.asset is BoardActionProfile mask) profiles.Add(mask);
        if (profiles.Count == 0) return;
        var names = new string[profiles.Count]; for (int i = 0; i < names.Length; i++) names[i] = profiles[i].displayName + " / " + profiles[i].name;
        restoreProfileIndex = EditorGUILayout.Popup("保管した行動設定", Mathf.Clamp(restoreProfileIndex, 0, profiles.Count - 1), names);
        if (GUILayout.Button("行動設定を復元して割り当て"))
        {
            bool ok = StudioDeletion.TryRestoreProfile(profiles[restoreProfileIndex], owner, archive, out string reason);
            notice = ok ? "行動設定を復元し、この設定へ割り当てました。「すべて保存」で保存してください。" : reason;
        }
    }
}
