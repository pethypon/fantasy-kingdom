# AI実装用 仕様書

---

## 優先度1：シールドバグ修正（工数：小）— ✅ 修正済み

### 問題（修正前）

`ShieldActivated` フラグが `true` にセットされた後、リセットされるコードが存在しなかった。

### 実装済みの修正内容

`BattleSystem.TickCrystalShields()` にてターン開始時に `ShieldTurns` を1減算し、
0になった時点で `ShieldActivated = false` にリセットする処理が実装済み。

**実装箇所：**
- `BattleSystem.cs` L112-150: `CheckCrystalShield()` でHP50%以下時にシールド発動
- `BattleSystem.cs` L133-150: `TickCrystalShields()` でターン開始時に減衰＆リセット
- `SimBoardState.cs` L868-879: AI用シミュレーションにも同ロジックを反映済み

**設計：**
- シールドは `GameConstants.CrystalShieldDuration` ターン持続（ターン減衰方式）
- `ShieldTurns` が0になると `ShieldActivated = false` → 再度HP50%以下で再発動可能
- セーブ/ロード時も `ShieldActivated` / `ShieldTurns` を正しく永続化済み

### 動作確認

1. [x] クリスタルHP50%以下でシールド発動
2. [x] ShieldTurns がターンごとに減少
3. [x] ShieldTurns=0 で ShieldActivated=false にリセット
4. [x] 再度HP50%以下で再発動可能

---

## 優先度2：パッシブスキル実装（工数：中）

### 現状

`switch` 文の各 `case` が `break;` のみで、パッシブ効果のロジックが空。

### 実装方針

**1スキルずつ実装・動作確認してから次へ進むこと。**

### パッシブスキル仕様

#### 【Knight】視界内攻撃ダメージ20%軽減

| 項目 | 内容 |
|------|------|
| スキル名 | Bulwark（仮） |
| 発動条件 | 攻撃者が Knight の `VisionCells` の中にいる |
| 効果 | 受けるダメージを 0.8倍 |
| タイミング | ダメージ計算時（最終ダメージ確定の直前） |
| 実装箇所 | ダメージ受け取りメソッド内のパッシブチェック部分 |

擬似コード：
```csharp
if (unit.HasPassive(PassiveType.Bulwark))
    if (unit.VisionCells != null && unit.VisionCells.Contains(attacker.Position))
        damage *= GameConstants.KNIGHT_DAMAGE_REDUCTION;
```

#### 【Assassin】視界外からの攻撃ダメージ1.25倍

| 項目 | 内容 |
|------|------|
| スキル名 | Shadowstrike（仮） |
| 発動条件 | 攻撃対象が Assassin の `VisionCells` の外にいる |
| 効果 | 与えるダメージを 1.25倍 |
| タイミング | ダメージ計算時（最終ダメージ確定の直前） |
| 実装箇所 | 攻撃ダメージ計算メソッド内のパッシブチェック部分 |

擬似コード：
```csharp
if (unit.HasPassive(PassiveType.Shadowstrike))
    if (unit.VisionCells == null || !unit.VisionCells.Contains(target.Position))
        damage *= GameConstants.ASSASSIN_BONUS_DAMAGE;
```

### 共通実装ルール

#### ダメージ計算の適用順序（必ず守ること）

```
基礎ダメージ
  → 攻撃側パッシブ補正（例：Shadowstrike）
  → 防御側パッシブ補正（例：Bulwark）
  → バフ/デバフ補正（StatusEffectSystem）
  → 最終ダメージ確定
```

#### VisionCell は必ず null チェック

```csharp
// NG -- クラッシュする
if (unit.VisionCells.Contains(target.Position))

// OK
if (unit.VisionCells != null && unit.VisionCells.Contains(target.Position))
```

#### HasPassive が存在しない場合は追加

```csharp
public bool HasPassive(PassiveType type)
{
    // 既存のスキル参照方法に合わせて実装
    // 例：return skills.Any(s => s.passiveType == type);
}
```

#### マジックナンバーは GameConstants に追加

```csharp
public const float KNIGHT_DAMAGE_REDUCTION = 0.8f;
public const float ASSASSIN_BONUS_DAMAGE   = 1.25f;
```

### 動作確認チェックリスト

**Knight パッシブ：**
- [ ] 視界内から攻撃 → ダメージが約20%減少
- [ ] 視界外から攻撃 → ダメージが通常通り
- [ ] VisionCell が null の状態で攻撃 → クラッシュしない

**Assassin パッシブ：**
- [ ] 視界外の敵を攻撃 → ダメージが約25%増加
- [ ] 視界内の敵を攻撃 → ダメージが通常通り
- [ ] VisionCell が null の状態 → クラッシュしない

---

## 実装時の注意事項

1. **TurnGenerator の構造を最初に把握する** — God Class なのでダメージ計算がどこにあるか確認してから手を入れる
2. **1修正 = 1コミット** — シールド修正とパッシブ実装は必ず別コミットにする
3. **パッシブ追加後は既存スキルの動作も再確認** — ダメージ計算に触るため、バフ/デバフ系の挙動が壊れていないかチェックする
