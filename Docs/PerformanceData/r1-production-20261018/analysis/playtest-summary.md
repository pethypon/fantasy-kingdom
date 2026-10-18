# 本番R1・実AI対戦3試合の観測結果（2026-10-18 JST）

対象: `work/r1-production-playtest-20261018`。壁候補・破棄Status作戦記録の修正**前**のbaseline。修正後の確認試合と混合しない。

実SampleSceneをタイトルから開始し、開発者用プレイヤーAIと通常敵AIで終局まで進行した。両軍脅威度15、通常資源/AP/時計/timeScale/AI予算を維持。人の手動プレイとは異なる。

| seed | 結果 | round | 所有ターン | 実時間秒 | 行動失敗/記録 | 失敗率 |
|---|---|---:|---:|---:|---:|---:|
| 161803 | プレイヤーAI勝利・敵王撃破 | 236 | 471 | 244.5 | 774/5577 | 13.88% |
| 271828 | プレイヤーAI勝利・敵王撃破 | 244 | 487 | 232.8 | 683/5409 | 12.63% |
| 314159 | プレイヤーAI勝利・敵王撃破 | 193 | 385 | 176.2 | 557/4812 | 11.58% |

3試合すべて終局、計1343所有ターン、653.6秒（約10.9分）。例外による試合中断0、行動記録のID欠落・重複0。2014/15798行動が失敗（12.75%）。理由はすべてExecutionFailedという集約情報で、全件を自軍壁のせいとは断定できない。同じround・陣営・actor・typeの失敗は最多2回で、同一ターンの無限再試行は観測していない。

## スキル・新職種

固有スキルは88試行、80成功、8失敗。成功能力：

- ArcherStationary: 7
- MagicRitual: 1
- ScoutVisionGuard: 30
- CrossbowStakes: 3
- HolyKnightSanctuary: 1
- SamuraiCounter: 10
- NinjaSwap: 6
- BerserkerRage: 22

通常意思決定による新6職種の成功召喚（両軍合計）：

| 種類 | 161803 | 271828 | 314159 | 合計 |
|---|---:|---:|---:|---:|
| WhiteMage | 0 | 0 | 0 | 0 |
| HolyKnight | 12 | 10 | 9 | 31 |
| Samurai | 14 | 14 | 7 | 35 |
| Ninja | 23 | 20 | 17 | 60 |
| Berserker | 25 | 23 | 13 | 61 |
| Navigator | 0 | 0 | 1 | 1 |

WhiteMageは通常意思決定では未使用。別の全新職種・実executor召喚テストとは区別する。未使用だけで実装不具合とは断定しない。3試合ともプレイヤーAIが勝ったが、先後/性格/相手/地形の違いを含む小標本で一般的な勝率や職種格差の判定はできない。

## 経済

| seed | 陣営 | 未払を観測したround | 最大未払駒 | round50以降未払観測 |
|---|---|---|---:|---:|
| 161803 | player | 12–21 | 3 | 0 |
| 161803 | enemy | 18–26, 33–37 | 3 | 0 |
| 271828 | player | 12–16 | 3 | 0 |
| 271828 | enemy | 12–18 | 3 | 0 |
| 314159 | player | 12–15 | 3 | 0 |
| 314159 | enemy | 12–22 | 3 | 0 |

資源0は消費と補充のタイミングを含むため、資源0だけで経済破綻とは扱わない。全資源の最小/中央値/90 percentile/最終値/0を観測したroundはJSON参照。初期維持費未払い区間があり、全試合の最終時点は両軍0体。序盤の不足と後半の余剰は分けて評価する。

## メモリ・実行負荷

| seed | Unity tracked変化MiB | Mono used変化MiB | GameObject変化 | OS private最小–最大MiB |
|---|---:|---:|---:|---:|
| 161803 | +7.50 | +34.73 | +502 | 1139.8–1390.1 |
| 271828 | +4.23 | +29.96 | +504 | 1238.9–1248.3 |
| 314159 | +3.98 | +28.23 | +385 | 1247.8–1255.6 |

外部OS観測は同じUnityプロセスを約15秒ごとに採取。cold開始30秒を除くprivateは1139.8→1248.0MiB、範囲1139.8–1255.6MiB。working setは957.6→1010.2MiB。cold peakはraw CSVに保持。Unity内process APIの全0は未提供として扱い、メモリ0とは解釈しない。

Unity tracked、Mono heap、OS private/working setは別指標。軍数、オブジェクト数、Editorキャッシュ、試合間reloadを含むため、増加だけでリークとは判定しない。逆に、11分弱から長期リーク無しも保証できない。実機playerとより長い対戦で比較する必要がある。

最終ReflectionRecords=128、CompletedOperations=24は全試合で共通。SharedMemory/PositionHistory/Knowledge等はJSONへ時系列集計。1試合目は作戦記録停止警告があり、上限到達だけから健康状態を断定しない。Editor batchmodeフレーム/GC計測はUIと観測器を含み、実機描画FPSの保証には使わない。

## 警告・修正対象

- 各試合2件: WildBoss未割当fallback生成、TimerSystem未設定の自動生成。
- seed161803: 破棄Status参照によるAI作戦記録停止警告1件。試合は継続したが機能低下のため修正対象。
- seed271828: ThirdFactionのUnsafe boss territory expansion rollback警告1件。安全拒否の記録で、ターン停止は起きていない。
- 対戦の観測区間中はError/Exception/Assertion/native warningが0。ただしcaught exceptionがWarningになるため「不具合0」とは言わない。
- Editor終了時JobTempAllocは対戦中managed診断と別に、終了ログと診断ビルドで調査する。

入力CSV/JSON/logは変更していない。集計JSONは全資源・能力・行動種別・メモリ・frame集計と警告原文を保持する。

## 修正後の確認1試合（別条件）

追加証拠: `work/r1-production-playtest-confirmed-20261018`。自軍壁候補、共通SpecialSkillMovementBlocked、破棄StatusのOperationLifetimeを修正した後の確認。新しいクリア後脅威度進行・新6職種の外見導入より前。

map seed=161803、両軍脅威度15。プレイヤーAI seed=15015・Intellect、敵AI seed=561453・Growth。敵AI seedと性格がbaselineとは異なるため、失敗率変化を厳密な同条件A/Bとは扱わない。

| 結果 | round | 所有ターン | 実時間秒 | 行動失敗/記録 | 失敗率 |
|---|---:|---:|---:|---:|---:|
| プレイヤーAI敗北・味方王撃破 | 93 | 186 | 72.6 | 2/1765 | 0.11% |

通常ルールで正常終局、fault無し。行動記録ID欠落/重複は0。固有スキルは4試行、4成功、0失敗。失敗内訳: Move=1, Surround=1。

観測中の警告2、Error 0、Exception 0、Assertion 0、native warning 0。破棄Statusによる作戦停止警告はこの試合では再現しなかった。対象テストによる寿命回帰確認とは分ける。

維持費未払の観測：

| 陣営 | round | 最大未払駒 | round50以降未払観測 |
|---|---|---:|---:|
| player | 12–21 | 3 | 0 |
| enemy | 19–22, 24–44, 48–49, 57–61 | 3 | 6 |

Unity trackedは開始→終局+6.34MiB、Mono used +24.40MiB、GameObject +214。OS privateの観測範囲は1294.5–1307.2MiB（5サンプル）。短い確認試合の軍数/Editor/cache増減を含み、長期リーク有無は判定しない。

実観測の合計は4試合・1529所有ターン・726.2秒（約12.1分）。プレイヤーAIは3勝1敗。ただし2つのsource版・複数の敵seed/性格を含む小標本で、一般勝率や修正による勝率改善とは扱わない。

初回launchの最低seed数guard失敗は実対戦として数えていない。第二launchのconfig/batch/matchに基づく正常終局1試合だけを追加した。確認試合の全資源・技能・メモリ・フレーム・警告は集計JSONのconfirmation節に保持。
