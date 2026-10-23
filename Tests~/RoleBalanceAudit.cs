#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Runtime data extraction and a deliberately narrow normal-damage stress bench.
/// This is not an AI match, a range simulation, or a measurement of full class win rates.
/// Run synchronously between matches, before subscribing match telemetry observers.
/// </summary>
public static class RoleBalanceAudit
{
    const int RoundLimit = 10;
    static readonly int[] TableLevels = { 1, 10, 50, 100 };
    static readonly int[] ExchangeLevels = { 1, 10 };
    static readonly Encoding CsvEncoding = new UTF8Encoding(true);

    [Serializable] sealed class AuditMetadata
    {
        public string Method = "Unity Play Mode: authored production data + isolated runtime normal-damage exchange";
        public string StartedUtc;
        public string UnityVersion;
        public bool BarrierRulesEnabled, LegacyDefence, ProvisionalBalance;
        public int Roles, StatRows, Exchanges, MixedLevelExchanges, Packets, Draws, RoundLimit, APBudget;
        public double Seconds;
        public bool LiveActorHealthAndEconomyUnchanged;
        public string[] Limitations = {
            "The adjacent normal-damage bench bypasses legal targeting, movement, range, terrain and AI decisions. It is not an AI match or a full class win-rate ranking.",
            "Each side has the same current configured maximum AP and resets fatigue at its next owner turn. Both opening orders are tested.",
            "Only DamageCalculator, barrier absorption, HP changes, normal-hit gauge notification and AP consume are exercised. No XP gain, active skills, ammunition, turn-start/end abilities, regen or automatic sniper recoil are executed.",
            "Samurai counter reactions affect the survivor outcome and final HP/barrier. Per-side damage totals count the initiating normal packet only, not nested reaction damage.",
            "Temporary actors are outside the map and excluded from CombatRegistry. Nearby auras, cooperation, battlefield visibility, and strategic role value are not assessed here.",
            "A draw means the ten-round stress-bench cutoff; it is not a game result. Full class balance requires the separately recorded actual AI matches and scenario tests."
        };
    }

    sealed class LiveSnapshot
    {
        readonly Status actor;
        readonly int hp, barrier, maxHP, atk, level, fatigue;
        readonly Vector3 position;
        public LiveSnapshot(Status value)
        {
            actor = value; hp = value.HP; barrier = value.Barrier; maxHP = value.MaxHP;
            atk = value.ATK; level = value.Level; fatigue = value.Fatigue; position = value.transform.position;
        }
        public bool Unchanged => actor != null && actor.HP == hp && actor.Barrier == barrier
            && actor.MaxHP == maxHP && actor.ATK == atk && actor.Level == level
            && actor.Fatigue == fatigue && actor.transform.position == position;
    }

    sealed class Score
    {
        public int Wins, Losses, Draws;
        public long HpDamage, BarrierDamage, AP;
    }

    /// <summary>Use the GameSystems of an actual initialized production scene.</summary>
    public static void Run(GameSystems systems, string output)
    {
        if (!Application.isPlaying) throw new InvalidOperationException("RoleBalanceAudit requires Play Mode.");
        if (systems?.UnitSetting?.UnitDataMap == null) throw new InvalidOperationException("Production UnitDataMap is not initialized.");
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Output folder is required.", nameof(output));
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var metadata = new AuditMetadata { StartedUtc = DateTime.UtcNow.ToString("O"), UnityVersion = Application.unityVersion,
            BarrierRulesEnabled = BarrierCombatSettings.Active.enabledForPlay,
            LegacyDefence = DamageCalculator.UsesLegacyDefence,
            ProvisionalBalance = BarrierCombatSettings.Active.trialBalance,
            Roles = UnitRoster.SummonableKinds.Length, RoundLimit = RoundLimit, APBudget = APSystem.MaximumAP };
        var savedRandom = UnityEngine.Random.state;
        var live = new List<Status>(); CombatRegistry.Collect(live);
        var snapshots = new List<LiveSnapshot>(live.Count);
        foreach (var actor in live) snapshots.Add(new LiveSnapshot(actor));
        string playerNation = systems.FactionState?.PlayerNation != null ? JsonUtility.ToJson(systems.FactionState.PlayerNation) : null;
        string enemyNation = systems.FactionState?.EnemyNation != null ? JsonUtility.ToJson(systems.FactionState.EnemyNation) : null;
        var cachedTurnField = typeof(Status).GetField("_cachedTurnGenerator", BindingFlags.Static | BindingFlags.NonPublic);
        if (cachedTurnField == null) throw new MissingFieldException("Status._cachedTurnGenerator");
        object previousTurn = cachedTurnField.GetValue(null);
        GameObject infrastructure = null, leftObject = null, rightObject = null;
        try
        {
            // This inactive hub never executes Awake, input callbacks, or a game state.
            // RuntimeSystems reads it while this synchronous scope runs, so Navigator's
            // AP modifier cannot borrow the live match's AP or environment state.
            infrastructure = new GameObject("Role audit isolated systems"); infrastructure.SetActive(false);
            var fixtureTurn = infrastructure.AddComponent<TurnGenerator>();
            var factions = infrastructure.AddComponent<FactionState>();
            factions.PlayerNation = infrastructure.AddComponent<NationState>();
            factions.EnemyNation = infrastructure.AddComponent<NationState>();
            factions.PlayerAP.Reset = factions.EnemyAP.Reset = APSystem.MaximumAP;
            var ap = infrastructure.AddComponent<APSystem>(); ap.Init(factions);
            fixtureTurn.Systems.APSystem = ap; fixtureTurn.Systems.FactionState = factions;
            cachedTurnField.SetValue(null, fixtureTurn);
            leftObject = new GameObject("Role audit isolated left");
            rightObject = new GameObject("Role audit isolated right");
            leftObject.transform.position = new Vector3(10000, 10000, 10000);
            rightObject.transform.position = new Vector3(10001, 10000, 10000);
            var left = leftObject.AddComponent<Status>(); var right = rightObject.AddComponent<Status>();
            CombatRegistry.Unregister(left); CombatRegistry.Unregister(right);
            var definitions = new Dictionary<Kind, UnitData>();
            foreach (var kind in UnitRoster.SummonableKinds)
            {
                if (!systems.UnitSetting.UnitDataMap.TryGetValue(kind, out var data) || data == null)
                    throw new InvalidOperationException("Missing production definition: " + kind);
                definitions.Add(kind, data);
            }
            ExportRoles(Path.Combine(output, "roles.csv"), definitions, left, factions, ap, metadata);
            var scores = new Dictionary<string, Score>();
            using (var summary = new StreamWriter(Path.Combine(output, "normal_exchange_summary.csv"), false, CsvEncoding))
            using (var packets = new StreamWriter(Path.Combine(output, "normal_exchange_packets.csv"), false, CsvEncoding))
            {
                Csv(summary, "exchange_id", "level", "left", "right", "opens", "outcome", "rounds", "left_AP", "right_AP",
                    "left_HP", "left_barrier", "right_HP", "right_barrier", "left_HP_damage", "left_barrier_damage", "right_HP_damage", "right_barrier_damage");
                Csv(packets, "exchange_id", "level", "round", "side", "attacker", "target", "AP", "AP_remaining", "requested", "HP_loss", "barrier_loss", "attacker_HP", "attacker_barrier", "target_HP", "target_barrier");
                foreach (int level in ExchangeLevels)
                    foreach (var leftKind in UnitRoster.SummonableKinds)
                        foreach (var rightKind in UnitRoster.SummonableKinds)
                            for (int opens = 0; opens < 2; opens++)
                                Exchange(left, right, definitions[leftKind], definitions[rightKind], level, opens,
                                    factions, ap, summary, packets, scores, metadata);
            }
            using (var writer = new StreamWriter(Path.Combine(output, "normal_exchange_scores.csv"), false, CsvEncoding))
            {
                Csv(writer, "level_role", "wins", "losses", "cutoff_draws", "HP_damage", "barrier_damage", "AP_used", "interpretation");
                foreach (var pair in scores)
                    Csv(writer, pair.Key, pair.Value.Wins, pair.Value.Losses, pair.Value.Draws,
                        pair.Value.HpDamage, pair.Value.BarrierDamage, pair.Value.AP,
                        "Normal-damage stress bench only; not a full class win rate");
            }
            // Cross-level exchanges are kept separate so they cannot silently
            // change the equal-level score table's denominator.
            var mixedScores = new Dictionary<string, Score>();
            using (var summary = new StreamWriter(Path.Combine(output, "normal_mixed_level_summary.csv"), false, CsvEncoding))
            using (var packets = new StreamWriter(Path.Combine(output, "normal_mixed_level_packets.csv"), false, CsvEncoding))
            {
                Csv(summary, "exchange_id", "left_level", "left", "right", "opens", "outcome", "rounds", "left_AP", "right_AP",
                    "left_HP", "left_barrier", "right_HP", "right_barrier", "left_HP_damage", "left_barrier_damage", "right_HP_damage", "right_barrier_damage", "right_level");
                Csv(packets, "exchange_id", "left_level", "round", "side", "attacker", "target", "AP", "AP_remaining", "requested", "HP_loss", "barrier_loss", "attacker_HP", "attacker_barrier", "target_HP", "target_barrier", "attacker_level", "target_level");
                foreach (var kind in UnitRoster.SummonableKinds)
                    for (int reverse = 0; reverse < 2; reverse++)
                        for (int opens = 0; opens < 2; opens++)
                        {
                            Exchange(left, right, definitions[kind], definitions[kind], reverse == 0 ? 1 : 10, opens,
                                factions, ap, summary, packets, mixedScores, metadata, reverse == 0 ? 10 : 1, true);
                            metadata.MixedLevelExchanges++;
                        }
            }
        }
        finally
        {
            cachedTurnField.SetValue(null, previousTurn);
            if (leftObject != null) UnityEngine.Object.DestroyImmediate(leftObject);
            if (rightObject != null) UnityEngine.Object.DestroyImmediate(rightObject);
            if (infrastructure != null) UnityEngine.Object.DestroyImmediate(infrastructure);
            UnityEngine.Random.state = savedRandom;
            SpecialSkillRuntime.InvalidateRules();
            metadata.LiveActorHealthAndEconomyUnchanged = true;
            foreach (var snapshot in snapshots) metadata.LiveActorHealthAndEconomyUnchanged &= snapshot.Unchanged;
            metadata.LiveActorHealthAndEconomyUnchanged &= playerNation == (systems.FactionState?.PlayerNation != null ? JsonUtility.ToJson(systems.FactionState.PlayerNation) : null)
                && enemyNation == (systems.FactionState?.EnemyNation != null ? JsonUtility.ToJson(systems.FactionState.EnemyNation) : null);
            metadata.Seconds = watch.Elapsed.TotalSeconds;
            File.WriteAllText(Path.Combine(output, "role_audit_metadata.json"), JsonUtility.ToJson(metadata, true), Encoding.UTF8);
        }
        if (!metadata.LiveActorHealthAndEconomyUnchanged) throw new Exception("Role audit changed live actors or national economy.");
        Debug.Log($"[RoleBalanceAudit] Completed {metadata.StatRows} stat rows, {metadata.Exchanges} bounded exchanges, {metadata.Packets} actual damage packets. Live health/economy unchanged. {output}");
    }

    static void ResetActor(Status actor, UnitData definition, int level, Team team)
    {
        // Death callbacks can flag a fixture's prior life. Toggle while it has no
        // renderer or board membership, then immediately remove its registry entry.
        actor.gameObject.SetActive(false);
        actor.kind = definition.kind; actor.team = team; actor.type = Type.Unit;
        actor.direction = Direction.N; actor.Fatigue = 0; actor.Experience = 0;
        actor.ActiveEffects.Clear(); actor.ShieldTurns = 0; actor.ShieldActivated = false;
        actor.SkillCooldown = 0; actor.HasMovedThisTurn = false; actor.FirstSkillUsedThisTurn = false;
        actor.ResetTurnFlags(); actor.ResetBattleFlags();
        actor.UpkeepUnpaidTurns = 0; actor.RestoreReflectionLifeId(Guid.NewGuid().ToString("N"));
        definition.ApplyToStatus(actor, level); definition.InitializeAbilities(actor);
        actor.gameObject.SetActive(true); CombatRegistry.Unregister(actor);
    }

    static void ExportRoles(string path, Dictionary<Kind, UnitData> definitions, Status actor,
        FactionState factions, APSystem ap, AuditMetadata metadata)
    {
        using var writer = new StreamWriter(path, false, CsvEncoding);
        Csv(writer, "kind", "name", "definition_id", "level", "HP", "ATK", "barrier", "attack_AP_full_HP", "attack_AP_half_HP", "move_AP_flat",
            "active_skill", "skill_enabled", "skill_AP", "cooldown", "configuration_error", "player_available", "enemy_available",
            "cost_wood", "cost_stone", "cost_iron", "cost_magic", "cost_water", "cost_bread", "cost_citizen", "cost_AP",
            "upkeep_wood", "upkeep_stone", "upkeep_iron", "upkeep_magic", "upkeep_wheat", "upkeep_water", "upkeep_bread", "upkeep_citizen",
            "move_tiles", "attack_tiles", "vision_tiles", "shape_assessment");
        foreach (var kind in UnitRoster.SummonableKinds)
            foreach (int level in TableLevels)
            {
                var data = definitions[kind]; ResetActor(actor, data, level, Team.Player); factions.PlayerAP.ResetForTurn();
                int attack = ap.CalcCost(APSystem.ActionType.Attack, actor);
                actor.HP = Mathf.Max(1, actor.MaxHP / 2);
                int halfAttack = ap.CalcCost(APSystem.ActionType.Attack, actor); actor.HP = actor.MaxHP;
                int move = ap.CalcCost(APSystem.ActionType.Move, actor, actor.transform.position, actor.transform.position + Vector3.right);
                var ability = SpecialSkillRuntime.Definition(actor); var upkeep = data.GetUpkeep(level);
                var profile = data.actionProfile;
                Csv(writer, kind, data.DisplayName, data.definitionId, level, actor.MaxHP, actor.ATK, actor.MaxBarrier, attack, halfAttack, move,
                    ability != null ? SpecialSkillPresentation.Name(ability) : "legacy/fixed", ability != null && ability.enabledForPlay,
                    ability != null ? SpecialSkillRuntime.GetAPCost(actor) : -1, ability != null ? ability.cooldown : -1,
                    ability?.ConfigurationError ?? "", data.availableToPlayer, data.availableToEnemy,
                    data.costWood, data.costStone, data.costIron, data.costMagic, data.costWater, data.costBread, data.costCitizen, data.costAP,
                    upkeep.Wood, upkeep.Stone, upkeep.Iron, upkeep.MagicOre, upkeep.Wheat, upkeep.Water, upkeep.Bread, upkeep.Citizen,
                    ProfileCount(profile, "move"), ProfileCount(profile, "attack"), ProfileCount(profile, "vision"),
                    profile == null ? "inherited runtime patterns; count not extracted" : "authored; see profile asset");
                metadata.StatRows++;
            }
    }

    // An inherited pattern is not counted as an empty authored mask.
    static string ProfileCount(BoardActionProfile profile, string category)
    {
        if (profile == null) return "not_extracted";
        var pattern = category == "move" ? profile.movement : category == "attack" ? profile.attack : profile.vision;
        return pattern != null && pattern.useCustom ? pattern.Offsets.Count.ToString(CultureInfo.InvariantCulture) : "inherited";
    }

    static void Exchange(Status left, Status right, UnitData leftData, UnitData rightData, int level, int opens,
        FactionState factions, APSystem ap, StreamWriter summary, StreamWriter packets, Dictionary<string, Score> scores, AuditMetadata metadata,
        int rightLevel = -1, bool mixedLevels = false)
    {
        if (rightLevel < 0) rightLevel = level;
        ResetActor(left, leftData, level, Team.Player); ResetActor(right, rightData, rightLevel, Team.Enemy);
        left.VisionCell.Clear(); right.VisionCell.Clear();
        left.VisionCell.Add(GridHelper.ToGrid(right.transform.position)); right.VisionCell.Add(GridHelper.ToGrid(left.transform.position));
        var totals = new long[2, 3]; // inflicted HP damage, inflicted barrier damage, consumed AP
        int id = ++metadata.Exchanges, rounds = 0;
        for (int round = 1; round <= RoundLimit && left.IsAlive && right.IsAlive; round++)
        {
            rounds = round;
            for (int owner = 0; owner < 2 && left.IsAlive && right.IsAlive; owner++)
            {
                int side = (opens + owner) % 2;
                var attacker = side == 0 ? left : right; var target = side == 0 ? right : left;
                factions.GetAPData(attacker.team).ResetForTurn(); attacker.Fatigue = 0;
                // AP fatigue guarantees a finite number of calls even with free-action abilities.
                for (int action = 0; action < APSystem.MaximumAP && attacker.IsAlive && target.IsAlive; action++)
                {
                    int cost = ap.CalcCost(APSystem.ActionType.Attack, attacker);
                    if (cost > factions.GetAP(attacker.team)) break;
                    ap.Consume(attacker.team, APSystem.ActionType.Attack, attacker);
                    SpecialSkillRuntime.BeginNormalAttack(attacker);
                    int requested = BarrierSystem.NormalAttackDamage(attacker, target, DamageCalculator.CalcNormal(attacker, target));
                    SpecialSkillRuntime.ApplyHpChange(attacker, target, requested, HpChangeKind.NormalDamage, deferCleanup: true, cause: HpChangeCause.NormalAttack);
                    // A Samurai can heal during its reaction inside ApplyHpChange.
                    // The committed packet preserves actual damage before that heal.
                    var committed = SpecialSkillRuntime.LastHpChange;
                    int hp = committed.ActualHpLoss, barrier = committed.ActualBarrierLoss;
                    SpecialSkillRuntime.OnAttackLanded(attacker);
                    totals[side, 0] += hp; totals[side, 1] += barrier; totals[side, 2] += cost;
                    if (mixedLevels)
                        Csv(packets, id, level, round, side, attacker.kind, target.kind, cost, factions.GetAP(attacker.team), requested,
                            hp, barrier, attacker.HP, attacker.Barrier, target.HP, target.Barrier, attacker.Level, target.Level);
                    else
                        Csv(packets, id, level, round, side, attacker.kind, target.kind, cost, factions.GetAP(attacker.team), requested,
                            hp, barrier, attacker.HP, attacker.Barrier, target.HP, target.Barrier);
                    metadata.Packets++;
                }
            }
        }
        string result = !left.IsAlive && !right.IsAlive ? "mutual_HP_zero" : !right.IsAlive ? "left_HP_survives"
            : !left.IsAlive ? "right_HP_survives" : "round_limit";
        bool leftWon = left.IsAlive && !right.IsAlive, rightWon = right.IsAlive && !left.IsAlive;
        if (!leftWon && !rightWon) metadata.Draws++;
        AddScore(scores, level, left.kind, leftWon, rightWon, totals, 0);
        AddScore(scores, rightLevel, right.kind, rightWon, leftWon, totals, 1);
        if (mixedLevels)
            Csv(summary, id, level, leftData.kind, rightData.kind, opens == 0 ? "left" : "right", result, rounds,
                totals[0, 2], totals[1, 2], left.HP, left.Barrier, right.HP, right.Barrier,
                totals[0, 0], totals[0, 1], totals[1, 0], totals[1, 1], rightLevel);
        else
            Csv(summary, id, level, leftData.kind, rightData.kind, opens == 0 ? "left" : "right", result, rounds,
                totals[0, 2], totals[1, 2], left.HP, left.Barrier, right.HP, right.Barrier,
                totals[0, 0], totals[0, 1], totals[1, 0], totals[1, 1]);
    }

    static void AddScore(Dictionary<string, Score> scores, int level, Kind kind, bool won, bool lost, long[,] totals, int side)
    {
        string key = level + "_" + kind;
        if (!scores.TryGetValue(key, out var score)) { score = new Score(); scores.Add(key, score); }
        if (won) score.Wins++; else if (lost) score.Losses++; else score.Draws++;
        score.HpDamage += totals[side, 0]; score.BarrierDamage += totals[side, 1]; score.AP += totals[side, 2];
    }

    static void Csv(TextWriter writer, params object[] values)
    {
        for (int index = 0; index < values.Length; index++)
        {
            if (index != 0) writer.Write(',');
            string value = Convert.ToString(values[index], CultureInfo.InvariantCulture) ?? "";
            writer.Write('"'); writer.Write(value.Replace("\"", "\"\"")); writer.Write('"');
        }
        writer.WriteLine();
    }
}
#endif
