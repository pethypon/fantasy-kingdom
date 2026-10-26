# メインクリスタル新規則 R1：実装記録

対象：`4100681` を基準とした `codex/ai-integrated-r1`。2026-10-27。
ユーザーの指示により、通常対戦・AI挙動調査は実行していない。追加テストはコードとして用意したもので、実行結果は **NOT RUN**。

## 確定仕様の反映

- 新規ゲームのメインクリスタル基本HPを `MainCrystalRules.MaximumHP = 5000` に集約し、`CrystalSystem.CrystalHP` も同じ値を参照する。
- メインクリスタル固有の半HP時シールドを撤去。過去の `ShieldTurns` / `ShieldActivated` / `ShieldEverActivated` は保存互換用に残すが、main crystal の保護には使わない。通常の駒のシールドとバリアは維持する。
- 正式な通常攻撃・攻撃スキルの予約のみ記録し、攻撃陣営の正式終了時に反撃する。被弾ごと・開始時の旧APIは反撃しない。
- 晶ごと・攻撃陣営ごと・正式roundごとに一回。Player / Enemy / Monster / Intruder の固定4枠であり、履歴リストは増えない。
- 現在の反撃対象は、攻撃側陣営の生存する駒のうち最大3体。各対象のその時点のMaxHPの15%を使用する。
- 反撃予約はダメージ処理前に消費し、死亡・勝敗処理は既存の正式な `HandleDeathIfDead` に委譲する。終了済みゲーム・破壊済み晶は追加反撃しない。
- 反撃ダメージも共有のバリア・完全無効・生還処理を使う。一般の被ダメージ規則を無効化するAI専用ダメージではない。

## 継承した規則と、提案として残す境界

| 項目 | 採用した実装 | 区分 |
| --- | --- | --- |
| 反撃範囲 | `TerritorySystem.IsInTerritory(position, crystal.team)`。既存の晶所有領土内という判定をそのまま継承 | 既存正式規則の再利用。新しい固定半径ではない |
| 主な対象順 | Chebyshevの近距離順 | 既存正式規則の再利用 |
| 同距離の順序 | cellの X→Z→Y、最後にstable life ID | 既存が未定義だったため決定的な実装提案。ユーザー確定値とはしない |
| 端数 | `Mathf.RoundToInt(target.MaxHP * .15f)` | 既存反撃の丸めを継承。最小1ダメージを新設しない |
| 発火する攻撃 | 正式な通常攻撃・直接攻撃スキルの正の要求ダメージ。完全無効で実HP損失0でも合法な要求があれば予約。毒・自傷・反射等からは予約しない | 0ダメージ境界の実装方針。ミスやキャンセルを新たに命中扱いしない |
| 第三勢力 | 共通の正式 `OnOwnerTurnEnd` を通るMonster/Intruderについて陣営を分けて管理 | 既存の敵対・ターン順は変更しない |

領土はサブクリスタルによって拡張され得るため、ここで継承した判定を「晶から新しい半径Nマス」とは表現しない。

## 保存互換と既存報酬

`UnitSaveData.MainCrystalRuleVersion` と `MainCrystalCounter` は任意フィールド。既存の全体schema番号だけを変更して旧ファイルを拒否しない。

旧規則の基本HPはこの基準版では15000。旧保存については、その基本値を超える既存報酬を `max(0, savedMaxHP - 15000)` として維持し、新基本5000へ加える。残HP割合を保つ。例：旧 `8000/16000` は新 `3000/6000`。死んでいる晶はHP0のまま。

新規則版の保存は、そのまま保存MaxHPと残HPを復元する。CrystalShard等の正当な上積みを再び引かず、報酬を再付与もしない。基本5000への変更は、既存アーティファクトの `MaxHP +1000` を削除する意味ではない。サブクリスタルのHP・報酬・経済処理は変更しない。

反撃pendingと消費済みroundをコピーして保存する。読み込み時には保存roundより未来の予約・不正なルール版を拒否する。旧保存に予約がなければ何も発火しない。

## 仮想盤面とAIへの公開API

- `SimUnit.HasDamageShield` と `Status.HasDamageShield` で、mainの旧シールドのみを除外する。
- `SimBoardState` はターン遷移前、攻撃陣営の終了時にpendingを消費する。攻撃直後には反撃せず、cloneは独立したpending状態を持つ。
- 仮想盤面の反撃範囲は、自軍の所有領土と現在観測できた相手領土だけを取得する。未視認相手領土の真値はシミュレーションへ渡さない。
- `MainCrystalRules.ObservedCounterDamage(board, actor, candidatePosition, attackedCrystal = null)` は、生の反撃packetの見込みを返す。攻撃対象の晶を指定すれば、候補攻撃がpendingを生む場合を評価できる。未視認の晶は扱わない。
- AI側の呼出元は既存の `AIObservedDamageBudget` へpacketを渡し、バリア等を一回だけ消費する。未知領土は観測済み範囲と同じ確度で扱わない。
- 晶を倒しきる候補については、呼出側が「反撃元がもう生存しない」を評価し、余分な反撃危険を加えない。

## Prefab更新

`CrystalPrefabEditor.ApplyMainCrystalRules` は2つのmain prefabだけをPrefabUtility経由で更新する。HP、MaxHP、廃止されたシールド値だけが対象。シーンやモデル/マテリアルを作り直さず、他のdirty assetを `SaveAssets` で保存しない。

非Play時のコンパイル後callbackとビルド前に実行する。未保存のPrefab Stage、dirtyなprefabは自動更新を避ける。手動メニューは `Fantasy Kingdom/ルール/メインクリスタルを新仕様へ更新`。

## 追加した開発用契約

`Tests~/IntegratedCrystalRulesTests.cs`：基本5000、旧保存の割合、CrystalShard上積み、予約の重複/次ターン/JSON/Player視点交換、両陣営で終了時の最大3体、旧開始/被弾API不発火、破壊済み晶不発火、仮想盤面clone分離。

`CoreLogicTests` と `SceneSmoke` の旧「15000/半HPシールド」期待値は、新仕様の正式値とmain以外の保護を維持する契約へ更新。

実行したと記載するには、別途、実行コマンド・版・結果が必要。本ファイル作成時点では、挙動調査・自動対戦・追加契約の実行を行っていない。
