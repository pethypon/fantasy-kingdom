using System;
using System.Collections.Generic;

/// <summary>R4's 40 immutable design principles. No rule, AP value, personality or emotional variable is added.</summary>
public static class AIStrategyKnowledgeCatalog
{
    public const int Version = 1;
    static readonly IReadOnlyList<AIStrategyPrinciple> all;
    static readonly Dictionary<string, AIStrategyPrinciple> index;
    public static IReadOnlyList<AIStrategyPrinciple> All => all;
    public static bool TryGet(string id, out AIStrategyPrinciple principle)
    { if (id == null) { principle = null; return false; } return index.TryGetValue(id, out principle); }
    static Dictionary<string, AIStrategyPrinciple> BuildIndex()
    {
        var map = new Dictionary<string, AIStrategyPrinciple>(StringComparer.Ordinal);
        foreach (var value in all) map.Add(value.Id, value); return map;
    }
    static readonly AIOperationGoal[] offense = {
        AIOperationGoal.DestroyEnemyCrystal, AIOperationGoal.DestroySubCrystal, AIOperationGoal.EliminateEnemyForce,
        AIOperationGoal.WeakenEnemyForce, AIOperationGoal.BreakEnemyDefense, AIOperationGoal.FlankEnemyForce,
        AIOperationGoal.SurroundEnemyForce, AIOperationGoal.ForceEnemyRetreat, AIOperationGoal.CreateDiversion,
        AIOperationGoal.ExploitWeakFront };
    static readonly AIOperationGoal[] defense = {
        AIOperationGoal.DefendOwnCrystal, AIOperationGoal.DefendSubCrystal, AIOperationGoal.HoldTerritory,
        AIOperationGoal.DelayEnemy };
    static readonly AIOperationGoal[] economy = {
        AIOperationGoal.EconomicRecovery, AIOperationGoal.SecureResourceArea, AIOperationGoal.CaptureTerritory };
    static readonly AIOperationGoal[] information = {
        AIOperationGoal.ScoutRegion, AIOperationGoal.LocateEnemyMainForce, AIOperationGoal.DestroyEnemyCrystal,
        AIOperationGoal.BreakEnemyDefense, AIOperationGoal.FlankEnemyForce, AIOperationGoal.ClearDungeon,
        AIOperationGoal.ObtainArtifact };

    static AIStrategyKnowledgeCatalog()
    {
        all = Array.AsReadOnly(Build());
        if (!AIStrategyCatalogValidator.Validate(all, out string reason))
            throw new InvalidOperationException("R4 strategy catalog rejected: " + reason);
        index = BuildIndex();
    }

    static AIStrategyPrinciple[] Build()
    {
        // Explicit conditions for every ID; a partial observation does not satisfy the remaining predicates.
        return new[] {
            P("CH-01", "二重脅威", "一度の合法位置変更から重要な二対象への圧力を作る。", offense,
                R(T(AIStrategyPredicate.TwoImportantTargetsThreatened),T(AIStrategyPredicate.LegalMoveAvailable),T(AIStrategyPredicate.SharedAPAffordable)),
                "REPOSITION;THREATEN_TWO", "double-threat", "TwoOfficialThreatsAvailable", "BothLinesBlockedOrAPInvalid", "SplitDefense;AbandonTarget", "FOCUS_FIRE"),
            P("CH-02", "守備の過負荷", "観測済みの一守備役が同時に守れない複数地点を比較する。", offense,
                R(T(AIStrategyPredicate.DefenderOverloaded),F(AIStrategyPredicate.ReserveCanCover),T(AIStrategyPredicate.ObservedTargetAvailable)),
                "SPLIT_THREAT;EVALUATE_RESPONSES", "overload", "ObservedDefenseGapCreated", "ObservedReserveCoversBoth", "ReserveDeployment;DefenderReposition", "FOCUS_FIRE;SCOUT_FOR_ANSWER"),
            P("CH-03", "守備役の誘導", "安全な牽制後、実際の敵移動を観測してから守備の隙を利用する。", offense,
                R(T(AIStrategyPredicate.SafeDiversionAvailable),T(AIStrategyPredicate.KnownTerrainRouteAvailable),T(AIStrategyPredicate.ObservedTargetAvailable)),
                "DIVERSION;OBSERVE_RESPONSE", "diversion", "ObservedDefenderDisplaced", "ExpectedEnemyResponseAbsent", "IgnoreDiversion;CounterAttack", "ATTACK;REINFORCE"),
            P("CH-04", "開き攻撃", "前衛の移動後に正式な射線を再生成し、後衛の攻撃を可能にする。", offense,
                R(T(AIStrategyPredicate.FiringLineOpened),T(AIStrategyPredicate.LegalMoveAvailable),T(AIStrategyPredicate.SharedAPAffordable)),
                "OPEN_LINE;ATTACK", "open-line", "ActualMoveThenOfficialAttack", "HeightOrTerrainStillBlocksLine", "MoveTarget;BlockLine", "FIND_FIRING_POSITION"),
            P("CH-05", "予防的防衛", "観測した致命的な次手に対して正式な被害を減らす配置を選ぶ。", defense,
                R(T(AIStrategyPredicate.FatalThreatToObjective),T(AIStrategyPredicate.DefenseActionEffective)),
                "BLOCK_THREAT;DEFEND", "prevent-defeat", "FormalObjectiveRiskReduced", "DefenseDoesNotReduceRisk", "AlternateTarget;AdditionalPressure", "FORM_DEFENSE;DISENGAGE"),
            P("CH-06", "駒の活動性", "安全を保って合法な攻撃・移動・援護の選択肢を増やす。", Join(offense, defense),
                R(T(AIStrategyPredicate.ActivityImproves),T(AIStrategyPredicate.LegalMoveAvailable),F(AIStrategyPredicate.IsolationRisk)),
                "IMPROVE_POSITION", "activity", "OfficialActionCoverageIncreased", "ActorIsolatedOrLethalRisk", "ContestFiringPosition", "FIND_FIRING_POSITION"),
            P("CH-07", "テンポ／主導権", "同じ正式AP内で行動順の違いによる結果を比較する。", Join(offense, defense),
                R(T(AIStrategyPredicate.OrderedOutcomeImproves),T(AIStrategyPredicate.SharedAPAffordable)),
                "ORDER_ACTIONS", "action-order", "ActualImportantEffectOccursEarlier", "OrderInvalidatesRangeOrAP", "DisruptPreparedSequence", "HOLD" , AIStrategyCostModel.SharedActionSequenceAP),
            P("CH-08", "戦力交換", "予測損失と目的進捗を比べ、不可欠な守備役を交換しない。", offense,
                R(T(AIStrategyPredicate.TradeGainExceedsLoss),F(AIStrategyPredicate.EssentialDefenderLost),T(AIStrategyPredicate.CounterResponseKnown)),
                "TRADE_EVALUATE", "force-trade", "ActualLossAndObjectiveDeltaRecorded", "EssentialDefenseLost", "RefuseTrade;CounterCapture", "HOLD"),
            P("CH-09", "強制的な応答", "観測済みの重大な合法脅威に対する複数の応答を比較する。", offense,
                R(T(AIStrategyPredicate.ForcingThreatAvailable),F(AIStrategyPredicate.BetterEnemyReplyAvailable),T(AIStrategyPredicate.ObservedTargetAvailable)),
                "FORCE_RESPONSE", "forcing-threat", "ObservedOpponentResponseConstrained", "SuperiorOpponentReplyExists", "CounterThreat;SacrificeTarget", "IMPROVE_POSITION"),
            P("CH-10", "局所数的優位", "後方の敗北条件を守りつつ、合法参戦可能な戦力を一点に集める。", offense,
                R(T(AIStrategyPredicate.LocalParticipationAdvantage),T(AIStrategyPredicate.RearDefenseSafe),T(AIStrategyPredicate.SharedAPAffordable)),
                "CONCENTRATE;ATTACK", "local-concentration", "ActualLocalParticipantsOrDamageIncreased", "RearObjectiveExposed", "Retreat;ReinforceFront", "FORM_DEFENSE"),
            P("SH-01", "攻め駒の相互支援", "攻撃役が攻撃後も正式な援護を受けられる配置を選ぶ。", offense,
                R(T(AIStrategyPredicate.MutualSupportAvailable),T(AIStrategyPredicate.SupportCellAvailable),T(AIStrategyPredicate.SharedAPAffordable)),
                "FORM_SUPPORT;ATTACK", "mutual-support", "ActualLegalSupportThenAttack", "SupportActorAlreadyAssigned", "CutSupport;AreaAttack", "REPOSITION"),
            P("SH-02", "層状守備", "前衛・遠隔・回復の支援が塞がれない防衛線を組む。", defense,
                R(T(AIStrategyPredicate.ThreeLayerDefenseAvailable),T(AIStrategyPredicate.SupportCellAvailable),T(AIStrategyPredicate.ExistingDutySafe)),
                "FORM_DEFENSE", "layered-defense", "ActualMultipleDefenseLayersAvailable", "NarrowPassageBlocksHealer", "Flank;BreakFront", "ROTATE_FRONTLINE"),
            P("SH-03", "勝利経路の連続計画", "現在観測した敗北条件対象へ合法な連続手で到達する。", offense,
                R(T(AIStrategyPredicate.WinningLegalSequence),T(AIStrategyPredicate.ObservedTargetAvailable),T(AIStrategyPredicate.SharedAPAffordable),T(AIStrategyPredicate.CounterResponseKnown)),
                "FINISH_SEQUENCE", "legal-victory-sequence", "OfficialVictoryConditionReached", "TargetHiddenOrIntermediateStepFailed", "DefendObjective;InterruptSequence", "SCOUT_FOR_ANSWER", AIStrategyCostModel.SharedActionSequenceAP),
            P("SH-04", "攻守の転換", "新たな重大観測を根拠に攻守を切り替え、微差で往復しない。", Join(offense, defense),
                R(T(AIStrategyPredicate.NewlyObservedStrategicChange),T(AIStrategyPredicate.StableObjectiveAvailable)),
                "SWITCH_STANCE", "stance-change", "EvidenceBackedOperationRevision", "NoNewStrategicEvidence", "ChangePressureAgain", "HOLD"),
            P("SH-05", "役割の再投入", "現任務の安全を維持できる既存駒を有効な別任務へ再配分する。", Join(offense, defense),
                R(T(AIStrategyPredicate.ReassignmentImproves),T(AIStrategyPredicate.ExistingDutySafe),T(AIStrategyPredicate.LegalMoveAvailable)),
                "REASSIGN_ROLE", "role-reuse", "ActualAssignmentAndReachImproved", "OriginalDutyUnsafe", "PressureOriginalFront", "HOLD"),
            P("GO-01", "領域価値", "既知の地点が実際の資源・視界・進路・防衛へ寄与するか比較する。", Join(economy, defense),
                R(T(AIStrategyPredicate.ValuableKnownRegion),T(AIStrategyPredicate.LegalTerritoryUseAvailable)),
                "CONTROL_REGION", "region-value", "ActualTerritoryVisionOrResourceGain", "NoRealRegionBenefit", "ContestRegion", "SCOUT_FOR_ANSWER"),
            P("GO-02", "孤立集団", "観測した孤立と増援の不確実性を分けて低損失の各個撃破を検討する。", offense,
                R(T(AIStrategyPredicate.EnemyGroupIsolated),T(AIStrategyPredicate.EnemyReinforcementExcluded),T(AIStrategyPredicate.ObservedTargetAvailable)),
                "ISOLATE_TARGET", "isolated-enemy", "ActualUnsupportedTargetLoss", "UnconfirmedOrObservedReinforcements", "ReconnectSupport;Retreat", "SCOUT_FOR_ANSWER"),
            P("GO-03", "厚み／援護網", "範囲攻撃への過密を避け、連続した合法援護を形成する。", Join(offense, defense),
                R(T(AIStrategyPredicate.SupportNetworkAvailable),F(AIStrategyPredicate.AreaAttackExposure),T(AIStrategyPredicate.SupportCellAvailable)),
                "BUILD_SUPPORT_NET", "mutual-support", "ActualConnectedSupportCoverage", "FormationExposedToAreaAttack", "AreaAttack;SeparateActors", "ADAPT_FORMATION"),
            P("GO-04", "分断と各個撃破", "安全な合法位置で敵の観測済み援護を切り、局地攻撃へつなげる。", offense,
                R(T(AIStrategyPredicate.SupportCutLegal),T(AIStrategyPredicate.SafeCutPosition),T(AIStrategyPredicate.LocalParticipationAdvantage)),
                "CUT_SUPPORT;FOCUS_FIRE", "support-cut", "ActualEnemySupportOrHPReduced", "CutPositionUnsafe", "Reconnect;Withdraw", "FOCUS_FIRE"),
            P("GO-05", "部分対全体最適", "局地利益が王・晶・経済の敗北につながらないか比較する。", Join(offense, Join(defense, economy)),
                R(T(AIStrategyPredicate.LocalGlobalConflict),T(AIStrategyPredicate.EconomyForecastAvailable),T(AIStrategyPredicate.StableObjectiveAvailable)),
                "GLOBAL_TRADEOFF", "global-safety", "ActualGlobalObjectivePreserved", "LocalGainCreatesImmediateDefeat", "AlternateFrontPressure", "DEFEND"),
            P("RT-01", "偵察の情報価値", "追加観測で実行案が変わる場合だけ、費用を上回る索敵を行う。", information,
                R(T(AIStrategyPredicate.DecisionRelevantUnknown),T(AIStrategyPredicate.ScoutInformationGainPositive),T(AIStrategyPredicate.LegalMoveAvailable)),
                "SCOUT_FOR_ANSWER", "information-value", "NewRelevantObservedFactChangesPlan", "NoDecisionRelevantInformationGain", "HideOrRelocate", "IMPROVE_POSITION", AIStrategyCostModel.ObservationOpportunity),
            P("RT-02", "機会費用", "軍備と経済投資を正式な資源・市民・維持費の予測内で比較する。", Join(economy, offense),
                R(T(AIStrategyPredicate.EconomyForecastAvailable),T(AIStrategyPredicate.CompetingInvestmentAvailable),T(AIStrategyPredicate.CitizenGrowthRespected)),
                "ALLOCATE_RESOURCES", "investment-choice", "ActualInvestmentAndForecastDelta", "OfficialResourceOrPopulationViolation", "ApplyMilitaryPressure", "REBUILD", AIStrategyCostModel.OfficialProductionForecast),
            P("RT-03", "能力への対策", "合法に観測した能力に対応できる自軍能力と維持費を比べる。", offense,
                R(T(AIStrategyPredicate.CounterAbilityObserved),T(AIStrategyPredicate.CounterCapabilityAvailable),T(AIStrategyPredicate.EconomyForecastAvailable)),
                "COUNTER_COMPOSITION", "counter-ability", "ActualCounterRoleOrEffectAvailable", "EnemyAbilityNotObserved", "ChangeComposition", "FORM_DEFENSE", AIStrategyCostModel.OfficialProductionForecast),
            P("RT-04", "増援時機", "即時敗北を避けつつ、実際に間に合う増援による改善を比較する。", Join(offense, defense),
                R(T(AIStrategyPredicate.ReinforcementArrivalImproves),T(AIStrategyPredicate.ReinforcementsTimely),T(AIStrategyPredicate.RearDefenseSafe)),
                "REINFORCE;ASSEMBLE", "reinforcement-timing", "ActualReinforcementArrivedBeforeDeadline", "ObjectiveFallsBeforeArrival", "AttackBeforeAssembly", "DEFEND"),
            P("RT-05", "生産と軍備の連携", "作戦に確定した不足役割を、住宅と市民増加を区別して経済へ渡す。", Join(economy, offense),
                R(T(AIStrategyPredicate.OperationRoleMissing),T(AIStrategyPredicate.ProductionMismatch),T(AIStrategyPredicate.CitizenGrowthRespected),T(AIStrategyPredicate.EconomyForecastAvailable)),
                "PLAN_PRODUCTION", "missing-role", "ActualNeededRolePurchasedWithinForecast", "HousingMistakenForImmediateCitizenGrowth", "PressureSupply", "REASSIGN_ROLE", AIStrategyCostModel.OfficialProductionForecast),
            P("RT-06", "複数戦線の配分", "観測した複数の危険へ到達時間と必要守備を考慮して戦力を配分する。", Join(offense, defense),
                R(T(AIStrategyPredicate.MultipleObservedFronts),T(AIStrategyPredicate.SafeForceAllocation),T(AIStrategyPredicate.RearDefenseSafe)),
                "ALLOCATE_FRONT", "front-allocation", "ActualImportantFrontsRemainCovered", "ForceOverdispersed", "ConcentrateOneFront", "CONCENTRATE"),
            P("RT-07", "拠点前の集結", "無限待機を避け、同期した合法攻撃が個別突撃を上回る場合に集結する。", offense,
                R(T(AIStrategyPredicate.AssemblyImprovesStrike),T(AIStrategyPredicate.AssemblyHasFiniteDeadline),T(AIStrategyPredicate.ObservedTargetAvailable)),
                "ASSEMBLE;SYNCHRONIZE", "timed-assembly", "ActualGroupArrivedAndAttacked", "AssemblyDeadlineWithoutProgress", "AttackAssembly;Withdraw", "ATTACK"),
            P("RT-08", "補給負担の管理", "遠征中の正式維持費予測を調べ、必要以上に進軍を禁止しない。", Join(offense, economy),
                R(T(AIStrategyPredicate.ExpeditionSupplyRisk),T(AIStrategyPredicate.SupplyForecastAvailable)),
                "CHECK_SUPPLY", "supply-risk", "ActualUpkeepDeficitAvoided", "RiskEstimateUnsupportedOrOverrestrictive", "ProlongConflict", "CONTROL_REGION", AIStrategyCostModel.OfficialProductionForecast),
            P("SL-01", "3D射点", "公式高さ・射線・範囲を満たす安全な射点を選ぶ。", offense,
                R(T(AIStrategyPredicate.LegalFiringPositionAvailable),T(AIStrategyPredicate.HeightAndLineOfSightVerified),T(AIStrategyPredicate.LegalMoveAvailable)),
                "FIND_FIRING_POSITION", "open-line", "ActualPositionOffersOfficialAttack", "OnlyVisualDistanceValidated", "BlockLine;MoveTarget", "REPOSITION"),
            P("SL-02", "前後交代", "負傷前衛を退かせても後衛が露出しない合法な交代を選ぶ。", Join(offense, defense),
                R(T(AIStrategyPredicate.WoundedFrontline),T(AIStrategyPredicate.RotationKeepsRearSafe),T(AIStrategyPredicate.SharedAPAffordable)),
                "ROTATE_FRONTLINE", "frontline-rotation", "ActualFrontSurvivalAndRearCoverage", "RearExposedByRotation", "PursueWoundedFront", "FORM_DEFENSE"),
            P("SL-03", "状態異常コンボ", "正式な効果が成立したときだけ後続攻撃を再生成する。", offense,
                R(T(AIStrategyPredicate.DebuffImprovesAttack),T(AIStrategyPredicate.DebuffEffectVerified),T(AIStrategyPredicate.SharedAPAffordable)),
                "DEBUFF_THEN_ATTACK", "debuff-combo", "ActualDebuffThenTargetHPOrProtectionLoss", "EffectImmuneFailedOrUnknown", "Cleanse;MoveTarget", "ATTACK", AIStrategyCostModel.SharedActionSequenceAP),
            P("SL-04", "反撃予算", "正式な反撃時点・対象・有限防護を共有損失予算として攻撃列へ反映する。", offense,
                R(T(AIStrategyPredicate.CounterResponseKnown),T(AIStrategyPredicate.CounterLossSurvivable),T(AIStrategyPredicate.LegalAttackAvailable)),
                "BUDGET_COUNTER", "counter-budget", "ActualCounterAndProtectionDeltaCompared", "WrongCounterTimingOrTargets", "SelectCounterTargets;Reposition", "ATTACK"),
            P("SL-05", "AP共有計画", "複数駒の動的な正式費用を計算し、後続攻撃を残せる順を選ぶ。", Join(offense, defense),
                R(T(AIStrategyPredicate.SharedAPContention),T(AIStrategyPredicate.SharedAPAffordable),T(AIStrategyPredicate.OrderedOutcomeImproves)),
                "SHARE_AP;ORDER_ACTIONS", "action-order", "ActualSequenceWithinSharedAP", "EarlierMoveConsumesLaterAttackAP", "InterruptSequence", "ATTACK", AIStrategyCostModel.SharedActionSequenceAP),
            P("SL-06", "編成の役割補充", "必要な不足役割だけを、正式な費用と容量で補充する。", Join(offense, economy),
                R(T(AIStrategyPredicate.OperationRoleMissing),T(AIStrategyPredicate.AffordableRoleRecruitAvailable),T(AIStrategyPredicate.EconomyForecastAvailable)),
                "RECRUIT_MISSING_ROLE", "missing-role", "ActualMissingRoleSummoned", "UnneededRoleOrUnaffordableDemand", "PressureRecruitment", "REASSIGN_ROLE", AIStrategyCostModel.OfficialProductionForecast),
            P("SL-07", "安全な撤退再攻勢", "即敗北を起こさない退路と再開条件を持って交戦を解く。", Join(offense, defense),
                R(T(AIStrategyPredicate.LegalDisengageAvailable),T(AIStrategyPredicate.ResumeConditionDefined),T(AIStrategyPredicate.RearDefenseSafe)),
                "DISENGAGE;REGROUP", "safe-disengage", "ActualRetreatThenMeaningfulResume", "RetreatCreatesDefeatOrEndlessOscillation", "Pursue;OccupyRoute", "HOLD"),
            P("HI-01", "カンナエ着想の包囲", "中央を保ち、既知の両側経路から合法な複数方向の圧力を作る。", offense,
                R(T(AIStrategyPredicate.TwoKnownSideRoutes),T(AIStrategyPredicate.CenterSurvives),T(AIStrategyPredicate.FlankRestrictsEscape),T(AIStrategyPredicate.SharedAPAffordable),T(AIStrategyPredicate.ObservedTargetAvailable)),
                "FEINT_CENTER;FLANK;PINCH", "two-sided-pressure", "ActualTwoSidePressureAndObservedEscapeChange", "SideRouteUnknownBlockedOrCenterLost", "RefuseAdvance;Withdraw;ReinforceFlank", "CONCENTRATE;SCOUT_FOR_ANSWER"),
            P("HI-02", "ファビウス着想の持久", "劣勢で即敗北がなく、実際に再編と供給維持が有利なら決戦を遅らせる。", Join(offense, defense),
                R(T(AIStrategyPredicate.InferiorObservedForce),T(AIStrategyPredicate.DelayImprovesOutcome),T(AIStrategyPredicate.LegalDisengageAvailable),T(AIStrategyPredicate.RearDefenseSafe)),
                "DELAY;DEFEND;REBUILD", "safe-delay", "ActualLossAvoidedAndForcesRecovered", "OpponentCanImmediatelyDestroyObjective", "PressureHome;DenySupply", "DEFEND"),
            P("HI-03", "局所集中戦術", "後方の正式敗北条件を守って主目標へ参加可能な戦力を集める。", offense,
                R(T(AIStrategyPredicate.LocalParticipationAdvantage),T(AIStrategyPredicate.RearDefenseSafe),T(AIStrategyPredicate.KnownTerrainRouteAvailable)),
                "CONCENTRATE;PRESS_ADVANTAGE", "local-concentration", "ActualObjectiveParticipationOrDamageIncreased", "DifferentDefeatConditionExposed", "ConcentrateDefense;CounterFront", "ALLOCATE_FRONT"),
            P("HI-04", "情報地形適応", "正式に既知の高さ・視界・経路の変化に合わせて接近方法を変える。", information,
                R(T(AIStrategyPredicate.TerrainChangesPlan),T(AIStrategyPredicate.KnownTerrainRouteAvailable),T(AIStrategyPredicate.HeightAndLineOfSightVerified)),
                "USE_KNOWN_TERRAIN", "terrain-adaptation", "ActualLegalRouteOrExposureImproved", "UnknownRouteClaimedSafe", "ChangeFiringPosition", "SCOUT_FOR_ANSWER"),
            P("HI-05", "特殊能力への対抗配置", "観測した能力に対して正式に期待被害が減る隊列へ変更する。", Join(offense, defense),
                R(T(AIStrategyPredicate.CounterAbilityObserved),T(AIStrategyPredicate.AbilityFormationResponseImproves),T(AIStrategyPredicate.SupportCellAvailable)),
                "ADAPT_FORMATION", "counter-ability", "ActualExposureOrSupportImproved", "AbilityUnobservedOrResponseIneffective", "ChangeAbilityOrTargets", "FORM_DEFENSE")
        };
    }
    static AIKnowledgeRequirement T(AIStrategyPredicate predicate) => new AIKnowledgeRequirement(predicate);
    static AIKnowledgeRequirement F(AIStrategyPredicate predicate) => new AIKnowledgeRequirement(predicate, AITruthValue.False);
    static AIKnowledgeRequirement[] R(params AIKnowledgeRequirement[] values) => values;
    static string[] S(string values) => values.Split(';');
    static AIOperationGoal[] Join(AIOperationGoal[] left, AIOperationGoal[] right)
    {
        var result = new List<AIOperationGoal>(left);
        foreach (var goal in right) if (!result.Contains(goal)) result.Add(goal); return result.ToArray();
    }
    static AIStrategyPrinciple P(string id, string label, string text, AIOperationGoal[] goals,
        AIKnowledgeRequirement[] requirements, string operators, string effectGroup, string effects,
        string failures, string responses, string fallbacks, AIStrategyCostModel costs = AIStrategyCostModel.OfficialActionAP)
    {
        var prefix = id.Substring(0, 2);
        var category = prefix == "CH" ? AIStrategyCategory.Chess : prefix == "SH" ? AIStrategyCategory.Shogi
            : prefix == "GO" ? AIStrategyCategory.Go : prefix == "RT" ? AIStrategyCategory.RTS
            : prefix == "SL" ? AIStrategyCategory.TacticalSLG : AIStrategyCategory.Historical;
        return new AIStrategyPrinciple(id, Version, category, label, text, goals, requirements, S(operators),
            effectGroup, S(effects), S(failures), S(responses), S(fallbacks), costs,
            category.ToString(), category == AIStrategyCategory.Historical
                ? AIStrategyEditorialStatus.NeedsEditorialVerification : AIStrategyEditorialStatus.DesignPrinciple);
    }
}
