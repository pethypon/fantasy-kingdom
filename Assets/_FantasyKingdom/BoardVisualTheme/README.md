# 陣営別の駒・建物と既存のマテリアル

Unityメニュー `Fantasy Kingdom → 開発スタジオ → 見た目 → 既存の白・黒・赤マテリアルで陣営別モデルを導入`。

表示カタログは `Assets/Resources/GameContent/BoardVisualTheme.asset`。味方は白、敵は黒、強敵は赤です。能力・費用・成長・陣営・セーブ形式を変更しません。

メインクリスタルとサブクリスタルは対象外です。元Prefabのメッシュとマテリアルを保ち、ロードや再適用でも陣営材質へ置き換えません。カタログのSubCrystal項目は旧データとの互換用で、本番の差し替えには使いません。

## 使用する既存マテリアル

`Assets/Prefabs/Units/Materials/Materials/キャラクターマテリアル/` にある次の完成済みMaterialをそのまま共有参照します。

- `プレイヤー　キャラクター　白.mat`：味方の駒・建物・既存の完成モデル
- `異形　キャラクター　黒.mat`：敵の駒・建物・既存の完成モデル
- `敵　キャラクター　赤.mat`：強敵

同じ三つの参照を使い、Materialの複製・新規作成はしません。元のMaterial、テクスチャ、`ImageMarble.shader`は編集しません。既存Shaderのオブジェクト座標によるトライプラナー投影を使うため、パレット用UVの駒・建物も同じマテリアルで表示できます。地面の草・岩・水はそのままです。

## 導入と編集

1. 通常16職種、敵17種・強敵4種、白黒建物モデルを先に生成します。
2. `BoardVisualThemeInstaller.InstallAndReview()` またはUnityメニューでカタログを作ります。
3. カタログの駒・建物・強敵の参照をUnity Inspectorから変更できます。

通常モデルはMeshだけを表示専用Prefabで共有し、StatusやColliderをコピーしません。敵モデルも表示専用Prefabです。選択判定やゲーム状態を見た目の切替で置き換えません。初期の味方Kingだけは `playerVisual` が空欄で、既存の形と白マテリアルを使用します。

再導入は既存のモデル参照と意図的な無効化を保護します。材質参照はユーザー指定の既存三つへ統一します。独自定義や未知のMeshは許可リストに入れない限り元の見た目を保ちます。

許可リストは標準駒/建物とGameGeneratorのUnitSetting・CrystalSystem・SummonSystem・BuildSystemに設定された初期モデルから作成します。標準UnitData、新6職種、既存18建物の編集用複製のIDも記録します。

既に作られたカタログへ既存マテリアルを反映する明示APIは `UseExistingFactionMaterials()`。元の材質Assetの内容を編集せず、表示カタログとこの機能の表示専用Prefab参照を切り替えます。建物の元Prefabは元の材質参照を保ち、実行時の表示だけをカタログの黒へ切り替えます。

## 確認

`ValidateAssets()` は17駒・18建物・4強敵の参照、表示専用Prefabの1Mesh/1Renderer、既存三つの材質参照、許可リストの重複を確認します。

`BoardVisualThemeTests.Run()` は隔離した検証プロジェクトで実行します。未知の定義/Meshの保護、既存三つのMaterialと元SOのSHA256不変、再導入による無効化状態の保全、新規Materialなし、Shaderエラーなしを確認します。

最終材質の見本は `Review/PlayerWhiteUnits.png`、`EnemyBlackUnits.png`、`CrimsonBosses.png`、`FactionBuildings.png`。撮影用Lightは見本用のシーンだけに存在します。

`DiscardUnpublishedMaterialsForValidation()` は未公開の試作8材質・Shader・模様テクスチャを除去する検証専用APIです。`UnityValidation`プロジェクト以外では実行できず、カタログから参照が残る試作品は除去しません。ユーザーの既存マテリアルを削除・編集しません。
