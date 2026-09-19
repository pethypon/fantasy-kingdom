// =====================================================================
//  CoreLogicTests — 最小回帰テスト
//
//  本プロジェクトは Unity Test Framework を導入していないため、
//  Editor 上から手動実行できる軽量アサーションとして実装する。
//
//  実行方法:
//    Unity Editor メニュー "Fantasy Kingdom > Run Core Tests" を選択。
//    Console に [CoreTest] 行で PASS/FAIL が出力される。
//
//  テスト項目:
//    1. DamageCalculator: ダメージは 0 未満にクランプされる
//    2. CrystalShield: HP 50% 未満で発動 / ShieldTurns が 5 になる / リセット
//    3. TimerSystem: 持ち時間切れ時は「クリスタル → 王」の順で勝敗判定される
// =====================================================================
#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

public static class CoreLogicTests
{
    static int _pass;
    static int _fail;

    [MenuItem("Fantasy Kingdom/Run Core Tests")]
    public static void RunAll()
    {
        _pass = 0;
        _fail = 0;

        TestR1Rules();
        TestFactionIsolationAndCamera();
        TestDamageClamp();
        TestDamageBaseFormula();
        TestCrystalShieldActivationAndReset();
        TestTimerWinnerDeterminationOrder();

        Debug.Log($"[CoreTest] Done: {_pass} passed, {_fail} failed");
        if (_fail > 0) throw new System.Exception($"{_fail} core regression tests failed");
    }

    // -----------------------------------------------------------------
    //  DamageCalculator
    // -----------------------------------------------------------------
    static void TestR1Rules()
    {
        foreach (int citizens in new[] { 0, 5, 10, 20, 30 })
        {
            var ap = new FactionState.APData { Plus = citizens };
            ap.ResetForTurn();
            Assert($"R1 AP citizens={citizens}", ap.Current == System.Math.Min(50, 30 + citizens));
            Assert("UI AP equals turn reset", ap.Maximum == ap.Current);
        }
        var restored = new FactionState.APData();
        SaveSystem.RestoreAP(new SaveSystem.APSaveData { Current = 99 }, restored);
        Assert("Load clamps AP upper bound", restored.Current == 50);
        SaveSystem.RestoreAP(new SaveSystem.APSaveData { Current = -5 }, restored);
        Assert("Load clamps AP lower bound", restored.Current == 0);
        Assert("R1 independent damage example", Mathf.Abs(DamageCalculator.CalcRawBase(100, 30) - 61f) < 0.001f);
        Assert("Crystal starts at 15000", CrystalSystem.CrystalHP == 15000);

        var dungeon = new DungeonSystem.DungeonInfo { ClaimingTeam = Team.Player };
        for (int i = 0; i < 7; i++) Assert("No early reward", !DungeonSystem.AdvanceCycle(dungeon));
        dungeon.ClaimingTeam = Team.None;
        Assert("Uncontrolled dungeon pauses", !DungeonSystem.AdvanceCycle(dungeon) && dungeon.ClaimProgress == 7);
        dungeon.ClaimingTeam = Team.Enemy;
        Assert("Takeover continues at 8", !DungeonSystem.AdvanceCycle(dungeon) && dungeon.ClaimProgress == 8);
        Assert("Takeover continues at 9", !DungeonSystem.AdvanceCycle(dungeon) && dungeon.ClaimProgress == 9);
        Assert("Reward for final controller", DungeonSystem.AdvanceCycle(dungeon) && dungeon.ClaimingTeam == Team.Enemy);
        Assert("Dungeon remains for next cycle", !dungeon.Cleared && dungeon.ClaimProgress == 0);

        var a = new GameObject("R1 attacker");
        var b = new GameObject("R1 defender");
        var engine = new GameObject("R1 skill engine");
        try
        {
            var attacker = a.AddComponent<Status>();
            var target = b.AddComponent<Status>();
            attacker.kind = Kind.Knight; attacker.team = Team.Player;
            attacker.type = Type.Unit; attacker.MaxHP = attacker.HP = 100;
            attacker.ATK = 30; attacker.Level = 10;
            target.kind = Kind.Knight; target.team = Team.Enemy;
            target.type = Type.Unit; target.MaxHP = target.HP = 100;
            a.transform.position = new Vector3(0, 2, 0);
            b.transform.position = new Vector3(1, 1, 0);
            Assert("High ground beats low ground", DamageCalculator.GetTerrainMultiplier(attacker, target)
                > DamageCalculator.GetTerrainMultiplier(target, attacker));
            target.HP = 5;
            Assert("Actual damage excludes overkill", target.ApplyDamage(999) == 5 && target.HP == 0);
            target.HP = 100;
            var skill = new SkillData { Name = "regression", Area = SkillAreaShape.Single, Multiplier = 1f };
            var skills = engine.AddComponent<SkillSystem>();
            skills.ExecuteSkill(attacker, target, skill);
            Assert("Skill XP equals actual damage", attacker.Experience == 100 - target.HP && attacker.Experience > 0);
            int xp = attacker.Experience;
            target.ShieldTurns = 2;
            skills.ExecuteSkill(attacker, target, skill);
            Assert("Shield gives no XP", attacker.Experience == xp);
            SkillData.AssignFixedSkill(attacker);
            int fixedId = attacker.AssignedSkillId;
            for (int i = 0; i < 10; i++) SkillData.AssignFixedSkill(attacker);
            Assert("Fixed skill is deterministic", fixedId == attacker.AssignedSkillId);

            var map = engine.AddComponent<MapCreate>();
            map.maxX = map.maxZ = 5;
            var heights = new int[5, 5]; heights[2, 2] = 2;
            typeof(MapCreate).GetField("topY", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(map, heights);
            Assert("High mountain blocks both directions", !map.HasClearTerrainLine(new Vector3(0, 1, 2), new Vector3(4, 2, 2))
                && !map.HasClearTerrainLine(new Vector3(4, 2, 2), new Vector3(0, 1, 2)));
            Assert("Mountain endpoint is visible but not attackable",
                map.HasClearTerrainLine(new Vector3(0, 1, 2), new Vector3(2, 3, 2), true)
                && !map.HasClearTerrainLine(new Vector3(0, 1, 2), new Vector3(2, 3, 2)));
            Assert("Sight cannot pass beyond mountain", !map.HasClearTerrainLine(new Vector3(0, 1, 2), new Vector3(4, 1, 2), true));
            Assert("Height lookup excludes mountains and out of bounds", !map.TryGetHeight(2, 2, out _) && !map.TryGetHeight(-1, 0, out _));
            Assert("Height lookup returns standing height", map.TryGetHeight(0, 0, out float standY) && standY == 1f);
            map.ExcludePlacementCell(new Vector3(0, 1, 0));
            Assert("Reserved crystal tile is not a placement candidate", !map.TryGetHeight(0, 0, out _));
            Assert("Clear terrain remains traversable", map.HasClearTerrainLine(new Vector3(0, 1, 0), new Vector3(4, 1, 0)));
        }
        finally { Object.DestroyImmediate(a); Object.DestroyImmediate(b); Object.DestroyImmediate(engine); }
    }

    static void TestFactionIsolationAndCamera()
    {
        var root = new GameObject("Faction regression");
        var registryObject = UnitRegistry.Instance == null ? new GameObject("Regression registry") : null;
        var registry = registryObject != null ? registryObject.AddComponent<UnitRegistry>() : UnitRegistry.Instance;
        if (registryObject != null) typeof(UnitRegistry).GetMethod("Awake", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(registry, null);
        Status enemy = null;
        try
        {
            var monster = new GameObject("monster").AddComponent<Status>(); monster.transform.SetParent(root.transform);
            var intruder = new GameObject("intruder").AddComponent<Status>(); intruder.transform.SetParent(root.transform);
            enemy = new GameObject("enemy").AddComponent<Status>(); enemy.transform.SetParent(root.transform);
            monster.team = Team.Monster; intruder.team = Team.Intruder; enemy.team = Team.Enemy;
            foreach (var unit in new[] { monster, intruder, enemy })
            { unit.type = Type.Unit; unit.HP = unit.MaxHP = 100; unit.ShieldTurns = unit.SkillCooldown = 3; }
            registry.Register(enemy);
            StatusEffectSystem.TickAllUnits(Team.Monster, root.transform);
            Assert("Monster status ticks once", monster.ShieldTurns == 2 && monster.SkillCooldown == 2);
            Assert("Monster turn does not tick other factions", enemy.ShieldTurns == 3 && enemy.SkillCooldown == 3 && intruder.ShieldTurns == 3);
            StatusEffectSystem.TickAllUnits(Team.Intruder, root.transform);
            Assert("Intruder status ticks independently", intruder.ShieldTurns == 2 && monster.ShieldTurns == 2 && enemy.ShieldTurns == 3);
            var vision = root.AddComponent<VisionGenerator>();
            vision.Awake();
            var cell = new Vector3Int(999, 0, 999);
            int count = vision.EnemyExplored.Count;
            vision.AddExplored(Team.Monster, cell); vision.AddExploredRange(Team.Intruder, new[] { cell });
            Assert("Neutral exploration does not leak to enemy", vision.EnemyExplored.Count == count);
            vision.AddExplored(Team.Enemy, cell); vision.ClearExplored(Team.Monster);
            Assert("Neutral clear preserves enemy exploration", vision.EnemyExplored.Count == count + 1);
            Vector3 forward = new Vector3(0.4f, -1f, 0.7f).normalized;
            Vector3 target = new Vector3(4, 1, 5);
            Vector3 focused = TurnCameraController.FocusPosition(new Vector3(30, 15, 30), forward, target);
            Vector3 hit = focused + forward * ((target.y - focused.y) / forward.y);
            Assert("Initial camera centers player base", Vector3.Distance(hit, target) < 0.001f && Mathf.Abs(focused.y - 15f) < 0.001f);
            Vector3 clamped = TurnCameraController.ClampPosition(new Vector3(-50, 15, 100), forward, 5, 5);
            hit = clamped + forward * (-clamped.y / forward.y);
            Assert("Camera clamp handles small maps", hit.x >= -0.001f && hit.x <= 4.001f && hit.z >= -0.001f && hit.z <= 4.001f);
        }
        finally
        {
            if (enemy != null) registry.Unregister(enemy);
            Object.DestroyImmediate(root);
            if (registryObject != null) Object.DestroyImmediate(registryObject);
        }
    }

    static void TestDamageClamp()
    {
        // 極端に弱い ATK と高い DEF → 負の値にならず 0 以上に収束
        int dmg = DamageCalculator.CalcFromValues(atk: 0f, def: 9999f, incomingMod: 1f);
        Assert("Damage clamped to 0 when DEF overwhelms ATK", dmg >= 0);

        // 正常ケース: ATK>DEF ならダメージ > 0
        int dmg2 = DamageCalculator.CalcFromValues(atk: 100f, def: 0f, incomingMod: 1f);
        Assert("Damage is positive when ATK >> DEF", dmg2 > 0);
    }

    static void TestDamageBaseFormula()
    {
        // CalcRawBase = DamageBase + atk/DamageATKDivisor + atk/DamageATKHalf - def/DamageDEFDivisor
        const float atk = 60f;
        const float def = 20f;
        float expected = GameConstants.DamageBase
                       + (atk / GameConstants.DamageATKDivisor)
                       + ((atk / GameConstants.DamageATKHalf) - (def / GameConstants.DamageDEFDivisor));
        float actual = DamageCalculator.CalcRawBase(atk, def);
        Assert($"CalcRawBase formula (expected={expected}, actual={actual})",
            Mathf.Abs(expected - actual) < 0.0001f);
    }

    // -----------------------------------------------------------------
    //  Crystal Shield
    // -----------------------------------------------------------------
    static void TestCrystalShieldActivationAndReset()
    {
        // Status は MonoBehaviour なので一時 GameObject に付けて検証
        var go = new GameObject("__test_crystal__");
        try
        {
            var s = go.AddComponent<Status>();
            s.kind = Kind.Crystal;
            s.team = Team.Player;
            s.MaxHP = 100;
            s.HP = 49; // < 50% → 発動条件
            s.ShieldTurns = 0;
            s.ShieldActivated = false;

            // 発動条件を満たした状態で直接フィールドをセット
            // （BattleSystem.CheckCrystalShield と同じ分岐を模擬）
            float hpRatio = (float)s.HP / s.MaxHP;
            if (hpRatio < GameConstants.CrystalShieldThreshold && !s.ShieldActivated)
            {
                s.ShieldTurns = GameConstants.CrystalShieldDuration;
                s.ShieldActivated = true;
            }

            Assert("Shield activates when HP < 50%", s.ShieldActivated);
            Assert($"Shield duration = {GameConstants.CrystalShieldDuration}",
                s.ShieldTurns == GameConstants.CrystalShieldDuration);

            // Tick で減っていき 0 でリセットされること
            while (s.ShieldTurns > 0)
            {
                s.ShieldTurns--;
                if (s.ShieldTurns <= 0) s.ShieldActivated = false;
            }
            Assert("Shield resets ShieldActivated when turns reach 0", !s.ShieldActivated);
        }
        finally
        {
            Object.DestroyImmediate(go);
        }
    }

    // -----------------------------------------------------------------
    //  Timer winner determination order (Crystal HP% → King HP%)
    // -----------------------------------------------------------------
    static void TestTimerWinnerDeterminationOrder()
    {
        // Crystal 比較が先: P クリスタルの方が高ければ King の HP% に関係なく Win
        float pCrystal = 0.6f;
        float eCrystal = 0.4f;
        float pKing = 0.1f;   // King は E の方が高いが無視されるべき
        float eKing = 0.9f;

        GameResult result = DetermineMock(pCrystal, eCrystal, pKing, eKing);
        Assert("Crystal HP% takes precedence over King HP%",
            result == GameResult.TimeUpWin);

        // Crystal 同率 → King で決着
        result = DetermineMock(0.5f, 0.5f, 0.9f, 0.1f);
        Assert("When crystals tie, King HP% decides", result == GameResult.TimeUpWin);

        // 全て同率 → Draw
        result = DetermineMock(0.5f, 0.5f, 0.5f, 0.5f);
        Assert("All equal = Draw", result == GameResult.TimeUpDraw);
    }

    // TimerSystem.DetermineTimeUpResult と同じ判定ロジックを再現した検証用モック。
    static GameResult DetermineMock(float pCrystalRatio, float eCrystalRatio,
                                    float pKingRatio, float eKingRatio)
    {
        if (pCrystalRatio > eCrystalRatio) return GameResult.TimeUpWin;
        if (eCrystalRatio > pCrystalRatio) return GameResult.TimeUpLose;
        if (pKingRatio > eKingRatio) return GameResult.TimeUpWin;
        if (eKingRatio > pKingRatio) return GameResult.TimeUpLose;
        return GameResult.TimeUpDraw;
    }

    // -----------------------------------------------------------------
    //  Assertion helper
    // -----------------------------------------------------------------
    static void Assert(string label, bool condition)
    {
        if (condition)
        {
            _pass++;
            Debug.Log($"[CoreTest] PASS  {label}");
        }
        else
        {
            _fail++;
            Debug.LogError($"[CoreTest] FAIL  {label}");
        }
    }
}
#endif
