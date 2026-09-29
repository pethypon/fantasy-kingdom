# 選択・範囲・建築・HUD（2026-09-29）

## 操作

- 同じ位置を再クリックすると、重なった選択可能な駒・建物を手前から奥へ巡回する。クリック位置・カメラ・現在の選択が変わった場合は先頭から選び直す。視界外の敵や死亡ユニットは候補に含めない。
- 選択した味方・視界内の敵の通常攻撃範囲を赤枠で表示する。敵ターンでは移動範囲を青で表示する。表示は確認用で、実行するには通常の攻撃操作が必要。
- 自ターンで移動可能マスにホバーすると、実際の移動処理と同じ計算で移動コストと移動後APを表示する。
- 建築一覧に資源・AP不足理由を表示する。配置中は画面下に領地外・地面なし・建物あり等の理由を表示し、クリック時にも再検証する。領地外を指した際に別のマスへ自動移動させない。
- 資源HUDは既存の装飾を維持し、各枠の幅を均等化。大きい数値は万・億に省略し、ホバーで省略していない値を表示する。
- 下部の操作説明は二段表示。AP・ステータスの下余白は `InputHintUI.ContentBottom` に合わせる。

## 主な調整場所

| 対象 | ファイル（Assets/Script 以下） |
| --- | --- |
| 重なった駒の選択 | Gamesystem/UnitSelectionPicker.cs、UnitClick.cs |
| 通常攻撃範囲・敵ターンの確認 | Gamesystem/TurnSystem/TurnInspectionController.cs |
| 移動後AP | UI/MovementPreviewUI.cs |
| 建築不可理由 | Gamesystem/BuildValidator.cs、BuildSystem.cs |
| 建築一覧 | UI/BuildSummonUIBuilder.cs |
| HUD配置・枠 | UI/UIBuilder.cs |
| 資源表示・正確な数値 | UI/ResourceBarUI.cs、ResourceValueTooltip.cs |
| 操作説明と下余白 | UI/InputHintUI.cs、APPanelUI.cs |

## 検証

- 分離した Unity 6000.3.8f1 プロジェクトで既存・追加の統合検証433項目を通過。
- 選択巡回の単独検証12項目を通過（複数Colliderの重複除去、70個超の当たり判定、視界・死亡除外、位置・カメラ変更、キャンセルなど）。
- 1280×720の実描画で資源最大値、建築理由、操作説明、下部AP・ステータスの収まりを確認。
- 回帰検証コードは Tests~/SelectionPickerTests.cs と SelectionUXTests.cs。Editorで実行する検証用で、製品ビルドには含めない。
- Windows版を `E:/fantasykingdom/Builds/Windows-20260929/FantasyKingdom.exe` に出力。検証用データを除外したビルドが成功し、起動ログに例外がないことを確認。
