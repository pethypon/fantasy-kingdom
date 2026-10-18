# 建物と環境の見た目（2026-10-18）

建物は18種類の用途別シルエットを残し、自軍の白大理石と敵軍の黒石を用意します。
白側は門枠、王国の紋章、支柱、基壇の象嵌を追加し、黒側は斜めの控え壁、棘状の冠柱と封印の形状を追加します。
敵用の異形OBJは敵駒の造形参考であり、自然物を異形生物へ置き換える目的ではありません。

`Fantasy Kingdom > 開発スタジオ > 見た目 > 白大理石と黒石の建物18種を導入`
で明示的に作成します。自動実行・Play中のアセット変更はありません。
APIは `BuildingVisualGenerator.InstallKingdomArt()`、検証は `ValidateKingdomArt()`、
見本の更新は `RenderKingdomPreview()` です。

追加の造形調整では、住宅系の壁を入口の周囲に分けて彫り込み、門と窓の枠、厚い軒、屋根の稜線を実形状にしました。
同じ白・黒マテリアルだけでも、入口の奥行きと石工の輪郭を識別できます。製材所の刃の支持台、採石場の筋交い、
鉱山の坑口金具、兵舎の武器、倉庫の積荷、大砲の車輪スポークなど、用途が読み取れる備品も補強しています。
サブクリスタルは今回の再生成から除外します。導入済みの白黒サブクリスタルMesh・Prefabを再保存せず、
メインクリスタルを含むクリスタルの元の造形・マテリアル設定を保護します。

- 通常: `Assets/_FantasyKingdom/Art/Buildings/Meshes` と `BuildingPalette.mat`
- 敵: 同フォルダの `EnemyMeshes`、`EnemyPrefabs`、`EnemyBuildingPalette.mat`
- 比較見本: `Review/KingdomBuildings.png`（上3段が自軍、下3段が敵軍）
- 敵見た目PrefabはMeshFilterとMeshRendererのみ。既存のゲーム用ColliderやStatusを追加しません。

上記のパレットは建物単体の編集用見本です。本番の表示には `BoardVisualTheme` が既存の白・黒マテリアルを適用します。
完成した本番材質の比較は `Assets/_FantasyKingdom/BoardVisualTheme/Review/FactionBuildings.png` を参照してください。

36モデル（うちサブクリスタル2モデルは保護）はそれぞれ1メッシュ・1サブメッシュです。各2500三角形以下、1マス内、接地Y=-0.5、
高さY<=0.9を検証します。陣営ごとに共有マテリアルを使用し、実行時のメッシュ生成はありません。
標準建物Prefabと既存建物データ内のVisualは、既知の生成メッシュを参照している場合のみ更新します。
独自メッシュを参照するPrefab、建物のステータス、定義、費用や生成設定は変更しません。

環境10種は通常の自然・危険地形として、彩度を抑えた葉群、根、倒木、シダ、岩の層、泥の轍、
水際の石と波紋、鉱物の堆積、滑らかな煙の塊を追加します。地形ブロックの素材は変更しません。
`TerrainEnvironmentProductionInstaller.Install()` で既存の標準見た目を再生成し、
`RenderPreview()` で `EnvironmentVisuals/Review/EnvironmentVisuals.png` を更新します。
独自の環境Prefab参照は維持します。

環境は各900三角形以下、1描画・1共有材質、影の投影なし、Collider/Status/MonoBehaviourなしです。
森・草むら・岩場は駒の中心を避け、視認性とクリック操作を保ちます。煙は頂点を共有して法線を滑らかにし、
透明材質の重なりやParticleSystemを追加せずに角張りを減らします。

隔離検証用の `KingdomWorldVisualTests.InstallAndReview()` は導入と2つの見本生成後、
描画予算、陣営差分、接地、中心空間、既存ステータスと環境定義の不変を確認します。
実Unityで5,078チェックとステータス・環境定義不変の検証を通過しました。本番への導入記録は
`Docs/Dark_Faction_Art_Animation_20261018.md` を参照してください。
