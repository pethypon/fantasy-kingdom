# AI実装用 仕様書

---

## 優先度1：シールドバグ修正（工数：小）

### 問題

`ShieldActivated` フラグが `true` にセットされた後、リセットされるコードが存在しない。
一度シールドが発動すると永続的に有効状態になる。

### 修正仕様

**対象ファイル：** `ShieldActivated` を参照・設定しているクラス（`TurnGenerater` または該当ユニットクラス）

**修正内容：**

以下の3箇所に `ShieldActivated = false` のリセット処理を追加する：

1. **ターン開始時**（各ユニットのターン開始処理）
   - 条件：毎ターン開始時に無条件でリセット
   - タイミング：AP回復処理の直前

2. **シールド効果の消費後**（ダメージ軽減処理の直後）
   - 条件：シールドがダメージを受けた場合
   - タイミング：軽減計算の直後、ダメージ適用の前

3. **ユニット状態リセット時**（既存のResetメソッド内）
   - 条件：ユニット初期化・復活時

### 設計意図の確認

- シールドが「1ターン持続」か「1回防御で消費」か、既存コードのコメントや命名から判断する
- 判断できない場合は **「1ターン持続、ターン終了時リセット」** を採用する

### 動作確認

1. ユニットにシールドを付与する
2. そのユニットが攻撃を受ける
3. 次ターン開始時にシールドがない状態になっていることを確認
4. 再度スキルを使うとシールドが再付与されることを確認

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

1. **TurnGenerater の構造を最初に把握する** — God Class なのでダメージ計算がどこにあるか確認してから手を入れる
2. **1修正 = 1コミット** — シールド修正とパッシブ実装は必ず別コミットにする
3. **パッシブ追加後は既存スキルの動作も再確認** — ダメージ計算に触るため、バフ/デバフ系の挙動が壊れていないかチェックする
