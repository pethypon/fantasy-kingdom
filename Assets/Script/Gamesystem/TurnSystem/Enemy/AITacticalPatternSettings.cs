using UnityEngine;

/// <summary>Inspector-editable rules, loaded once by the commander from Resources/AI.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/AI/Tactical patterns", fileName = "AITacticalPatternSettings")]
public sealed class AITacticalPatternSettings : ScriptableObject
{
    public AITacticalRule[] Rules = AITacticalPatterns.DefaultRules();
}
