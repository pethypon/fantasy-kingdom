using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>Undoable catalog retirement; never deletes assets, shared prefabs, or models.</summary>
[InitializeOnLoad]
public static class StudioDeletion
{
    static StudioDeletion() => Undo.undoRedoPerformed += RefreshCaches;
    static void RefreshCaches() { FacilityAuthoringCatalog.ResetCache(); GameContentRevision.NotifyChanged(); }
    public const string ArchivePath = GameDevelopmentStudioWindow.ContentFolder + "/StudioArchive.asset";
    public static StudioArchiveData LoadArchive() => AssetDatabase.LoadAssetAtPath<StudioArchiveData>(ArchivePath);
    public static StudioArchiveData GetArchive()
    {
        var archive = LoadArchive(); if (archive != null) return archive;
        UnitWorkshopWindow.EnsureFolder(GameDevelopmentStudioWindow.ContentFolder);
        archive = ScriptableObject.CreateInstance<StudioArchiveData>(); AssetDatabase.CreateAsset(archive, ArchivePath);
        return archive;
    }
    public static bool IsDeleted(UnityEngine.Object asset) => LoadArchive()?.IsDeleted(asset) == true;
    public static void RequireAvailableId(string id, UnityEngine.Object owner, StudioArchiveData archive = null)
    {
        if (archive == null) archive = LoadArchive();
        var existing = archive?.Find(owner);
        if (existing != null && !string.Equals(existing.reservedId ?? string.Empty, id ?? string.Empty, StringComparison.Ordinal))
            throw new ArgumentException("保管済みの保存用IDは変更できません。新しい設定は複製して作成してください。");
        if (archive?.IsIdReservedByOther(id, owner) == true)
            throw new ArgumentException("削除・保管した設定の保存用IDは再利用できません。複製機能で新しい設定を作成してください。");
    }
    public static void MarkRegistered(UnityEngine.Object asset)
    {
        var archive = LoadArchive(); var entry = archive?.Find(asset); if (entry == null || !entry.deleted) return;
        Undo.RecordObject(archive, "保管した設定を再登録"); entry.deleted = false; EditorUtility.SetDirty(archive);
    }
    static string Id(UnityEngine.Object asset) => asset is UnitData unit ? unit.definitionId
        : asset is FacilityDefinitionData building ? building.definitionId : string.Empty;
    static string ProtectedReason(UnityEngine.Object asset)
    {
        if (asset == null) return "削除する設定を選択してください。";
        string path = AssetDatabase.GetAssetPath(asset);
        if (path.StartsWith(GameDevelopmentStudioWindow.ContentFolder + "/", StringComparison.Ordinal)
            || path.StartsWith(UnitWorkshopWindow.UnitsFolder + "/", StringComparison.Ordinal)) return null;
        return "標準データ・外部アセットは削除できません。開発スタジオで作成した設定だけが対象です。";
    }
    public static string ReferenceFailure(UnityEngine.Object asset, params UnityEngine.Object[] allowedOwners)
    {
        var path = AssetDatabase.GetAssetPath(asset); if (string.IsNullOrEmpty(path)) return "設定を先に保存してください。";
        var allowed = new HashSet<string>(StringComparer.Ordinal) { path };
        if (allowedOwners != null) foreach (var owner in allowedOwners)
            if (owner != null) allowed.Add(AssetDatabase.GetAssetPath(owner));
        if (!EditorApplication.isPlaying)
            foreach (var behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || EditorUtility.IsPersistent(behaviour) || !behaviour.gameObject.scene.IsValid()) continue;
                var serialized = new SerializedObject(behaviour); var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == asset)
                        return "削除できません。開いているシーンの「" + behaviour.name + "」がこの設定を使用しています。先に参照を外してください。";
            }
        foreach (string candidate in AssetDatabase.GetAllAssetPaths())
        {
            if (!candidate.StartsWith("Assets/", StringComparison.Ordinal) || allowed.Contains(candidate) || AssetDatabase.IsValidFolder(candidate)) continue;
            // Direct references only: an authored prefab referring back to its own definition is permitted.
            foreach (string dependency in AssetDatabase.GetDependencies(candidate, false))
                if (string.Equals(dependency, path, StringComparison.Ordinal))
                    return "削除できません。「" + candidate + "」がこの設定を使用しています。先にその参照を外してください。";
        }
        return null;
    }
    static StudioArchiveData.Entry Archive(StudioArchiveData archive, UnityEngine.Object asset, StudioArchiveData.Registration registration,
        GameObject prefab = null, Kind kind = Kind.None, bool previouslyRetired = false)
    {
        if (archive.entries == null) archive.entries = new List<StudioArchiveData.Entry>();
        var entry = archive.Find(asset);
        if (entry == null)
        { entry = new StudioArchiveData.Entry { asset = asset, reservedId = Id(asset) }; archive.entries.Add(entry); }
        entry.deleted = true; entry.previouslyRetired = previouslyRetired;
        entry.previousRegistration = registration; entry.previousPrefab = prefab; entry.previousKind = kind;
        return entry;
    }
    static int BeginUndo(string label, params UnityEngine.Object[] objects)
    {
        Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup(); Undo.SetCurrentGroupName(label);
        foreach (var item in objects) if (item != null) Undo.RecordObject(item, label);
        return group;
    }
    static void Complete(int group, params UnityEngine.Object[] objects)
    {
        foreach (var item in objects) if (item != null) EditorUtility.SetDirty(item);
        Undo.CollapseUndoOperations(group); Undo.FlushUndoRecordObjects();
        FacilityAuthoringCatalog.ResetCache(); GameContentRevision.NotifyChanged();
    }
    public static bool TryDeleteUnit(UnitData data, UnitAuthoringCatalog catalog, StudioArchiveData archive, out string reason)
    {
        reason = ProtectedReason(data); if (reason != null) return false;
        if (catalog == null || archive == null) { reason = "登録一覧と保管データが必要です。"; return false; }
        if (archive.IsDeleted(data)) { reason = "この設定はすでに削除・保管されています。"; return false; }
        var prior = catalog.units?.Find(entry => entry?.data == data);
        var retired = catalog.retiredUnits?.Find(entry => entry?.data == data);
        var previousEntry = prior ?? retired;
        reason = ReferenceFailure(data, catalog, archive, data.prefab, previousEntry?.prefab); if (reason != null) return false;
        if (previousEntry != null && !data.IsValidForAuthoring) { reason = "登録済みの設定にエラーがあります。能力・費用を修正してから削除してください。"; return false; }
        int group = BeginUndo("駒を登録解除して削除", catalog, archive);
        var registration = previousEntry == null ? StudioArchiveData.Registration.Draft : previousEntry.standaloneDefinition
            ? StudioArchiveData.Registration.Unit : StudioArchiveData.Registration.StandardUnit;
        Archive(archive, data, registration, previousEntry?.prefab, previousEntry?.kind ?? data.kind, prior == null && retired != null);
        catalog.units?.RemoveAll(entry => entry?.data == data);
        if (catalog.retiredUnits == null) catalog.retiredUnits = new List<UnitAuthoringCatalog.Entry>();
        if (!string.IsNullOrWhiteSpace(data.definitionId) && data.IsValidForAuthoring && (data.prefab != null || previousEntry?.prefab != null)
            && !catalog.retiredUnits.Exists(entry => entry?.data == data))
            catalog.retiredUnits.Add(prior ?? new UnitAuthoringCatalog.Entry { kind = data.kind, data = data, prefab = data.prefab, standaloneDefinition = true });
        Complete(group, catalog, archive); return true;
    }
    public static bool TryDeleteBuilding(FacilityDefinitionData data, FacilityAuthoringCatalog catalog, StudioArchiveData archive, out string reason)
    {
        reason = ProtectedReason(data); if (reason != null) return false;
        if (catalog == null || archive == null) { reason = "登録一覧と保管データが必要です。"; return false; }
        if (archive.IsDeleted(data)) { reason = "この設定はすでに削除・保管されています。"; return false; }
        reason = ReferenceFailure(data, catalog, archive, data.prefab); if (reason != null) return false;
        bool standard = catalog.standardOverrides?.Contains(data) == true;
        bool independent = catalog.buildings?.Contains(data) == true;
        if ((standard || independent) && !data.IsValid) { reason = "登録済みの設定にエラーがあります。レベル設定を修正してから削除してください。"; return false; }
        int group = BeginUndo("建物を登録解除して削除", catalog, archive);
        Archive(archive, data, standard ? StudioArchiveData.Registration.StandardBuilding
            : independent ? StudioArchiveData.Registration.Building : StudioArchiveData.Registration.Draft,
            previouslyRetired: !standard && !independent && catalog.retiredDefinitions?.Contains(data) == true);
        catalog.buildings?.Remove(data); catalog.standardOverrides?.Remove(data);
        if (catalog.retiredDefinitions == null) catalog.retiredDefinitions = new List<FacilityDefinitionData>();
        if (data.IsValid && !catalog.retiredDefinitions.Contains(data)) catalog.retiredDefinitions.Add(data);
        Complete(group, catalog, archive); return true;
    }
    public static bool TryRestore(UnityEngine.Object asset, UnitAuthoringCatalog units, FacilityAuthoringCatalog buildings,
        StudioArchiveData archive, out string reason)
    {
        reason = null; var entry = archive?.Find(asset);
        if (entry == null || !entry.deleted) { reason = "復元する保管済み設定を選択してください。"; return false; }
        if (!string.Equals(entry.reservedId ?? string.Empty, Id(asset) ?? string.Empty, StringComparison.Ordinal))
        { reason = "保管時の保存用IDが変更されています。元のIDへ戻してから復元してください。"; return false; }
        if (asset is UnitData unit)
        {
            if (units == null) { reason = "駒の登録一覧がありません。"; return false; }
            if ((units.units != null && units.units.Exists(item => item?.data != null && item.data != unit && item.data.definitionId == unit.definitionId))
                || (units.retiredUnits != null && units.retiredUnits.Exists(item => item?.data != null && item.data != unit && item.data.definitionId == unit.definitionId)))
            { reason = "同じ保存用IDの駒が存在するため復元できません。IDを使い回さず、新しい設定は複製して作成してください。"; return false; }
            if (!entry.previouslyRetired && entry.previousRegistration == StudioArchiveData.Registration.StandardUnit && units.units != null
                && units.units.Exists(item => item != null && !item.standaloneDefinition && item.kind == entry.previousKind && item.data != unit))
            { reason = "同じ標準兵種に別の設定が登録されています。先にその登録を解除してください。"; return false; }
            int group = BeginUndo("削除した駒を復元", units, archive);
            if (!entry.previouslyRetired) units.retiredUnits?.RemoveAll(item => item?.data == unit);
            if (entry.previouslyRetired && units.retiredUnits == null) units.retiredUnits = new List<UnitAuthoringCatalog.Entry>();
            if (!entry.previouslyRetired && units.units == null) units.units = new List<UnitAuthoringCatalog.Entry>();
            var restoreList = entry.previouslyRetired ? units.retiredUnits : units.units;
            if (entry.previousRegistration != StudioArchiveData.Registration.Draft && !restoreList.Exists(item => item?.data == unit))
                restoreList.Add(new UnitAuthoringCatalog.Entry { kind = entry.previousKind, data = unit,
                    prefab = entry.previousPrefab != null ? entry.previousPrefab : unit.prefab,
                    standaloneDefinition = entry.previousRegistration == StudioArchiveData.Registration.Unit });
            entry.deleted = false; Complete(group, units, archive); return true;
        }
        if (asset is FacilityDefinitionData building)
        {
            if (buildings == null) { reason = "建物の登録一覧がありません。"; return false; }
            foreach (var list in new[] { buildings.buildings, buildings.standardOverrides, buildings.retiredDefinitions })
                if (list != null && list.Exists(item => item != null && item != building && item.definitionId == building.definitionId))
                { reason = "同じ保存用IDの建物が存在するため復元できません。IDを使い回さず、新しい設定は複製して作成してください。"; return false; }
            if (!entry.previouslyRetired && entry.previousRegistration == StudioArchiveData.Registration.StandardBuilding && buildings.standardOverrides != null
                && buildings.standardOverrides.Exists(item => item != null && item != building && item.behaviourKind == building.behaviourKind))
            { reason = "同じ標準建物に別の設定が登録されています。先にその登録を解除してください。"; return false; }
            int group = BeginUndo("削除した建物を復元", buildings, archive);
            if (entry.previouslyRetired)
            { if (buildings.retiredDefinitions == null) buildings.retiredDefinitions = new List<FacilityDefinitionData>(); if (!buildings.retiredDefinitions.Contains(building)) buildings.retiredDefinitions.Add(building); }
            else buildings.retiredDefinitions?.Remove(building);
            if (!entry.previouslyRetired && entry.previousRegistration == StudioArchiveData.Registration.StandardBuilding)
            { if (buildings.standardOverrides == null) buildings.standardOverrides = new List<FacilityDefinitionData>(); if (!buildings.standardOverrides.Contains(building)) buildings.standardOverrides.Add(building); }
            else if (!entry.previouslyRetired && entry.previousRegistration == StudioArchiveData.Registration.Building)
            { if (buildings.buildings == null) buildings.buildings = new List<FacilityDefinitionData>(); if (!buildings.buildings.Contains(building)) buildings.buildings.Add(building); }
            entry.deleted = false; Complete(group, buildings, archive); return true;
        }
        reason = "この設定の種類はここから復元できません。"; return false;
    }
    public static bool TryDeleteProfile(BoardActionProfile profile, UnityEngine.Object owner, StudioArchiveData archive, out string reason)
    {
        reason = ProtectedReason(profile); if (reason != null) return false;
        if (!(owner is UnitData) && !(owner is FacilityDefinitionData)) { reason = "行動設定を割り当てた駒か建物を選択してください。"; return false; }
        if (archive == null) { reason = "保管データが必要です。"; return false; }
        if (archive.IsDeleted(profile)) { reason = "この行動設定はすでに保管されています。"; return false; }
        var assigned = owner is UnitData unitOwner ? unitOwner.actionProfile : ((FacilityDefinitionData)owner).actionProfile;
        if (assigned != profile) { reason = "選択した設定へ割り当てられている行動設定だけを削除できます。"; return false; }
        reason = ReferenceFailure(profile, owner, archive); if (reason != null) return false;
        int group = BeginUndo("行動設定を解除して削除", owner, archive);
        SetProfile(owner, null); Archive(archive, profile, StudioArchiveData.Registration.ActionProfile);
        Complete(group, owner, archive); return true;
    }
    public static bool TryRestoreProfile(BoardActionProfile profile, UnityEngine.Object owner, StudioArchiveData archive, out string reason)
    {
        reason = null; var entry = archive?.Find(profile);
        if (entry?.deleted != true || (!(owner is UnitData) && !(owner is FacilityDefinitionData)))
        { reason = "保管済みの行動設定と割り当て先を選択してください。"; return false; }
        int group = BeginUndo("削除した行動設定を復元", owner, archive);
        SetProfile(owner, profile); entry.deleted = false; Complete(group, owner, archive); return true;
    }
    static void SetProfile(UnityEngine.Object owner, BoardActionProfile profile)
    { if (owner is UnitData unit) unit.actionProfile = profile; else if (owner is FacilityDefinitionData building) building.actionProfile = profile; }
}
