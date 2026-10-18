# 環境・新規ゲーム開始時の画面検証

実SampleScene、実タイトルの「はじめから」ボタン、本番環境カタログを使用。開始から20実フレーム以上待機し、実カメラで1600×900 PNGを取得しました。素材や生成・戦闘ルールは変更していません。

- environment-startup.png: 通常プレイのカメラ位置・投影と実HUD。本番Fogと環境の表示状態を保持しています。HUDをRenderTextureへ含めるため、取得中だけOverlay Canvasを同じカメラへ接続し、その後元に戻しています。
- overview-debug.png: 全盤面の生成配置を調べるためのdebug fixture。通常プレイの視界ではありません。
- forest-closeup-debug.png / bush-closeup-debug.png: 開幕に自然生成された連結3〜4マスの森・草むらを拡大したdebug fixture。通常プレイの視界ではありません。

debug画像はFog（盤外Fogを含む）のRendererだけを一時停止し、生成済み環境Prefabのrootを一時表示、HUDを一時非表示にしています。未発見の間欠泉等も見えます。探索・発見・視界データ・セーブ状態を変更していません。取得後はカメラ・Canvas・Fog Renderer・環境activeSelf・共有材質参照を復元し、RenderTexture/Texture2Dを解放しています。

seed=56192; environmentSeed=1479016377; map=35×35; environmentCells=136
森 4マス: (0,23), (0,24), (0,25), (1,25)
草むら 4マス: (0,6), (0,7), (1,7), (0,8)
