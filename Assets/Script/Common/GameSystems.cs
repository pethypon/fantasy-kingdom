/// <summary>
/// 全ゲームシステムへの参照を一元管理するコンテナ。
/// TurnGenerator の 20+ 公開フィールドを集約し、結合度を低減する。
/// 各ステートクラスは GameSystems 経由でシステムにアクセスする。
/// </summary>
public class GameSystems
{
    // ================================================================
    //  マップ・ユニット基盤
    // ================================================================
    public MapCreate MapCreate { get; set; }
    public CrystalSystem CrystalSystem { get; set; }
    public UnitSetting UnitSetting { get; set; }

    // ================================================================
    //  コアゲームシステム
    // ================================================================
    public MoveGenerator MoveGenerator { get; set; }
    public AttackGenerator AttackGenerator { get; set; }
    public BattleSystem BattleSystem { get; set; }
    public VisionGenerator VisionGenerator { get; set; }
    public UnitClick UnitClick { get; set; }
    public SkillSystem SkillSystem { get; set; }

    // ================================================================
    //  経済・建築・召喚
    // ================================================================
    public APSystem APSystem { get; set; }
    public BuildSystem BuildSystem { get; set; }
    public SummonSystem SummonSystem { get; set; }
    public EconomySystem EconomySystem { get; set; }
    public BuildingAttackSystem BuildingAttackSystem { get; set; }
    public SubCrystalSystem SubCrystalSystem { get; set; }
    public FactionState FactionState { get; set; }
    public TerritorySystem TerritorySystem { get; set; }
    public DungeonSystem DungeonSystem { get; set; }
    public WildBossSystem WildBossSystem { get; set; }

    // ================================================================
    //  タイマー・AI
    // ================================================================
    public TimerSystem TimerSystem { get; set; }
    public AICommander AICommander { get; set; }

    // ================================================================
    //  UI
    // ================================================================
    public UnitPanelUI UnitPanelUI { get; set; }
    public DamagePreviewUI DamagePreviewUI { get; set; }
    public InputHintUI InputHintUI { get; set; }
    public MoveUndoSystem MoveUndoSystem { get; set; }

    // ================================================================
    //  便利メソッド
    // ================================================================

    /// <summary>
    /// 視界の再計算（ショートハンド）。
    /// RefreshVision は「盤面が変化したので今すぐ視界を更新したい」という意図で
    /// 呼ばれる（移動・召喚・撃破・強敵の手下スポーン・ターン遷移など）。
    /// VisionPoint には同一フレーム内の重複計算を抑止するキャッシュがあるため、
    /// ターン遷移が1フレーム内に連鎖するとこの更新が握り潰され、
    /// 視界外の駒・強敵の手下が霧を貫通して表示されてしまう。
    /// そのため必ず MarkVisionDirty でキャッシュを無効化してから再計算する。
    /// </summary>
    public void RefreshVision()
    {
        if (VisionGenerator != null && MapCreate != null && MoveGenerator != null && CrystalSystem != null)
        {
            // 呼び出し側は「状態が変わったから再計算したい」場面で呼ぶため、
            // 同一フレームキャッシュに弾かれないよう必ず dirty を立てる
            VisionGenerator.MarkVisionDirty();
            VisionGenerator.VisionPoint(MapCreate, MoveGenerator, CrystalSystem);
        }
    }
}
