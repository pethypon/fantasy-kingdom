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
<<<<<<< HEAD
        // ƒ^[ƒ“ƒJƒEƒ“ƒgXV
        turngenerater.Turn++;

        // AP ƒŠƒZƒbƒgiFactionState.ResetAPForTurn ‚ª Reset+Plus-Minus ‚ðŒvŽZj
        turngenerater.apsystem.ResetAP(Team.Player);

        // ”æ˜JƒŠƒZƒbƒg
=======
        // ã‚¿ãƒ¼ãƒ³ã‚«ã‚¦ãƒ³ãƒˆæ›´æ–°
        turngenerater.Turn++;

        // AP ãƒªã‚»ãƒƒãƒˆï¼ˆFactionState.ResetAPForTurn ãŒ Reset+Plus-Minus ã‚’è¨ˆç®—ï¼‰
        turngenerater.apsystem.ResetAP(Team.Player);

        // ç–²åŠ´ãƒªã‚»ãƒƒãƒˆ
>>>>>>> ef5d789a27d65a3019e0abf6f523ef6eed232b13
        turngenerater.apsystem.ResetFatigue(unitset.PlayerUnit);

        turngenerater.ChangeState(new PlayerMove(turngenerater, unitclick,
            attackpoint, battlesystem, visiongenerater,
            movegenerater, mapcreate, crystalsystem, unitset));
    }

    public void Update() { }
    public void Exit() { }
}
