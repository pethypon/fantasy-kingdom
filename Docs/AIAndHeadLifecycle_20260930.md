# AI実行検証・頭上UI・描画負荷

## 頭上UI
- `Assets/Script/UI/UnitHeadUI.cs` の `OnDisable` で外部Canvasの表示を即座に隠す。死亡処理は駒を破棄せず `SetActive(false)` にするため、`LateUpdate` のHP判定だけでは消せない。
- `Attach` は同じオブジェクトへの再呼び出しで既存コンポーネントを返す。
- Status側だけの非表示にも追従する。破棄時の外部UI削除は継続。
- 位置・拡縮が同じ場合はRectTransformへの再代入を避ける。選択中の前面表示とズーム追従は維持。

## 描画上限
`Assets/Script/Common/RuntimeFrameBudget.cs` が開始時に一度だけ常駐オブジェクトを生成する。通常60 FPS、非アクティブ15 FPS。デスクトップでVSyncによる上書きを避け、終了時に元の設定を戻す。バッチ検証には自動生成しない。

Play中のHierarchyの `DontDestroyOnLoad / RuntimeFrameBudget` のInspectorで上限を調整できる（Play中の変更は保存されない）。既定値は同スクリプトの2つのSerializeField。ゲーム時間・ターン規則は変更しない。

描画を必要以上に繰り返す負荷を抑える対策であり、ファンの回転数や温度は未測定。Unityのインポート・シェーダーコンパイル等のEditor負荷には別途影響される。

## AI
- 移動・攻撃・スキル実行直前に、行動者の生存、陣営、非表示、スタン等を再確認。
- 移動パターンと、通常攻撃の対象・視界・射程・地形を再確認し、無効ならAPを消費しない。
- スキル候補のAP判定に実際の割引後コストを使用。消費時は既存AP処理に基本コストを渡し、割引の二重適用を避ける。
- 攻撃・スキル中の一時的な選択と対象をfinallyで復元。
- 失敗候補キーに行動者を含め、別ユニットの同種行動まで抑制しない。

## 検証
隔離UnityプロジェクトのSceneSmokeに `Tests~/AIExplorationTests.cs` と `Tests~/HeadLifecycleTests.cs` を組み込んで実行。484件のPASS、ALL PASSED。
頭上UIは実際の `HandleDeathIfDead`、非表示、コンポーネント停止、重複取り付け、HPゼロを検証。描画上限はフォーカス切替と終了時復元を検証。実機でのFPS・消費電力の比較測定は未実施。
