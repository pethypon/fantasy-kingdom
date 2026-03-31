using UnityEngine;

public class EnemyMove : StateCore
{
    private TurnGenerater turngenerater;
    private UnitClick unitclick;
    private AttackPointt attackpoint;
    private BattleSystem battlesystem;
    private VisionGenerater visiongenerater;
    private MoveGererater movegenerater;
    private MapCreate mapcreate;
    private CrystalSystem crystalsystem;
    private UnitSetting unitset;

    public EnemyMove(
        TurnGenerater turngenerater, UnitClick unitclick, AttackPointt attackpoint,
        BattleSystem battlesystem, VisionGenerater visiongenerater, MoveGererater movegenerater,
        MapCreate mapcreate, CrystalSystem crystalsystem, UnitSetting unitset)
    {
        this.turngenerater = turngenerater;
        this.unitclick = unitclick;
        this.attackpoint = attackpoint;
        this.battlesystem = battlesystem;
        this.visiongenerater = visiongenerater;
        this.movegenerater = movegenerater;
        this.mapcreate = mapcreate;
        this.crystalsystem = crystalsystem;
        this.unitset = unitset;
    }

    public void Entry()
    {
        visiongenerater.VisionPoint(mapcreate, movegenerater, crystalsystem);

        // ========================================
        //  AI指揮官による全体指揮実行
        // ========================================
        if (turngenerater.aiCommander != null)
        {
            turngenerater.aiCommander.ExecuteTurn();
        }
        else
        {
            Debug.LogWarning("[EnemyMove] AICommander未初期化 — AI行動スキップ");
        }

        // 視界再計算（AI行動後）
        visiongenerater.VisionPoint(mapcreate, movegenerater, crystalsystem);

        // Special Ability: ターン終了時処理（応急処置、聖域反応）
        SpecialAbilitySystem.OnTurnEnd(unitset.EnemyUnit);

        // Enemy の資源獲得（ターン終了時）
        if (turngenerater.economysystem != null)
            turngenerater.economysystem.ProcessTurn(Team.Enemy);

        // Enemy の攻撃建築物による自動攻撃
        if (turngenerater.buildingAttackSystem != null)
            turngenerater.buildingAttackSystem.ProcessAttacks(Team.Enemy);

        // タイマー停止
        if (turngenerater.timerSystem != null)
            turngenerater.timerSystem.StopTurn();

        // プレイヤーターンへ
        turngenerater.ChangeState(new PlayerStart(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));

        Debug.Log("[EnemyMove] 敵ターン終了");
    }

    public void Update() { }

    public void Exit()
    {
        visiongenerater.VisionPoint(mapcreate, movegenerater, crystalsystem);
    }
}
