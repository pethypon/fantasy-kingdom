using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Editable, provisional R1 production tuning and explicit installation records.</summary>
[CreateAssetMenu(menuName = "Fantasy Kingdom/駒固有能力/R1本番導入設定")]
public sealed class R1ProductionContentSettings : ScriptableObject
{
    [Serializable]
    public sealed class Entry
    {
        public Kind kind;
        [Tooltip("既存の駒は導入対象。新しい駒は能力値・成長・費用の複製元。")]
        public UnitData sourceData;
        [Tooltip("新しい駒の表示に利用する既存の駒Prefab。元のPrefabは変更しません。")]
        public GameObject sourcePrefab;
        [Tooltip("指定時は通常攻撃マスだけをこの駒から継承します。忍者の遠距離クナイ用。")]
        public UnitData attackPatternSource;
        public SpecialSkillDefinition skillDefaults;
        public bool createPlayableUnit;
        [TextArea] public string balanceNote;
    }

    [Serializable]
    public sealed class InstalledRecord
    {
        public Kind kind;
        public UnitData data;
        public SpecialSkillDefinition ability;
        public string version;
    }

    public string version = "R1-production-provisional-20261017";
    [TextArea] public string balanceStatus;
    public List<Entry> entries = new List<Entry>();
    [Tooltip("導入済みの駒。再実行でゲーム内調整を上書きしないための記録です。")]
    public List<InstalledRecord> installed = new List<InstalledRecord>();
}
