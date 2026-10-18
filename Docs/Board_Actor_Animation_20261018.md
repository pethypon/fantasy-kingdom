# 駒・強敵の軽量アニメーション

更新後の実Unity検証は46チェック成功。攻撃系の固有スキルと反撃は実ダメージ処理から攻撃通知へ接続する。回復・補助・供儀・遅延地形ダメージの汎用通知では駒を動かさない。最新の統合結果は `Docs/Board_Art_Refinement_Event_Animation_20261018.md` を参照。

2026年10月18日更新。ユーザー指定に合わせ、表示の動作を **移動・通常攻撃・被弾の3種類だけ** に絞った。待機中の呼吸、揺れ、回転、Ghost Kingの浮遊、汎用スキルの伸縮、撃破時の独立した退場動作は行わない。骨格を持たないOBJ/チェス駒に対する、模型全体の短い踏み込み、傾き、被弾反応である。

## 統合

- `BoardActorPresentation.Apply` が、ゲーム処理とは別の表示専用子 `ActorVisual` を作り、材質・モデル・向きを設定して `BoardActorAnimation.Bind` で関連付ける。
- `SpecialSkillRuntime.OnMoved` などの実移動経路が移動通知を、`BattleSystem`、建物砲撃、強敵通常攻撃・反撃が通常攻撃通知を使う。表示だけを0.18秒/0.24秒で補間する。移動取消も表示を元の位置へ追従させる。
- `Status` の実HP減少で0.17秒の被弾反応を通知する。撃破は動作を中止し、既存のゲーム処理が即座に駒を取り除く。独立した死亡メッシュ・材質・オブジェクトを作らない。
- `NotifySkill` は既存呼出元との互換入口を維持するが、表示の追加動作は起こさない。スキルのゲーム効果や、実移動・実被弾の通知は変更していない。
- Q/E、敵AI、第三陣営AI、召喚、初期配置後の向き、セーブ復元が `NotifyFacing` を使い、停止中でも表示の向きを即時更新する。方向変更だけでアニメーション更新を起動しない。
- セーブ復元全体の `SuppressEffects` スコープは引き続き有効。復元値を被弾・移動として再生せず、入れ子・例外・繰り返しDisposeに対応する。表示モデルの関連付け、向き、Fog表示更新は抑止しない。

## 待機の停止と視界

`Bind` と `SetVisible(true)` は表示状態を登録するだけで、フレームごとの更新対象には追加しない。許可された動作が始まるとその駒を更新リストへ入れ、終了時に保存した位置・回転・スケールへ正確に戻して取り除く。更新中の駒が0体になると共有 `BoardActorAnimationDriver.enabled=false` とし、`LateUpdate` 自体を停止する。待機中のSin計算や駒ごとのUpdate、Animator、Coroutine、材質インスタンスはない。

Fogによって隠れた時点で動作を中止する。未確認の敵へ通知しても動作を登録しない。見えない区間や長距離の移動は軌跡を描かず、短い到着動作だけにする。見えない攻撃対象の位置を姿勢で漏らさない。表示専用の可視セル確認は既存HashSetを参照し、AI知識や標的判定とは分離する。

時間は `unscaledTimeAsDouble` を使う。論理ルートTransform、Collider、占有位置、経路、AP、行動順、セーブ値は補間しない。演出は戦闘処理やターンを待たせない。

## 検証入口

`Tests~/BoardActorAnimationTests.cs` の `PlayMode(GameSystems)` を、実際に開始したゲームシーンで実行する。待機とGhost Kingの完全静止、共有コールバック停止、3動作の開始・終了、正確な姿勢復帰、論理位置・Collider・HP・AP・Randomの不変、視界外の抑止、即時方向追従、セーブ復元時の抑止、死亡オブジェクト0件、複数子メッシュの登録解除を検証する。旧版41チェックの成功記録を、今回更新後の実行結果として扱わない。更新版の実Unity実行結果は統合検証記録を参照する。

## PNG連番の動作見本

`Tests~/BoardAnimationPreview.cs` の `Capture(GameSystems)` は実Play Modeの開始後に、白ナイト・黒ナイト・赤ドラゴンを通常の生成経路で一時作成する。完全静止の待機、移動、攻撃、被弾を960×420、30fps、各20枚、計80枚で記録する。既存の白・黒・赤マテリアルを書き換えない。

これは戦闘リプレイではなく、通知APIと本番 `BoardActorAnimation.Tick` を使う表示動作見本。独自のポーズ計算はない。全ての待機フレームが正確に同じ姿勢であることも検証する。実駒・ゲームカメラ・APを変更せず、作成した表示物と描画用資源は終了時に破棄する。

実行先は隔離した `UnityValidation` プロジェクトに制限する。`Tests~` のソースを `Assets/Editor` に同期し、`-batchmode -executeMethod BoardVisualLiveValidation.Run -logFile <絶対パス>` を実行する。この入口はPlay Modeの開始・終了を管理するため `-quit` は付けない。

出力は `Docs/PlaytestScreenshots/board-animation-event-only-20261018/frames/frame_000.png` ～ `frame_079.png` と、同フォルダーの `recording.json`。旧版6区間120枚の記録とは別フォルダーに保存する。
