# Fantasy Kingdom 長時間AI・隠し日記・見下ろしカメラ 実装報告

2026-10-11 / Unity 6000.3.8f1 / 対象 master

**500 own-turn試験と同一条件での行動比較を完了しました。**

AIの探索深度、候補数上限、思考時間の既存予算、評価点、作戦、自己評価、学習、BeliefMap、PlayerModelは削減していません。候補が空であると確定できる処理、表示、ログ、参照の寿命を整理しました。

計測は固定負荷の開発用fixtureです。Scout4体、AP6、資源0、脅威度15、性格Combat、seed56192を使い、500回のAI自身のターンを実行します。実行行動は16件で、戦闘のない局面のためSearchは0回です。通常の対戦500ラウンドや、すべての戦況でのAI性能を証明する試験ではありません。CPUはAIイテレータの実処理とターン終了時の自己評価を計測し、`yield`による待ち時間を含めません。

## 1. 実装時の最新Commit SHA

開始時にローカルmasterと取得したorigin/masterを確認し、どちらも `d03dc55205830e1336ca4297d43fd36f7795a565` でした。この報告はそのCommitへの作業差分を対象とします。変更をCommit・Pushしていません。

## 2. Profilerで確認した主な重い箇所

16種類の `AI.*` ProfilerMarkerを追加しました。空の攻撃候補生成でもマップ全体を走査しており、最適化前の追加計測ではCandidateGeneration平均3.2384ms、P95 6.6927msでした（[空候補最適化前の計測ログ](<C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/long-session-after2.log>)）。候補を減らさず、対象が存在しない条件で早期終了するようにしました。以下は最終Afterのマーカー計測です。

| Marker | 呼出回数 | 平均ms | P95 ms | 最大ms |
| --- | ---: | ---: | ---: | ---: |
| AI.BoardSnapshot | 4,480 | 0.1818 | 0.2132 | 7.0262 |
| AI.CandidateGeneration | 6,948 | 0.0503 | 0.0813 | 10.9388 |
| AI.ActionEvaluation | 13,903 | 0.0277 | 0.0943 | 17.5242 |
| AI.Search | 0 | 0.0000 | 0.0000 | 0.0000 |
| AI.Selection | 3,474 | 0.0021 | 0.0005 | 3.8411 |
| AI.Execution | 16 | 2.6018 | 23.8538 | 23.8538 |
| AI.Reflection | 1,245 | 0.3107 | 0.0295 | 16.3943 |
| AI.DelayedCredit | 16 | 0.0187 | 0.2522 | 0.2522 |
| AI.ArmyUtilization | 1,033 | 0.0096 | 0.0093 | 1.8368 |
| AI.Operation | 4,490 | 0.1071 | 0.2662 | 9.7371 |
| AI.OperationScoring | 9,970 | 0.0071 | 0.0104 | 3.1971 |
| AI.OperationLearning | 35 | 0.1284 | 0.7870 | 2.6213 |
| AI.Diary | 33 | 0.4899 | 0.8328 | 3.2294 |
| AI.Persistence | 0 | 0.0000 | 0.0000 | 0.0000 |
| AI.Presentation | 7,956 | 0.0004 | 0.0004 | 0.4810 |
| AI.UI | 0 | 0.0000 | 0.0000 | 0.0000 |

平均・最大は全観測、P95は直近256サンプルのリングから取得します。0回は「このfixtureでは未実行」を意味し、処理が無料という意味ではありません。

## 3. 長時間劣化の原因として確認できたもの

旧コードのConsole出力は500 own turnsで 40,253 件でした。文字列を作成してから捨てるパターンもあったため、ログレベル確認を文字列生成の前へ移しました。候補が空でも繰り返す全マップ走査もCPUの主要な無駄でした。

一方、計測環境自体の差も分離しました。EditorのProfiler録画履歴を有効にした試験と、通常Editor起動を混ぜた試験はNativeメモリの条件が異なります。それらは受け入れ比較から外し、Before/Afterとも `-batchmode`、Profiler録画OFFで比較しました。メモリ増加のすべてをAIのリークと断定していません。

## 4. 原因ではなかったもの

このfixtureでは共有視界記憶、移動履歴、直近自己評価、完了作戦の件数に比例した無制限増加は確認しませんでした。既存Operation/Reflectionの上限は有効でした。Minimaxは未実行のため、Minimaxを今回の長時間劣化原因とも、問題なしとも判定していません。

## 5. 変更したファイル

既存52ファイルを変更しました。多くのEnemy/MLファイルはログ文字列生成前のレベル確認のみです。主要な責務は、Commanderの計測・参照解放、Boardの空候補処理、Reflection/Operationの読み取り用件数、Inputの集約、Cameraの復元、Diary/ActionLogの表示管理です。

- [DevelopmentLog.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Common/DevelopmentLog.cs>)
- [AIActionEvaluator.Personality.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIActionEvaluator.Personality.cs>)
- [AIActionEvaluator.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIActionEvaluator.cs>)
- [AIActionExecutor.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIActionExecutor.cs>)
- [AIActionGenerator.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIActionGenerator.cs>)
- [AIBoardState.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIBoardState.cs>)
- [AIBuildEvaluator.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIBuildEvaluator.cs>)
- [AIBuildPlanner.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIBuildPlanner.cs>)
- [AICommander.Hierarchical.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AICommander.Hierarchical.cs>)
- [AICommander.Operations.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AICommander.Operations.cs>)
- [AICommander.Reflection.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AICommander.Reflection.cs>)
- [AICommander.Selection.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AICommander.Selection.cs>)
- [AICommander.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AICommander.cs>)
- [AIDeterministicRandom.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIDeterministicRandom.cs>)
- [AILearning.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AILearning.cs>)
- [AIMinimaxEngine.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIMinimaxEngine.cs>)
- [AIPersonality.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIPersonality.cs>)
- [AIPlanManager.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIPlanManager.cs>)
- [AIRoleAssigner.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIRoleAssigner.cs>)
- [AISearchEngine.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AISearchEngine.cs>)
- [AIStrategicGovernor.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIStrategicGovernor.cs>)
- [AIThreatLevel.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIThreatLevel.cs>)
- [DivisionCommander.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/DivisionCommander.cs>)
- [EnemyStart.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/EnemyStart.cs>)
- [KingCommanderSystem.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/KingCommanderSystem.cs>)
- [CounterStrategyEngine.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/ML/CounterStrategyEngine.cs>)
- [MLIntegration.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/ML/MLIntegration.cs>)
- [MLPersistence.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/ML/MLPersistence.cs>)
- [MLReplayBuffer.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/ML/MLReplayBuffer.cs>)
- [MLTrainer.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/ML/MLTrainer.cs>)
- [PlayerProfiler.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/ML/PlayerProfiler.cs>)
- [RealtimeAdaptation.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/ML/RealtimeAdaptation.cs>)
- [AIOperationLearningProfile.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Operation/AIOperationLearningProfile.cs>)
- [AIOperationManager.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Operation/AIOperationManager.cs>)
- [AIOperationPersistence.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Operation/AIOperationPersistence.cs>)
- [AIOperationScorer.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Operation/AIOperationScorer.cs>)
- [AIActionReflectionSystem.Evaluation.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/AIActionReflectionSystem.Evaluation.cs>)
- [AIActionReflectionSystem.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/AIActionReflectionSystem.cs>)
- [AIArmyUtilizationAnalyzer.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/AIArmyUtilizationAnalyzer.cs>)
- [AIDelayedCreditAssigner.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/AIDelayedCreditAssigner.cs>)
- [AIDiaryWriter.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/AIDiaryWriter.cs>)
- [AIReflectionSaveRepository.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/AIReflectionSaveRepository.cs>)
- [TurnStrategyPlanner.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/TurnStrategyPlanner.cs>)
- [TurnCameraController.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/TurnCameraController.cs>)
- [TurnGenerator.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/TurnGenerator.cs>)
- [TurnInputHandler.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/TurnInputHandler.cs>)
- [GameMenuUI.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/SaveLoad/GameMenuUI.cs>)
- [AIDiaryUI.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/UI/AIDiaryUI.cs>)
- [ActionLogUI.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/UI/ActionLogUI.cs>)
- [FloatingDamageUI.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/UI/FloatingDamageUI.cs>)
- [PlayUsabilityTests.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Tests~/PlayUsabilityTests.cs>)
- [SceneSmoke.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Tests~/SceneSmoke.cs>)

## 6. 新規追加ファイル

- [AIPerformanceConfigEditor.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Editor/AIPerformanceConfigEditor.cs>)
- [PerformanceConfig.asset](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Resources/AI/PerformanceConfig.asset>)
- [AIPerformanceConfig.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/Performance/AIPerformanceConfig.cs>)
- [AIPerformanceCounters.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/Performance/AIPerformanceCounters.cs>)
- [AIPresentationQueue.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/Performance/AIPresentationQueue.cs>)
- [CameraStateSnapshot.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/CameraStateSnapshot.cs>)
- [AICommander.Performance.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AICommander.Performance.cs>)
- [AIRuntimeMaintenance.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/AIRuntimeMaintenance.cs>)
- [AIDiarySnapshot.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/Enemy/Reflection/AIDiarySnapshot.cs>)
- [TopDownCameraSettings.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Gamesystem/TurnSystem/TopDownCameraSettings.cs>)
- [AILongSessionCoreTests.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Script/Tests/AILongSessionCoreTests.cs>)
- [AILongSessionSoak.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Tests~/AILongSessionSoak.cs>)
- [AIRuntimeMaintenanceTests.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Tests~/AIRuntimeMaintenanceTests.cs>)
- [HiddenDiaryTests.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Tests~/HiddenDiaryTests.cs>)
- [TopDownCameraTests.cs](<E:/fantasykingdom/Unity/fantasy kingdom/Tests~/TopDownCameraTests.cs>)

Unityへ入れる新規スクリプト・設定には `.meta` も追加しました。検証専用スクリプトは本番のビルド対象から外れる配置またはEditor/Development条件に限定しました。

本報告と比較PNGに加え、比較CSV・traceも [Before CSV](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/PerformanceData/AI_LongSession_20261011_before.csv>) / [After CSV](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/PerformanceData/AI_LongSession_20261011_after.csv>) / [Before trace](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/PerformanceData/AI_LongSession_20261011_before_trace.txt>) / [After trace](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/PerformanceData/AI_LongSession_20261011_after_trace.txt>) に保存しました。CSVは列名を付けてあり、作業用Unity検証フォルダを片付けた後も比較データを確認できます。

## 7. 削除した不要コード

GameMenuUIが独自に所有していたMenuBack入力監視、Cameraの独立したデバイス監視、毎ターン生成していた死亡履歴削除用Listを削除しました。AI日記を開くためだけのAI初期化・ファイル再読込、閉じるたびのRoot破棄をなくしました。AI機能を削除しての高速化は行っていません。

## 8. AI Core最適化内容

`GetAttackTargets` は、視認中の対象が0で攻撃できる既知クリスタルもない場合だけ空の結果を返します。敵指定スキルも同じ確定条件を使います。敵がいる通常経路、地面指定AoE、味方・自分指定スキルは従来の規則を維持しました。移動候補はオフセットをindexで走査し、変わらない座標・可視条件をループ外へ移しました。移動、占有、AP、地形通行規則、候補順序はオラクル比較で確認しました。

## 9. GC Allocation改善内容

ログ文字列の事前レベル判定、削除キーscratchの再利用、ActionLogのStringBuilder再利用、スナップショットの必要時作成を行いました。通常Releaseでは診断用stage配列・マーカー配列を生成しません。

バイト計測にはMemoryカテゴリの `GC Allocated In Frame` を使いました。`GC.Alloc` サンプルのValueは時間であり、バイト数として利用しません。割当量はメタデータまたはbyte単位counterを読む必要があります。[Unityの割当メタデータ](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Profiling.RawFrameDataView.GetSampleMetadataAsLong.html)

Monoでスレッド専用APIが利用できない場合も0バイトと報告せず、利用不可を区別します。今回のCSVは完了フレームの割当をown-turnへ集計した値で、Editor/UI/fixtureの記録コピーも含みます。AI専用の割当量という主張はしていません。Runtimeの即時計測にはCurrentValue、履歴容量0を使います。[Unity ProfilerRecorder.CurrentValue](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorder.CurrentValue.html)

## 10. Collection管理改善内容

新しい診断データは陣営別のリング（初期1000、設定上限2000ターン）、stage別256サンプルです。表示キューは256件、日記は1ページ100イベント・30000文字が初期値です。Owner/Lifetime/Maximum/Cleanup/Failureを新規Runtimeクラスのコメントに記録しました。所有者をまたぐ共有scratchは追加していません。

## 11. _unitPositionHistory改善

既存のAnti-Oscillationが参照している直近4座標を維持します。自軍own-turn5回ごとに死亡、破棄、陣営変更、種類変更、階層から完全に離脱した駒のキーを削除します。scratchは再利用し、処理後に参照スロットをClearします。戦闘終了・Scene破棄では自軍の一時参照を解放します。200死亡キーの回帰試験を含みます。

## 12. _sharedMemoryについて行った変更

新しいTTL、件数上限、強制Clearは追加しませんでした。基準Commitに存在していた7ターンの観測寿命と信頼度計算を維持しています。今回の変更で古いFog情報を勝手に削除すると索敵判断が変わるため、件数の観測だけを追加しました。このfixtureの共有記憶は最大0件です。

## 13. Reflection改善

自己評価、遅延報酬、軍の活用評価、日記、永続化へ個別計測を追加しました。UI用の値スナップショットと読み取り専用件数APIを用意し、表示時にSave DTO全体の生成やAI初期化を行いません。報酬、経験値、学習更新のタイミングと上限は維持しました。ターン終了時に別途実行される自己評価もCPUサンプルへ含めます。

## 14. Operation改善

作戦、作戦評価、作戦学習、保存の計測を追加しました。既存の知識1000件、行動証拠128件、完了作戦24件、改訂8件、ステップ12件、割当64件、完了ID512件、ファイル8MBの初期上限を維持しました。作戦の選択・スコア・継続条件を性能都合で変更していません。

## 15. Log改善

`Off / Error / Summary / Verbose / Trace` を導入しました。初期値Summary、候補全件ログはTraceです。文字列補間、StringBuilder、候補ログループへ入る前にレベルを確認します。500 own turnsでConsoleログは40,253→16件でした。After fixtureはOff設定です。試験進捗・計測結果、Unity自身のログはこの件数の対象と区別しました。

## 16. File I/O改善

既存のReflection/Operationの一時ファイル・置換・backup保存を再利用し、別形式を作りませんでした。実ファイルRead/Writeの回数を観測できます。日記UIはメモリから開きます。100行動で追加Write0、表示時追加Read0を試験しました。通常の学習保存は既存の周期・戦闘終了・明示保存を維持し、500-turn fixtureはユーザーの学習ファイルを汚さない `persistent:false` です。

## 17. Presentation Queue改善

ActionLogUIがScene所有の表示キューを持ち、ゲーム状態やAI Action自体は入れません。初期上限256件、128件以上でCatch-Up、64件以下で通常表示へ戻ります。通常8件・Catch-Up32件を1フレームに排出します。低優先の表示だけを間引き・同一actor移動を統合でき、Damage/Kill等の重要イベントは上限到達時にも最古を即配送して保持上限を守ります。Scene終了でClearします。表示の待ち時間・Popup寿命だけが短くなり、AIの実行・報酬・学習を省略しません。

## 18. AI Diary Shift+F7実装内容

既存TurnInputHandlerへ集約しました。左右Shift+F7で開閉し、F7単体・Shift単体は無反応、長押しは1回だけです。初期状態は非表示で、通常の操作説明には隠しキーを追加しません。Rootを1個だけ再利用し、メモリにある日記のページを遅延表示します。Revisionが変わった場合だけ初期最大5Hzで更新します。表示例外は日記UIだけを閉じ、AIの自己評価を継続します。

## 19. Diary Hidden時の負荷

Root inactive、Raycast/Interactable OFFです。100行動の間、日記Text再構築0、Layout再構築0、ActionごとのFile Write0でした。構造化した記録・学習は継続しました。500-turn fixtureの `AI.UI` は0回です。毎フレーム呼ばれるTickの表示状態確認そのものは残るため、厳密にCPUが0という意味ではありません。

## 20. Ctrl+Space TopDown実装内容

左右Ctrl+Spaceで切り替え、既存Space行動を同じフレームだけ消費して二重発火を防ぎます。Space単体の既存Actionは維持しました。MapCreateのXZタイル範囲・高さ・画面aspect・余白1.10から全体表示を計算し、固定座標のCameraを追加しません。平行投影が初期値で、WASD/中ボタンドラッグによるPan、ホイールZoomを継続できます。

## 21. Camera完全復元テスト

入り口で位置・回転・投影方式・FOV・OrthographicSize・near/far clip・Camera/Controller enabled・ドラッグ・直前ポインタを保存します。退出で同じsnapshotを復元し、TopDown状態で上書きしません。100回の往復は位置差≤0.001、回転差≤0.01°で合格しました。投影/FOV/size/clip/internal stateも一致しました。1×1～256×128/128×256の5サイズ×4aspect、Scene変更、Camera交換、参照欠如のWarning1回を確認しました。

## 22. 500ターンSoak Test結果

Before/Afterとも500 own turns・16行動を完走しました。50ターンをWarmupから除外し、After終盤/序盤CPU比は**1.0945**、基準1.20に対して合格です。行動traceは一致です。非同期yieldの待ち時間をCPU改善へ含めていません。

[Beforeログ](<C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/long-session-before-controlled2.log>) / [Afterログ](<C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/long-session-after-controlled.log>) / [Before CSV](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/PerformanceData/AI_LongSession_20261011_before.csv>) / [After CSV](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/PerformanceData/AI_LongSession_20261011_after.csv>)

1000 own-turn起動口も用意しましたが、今回の受け入れ実行は500 own turnsです。

## 23. Turn51～100平均

Before **61.9272ms**、After **6.4340ms**。同じfixtureで**89.6%削減**しました。

## 24. Turn451～500平均

Before **61.0447ms**、After **7.0421ms**。同じfixtureで**88.5%削減**しました。Afterの序盤に対して終盤は+9.45%です。

## 25. GC Alloc Before / After

| own-turn区間 | Before bytes/own-turn | After bytes/own-turn | 削減率 |
| --- | ---: | ---: | ---: |
| 51～100 | 1,021,813 | 723,413 | 29.2% |
| 451～500 | 1,158,632 | 860,045 | 25.8% |

これは完了フレーム単位のバイトcounterをown-turnへ集計した値です。fixtureの記録生成・UI・Editorを含むため、AI専用Allocとして利用しません。stage内counterを読めない環境では利用不可（-1）とします。

## 26. Memory Before / After

| 値 | own-turn区間 | Before MiB | After MiB |
| --- | --- | ---: | ---: |
| Managed used | 51～100 | 23.21 | 23.67 |
| Managed used | 451～500 | 24.17 | 26.52 |
| Native推定 | 51～100 | 166.13 | 158.08 |
| Native推定 | 451～500 | 210.06 | 155.24 |

Managedは `Profiler.GetMonoUsedSizeLong()`、Native推定は `GetTotalAllocatedMemoryLong() - Managed` です。Memory ProfilerのNative項目全体と同義ではありません。Root、日記、stageリングを追加したため、AfterのManagedがBeforeより少ないという保証はありません。AfterのNativeは初期化後に一定範囲へ収まりました。Managedの区間平均は約2.85MiB増え、GCの波も残っています。Collection上限違反はありませんでしたが、Managedの長期plateauをこの500回だけで断定しません。何年もリークしない保証とはしません。

![500 own-turnのCPU・フレームGC・メモリ比較](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/AI_LongSession_20261011.png>)

薄い線は生データ、太い線は50ターン移動平均、背景の灰色はWarmupです。CPU/GCの縦軸は移動平均の比較が読める範囲に設定しました。上端を超える生データのスパイクは三角で示し、個数を図内に記載しています。

## 27. 最大Collection件数

| Collection | Before実測最大 | After実測最大 | 維持する上限 |
| --- | ---: | ---: | --- |
| 移動履歴のユニットキー | 4 | 4 | 生存中の自軍ユニット数 |
| 移動履歴の総件数 | 16 | 16 | 各ユニット4件 |
| 共有視界記憶 | 0 | 0 | 新しい数値上限・TTLを追加しない |
| Reflection直近記録 | 16 | 16 | 既存128件（この試験の設定） |
| 完了作戦 | 24 | 24 | 既存24件（初期設定） |

表示キューはCore試験で上限到達・重要イベントのみの連続投入も確認しました。診断リングは設定数を超えるサンプルで上書きを確認しました。Reflection/Operation知識・遅延報酬には既存の設定上限を維持しています。このfixtureで実際に上限まで埋めたという意味ではありません。

## 28. Event Leak Test

CameraのSceneManager購読を10回enable/disableした後も1件であること、日記を10回開閉してRootが1個であることを実シーンで確認しました。TurnGeneratorは所有するInput eventをOnDestroyで解除し、Scene所有のカメラはOnDisableでScene eventを解除します。これらは10回の完全な新規対戦を実施した試験とは区別します。

## 29. Determinism Test

同一Map/seed56192/脅威度15/Combat性格/プレイヤー行動なし/同じ計算予算で比較しました。fixtureは同じ性格trait、決まったLife IDを使い、Environment.TickCount由来の初期traitとGUIDの並び順が比較を乱さないようにします。これらは試験条件の固定で、Production AIの能力や乱数規則を変更する処置ではありません。

## 30. AI Action Trace差分

Turn、ActionType、Actor、Target、TargetCategory、TargetPosition、実行成否の署名を比較しました。16件すべて同一、追加・削除・順序差分0でした。 Life IDはfixtureの安定したactor名へ正規化します。After trace SHA-256: `35b0c253a30fb36ec152540535678fe3580309a64b24a33f5881b72e54dcc328`。

[Before trace](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/PerformanceData/AI_LongSession_20261011_before_trace.txt>) / [After trace](<E:/fantasykingdom/Unity/fantasy kingdom/Docs/PerformanceData/AI_LongSession_20261011_after_trace.txt>)

## 31. Compile結果

Editorの最終検証でCompile Error0、SceneSmoke ALL PASSEDでした。通常Windowsビルド（Development Build OFF）成功、errors=0 / warnings=0、12.6秒。 Release出力は [FantasyKingdom.exe](<C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/ReleaseValidationBuild/FantasyKingdom.exe>)、ログは [long-session-release.log](<C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/long-session-release.log>)。検証にはメインのUnity Editorを停止せず、隔離コピーを使いました。

## 32. 実行したTest

| テスト | 確認内容 |
| --- | --- |
| AILongSessionCoreTests | 表示キューの上限・重要イベント保持・catch-up・log gate・診断リング・故障分離 |
| AIRuntimeMaintenanceTests | 生存/死亡/破棄/所属/階層離脱、4履歴、200死亡キー、攻撃・スキル・移動の旧処理との一致 |
| HiddenDiaryTests | 非表示100行動、追加I/O0、読取専用snapshot、ページング、unicode、Root再利用、例外後のAI継続 |
| TopDownCameraTests | 左右キー・押下edge・既存Space・実InputSystem入力・100復元・map/aspect・Scene/Camera変更 |
| SceneSmoke | 既存のTimer、建築、召喚、UI、経済、Reflection、探索、Operation等の回帰 |
| AILongSessionSoak | 実Commanderによる500 own turns、Before/After、collection bounds、trace |
| ReleaseValidation | Productionシーンの通常Windowsビルド |

## 33. 成功したTest

最新の完全回帰ログ [long-session-regression4.log](<C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/long-session-regression4.log>) で、LongSessionCore19、RuntimeMaintenance71、HiddenDiary24、TopDownCamera51が成功しました。既存のOperationCore39、OperationRuntime21、Reflection pure71、Integrated93、Runtime35、実Board/Economy87、探索397（core97/boundary33/integration267）、TimeUp17、Core56/0失敗、経済R2/基礎資源の回帰も通り、SceneSmoke ALL PASSEDでした。Soakの上限監視は500回を通じて違反0です。

## 34. 未解決問題

この耐久fixtureではSearch0、実行16行動のため、500ターンの実戦、ML重学習、最大マップの多部隊戦闘、Presentationの長時間大量排出までの性能保証には使えません。GCはframe-levelでAI専用のバイト内訳ではありません。Managedの区間平均は増加が残るため、長期plateauには追加計測が必要です。1000-turn起動口は未実行です。10回の完全なbattle start/endの統合試験はこの報告のEvent Leak Testには含めません。

試験初期に、通常Editor起動とbatch起動の差、Profiler録画履歴、GUIDと時間依存traitによる比較の不安定性が見つかりました。それらの測定は最終受け入れ値へ混ぜず、同じ条件で再実行しました。これにより問題を隠すためのAI予算・候補数調整は行っていません。

## 35. PERF_TODO

Unityの **Fantasy Kingdom → パフォーマンス → 長時間AIの設定**、または [PerformanceConfig.asset](<E:/fantasykingdom/Unity/fantasy kingdom/Assets/Resources/AI/PerformanceConfig.asset>) で次の値を調整できます。

| 調整項目 | 初期値 | 次に確認する条件 |
| --- | ---: | --- |
| Maintenance own-turn間隔 | 5 | 大量死亡/Scene終了で不要参照が残らない |
| 表示キュー上限/High/Low | 256 / 128 / 64 | 重要イベントを失わず一定件数へ戻る |
| Catch-up演出倍率 | 0.25 | 表示が追いつき、重要情報が読める |
| 日記イベント/文字数/更新 | 100 / 30000 / 5Hz | 最大記録後も表示待ちが短い |
| ログレベル | Summary | 通常時候補全件log0、必要時のみTrace |
| 終盤/序盤CPU比 | 1.20 | 対戦の500～1000 own turnsでも基準内 |
| 診断turnリング | 1000 | 項目数を増やしても保持範囲が固定 |

実戦の固定trace、最大編成、日記開/閉、演出大量投入、ML有効状態を組み合わせた性能基準を追加測定する余地があります。AI深度、候補上限、学習機能を削ってこの基準へ合わせる運用は行いません。
