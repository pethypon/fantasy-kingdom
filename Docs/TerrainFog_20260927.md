# 地形バリエーション・霧・Windowsビルド（2026-09-27）

## ブロックの追加
Unityメニュー「Fantasy Kingdom > Terrain > 地形バリエーション設定を選択」で
`Assets/Resources/Terrain/TerrainVariants.asset` を開く。

- Grass：草原・高さ1。GrassBlock / GrassMeadow / GrassClover。
- Low Mountain：低山・高さ2。RockyGrassBlock / LowMountainSparse / LowMountainGreen。
- High Mountain：高山・高さ3。HighMountainBlock / HighMountainDark / HighMountainGrey。

一覧の要素数を増やしてPrefabを追加する。空欄は無視される。
Prefabは1×1×1、中心原点、地形Layer（6）、Collider付きが基本。
既存Prefabの複製からマテリアルを差し替えると簡単。
設定は次回のマップ生成・ロード時から有効。形状の変更は通行ルールを変えない。
水は従来のWaterBlockを使用。

MapCreateの「Terrain Variants」に別カタログを指定することも可能。
未指定なら上記の標準カタログを読み込む。カタログが空なら以前の単一Prefabへ戻る。
同じシード・座標・一覧なら同じ見た目。ゲーム進行用の乱数は消費しない。
一覧を編集した場合、以前のセーブもロード時のブロックの見た目が変わる。

## 画像の対応
|マテリアル/Prefab|元画像|
|---|---|
|GrassMeadow|d18108d4-22b1-42ce-b0fa-7a7b5a5ea7a1.png|
|GrassClover|72b5436f-c6a5-4364-8f41-d2c8d8dfbc7a.png|
|LowMountainSparse|946d4909-84f5-4a51-8bfd-c259f42fe4ce.png|
|LowMountainGreen|3d07814c-c269-4269-abe6-2efd3e307055.png|
|HighMountainDark|c7d1fba3-66a3-46f8-aaf5-5d299f2d0c2a.png|
|HighMountainGrey|8b9f28eb-4e46-4bc1-8c36-9748489bb442.png|

全て `Assets/Resources/Terrain`。画像は同フォルダのTextures。
原画像は変更していない。材質を共有し、MipMapと非Readable設定を使用。

## 霧の調整
- `Assets/Resources/Terrain/UnknownMist.mat`：未探索。Alpha=1で地形を隠す。
- `Assets/Resources/Terrain/ExploredMist.mat`：探索済み・現在視界外。Alpha=.55。
- Shader：`Assets/Shaders/CartographicMist.shader`
- Mist texture A：8120af43-5b51-4b35-a4da-e4767d82803b.png
- Mist texture B：ee577f47-69c9-4cdc-a487-7f775b277ae2.png

Deep slateとSilver mistで暗部・明部の色、Cloud scaleで模様の細かさ、
Drift speedで流れる速度を調整する。未探索のAlphaは1を維持すると地形が透けない。
テクスチャはMirrorで折り返し、マップ座標で連続表示する。
霧のマテリアルはFogChunkRendererで上記2つを共有するため、古いFog.prefabの材質ではなくこちらを編集する。
視界判定は従来のVisionGenerator。変更した16×16チャンクのみ再構築する方式を維持。

## ビルド修正
SteampunkUI/UtilsのasmdefをEditor専用にした。
Editorフォルダ配下でも、上位asmdefが全プラットフォーム対象だとEditor専用コードがPlayerへ入るため、
UnityEditor.Compilation / EditorWindow / InitializeOnLoadのコンパイルエラーが発生していた。
EditorBuildSettingsにSampleSceneを登録。
アセット更新でasmdefが上書きされた場合はInclude PlatformsのEditor指定を確認する。

Windows x64出力：`E:/fantasykingdom/Builds/Windows-20260927/FantasyKingdom.exe`
exeとFantasyKingdom_Data、UnityPlayer.dll等の同梱ファイルは一緒に配布する。
分離した検証用プロジェクトで作成し、検証用ユニット・カタログは最終ビルドから除外済み。

## 検証
- 既存シーン・ゲーム進行テスト：404項目PASS。
- 高さごと100座標の選択再現性、全種類の出現、乱数の非干渉、空欄・空カタログのフォールバックを確認。
- 全地形の画像参照、霧2枚の画像参照、霧シェーダーのコンパイルを確認。
- 現在のScreen Space版HP表示：レベルアップ・最大HP変更・回復・ダメージ・死亡を含む8項目PASS。
- Windows x64ビルドSucceeded。
- Unityの新規盤面で地形画像・霧・上部UIの枠内配置を目視確認。
