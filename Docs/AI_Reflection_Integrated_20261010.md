# AI経験学習・自己評価・AI日記：統合仕様の実装報告

更新日：2026-10-10。対象は添付された「AI経験学習・自己評価・AI日記 修正・改善 統合実装仕様書」。探索R2の完了後に実装した。

既存のCommander、Minimax、Belief、PlayerModel、StrategicPriorityを保ち、行動後の実際の成果から学習する層を更新した。健康な経済を維持するだけの加点、同じ敵の再発見、準備と無関係な撃破からの加点を防ぐ。学習補正は同じ戦略優先度内の点数へ適用し、合法な候補を削除しない。

設定はUnityの **Fantasy Kingdom → AI → 自己評価・日記の設定**。`Assets/Resources/AI/ReflectionConfig.asset` に全89項目を日本語で表示する。既存の設定値を保持し、新規項目だけ標準値を追加した。通常のゲーム設定・資源・APの編集画面は既存の開発スタジオを引き続き使える。

## 仕様書の32項目に対する報告

スクリプトの共通ディレクトリは `Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/`。

| 番号 | 項目 | 実装・確認内容 |
|---|---|---|
| 1 | 変更したファイル | `AIActionReflectionSystem.cs`、`AIActionLearningProfile.cs`、`AIActionRecord.cs`、`AIActionFeatureExtractor.cs`、`AIReflectionConfig.cs`、`AIDiaryWriter.cs`、`AIReflectionSaveRepository.cs`、`AICommander.Reflection.cs`、日本語Inspector、ReflectionConfig.asset、既存のCore/実行テスト。探索R2とは別の責務に分けた。 |
| 2 | 新規追加ファイル | `AIActionReflectionSystem.Evaluation.cs`、`AIRewardBreakdown.cs`、`AIEconomyRewardTracker.cs`、`AIDelayedCreditAssigner.cs`、`AIArmyUtilizationAnalyzer.cs`、`AIReflectionDecisionTrace.cs`、`AIReflectionMigration.cs`、`AIReflectionIntegratedTests.cs`。各metadataを追加。 |
| 3 | Economy Reward修正 | Healthy維持は0。Warning/Crisis/CollapseからHealthyへの回復episodeに標準+1を一度だけ付与。再回復の加点には2自軍ターンの再悪化が必要。盤面がないBeginTurnで架空のHealthyを観測しない。 |
| 4 | Minimum Samples | 標準2件未満では選択へ使わない。2件以上は件数/10の信頼度を適用。影響倍率と類似度を適用した後にも最終補正を上下限で制限する。 |
| 5 | Reward Breakdown | 戦闘・Artifact・生存・位置・局所戦力・目標・経済・情報・防衛・効率・準備・反復/失敗/軍利用減点・後続成果を分離。カテゴリ上限と行動合計の標準−3～+3を適用し、Rewardと実内訳の合計を一致させる。 |
| 6 | Combat Reward | 通常撃破は標準1、陣形撃破は合計2、Artifactは1。実イベントのLifeIdで重複を防止。陣形への遅い補正は通常撃破を置換し、+3や日記内の二重撃破を作らない。 |
| 7 | Survival Reward | 実損失・被害・回復と観測した危険の変化を使う。被害が軽い有利交換や、低HP・不利戦力から安全へ逃れた撤退を評価。移動や撤退そのものを自動失敗扱いにしない。 |
| 8 | Position Reward | 高所へ移るだけで加点しない。実際の攻撃可能性・危険低下・射程上の利点が必要。射程管理と悪化も評価する。 |
| 9 | LocalPower Reward | 観測した敵と自軍の戦力比の改善・悪化を評価する。軍利用判定でも真の自軍戦力/観測敵戦力を使い、−1～1の優勢指標を比率として流用しない。 |
| 10 | Objective Reward | 有効な既知目標への距離が実際に縮み、安全条件も満たす行動を評価。緊急防衛時には遠方目標への接近を褒めない。 |
| 11 | Economy Reward | 因果を確認できた成功Build/Upgradeのみ、その行動の経験へ回復分を還元。自然回復はターン評価へ付与。決着したターンも未処理回復を一度だけ確定する。 |
| 12 | Information Reward | Scoutが新しく開いた地形・新しく発見した敵を評価。戦闘中の観測済みLifeIdを保持し、見失った敵の再発見では繰り返し稼げない。不確実性低下だけで無条件加点しない。 |
| 13 | Defense Reward | 自軍クリスタルの危険解消・安全な接近など、実際の防衛成果を評価。王の生存と緊急防衛の優先順位は維持する。 |
| 14 | Preparation Reward | 状態効果数だけでなく種類・残期間のsignature、Shield、実AP回復、味方の回復・範囲支援の結果を使う。同数のバフ更新や加速も認識する。自傷・味方への被害を敵への成果にしない。 |
| 15 | Delayed Credit | 直近5行動、標準減衰0.6、行動ごと最大1。無関係な行動も窓を消費する。発見した敵・支援した味方・意味のある位置取りのLifeId因果が必要。同じ事件は一度だけ。学習のSamplesを増やさず実報酬と評価値を補正する。空き予算に収まらない付与分は返し、保存履歴と実額を揃える。 |
| 16 | IdleArmy | 自軍ターン終了時の集計で標準4ターンの停滞を検出。戦闘・探索・支援・目標進展を軍活用として扱う。 |
| 17 | ExcessiveProduction | 標準5ターンの+5体、利用率40%未満、維持費増、経済悪化、目標進展なしなど、複数条件が揃った場合だけ検出。不利戦力の補強を誤罰しない。 |
| 18 | MissedOpportunity | 有効な行動機会・十分な観測戦力・安全性が揃った状態が標準3ターン続いた場合に検出。単にAPを残しただけでは罰しない。 |
| 19 | Turtle判定 | 敵が遠く、経済が健全で、目標もあるのに本拠点に留まり続ける場合を検出。合理的防衛、予備戦力、AP温存、未観測敵は保護する。軍利用の減点はターン合計標準最大1。 |
| 20 | FailureReason追加 | 既存enumの値を維持して末尾へIdleArmy、ExcessiveProduction、MissedOpportunity、BadTrade、BadRangeManagementなどを追加。 |
| 21 | SuccessReason追加 | 撃破・有利交換・撤退・高所/射程管理・探索・回復・防衛・準備・軍活用を記録。遅い陣形補正もProfileと日記の成功理由集計へ反映。 |
| 22 | AI日記変更 | 選択時の観測、目的、行動に対応する意図と期待、実結果、実学習値の変化を表示。存在しない内部思考を生成しない。別期間の補正は実カテゴリ差分を記す。 |
| 23 | 日記重複防止 | 自軍15/30ターンごと。境界ターンのFINALは同じファイルを置換し件数を増やさない。17ターン決着の最後の期間は16～17。保存再開でも出力状態を保持。 |
| 24 | FoW安全性 | 現在見えない敵のHP・座標・戦力・効果を読んで成果や候補評価へ使わない。範囲支援の対象にも同じ制限。実Commander/実盤面でEnemyと開発用Playerの両側を検証。 |
| 25 | Persistence変更 | クロス戦闘Profileは一般化した文脈/行動キーと経験だけ。座標・現在HP・LifeId・Unity参照は戦闘継続側に限定。ディスク保存は日記/戦闘終了境界に限定。開発用Player AIは永続学習へ書き込まない。 |
| 26 | Schema Migration | Profile/戦闘DTOをv2化。v1のキー・値・Samplesを保持し、phaseのない旧キーも選択へ参照可能。不正entryだけ除外して有効経験を救出。v2→backup→v1→backupの順で復旧。補正時もSequenceを進め、ゲームsave内の新しい経験を復元する。 |
| 27 | Performance対策 | 候補の特徴量は盤面generationでキャッシュ。学習検索と通常の期間集計はDictionary。軍解析は自軍ターン末だけ。後続成果は直近の固定窓だけを確認し、撃破通知も当該行動の事件だけを処理。記録/事件/範囲効果/経験の上限を保持。 |
| 28 | 実行したTest | T01～T42統合検証、旧Core/Reflection/Balance、実Commander/FoW/スキル/セーブ/勝敗UI、探索R2、経済・開発スタジオ・建築/召喚UI回帰。3条件の実AI対戦と各60ターンの経済耐久を実行。 |
| 29 | 成功したTest | T01～T42を含む最終93件、既存Reflection71件、実Commander/経済87件、実イベント/保存/UI/スキル35件、探索R2計397件。旧Coreとルール30件、他の操作/経済/authoring回帰も成功。3対戦・3経済耐久が成功。 |
| 30 | 未解決事項 | テストは、あらゆる地形・ユーザー作成駒・全戦況の最善手を保証するものではない。実対戦の不足継続は最大16/22/27ターンで、戦略・経済の数値調整余地がある。Unity Editorの検索Index起動例外3件と、終了時の既存JobTempAlloc診断を記録した。ゲームコードの例外は検出していない。 |
| 31 | BALANCE_TODO | 加点/減点、カテゴリと合計上限、信頼度、後続成果窓/減衰、軍の閾値、回復再開条件、phase閾値はConfigへ集約した暫定調整値。候補の詳細ログは標準OFF。通常ルール・AP・生産量は既存の設定画面で変更できる。 |
| 32 | コンパイル結果 | 通常Windows64ビルド成功、コンパイル/ビルドのエラー0・警告0。Developmentを無効にしたBuildOptions.Noneで12.4秒。ビルド終了後のEditor内部診断は項目30と区別する。 |

## 検証記録

検証専用の `UnityValidation` で実施し、ユーザーのUnityの再生・シーン・作業中データには操作を加えていない。

- `reflection-v2-regression4.log`：T01～T42を含む89件、既存Reflection71件、実Commander/経済87件、実イベント/保存/UI/スキル35件、探索R2計397件。`[SceneSmoke] ALL PASSED`。
- `reflection-v2-final-core.log`：報酬予算を使い切った場合の後続評価、予算回復後の別成果、保存額と実額、サンプル数の整合を4件追加した最終93件が成功。旧Core/Reflection/Balanceと探索Core/Boundaryも成功。
- 性能測定：最終検証で経験2,000件に対する補正検索100,000回が16.43ms。この検証環境での単体検索の値であり、ゲーム全体のFPSの保証ではない。
- `reflection-v2-longrun.log`：実AI対戦3ケースと、各60ターンの経済耐久3ケースを完了。実際の決着・対戦到達ターンと経済耐久を混同せず、以下に分けて記す。

| Seed / 性格 | 実対戦ラウンド | 対戦中の最大不足継続 | 別枠の経済耐久 | 耐久最低木/パン・最大不足 |
|---|---:|---:|---:|---|
| 1701 / Growth | 17で決着 | 16 | 60ターン | 木76 / パン75 / 1ターン |
| 2718 / Intellect | 42で決着 | 22 | 60ターン | 木76 / パン75 / 1ターン |
| 3141 / Combat | 50到達 | 27 | 60ターン | 木76 / パン75 / 1ターン |

- `reflection-v2-release-build.log`：`[ReleaseValidation] result=Succeeded errors=0 warnings=0 seconds=12.4`。実行ファイルは検証workspaceの `work/ReleaseValidationBuild/FantasyKingdom.exe`。ビルド対象は実プロジェクトのSampleScene、通常Windows64。
- Editor内部診断：検索Index例外はUnityEditor.Searchの起動スタックだけを識別し、ゲームスクリプトを含む例外を許容していない。終了時JobTempAlloc診断は前の探索R2ビルドにも存在する。今回のReflectionコードにも既存ゲームスクリプトにもNativeArray/TempJobの直接利用はなく、このログだけではゲーム中のメモリリークの原因は特定できない。
- `git diff --check`（既存CRLFを考慮）が成功。Unityスクリプトのmetadataあり。既存のルール値・ゲーム内容・ユーザーの編集を保持。

上記ログの保存ディレクトリ：`C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/`。

関連する探索R2の報告は `Docs/AI_Exploration_R2_20261009.md`。以前の自己評価R1の報告は `Docs/AI_Reflection_Balance_20261008.md` であり、Healthy維持報酬と1sample時の補正など、今回変更した事項は本報告を優先する。
