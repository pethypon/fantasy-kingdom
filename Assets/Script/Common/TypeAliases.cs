// =====================================================================
//  TypeAliases — レガシー命名のタイポに対するクリーンな型エイリアス
//
//  元クラス名はプレハブのシリアライズ参照に使われているためリネーム不可。
//  新規コードではこのエイリアスを使用することで可読性を保つ。
//
//  Unity 6 (6000.x) は C#11 をサポートするため global using 利用可能。
// =====================================================================

// マップ・システム
global using GameGenerator = GameGenerater;
global using MoveGenerator = MoveGererater;
global using AttackSystem = AttackPointt;
global using VisionGenerator = VisionGenerater;
global using TurnGenerator = TurnGenerater;
