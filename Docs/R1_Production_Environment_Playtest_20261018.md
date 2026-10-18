# R1本番導入・開始環境・駒モデル・クリア進行の確認

2026年10月18日（日本時間）。Unity 6000.3.8f1。実行は本体を複製した `UnityValidation` で行い、検証済みの本番アセットを元プロジェクトへ反映した。ユーザーが開いているUnityを終了・操作していない。

## 実装した内容

| 依頼 | 反映内容 |
| --- | --- |
| 既存10種の能力を本番で有効化 | 実データの固有能力を有効化し、AP・CD・曲線・対象規則の未設定値を補完 |
| 新6職種の本番導入 | 白魔導士・聖騎士・侍・忍者・狂戦士・航海士を両陣営の通常召喚とAIへ登録 |
| 未確定値 | Unityで編集できる「暫定初期調整v1」を導入。確定仕様・最終バランスとは区別 |
| 開始時の環境生成 | 新規ゲームの初期駒配置後、視界作成前に生成。ロード時は保存内容を復元 |
| 森・草むら | 同じ高さの上下左右に連結するランダムな3〜4マス。群同士が隣接して巨大な塊にならない |
| 環境の見た目 | 森・草むら・岩場・毒沼・泥・道・泉・間欠泉・洞窟・煙の10種を作成 |
| 駒の見た目 | 召喚用の球体だった既存10職種と新6職種へ、装備・輪郭が異なる金属チェス風モデルを導入 |
| マップ外の霧 | 既存の未知領域のマテリアルを共有した外周。盤面は開けたまま、背後の灰色を隠す |
| クリア後の脅威度 | 通常・時間切れ勝利で次回+1、上限100。同じ試合の再ロード・終了処理の再入では重複加算しない |

初期配置に使っている完成済みモデル、王、異形の王は保持した。新しいモデルは元のStatus・選択Collider・Prefab GUID・Stats・能力・各行動マスクを保持する。見た目の変更による新しいUpdateや物理判定は追加していない。

## Unityで変更する場所

- 能力の初期調整一覧: `Assets/_FantasyKingdom/GameContent/R1Production/R1ProductionSettings.asset`
- 既存職種の能力: 既存UnitDataの `specialSkill` 参照先。`Assets/_FantasyKingdom/GameContent/SpecialAbilities/` に格納。
- 新6職種: `Assets/_FantasyKingdom/GameContent/R1Production/Units/<職種>/` のStats・Ability・ActionTiles・Visual。
- 環境と生成率: `Assets/Resources/Terrain/EnvironmentCatalog.asset`。日本語の生成設定から変更できる。
- 環境モデル: `Assets/_FantasyKingdom/EnvironmentVisuals/Prefabs/`
- 16職種の共有メッシュ・材質: `Assets/_FantasyKingdom/UnitVisuals/`
- 既存10職種の召喚Prefab: `Assets/Prefabs/Units/Player/`。通常召喚では両陣営が共用する。

導入済みなので再インストールは不要。明示的な導入メニューは `Fantasy Kingdom → 開発スタジオ → 環境` と `駒の見た目`。実行時やEditor起動時に自動でアセットを上書きする処理はない。導入後の独自編集・モデル差し替え・意図的な無効化を再導入で上書きしない。

AP・CD等の全表は [初期調整記録](R1_Production_Content_20261017.md)。新6職種の基礎値・製作費・維持費は既存の役割から引き継いだ暫定値で、それぞれ独立したデータとして調整できる。

## 生成と描画の負荷

初期生成は独立した乱数と上限付き試行を使い、他のゲーム乱数を変えない。既存の手動配置、川・高山、初期拠点・駒の周囲を保護する。地形のないマスには置かない。洞窟は相互接続のペアで生成し、開始時に煙は生成しない。保存した盤面では生成し直さない。

標準設定の生成上限は160セル。環境モデルは各1Mesh・1Renderer、10種で共有Material1個、追加Collider・Status・MonoBehaviourなし、影投影なし。描画は視界判定に従い、見えていない環境を表示して敵AIへ知らせることはない。

駒は各1Mesh・1Renderer・1SubMesh、16職種で共有Material1個、各1,800三角形以下。モデル・材質はEditorで焼き込み、ゲーム中の生成処理・材質複製・個別Updateは増やしていない。

外周の霧は1Renderer・16頂点・8三角形。既存の霧マテリアルを共有し、毎フレームの再構築を行わない。

## 実AI対戦と、その結果から直した不具合

タイトルから通常の新規ゲームを開始し、開発者用プレイヤーAI対通常敵AIで実際に終局まで進めた。人の手動操作によるプレイとは区別する。両軍脅威度15、通常資源・AP・時間・timeScale・思考予算を維持した。

| 観測版 | 試合 | 所有ターン | 決着 |
| --- | ---: | ---: | --- |
| 修正前の本番R1・環境有効 | 3 | 1,343 | 全試合プレイヤーAI勝利、敵王撃破 |
| 移動候補・作戦寿命修正後 | 1 | 186 | 敵AI勝利、味方王撃破 |
| 合計 | 4 | 1,529 | 全試合正常終局 |

実対戦部分は合計約12.1分。数時間・一晩の耐久検証ではなく、長期リークや公平な勝率を保証する結果ではない。最新のクリア進行とモデル差し替えは、この対戦後に導入して別の回帰試験・ビルドで確認した。

修正前は15,798行動中2,014失敗。調査から次を修正した。

1. 自軍の壁をAIの移動候補から除外。既存の盤面更新時に占有セルを集め、候補ごとの全建物走査は増やさない。
2. 詠唱・聖騎士の回復領域・回復直後の行動制限を、移動・支援・退却等の共通候補入口でも確認する。
3. 忍者の位置交換は、両端が既知の自軍建物に重なる候補を除外する。見えない敵の占有は透視しない。
4. 作戦がターン開始時から保持した部隊一覧に、倒された・Destroy済みのStatusが残る不具合を修正。作戦終了時と参照時の生存確認を加え、消えた担当駒を「集合済み」にしない。
5. 新職種のAI評価・採用役割を継承元へ接続。R1能力が有効な駒に旧ランダム能力が復活するシミュレーションのfallbackを修正した。

修正後の確認試合は1,765行動中2失敗、固有スキル4回すべて成功、作戦停止警告は再現しなかった。敵AIのseed・性格が前の3試合と異なるため、数値を厳密な同条件A/B改善率としては使わない。失敗が全て壁由来だったとも断定しない。

初期3試合では固有スキル88試行・80成功、8種類を使用した。全新職種は両陣営の実executorによる216項目の召喚検証にも通った。一方、通常意思決定で白魔導士の召喚は観測できず、全職種が同程度に選ばれると証明したわけではない。

初期維持費未払いと後半の余剰を分けて記録した。確認試合では敵の未払いがround57〜61にもあり、経済バランスの最終確定にはさらなる条件別試行が必要。詳細CSV・集計は [観測結果](PerformanceData/r1-production-20261018/analysis/playtest-summary.md)。

## Unityでの検証

- `SceneSmoke.Run`: 全体回帰ALL PASSED。保存互換、タイマー終端、既存能力・環境・戦闘AI等を含む。
- `R1ProductionContentTests`: 本番16職種の設定113項目を確認。
- `R1ProductionAIRecruitmentTests`: 両陣営・新6職種の実候補生成と召喚216項目を確認。
- `EnvironmentGenerationTests`: 複数seedのセル・連結・予約領域・保存復元・生成上限8,700項目を確認。独立した8,700本のテストケースという意味ではない。
- `R1AIOwnWallLegalityTests`: 自軍壁、隠れた敵壁、通常建物、位置交換、行動制限42項目を確認。
- `AIOperationLifetimeTests`: 真のUnity fake-null、キャッシュ、ターン末尾、次ターン継続を両陣営26項目で確認。
- `ThreatClearProgressionTests`: 勝利+1、重複、上限、開発AI除外、v8/v1互換、未来形式保護34項目を確認。
- `R1UnitVisualTests`: 全16モデルの予算、実Prefab参照、既存データ保全、再導入、独自編集保護119項目を確認。完成済み初期モデル等のSHA不変も確認。
- Unity Test Framework: **9件中9件成功、失敗・スキップ0**。実シーンを起動する統合試験を含む。
- Windows x64非Developmentビルド: **Succeeded、ビルドエラー0・警告0**。Editor終了後の警告は次節に別記する。
- 森・草むらの実生成盤面、通常Fog画面、環境10種、駒16職種の見本を画像で確認した。

## 終了時メモリ警告の調査結果と限界

起動idle、空シーン、ゲーム開始20実フレーム後の通常停止、実対戦では、同じJobTempAlloc終了警告は再現しなかった。ビルド直後のEditor終了では再現した。

native allocation stackを有効にしたビルドで、ユニーク2,792件・1,071,928bytes（約1.02MiB）を確認。全件が `ShaderCompilation::CompileVariantJob` を含むUnity内部のshader compilation経路だった。同じallocation群が2回出力されており、2倍の漏れ量として数えていない。ビルド後3秒・12,230Editor更新を待つ比較でも残存した。

**この終了警告自体は未解消**。具体的なshader/materialと解放されない根本理由は未特定。AIのC#処理に由来するstackは得られていないが、ゲーム全体の長期リーク不存在を保証するものでもない。警告抑制、強制GC、実行時の定期全削除によって隠していない。

初期3試合のOS privateはcold開始30秒を除き約1,139.8→1,248.0MiB。Unity tracked増加は各試合約4〜8MiBで、駒・建物・Editorキャッシュの増加も含む。常駐量・累積allocation・Mono・native・OS privateを混同しない。詳しいstack分類・ログSHAは [native診断](PerformanceData/r1-production-20261018/analysis/native-memory.md)。

## 記録

`Docs/PerformanceData/r1-production-20261018/` に観測CSV/JSON、集計、UnityテストXML、検証ログのgzip原本、native診断、SHA256目録を保持する。実盤面の画像は `Docs/PlaytestScreenshots/environment-startup-20261018/`。debug画像は環境確認のためFogを一時的に隠した撮影で、通常プレイ画面とは区別する。

駒の見本: [16職種](../Assets/_FantasyKingdom/UnitVisuals/Review/R1ChessUnits.png)。環境見本: [10種類](../Assets/_FantasyKingdom/EnvironmentVisuals/Review/EnvironmentVisuals.png)。

クリア進行の保存方式・旧版移行は [脅威度進行](Threat_Clear_Progression_20261018.md)。検証fixture由来の参照されていない能力アセット67組は、生成・コピー記録とSHA一致、全GUID参照0を確認してバックアップ後に除去した。ユーザー作成物や既存の回復用シーンは変更していない。
