# 固有能力・スキル操作・環境・戦闘AI R1 統合結果

2026-10-17。対象：`58ead5ed-3573-4168-8d52-f3f25f6b4d57/貼り付けたテキスト.txt` のR1仕様と、その後のスキル操作の追加要望。

## 今回のスキル操作

選択した味方のスキルボタンと2キーを同じコマンド経路へ接続した。

| スキルの対象 | 操作 |
| --- | --- |
| 敵・味方を1体選ぶ | ボタン／2キーで範囲表示 → 範囲内の対象を左クリックして発動 |
| 自分・範囲内の全対象へ一括で作用 | ボタン／2キーで即時発動。対象なし、AP不足、準備中、未設定の場合は理由を表示し、消費しない |
| マップ全域から味方1体を選ぶ位置交換・生命接続など | 対象を選ぶ操作を維持。射程が全域でも、対象は自動で決めない |
| 魔法使いの供儀 | マップで敵を指定してから供儀する味方を選択 |
| 固有能力が未設定の既存データ型スキル | 単体／指定マスは範囲選択、Self／SelfAreaは即時発動 |

紫は使用範囲、橙は選択・ホバー中の効果範囲。右クリックで取消、1キーで通常攻撃へ戻る。固定スキル中に通常攻撃用の数値プレビューや赤い範囲を重ねない。旧直線スキルのプレビュー始点も実際の発射位置に揃えた。

即時発動は、元の `SpecialSkillRuntime.TryExecute` を1回だけ呼ぶ。拒否した2キー入力も処理済みとして扱い、通常スキル経路への二重実行を防ぐ。狂戦士は1操作につき次の1段階だけ上昇し、APは5→8→11→15→20。装填・自己強化・突進は、敵がいなくても元の使用条件を満たせば使用できる。斥候と聖騎士は本人も効果対象にできる。自死する司祭は本人だけを対象数に含めない。

確定直前にAP・対象・状態を再検証し、成功後は生存している駒の選択・移動範囲・ステータスを復元する。自死した駒のパネルは閉じ、ゲーム終了状態を上書きしない。ロック射撃でもスタン・死亡・攻撃不可の駒はAP消費前に拒否する。供儀／能力詳細のモーダルは行動ログなどの独立HUDより上へ描画し、背面の操作を遮る。

## R1の実装

既存のStatus、戦闘、視界、地形、AI、セーブの経路を拡張した。別の戦闘AI・HP管理・視界管理は追加していない。

### HP・回復・強制移動

- HP変化結果に要求量、実損失、実回復、発生源・対象・原因、完全無効を記録できる。自然回復の時計は実際にHPが1以上減った時にリセットする。
- 無損失の所有ターンを5回完了した、その終了時から最大HP10%を回復する。同じターンの終了処理を重複適用しない。自傷・供儀・毒・煙・雷線・爆発・反撃も共通HP経路へ接続した。
- Guardianの生命接続は、元対象にとって致死の攻撃なら元対象を救い、接続相手を死亡させて解除する。完全無効は肩代わりより先に解決する。
- 押し出し、引き寄せ、位置交換、洞窟到着を共通の移動確定処理へ接続した。地表高、配置、占有、環境侵入、雷線、視界を更新する。Knightの連鎖押し出しは全対象の移動先を検証してから確定する。

### 駒の固有能力

| 駒 | R1で統合した主な処理 |
| --- | --- |
| Knight | 前方3段階の突破・連鎖押し出し・侵入地形処理。既存の視認ATK低下と背面補正を維持 |
| Archer | 周囲2マスの敵個体を記憶、2ターンの隠密・毒、対象敵ATK20%上昇、本人の攻撃禁止。静止時最大1.75倍は別の常時能力 |
| Magic | 周囲の低HP駒による対象別倍率、15%供儀の実損失合計を予約攻撃へ反映 |
| Assassin | 味方通常攻撃、HP条件に応じた処刑、味方撃破後の設定付き強化、敵撃破後の自HP1損失 |
| Scout | 設定したリセット周期で移動・通常攻撃を各1回無料化。既存の視界内味方へのダメージ無効と別管理 |
| Priest | 周囲1マスの敵味方を所有ターン開始時に5%回復、自死して周囲2マスを浄化 |
| Guardian | クリスタルへの通常攻撃2倍、通常攻撃回数によるAP増加、生命接続 |
| Crossbow | 3発のFIFO弾倉、通常弾補充、杭弾の装填、満弾変換、命中時の杭、移動に追従する雷線 |
| Magic Sniper | 通常射撃ごとの最大HP20%自傷、HP割合条件による1.5倍、ロック射撃時の距離反動を別のダメージとして解決 |
| Bomber | 自分が実ダメージを与えなかった所有ターンを数え、10ターンで現在HP2倍の範囲爆発・自死。5ターン爆弾付与は別状態 |
| White Mage | 視界内味方の最大HP30%回復 |
| Holy Knight | 攻撃後のATK25%奪取・翌所有ターン加算・期限時の相手へのATK加算と解除。設定範囲で3ターン回復するスキルとは別管理 |
| Samurai | 刀ゲージ反撃・実ダメージ分の回復を維持。敵を3所有ターン挑発する能動スキルを追加 |
| Ninja | 非攻撃ターンのクナイ蓄積、視界外からの攻撃0.4倍、マップ上の味方との位置交換 |
| Berserker | 使用ごとに1段階強化、最大5段階。1回攻撃するとスキル由来の強化・弱体を解除 |
| Navigator | 残APによる通常攻撃倍率と、正面1マスへの敵の引き寄せ |

新しい状態は既存の `Status.Special` とセーブへ追加し、試合開始時の `ResetForMatch` に接続した。列挙値は既存の番号を維持し、後方へ追加した。

Crossbowの満弾変換は試合・ターン・駒・操作順の安定した値を使い、Unityの外部乱数状態によって結果が変わらない。候補の合法性を照会しただけでは弾倉を生成・変更しない。雷線の同じ移動処理中の重複命中も防ぐ。

### 環境と視界

- 森の視界低下・近距離発見・攻撃後露見。露見状態は個体に追従して期限切れで消える。
- 岩場の遠距離0.75倍とStoneタグ1.25倍。攻撃名の文字列で判定しない。
- 隠し間欠泉の陣営別発見と最大HP25%侵入ダメージ。
- 煙の標準30ラウンド・所有ターンごと最大HP10%ダメージ。
- 洞窟は相互接続した1組を同時に1体だけが使用し、AP3で予約、次の所有ターンに到着。出口占有時は到着を保留する。
- Archerの個体への隠密はタイルFogと分離し、選択・攻撃・AI・Renderer・Collider・HP表示へ同じ `IsActorVisibleTo` を適用した。

環境設定の詳細と互換性は [環境R1の記録](Environment_R1_20261017.md) を参照。

### AI・軽量化・復旧性

- 既存の候補生成・評価・実行へ新能力と地形の影響を接続した。現在の優先順位・Governorを維持する。
- 既知の地形・視認した駒から、押し出し・引き寄せ・交換先の危険、回復、将来の雷線と経路を評価する。未発見の間欠泉や隠れた駒を世界データから先読みしない。
- 自傷、味方巻き込み、敵への回復・将来ATK返却、退避中の攻撃禁止も評価する。Knightの移動に架空の通常攻撃ダメージを加点しない。
- 将来位置の評価件数に上限を設けた近似評価であり、全ての複数ターン展開を完全シミュレーションしたものではない。
- AP0でも無料行動があれば候補を生成できる。既存の反復・時間上限は維持する。
- 能力を持つ生存個体の照会はフレーム・登録変更に応じたキャッシュを使う。範囲表示は共有メッシュ1つで描画し、マスごとのGameObject／Collider／Updateを作らない。ホバープレビューは約0.08秒間隔。
- 戦闘テレメトリは標準OFF、ONでも512件のリング上限。通常実行で無制限にログ・リストが増えない。
- Windowsの一時的な保存ファイル共有ロックに限って、原子的置換を最大2回再試行する。既存の有効な世代を先に削除する回避策は使わない。

## 主要ファイル

### 新規

- `Assets/Script/Unit&Battle/SpecialSkillDefinition.R1.cs` / `.Crossbow.cs`、`SpecialState.R1.cs` / `.Crossbow.cs`、`CombatAttackTags.cs`
- `Assets/Script/Gamesystem/SpecialSkillRuntime.AbilitiesR1.cs` / `.Crossbow.cs` / `.ForcedMovement.cs`、`HpChangeResult.cs`、`CombatRuleTelemetry.cs`
- `Assets/Script/Gamesystem/TurnSystem/Enemy/AIActionEvaluator.SpecialProjection.cs`
- `Assets/Script/Gamesystem/TurnSystem/Player/PlayerAttack.SkillCommands.cs` / `.SpecialTargeting.cs` / `.LegacySkillPreview.cs`
- `Assets/Script/Mapsystem/TerrainEnvironmentRuntime.Dynamic.cs`
- `Assets/Script/UI/SkillTargetRangeView.cs`
- `Assets/Tests/EditMode/FantasyKingdomR1ContractTests.cs` とテスト専用asmdef
- `Tests~/AbilityR1Tests.cs`、`CrossbowR1Tests.cs`、`SpecialSkillR1AITests.cs`、`SpecialSkillTargetingTests.cs`

### 既存経路の変更

- `SpecialSkillRuntime.cs` / `.Actions.cs` / `.Hp.cs` / `.Turns.cs` / `.Persistence.cs`、`SpecialSkillDefinition.cs`、`SpecialState.cs`
- `Status.cs`、`UnitData.cs`、`SkillData.cs`、`BattleSystem.cs`、`DamageCalculator.cs`、`NaturalRecoverySystem.cs`、`CombatRegistry.cs`
- `GameGenerator.cs`、`GameSaveSystem`関連の特殊状態保存、`AtomicSaveStorage.cs`
- `VisionGenerator.cs`、`UnitClick.cs`、`UnitSelectionPicker.cs`、`AttackGenerator.cs`
- `PlayerMove.cs`、`PlayerAttack.cs`、`APSystem.cs`、所有ターンの終了処理、`SimBoardState.Turn.cs`
- `AIActionGenerator.SpecialSkills.cs`、`AIActionEvaluator.SpecialSkills.cs`、`AIActionExecutor.cs`、`AIBoardState.SpecialSkills.cs`、既存の戦闘・地形・学習特徴量の照会経路
- `TerrainEnvironmentRuntime.cs`、`TerrainEnvironmentDefinition.cs`、環境編集画面
- `UnitPanelUI.SpecialSkills.cs`、`SpecialSkillPresentation.cs`、`SpecialSkillSelectionUI.cs`、`TurnInputHandler.cs`、`TurnInspectionController.cs`
- `Tests~/SceneSmoke.cs` と既存の特殊能力・自然回復・環境・操作回帰

## 未確定値と互換性

以下を勝手なバランス値で埋めていない。既存値があるものは維持し、新たに必要な未指定値は設定欄／`UNRESOLVED_BALANCE`として残している。

| 項目 | 現状 |
| --- | --- |
| Archer／Samuraiの新しい能動スキルAP | -1（未設定）。旧0APは常時能力の値なので転用しない |
| 各スキルのクールダウン | 既存設定を維持。未設定は-1で発動を拒否 |
| Knightの低下率・重複、Archerの途中倍率 | 既存の設定・曲線を使用 |
| Assassinの撃破後強化の期間・重複 | 未設定の追加強化は有効化しない |
| Scout無料行動のリセット周期、Guardian攻撃回数のリセット | OwnerTurn／LifetimeをUnityで設定 |
| Crossbow初期弾数と雷線の陣営判定 | 初期0〜3発と陣営マスクの明示が必要 |
| Sniper距離反動 | 既存曲線を維持。通常20%自傷とは別に解決 |
| Bomber自動爆発範囲 | 新設定がなければ既存の爆発半径を再利用。爆弾の陣営・死亡時方針も既存設定 |
| Holy Knight範囲と中断、Samuraiゲージ、Ninja／Berserker／Navigatorの曲線 | 既存設定を使用。未設定は対応スキルを拒否 |
| 環境の未確定値 | 環境R1の記録を参照 |

本番の既存10テンプレートについて、未確定状態のまま無断で全能力を有効化していない。新6職種は開発スタジオで定義・作成・設定できる。表示検証専用のCD0や曲線は本番データに採用していない。

仕様書にあるVoxelMap3D／DDA／Reachable3D、SkillStateStore、AttemptAction、UnitAbilities.csvはこのチェックアウトには存在しない。既存の地表高・地形LoS、Status.Special、TryExecute、SkillDataへ統合した。存在しないV25用の並行システムは追加していない。

## 検証と証拠

Unity 6000.3.8f1を使い、ユーザーの作業用Unityとは別の `work/UnityValidation` で実行した。ユーザーのUnityを停止・再生状態変更していない。

- `SceneSmoke.Run`：全体回帰成功。固有能力・操作・AI・自然回復・環境の関連チェックは計611項目（既存の関連回帰を含む）。
- 内訳：Runtime82、Actions64、CrossbowR1 47、AbilityR1 71、Targeting25、UI49、SpecialAI97、R1AI52、NaturalRecovery33、Environment91。ほかの既存SceneSmoke回帰も成功。
- Unity Test Framework：7テスト成功。うち1つは実シーンをPlayModeに入れて上記のランタイム回帰を再利用する統合テストなので、611項目に独立した7件を単純加算しない。
- 主な回帰：致死肩代わり、無効化、実損失、回復5ターン目終了、複数反動、確定的な弾倉変換、保存復元、強制移動侵入、AIの透視防止・AP0、範囲外クリック、即時一括発動、対象なし・AP不足、狂戦士の段階、司祭死亡後のUI解除。
- 表示検証：実シーン・カメラ・HUDから6画面を取得。仮想キーボードの2入力とマップの実Colliderレイ選択も通している。手動で複数試合遊んだ記録や、バランスが適切と保証する証拠ではない。
- Windows64非Developmentビルド：成功。BuildReportはエラー0・警告0、ビルド13.4秒。最終ログは下の証拠ディレクトリに保存した。

ログ・UTF XML・検証情報・ソースハッシュは [PerformanceData/r1-integration-20261017](PerformanceData/r1-integration-20261017/) 、画面は [PlaytestScreenshots/r1-integration-20261017](PlaytestScreenshots/r1-integration-20261017/) に保存。

UnityのバッチEditor終了時にネイティブのJobTempAlloc警告を観測している。BuildReportの警告件数とは区別する。今回の回帰でアプリ側の長時間メモリ漏れを完全否定したわけではなく、警告の発生元も未特定。長時間自動対戦の性能・バランス測定は別途必要。
