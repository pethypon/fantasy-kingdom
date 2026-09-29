# Fantasy Kingdom 制作入口

Unity の上部メニュー **Fantasy Kingdom → プロジェクト案内** から、編集したい内容のフォルダへ移動できます。

## ユニット作成

1. **Fantasy Kingdom → ユニット工房** を開く（再生停止中）。
2. 名前とモデルPrefabを指定。モデルなしなら仮のカプセルを作成。
3. 行動タイプを選び、HP・攻撃・防御・成長率・維持費・召喚コストを設定。
4. 「ゲームに登録」を有効にして「Prefab とデータを作成」。
5. 次回のゲーム開始から反映。

`Units/名前/Unit.prefab` が見た目、`Stats.asset` が能力値です。後からInspectorで編集できます。
有効な登録は `Assets/Resources/GameContent/UnitCatalog.asset` で管理します。
同じ行動タイプの有効な登録は1つ。登録を外すと既存の設定へ戻ります。
「ゲームに登録」を外して作れば見た目・能力値の候補を複数保存できます。
移動・攻撃・AIのルールは既存タイプを利用します。独自の新しい行動ルールはコード対応が必要です。

## フォルダの役割

| 場所 | 内容 |
| --- | --- |
| `_FantasyKingdom/Units` | 新しく作成したユニット（1体ごとのPrefab・能力値） |
| `Data` | 既存の能力値・設定 |
| `Prefabs` | 既存の駒・建物・ゲームシステム |
| `Resources/GameContent` | 実行時に読み込むユニット登録 |
| `Resources/Terrain` | 地形・霧・地形バリエーション |
| `Resources/UI` | UIテーマ |
| `Scenes` | シーン |
| `Script` | ゲーム本体コード |
| `Editor` | Unity内の制作ツール |
| `Gentleland` / `Honeti` / `TextMesh Pro` | 外部アセット。更新との互換性のため元の配置を維持 |

Resources内の登録ファイルの名前・場所は実行時読込に使います。移動する場合は読込処理も確認してください。
