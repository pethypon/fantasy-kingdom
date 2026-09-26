# 長期保守の基準と今回の監査（2026-09-24）

## 対象と結果
移動予告/クリック、地形グリッド、選択表示、オブジェクトプールを重点的に監査し、周辺の初期化・検索・マテリアル生成箇所も検索した。全ファイルを行単位で精査したという意味ではない。10年間の無修正動作を保証するものではなく、継続検証できる基盤として扱う。

- MovementPreviewUI: 状態判定、パネル位置、経路更新を分離。同じAPと移動先なら文字列・TMPの更新を行わず、同じ始点/終点/マップなら経路頂点を再計算しない。表示が必要になるまでUI/Materialを生成しない。参照が失われたら表示を隠す。
- MoveGenerator.TryGetMoveDestination: ホバーとクリックが同一判定を使う。駒や霧などに隠された移動マーカーをクリックで貫通しない。子Colliderも対応し、実行先はMovePositionsの正規座標を使う。古いマーカーは実行不可。
- ObjectPool: 破棄済みの待機要素を飛ばして次の再利用可能要素を使用する。管理外オブジェクトを返却済み集合に残さない。ClearAllは待機中だけ破棄し、貸出中のPrefab対応は保持する。二重返却は無視する。
- UnitSelectionHighlight: 生成Materialを所有・破棄し、renderer.material経由の参照を除去。再Initで表示物を重複生成しない。選択リングは駒の高さに追従する。
- MapGridOverlay: 再構築用リストを再利用し、座標変換行列を一度取得する。無効化中に再構築しても表示を勝手に有効化しない。

## 所有と更新の契約
- AP消費の唯一の計算元はAPSystem。UI独自の消費式を追加しない。予告は毎フレーム現在AP/疲労を再評価するため、表示キャッシュで古いコストを使わない。
- 移動範囲はMoveGenerator、画面の配置と予定ラインはMovementPreviewUIが担当する。表示変更でゲームルールを変更しない。
- new Material / new Meshした側が破棄する。アセット由来sharedMaterial/sharedMeshを破棄しない。
- プールから貸し出した物はReturnする。ClearAllはシーン全体の破棄処理ではない。外部で貸出物をDestroyする設計を新規導入しない。
- 地形は生成後不変を前提にルートをキャッシュ。将来、動的な高さ変更を入れる際はMapのリビジョン番号を更新条件に追加する。
- 既存[SerializeField]やPrefab GUIDの変更には移行処理を用意する。セーブ構造を変える際はバージョンと旧データ読込テストを追加する。

## 検証
Unity 6000.3.8f1 の隔離プロジェクト UnityValidation で SceneSmoke.Run を実行。
367 PASS（既存361 + MaintenanceRegressionTests 6）、ALL PASSED。
今回の追加テストは、ClearAll後の貸出物返却、破棄済み待機物、二重返却、子Collider、遮蔽物、古いマーカーを確認する。
ログ: C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/maintenance-validation.log
FPSや実機GPU時間の改善率は未計測。削減したのは上述の処理・確保であり、ゲーム全体の速度改善率は主張しない。

## 検証の再実行
Tests~はUnityの通常コンパイル対象外。SceneSmoke.csとRegressionTests、TerrainOptimizationTestsを隔離したプロジェクトのAssets/Editorへコピーする。Assets/Script、必要なPrefab/Resources/Scene/Packages/ProjectSettingsを現在版に同期する。実際の開発プロジェクトやユーザーのセーブ先でテストを実行しない。
SceneSmokeはプロジェクト名にUnityValidationを要求し、companyName/productNameを検証専用へ変更する。既存のAssets/Editor/TerrainPrefabEditor.csを利用する。Tests~/SceneSmoke.csは今回通過した実行器の保存版。
実行例: Unity.exe -batchmode -nographics -projectPath ".../UnityValidation" -executeMethod SceneSmoke.Run -logFile ".../validation.log"
非同期Playmode検証なので -quit は付けない。ALL PASSED、プロセス終了コード、例外を確認する。

## 継続運用
1. Unity/Packageの版をProjectVersion.txtとmanifest/lockで管理する。更新は専用コピー/ブランチで行い、セーブ読込と回帰テスト、対象機種のビルドを先に確認する。
2. 表示変更はGameビューでも確認する。バッチテストだけでは文字サイズ・重なり・透明描画順を保証しない。
3. 性能は同じシード・同じ駒数・同じ機種でCPU/GPU/GCを比較する。特に100体時AI、視界更新、地形再生成を計測対象にする。
4. 次の監査対象はWildBossSystem等の動的Material生成/破棄、セーブ移行、初期化依存の明示化。今回未修正の領域を完了扱いにしない。

## 追加監査・改善（2026-09-25）

### 動的な描画資源の所有
建築・召喚のフォールバック、ダンジョンマーカー、ボスのデコイ/親衛騎士/雷クリスタルを確認。以前は各オブジェクトでnew Materialしていたが、オブジェクト破棄でMaterialが自動解放されないため、明示的な所有管理へ変更した。
PrimitiveMaterialBinding.Apply(renderer,color)に統一し、同色のStandardマテリアルを参照数付きで共有する。色変更はApply経由で行い、sharedMaterialを直接変更・破棄しない。OnDisableで参照を返し、最後の利用者がいなくなった時点で破棄する。OnEnableで再取得するため、スクリプト再読み込みにも対応する。既存アセットのMaterialはこの管理対象外。
既存のOpaque設定を維持しており、色のalphaだけで半透明になる仕様変更は加えていない。

### 終了処理
BuildSystem/SummonSystemの破棄時にカーソルの後始末を実行する。BuildCursorControllerはカーソル本体が先に破棄されても生成Materialを解放する。
UnitPanelUIのHPバーで生成したSpriteをパネルとともに解放する。共有のTexture2D.whiteTextureは破棄しない。Sprite範囲はTexture実寸から取得する。

### 確保の削減
ダンジョンの報酬候補は起動時に一度作成し、報酬抽選ごとのEnum配列/List生成を除去した。候補順序とRandom呼び出しは従来と同じ。
ResourceBarUI/APPanelUI/TopBarUIはすでに値変更時のみテキストを更新する仕組みがあるため、今回それらを再設計していない。

### 検証結果
隔離UnityValidation / Unity6000.3.8f1 / SceneSmoke.Run。
376 PASS = 前回367 + MaterialLifetimeRegressionTestsの9項目、ALL PASSED。
同色100オブジェクトでMaterial1個、同色再適用、他オブジェクトへ波及しない色変更、無効化/再有効化/部分破棄/全破棄の参照管理を確認した。
実際のNative Material破棄はPlay時にはUnityのフレーム末尾で行われる。テストの「管理情報なし」は参照数管理のエントリを検証するもの。GPUメモリ量やFPS改善率は計測していない。
ログ: C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/material-lifetime-validation.log
追加テストの再実行時はTests~/MaterialLifetimeRegressionTests.csもAssets/Editorにコピーする。

## 操作・UI更新の追加改善（2026-09-25）

- UnitClickの初回選択と再選択に共通の生存・所属・スタン判定を適用する。無効な再選択は現在の選択や移動マーカーを消さない。
- 通常攻撃とスキルもGetComponentInParentでStatusを解決する。スキルの範囲判定にはモデルの子Collider位置ではなくStatusの盤面位置を使用する。敵視界・所属・AP・範囲判定は維持する。
- マウス入力時のCamera.main未配置を安全に処理する。
- PlayerMoveのClick2直後の無条件な視界再構築を除去。盤面が変化する実際の移動処理内で更新する。今後Click2に盤面変更操作を追加する際も、成功した変更側が視界更新を担当する。
- UnitPanelUIは同じホバー対象の再指定で即時再描画しない。選択・ホバーとも0.15秒周期で状態を反映し、対象変更と明示的Refreshは即時反映する。ホバー中に行動ボタンを一瞬有効化してから隠す処理を除去した。ボタンラベル参照をキャッシュし、レベル文字列の二重設定も除去した。
- 範囲スキルのCombatRegistry走査は所有するListを再利用し、Snapshotの一時Listと配列の生成を除去した。スキルの対象結果リストは呼出しごとに独立させている。

### 検証
隔離UnityValidation / Unity 6000.3.8f1 / SceneSmoke.Runで385 PASS（前回376 + InteractionMaintenanceTests 9）、ALL PASSED。
追加検証: ホバー中のボタン再有効化なし、同じ対象の即時更新抑制、周期HP更新、選択固定とボタン復帰、スタン/死亡ユニットへの再選択拒否、位置がずれた子Collider、味方への通常攻撃拒否、子Colliderからの回復スキル成功とAP消費。
初回テストではAPを100に設定しても既存仕様の50上限で丸められるため、テスト期待値を実際の消費前APに修正した。ゲームのAP上限仕様は変更していない。
Tests~/InteractionMaintenanceTests.csを隔離プロジェクトのAssets/Editorへコピーして再実行する。実行器はTests~/SceneSmoke.csに保存。
結果: Tests~/InteractionMaintenanceValidationResults.txt
ログ: C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/interaction-maintenance-validation.log
今回はバッチPlayMode検証。Gameビューでの目視や対象機種のFPS測定は未実施。

## 戦闘UI・巡回操作の改善（2026-09-25）

- ステータスパネルを濃紺のほぼ不透明な背景・細い外枠・陣営色の見出し線へ変更。HP文字を30へ拡大し、情報列の内側余白を整理。行動ボタンは暗い色面と赤/紫/青緑/金の縦ラインを使用する。BrandGuide.ApplyCommandStyleへ装飾を集約。追加画像アセットや毎フレームの装飾アニメーションはない。
- Tabは実際の現在選択を基準に巡回し、Shift+Tabは逆順。生存・アクティブ階層・味方・ユニット・スタンを判定する。選択候補の一時Listを除去し、レジストリ不在時のみ再利用Listへ収集する。同じ1体しか選べない場合は移動マーカーを作り直さない。
- Shift単独のTurnEnd割当をPlayerAction.inputactionsと生成コードから削除した。EnterとUIボタンは維持する。逆順巡回時の誤ターン終了を防ぐための意図したキー変更。入力アセットを再生成しても同期する。
- 通常攻撃クリックとダメージ予測の対象判定をUnitClick.CanTargetNormalAttackへ共通化。視界・範囲・生存・敵対陣営の条件に一致した対象だけを表示する。子Colliderと魔物/乱入者/強敵に対応。スキル中は通常攻撃の誤った予測を出さない（スキル専用予測は未実装）。
- ダメージ予測は同じ対象でも0.15秒周期でHP・防御等を再計算し、位置追従は毎フレーム行う。文字を日本語化し28/24へ拡大。Canvas座標で上下左右を制限し、解像度スケーリングで画面外へ出る問題を修正。UI上やメニュー表示中は隠す。

### 確認
Unity本体のGameビュー（1280x800、Editor内表示0.76倍）でプレイヤーのスカウトを選択し、パネルの配色・余白・ボタン・HPと操作ヒントの配置を目視確認。確認用Playを終了した。ダメージ予測の全機種目視検証やFPS計測は未実施。
隔離UnityValidationのSceneSmoke.Run: 404 PASS、ALL PASSED。既存385にTacticalUIRegressionTestsの19項目を追加。巡回の正逆順/折り返し/選択なし/スタン/死亡/非アクティブ、ShiftとEnterの割当、敵対4陣営・味方・範囲外・視界外の予測条件、装飾の非Raycastを検証。
Tests~/TacticalUIRegressionTests.csもAssets/Editorへ配置して再実行。入力アセットAssets/Actionも同期する。
ログ: C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/tactical-ui-validation.log
結果: Tests~/TacticalUIValidationResults.txt
