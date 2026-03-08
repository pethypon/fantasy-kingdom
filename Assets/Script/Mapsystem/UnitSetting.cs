using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class UnitSetting : MonoBehaviour
{
    [Header("�L���O")]
    [SerializeField] GameObject KingPiece;
    [Header("�ٌ`")]
    [SerializeField] GameObject StrangePiece;

    [Header("���j�b�g�z�u�e�I�u�W�F�N�g")]
    [SerializeField] public Transform PlayerUnit;
    [SerializeField] public Transform EnemyUnit;

    // ������ UnitData �Ǘ��iDictionary ���j ����������������������������������������������������������
    // SerializeField ��11���ׂȂ����R�F
    // �V���� Kind ��ǉ����邽�тɃt�B�[���h�ǉ���Inspector�ݒ��2��Ԃ���������B
    // Dictionary �Ȃ烊�X�g��1�G���g���ǉ����邾���ōςށi�݌v����2�j�B
    [System.Serializable]
    public class UnitDataEntry
    {
        public Kind kind;
        public UnitData data;
    }

    [Header("���j�b�g�f�[�^�iKind�ʁj")]
    [SerializeField] private List<UnitDataEntry> _unitDataList;

    // �O������̓ǂݎ���p�iGameGenerater�EBattleSystem�EPlayerSummon ���Q�Ɓj
    public Dictionary<Kind, UnitData> UnitDataMap { get; private set; }

    // ������ ������ ������������������������������������������������������������������������������������������������������������
    private void Awake()
    {
        UnitDataMap = new Dictionary<Kind, UnitData>();
        foreach (var entry in _unitDataList)
        {
            if (entry.data != null)
                UnitDataMap[entry.kind] = entry.data;
            else
                Debug.LogWarning($"[UnitSetting] Kind:{entry.kind} ��UnitData��null�ł�");
        }
    }

    // ������ ���ʐ������\�b�h ����������������������������������������������������������������������������������������

    /// <summary>
    /// ���j�b�g�𐶐����ăX�e�[�^�X�𑦍��ɓK�p����B
    /// �Q�[�����̐V�K�����͂��ׂĂ��̃��\�b�h�o�R�ōs���i��̐������̓K�p��S���j�B
    /// </summary>
    public GameObject SpawnUnit(GameObject prefab, Vector3 pos,
                                Transform parent, int level = 1)
    {
        var obj = Instantiate(prefab, pos, Quaternion.identity, parent);

        var status = obj.GetComponentInChildren<Status>();
        if (status == null)
        {
            Debug.LogWarning($"[UnitSetting] {prefab.name} ��Status��������܂���");
            return obj;
        }

        if (UnitDataMap.TryGetValue(status.kind, out UnitData data))
            data.ApplyToStatus(status, level);
        else
            Debug.LogWarning($"[UnitSetting] Kind:{status.kind} ��UnitData�����o�^�ł�");

        // 頭上UI（Lv + HP）をアタッチ
        UnitHeadUI.Attach(obj);

        return obj;
    }

    // ������ �Q�[���J�n���̃��j�b�g�z�u ������������������������������������������������������������������
    public void UnitSet()
    {
        // PCP�AECP�ASetPos �����o��
        CrystalSystem crystalsystem = GetComponent<CrystalSystem>();
        MapCreate mapcreate = GetComponent<MapCreate>();
        Vector3 pcp = crystalsystem.PCP;
        Vector3 ecp = crystalsystem.ECP;
        var setpos = mapcreate.SetPos;

        // �z�u�ʒu���i��iKingPoint�FPCP����1�}�X�ȓ��j
        var KingPoint = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - pcp.x);
            float pz = Mathf.Abs(p.z - pcp.z);
            return px <= 1 && pz <= 1 && p != pcp;
        }).ToList();

        // �z�u�ʒu���i��iStrangePoint�FECP����1�}�X�ȓ��j
        var StrangePoint = setpos.Where(p =>
        {
            float px = Mathf.Abs(p.x - ecp.x);
            float pz = Mathf.Abs(p.z - ecp.z);
            return px <= 1 && pz <= 1 && p != ecp;
        }).ToList();

        // Instantiate �� SpawnUnit �ɒu�������i�X�e�[�^�X�K�p���݁j
        Vector3 KP = KingPoint[Random.Range(0, KingPoint.Count)];
        SpawnUnit(KingPiece, KP, PlayerUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>���ݒu");

        Vector3 SP = StrangePoint[Random.Range(0, StrangePoint.Count)];
        SpawnUnit(StrangePiece, SP, EnemyUnit);
        Debug.Log("<color=#ffff00ff>[StartSetting]</color>�ٌ`�̉��ݒu");
    }
}
