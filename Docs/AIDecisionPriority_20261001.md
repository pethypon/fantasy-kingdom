# AI判断と敵ターンの待ち時間（2026-10-01）

## 優先した症状

敵が移動した後も「敵のターン」が残り、操作が戻らないように見える報告を優先した。最新 Editor.log には敵ターン終了、プレイヤー AP リセット、PlayerMove.Entry の記録がある。継続不能な停止そのものは再現していないため、この画像の原因を確定したとは扱わない。

確認できた待ち時間の問題として、1回の Minimax にターン残り予算を全て渡していた。AISearchEngine の1判断の先読み予算を350msに分離し、完了した深さの結果または既存評価を使って行動へ戻す。ターン全体の予算と1フレームのスライス予算は維持する。350msは全ターンの長さではなく、同期処理を強制中断する保証でもない。

EnemyTurnBannerUI は思考／行動中の経過秒を表示し、結び付いた TurnGenerator が敵ターン以外なら自身でも非表示にする。再表示時の高さも初期化した。表示を消すために勝手にターンを進める処理は追加していない。

## 判断の修正

- 探索と増援が終わった時点で、最新の盤面から戦略・AP配分・役割を再計算。
- 師団所属でも通常攻撃・スキルを通常判断から除外しない。移動後の攻撃を妨げていた半減評価も解除。
- 先行建築・探索・召喚が、実行可能な通常攻撃による撃破1回分のAPを奪わないようにする。対象が盾で守られている場合は撃破できると決めつけない。
- 遠方の記憶だけで先行建築を止めない。近くの視認済み脅威や実行可能な撃破を優先する。
- 共通の優先判定は AITacticalPriorities に集約。隠れた敵の実位置は参照しない。

## 検証

別の Unity 検証コピーで SceneSmoke の604項目が通過。新しいチェックは攻撃APの確保、盾の扱い、増援のAP消費制約、記憶だけでは建築を止めないこと、1判断で全ターン予算を消費しないこと。

測定例: 8秒のターン予算に対して1判断350.92ms、残り7649.08ms。実機の全フレーム時間や長時間プレイの保証ではない。

ソース: Assets/Script/Gamesystem/TurnSystem/Enemy/AISearchEngine.cs、AICommander.cs、AICommander.Recon.cs、AICommander.Selection.cs、AITacticalPriorities.cs、AIBuildPlanner.cs、EnemyStart.cs、Assets/Script/UI/EnemyTurnBannerUI.cs。

TurnCycleRunner: 6サイクル39項目通過。通常終了・タイマー切れ・例外・終了期限・表示の自己修復を確認。検証コピーの起動時に UnityEditor.Search.SearchDatabase 内の例外が1件出たが、ゲームの遷移テストは完了。意図的に注入した MoveNext/Dispose 例外は別途2件。
