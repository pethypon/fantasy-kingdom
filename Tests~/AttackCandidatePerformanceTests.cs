#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

public static class AttackCandidatePerformanceTests
{
    public static void Run(GameSystems live)
    {
        var holder = new GameObject("Attack candidate oracle");
        var old = new LegacyAttackGenerator();
        var current = holder.AddComponent<AttackGenerator>();
        old.mapcreate = current.mapcreate = live.MapCreate;
        old.moveGenerator = current.moveGenerator = live.MoveGenerator;
        var actors = new List<GameObject>();
        UnitData authored = null; BoardActionProfile profile = null;
        int checks = 0;
        try
        {
            Status Create(Team team, Vector3 position)
            {
                var obj = new GameObject("Candidate actor"); actors.Add(obj);
                var value = obj.AddComponent<Status>(); value.team = team; value.type = Type.Unit;
                value.HP = value.MaxHP = 10; obj.transform.position = position; return value;
            }
            var actor = Create(Team.Player, new Vector3(12, 1, 12));
            for (int x = -6; x <= 6; x++) for (int z = -6; z <= 6; z++)
                if (x != 0 || z != 0) Create((x + z) % 3 == 0 ? Team.Player : Team.Enemy, actor.transform.position + new Vector3(x, 0, z));
            void Equal(string label)
            {
                if (!old.AttackP.SequenceEqual(current.AttackP)) throw new InvalidOperationException("[AttackCandidates] oracle mismatch " + label);
                checks++;
            }
            foreach (Kind kind in AttackPatterns.NormalMap.Keys)
                foreach (Direction direction in new[] { Direction.N, Direction.S })
                {
                    actor.kind = kind; actor.direction = direction;
                    old.NormalAttackPData(actor, actor.transform.position); current.NormalAttackPData(actor, actor.transform.position);
                    Equal(kind + " / " + direction);
                    foreach (var skill in SkillData.Table.Values)
                    {
                        actor.AssignedSkillId = skill.Id;
                        old.SkillAttackPData(actor, actor.transform.position); current.SkillAttackPData(actor, actor.transform.position);
                        Equal(kind + " / " + direction + " / skill " + skill.Id);
                    }
                }
            authored = ScriptableObject.CreateInstance<UnitData>(); profile = ScriptableObject.CreateInstance<BoardActionProfile>();
            authored.actionProfile = profile; actor.GrowthData = authored; actor.kind = Kind.Knight;
            profile.attack.useCustom = true; profile.attack.SetCell(5, 2, true); profile.attack.SetCell(0, 5, true);
            foreach (Direction direction in new[] { Direction.N, Direction.S })
            { actor.direction = direction; old.NormalAttackPData(actor, actor.transform.position); current.NormalAttackPData(actor, actor.transform.position); Equal("authored " + direction); }
            actor.GrowthData = null; actor.kind = Kind.Knight; actor.direction = Direction.N;
            // Same living actors, terrain and query work; candidate limits/AI scores are untouched.
            for (int i = 0; i < 10; i++) { old.NormalAttackPData(actor, actor.transform.position); current.NormalAttackPData(actor, actor.transform.position); }
            var watch = Stopwatch.StartNew();
            for (int i = 0; i < 200; i++) old.NormalAttackPData(actor, actor.transform.position);
            double before = watch.Elapsed.TotalMilliseconds; watch.Restart();
            for (int i = 0; i < 200; i++) current.NormalAttackPData(actor, actor.transform.position);
            double after = watch.Elapsed.TotalMilliseconds;
            Equal("after benchmark");
            Debug.Log("[AttackCandidates] ALL PASSED checks=" + checks + " queries=200 beforeMs=" + before.ToString("F3")
                + " afterMs=" + after.ToString("F3") + " actors=" + actors.Count + " mapCells=" + live.MapCreate.SetPos.Count);
        }
        finally
        {
            foreach (var obj in actors) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            UnityEngine.Object.DestroyImmediate(holder);
            if (authored != null) UnityEngine.Object.DestroyImmediate(authored);
            if (profile != null) UnityEngine.Object.DestroyImmediate(profile);
        }
    }
}
#endif
