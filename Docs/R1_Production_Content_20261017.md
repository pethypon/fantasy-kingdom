# R1本番コンテンツと初期調整 v1

2026-10-17。以下はプレイ可能にするための**暫定初期調整**であり、R1仕様に未記載だった値が確定仕様になったという意味ではない。Unity上で個別に変更できる。

## 導入と編集

`Fantasy Kingdom > 駒固有能力 > R1本番データを導入（未設定値のみ）`、または `R1ProductionContentInstaller.Install()` を実行する。ゲーム再生中は実行できない。自動起動フックは追加していない。

- 設定一覧: `Assets/_FantasyKingdom/GameContent/R1Production/R1ProductionSettings.asset`
- 初期調整テンプレート: 同フォルダの `Defaults/{Kind}.asset`
- 新6職種の実データ: 同フォルダの `Units/{Kind}/Stats.asset`、`Ability.asset`、`ActionTiles.asset`、`Visual.prefab`
- 召喚登録: `Assets/Resources/GameContent/UnitCatalog.asset`

導入前に全設定・競合・継承マスクを確認する。既存10職種のUnitDataと固有能力のGUIDを維持し、基礎能力値・コスト・成長率・維持費・既存マスクを変更しない。固有能力の負数・未設定enum・空曲線だけを補完し、能力を有効化する。旧Archer雛形の「無効・AP0・CD未設定・静止曲線空」に一致する場合だけ、旧常時能力のAP0を新能動スキルのAP10へ移行する。

新しい職種は既存データと見た目を独立した資産へ複製する。名前・保存ID・Kind・固有能力を設定し、移動・攻撃・視界を明示したマスクとして保持する。元の駒は変更しない。UnitCatalogへの登録で、既存の召喚・AI採用・セーブ復元の経路を利用する。

導入した職種は設定一覧の `installed` に記録する。再実行は記録済みの駒・能力・カタログを変更せず、新たな駒や設定を複製しない。導入後の調整は実際の `specialSkill` 参照先、Stats、ActionTilesを編集する。Defaultsの変更が既に導入済みの能力を自動上書きすることはない。他の作成済み駒を同じKindのカタログへ登録している場合も、その登録を無断で置き換えない。

## APとクールダウン

APのうち弓兵・侍以外はR1の指定値。CD、弓兵・侍の能動APは初期調整案。

| 職種 | スキルAP | CD |
| --- | ---: | ---: |
| Knight | 14 | 2 |
| Archer | 10 | 3 |
| Magic | 22 | 3 |
| Assassin | 14 | 3 |
| Scout | 20 | 3 |
| Priest | 8 | 0 |
| Guardian | 18 | 2 |
| Crossbow | 10 | 0 |
| Magicsniper | 18 | 2 |
| Bomber | 20 | 3 |
| WhiteMage | 12 | 2 |
| HolyKnight | 15 | 5 |
| Samurai | 8 | 4 |
| Ninja | 13 | 2 |
| Berserker | 5→8→11→15→20 | 0 |
| Navigator | 12 | 2 |

## 未指定部分の暫定調整

| 項目 | 初期調整 v1 |
| --- | --- |
| Knight | 視界内ATK低下10%、最も強い値のみ適用 |
| Archer | 静止0〜5所有ターン: 1 / 1.15 / 1.30 / 1.45 / 1.60 / 1.75倍。点間は線形 |
| Magic | 供儀上限3体 |
| Assassin | 味方撃破による強化2所有ターン、最も強い値のみ適用 |
| Scout | 無料移動・通常攻撃は各所有ターンに1回へリセット |
| Guardian | 通常攻撃回数を各所有ターンにリセット |
| Crossbow | 初期通常弾2発、雷線は敵に作用 |
| Magic Sniper | ロック距離反動は0マス0%→25マス15%の線形。通常攻撃20%自傷とは別 |
| Bomber | 爆弾・自動爆発ともChebyshev半径2、爆弾は敵味方と所持者に作用、所持者死亡時は取消 |
| HolyKnight | 中心を含む5×5範囲、被ダメージでは中断しない。発動中の被ダメージ2倍は維持 |
| Samurai | 通常攻撃ごとゲージ+1、最大3 |
| Ninja | クナイ蓄積0〜5ターンで1→2倍の線形 |
| Berserker | 段階0〜5のATK: 1/1.3/1.6/1.9/2.2/2.5、DEF: 1/.9/.8/.7/.6/.5。通常攻撃APはHP比0で1AP、1で4APの線形 |
| Navigator | 残AP0で1倍、60で1.5倍の線形 |

## 新6職種の暫定基礎値

元の職種からHP・ATK・DEF・成長率・製作費・維持費・表示Prefabを継承した。これらも独立したUnitDataとして変更できる。

| 新職種 | 基礎値・表示・移動・視界の継承元 | 通常攻撃 |
| --- | --- | --- |
| WhiteMage | Priest | Priest |
| HolyKnight | Knight | Knight |
| Samurai | Knight | Knight |
| Ninja | Assassin | Archerの正面2・3マス（遠距離クナイ） |
| Berserker | Knight | Knight |
| Navigator | Guardian | 正面1マス。向きで反転 |

WhiteMageの常時能力はR1に従って追加しない。敵味方の双方に新職種を登録し、召喚UIは既存10＋新6の16職種を表示する。

## 検証用入口

`Tests~/R1ProductionContentTests.cs` を既存の検証コピーと同様にEditorへ配置し、導入後に `R1ProductionContentTests.Run()` を実行する。16能力の有効化と `ConfigurationError == null`、新6の表示・Collider・マスク・両陣営召喚・保存ID・カタログ接続、既存値保護、再実行時の非複製を確認する。数値の競技バランスの良さを保証するテストではない。
