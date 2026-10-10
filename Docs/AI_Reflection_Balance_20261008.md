# AI自己評価・日記・AP・維持費・生産調整の実装報告

対象：2026-10-07の自己評価仕様書と、勝敗画面・TimerSystem参照エラーの修正。日付：2026-10-08。

追記（2026-10-10）：自己評価は統合仕様に更新済み。Healthy維持の加点を廃止し、回復episode、最低サンプル数・信頼度、報酬内訳、後続成果、軍利用、日記重複防止とv2保存を導入した。今回変更した事項は `AI_Reflection_Integrated_20261010.md` を優先する。

## 1. 新規ファイル

以下はプロジェクトルート `E:/fantasykingdom/Unity/fantasy kingdom` からの相対パス。Unity内の新規スクリプトにはmetadataも追加した。

| ファイル | 役割 |
|---|---|
| Assets/Script/Gamesystem/MatchObjectiveRules.cs | 決着対象・王/クリスタルの理由・時間切れの理由と正確なHP率比較 |
| Assets/Script/Gamesystem/TurnSystem/Enemy/AICommander.Reflection.cs | Commanderへの記録・選択補正・試合結果・セーブの接続と例外隔離 |
| Assets/Script/Gamesystem/TurnSystem/Enemy/ReflectionEvents.cs | 実ダメージ・実撃破・実アーティファクト獲得の通知 |
| Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/AIReflectionConfig.cs | 有効化・学習率・上限・日記間隔・減点の集中設定 |
| 同 Reflection/AIActionRecord.cs | 値のみの行動記録・Before/After・戦闘継続DTO |
| 同 Reflection/AIActionFeatureExtractor.cs | 自軍・観測済み情報の抽出と世代単位のキャッシュ |
| 同 Reflection/AIActionLearningProfile.cs | 文脈ごとの学習・反復検出・失敗分析・報酬計算・戦略結果統計 |
| 同 Reflection/AIActionReflectionSystem.cs | Begin/Complete、実イベント重複防止、ターン・戦闘境界の管理 |
| 同 Reflection/AIReflectionSaveRepository.cs | version付きJSON、temp/backup/破損退避と復旧 |
| 同 Reflection/AIDiaryWriter.cs | 区間と戦闘全体の集計・日記テキスト生成 |
| Assets/Script/UI/AIDiaryUI.cs | 最新日記の日本語閲覧画面、スクロール・保存先コピー |
| Assets/Script/Unit&Battle/BuildingProductionBalance.cs | 生産量の任意調整と整数資源の周期生産 |
| Assets/Editor/AIReflectionConfigEditor.cs | 自己評価設定の日本語Inspector・設定を開くメニュー |
| Assets/Editor/GameDevelopmentStudioWindow.Balance.cs | 開発スタジオ内の生産調整・既存生産値のコピー |
| Assets/Resources/AI/ReflectionConfig.asset | 初期設定。日記15自軍ターン、候補ログOFF |
| Assets/Script/Tests/AIReflectionCoreTests.cs | 記録・報酬・反復・学習・日記・安全保存の検証 |
| Assets/Script/Tests/ReflectionBalanceCoreTests.cs | AP60・維持費・IPの検証 |
| Tests~/ReflectionIntegrationTests.cs | 本物の盤面・選択・実行・実経済と予測の一致 |
| Tests~/RuntimeReflectionEventsTests.cs | セーブ・実ダメージ・勝敗・日記UI・戦略統計の検証 |
| Tests~/TimeUpOutcomeTests.cs | 本物のTimerSystemと結果理由、同率・大きなHPの検証 |
| Tests~/ObjectiveClarityTests.cs | 王/クリスタルを区別する結果画面と配置の検証 |

## 2–3. 修正ファイルと変更内容

| ファイル群 | 変更 |
|---|---|
| AICommander.cs / AICommander.Selection.cs | 実行の前後に自己評価を接続。既存優先順位内だけ学習補正。補正前後の正確なスコアを記録 |
| AICommander.Recon.cs / AICommander.Hierarchical.cs | 旧互換フェーズを再利用した場合も同じ記録境界を通す。通常ゲームの統一ループは維持 |
| AIBoardState.cs | 自軍領土数を既存コレクションから取得 |
| EnemyMove.cs / PlayerMove.cs | 自軍の経済処理後に反省ターンを完了。終局後の次ターン遷移を抑止 |
| Status.cs / BattleSystem.cs | 実ダメージと撃破の通知。死亡までのIDを保存し、プール再利用は別の生存ID |
| DungeonSystem.cs / R1ContentCatalog.cs / SystemInitializer.cs | 実アーティファクト獲得にだけ通知。ダンジョンは場所・獲得ラウンドで重複防止 |
| GameSystems.cs / TurnGenerator.cs | 所有するTurnGeneratorを明示的に渡す |
| GameEndState.cs / MatchObjectiveRules.cs / TimerSystem.cs | 常設条件HUDは追加せず、結果画面で実際の決着理由を表示。GameEndStateはTimerSystemのLastTimeUpResult/TimeUpReasonTextを参照しない。時間切れ判定はInt64の交差積でHP率を比較 |
| GameMenuUI.cs | 日記への入口とEscで最上位画面だけ閉じる処理。6項目が収まるボタン高さ |
| GameConstants.cs / FactionState.cs / APSystem.cs / EconomyInitializer.cs | AP上限60、実効最大APへのclamp、ターン全回復、市民ボーナス計算の統一。個別行動コスト上限50は別定数で保持 |
| SimBoardState.cs | 自軍の実効最大APを反映。未知の相手人口は参照せず、既存の相手AP推定を上限内に収める |
| UnitData.cs / UnitStaticData.cs | 通常駒のパン・鉄維持費、王/魔王/明示免除。既存追加維持費は保持 |
| EconomySystem.cs / StrategicEconomyForecast.cs | 維持費→生産→人口の順序を同期し、周期の位相と未払いを共有 |
| FacilityData.cs / FacilityDefinitionData.cs / GameAuthoringRules.cs | 既存・自作施設とも共通の生産調整と生産周期を使う |
| AIBasicResourceEconomy.cs / StrategicProductionDemand.cs | 生産周期を1ターンあたりの供給・入力評価へ反映 |
| SaveSystem.cs / DataTypes / Collect / Restore / Migration / SaveGameApplier.cs | セーブv4、BattleId・途中学習・重複防止台帳・生存ID・生産位相を保持。AP復元と人口ボーナスの再同期 |
| 開発スタジオのcs / Fields / RulesThird | 自己評価設定への入口、生産周期、免除、標準維持費の日本語説明 |
| Resources/GameContent/Rules.asset | AP上限フィールドのみ50→60。既存の有効化・その他のユーザー設定は保持 |
| CoreLogicTests / Tests~の旧経済・制作・探索・情報成長テスト | 新維持費に合う実軍数と合法APを明示。従来の費用・安全性・探索・供給の検証は削除していない |

Boss.asset、フォント、ユーザーが削除した自作建物、EditorBuildSettingsのユーザー変更は保護した。前回の全資源経済AIの変更も保持している。

## 4. Reflectionの実行フロー

1. Commanderが設定を読み、戦闘のIDと自軍ターンを開始する。
2. 既存Governor・ルール・Minimax・脅威度によって候補を作る。
3. 実行可能候補を戦略優先順位で絞る。その範囲だけ学習補正し、既存のミス率を適用する。
4. 実行直前にBeforeと選択スコアを記録。通常のExecutorで実行する。
5. 実イベントのダメージ・撃破・報酬を受け取り、finallyでAfter・成否・失敗理由を確定する。
6. 既に更新済みの盤面は次のループで再走査せず、候補の情報も盤面世代ごとに再利用する。
7. 経済処理後に自軍ターンを完了。15回ごとに区間日記、終局では即座に最終日記を出す。
8. 終局が行動の途中に発生した場合、最後の行動完了まで終局記録を保留し、同じ実行境界で確定する。

記録部分の例外でAI本体を止めない。記録無効時は従来Executorを直接使う。

## 5. 報酬の計算場所

`AIActionLearningProfile.cs`の`AIActionRewardEvaluator`と`AIActionReflectionSystem`の実イベント台帳・ターン/終局処理。

| 確定した成果 | 報酬 |
|---|---|
| 通常の敵駒撃破 | +1、同一の生存IDは1回 |
| 明示された陣形撃破 | +2、通常+1を置き換える。合計+3にはしない |
| アーティファクトの実獲得 | +1、獲得イベントIDごとに1回 |
| 経済安定 | 自軍1ターンにつき最大+1 |
| 勝利 | 戦闘全体へ+100。最後の行動に加えない |
| 敗北 / 引き分け | 標準0 |

経済を立て直した建築・強化の因果関係が分かる場合だけ、その行動へ安定報酬を帰属する。それ以外はターン報酬。陣形撃破を位置から推測する新ルールは追加していない。明示イベントの接続口は`AIReflectionEvents.RecordDamage(..., formationKill: true)`。

## 6. Selectionへの反映

`AICommander.Selection.cs`。先に王の生存等の`StrategicPriority`で候補を絞り、学習値をその中の比較スコアへ加える。失敗済み候補・AP不足候補を復活させない。学習値は標準−5～+5、影響倍率1。既存の脅威度・探索時間・ミス率を上書きしない。無効化・記録のみモードもテスト済み。

## 7. Fog of War

Snapshotは自軍コレクションと`AlivePlayerUnits`の観測済み情報を使う。対象が観測リストから消えた場合、非公開HP・位置を読まずHP=-1とする。撃破は相手が見えなくなったことから推測しない。実ダメージ/実撃破イベントで完了結果を補完する。Player側の開発AIも同じ陣営視点を使用。

## 8–9. 日記と学習JSONの保存先

- 日記：`Application.persistentDataPath/FantasyKingdom/AI/Diary/AI_Diary_Enemy_<BattleId>_Turn15.txt`など。
- 学習：`Application.persistentDataPath/FantasyKingdom/AI/Learning/EnemyFaction_Default.v1.json`。
- 保存は日記間隔・終局の境界だけ。行動ごとのディスク保存はしない。
- JSONはtempへ書いてflushし、旧ファイルをbackupとして置換。破損ファイルは`.corrupt.<時刻>`へ退避し、backupまたは空のプロフィールで継続。
- 学習パターン2048件、最近の行動128件、戦略統計32件、完了戦闘ID128件を標準上限とする。
- 永続学習のキーに絶対座標やRuntime IDを使用しない。戦闘継続のID・座標はゲームセーブの戦闘DTOに限定。
- F8を使用した開発戦闘は通常プロフィールへ保存しない。プレイヤーAIはメモリだけの別スコープ。

閲覧：ゲームメニュー／勝敗画面の「AI日記を見る」。全日記ファイルを毎フレーム列挙せず、最新のメモリ記録を表示する。

## 10–11. AP60とIP

AP60はGameConstants、共通設定、実効最大値・Set/Restoreのclamp、ターン全回復、市民ボーナス、実シミュレーション、保存復元、Rules.asset、テストで反映。表示は既存APPanelUIの最大値参照で追従する。通常の基礎30＋市民1人あたり1、ペナルティを引いた実効最大まで毎ターン戻る。

IPは上限50・毎ターン+5のまま。個別行動コスト上限50もAPプールと分離して維持。

## 12–13. Lv1維持費と予測

`UnitData.GetUpkeep`が共通定義。通常駒はパン1・鉄1、Lv10～19で2、Lv20～29で3。King、Boss、`upkeepExempt`が明示された駒は無料。全召喚駒を無料にはしていない。

実経済と予測はこの同じGetUpkeepと施設recipeを使用し、ユニット維持費→建物維持費→クリスタル収入→建物生産→人口の順で処理する。当ターンの生産で支払い失敗を後から帳消しにしない。維持費未払いの既存ペナルティを保持。

## 14. Productionの定義

既存施設はFacilityData、自作施設はFacilityDefinitionDataのrecipe。共通の`BuildingProductionBalance.Resolve`で任意の生産量/確率生産量/周期の上書きを行う。入力・維持費・クリスタル収入を生産量調整で変更しない。

`ProductionIntervalTurns=1`は毎ターン、2は隔ターン。0の旧データは1として扱う。位相は保存する`NationState.TurnsAlive`を使う。整数1を2ターンごとに生産する設定は、AI評価では毎ターン0.5として扱うため切り捨てない。

Unityメニュー：Fantasy Kingdom → 開発スタジオ（日本語）→ 陣営・ゲームルール → 建物の生産量・生産周期。駒づくりの維持費欄に免除と標準料金を表示。自己評価設定は同スタジオ上部「AI自己評価・日記」またはFantasy Kingdom → AI → 自己評価・日記の設定。

## 15. BALANCE_TODO

- 建物の具体的な減産量は仕様書で未確定。全施設20%減等の値は導入せず、既存の数値を保持。共通設定から数値と周期を調整できる。
- 進展のない反復0.25、往復0.5、実行失敗0.25の減点は調整対象としてConfigへ集中。敗北報酬は0。
- 陣形撃破は明示された実イベントだけ対応。陣形の新たな判定ルールは未定義なので自動認定しない。

## 16–19. 検証結果

検証完了。Unity 6000.3.8f1、編集用プロジェクトを停止せず、独立したUnityValidationへ最新のソースを同期して実行した。

- `reflection-regression9.log`：SceneSmokeの既存テスト全体がALL PASSED。Core56、ReflectionBalance30、Reflection70、ReflectionIntegration87、RuntimeReflection29、TimeUpOutcome17の各チェックを含む。R2経済と全資源経済のテストも全件合格。
- `reflection-long-run1.log`：3seed（1701/2718/3141）の実ゲームが正常進行。Growthは24ラウンドで正常終局、Intellect/Combatは50ラウンドまで到達。各条件の実経済60ターン耐久も合格（最小木材76/パン75、最大連続供給不足1ターン）。
- 実ゲームでは不足資源の回復が最大25ターン停滞する局面も記録された。進行/例外と制御された供給耐久の合格は、あらゆる実戦の経済判断が最適であることを意味しない。この戦略上の課題は次の探索R2と合わせて継続監査する。
- `reflection-release-build.log`：通常Windows64ビルドSucceeded、コンパイルエラー0、ビルド警告0、8.3秒。
- 脅威度100の既存性能チェック：Editorで1ターン2729.204ms、設定予算3000ms＋許容750ms以内。開発ログ込みの値でありGPU/実リリースの性能測定ではない。
- 回帰テストの故意のJSON破損/保存不可は期待した警告3件。長時間テストでは既知のUnity Editor検索インデックス起動例外3件を記録し、該当する起動時スタックだけを除外。ゲーム由来の例外は除外していない。
- `git diff --check`で空白エラーなし。Windowsの改行変換通知はコンパイル警告ではない。

ログの場所：`C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/`。検証ビルド：同work内`ReleaseValidationBuild/FantasyKingdom.exe`。

追加したチェックは仕様119～141に対応：実/陣形撃破・重複防止・記録の無効化・実Selection・優先順位・両陣営Fog・反復・有意味な行動・日記15/30/早期終局・破損復旧・AP60/全回復/IP据置・Lv1維持費/免除・実経済と予測・周期と整数出力。さらに実ダメージ→勝敗→日記UI、セーブv4、戦略結果、TimerSystemと理由表示を検証。

旧テストの維持費不足は、無料の王や無視される旧rawパン費ではなく実際の通常駒数で作るよう修正。APのテスト準備は実効最大値以内にし、期待する費用や未消費の判定は保持した。

## 20. 未解決・検証の範囲

生産減少の具体的なバランス値は上記TODO。全プラットフォーム・10年間のあらゆる不具合がないことを保証するものではない。今回の自動回帰、通常Windowsビルド、実ゲームの長期進行を検証対象とする。通常の選択・攻撃・建築・召喚ルールと第三陣営のIPは維持した。
