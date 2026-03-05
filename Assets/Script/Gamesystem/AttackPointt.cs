using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class AttackPointt : MonoBehaviour
{
    public PlayerMove.AttackMode attackmode;
    public List<Vector3> AttackP;
    public List<Vector3> setpos;

    [Header("���j�b�g���W")]
    [SerializeField] public HashSet<Vector3> unitdata;

    public Status obj;
    public Vector3 objp;
    public Vector3 attackpos;
    public RaycastHit targethit;

    [Header("�}�b�v�N���G�C�g")]
    [SerializeField] public MapCreate mapcreate;

    [Header("�v���C���[���[�u")]
    [SerializeField] public PlayerMove move;

    [Header("���[�u�W�F�l���[�^�[")]
    [SerializeField] public MoveGererater movegenerater;

    [Header("�A�^�b�N�|�C���g")]
    [SerializeField] public GameObject AttackPoint;

    [Header("�A�^�b�N�|�C���g�e")]
    [SerializeField] public Transform APparent;

    // ������ ��킲�Ƃ̍U���͈͔��� ����������������������������������������������������������������������������
    // dx = p.x - objp.x�i�����t���j, dz = p.z - objp.z�i�����t���j
    // Priest �͖������̂��߃G���g���Ȃ��i����ǉ��\��j
    // �V�������ǉ�����ꍇ�͂�����1�s�ǉ����邾���ł悢
    public static readonly Dictionary<Kind, Func<float, float, bool>> AttackPredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // �O��3�}�X�i���}1�E���i�j
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // �O��3�}�X�iKing�Ɠ����U���͈́j
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) <= 1 && dz == 1 },

        // �O�����i2�E3�}�X
        { Kind.Archer,      (dx, dz) => dx == 0 && (dz == 2 || dz == 3) },

        // �\��������2�}�X
        { Kind.Magic,       (dx, dz) => (Mathf.Abs(dx) == 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // �O�΂߁}1�}�X
        { Kind.Assassin,    (dx, dz) => Mathf.Abs(dx) == 1 && dz == 1 },

        // ���E��1�}�X
        { Kind.Scout,       (dx, dz) => Mathf.Abs(dx) == 1 && dz == 0 },

        // �O���i1�}�X
        { Kind.Guardian,    (dx, dz) => dx == 0 && dz == 1 },

        // �O���i1�E2�}�X
        { Kind.Crossbow,    (dx, dz) => dx == 0 && (dz == 1 || dz == 2) },

        // ���E��4�}�X
        { Kind.Magicsniper, (dx, dz) => Mathf.Abs(dx) == 4 && dz == 0 },

        // �O���i3�}�X
        { Kind.Bomber,      (dx, dz) => dx == 0 && dz == 3 },

        // 隣接4マス（前後左右）の味方を回復対象とする
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 0) || (dx == 0 && Mathf.Abs(dz) == 1) },
    };

    // ������ �U�����[�h�ɉ������|�C���g���� ����������������������������������������������������������
    public void AttackPointCall(Status Obj, Vector3 ObjP, PlayerMove move)
    {
        this.move = move;
        setpos = mapcreate.SetPos;
        attackmode = move.attackmode;

        switch (attackmode)
        {
            case PlayerMove.AttackMode.Normal:
                NormalAttackPData(Obj, ObjP);
                PointCreate();
                break;
            case PlayerMove.AttackMode.Skill:
                // ��������\��
                break;
        }
    }

    // ������ �U���|�C���g�I�u�W�F�N�g�̐��� ����������������������������������������������������������
    public void PointCreate()
    {
        for (int i = 0; i < AttackP.Count; i++)
        {
            Vector3 pos = AttackP[i];
            pos.y -= 0.17f;
            Instantiate(AttackPoint, pos, Quaternion.identity, APparent);
        }
    }

    // ������ �U���|�C���g�I�u�W�F�N�g�̍폜 ����������������������������������������������������������
    public void AtkpDestroy()
    {
        foreach (Transform child in APparent)
        {
            Destroy(child.gameObject);
        }
        AttackP?.Clear();
    }

    // ������ �ʏ�U���̍U���͈͌v�Z ����������������������������������������������������������������������������
    public void NormalAttackPData(Status Obj, Vector3 ObjP)
    {
        AttackP?.Clear();
        obj = Obj;
        objp = ObjP;
        movegenerater.UnitPointCore();
        unitdata = movegenerater.UnitPointData;

        if (!AttackPredicateMap.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
            Debug.Log($"[AttackPointt] Kind '{obj.kind}' �̍U���p�^�[���͖������ł�");
            return;
        }

        Vector3 ownCell = movegenerater.Cell(objp);
        Vector3 pcpCell = movegenerater.Cell(movegenerater.pcp);

        // Priestだけ味方ユニットを対象にする（それ以外は敵・建物・壁を対象）
        bool isPriest = obj.kind == Kind.Priest;

        AttackP = setpos.Where(p =>
        {
            float dx = Mathf.RoundToInt(p.x - objp.x);
            float dz = Mathf.RoundToInt(p.z - objp.z);
            float dirDx = obj.direction == Direction.S ? -dx : dx;
            float dirDz = obj.direction == Direction.S ? -dz : dz;
            Vector3 cell = movegenerater.Cell(p);
            bool occupied = unitdata.Contains(cell);
            bool notSelf = cell != ownCell && cell != pcpCell;
            return occupied && notSelf && predicate(dirDx, dirDz);
        }).ToList();
    }
}
