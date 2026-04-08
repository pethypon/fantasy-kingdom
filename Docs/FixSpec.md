# Fantasy Kingdom — 実装仕様メモ

このドキュメントは、過去に「未実装/要修正」と記載されていた項目の現状を
最新コードに合わせて整理したものです。各項目は **実装済み** であり、
本ファイルは仕様の確認とリグレッション防止用のリファレンスです。

---

## 1. クリスタルシールド — 実装済み

### 仕様

| 項目 | 値 / 挙動 |
|------|-----------|
| 発動条件 | クリスタルの HP が `MaxHP * CrystalShieldThreshold` 未満になった瞬間 |
| 持続 | `CrystalShieldDuration` ターン (現状 5 ターン) |
| 効果 | シールド中はダメージを完全無効 (`BattleSystem.Battle()` で早期 return) |
| 終了処理 | `ShieldTurns` が 0 に達した時点で `ShieldActivated = false` にリセットされ、再度 HP 50% 未満で再発動可能 |
| 永続化 | `SaveSystem` が `ShieldActivated` / `ShieldTurns` を保存・復元 |

### 定数 (`Assets/Script/Common/GameConstants.cs`)

```csharp
public const float CrystalShieldThreshold = 0.5f;   // HP 50% 未満で発動
public const int   CrystalShieldDuration  = 5;      // 持続ターン
```

### 実装箇所

| ファイル | 行 | 役割 |
|----------|----|------|
| `Assets/Script/Gamesystem/BattleSystem.cs` | L41–43 | シールド中は被ダメ無効化 |
| `Assets/Script/Gamesystem/BattleSystem.cs` | L112–128 | `CheckCrystalShield()` 発動 |
| `Assets/Script/Gamesystem/BattleSystem.cs` | L133–150 | `TickCrystalShields()` 減衰&リセット |
| `Assets/Script/Gamesystem/TurnSystem/Enemy/SimBoardState.cs` | AI シミュレーションでも同ロジックを反映 |

### 動作確認チェックリスト

- [x] クリスタル HP が 50% を切るとシールド発動
- [x] `ShieldTurns` がターン開始時 (`TickCrystalShields`) ごとに 1 ずつ減少
- [x] `ShieldTurns == 0` で `ShieldActivated = false` にリセット
- [x] 再度 HP 50% 未満になれば再発動可能
- [x] 既存の最小回帰テスト (`Assets/Script/Tests/CoreLogicTests.cs::TestCrystalShieldActivationAndReset`) が PASS

---

## 2. パッシブスキル (Knight Bulwark / Assassin Shadowstrike) — 実装済み

ダメージ計算は `DamageCalculator` に集約されており、攻撃側と防御側の
パッシブ補正は次の 2 関数で処理されます。

- `DamageCalculator.GetAttackerPassiveMultiplier(attacker, target)`
- `DamageCalculator.GetDefenderPassiveMultiplier(attacker, target)`

両者は `CalcNormal` / `CalcSkill` の中で乗算されます。

### Knight (防御側パッシブ)

| 項目 | 内容 |
|------|------|
| 仮称 | Bulwark |
| 発動条件 | 攻撃者が Knight の `VisionCell` の **中** にいる |
| 効果 | 受けるダメージ ×`KnightVisionDamageReduction` (= 0.8) |
| 副次効果 | 攻撃者が Knight の `VisionCell` の **外** にいる場合は ×`KnightOutOfVisionDamageIncrease` (= 1.1) |
| 実装 | `DamageCalculator.GetDefenderPassiveMultiplier` (DamageCalculator.cs L79–99) |

> 注: 旧仕様では「視界内のみ 0.8 倍」と記載されていましたが、
> 現状では視界外攻撃に対する 1.1 倍ペナルティも実装済みです。

### Assassin (攻撃側パッシブ)

| 項目 | 内容 |
|------|------|
| 仮称 | Shadowstrike |
| 発動条件 | 攻撃対象 (`target.VisionCell`) に Assassin が含まれていない |
| 効果 | 与ダメージ ×`AssassinShadowstrikeDamage` (= 1.25) |
| 実装 | `DamageCalculator.GetAttackerPassiveMultiplier` (DamageCalculator.cs L36–43) |

### 関連定数 (`GameConstants.cs`)

```csharp
public const float KnightVisionDamageReduction      = 0.8f;
public const float KnightOutOfVisionDamageIncrease  = 1.1f;
public const float AssassinShadowstrikeDamage       = 1.25f;
public const float PassiveMultiplierMax             = 2.0f; // 上限クランプ
```

### 同居する他キャラのパッシブ (参考)

| Kind | 効果 |
|------|------|
| Archer | 距離ボーナス: 1 マスごとに +0.25、最大 +0.75 |
| Magic | 建物に ×1.15、距離ボーナス Archer と同じ |
| Guardian | 建物に ×2.0 |

すべて `GetAttackerPassiveMultiplier` 内の `switch` で実装済み。

### ダメージ計算の適用順序 (実装上)

```
基礎ダメージ (CalcRawBase)
  → 攻撃側パッシブ (GetAttackerPassiveMultiplier)
  → 防御側パッシブ (GetDefenderPassiveMultiplier)
  → 上限クランプ (PassiveMultiplierMax)
  → Special Ability 修飾 (SpecialAbilitySystem)
  → 状態異常 IncomingDamageModifier
  → 0 でクランプ → 整数化
```

### 実装上の注意点

- `VisionCell` は必ず null チェック (`DamageCalculator` 内で済み)
- パッシブ倍率は `PassiveMultiplierMax` で上限クランプされる
- 通常攻撃 (`CalcNormal`) とスキル攻撃 (`CalcSkill`) の両方で適用される

### 動作確認チェックリスト

- [x] Knight: 視界内から攻撃で被ダメ約 20% 減
- [x] Knight: 視界外から攻撃で被ダメ約 10% 増
- [x] Knight: `VisionCell` が null でもクラッシュしない
- [x] Assassin: 視界外の敵への与ダメが約 25% 増
- [x] Assassin: 視界内の敵への与ダメが通常通り
- [x] Assassin: `VisionCell` が null の状況でクラッシュしない

---

## 3. 構造に関する補足 (旧 "実装時の注意事項" の更新)

旧版の本ドキュメントには「TurnGenerator は God Class なのでまず構造を把握する」
旨の記載がありましたが、リファクタにより以下の状態になっています:

| ファイル | 行数 | 責務 |
|----------|------|------|
| `TurnGenerator.cs` | ~80 | ステートマシン駆動 / AI モード切替 / `GameSystems` & `GameContext` 集約 |
| `TurnInputHandler.cs` | ~35 | プレイヤー入力読み取り |
| `TurnCameraController.cs` | ~50 | カメラ移動・ズーム |

ダメージ計算は `Assets/Script/Common/DamageCalculator.cs` に集約されており、
通常/スキル/AI シミュレーションすべて同関数経由で計算されます。
ダメージ式に手を入れる場合は **DamageCalculator のみ** を変更してください。

### コミット粒度

- 1 修正 = 1 コミット (戦闘式の変更とパッシブ追加は別コミット)
- ダメージ式に触るときはバフ/デバフ系の挙動 (`StatusEffectSystem`) も再確認
- 変更後は `Fantasy Kingdom > Run Core Tests` (Editor メニュー) で
  最小回帰テストを実行してから PR 化

---

## 4. リグレッション基準

`Assets/Script/Tests/CoreLogicTests.cs` の以下テストが PASS することを最低条件にする。

| テスト | 検証内容 |
|--------|----------|
| `TestDamageClamp` | ATK ≪ DEF でダメージが 0 にクランプ |
| `TestDamageBaseFormula` | `CalcRawBase` が `1 + atk/6 + atk/2 - def/4` に一致 |
| `TestCrystalShieldActivationAndReset` | HP < 50% で発動 → 5 ターン → リセット |
| `TestTimerWinnerDeterminationOrder` | 時間切れ判定が「クリスタル HP% → 王 HP%」順 |
