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

        // Enemy の資源獲得（ターン終了時）
        if (turngenerater.economysystem != null)
            turngenerater.economysystem.ProcessTurn(Team.Enemy);

        // Enemy の攻撃建築物による自動攻撃
        if (turngenerater.buildingAttackSystem != null)
            turngenerater.buildingAttackSystem.ProcessAttacks(Team.Enemy);

        // タイムリミット: 総合持ち時間チェック
        if (turngenerater.timeLimitSystem != null)
        {
            // 敵ターンでも少し時間消費（AI未実装なので最小限）
            turngenerater.timeLimitSystem.OnTurnEnd();

            if (turngenerater.timeLimitSystem.EnemyTimeRemaining <= 0f)
            {
                GameResult result = turngenerater.timeLimitSystem.JudgeByHP();
                turngenerater.ChangeState(new GameEndState(turngenerater, result));
                Debug.Log("[EnemyMove] 敵持ち時間切れ → ゲーム終了");
                return;
            }
        }

        // AI未実装: 即座にプレイヤーターンへ
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
