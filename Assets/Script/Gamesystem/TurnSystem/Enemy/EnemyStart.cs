using UnityEngine;

public class EnemyStart : StateCore
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

    public EnemyStart(
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
        Debug.Log("[EnemyStart] 敵ターン開始");

        // プレイヤーの残留MovePoint/AttackPointをクリア
        movegenerater.MoveReset();
        turngenerater.attackpoint.AtkpDestroy();

        // 選択状態もクリア
        turngenerater.SelectUnit = null;

        // UnitPanelUI を非表示
        if (turngenerater.unitPanelUI != null)
            turngenerater.unitPanelUI.Hide();

        // InputHintUI を敵ターン表示に
        if (turngenerater.inputHintUI != null)
            turngenerater.inputHintUI.SetHints(InputHintUI.Hints.EnemyTurn);

        // 敵ターンバナー表示
        EnemyTurnBannerUI.Show();

        // クリスタルシールドのターン経過（敵陣営）
        BattleSystem.TickCrystalShields(crystalsystem.Enemycrystal);

        // 状態異常ティック（DoTダメージ + ターン経過 + シールド + スキルクールダウン）
        StatusEffectSystem.TickAllUnits(unitset.EnemyUnit);

        // Special Ability: ターン開始時処理（フラグリセット + 浄化光輪）
        SpecialAbilitySystem.OnTurnStart(unitset.EnemyUnit);

        turngenerater.apsystem.ResetAP(Team.Enemy);
        turngenerater.apsystem.ResetFatigue(unitset.EnemyUnit);

        // サブクリスタル返却待ちタイマー処理
        if (turngenerater.subCrystalSystem != null)
            turngenerater.subCrystalSystem.TickPendingReturns(Team.Enemy);

        // タイマー開始（敵ターン）
        if (turngenerater.timerSystem != null)
            turngenerater.timerSystem.StartTurn(Team.Enemy);

        turngenerater.ChangeState(new EnemyMove(
            turngenerater, unitclick, attackpoint, battlesystem,
            visiongenerater, movegenerater, mapcreate, crystalsystem, unitset));
    }

    public void Update() { }
    public void Exit() { }
}
