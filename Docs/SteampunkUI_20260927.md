# SteampunkUI の調整場所

現在のUIは UIBuilder が実行時に生成します。停止中のシーンには完成した Canvas はありません。
Play中のHierarchyで位置を変えた場合、停止すると元に戻ります。恒久変更は以下で行います。

## Unity Inspector で外観を変更

メニュー **Fantasy Kingdom → UI → テーマ設定を選択** を選びます。
Assets/Resources/UI/SteampunkUITheme.asset が選択されます。

- Text Tint: 共通ボタン文字色
- Button Tint: ボタン画像の色
- Panel Tint: メニュー・ステータス等の背景色
- Heading Tint: 見出し色
- Frame Tint: 金属枠の色
- Border Scale: 枠の縮尺。大きくすると枠が細くなります。
- Panel / Frame / Title: 背景・枠・見出し画像
- Yellow / Red / Green: 通常・攻撃系・実行系ボタンの画像グループ（互換性のため旧名を維持）
- Hover / Down: ホバー・押下画像

変更後は再生し直して確認してください。素材そのものは Assets/Gentleland/SteampunkUI/Art を参照します。
日本語フォントと一部コマンドアイコンは従来のものを維持しています。

## コードで配置と大きさを変更

| 調整対象 | ファイル / メソッド |
| --- | --- |
| 資材欄・上部バー | Assets/Script/UI/UIBuilder.cs / BuildTopBar, CreateResourceCell |
| 下部ステータス、HP・レベルの配置 | Assets/Script/UI/UIBuilder.cs / BuildBottomUnitPanel |
| メニュー、セーブ・ロード画面 | Assets/Script/SaveLoad/GameMenuUI.cs |
| 駒の頭上のHP・レベル背景 | Assets/Script/UI/UnitHeadUI.cs / Build |
| ステータスの内容、HP値更新 | Assets/Script/UI/UnitPanelUI.cs |
| 共通文字サイズ、役割の色 | Assets/Script/UI/BrandGuide.cs |
| ボタン・パネルへのテーマ適用 | Assets/Script/UI/GameUITheme.cs |

RectTransform の sizeDelta がサイズ、anchoredPosition が位置、offsetMin/offsetMax が余白です。
UIBuilder の基準解像度と CanvasScaler により画面サイズに合わせて拡縮されます。

## 実装方針

GameUITheme は旧 WoodenUITheme のGUIDを保持して改名しています。
従来の WoodenUITheme.asset は残していますが、実行時は SteampunkUITheme.asset を読み込みます。
素材の元ファイルは編集せず、スライス済みSpriteへの参照で利用しています。
装飾は入力を受けず、文字より背面に配置します。毎フレームの装飾再生成は行いません。

## 確認結果（2026-09-27）

Unity 6000.3.8f1 のGameビュー1280×800で、タイトルボタン、資材欄、頭上表示、選択ステータス、メニューを確認。
金属枠がAP欄の文字を隠す箇所を修正し、再度表示確認しました。
隔離した検証プロジェクトの既存回帰チェック404件がPASS、SceneSmokeはALL PASSEDです。
