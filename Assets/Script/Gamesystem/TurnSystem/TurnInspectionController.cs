using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Read-only selection ranges for both turns. Never writes movement or attack buffers.</summary>
[DisallowMultipleComponent]
public sealed class TurnInspectionController : MonoBehaviour
{
    TurnGenerator turn;
    Status selected;
    Mesh mesh;
    Material material;
    MeshRenderer rangeRenderer;
    float nextRefresh;
    bool ownsSelection;
    bool showMovement;
    readonly UnitSelectionPicker picker = new UnitSelectionPicker();
    readonly List<Vector3> vertices = new List<Vector3>(512);
    readonly List<Color> colors = new List<Color>(512);
    readonly List<int> indices = new List<int>(768);
    public void Bind(TurnGenerator value) => turn = value;

    public void Tick()
    {
        if (turn == null) return;
        bool inspecting = turn.CurrentState is EnemyStart || turn.CurrentState is EnemyMove;
        if (!inspecting)
        {
            if (ownsSelection) Clear();
            bool playing = turn.CurrentState is PlayerMove || turn.CurrentState is PlayerAttack;
            var candidate = playing ? UnitPanelUI.SelectedStatus : null;
            if (turn.Systems.BuildSystem != null && turn.Systems.BuildSystem.IsActive) candidate = null;
            if (turn.Systems.SummonSystem != null && turn.Systems.SummonSystem.IsActive) candidate = null;
            if (candidate != selected) { selected = candidate; nextRefresh = 0; }
        }
        showMovement = inspecting;
        if (turn.Context.RightClickDown) { Clear(); return; }
        if (inspecting && turn.Context.LeftClickDown && Mouse.current != null && Camera.main != null)
        {
            var ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
            if (picker.Pick(ray,Mouse.current.position.ReadValue(),UnitPanelUI.SelectedStatus,Visible,out var candidate,out _))
            {
                Inspect(candidate);
            }
        }
        if (selected == null) { if (rangeRenderer != null) rangeRenderer.enabled = false; return; }
        if (!Visible(selected) || UnitPanelUI.SelectedStatus != selected) { Clear(); return; }
        if (Time.unscaledTime < nextRefresh) return;
        nextRefresh = Time.unscaledTime + .15f;
        RefreshRanges();
    }

    internal bool Inspect(Status candidate)
    {
        if (turn == null || !(turn.CurrentState is EnemyStart || turn.CurrentState is EnemyMove) || !Visible(candidate)) return false;
        selected = candidate;
        ownsSelection = true;
        turn.Systems.UnitPanelUI?.ShowInspection(selected);
        nextRefresh = 0;
        return true;
    }

    bool Visible(Status unit) => UnitSelectionPicker.IsVisibleActor(unit,turn.Systems.VisionGenerator);

    void Clear()
    {
        if (ownsSelection && selected != null && UnitPanelUI.SelectedStatus == selected) turn.Systems.UnitPanelUI?.Hide();
        ownsSelection = false;
        picker.Reset();
        selected = null;
        if (rangeRenderer != null) rangeRenderer.enabled = false;
    }

    void RefreshRanges()
    {
        var map = turn.Systems.MapCreate;
        var moves = turn.Systems.MoveGenerator;
        if (map == null || moves == null) return;
        if (mesh == null)
        {
            mesh = new Mesh { name = "Inspection ranges" }; mesh.MarkDynamic();
            material = new Material(Resources.Load<Shader>("Shaders/InspectionRange"));
            var child = new GameObject("Read-only ranges", typeof(MeshFilter), typeof(MeshRenderer));
            child.transform.SetParent(transform, false);
            child.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            child.GetComponent<MeshFilter>().sharedMesh = mesh;
            rangeRenderer = child.GetComponent<MeshRenderer>(); rangeRenderer.sharedMaterial = material;
            rangeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            rangeRenderer.receiveShadows = false;
        }
        vertices.Clear(); colors.Clear(); indices.Clear();
        var from = selected.transform.position;
        foreach (var cell in map.SetPos)
        {
            if (turn.Systems.VisionGenerator != null && !turn.Systems.VisionGenerator.IsInVisionXZ(Team.Player, cell)) continue;
            float dx = cell.x - from.x, dz = cell.z - from.z;
            if (dx == 0 && dz == 0) continue;
            var surface = cell - Vector3.up * GameConstants.MovePointYOffset;
            if (showMovement && selected.type == Type.Unit && !StatusEffectSystem.IsMovementBlocked(selected)
                && MovePatterns.CanMove(selected.kind, selected.direction, dx, dz)
                && map.CanTraverse(from, cell) && !moves.IsOccupied(moves.Cell(cell)))
                Quad(surface, -.38f, -.38f, .38f, .38f, new Color(.15f,.7f,1f,.32f));
            if (AttackPatterns.CanAttack(selected.kind, selected.direction, dx, dz) && map.CanAttackAcrossTerrain(selected, cell))
            {
                var color = new Color(1f,.35f,.18f,.85f);
                Quad(surface, -.44f,-.44f,.44f,-.39f,color);
                Quad(surface, -.44f,.39f,.44f,.44f,color);
                Quad(surface, -.44f,-.39f,-.39f,.39f,color);
                Quad(surface, .39f,-.39f,.44f,.39f,color);
            }
        }
        mesh.Clear(); mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices,0);
        mesh.RecalculateBounds(); rangeRenderer.enabled = indices.Count > 0;
    }

    void Quad(Vector3 center, float x0, float z0, float x1, float z1, Color color)
    {
        int start = vertices.Count;
        vertices.Add(center + new Vector3(x0,0,z0)); vertices.Add(center + new Vector3(x0,0,z1));
        vertices.Add(center + new Vector3(x1,0,z1)); vertices.Add(center + new Vector3(x1,0,z0));
        for (int i=0;i<4;i++) colors.Add(color);
        indices.Add(start); indices.Add(start+1); indices.Add(start+2);
        indices.Add(start); indices.Add(start+2); indices.Add(start+3);
    }
    void OnDestroy() { if (mesh != null) Destroy(mesh); if (material != null) Destroy(material); }
}
