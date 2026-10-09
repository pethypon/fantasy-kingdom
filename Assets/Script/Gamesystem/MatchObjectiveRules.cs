/// <summary>Shared decisive-target classification and factual match-result explanations.</summary>
public static class MatchObjectiveRules
{
    public static bool IsDecisive(Kind kind, Team team)
        => (team == Team.Player || team == Team.Enemy) && (kind == Kind.King || kind == Kind.Crystal);

    public static string DefeatReason(GameResult result, Status target)
    {
        bool victory = result == GameResult.Win;
        string fallback = victory ? "勝利条件が成立しました" : "敗北条件が成立しました";
        if (target == null || !IsDecisive(target.kind, target.team)
            || target.team != (victory ? Team.Enemy : Team.Player)) return fallback;
        if (target.kind == Kind.King)
            return victory ? "敵の王を撃破しました" : "味方の王が撃破されました";
        return victory ? "敵のメインクリスタルを破壊しました" : "味方のメインクリスタルが破壊されました";
    }

    public static string TimeUpReason(GameResult result, GameSystems systems)
    {
        if (result != GameResult.TimeUpWin && result != GameResult.TimeUpLose && result != GameResult.TimeUpDraw) return "";
        var crystals = systems?.CrystalSystem;
        var player = crystals != null && crystals.Playercrystal != null
            ? crystals.Playercrystal.GetComponentInChildren<Status>() : null;
        var enemy = crystals != null && crystals.Enemycrystal != null
            ? crystals.Enemycrystal.GetComponentInChildren<Status>() : null;
        if (player == null || enemy == null)
            return result == GameResult.TimeUpDraw ? "判定に必要なクリスタル情報がなく、引き分けになりました"
                : result == GameResult.TimeUpWin ? "総持ち時間切れの判定で勝利しました" : "総持ち時間切れの判定で敗北しました";
        if (result == GameResult.TimeUpDraw)
            return "総持ち時間切れ：メインクリスタルと王の残HP率がともに同率のため引き分け";
        string criterion = CompareHealthRatios(player, enemy) == 0 ? "クリスタルは同率、王" : "メインクリスタル";
        return "総持ち時間切れ：" + criterion + "の残HP率が敵より"
            + (result == GameResult.TimeUpWin ? "高かったため勝利" : "低かったため敗北");
    }

    public static int CompareHealthRatios(Status left, Status right)
    {
        // Integer cross-products preserve exact ties and fit in Int64 for Int32 HP values.
        long leftMax = left != null && left.MaxHP > 0 ? left.MaxHP : 1;
        long rightMax = right != null && right.MaxHP > 0 ? right.MaxHP : 1;
        long leftHP = left != null && left.MaxHP > 0 ? left.HP : 0;
        long rightHP = right != null && right.MaxHP > 0 ? right.HP : 0;
        return (leftHP * rightMax).CompareTo(rightHP * leftMax);
    }
}
