# 隔離コピーでの検証

1. Assets / Packages / ProjectSettings を、名前に UnityValidation を含む別ディレクトリへコピーする。
2. Tests~ 内の SceneSmoke.cs と TerrainOptimizationTests.cs をコピー先の Assets/Editor に配置する。
3. Unity 6000.3.8f1 を `-batchmode -nographics -projectPath "コピー先" -executeMethod SceneSmoke.Run -logFile "ログ出力先"` で起動する。`-quit` は付けない。
4. `[CoreTest] Done: 54 passed, 0 failed`、`[SceneSmoke] ALL PASSED` と終了コード0を確認する。シーン43＋地形/最適化37と合わせ134項目。
5. 製品版ビルド検証は ReleaseValidation.cs も Assets/Editor に置き、`-executeMethod ReleaseValidation.Run` で実行する。

テストはコピー先の製品名・カタログ・検証用ユニットを変更し、終了時にUnityを閉じる。元の制作プロジェクトでは実行しない。コピー先の検証用Resourcesを元へ戻さない。

GCの検証にはUnity ProfilerRecorderを使用し、既知の割り当てを検出できることを最初に検査する。一般的な.NETのGC.GetAllocatedBytesForCurrentThreadはこのUnity環境では使用しない。

結果はOptimizationValidationResults.txt、実装と測定条件は../Docs/TerrainOptimization_R1.mdを参照。

2026-09-21追記: 最新のSceneSmokeではVisualRegressionTests.csもコピー先Assets/Editorへ配置する。合計350項目。表示崩れの画像を出す場合は -nographics を省略する。画像はコピー先の隣のvisual-capturesへ出力される。
