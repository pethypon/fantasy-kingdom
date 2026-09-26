# 移動予告と地形素材（2026-09-24）

## 操作
駒を選択し、移動可能マスへカーソルを置くと「移動コスト」と「残りAPの変化」、薄い水色の予定ラインを表示する。対象外・AP不足・攻撃/建築/召喚モード・メニュー表示中は隠す。予告パネルはクリックを遮らない。
AP計算は移動実行と同じ APSystem.CalcCost。現在は駒固有の移動パターンに従う1回の移動（基本3AP＋疲労・上り坂・状態/能力補正）。歩いたセル数ごとの課金や複数手の自動移動は導入していない。経路による課金差はなく、表示ラインはその一手の直接移動を表す。

## 作成素材
Assets/Resources/Terrain 内に WaterImage.mat / GrassImage.mat / RockyGrassImage.mat / RockImage.mat を作成。画像は Textures 内。Standardシェーダー、色は白、金属度0、水だけ光沢強め。元画像は変更なし。
WaterBlock / GrassBlock / RockyGrassBlock / HighMountainBlock の4つのPrefabで使用。

## 自作オブジェクトへの差し替え
MapCreate の Inspector の Grass Prefab は1段目、Rocky Grass Prefab は2段目、High Mountain Prefab は3段目、Water Prefab は川に使用。各Prefabの基準は中心原点・1×1×1・地形Collider付き。モデルのRendererに対応するImageマテリアルを設定してPrefabを割り当てる。空欄ならResources内の既定Prefabを読み込む。
各柱の下段もその段に合う地形を使用する。R1以外の旧地形生成は既存dirtPrefabを維持。水と3段目の高山は引き続き移動不可。

## 関連修正
現在の初期配置モデルにStatusがなく操作不能だったため、王/異形の王/初期部隊は配置スロットのKindとTeamで初期化し、必要なStatus/Colliderを補う。子Colliderをクリックした場合も親Statusを選択する。モデルの外見やPrefab自体のステータスは書き換えない。

## 確認
Unityで4つのPrefabのTexture/Colliderと2つの表示Shaderを検証してPASS。Playで地形画像・マス線・選択ハイライトを確認。スカウトのホバー予告3AP、35→32と実移動後32APが一致。続くホバーで疲労込み4AP、32→28を確認。再生終了後、予告文字を30にし、パネルをカーソルの上側へ配置して下部ステータスとの重なりを軽減。
