# Image Marble

Shader: Fantasy Kingdom/Image Marble (Built-in Render Pipeline)

指定のJPGをMarble image、PoliigonのNormal.pngをStone normalに設定。「大理石　白」に適用済み。

- Pattern contrast: 1で画像そのまま。1より大きくすると暗い筋を強調。
- Normal strength: 凹凸の強さ。初期値0.12。提供された法線画像は別素材のため、画像の筋と一致する凹凸ではない。不要なら0。
- Polish / Smoothness: 光沢。初期値0.85。
- Metallic: 金属反射。大理石の初期値0。
- Marble imageのTiling: 模様の繰り返し回数。モデルのUVを使用する。

JPGの誤ったNormal Map読み込みをDefault/sRGBに修正。元画像は変更なし。
UnityでShaderHasError=falseとマテリアルプレビューの筋模様を確認済み。

## UVがない台座への対応
台座.objのUVが(0,0)のみのため画像の一点が全面に出ていた。Image MarbleにProject without UVを追加し、大理石 白で有効化。ローカル座標の3方向から画像と法線を投影する。Projection tiles per local unitで模様の密度を調整できる。通常のUV方式もチェックを外して利用可能。
Unityで台座の天面・側面に実画像の筋が出ることと、シェーダーのコンパイル成功を確認。

## 柄の色
Pattern colorで画像の暗い柄部分の色を変更可能。Stone colorは地色。Pattern colorの初期値は黒で従来の見た目を保持。Pattern contrastは混ざり具合・柄の濃さ。UV投影・UV不要投影の両方に共通。
Unityの台座実物で赤い柄に変わることを確認し、確認後は黒に戻した。
