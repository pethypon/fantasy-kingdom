# AI探索の分割実行

`AICommander.SearchSliceBudgetMs` の既定値は3ms（実行時1〜5msに制限）。`AISlicedWork` が再帰MinimaxのIEnumeratorをスタックで処理し、時間到達後にUnityへ戻す。盤面複製など個々の処理を途中停止することはできないため、3msは目標であって厳密な上限ではない。

`TurnThinkingBudgetMs` は8秒を維持。フレーム待ちも含む実時間の上限を設け、分割によって数十秒へ引き延ばされることを防ぐ。完成した探索深さだけ採用し、未完成なら直前の完了深さへ戻す。探索前の候補生成等はこの3ms枠の対象外。

同期APIは検証ツール向けに残す。ゲームのEnemyMove→ExecuteTurnStepsは分割APIを利用。ゲーム終了・ターン中断で入れ子の探索をDisposeし、全作業盤面と初期スナップショットをfinallyで返却する。

軽量化: エンジン・各深さの候補バッファ再利用、反復深化配列再利用、安定マージソートによるO(n log n)化、召喚位置の重複計算削減、盤面/ユニットプールの保持数上限と共有地形参照の解放。

`Tests~/AISlicingTests.cs` は旧同期探索との評価一致、中断20回の返却、未開始中断後の再使用、予算0、分割回数を検証。旧実装は `LegacyAIMinimaxEngine*.cs` に検証用として保存し、製品に含めない。

`Tests~/AIFrameRunner.cs` は実際のUnity PlayフレームごとにCommanderを進め、UI Sliderを更新するヘッドレス検証。1000ms予算の記録: 210フレーム、実時間1012.58ms、AI実処理770.96ms、Commander一回最大35.06ms、探索スライス最大8.84ms。Editor/JIT/GC込みの単一条件であり、全盤面や製品FPSの保証ではない。この実行ではUnityEditor.Searchの起動時索引エラーが別途記録されたが、ゲーム側の検証は完了した。
