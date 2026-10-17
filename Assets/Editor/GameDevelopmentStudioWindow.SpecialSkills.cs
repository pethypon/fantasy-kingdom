using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed partial class GameDevelopmentStudioWindow
{
    bool specialSkillFold;
    int specialTemplateRole;
    static readonly Kind[] SpecialTemplateRoles = { Kind.WhiteMage, Kind.HolyKnight, Kind.Samurai, Kind.Ninja, Kind.Berserker, Kind.Navigator };
    static readonly string[] SpecialTemplateLabels = { "白魔導士", "聖騎士", "侍", "忍者", "狂戦士", "航海士" };

    void DrawUnitSpecialSkill(SerializedObject unitObject)
    {
        specialSkillFold = EditorGUILayout.Foldout(specialSkillFold, "駒固有能力（複数ターン・生命接続・爆弾など）", true);
        if (!specialSkillFold) return;
        EditorGUILayout.PropertyField(unitObject.FindProperty("attackTags"), new GUIContent("攻撃の属性タグ（岩場の石属性補正など）"));
        var reference = unitObject.FindProperty("specialSkill");
        if (reference == null) return;
        EditorGUILayout.PropertyField(reference, new GUIContent("駒固有能力の設定"));
        if (reference.objectReferenceValue == null)
        {
            EditorGUILayout.HelpBox("従来スキルとは別系統です。未確定のAP・倍率・範囲等は未設定のまま保管し、設定が揃うまで発動できません。既存のスキルはそのまま使えます。", MessageType.Info);
            if (GUILayout.Button("この駒専用の固有能力設定を作成"))
            {
                unitObject.ApplyModifiedProperties();
                var definition = CreateSpecialSkill(selectedUnit, UnitSpecialSkill.None);
                Undo.RecordObject(selectedUnit, "駒固有能力を設定"); selectedUnit.specialSkill = definition;
                EditorUtility.SetDirty(selectedUnit); unitObject.Update();
            }
        }
        else
        {
            var definition = (SpecialSkillDefinition)reference.objectReferenceValue;
            DrawSpecialDefinition(definition);
            if (GUILayout.Button("設定を複製して、この駒だけで編集"))
            {
                unitObject.ApplyModifiedProperties();
                string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(selectedUnit)).Replace('\\', '/');
                Undo.RecordObject(selectedUnit, "固有能力設定を独立");
                selectedUnit.specialSkill = DuplicateSpecialSkill(definition, folder);
                EditorUtility.SetDirty(selectedUnit); unitObject.Update();
            }
        }
        EditorGUILayout.Space(5);
        EditorGUILayout.LabelField("追加の特殊役割を、選択中の駒から複製", EditorStyles.boldLabel);
        specialTemplateRole = EditorGUILayout.Popup("作成する役割", specialTemplateRole, SpecialTemplateLabels);
        if (GUILayout.Button("能力値・見た目・行動マスを引き継いで新しい駒を作成"))
            TryAction(() => CreateSpecialRoleCopy(specialTemplateRole));
        EditorGUILayout.HelpBox("新しい6役割のHP・攻撃力・移動マス等は指定されていません。選択中の駒から複製し、下の能力値・行動マス・固有能力を編集してください。元の駒は変わりません。", MessageType.None);
    }

    void CreateSpecialRoleCopy(int index)
    {
        if (selectedUnit == null || index < 0 || index >= SpecialTemplateRoles.Length) throw new ArgumentException("複製元の駒を選択してください。");
        // Resolve fallback masks while the source Kind still identifies their rules.
        // Changing Kind first would leave the new roles without an ordinary attack.
        var inherited = CaptureSpecialRoleProfile(selectedUnit);
        try
        {
            DuplicateSelected();
            EditorUtility.CopySerialized(inherited, selectedUnit.actionProfile);
            EditorUtility.SetDirty(selectedUnit.actionProfile);
        }
        finally { DestroyImmediate(inherited); }
        selectedUnit.kind = SpecialTemplateRoles[index]; selectedUnit.displayName = SpecialTemplateLabels[index];
        var ability = index switch
        {
            0 => UnitSpecialSkill.WhiteMageHeal, 1 => UnitSpecialSkill.HolyKnightSanctuary,
            2 => UnitSpecialSkill.SamuraiCounter, 3 => UnitSpecialSkill.NinjaSwap,
            4 => UnitSpecialSkill.BerserkerRage, _ => UnitSpecialSkill.NavigatorPull
        };
        // The inherited definition is already an independent asset. Reset that copy so
        // a different ability never inherits an undecided AP/curve from the source role.
        if (selectedUnit.specialSkill != null)
        {
            var defaults = CreateInstance<SpecialSkillDefinition>();
            try
            {
                defaults.ability = ability; defaults.displayName = SpecialSkillPresentation.Name(ability);
                defaults.ApplySpecifiedAPCost();
                EditorUtility.CopySerialized(defaults, selectedUnit.specialSkill);
                EditorUtility.SetDirty(selectedUnit.specialSkill);
            }
            finally { DestroyImmediate(defaults); }
        }
        else selectedUnit.specialSkill = CreateSpecialSkill(selectedUnit, ability);
        string prefabPath = AssetDatabase.GetAssetPath(selectedUnit.prefab);
        var prefab = PrefabUtility.LoadPrefabContents(prefabPath);
        try
        {
            prefab.GetComponentInChildren<Status>(true).kind = selectedUnit.kind;
            PrefabUtility.SaveAsPrefabAsset(prefab, prefabPath);
        }
        finally { PrefabUtility.UnloadPrefabContents(prefab); }
        EditorUtility.SetDirty(selectedUnit); RegisterUnit(selectedUnit); RefreshLists();
        if (index == 3 && !HasRangedAttack(selectedUnit.actionProfile))
            ShowNotification(new GUIContent("忍者の攻撃マスは複製元の近接設定です。クナイ用に遠距離マスを設定してください。"));
    }

    static BoardActionProfile CaptureSpecialRoleProfile(UnitData source)
    {
        var original = source.actionProfile;
        var profile = original != null ? Instantiate(original) : CreateInstance<BoardActionProfile>();
        try
        {
            profile.movement = CaptureSpecialRolePattern(original?.movement, source.kind, 0);
            profile.attack = CaptureSpecialRolePattern(original?.attack, source.kind, 1);
            profile.vision = CaptureSpecialRolePattern(original?.vision, source.kind, 2);
            return profile;
        }
        catch { DestroyImmediate(profile); throw; }
    }

    static BoardTilePattern CaptureSpecialRolePattern(BoardTilePattern source, Kind kind, int mode)
    {
        if (source != null && source.useCustom)
            return JsonUtility.FromJson<BoardTilePattern>(JsonUtility.ToJson(source));
        var offsets = new System.Collections.Generic.HashSet<Vector2Int>();
        if (mode == 2)
        {
            foreach (var point in VisionGenerator.BaseVisionOffsets(kind)) offsets.Add(new Vector2Int(point.x, point.z));
            offsets.Add(Vector2Int.zero);
        }
        else
        {
            // Legacy movement/attack ranges are bounded by four cells, including
            // Magic Sniper's +/-4. Custom masks were copied above without sampling.
            for (int x = -4; x <= 4; x++) for (int z = -4; z <= 4; z++)
                if ((x != 0 || z != 0) && (mode == 0 ? MovePatterns.CanMove(kind, Direction.N, x, z)
                    : AttackPatterns.CanAttack(kind, Direction.N, x, z))) offsets.Add(new Vector2Int(x, z));
        }
        int minX = 0, maxX = 0, minZ = 0, maxZ = 0;
        foreach (var offset in offsets)
        { minX = Mathf.Min(minX, offset.x); maxX = Mathf.Max(maxX, offset.x); minZ = Mathf.Min(minZ, offset.y); maxZ = Mathf.Max(maxZ, offset.y); }
        if (maxX - minX >= BoardTilePattern.Size || maxZ - minZ >= BoardTilePattern.Size)
            throw new ArgumentException("複製元の" + (mode == 0 ? "移動" : mode == 1 ? "攻撃" : "視界")
                + "マスは8×8に収まりません。元の駒で個別マスを設定してから複製してください（範囲を切り捨てる処理は行いません）。");
        var pattern = new BoardTilePattern
        {
            useCustom = true,
            originX = Mathf.Clamp(3, -minX, 7 - maxX), originZ = Mathf.Clamp(3, -minZ, 7 - maxZ),
            directionIndependent = mode == 0 ? MovePatterns.DirectionIndependent.Contains(kind)
                : mode == 1 && AttackPatterns.DirectionIndependent.Contains(kind)
        };
        foreach (var offset in offsets) pattern.SetCell(offset.x + pattern.originX, offset.y + pattern.originZ, true);
        return pattern;
    }

    static bool HasRangedAttack(BoardActionProfile profile)
    {
        if (profile == null || !profile.CanAttack || profile.attack == null) return false;
        foreach (var offset in profile.attack.Offsets) if (Mathf.Max(Mathf.Abs(offset.x), Mathf.Abs(offset.y)) > 1) return true;
        return false;
    }

    static SpecialSkillDefinition CreateSpecialSkill(UnitData owner, UnitSpecialSkill ability)
    {
        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(owner)).Replace('\\', '/');
        var definition = CreateInstance<SpecialSkillDefinition>(); definition.ability = ability;
        definition.displayName = ability == UnitSpecialSkill.None ? string.Empty : SpecialSkillPresentation.Name(ability);
        definition.ApplySpecifiedAPCost();
        AssetDatabase.CreateAsset(definition, AssetDatabase.GenerateUniqueAssetPath(folder + "/SpecialSkill.asset"));
        return definition;
    }

    static SpecialSkillDefinition DuplicateSpecialSkill(SpecialSkillDefinition source, string folder)
    {
        if (source == null) return null;
        var copy = Instantiate(source); copy.name = source.name;
        AssetDatabase.CreateAsset(copy, AssetDatabase.GenerateUniqueAssetPath(folder + "/SpecialSkill.asset"));
        return copy;
    }

    internal static void DrawSpecialDefinition(SpecialSkillDefinition definition)
    {
        if (definition == null) return;
        var so = new SerializedObject(definition); so.Update();
        void Draw(string name, string label)
        {
            var field = so.FindProperty(name); if (field == null) return;
            if (name == "knightPenaltyStackRule" || name == "assassinBoostStackRule")
                field.intValue = EditorGUILayout.IntPopup(label, field.intValue, new[] { "未確定", "最大の効果のみ", "倍率を乗算" }, new[] { 0, 1, 2 });
            else if (name == "scoutFreeMoveScope" || name == "scoutFreeAttackScope" || name == "guardianAttackCostReset")
                field.intValue = EditorGUILayout.IntPopup(label, field.intValue, new[] { "未確定", "自軍ターンごと", "この駒の生存中" }, new[] { 0, 1, 2 });
            else EditorGUILayout.PropertyField(field, new GUIContent(label), true);
        }
        Draw("enabledForPlay", "ゲームで有効にする"); Draw("displayName", "能力の表示名");
        var type = so.FindProperty("ability");
        type.intValue = EnumPopup(type.intValue, "能力の種類", typeof(UnitSpecialSkill), value => SpecialSkillPresentation.Name((UnitSpecialSkill)value));
        var ability = (UnitSpecialSkill)type.intValue;
        if (GUILayout.Button("指定APを反映（再使用待機・未確定の数値は変更しません）"))
        {
            so.ApplyModifiedProperties(); Undo.RecordObject(definition, "指定された固有能力APを反映");
            definition.ApplySpecifiedAPCost(); EditorUtility.SetDirty(definition); so.Update();
        }
        if (ability != UnitSpecialSkill.None)
        { Draw("apCost", "使用AP（−1は未設定）"); Draw("cooldown", "再使用待機ターン（−1は未設定）"); }
        switch (ability)
        {
            case UnitSpecialSkill.KnightBreakthrough: Draw("knightPenaltyStackRule", "複数ナイトの弱体の重複方式（未確定）"); Draw("knightVisibleEnemyAtkPenalty", "視界内の敵のATK低下率（−1は未設定）"); break;
            case UnitSpecialSkill.ArcherStationary: Draw("stationaryAttackCurve", "未移動ターン → 攻撃倍率（上限1.75）"); break;
            case UnitSpecialSkill.AssassinVisionSeal:
                Draw("assassinBoostDuration", "味方撃破後の強化ターン（−1は未確定）"); Draw("assassinBoostStackRule", "撃破強化の重複方式（未確定）"); break;
            case UnitSpecialSkill.ScoutVisionGuard:
                Draw("scoutFreeMoveScope", "移動AP無料のリセット範囲（未確定）"); Draw("scoutFreeAttackScope", "攻撃AP無料のリセット範囲（未確定）"); break;
            case UnitSpecialSkill.GuardianLifeLink: Draw("guardianAttackCostReset", "攻撃AP累積のリセット範囲（未確定）"); break;
            case UnitSpecialSkill.MagicRitual: Draw("sacrificeLimit", "供儀人数上限（−1は未設定）"); break;
            case UnitSpecialSkill.CrossbowStakes:
                Draw("crossbowInitialAmmo", "初期弾数（−1は未確定、0〜3）");
                DrawFactionMask(so.FindProperty("lightningFactionMask"), "雷線が当たる陣営"); break;
            case UnitSpecialSkill.MagicSniperLock:
                Draw("sniperDamageCurve", "距離 → 攻撃倍率（上限2.5）"); Draw("sniperRecoilCurve", "距離 → 最大HPに対する反動率（上限0.15）"); break;
            case UnitSpecialSkill.LifeBomb:
                Draw("bomberAutoExplosionRadius", "10ターン無加害の自爆半径（−1は未確定）");
                Draw("bombExplosionRadius", "爆発半径（−1は未設定）");
                DrawFactionMask(so.FindProperty("bombFactionMask"), "爆発が当たる陣営");
                var policy = so.FindProperty("bombOnHolderDeathPolicy");
                policy.intValue = EditorGUILayout.IntPopup("所持者が死亡した時", policy.intValue, new[] { "未設定", "爆発を取り消す", "死亡地点で爆発する" }, new[] { 0, 1, 2 });
                break;
            case UnitSpecialSkill.HolyKnightSanctuary:
                Draw("holyKnightArea", "回復範囲の相対マス（X・Z）"); Draw("holyKnightInterruptionConfigured", "中断条件を確認・設定済み"); Draw("holyKnightInterruptOnDamage", "被ダメージで中断する"); break;
            case UnitSpecialSkill.SamuraiCounter:
                Draw("samuraiGaugeGain", "攻撃時の刀ゲージ増加（−1は未設定）"); Draw("samuraiGaugeMax", "刀ゲージMAX（−1は未設定）"); break;
            case UnitSpecialSkill.NinjaSwap:
                EditorGUILayout.HelpBox("位置交換は全マップの味方の駒が対象です。敵とは交換できません。", MessageType.Info);
                Draw("ninjaChargeCurve", "クナイ段階 → 攻撃倍率"); break;
            case UnitSpecialSkill.BerserkerRage:
                Draw("stageAPCosts", "次の強化段階へ進むスキルAP（1〜5段階・上限25）");
                Draw("berserkerAttackCurve", "強化段階 → 攻撃倍率（上限2.5）"); Draw("berserkerDefenceCurve", "強化段階 → 防御倍率"); Draw("berserkerAttackAPCurve", "HP割合 → 通常攻撃AP"); break;
            case UnitSpecialSkill.NavigatorPull: Draw("navigatorAPDamageCurve", "残りAP → 攻撃倍率（上限1.5）"); break;
        }
        so.ApplyModifiedProperties();
        string reason = definition.ConfigurationError;
        if (reason != null) EditorGUILayout.HelpBox(reason + "。未確定値を確認してから入力してください。", MessageType.Warning);
        EditorGUILayout.HelpBox(SpecialSkillPresentation.Description(definition).Replace("<color=#FFD0A0>", "").Replace("</color>", ""), MessageType.None);
    }

    static void DrawFactionMask(SerializedProperty property, string label)
    {
        if (property == null) return;
        property.intValue = EditorGUILayout.MaskField(label, property.intValue, new[] { "敵", "味方", "所持者本人" });
        if (property.intValue == 0) EditorGUILayout.HelpBox("対象陣営が未設定です。必要な陣営を選択してください。", MessageType.Warning);
    }
}

[CustomEditor(typeof(SpecialSkillDefinition))]
public sealed class SpecialSkillDefinitionEditor : Editor
{
    public override void OnInspectorGUI() => GameDevelopmentStudioWindow.DrawSpecialDefinition((SpecialSkillDefinition)target);
}
