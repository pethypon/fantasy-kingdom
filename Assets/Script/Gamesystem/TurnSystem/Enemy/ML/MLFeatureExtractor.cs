using System.Collections.Generic;
using UnityEngine;

// =====================================================================
//  MLFeatureExtractor — 盤面状態を固定長特徴ベクトルに変換
//
//  64次元の特徴ベクトルを生成:
//  [0-9]   自軍状態 (駒数, HP比, ATK比, クリスタルHP, AP, 経済, etc.)
//  [10-19] 敵軍状態 (同上, ただし視界内のみ)
//  [20-29] 相対的優位性 (駒数差, 価値差, 距離, 前線厚み)
//  [30-39] 行動コンテキスト (行動タイプ, AP消費, ターゲット情報)
//  [40-49] 位置・空間特徴 (クリスタル距離, 味方密度, 前線位置)
//  [50-59] 戦術特徴 (包囲度, 孤立度, 視界, 脅威度)
//  [60-63] メタ特徴 (ターン数, フェーズ, 戦略, 性格)
//
//  すべての値は -1.0 ～ +1.0 に正規化。
// =====================================================================
public static class MLFeatureExtractor
{
    // ================================================================
    //  盤面特徴の抽出（行動なし：盤面全体の評価用）
    // ================================================================
    public static float[] ExtractBoardFeatures(AIBoardState board)
    {
        var f = new float[MLBrain.InputSize];

        // ---- 自軍状態 [0-9] ----
        f[0] = Normalize(board.AliveEnemyUnits.Count, 0, 20);
        f[1] = AverageHPRatio(board.AliveEnemyUnits);
        f[2] = AverageATK(board.AliveEnemyUnits);
        f[3] = AverageDEF(board.AliveEnemyUnits);
        f[4] = board.EnemyCrystalMaxHP > 0
            ? (float)board.EnemyCrystalHP / board.EnemyCrystalMaxHP * 2f - 1f : 0f;
        f[5] = Normalize(board.EnemyAP, 0, 30);
        f[6] = EconomyScore(board);
        f[7] = Normalize(board.EnemySubCrystals, 0, 5);
        f[8] = Normalize(CountBuildings(board), 0, 20);
        f[9] = UnitDiversity(board.AliveEnemyUnits);

        // ---- 敵軍状態 [10-19] ----
        f[10] = Normalize(board.AlivePlayerUnits.Count, 0, 20);
        f[11] = AverageHPRatio(board.AlivePlayerUnits);
        f[12] = AverageATK(board.AlivePlayerUnits);
        f[13] = AverageDEF(board.AlivePlayerUnits);
        f[14] = board.PlayerCrystalVisible && board.PlayerCrystalHP >= 0
            ? Normalize(board.PlayerCrystalHP, 0, 200) : 0f;
        f[15] = board.PlayerCrystalVisible ? 1f : -1f;
        f[16] = board.IsFirstContact ? 1f : -1f;
        f[17] = Normalize(board.GetExplorationRatio(), 0f, 1f);
        f[18] = 0f; // 予約
        f[19] = 0f; // 予約

        // ---- 相対的優位性 [20-29] ----
        f[20] = Normalize(board.AliveEnemyUnits.Count - board.AlivePlayerUnits.Count, -10, 10);
        f[21] = Normalize(TotalPieceValue(board.AliveEnemyUnits) - TotalPieceValue(board.AlivePlayerUnits),
            -300, 300);
        f[22] = board.CanUsePlayerCrystalAsTarget()
            ? Normalize(Vector3.Distance(GetCentroid(board.AliveEnemyUnits), board.PlayerCrystalPos), 0, 40)
            : 0f;
        f[23] = FrontlineThickness(board.AliveEnemyUnits);
        f[24] = board.GetAdvantageRatio();
        f[25] = CrystalThreatLevel(board);
        f[26] = Normalize(NearCrystalGuards(board), 0, 6);
        f[27] = Normalize(NearCrystalThreats(board), 0, 6);
        f[28] = 0f; // 予約
        f[29] = 0f; // 予約

        // ---- メタ特徴 [60-63] ----
        f[60] = Normalize(board.TurnCount, 0, 60);
        f[61] = board.TurnCount <= 10 ? -1f : (board.TurnCount <= 20 ? 0f : 1f); // フェーズ
        f[62] = 0f; // 戦略（MLIntegrationで設定）
        f[63] = 0f; // 性格（MLIntegrationで設定）

        return f;
    }

    // ================================================================
    //  行動特徴の追加抽出（盤面特徴に行動情報を上書き）
    // ================================================================
    public static float[] ExtractActionFeatures(AIBoardState board, AIAction action)
    {
        var f = ExtractBoardFeatures(board);
        if (action == null) return f;
        OverlayActionFeatures(f, board, action);
        return f;
    }

    /// <summary>
    /// 盤面特徴が既にセット済みのバッファに行動特徴を上書きする（高速版）。
    /// boardFeaturesBase を f にコピーしてから行動部分のみ上書き。
    /// ホットパスで ExtractBoardFeatures() の再計算を完全に回避する。
    /// </summary>
    public static void OverlayActionFeaturesFromCache(float[] f, float[] boardFeaturesBase, AIBoardState board, AIAction action)
    {
        System.Array.Copy(boardFeaturesBase, 0, f, 0, Mathf.Min(boardFeaturesBase.Length, f.Length));
        if (action != null)
            OverlayActionFeatures(f, board, action);
    }

    /// <summary>行動依存の特徴 [30-59] をバッファに書き込む</summary>
    static void OverlayActionFeatures(float[] f, AIBoardState board, AIAction action)
    {
        // ---- 行動コンテキスト [30-39] ----
        f[30] = EncodeActionType(action.ActionType);
        f[31] = Normalize(action.APCost, 0, 10);
        f[32] = action.TargetUnit != null
            ? (float)action.TargetUnit.HP / Mathf.Max(1, action.TargetUnit.MaxHP) * 2f - 1f
            : 0f;
        f[33] = action.TargetUnit != null
            ? Normalize(action.TargetUnit.ATK, 0, 50) : 0f;
        f[34] = action.Unit != null
            ? (float)action.Unit.HP / Mathf.Max(1, action.Unit.MaxHP) * 2f - 1f : 0f;
        f[35] = EstimatedDamageRatio(action);
        f[36] = action.ActionType == AIActionType.SkillUse && action.Skill != null
            ? Normalize(action.Skill.Multiplier, 0, 3) : 0f;
        f[37] = action.ActionType == AIActionType.Build
            ? EncodeFacility(action.Facility) : 0f;
        f[38] = action.ActionType == AIActionType.Summon
            ? EncodeUnitKind(action.SummonKind) : 0f;
        f[39] = CanKillTarget(action) ? 1f : -1f;

        // ---- 位置・空間特徴 [40-49] ----
        f[40] = Normalize(Vector3.Distance(action.TargetPos, board.EnemyCrystalPos), 0, 40);
        f[41] = board.CanUsePlayerCrystalAsTarget()
            ? Normalize(Vector3.Distance(action.TargetPos, board.PlayerCrystalPos), 0, 40)
            : 0f;
        f[42] = AllyDensityAt(action.TargetPos, board.AliveEnemyUnits);
        f[43] = EnemyDensityAt(action.TargetPos, board.AlivePlayerUnits);
        f[44] = action.Unit != null
            ? Normalize(Vector3.Distance(action.Unit.transform.position, action.TargetPos), 0, 15)
            : 0f;
        f[45] = IsRetreatDirection(action, board) ? 1f : -1f;
        f[46] = IsAdvanceDirection(action, board) ? 1f : -1f;
        f[47] = 0f;
        f[48] = 0f;
        f[49] = 0f;

        // ---- 戦術特徴 [50-59] ----
        f[50] = SurroundingLevel(action, board);
        f[51] = IsolationLevel(action, board);
        f[52] = action.Unit != null ? Normalize(board.EstimateNewVisionCells(action.TargetPos), 0, 20) : 0f;
        f[53] = CounterDangerEstimate(action, board);
        f[54] = action.ActionType == AIActionType.Attack && action.TargetUnit != null
            ? TargetImportance(action.TargetUnit) : 0f;
        f[55] = action.ActionType == AIActionType.Support ? 1f : -1f;
        f[56] = StatusEffectAdvantage(action);
        f[57] = 0f;
        f[58] = 0f;
        f[59] = 0f;
    }

    // ================================================================
    //  正規化ヘルパー (-1 ～ +1)
    // ================================================================
    static float Normalize(float value, float min, float max)
    {
        if (max <= min) return 0f;
        return Mathf.Clamp((value - min) / (max - min) * 2f - 1f, -1f, 1f);
    }

    static float AverageHPRatio(List<Status> units)
    {
        if (units.Count == 0) return 0f;
        float sum = 0f;
        int count = 0;
        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u.MaxHP <= 0) continue;
            sum += (float)u.HP / u.MaxHP;
            count++;
        }
        return count > 0 ? sum / count * 2f - 1f : 0f;
    }

    static float AverageATK(List<Status> units)
    {
        if (units.Count == 0) return 0f;
        float sum = 0f;
        int count = 0;
        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            sum += u.ATK;
            count++;
        }
        return count > 0 ? Normalize(sum / count, 0, 50) : 0f;
    }

    static float AverageDEF(List<Status> units)
    {
        if (units.Count == 0) return 0f;
        float sum = 0f;
        int count = 0;
        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            sum += u.DEF;
            count++;
        }
        return count > 0 ? Normalize(sum / count, 0, 40) : 0f;
    }

    static float EconomyScore(AIBoardState board)
    {
        if (board.EnemyResources == null) return 0f;
        var r = board.EnemyResources;
        float score = r.Wood + r.Stone + r.Water + r.Plank + r.CutStone +
                      r.Iron + r.Bread + r.Citizen * 20f;
        return Normalize(score, 0, 500);
    }

    static int CountBuildings(AIBoardState board)
    {
        int count = 0;
        if (board.EnemyBuildingCounts != null)
        {
            foreach (var kvp in board.EnemyBuildingCounts)
                count += kvp.Value;
        }
        return count;
    }

    static float UnitDiversity(List<Status> units)
    {
        var kinds = new HashSet<Kind>();
        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            kinds.Add(u.kind);
        }
        return Normalize(kinds.Count, 0, 12);
    }

    static float TotalPieceValue(List<Status> units)
    {
        float value = 0f;
        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            value += AIBoardEvaluator.GetPieceValuePublic(u);
        }
        return value;
    }

    static Vector3 GetCentroid(List<Status> units)
    {
        if (units.Count == 0) return Vector3.zero;
        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            sum += u.transform.position;
            count++;
        }
        return count > 0 ? sum / count : Vector3.zero;
    }

    static float FrontlineThickness(List<Status> units)
    {
        if (units.Count <= 1) return -1f;
        var centroid = GetCentroid(units);
        float avgDist = 0f;
        int count = 0;
        foreach (var u in units)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            avgDist += Vector3.Distance(u.transform.position, centroid);
            count++;
        }
        if (count == 0) return -1f;
        avgDist /= count;
        // 密集度: 近いほど +1、分散してるほど -1
        return Normalize(10f - avgDist, 0, 10);
    }

    static float CrystalThreatLevel(AIBoardState board)
    {
        int threats = 0;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(pu.transform.position, board.EnemyCrystalPos) < 5f)
                threats++;
        }
        return Normalize(threats, 0, 5);
    }

    static int NearCrystalGuards(AIBoardState board)
    {
        int count = 0;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(u.transform.position, board.EnemyCrystalPos) < 5f)
                count++;
        }
        return count;
    }

    static int NearCrystalThreats(AIBoardState board)
    {
        int count = 0;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(pu.transform.position, board.EnemyCrystalPos) < 5f)
                count++;
        }
        return count;
    }

    // ================================================================
    //  行動エンコーディング
    // ================================================================
    static float EncodeActionType(AIActionType type)
    {
        switch (type)
        {
            case AIActionType.Attack:      return 1.0f;
            case AIActionType.SkillUse:    return 0.8f;
            case AIActionType.Surround:    return 0.6f;
            case AIActionType.Move:        return 0.2f;
            case AIActionType.Support:     return 0.0f;
            case AIActionType.Retreat:     return -0.2f;
            case AIActionType.DefenseRepos:return -0.4f;
            case AIActionType.Build:       return -0.6f;
            case AIActionType.Summon:      return -0.4f;
            case AIActionType.SubCrystal:  return -0.8f;
            case AIActionType.Wait:        return -1.0f;
            default: return 0f;
        }
    }

    static float EncodeFacility(FacilityKind f)
    {
        // 経済系: 正、軍事系: 負
        switch (f)
        {
            case FacilityKind.Well:        return -0.8f;
            case FacilityKind.LoggingCamp: return -0.6f;
            case FacilityKind.Quarry:      return -0.4f;
            case FacilityKind.Field:       return -0.2f;
            case FacilityKind.Mine:        return 0.0f;
            case FacilityKind.House:       return 0.2f;
            case FacilityKind.LumberMill:  return 0.4f;
            case FacilityKind.StoneWorks:  return 0.5f;
            case FacilityKind.Bakery:      return 0.6f;
            case FacilityKind.Smelter:     return 0.7f;
            default: return 0f;
        }
    }

    static float EncodeUnitKind(Kind k)
    {
        switch (k)
        {
            case Kind.Knight:       return 0.0f;
            case Kind.Archer:       return 0.2f;
            case Kind.Magic:        return 0.4f;
            case Kind.Assassin:     return 0.6f;
            case Kind.Scout:        return -0.4f;
            case Kind.Priest:       return -0.6f;
            case Kind.Guardian:     return -0.2f;
            case Kind.Crossbow:     return 0.3f;
            case Kind.Magicsniper:  return 0.8f;
            case Kind.Bomber:       return 1.0f;
            default: return 0f;
        }
    }

    // ================================================================
    //  戦術ヘルパー
    // ================================================================

    static float EstimatedDamageRatio(AIAction action)
    {
        if (action.Unit == null || action.TargetUnit == null) return 0f;
        int dmg = Mathf.Max(0, 1 + (action.Unit.ATK / 6) +
            ((action.Unit.ATK / 2) - (action.TargetUnit.DEF / 4)));
        return Normalize(dmg, 0, action.TargetUnit.MaxHP);
    }

    static bool CanKillTarget(AIAction action)
    {
        if (action.Unit == null || action.TargetUnit == null) return false;
        int dmg = Mathf.Max(0, 1 + (action.Unit.ATK / 6) +
            ((action.Unit.ATK / 2) - (action.TargetUnit.DEF / 4)));
        return dmg >= action.TargetUnit.HP;
    }

    static float AllyDensityAt(Vector3 pos, List<Status> allies)
    {
        int count = 0;
        foreach (var u in allies)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(u.transform.position, pos) < 3f)
                count++;
        }
        return Normalize(count, 0, 5);
    }

    static float EnemyDensityAt(Vector3 pos, List<Status> enemies)
    {
        int count = 0;
        foreach (var u in enemies)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(u.transform.position, pos) < 3f)
                count++;
        }
        return Normalize(count, 0, 5);
    }

    static bool IsRetreatDirection(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return false;
        float curDist = Vector3.Distance(action.Unit.transform.position, board.EnemyCrystalPos);
        float newDist = Vector3.Distance(action.TargetPos, board.EnemyCrystalPos);
        return newDist < curDist;
    }

    static bool IsAdvanceDirection(AIAction action, AIBoardState board)
    {
        if (action.Unit == null || !board.CanUsePlayerCrystalAsTarget()) return false;
        float curDist = Vector3.Distance(action.Unit.transform.position, board.PlayerCrystalPos);
        float newDist = Vector3.Distance(action.TargetPos, board.PlayerCrystalPos);
        return newDist < curDist;
    }

    static float SurroundingLevel(AIAction action, AIBoardState board)
    {
        if (action.TargetUnit == null) return 0f;
        Vector3 tPos = action.TargetUnit.transform.position;
        int allies = 0;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(u.transform.position, tPos) < 3f)
                allies++;
        }
        return Normalize(allies, 0, 4);
    }

    static float IsolationLevel(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return 0f;
        float nearest = float.MaxValue;
        foreach (var u in board.AliveEnemyUnits)
        {
            if (u == null || !u.gameObject.activeInHierarchy || u == action.Unit) continue;
            float d = Vector3.Distance(action.TargetPos, u.transform.position);
            if (d < nearest) nearest = d;
        }
        // 近いほど -1 (安全)、遠いほど +1 (孤立)
        return nearest < float.MaxValue ? Normalize(nearest, 0, 15) : 1f;
    }

    static float CounterDangerEstimate(AIAction action, AIBoardState board)
    {
        if (action.Unit == null) return 0f;
        int threatsInRange = 0;
        foreach (var pu in board.AlivePlayerUnits)
        {
            if (pu == null || !pu.gameObject.activeInHierarchy) continue;
            if (Vector3.Distance(pu.transform.position, action.TargetPos) < 4f)
                threatsInRange++;
        }
        return Normalize(threatsInRange, 0, 4);
    }

    static float TargetImportance(Status target)
    {
        float value = AIBoardEvaluator.GetPieceValuePublic(target);
        return Normalize(value, 0, 200);
    }

    static float StatusEffectAdvantage(AIAction action)
    {
        if (action.Skill == null) return 0f;
        // デバフ付与スキルは高価値
        if (action.Skill.InflictDebuff != StatusEffectType.None)
            return 0.5f;
        if (action.Skill.GrantBuff != BuffType.None)
            return 0.3f;
        return 0f;
    }
}
