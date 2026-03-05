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

    public void UC(PlayerMove playermove, TurnGenerater turngenerater, AttackPointt attackpoint)
    {
        this.playermove = playermove;
        this.turngenerater = turngenerater;
        this.attackpoint = attackpoint;
    }

    // ������ ���N���b�N�F�v���C���[���j�b�g�I�� ������������������������������������������
    public void Click1()
    {
        if (!TryGetMouseRay(out Ray ray)) return;
        if (!Physics.Raycast(ray, out playermove.hit, RayDistance)) return;

        playermove.Obj = playermove.hit.transform.GetComponent<Status>();
        playermove.ObjP = playermove.hit.transform.position;

        if (playermove.Obj == null) return;
        if (playermove.Obj.team != Team.Player) return;
        if (playermove.Obj.type != Type.Unit) return;

        turngenerater.movegenerater.MoveCore(playermove.Obj, playermove.ObjP);
        turngenerater.SelectUnit = playermove.Obj;
        turngenerater.OldCell = turngenerater.SelectUnit.transform.position;
        playermove.MenuSwitch = true;
        Debug.Log("<color=#00ff00ff>[Controller]</color> OK");
    }

    // ������ ���N���b�N�i�ړ��m�� or �đI���j ������������������������������������������������
    public void Click2()
    {
        Debug.Log("Click2�����n��");
        if (!TryGetMouseRay(out Ray ray)) return;
        if (!Physics.Raycast(ray, out playermove.hit, RayDistance)) return;
        Debug.Log("Click2�iRay��΂������j");

        playermove.MP = playermove.hit.transform.GetComponent<Status>();
        if (playermove.MP == null) return;
        Debug.Log("Click2�iMP��Null����Ȃ��ꍇ�̑I�����j");

        if (playermove.MP.team == Team.None && playermove.MP.type == Type.MovePoint)
        {
            HandleMovePointClick();
        }
        else if (playermove.MP.team == Team.Player && playermove.MP.type == Type.Unit)
        {
            HandlePlayerUnitReselect();
        }
    }

    // ������ �U���N���b�N ����������������������������������������������������������������������������������������
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
        // Priestは味方ユニットを対象にする（回復）、それ以外は敵ユニットを対象にする
        bool isPriest = playermove.Obj != null && playermove.Obj.kind == Kind.Priest;
        if (isPriest)
        {
            if (ATKC.team != Team.Player || ATKC.type != Type.Unit) return;
        }
        else
        {
            if (ATKC.team != Team.Enemy || ATKC.type != Type.Unit) return;
        }
        if (attackpoint.AttackP == null) return;

        Vector3 attackSame = ATKC.transform.position;
        bool isInRange = attackpoint.AttackP.Any(p => p.x == attackSame.x && p.z == attackSame.z);
        if (!isInRange) return;

        // ������ AP �`�F�b�N ������������������������������������������������������������������������������������
        if (!turngenerater.apsystem.CanAct(Team.Player, APSystem.ActionType.Attack, playermove.Obj))
        {
            Debug.Log("[APSystem] AP�s���F�U���ł��܂���");
            return;
        }

        battlesystem.target = ATKC;
        playermove.MenuSwitch = false;
        battlesystem.DamageGenerater(turngenerater);
        turngenerater.apsystem.Consume(Team.Player, APSystem.ActionType.Attack, playermove.Obj);
        playerattack.AttackSuccess = true;
    }

    // ������ �ړ���(MovePoint)�N���b�N���̏��� ������������������������������������������
    private void HandleMovePointClick()
    {
        Debug.Log("<color=#00ff00ff>[Controller]</color> OK2");

        Vector3 from = turngenerater.OldCell;           // �ړ����i�I�����ɋL�^�ς݁j
        Vector3 to = playermove.MP.transform.position;
        to.y += 0.47f;                                  // MovePoint �� Y �I�t�Z�b�g��߂�

        // ������ AP �`�F�b�N ������������������������������������������������������������������������������������
        if (!turngenerater.apsystem.CanAct(Team.Player, APSystem.ActionType.Move, playermove.Obj, from, to))
        {
            Debug.Log("[APSystem] AP�s���F�ړ��ł��܂���");
            return;
        }

        // ������ �ړ��m�� ������������������������������������������������������������������������������������������
        turngenerater.SelectUnit.transform.position = to;
        turngenerater.NewCell = turngenerater.SelectUnit.transform.position;
        turngenerater.movegenerater.MoveUpdate(turngenerater.OldCell, turngenerater.NewCell);
        turngenerater.movegenerater.MoveReset();

        // ������ AP ����i�ړ�������j ����������������������������������������������������������������
        turngenerater.apsystem.Consume(Team.Player, APSystem.ActionType.Move, playermove.Obj, from, to);

        playermove.MP = null;
        playermove.Obj = null;
        playermove.MenuSwitch = false;
    }

    // ������ �v���C���[���j�b�g�đI�����̏��� ����������������������������������������������
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
        Debug.Log("<color=#00ff00ff>[Controller]</color> OK");
    }

    // ������ �}�E�X�ʒu����Ray�𐶐��i�V���̓V�X�e���Ή��j ����������������������
    private bool TryGetMouseRay(out Ray ray)
    {
        ray = default;
        if (Mouse.current == null) return false;
        Vector2 mousePos = Mouse.current.position.ReadValue();
        ray = Camera.main.ScreenPointToRay(mousePos);
        return true;
    }
}
