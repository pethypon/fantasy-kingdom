#if UNITY_EDITOR
using System;
using UnityEngine;

public static class AIIntegratedR1RecruitmentTests
{
    static void Check(string name, bool result)
    { if (!result) throw new Exception("[R1Recruitment] FAIL " + name); Debug.Log("[R1Recruitment] PASS " + name); }
    public static void Run()
    {
        var needs = new[] { Need("healer", 1, 3), Need("healer", 1, 3), Need("frontline", 2, 3), Need("healer", 1, 5) };
        Check("EC-shared-role-from-two-squads", AIStrategicGovernor.DueOperationRoleDemand(needs, "healer", 3) == 2);
        Check("EC-future-demand-does-not-disappear", AIStrategicGovernor.DueOperationRoleDemand(needs, "healer", 5) == 3);
        Check("EC-other-role-independent", AIStrategicGovernor.DueOperationRoleDemand(needs, "frontline", 3) == 2);
        Check("EC-bounded-shopping-list", AIStrategicGovernor.DueOperationRoleDemand(new[] { Need("healer", int.MaxValue, 1), Need("healer", 2, 1) }, "healer", 1) == 3);
        Check("EC-nonpositive-needs-ignored", AIStrategicGovernor.DueOperationRoleDemand(new[] { Need("healer", -1, 1), Need("healer", 0, 1) }, "healer", 1) == 0);
        Check("EC-unsupported-role-not-purchased", AIStrategicGovernor.DueOperationRoleDemand(needs, "invented", 5) == 0);
    }
    static AIOperationRecruitmentNeed Need(string role, int count, int turn) => new AIOperationRecruitmentNeed(role, count, turn, 1);
}
#endif
