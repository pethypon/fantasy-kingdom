using System;
using UnityEditor;
using UnityEngine;

/// <summary>Japanese tile editor shared by unit and building authoring.</summary>
public static class DevelopmentStudioTiles
{
    static BoardTilePattern painting;
    static bool paintValue;
    static int narrowTab;
    static readonly string[] TabNames = { "移動マス", "攻撃マス", "視界マス" };
    static readonly Color[] Colors = { new Color(.24f, .65f, .9f), new Color(.95f, .42f, .28f), new Color(.2f, .74f, .52f) };

    public static void Draw(BoardActionProfile profile, Kind role, float availableWidth)
    {
        if (profile == null) { EditorGUILayout.HelpBox("「マス設定を作成」を押して個別の行動を設定してください。", MessageType.Info); return; }
        EditorGUILayout.LabelField("行動タイプ", EditorStyles.boldLabel);
        int nextType = EditorGUILayout.Popup("行動の組み合わせ", (int)profile.actionType,
            new[] { "汎用（移動・攻撃・スキル）", "戦闘（移動・攻撃・スキル）", "支援（移動・スキル）", "固定（攻撃・スキル）", "行動しない" });
        if (nextType != (int)profile.actionType)
        { Undo.RecordObject(profile, "行動タイプ変更"); profile.actionType = (ActorActionType)nextType; EditorUtility.SetDirty(profile); }
        EditorGUILayout.HelpBox("黄：現在地　青：移動　赤：攻撃　緑：視界。クリックでON/OFF、ドラッグでまとめて塗れます。↑が前方向です。自分のマスは移動・攻撃先にできません。", MessageType.None);
        if (availableWidth >= 870)
        {
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < 3; i++) { EditorGUILayout.BeginVertical(GUILayout.Width(278)); DrawOne(profile, Pattern(profile, i), role, i); EditorGUILayout.EndVertical(); }
            EditorGUILayout.EndHorizontal();
        }
        else
        { narrowTab = GUILayout.Toolbar(narrowTab, TabNames); DrawOne(profile, Pattern(profile, narrowTab), role, narrowTab); }
        if (Event.current.rawType == EventType.MouseUp) painting = null;
    }
    static BoardTilePattern Pattern(BoardActionProfile p, int i) => i == 0 ? p.movement : i == 1 ? p.attack : p.vision;
    static void DrawOne(BoardActionProfile profile, BoardTilePattern pattern, Kind role, int mode)
    {
        if (pattern == null) return;
        EditorGUILayout.LabelField(TabNames[mode] + "  8 × 8", EditorStyles.boldLabel);
        bool enabled = EditorGUILayout.ToggleLeft("この駒専用のマス設定を使う", pattern.useCustom);
        if (enabled != pattern.useCustom)
        { Undo.RecordObject(profile, "個別マス設定切替"); if (enabled && pattern.cells == 0) FillLegacy(pattern, role, mode); pattern.useCustom = enabled; EditorUtility.SetDirty(profile); }
        bool independent = EditorGUILayout.ToggleLeft("駒の向きによらず同じ配置", pattern.directionIndependent);
        if (independent != pattern.directionIndependent)
        { Undo.RecordObject(profile, "マスの向き変更"); pattern.directionIndependent = independent; EditorUtility.SetDirty(profile); }
        using (new EditorGUI.DisabledScope(!pattern.useCustom))
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("全解除")) Mutate(profile, () => { pattern.cells = 0; if (mode == 2) pattern.SetCell(pattern.originX, pattern.originZ, true); });
            if (GUILayout.Button("既存役割から")) Mutate(profile, () => FillLegacy(pattern, role, mode));
            if (GUILayout.Button("左右対称")) Mutate(profile, () => Mirror(pattern));
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.LabelField("← 西      ↑ 前（北）      東 →", EditorStyles.centeredGreyMiniLabel, GUILayout.Width(270));
        var grid = GUILayoutUtility.GetRect(270, 270, GUILayout.Width(270), GUILayout.Height(270));
        const float tile = 32;
        var preview = pattern;
        if (!pattern.useCustom)
        { preview = new BoardTilePattern { originX = pattern.originX, originZ = pattern.originZ }; FillLegacy(preview, role, mode); }
        for (int row = 0; row < 8; row++) for (int x = 0; x < 8; x++)
        {
            int z = 7 - row;
            var rect = new Rect(grid.x + x * 33, grid.y + row * 33, tile, tile);
            bool origin = x == pattern.originX && z == pattern.originZ;
            bool set = preview.IsCellSet(x, z);
            var color = origin ? new Color(.86f, .7f, .25f) : set ? Colors[mode] : new Color(.22f, .25f, .29f);
            if (!pattern.useCustom && !origin) color *= .65f;
            EditorGUI.DrawRect(rect, color);
            var label = origin ? "駒" : set ? mode == 0 ? "移" : mode == 1 ? "攻" : "視" : "·";
            GUI.Label(rect, new GUIContent(label, $"横 {x - pattern.originX:+0;-0;0} / 前後 {z - pattern.originZ:+0;-0;0}"),
                new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } });
            var e = Event.current;
            if (!pattern.useCustom || origin && mode != 2 || !rect.Contains(e.mousePosition) || e.button != 0) continue;
            if (e.type == EventType.MouseDown)
            { Undo.RecordObject(profile, "マスを塗る"); painting = pattern; paintValue = !set; Paint(profile, pattern, x, z, mode); e.Use(); }
            else if (e.type == EventType.MouseDrag && painting == pattern)
            { Paint(profile, pattern, x, z, mode); e.Use(); }
        }
        int newX = EditorGUILayout.IntSlider("現在地の列（左から）", pattern.originX, 0, 7);
        int newZ = EditorGUILayout.IntSlider("現在地の行（下から）", pattern.originZ, 0, 7);
        if (newX != pattern.originX || newZ != pattern.originZ)
            Mutate(profile, () => { pattern.originX = newX; pattern.originZ = newZ; if (mode == 2) pattern.SetCell(newX, newZ, true); else pattern.SetCell(newX, newZ, false); });
        EditorGUILayout.LabelField(pattern.useCustom ? $"有効マス：{pattern.Offsets.Count}" : "既存ルールを使用（8×8外は省略）", EditorStyles.miniLabel);
    }
    static void Paint(BoardActionProfile profile, BoardTilePattern p, int x, int z, int mode)
    { p.SetCell(x, z, mode == 2 && x == p.originX && z == p.originZ || paintValue); EditorUtility.SetDirty(profile); }
    static void Mutate(BoardActionProfile profile, Action action)
    { Undo.RecordObject(profile, "マス設定変更"); action(); EditorUtility.SetDirty(profile); }
    public static void FillLegacy(BoardTilePattern p, Kind role, int mode)
    {
        p.cells = 0;
        if (mode == 2)
        {
            foreach (var offset in VisionGenerator.BaseVisionOffsets(role)) p.SetCell(offset.x + p.originX, offset.z + p.originZ, true);
            p.SetCell(p.originX, p.originZ, true);
        }
        else
        {
            for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
            {
                int dx = x - p.originX, dz = z - p.originZ;
                if (dx == 0 && dz == 0) continue;
                bool valid = mode == 0 ? MovePatterns.CanMove(role, Direction.N, dx, dz) : AttackPatterns.CanAttack(role, Direction.N, dx, dz);
                if (valid) p.SetCell(x, z, true);
            }
        }
        p.directionIndependent = mode == 0 ? MovePatterns.DirectionIndependent.Contains(role) : mode == 1 && AttackPatterns.DirectionIndependent.Contains(role);
    }
    static void Mirror(BoardTilePattern p)
    {
        long original = p.cells;
        for (int z = 0; z < 8; z++) for (int x = 0; x < 8; x++)
            if ((unchecked((ulong)original) & (1UL << (z * 8 + x))) != 0) p.SetCell(p.originX * 2 - x, z, true);
    }
}
