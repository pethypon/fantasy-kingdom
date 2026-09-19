# R1 シーン統合検証

1. 元プロジェクトの Assets / Packages / ProjectSettings を、名前に UnityValidation を含む別ディレクトリへコピーする。
2. この SceneSmoke.cs をコピー先の Assets/Editor に置く。
3. Unity 6000.3.8f1 を `-batchmode -nographics -projectPath "コピー先" -executeMethod SceneSmoke.Run -logFile "ログ出力先"` で起動する。`-quit` は指定しない。
4. `[CoreTest] Done` と `[SceneSmoke] ALL PASSED`、終了コード0を確認する。

このテストはコピー先の製品名、R1カタログ、検証用ユニットを変更し、完了後にUnityを終了する。元の制作プロジェクトでは実行しない。ログの全体には環境情報が含まれるため、共有時は検証行のみ抽出する。
