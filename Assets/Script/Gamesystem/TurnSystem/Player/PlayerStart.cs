using UnityEngine;

public class PlayerStart : StateCore
{
    private TurnGenerater turngenerater;
    private UnitClick unitclick;
    public AttackPointt attackpoint;
    public BattleSystem battlesystem;
    public VisionGenerater visiongenerater;
    public MoveGererater movegenerater;
    public MapCreate mapcreate;
    public CrystalSystem crystalsystem;
    public UnitSetting unitset;

    public PlayerStart(TurnGenerater turngenerater, UnitClick unitclick,
        AttackPointt attackpoint, BattleSystem battlesystem,
        VisionGenerater visiongenerater, MoveGererater movegenerater,
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
        // ターンカウント更新
        turngenerater.Turn++;

        // クリスタルシールドのターン経過（両陣営）
        BattleSystem.TickCrystalShields(crystalsystem.Playercrystal);
        BattleSystem.TickCrystalShields(crystalsystem.Enemycrystal);

        // 状態異常ティック（DoTダメージ + ターン経過）
        StatusEffectSystem.TickAllUnits(unitset.PlayerUnit);

        // AP リセット（FactionState.ResetAPForTurn で Reset+Plus-Minus を計算）
        turngenerater.apsystem.ResetAP(Team.Player);

        // 疲労リセット
        turngenerater.apsystem.ResetFatigue(unitset.PlayerUnit);

        // サブクリスタル返却待ちタイマー処理
        if (turngenerater.subCrystalSystem != null)
            turngenerater.subCrystalSystem.TickPendingReturns(Team.Player);
        else
            Debug.LogWarning("[PlayerStart] subCrystalSystem が null のため TickPendingReturns をスキップ");

        // タイマー開始（プレイヤーターン）
        if (turngenerater.timerSystem != null)
            turngenerater.timerSystem.StartTurn(Team.Player);

        turngenerater.ChangeState(new PlayerMove(turngenerater, unitclick,
            attackpoint, battlesystem, visiongenerater,
            movegenerater, mapcreate, crystalsystem, unitset));
    }

    public void Update() { }
    public void Exit() { }
}
