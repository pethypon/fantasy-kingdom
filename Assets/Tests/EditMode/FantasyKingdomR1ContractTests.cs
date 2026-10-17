using System;
using System.Reflection;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEditor.SceneManagement;
using UnityEngine.TestTools;

// Runtime scripts are the existing predefined Assembly-CSharp. Reflection avoids
// moving the whole production codebase into asmdefs just to add framework tests.
public sealed class FantasyKingdomR1ContractTests
{
    static System.Type Runtime(string name) => System.Type.GetType(name + ", Assembly-CSharp", true);
    static void Set(object value, string name, object data) => value.GetType().GetField(name).SetValue(value, data);
    static T Get<T>(object value, string name) => (T)value.GetType().GetField(name).GetValue(value);

    [UnityTest]
    public IEnumerator RuntimeMechanics_Environment_AndCombatAI_ThroughRealScene()
    {
        if (!Application.dataPath.Contains("UnityValidation")) Assert.Ignore("Run integration fixtures in the isolated UnityValidation project.");
        EditorSceneManager.OpenScene("Assets/Scenes/SampleScene.unity");
        yield return new EnterPlayMode();
        var game = UnityEngine.Object.FindFirstObjectByType(Runtime("GameGenerator"));
        Assert.That(game, Is.Not.Null);
        var title = Runtime("TitleScreenUI").GetProperty("Instance").GetValue(null) as Component;
        game.GetType().GetField("_waitingForTitle", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(game, false);
        if (title != null) UnityEngine.Object.DestroyImmediate(title.gameObject);
        UnityEngine.Random.InitState(12345);
        game.GetType().GetMethod("StartGameInit", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(game, new object[] { -1 });
        var turn = UnityEngine.Object.FindFirstObjectByType(Runtime("TurnGenerator"));
        var systems = turn.GetType().GetProperty("Systems").GetValue(turn);
        foreach (var suite in new[] { "CrossbowR1Tests", "AbilityR1Tests" })
            System.Type.GetType(suite + ", Assembly-CSharp-Editor", true).GetMethod("Run").Invoke(null, new[] { systems });
        System.Type.GetType("TerrainEnvironmentTests, Assembly-CSharp-Editor", true).GetMethod("PlayMode").Invoke(null, new[] { systems });
        System.Type.GetType("SpecialSkillTargetingTests, Assembly-CSharp-Editor", true).GetMethod("PlayMode").Invoke(null, new[] { systems });
        System.Type.GetType("SpecialSkillAITests, Assembly-CSharp-Editor", true).GetMethod("PlayMode").Invoke(null, new[] { systems });
        System.Type.GetType("SpecialSkillR1AITests, Assembly-CSharp-Editor", true).GetMethod("PlayMode").Invoke(null, new[] { systems });
        System.Type.GetType("SpecialSkillUITests, Assembly-CSharp-Editor", true).GetMethod("PlayMode").Invoke(null, new[] { systems });
        yield return new ExitPlayMode();
    }

    [UnityTearDown]
    public IEnumerator EndFixturePlayMode()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }

    [Test]
    public void NaturalRecovery_FifthCompletedTurn_HealsAtEnd_AndDuplicateEndIsIdempotent()
    {
        var host = new GameObject("R1 UTF recovery fixture");
        try
        {
            var actor = host.AddComponent(Runtime("Status"));
            Set(actor, "MaxHP", 100);
            Runtime("Status").GetProperty("HP").SetValue(actor, 40);
            Set(actor, "NaturalRecovery", Activator.CreateInstance(Runtime("NaturalRecoveryState")));
            var end = Runtime("NaturalRecoverySystem").GetMethod("ProcessActorEnd");
            for (int turn = 1; turn <= 4; turn++) end.Invoke(null, new object[] { actor, turn });
            Assert.That(Runtime("Status").GetProperty("HP").GetValue(actor), Is.EqualTo(40));
            end.Invoke(null, new object[] { actor, 5 });
            Assert.That(Runtime("Status").GetProperty("HP").GetValue(actor), Is.EqualTo(50));
            end.Invoke(null, new object[] { actor, 5 });
            Assert.That(Runtime("Status").GetProperty("HP").GetValue(actor), Is.EqualTo(50));
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    [Test]
    public void NaturalRecovery_ActualOneHpLoss_Resets_ButEqualAssignmentDoesNot()
    {
        var host = new GameObject("R1 UTF HP fixture");
        try
        {
            var actor = host.AddComponent(Runtime("Status")); Set(actor, "MaxHP", 100);
            var hp = Runtime("Status").GetProperty("HP"); hp.SetValue(actor, 60);
            var recovery = Activator.CreateInstance(Runtime("NaturalRecoveryState"));
            Set(recovery, "ConsecutiveSafeTurns", 5); Set(actor, "NaturalRecovery", recovery);
            hp.SetValue(actor, 60); Assert.That(Get<int>(recovery, "ConsecutiveSafeTurns"), Is.EqualTo(5));
            hp.SetValue(actor, 59); Assert.That(Get<int>(recovery, "ConsecutiveSafeTurns"), Is.Zero);
            Assert.That(Get<bool>(recovery, "HpLostSinceLastOwnTurn"), Is.True);
        }
        finally { UnityEngine.Object.DestroyImmediate(host); }
    }

    [Test]
    public void UndefinedNewActiveSkillCosts_AreNotInvented_AndRageCostsStaySequential()
    {
        var definition = ScriptableObject.CreateInstance(Runtime("SpecialSkillDefinition"));
        try
        {
            var ability = Runtime("UnitSpecialSkill");
            foreach (string name in new[] { "ArcherStationary", "SamuraiCounter" })
            {
                Set(definition, "ability", Enum.Parse(ability, name));
                definition.GetType().GetMethod("ApplySpecifiedAPCost").Invoke(definition, null);
                Assert.That(Get<int>(definition, "apCost"), Is.EqualTo(-1), name);
                Assert.That(definition.GetType().GetProperty("IsPassive").GetValue(definition), Is.False);
            }
            Set(definition, "ability", Enum.Parse(ability, "BerserkerRage"));
            definition.GetType().GetMethod("ApplySpecifiedAPCost").Invoke(definition, null);
            Assert.That(Get<int[]>(definition, "stageAPCosts"), Is.EqualTo(new[] { 5, 8, 11, 15, 20 }));
        }
        finally { UnityEngine.Object.DestroyImmediate(definition); }
    }

    [Test]
    public void Crossbow_ThreeRounds_AndExactLightningAgeMultipliers()
    {
        var definition = Runtime("SpecialSkillDefinition");
        Assert.That(definition.GetField("CrossbowMagazineCapacity").GetRawConstantValue(), Is.EqualTo(3));
        var multiplier = definition.GetMethod("StakeMultiplier");
        Assert.That(multiplier.Invoke(null, new object[] { 1 }), Is.EqualTo(1.25f));
        Assert.That(multiplier.Invoke(null, new object[] { 2 }), Is.EqualTo(1.5f));
        Assert.That(multiplier.Invoke(null, new object[] { 3 }), Is.EqualTo(2f));
    }

    [Test]
    public void Telemetry_IsOptIn_Resettable_AndBounded()
    {
        var telemetry = Runtime("CombatRuleTelemetry"); telemetry.GetMethod("Reset").Invoke(null, null);
        var record = telemetry.GetMethod("Record");
        object[] arguments = { "utf", null, null, null, 0, 0, 0f, true, false };
        record.Invoke(null, arguments);
        Assert.That(telemetry.GetProperty("Count").GetValue(null), Is.Zero);
        telemetry.GetProperty("Enabled").SetValue(null, true);
        for (int i = 0; i < 600; i++) record.Invoke(null, arguments);
        Assert.That(telemetry.GetProperty("Count").GetValue(null), Is.EqualTo(512));
        telemetry.GetMethod("Reset").Invoke(null, null);
        Assert.That(telemetry.GetProperty("Enabled").GetValue(null), Is.False);
    }

    [Test]
    public void AtomicSave_TransientSharingLock_RetriesWithoutDeletingValidGeneration()
    {
        string directory = Path.Combine(Path.GetTempPath(), "FK-R1-Atomic-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string staged = Path.Combine(directory, "new.tmp"), current = Path.Combine(directory, "current.json"), previous = Path.Combine(directory, "previous.json");
        var storage = Runtime("AtomicSaveStorage");
        var injection = storage.GetField("ReplaceForTests", BindingFlags.Static | BindingFlags.NonPublic);
        var original = injection.GetValue(null); int calls = 0;
        try
        {
            File.WriteAllText(staged, "new"); File.WriteAllText(current, "old");
            injection.SetValue(null, new Action<string,string,string>((source, destination, backup) =>
            {
                calls++;
                if (calls <= 2) throw new IOException("Injected sharing violation", unchecked((int)0x80070020));
                File.Replace(source, destination, backup);
            }));
            storage.GetMethod("Replace", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { staged, current, previous });
            Assert.That(calls, Is.EqualTo(3)); Assert.That(File.ReadAllText(current), Is.EqualTo("new"));
            Assert.That(File.ReadAllText(previous), Is.EqualTo("old"));
        }
        finally
        {
            injection.SetValue(null, original);
            foreach (var file in Directory.GetFiles(directory)) File.Delete(file);
            Directory.Delete(directory);
        }
    }
}
