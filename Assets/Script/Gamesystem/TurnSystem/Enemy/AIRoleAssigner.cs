using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  UnitRole — 駒に割り当てるロール
// =====================================================================
public enum UnitRole
{
    Vanguard,       // 前衛: 敵に接近・攻撃
    Guardian,       // 護衛: BOSSやクリスタルの近くで防衛
    Striker,        // 突破: 高価値目標への集中攻撃
    Scout_Role,     // 偵察: 未探索エリアの情報収集（ScoutのKindと区別するため_Role付き）
    Support,        // 後方支援: 回復・バフ
    EconomyGuard,   // 経済保護: 建築物周辺の防衛
}

// =====================================================================
//  AIRoleAssigner — 局面に応じた駒のロール再割当
//  仕様:
//  ・駒に固定役割を持たせるのではなく、局面に応じて再評価する
//  ・BOSSの方針（TurnStrategy）に従って配分を制御する
//  ・毎ターン実行し、状況変化に応じてロールを動的に更新
// =====================================================================
public class AIRoleAssigner
{
    // 駒→ロールの割当（毎ターン更新）
    Dictionary<Status, UnitRole> _assignments = new Dictionary<Status, UnitRole>();

    /// <summary>現在の割当を取得</summary>
    public UnitRole GetRole(Status unit)
    {
        if (unit != null && _assignments.TryGetValue(unit, out var role))
            return role;
        return UnitRole.Vanguard; // デフォルト
    }

    /// <summary>
    /// ロール割当を更新（毎ターン呼び出し）
    /// 戦略・盤面・BOSSの存在に応じて全駒にロールを再割当する
    /// </summary>
    public void AssignRoles(AIBoardState board, TurnStrategy strategy, AIPersonality personality)
    {
        _assignments.Clear();

        var units = board.AliveEnemyUnits;
        if (units.Count == 0) return;

        // 分類用リスト
        var scouts = new List<Status>();
        var healers = new List<Status>();
        var ranged = new List<Status>();
        var melee = new List<Status>();
        var boss = new List<Status>();

        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (u.IsBoss) { boss.Add(u); continue; }

            switch (u.kind)
            {
                case Kind.Scout:
                    scouts.Add(u);
                    break;
                case Kind.Priest:
                    healers.Add(u);
                    break;
                case Kind.Archer:
                case Kind.Magic:
                case Kind.Crossbow:
                case Kind.Magicsniper:
                case Kind.Bomber:
                    ranged.Add(u);
                    break;
                default:
                    melee.Add(u);
                    break;
            }
        }

        // --- Scout → 偵察ロール（常に） ---
        foreach (var s in scouts)
            _assignments[s] = UnitRole.Scout_Role;

        // --- Priest → 後方支援（常に） ---
        foreach (var h in healers)
            _assignments[h] = UnitRole.Support;

        // --- BOSS → 戦略に応じて ---
        foreach (var b in boss)
        {
            if (personality.BossFrontlineRate > 0.6f)
                _assignments[b] = UnitRole.Vanguard;
            else
                _assignments[b] = UnitRole.Guardian;
        }

        // --- 戦略に応じたロール割当 ---
        switch (strategy)
        {
            case TurnStrategy.Assault:
            case TurnStrategy.ContactEngage:
                AssignAssaultRoles(melee, ranged, board);
                break;

            case TurnStrategy.CrystalDefense:
                AssignDefenseRoles(melee, ranged, board);
                break;

            case TurnStrategy.ScoutSearch:
                AssignSearchRoles(melee, ranged, board);
                break;

            case TurnStrategy.EconomyBuild:
                AssignEconomyRoles(melee, ranged, board);
                break;

            case TurnStrategy.RetreatRegroup:
                AssignRegroupRoles(melee, ranged, board);
                break;

            default: // Balanced
                AssignBalancedRoles(melee, ranged, board);
                break;
        }

        // ログ
        var roleCounts = new Dictionary<UnitRole, int>();
        foreach (var kvp in _assignments)
        {
            if (!roleCounts.ContainsKey(kvp.Value))
                roleCounts[kvp.Value] = 0;
            roleCounts[kvp.Value]++;
        }
        string roleStr = "";
        foreach (var kvp in roleCounts)
            roleStr += $"{kvp.Key}={kvp.Value} ";
        Debug.Log($"[AIRoleAssigner] ロール割当: {roleStr}");
    }

    // ---- 攻勢ロール ----
    void AssignAssaultRoles(List<Status> melee, List<Status> ranged, AIBoardState board)
    {
        // 近接: 前衛主体、1体はクリスタル護衛
        bool guardAssigned = false;
        foreach (var u in melee)
        {
            if (!guardAssigned && u.kind == Kind.Guardian)
            {
                _assignments[u] = UnitRole.Guardian;
                guardAssigned = true;
            }
            else
            {
                _assignments[u] = UnitRole.Vanguard;
            }
        }

        // 遠距離: 突破役
        foreach (var u in ranged)
            _assignments[u] = UnitRole.Striker;
    }

    // ---- 防衛ロール ----
    void AssignDefenseRoles(List<Status> melee, List<Status> ranged, AIBoardState board)
    {
        foreach (var u in melee)
            _assignments[u] = UnitRole.Guardian;

        // 遠距離も護衛寄り
        foreach (var u in ranged)
            _assignments[u] = UnitRole.Guardian;
    }

    // ---- 索敵ロール ----
    void AssignSearchRoles(List<Status> melee, List<Status> ranged, AIBoardState board)
    {
        // 一部の近接を前衛（Scout支援）、残りは護衛
        int vanguardCount = Mathf.Max(1, melee.Count / 2);
        for (int i = 0; i < melee.Count; i++)
        {
            _assignments[melee[i]] = i < vanguardCount ? UnitRole.Vanguard : UnitRole.Guardian;
        }

        // 遠距離は後方支援
        foreach (var u in ranged)
            _assignments[u] = UnitRole.Support;
    }

    // ---- 経済ロール ----
    void AssignEconomyRoles(List<Status> melee, List<Status> ranged, AIBoardState board)
    {
        // 近接は経済保護
        foreach (var u in melee)
            _assignments[u] = UnitRole.EconomyGuard;

        // 遠距離も経済保護
        foreach (var u in ranged)
            _assignments[u] = UnitRole.EconomyGuard;
    }

    // ---- 再編ロール ----
    void AssignRegroupRoles(List<Status> melee, List<Status> ranged, AIBoardState board)
    {
        // 全員護衛寄り
        foreach (var u in melee)
            _assignments[u] = UnitRole.Guardian;
        foreach (var u in ranged)
            _assignments[u] = UnitRole.Support;
    }

    // ---- バランスロール ----
    void AssignBalancedRoles(List<Status> melee, List<Status> ranged, AIBoardState board)
    {
        // 近接: 半分前衛、半分護衛
        int half = Mathf.Max(1, melee.Count / 2);
        for (int i = 0; i < melee.Count; i++)
        {
            _assignments[melee[i]] = i < half ? UnitRole.Vanguard : UnitRole.Guardian;
        }

        // 遠距離: 突破
        foreach (var u in ranged)
            _assignments[u] = UnitRole.Striker;
    }

    /// <summary>ロールに基づく行動評価補正</summary>
    public float GetRoleBonus(Status unit, AIAction action, AIBoardState board)
    {
        var role = GetRole(unit);
        float bonus = 0f;

        switch (role)
        {
            case UnitRole.Vanguard:
                if (action.ActionType == AIActionType.Move)
                {
                    float approach = GetApproachValue(action, board);
                    bonus += approach * 3f;
                }
                if (action.ActionType == AIActionType.Attack) bonus += 8f;
                if (action.ActionType == AIActionType.Surround) bonus += 6f;
                if (action.ActionType == AIActionType.Retreat) bonus -= 8f;
                break;

            case UnitRole.Guardian:
                // クリスタルに近い位置を好む
                if (action.ActionType == AIActionType.Move)
                {
                    float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (crystalDist < 5f) bonus += 10f;
                    else if (crystalDist > 8f) bonus -= 8f;
                }
                if (action.ActionType == AIActionType.DefenseRepos) bonus += 12f;
                if (action.ActionType == AIActionType.Attack) bonus += 5f;
                break;

            case UnitRole.Striker:
                if (action.ActionType == AIActionType.Attack) bonus += 12f;
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null && action.Skill.Multiplier > 0)
                    bonus += 10f;
                // 高価値目標に集中
                if (action.TargetUnit != null)
                {
                    if (action.TargetUnit.kind == Kind.Crystal) bonus += 15f;
                    if (action.TargetUnit.kind == Kind.King) bonus += 10f;
                    if (action.TargetUnit.kind == Kind.Priest) bonus += 8f;
                }
                break;

            case UnitRole.Scout_Role:
                if (action.ActionType == AIActionType.Move)
                {
                    int newCells = board.EstimateNewVisionCells(action.TargetPos);
                    bonus += Mathf.Min(newCells * 4f, 35f);
                }
                if (action.ActionType == AIActionType.Retreat) bonus -= 5f;
                break;

            case UnitRole.Support:
                if (action.ActionType == AIActionType.SkillUse && action.Skill != null)
                {
                    if (action.Skill.FixedHeal > 0) bonus += 12f;
                    if (action.Skill.GrantBuff != BuffType.None) bonus += 8f;
                }
                if (action.ActionType == AIActionType.Support) bonus += 10f;
                // 後方支援は前線に出すぎない
                if (action.ActionType == AIActionType.Move)
                {
                    float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (crystalDist > 10f) bonus -= 8f;
                }
                break;

            case UnitRole.EconomyGuard:
                // 自陣近くに留まる
                if (action.ActionType == AIActionType.Move)
                {
                    float crystalDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
                    if (crystalDist < 6f) bonus += 8f;
                    else if (crystalDist > 10f) bonus -= 10f;
                }
                break;
        }

        return bonus;
    }

    static float GetApproachValue(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return 0f;
        // 視界内の敵への接近
        float best = 0f;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            float dBefore = Vector3.Distance(action.Unit.transform.position, pu.transform.position);
            float dAfter = Vector3.Distance(action.TargetPos, pu.transform.position);
            float a = dBefore - dAfter;
            if (a > best) best = a;
        }
        return best;
    }
}
