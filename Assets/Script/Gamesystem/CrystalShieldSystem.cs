using UnityEngine;

/// <summary>
/// メインクリスタルの無敵シールド管理。
/// HP50%以下になった最初の1回だけ5ターン無敵を付与する。
/// </summary>
public class CrystalShieldSystem : MonoBehaviour
{
    private const int ShieldTurns = 5;

    private int playerShieldTurns;
    private int enemyShieldTurns;
    private bool playerTriggered;
    private bool enemyTriggered;

    public void TickTurn(Team team)
    {
        if (team == Team.Player && playerShieldTurns > 0) playerShieldTurns--;
        if (team == Team.Enemy && enemyShieldTurns > 0) enemyShieldTurns--;
    }

    public bool IsInvincible(Status target)
    {
        if (target == null || target.kind != Kind.Crystal) return false;
        return target.team == Team.Player ? playerShieldTurns > 0 : enemyShieldTurns > 0;
    }

    public void TryActivate(Status crystal)
    {
        if (crystal == null || crystal.kind != Kind.Crystal) return;

        int halfHp = CrystalSystem.CrystalHP / 2;
        if (crystal.HP > halfHp) return;

        if (crystal.team == Team.Player)
        {
            if (playerTriggered) return;
            playerTriggered = true;
            playerShieldTurns = ShieldTurns;
            Debug.Log("[CrystalShield] Player crystal shield activated (5 turns)");
        }
        else if (crystal.team == Team.Enemy)
        {
            if (enemyTriggered) return;
            enemyTriggered = true;
            enemyShieldTurns = ShieldTurns;
            Debug.Log("[CrystalShield] Enemy crystal shield activated (5 turns)");
        }
    }

    public int GetRemainingTurns(Team team)
    {
        return team == Team.Player ? playerShieldTurns : enemyShieldTurns;
    }
}
