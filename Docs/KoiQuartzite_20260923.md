# 鯉の石材マテリアル（2026-09-23）

対象モデル: `Assets/Prefabs/Units/導入用/鯉　異形 完成2.obj`

元ファイルとUnity内OBJのSHA256一致を確認。

- Material: `Assets/Prefabs/Units/Materials/Poliigon_StoneQuartzite_8060/Poliigon_StoneQuartzite_8060.mat`
- Prefab: `Assets/Prefabs/Units/Enemy/Enemy Level1/Koi_Quartzite_8060.prefab`
- Built-in Standard shader。BaseColorはsRGB、NormalはNormal Map、AOとMetallicSmoothnessは線形。
- MetallicSmoothnessのRにMetallic、Aに255−Roughnessを格納。2K、MipMap有効。
- OBJのRed_marbleスロットを上記マテリアルにリマップ。再配置・再インポートでも適用される。
- Displacementは形状を変えないため未使用。元のUVと法線を保持。
- 既存シーンの別モデル「鯉 異形」や台座には変更を加えていない。

UnityのPrefabモードで表示確認。Renderer 1個の全マテリアル参照を検証済み。作業用Editorスクリプトは検証後に削除。

## 大理石の質感の強化

- Shaderを `Fantasy Kingdom/Polished Marble` に変更（Assets/Shaders/PolishedMarble.shader）。元テクスチャの微細な質感を残し、白い石と灰色の脈模様を合成。
- 模様はメッシュのローカル座標から生成し、UVの継ぎ目やユニット移動でずれない。
- Metallic 0、Polish 0.78、Fine relief 0.12。筋の色、強さ、密度はマテリアルInspectorで調整可能。
- OBJ importerで法線を角度100度から再計算し、三角形単位の反射を緩和。頂点位置・形状は変更なし。
- Unityでシェーダーエラーなしを検証。拡大表示で筋模様と滑らかな陰影を確認。
