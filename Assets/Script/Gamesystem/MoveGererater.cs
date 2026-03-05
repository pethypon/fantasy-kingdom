using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MoveGererater : MonoBehaviour
{
    [SerializeField] public MapCreate mapcreate;
    [SerializeField] public TurnGenerater turngenerater;

    [Header("�ړ��ʒu�\���̃I�u�W�F�N�g")]
    [SerializeField] public GameObject MovePoint;

    [Header("���[�u�e�I�u�W�F�N�g")]
    [SerializeField] public Transform Move;

    [Header("���j�b�g���W")]
    [SerializeField] public HashSet<Vector3> UnitPointData = new HashSet<Vector3>();

    [Header("�N���X�^���V�X�e��")]
    [SerializeField] CrystalSystem crystalsystem;

    [Header("���j�b�g�Z�b�e�B���O")]
    [SerializeField] UnitSetting unitsetting;

    [Header("���j�b�g�{�b�N�X")]
    [SerializeField] Transform PlayerUnit;
    [SerializeField] Transform EnemyUnit;

    public List<Vector3> setpos;
    public List<Vector3> MoveUnitP;
    public Vector3 objp;
    private Status obj;
    public Vector3 pcp;
    public Vector3 ecp;
    public Vector3 usp;

    // ������ ��킲�Ƃ̈ړ����� ������������������������������������������������������������������������������������
    // dx = p.x - objp.x�i�����t���j, dz = p.z - objp.z�i�����t���j
    // �V�������ǉ�����ꍇ�͂�����1�s�ǉ����邾���ł悢
    public static readonly Dictionary<Kind, Func<float, float, bool>> MovePredicateMap =
        new Dictionary<Kind, Func<float, float, bool>>
    {
        // �S����1�}�X
        { Kind.King,        (dx, dz) => Mathf.Abs(dx) <= 1
                                     && Mathf.Abs(dz) <= 1 },

        // �㉺���E1�}�X
        { Kind.Knight,      (dx, dz) => Mathf.Abs(dx) + Mathf.Abs(dz) == 1 },

        // �΂�1�}�X
        { Kind.Archer,      (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // �΂�1�}�X�iArcher�Ɠ����ړ��j
        { Kind.Magic,       (dx, dz) => Mathf.Abs(dx) == 1
                                     && Mathf.Abs(dz) == 1 },

        // �j�n���сi2�~1 or 1�~2�j
        { Kind.Assassin,    (dx, dz) => (Mathf.Abs(dx) == 2 && Mathf.Abs(dz) == 1)
                                     || (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) == 2) },

        // ���}1�{�O��1 or ���i2
        { Kind.Scout,       (dx, dz) => (Mathf.Abs(dx) == 1 && Mathf.Abs(dz) <= 1)
                                     || (dx == 0 && Mathf.Abs(dz) == 2) },

        // �O�΂�1 or ��뒼�i1�i�����l���F�����t���j
        { Kind.Priest,      (dx, dz) => (Mathf.Abs(dx) == 1 && dz == 1)
                                     || (dx == 0 && dz == -1) },

        // ���E2�}�X or �O��1�}�X
        { Kind.Guardian,    (dx, dz) => (Mathf.Abs(dx) <= 2 && dz == 0)
                                     || (dx == 0 && Mathf.Abs(dz) == 1) },

        // �O���i1-3�}�X or �΂ߌ��1�}�X�i�����t���j
        { Kind.Crossbow,    (dx, dz) => (dx == 0 && (dz == 1 || dz == 2 || dz == 3))
                                     || (Mathf.Abs(dx) == 1 && dz == -1) },

        // �E�O�΂�1 or ���E���3�}�X�i�����t���j
        { Kind.Magicsniper, (dx, dz) => (dx == 1 && dz == 1)
                                     || (Mathf.Abs(dx) == 3 && dz == -1) },

        // �΂ߑO��2�p�^�[���i�����t���j
        { Kind.Bomber,      (dx, dz) => (dx == -1 && dz ==  1) || (dx ==  2 && dz ==  2)
                                     || (dx ==  1 && dz == -1) || (dx == -2 && dz == -2) },
    };

    // ������ ���j�b�g��L���W�̍X�V ����������������������������������������������������������������������������
    public void UnitPointCore()
    {
        UnitPointData.Clear();
        pcp = crystalsystem.PCP;
        ecp = crystalsystem.ECP;
        UnitPointData.Add(Cell(pcp));
        UnitPointData.Add(Cell(ecp));

        foreach (Status us in PlayerUnit.GetComponentsInChildren<Status>())
        {
            if (us.type != Type.Unit) continue;
            usp = us.transform.position;
            UnitPointData.Add(Cell(usp));
        }
        foreach (Status us in EnemyUnit.GetComponentsInChildren<Status>())
        {
            if (us.type != Type.Unit) continue;
            usp = us.transform.position;
            UnitPointData.Add(Cell(usp));
        }

        // 壁をユニットと同様に移動不可マスとして登録する
        foreach (Transform parent in new[] { PlayerUnit, EnemyUnit })
        {
            foreach (Status s in parent.GetComponentsInChildren<Status>())
            {
                if (s.type == Type.Wall && s.gameObject.activeSelf)
                    UnitPointData.Add(Cell(s.transform.position));
            }
        }
    }

    // ������ �O���b�h���W�ւ̊ۂ� ��������������������������������������������������������������������������������
    public Vector3 Cell(Vector3 v)
    {
        return new Vector3(Mathf.RoundToInt(v.x), 0f, Mathf.RoundToInt(v.z));
    }

    // ������ �ړ��͈͂̌v�Z�ƃI�u�W�F�N�g���� ������������������������������������������������������������
    public void MoveCore(Status Obj, Vector3 ObjP)
    {
        setpos = mapcreate.SetPos;
        MoveUnitP.Clear();
        obj = Obj;
        objp = ObjP;

        if (!MovePredicateMap.TryGetValue(obj.kind, out Func<float, float, bool> predicate))
        {
            Debug.LogWarning($"[MoveGererater] Kind '{obj.kind}' �̈ړ��p�^�[��������`�ł�");
            return;
        }

        Debug.Log($"<color=#00ff00ff>[Controller]</color>{obj.kind}");

        MoveUnitP = setpos.Where(p =>
        {
            float dx = p.x - objp.x;
            float dz = p.z - objp.z;
            // Direction.Sのとき前後を反転（南向きユニットの移動範囲を正しく計算するため）
            float dirDx = obj.direction == Direction.S ? -dx : dx;
            float dirDz = obj.direction == Direction.S ? -dz : dz;
            bool occupied = UnitPointData.Contains(Cell(p));
            return predicate(dirDx, dirDz) && !occupied;
        }).ToList();

        MoveCreate();
    }

    // ������ �ړ��|�C���g�I�u�W�F�N�g�̐��� ����������������������������������������������������������
    public void MoveCreate()
    {
        for (int i = 0; i < MoveUnitP.Count; i++)
        {
            Vector3 pos = MoveUnitP[i];
            pos.y -= 0.47f;
            Instantiate(MovePoint, pos, Quaternion.identity, Move);
            Debug.Log("<color=#00ff00ff>[Controller]</color>MovePoint");
        }
    }

    // ������ �ړ��|�C���g�I�u�W�F�N�g�̍폜 ����������������������������������������������������������
    public void MoveReset()
    {
        foreach (Transform child in Move.transform)
        {
            Destroy(child.gameObject);
        }
    }

    // ������ UnitPointData �̍X�V ��������������������������������������������������������������������������������
    public void MoveUpdate(Vector3 OldCell, Vector3 NewCell)
    {
        UnitPointData.Add(Cell(NewCell));
        UnitPointData.Remove(Cell(OldCell));
    }
}
