using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class UnitClick : MonoBehaviour
{
    [SerializeField] private PlayerMove playermove;
    [SerializeField] private TurnGenerater turngenerater;
    [SerializeField] private PlayerAttack playerattack;
    [SerializeField] private BattleSystem battlesystem;
    [SerializeField] private AttackPointt attackpoint;
    [SerializeField] public Status ATKC;

    public RaycastHit attackhit;
    private const float RayDistance = 100f;
    private readonly int MovePointLayerMask = 1 << LayerMask.NameToLayer("MovePoint");

    public void UC(PlayerMove playermove, TurnGenerater turngenerater, AttackPointt attackpoint)
    {
        this.playermove = playermove;
        this.turngenerater = turngenerater;
        this.attackpoint = attackpoint;
    }

    // ---- 左クリック：プレイヤーユニットまたは建築物選択 ----
    public void Click1()
    {
        if (playermove != null && playermove.BuildMode) return;
        if (!TryGetMouseRay(out Ray ray)) return;
        if (!Physics.Raycast(ray, out playermove.hit, RayDistance)) return;

        playermove.Obj = playermove.hit.transform.GetComponent<Status>();
        playermove.ObjP = playermove.hit.transform.position;

        if (playermove.Obj == null) return;
        if (playermove.Obj.team != Team.Player) return;

        // 建築物・壁クリック（移動ポイントは生成しない）
        if (playermove.Obj.type == Type.Building || playermove.Obj.type == Type.Wall)
        {
            turngenerater.SelectUnit = playermove.Obj;
            if (turngenerater.unitPanelUI != null)
                turngenerater.unitPanelUI.Show(playermove.Obj);
            Debug.Log("<color=#00ff00ff>[Controller]</color> 建築物選択");
            return;
        }

        if (playermove.Obj.type != Type.Unit) return;

        turngenerater.movegenerater.MoveCore(playermove.Obj, playermove.ObjP);
        turngenerater.SelectUnit = playermove.Obj;
        turngenerater.OldCell = turngenerater.SelectUnit.transform.position;
        playermove.MenuSwitch = true;

        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Show(playermove.Obj);

        Debug.Log("<color=#00ff00ff>[Controller]</color> OK");
    }

    // ---- 左クリック（移動確定 or 再選択） ----
    public void Click2()
    {
        if (playermove != null && playermove.BuildMode) return;
        Debug.Log("Click2処理開始");
        if (!TryGetMouseRay(out Ray ray)) return;

        // MovePoint レイヤーを優先してRaycast
        if (Physics.Raycast(ray, out playermove.hit, RayDistance, MovePointLayerMask))
        {
            Debug.Log("Click2（MovePointレイヤーにヒット）");
            HandleMovePointClick();
            return;
        }

        // MovePoint に当たらなかった場合、全レイヤーでユニット再選択を試みる
        if (!Physics.Raycast(ray, out playermove.hit, RayDistance)) return;

        playermove.MP = playermove.hit.transform.GetComponent<Status>();
        if (playermove.MP == null) return;

        if (playermove.MP.team == Team.Player && playermove.MP.type == Type.Unit)
        {
            HandlePlayerUnitReselect();
        }
    }

    // ---- 攻撃クリック ----
    public void AttackClick(
        BattleSystem battlesystem,
        PlayerAttack playerattack,
        AttackPointt attackpoint,
        PlayerMove playermove)
    {
        this.playermove = playermove;
        this.battlesystem = battlesystem;
        this.playerattack = playerattack;
        this.attackpoint = attackpoint;

        if (!TryGetMouseRay(out Ray ray)) return;
        if (!Physics.Raycast(ray, out attackhit, RayDistance)) return;
        if (!attackhit.transform.TryGetComponent<Status>(out ATKC)) return;
        if (ATKC.team != Team.Enemy || ATKC.type != Type.Unit) return;
        if (attackpoint.AttackP == null) return;

        Vector3 attackSame = ATKC.transform.position;
        bool isInRange = attackpoint.AttackP.Any(p => p.x == attackSame.x && p.z == attackSame.z);
        if (!isInRange) return;

        // ---- APチェック ----
        if (!turngenerater.apsystem.CanAct(Team.Player, APSystem.ActionType.Attack, playermove.Obj))
        {
            Debug.Log("[APSystem] AP不足：攻撃できません");
            return;
        }

        battlesystem.target = ATKC;
        playermove.MenuSwitch = false;
        battlesystem.DamageGenerater(turngenerater);
        turngenerater.apsystem.Consume(Team.Player, APSystem.ActionType.Attack, playermove.Obj);
        playerattack.AttackSuccess = true;
    }

    // ---- 移動先(MovePoint)クリック時の処理 ----
    private void HandleMovePointClick()
    {
        Debug.Log("<color=#00ff00ff>[Controller]</color> OK2");

        Vector3 from = turngenerater.OldCell;           // 移動元（選択時に記録済み）
        Vector3 to = playermove.hit.transform.position;
        to.y += 0.47f;                                  // MovePoint の Y オフセットを戻す

        // ---- APチェック ----
        if (!turngenerater.apsystem.CanAct(Team.Player, APSystem.ActionType.Move, playermove.Obj, from, to))
        {
            Debug.Log("[APSystem] AP不足：移動できません");
            return;
        }

        // ---- 移動確定 ----
        turngenerater.SelectUnit.transform.position = to;
        turngenerater.NewCell = turngenerater.SelectUnit.transform.position;
        turngenerater.movegenerater.MoveUpdate(turngenerater.OldCell, turngenerater.NewCell);
        turngenerater.movegenerater.MoveReset();

        // ---- AP消費（移動コスト） ----
        turngenerater.apsystem.Consume(Team.Player, APSystem.ActionType.Move, playermove.Obj, from, to);

        playermove.MP = null;
        playermove.Obj = null;
        playermove.MenuSwitch = false;

        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Hide();
    }

    // ---- プレイヤーユニット再選択時の処理 ----
    private void HandlePlayerUnitReselect()
    {
        turngenerater.movegenerater.MoveReset();
        playermove.Obj = playermove.hit.transform.GetComponent<Status>();
        playermove.ObjP = playermove.hit.transform.position;
        turngenerater.movegenerater.MoveCore(playermove.Obj, playermove.ObjP);
        turngenerater.SelectUnit = playermove.Obj;
        turngenerater.movegenerater.UnitPointData.Remove(
            turngenerater.movegenerater.Cell(turngenerater.OldCell));
        turngenerater.OldCell = turngenerater.SelectUnit.transform.position;

        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Show(playermove.Obj);

        Debug.Log("<color=#00ff00ff>[Controller]</color> OK");
    }

    // ---- マウス位置からRayを生成（新入力システム対応） ----
    private bool TryGetMouseRay(out Ray ray)
    {
        ray = default;
        if (Mouse.current == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        ray = Camera.main.ScreenPointToRay(mousePos);
        return true;
    }
}
