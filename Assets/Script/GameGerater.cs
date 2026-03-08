using System.Collections.Generic;
using UnityEngine;

public class GameGenerater : MonoBehaviour
{
    [SerializeField] MapCreate _MapCreate;
    [SerializeField] CrystalSystem _CrystalSystem;
    [SerializeField] TerritorySystem _TerritorySystem;
    [SerializeField] UnitSetting _UnitSetting;
    [SerializeField] MoveGererater _MoveGenerater;
    [SerializeField] VisionGenerater _VisionGenerater;
    [SerializeField] APSystem _APSystem;
    [SerializeField] BuildSystem _BuildSystem;
    [SerializeField] SummonSystem _SummonSystem;
    [SerializeField] TurnGenerater _TurnGenerater;

    [Header("UI")]
    [SerializeField] APPanelUI _APPanelUI;
    [SerializeField] ResourceBarUI _ResourceBarUI;
    private UIBuilder _uiBuilder;

    [Header("Crystal �e�I�u�W�F�N�g")]
    [SerializeField] Transform _PlayerCrystal;
    [SerializeField] Transform _EnemyCrystal;

    // ���W���� Crystal �q�i���X�N���v�g����Q�Ɖj
    [HideInInspector] public List<GameObject> PlayerCrystalChildren = new List<GameObject>();
    [HideInInspector] public List<GameObject> EnemyCrystalChildren = new List<GameObject>();

    void Awake()
    {
        // ---- UIBuilder の生成・取得 ----
        _uiBuilder = Object.FindFirstObjectByType<UIBuilder>();
        if (_uiBuilder == null)
        {
            var uiGo = new GameObject("UIBuilder");
            _uiBuilder = uiGo.AddComponent<UIBuilder>();
        }

        // UIBuilder が生成した UI パネルを取得
        if (_APPanelUI == null && _uiBuilder.APPanel != null)
            _APPanelUI = _uiBuilder.APPanel;
        if (_ResourceBarUI == null && _uiBuilder.ResourceBar != null)
            _ResourceBarUI = _uiBuilder.ResourceBar;

        // ���� �n�`�E�N���X�^������ ��������������������������������������������������������������������������
        _MapCreate.noisegenerater();
        _MapCreate.BuildTop();
        _CrystalSystem.CrystalCore();

        // ���� ���j�b�g�z�u�iSpawnUnit ���� UnitData ��K�p�j ������������������������
        _UnitSetting.UnitSet();

        // ���� �Q�[���J�n���F�V�[����̑S���j�b�g�� UnitData ��K�p ������������
        // UnitSet() �� Awake ���Ԃɍ���Ȃ��P�[�X�� Prefab ���u���ɑΉ����邽��
        // SpawnUnit() ���S���ł��Ă��Ȃ���������ŕ⊮����
        ApplyAllUnitData(_UnitSetting.PlayerUnit);
        ApplyAllUnitData(_UnitSetting.EnemyUnit);

        // ���� �̒n�E�ړ��E���E�̍\�z ����������������������������������������������������������������������
        // ApplyAllUnitData �̌�Ɏ��s���闝�R�F
        // Territory�EMove�EVision �̌v�Z�̓X�e�[�^�X�K�p��ɍs������
        _TerritorySystem.Territory();
        _MoveGenerater.UnitPointCore();
        _VisionGenerater.VisionPoint(_MapCreate, _MoveGenerater, _CrystalSystem);

        // ���� Crystal �q�I�u�W�F�N�g�����W ����������������������������������������������������������
        CollectChildren(_PlayerCrystal, PlayerCrystalChildren);
        CollectChildren(_EnemyCrystal, EnemyCrystalChildren);

        // ���� FactionState �� APSystem �ɒ��� ����������������������������������������������������
        FactionState factionState = _PlayerCrystal.GetComponentInChildren<FactionState>();
        if (factionState == null)
            Debug.LogError("[GameGenerater] FactionState �� PlayerCrystal �̎q�Ɍ�����܂���");
        _APSystem.Init(factionState);

        // ---- BuildSystem 初期化 ----
        if (_BuildSystem != null)
        {
            _BuildSystem.Init(_TurnGenerater, _TerritorySystem, _APSystem,
                              factionState, _MoveGenerater, _MapCreate);
            _TurnGenerater.buildsystem = _BuildSystem;

            // UIBuilder の建築ボタンに BuildSystem を接続
            if (_uiBuilder != null)
                _uiBuilder.InitBuildButtons(_BuildSystem, _APSystem, factionState);
        }

        // ---- SummonSystem 初期化 ----
        if (_SummonSystem != null)
        {
            _SummonSystem.Init(_TurnGenerater, _TerritorySystem, _APSystem,
                               factionState, _MoveGenerater, _MapCreate,
                               _UnitSetting, _VisionGenerater);
            _TurnGenerater.summonsystem = _SummonSystem;

            if (_uiBuilder != null)
                _uiBuilder.InitSummonButtons(_SummonSystem, _APSystem, factionState, _UnitSetting);
        }

        // ���� UI �� FactionState ��n�� ����������������������������������������������������
        if (_APPanelUI != null) _APPanelUI.Init(factionState);
        if (_ResourceBarUI != null) _ResourceBarUI.Init(factionState);

        // ���� ���������ݒ�iGameReference �����j ����������������������������������������������
        if (factionState != null)
            InitResources(factionState.PlayerResources);

        // ���� �^�[���J�n ����������������������������������������������������������������������������������������������
        _TurnGenerater.StartFirstTurn();
    }

    // ����������������������������������������������������������������������������������������������������������������������������������
    //  �Q�[���J�n���̑S���j�b�g�K�p
    //  �Q�[�����̐����� UnitSetting.SpawnUnit() ���S�����邽�߁A
    //  �����ł́u�J�n���_�ŃV�[����ɑ��݂����v������Ώۂɂ���
    // ����������������������������������������������������������������������������������������������������������������������������������

    /// <summary>
    /// �w��̐e�I�u�W�F�N�g�z���̑S���j�b�g�� UnitData ��K�p����B
    /// �Q�[���J�n���̈ꊇ�K�p��S���i�������� UnitSetting.SpawnUnit() ���S���j�B
    /// </summary>
    private void ApplyAllUnitData(Transform unitParent)
    {
        foreach (Status status in unitParent.GetComponentsInChildren<Status>())
        {
            // MovePoint ���̃��j�b�g�ȊO�͏��O
            if (status.type != Type.Unit) continue;

            if (_UnitSetting.UnitDataMap.TryGetValue(status.kind, out UnitData data))
                data.ApplyToStatus(status, status.Level);  // Lv �̓f�t�H���g Lv1
            else
                Debug.LogWarning($"[GameGenerater] Kind:{status.kind} ��UnitData�����o�^�ł�");
        }
    }

    // ����������������������������������������������������������������������������������������������������������������������������������
    //  ���������ݒ�
    // ����������������������������������������������������������������������������������������������������������������������������������

    /// <summary>
    /// �����z�z�������Z�b�g����iGameReference �����j�B
    /// ���l�̓}�W�b�N�i���o�[�ɂ��Ȃ��i�݌v����5�j�B
    /// </summary>
    private void InitResources(FactionState.ResourceData res)
    {
        const int InitWood = 100;
        const int InitStone = 100;
        const int InitWater = 50;
        const int InitPlank = 50;
        const int InitCutStone = 50;
        const int InitBread = 60;
        const int InitCitizen = 5;

        res.Wood = InitWood;
        res.Stone = InitStone;
        res.Water = InitWater;
        res.Plank = InitPlank;
        res.CutStone = InitCutStone;
        res.Bread = InitBread;
        res.Citizen = InitCitizen;
    }

    // ����������������������������������������������������������������������������������������������������������������������������������
    //  ���[�e�B���e�B
    // ����������������������������������������������������������������������������������������������������������������������������������
    private void CollectChildren(Transform parent, List<GameObject> result)
    {
        result.Clear();
        foreach (Transform child in parent)
            result.Add(child.gameObject);
    }
}
