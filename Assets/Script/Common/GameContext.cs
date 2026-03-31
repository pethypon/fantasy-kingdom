/// <summary>
/// ゲーム全体のシステム参照を束ねる不変コンテキスト。
/// 状態クラス（StateCore）のコンストラクタに個別に9引数渡す代わりに、
/// このオブジェクト1つを渡すことで結合度を大幅に下げる。
///
/// 設計原則:
///   - 読み取り専用プロパティのみ（外部からの差し替え不可）
///   - TurnGenerater.StartFirstTurn() で一度だけ生成される
///   - 新システム追加時はここにプロパティを1行追加するだけで全状態クラスからアクセス可能
/// </summary>
public class GameContext
{
    // ================================================================
    //  コアシステム参照（読み取り専用）
    // ================================================================
    public TurnGenerater TurnGen { get; }
    public UnitClick UnitClick { get; }
    public AttackPointt AttackPoint { get; }
    public BattleSystem BattleSystem { get; }
    public VisionGenerater VisionGen { get; }
    public MoveGererater MoveGen { get; }
    public MapCreate MapCreate { get; }
    public CrystalSystem CrystalSystem { get; }
    public UnitSetting UnitSetting { get; }

    public GameContext(
        TurnGenerater turnGen,
        UnitClick unitClick,
        AttackPointt attackPoint,
        BattleSystem battleSystem,
        VisionGenerater visionGen,
        MoveGererater moveGen,
        MapCreate mapCreate,
        CrystalSystem crystalSystem,
        UnitSetting unitSetting)
    {
        TurnGen = turnGen;
        UnitClick = unitClick;
        AttackPoint = attackPoint;
        BattleSystem = battleSystem;
        VisionGen = visionGen;
        MoveGen = moveGen;
        MapCreate = mapCreate;
        CrystalSystem = crystalSystem;
        UnitSetting = unitSetting;
    }

    /// <summary>視界再計算のショートハンド</summary>
    public void RefreshVision()
    {
        VisionGen.VisionPoint(MapCreate, MoveGen, CrystalSystem);
    }

    /// <summary>指定チームのターン終了共通処理（資源獲得＋建築物攻撃）</summary>
    public void ProcessTurnEnd(Team team)
    {
        if (TurnGen.economysystem != null)
            TurnGen.economysystem.ProcessTurn(team);
        if (TurnGen.buildingAttackSystem != null)
            TurnGen.buildingAttackSystem.ProcessAttacks(team);
    }

    /// <summary>残留 MovePoint / AttackPoint をクリアする</summary>
    public void ClearPointMarkers()
    {
        MoveGen.MoveReset();
        AttackPoint.AtkpDestroy();
    }
}
