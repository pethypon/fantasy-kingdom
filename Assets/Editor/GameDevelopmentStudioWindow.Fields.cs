using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public sealed partial class GameDevelopmentStudioWindow
{
    static readonly Dictionary<string,string> Labels = new Dictionary<string,string>
    {
        {"displayName","表示名"},{"category","種類の分類"},{"defaultTeam","初期陣営"},{"kind","基礎役割（AIの判断用）"},
        {"availableToPlayer","プレイヤーが作成できる"},{"availableToEnemy","敵AIが作成できる"},{"prefab","見た目のPrefab"},
        {"baseHP","最大HP"},{"baseATK","攻撃力"},{"baseDEF","防御力"},{"hpGrowth","HP成長率"},{"atkGrowth","攻撃成長率"},{"defGrowth","防御成長率"},
        {"costWood","作成費：木材"},{"costStone","作成費：石材"},{"costIron","作成費：鉄"},{"costMagic","作成費：魔石"},
        {"costWater","作成費：水"},{"costBread","作成費：パン"},{"costCitizen","作成費：市民"},{"costAP","作成費：AP"},
        {"upkeepWood","維持費：木材"},{"upkeepStone","維持費：石材"},{"upkeepIron","維持費：鉄"},{"upkeepMagic","維持費：魔石"},
        {"upkeepWater","維持費：水"},{"upkeepBread","維持費：パン"},{"upkeepMagicScales","魔石の維持費も成長に合わせる"},
        {"useAuthoredAbilities","この駒専用の能力を使う"},{"authoredPassive","常時発動する能力"},{"authoredSpecialAbility","特殊能力"},
        {"behaviourKind","施設の基本動作"},{"buildAP","建築費：AP"},{"HP","最大HP"},{"ATK","攻撃力"},{"DEF","防御力"},
        {"UpgradeAP","強化費：AP"},{"SpecialValue","特殊効果の値"},{"BonusChance1","確率生産１の発生率"},{"BonusChance2","確率生産２の発生率"},
        {"Wood","木材"},{"Stone","石材"},{"Iron","鉄"},{"MagicOre","魔石"},{"Wheat","小麦"},{"Bread","パン"},{"Water","水"},{"Citizen","市民"},
        {"applyRules","このルールで新規ゲームを始める"},{"aiBreadReserveTurns","AIが確保するパンの予備ターン数"},{"turnSeconds","１ターンの制限時間（秒）"},{"totalSeconds","各軍の持ち時間（秒）"},
        {"timeBonusSeconds","手動でターン終了した時の加算秒数"},{"maximumAP","行動ポイントの上限（AP）"},{"moveAP","基本移動AP"},{"attackAP","基本攻撃AP"},{"heightAP","高さ差１段の追加AP"},
        {"citizenAP","市民１人によるAP増加"},{"breadPerCitizen","市民１人が消費するパン"},{"starvationGraceTurns","飢餓の猶予ターン数"},
        {"mapWidth","マップの横幅（マス）"},{"mapDepth","マップの奥行き（マス）"},{"noiseScale","地形の細かさ"},{"riverHalfWidth","川の半幅"},
        {"color","陣営の表示色"},{"initialAP","初期AP"},
        {"InitialIP","初期介入ポイント（IP）"},{"HistoryTurns","戦況履歴の保存ターン数"},{"PredictionTurns","戦況を予測するターン数"},
        {"ReserveProbability","交戦中に介入を見送る確率"},{"ActiveWarThreshold","激しい交戦と判断する値"},{"BalancedPowerRatio","互角と判断する戦力比"},
        {"StagnationThreshold","膠着と判断する値"},{"DecisiveHPRatio","決着間近と判断するクリスタルHP比"},
        {"MaximumAdvantageEraseRatio","優勢を削る上限比"},{"MaximumPowerSwingRatio","戦力を変える上限比"},{"PreventLeaderFlip","イベントで優勢軍を逆転させない"},
        {"MaximumStrongEnemyExpansion","強敵の領土拡大回数の上限"},{"MinimumTurnsBetweenMajorEvents","大型イベントの最短間隔（ターン）"},
        {"GlobalEventCooldown","全イベント共通の待機ターン数"},{"MinimumObjectiveSpawnDistance","目標から出現位置までの最短距離"},{"ResponseTurns","対応できる猶予ターン数"},
        {"MaxCandidates","候補数の上限"},{"MaxSpawnCells","出現位置の確認数上限"},{"DirectorBudgetMs","イベント判断の時間上限（ms）"},
        {"TacticalSliceMs","１フレームのAI処理時間（ms）"},{"TacticalMaxActions","１ターンのAI行動数上限"},
        {"TacticalPathExpansions","経路探索の確認数上限"},{"TacticalPathBudgetMs","経路探索の時間上限（ms）"},
        {"DisplayName","イベントの表示名"},{"IpCost","必要な介入ポイント（IP）"},{"MinThreatLevel","発生する最低脅威度"},{"MaxThreatLevel","発生する最高脅威度"},
        {"CooldownTurns","再発生までの待機ターン数"},{"MajorCooldownTurns","大型イベント後の待機ターン数"},
        {"TargetPlayer","プレイヤーを対象にする"},{"TargetEnemy","敵軍を対象にする"},{"TargetThirdFaction","第三陣営を対象にする"},
        {"AllowedDuringPreserveWar","交戦を保護している間も許可"},{"AllowedDuringDecisivePhase","決着間近でも許可"},
        {"ObjectiveTurns","襲撃の継続ターン数"},{"EstimatedPowerSwing","想定する戦力変化の比率"},{"EstimatedDisruption","想定する戦況への影響度"},
        {"MinimumStagnation","発生に必要な膠着度"},{"Prefab","襲撃する駒のPrefab"},{"Stats","襲撃する駒の能力データ"},
        {"SpawnCount","出現する駒の数"},{"CanActImmediately","出現したターンから行動する"},{"LocalExpansionRadius","一度に拡大する範囲（マス）"},
        {"EncounterId","遭遇データID（既存コンテンツ用）"},
        {"enableDecisionLogs","AI経済の判断ログを出力"},
        {"forecastTurns","経済を予測するターン数"},
        {"reserveTurns","資源を確保する予備ターン数"},
        {"recoveryWindowTurns","不足在庫を回復する目標ターン数"},
        {"plannedDemandWeight","将来の建築・召喚を見込む割合"},
        {"needWeight","不足の緊急度による建築加点"},
        {"coverageWeight","不足を解消する割合による建築加点"},
        {"chainRecoveryWeight","停止した生産を復旧する加点"},
        {"reserveRecoveryWeight","予備資源を回復する加点"},
        {"overstockPenalty","不要な増産への減点"},
        {"warningMilitaryPenalty","経済警戒時の軍事建築への減点"},
        {"localMilitarySaturationPenalty","近くに防衛施設が多い場合の減点"},
        {"uncoveredWallThreatPenalty","敵の侵入経路と無関係な壁への減点"},
        {"emergencyKingDamageFraction","Kingの緊急防衛を始める被害割合"},
        {"emergencyCrystalDamageFraction","クリスタルの緊急防衛を始める被害割合"},
        {"criticalReserveFraction","経済危機と判断する予備資源の割合"},
        {"sustainedDeficitUrgency","継続する赤字の最低緊急度"},
        {"deficitEpsilon","生産不足を判定する最小量"},
        {"plannedUnitLevel","軍拡張の維持費を見込む駒レベル"},
        {"plannedBuildActions","予測する建築回数"},
        {"plannedSummonActions","予測する召喚回数"},
        {"explorationSummonWeight","探索中の追加召喚を見込む割合"}
    };
    static void Fields(SerializedObject so, params string[] names) { foreach (var name in names) Field(so.FindProperty(name)); }
    static void Field(SerializedProperty p)
    {
        if (p == null) return;
        string label = Labels.TryGetValue(p.name,out var translated) ? translated : p.displayName;
        if (p.propertyType == SerializedPropertyType.Enum)
        {
            if (p.name == "kind") { p.intValue = EnumPopup(p.intValue,label,typeof(Kind),v => KindNameJP.Get((Kind)v)); return; }
            if (p.name == "defaultTeam") { p.intValue = EnumPopup(p.intValue,label,typeof(Team),v => TeamLabel((Team)v)); return; }
            if (p.name == "behaviourKind") { p.intValue = EnumPopup(p.intValue,label,typeof(FacilityKind),v => FacilityData.Table.TryGetValue((FacilityKind)v,out var info) ? info.DisplayName : "未設定"); return; }
            if (p.name == "authoredPassive")
            { p.intValue = EnumPopup(p.intValue,label,typeof(PassiveSkill),v => new[]{"なし","鉄壁","狩人の目","破壊者","暗殺","狙撃","異形の王のオーラ"}[Convert.ToInt32(v)]); return; }
            if (p.name == "authoredSpecialAbility")
            { p.intValue = EnumPopup(p.intValue,label,typeof(SpecialAbility),v => SpecialAbilityData.Table.TryGetValue((SpecialAbility)v,out var info) ? info.NameJP : "なし"); return; }
        }
        EditorGUILayout.PropertyField(p,new GUIContent(label),true);
    }
    static int EnumPopup(int current,string label,System.Type type,Func<object,string> translate)
    {
        var values=Enum.GetValues(type); var names=new string[values.Length]; var integers=new int[values.Length];
        for(int i=0;i<names.Length;i++){var value=values.GetValue(i);names[i]=translate(value);integers[i]=Convert.ToInt32(value);}
        return EditorGUILayout.IntPopup(label,current,names,integers);
    }
    static string TeamLabel(Team team) => team == Team.Player ? "プレイヤー" : team == Team.Enemy ? "敵軍"
        : team == Team.Monster ? "魔物（第三陣営）" : team == Team.Intruder ? "乱入者（第三陣営）" : team == Team.Obstacle ? "強敵" : "未所属";
    static void DrawBundle(SerializedProperty bundle,string label)
    {
        if (bundle == null) return;
        bundle.isExpanded = EditorGUILayout.Foldout(bundle.isExpanded,label,true);
        if(!bundle.isExpanded)return;
        EditorGUI.indentLevel++;
        foreach(var key in new[]{"Wood","Stone","Iron","MagicOre","Wheat","Bread","Water","Citizen"}) Field(bundle.FindPropertyRelative(key));
        EditorGUI.indentLevel--;
    }
    static void DrawSkill(SerializedProperty p)
    {
        var ids = new List<int>{-1}; var names = new List<string>{"なし"};
        foreach(var entry in SkillData.Table){ids.Add(entry.Key);names.Add(entry.Value.Name);}
        p.intValue=EditorGUILayout.IntPopup("使用するスキル",p.intValue,names.ToArray(),ids.ToArray());
    }
}
