# ビルドと復元の手順

更新日: 2026-10-11。Unity 6000.3.8f1を基準とします。

## Unityでの操作

1. **Fantasy Kingdom → 長期運用 → 全コンテンツと保存用IDを検証** を実行します。
2. 通常のBuild Profilesで対象シーン・OS・出力先を指定します。現在の製品シーンは `Assets/Scenes/SampleScene.unity` です。
3. ビルド前のエラーはConsoleに記載された登録アセットで修正します。ID重複は複製で新しい定義を作るか重複登録を外して解決し、公開済みIDをその場で変更しません。
4. 成功した製品と、隣に出力された `FKBuildInfo.json` を同じフォルダで保管します。

FKLongTermBuildProcessorが通常ビルドにも自動で参加します。製品Version、Commit、未コミット変更、UnityVersion、UTC日時、SaveSchema、packages-lockのSHA-256を記録します。ゲームからは `FKBuildInfo.Current` で読み取れます。

## 一時アセットの復元

ビルド中だけ `Assets/Resources/Build/FKBuildInfo.json` を用意して同梱します。既存ファイルと.metaがある場合は元のバイト列とGUIDを保持し、ビルド後に戻します。復元記録は `Library/FKBuildMetadata/restore.json` です。

失敗したビルドの終了後やEditorの次回起動時にも復元します。生成ファイルまたは.metaがビルド中に手動変更されていた場合は上書きせず、記録を残して警告します。Consoleに出たパスを確認し、元のバックアップと変更内容を保管してから **Fantasy Kingdom → 長期運用 → 中断したビルドの情報アセットを復元** を実行してください。

復元が停止しているときにLibraryを先に削除すると、元ファイルのバックアップも失われます。復元か退避を済ませてください。生成情報は制作コンテンツとしてカタログへ登録しません。

## 隔離検証

制作中のUnityを終了・再起動せず、Assets、Packages、ProjectSettingsを名前にUnityValidationを含む別フォルダへコピーします。Tests~のハーネスはコピー先Assets/Editorに配置します。

入口は `SceneSmoke.Run`、通常製品版ビルドは `ReleaseValidation.Run` です。テストは検証用製品名・カタログを作るため、製品版検証前に制作プロジェクトのResourcesとProjectSettingsをコピーし直してfixtureを取り除きます。

```powershell
$env:FK_BUILD_COMMIT = (git -C 'E:/fantasykingdom/Unity/fantasy kingdom' rev-parse HEAD).Trim()
$env:FK_BUILD_DIRTY = 'true' # 作業中のソース。未変更のリリースはfalse。
Start-Process -FilePath 'C:/Program Files/Unity/Hub/Editor/6000.3.8f1/Editor/Unity.exe' -ArgumentList '-batchmode -nographics -projectPath "C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/UnityValidation" -executeMethod ReleaseValidation.Run -logFile "C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/long-term-release.log"' -WindowStyle Hidden -Wait
```

Gitを含まないコピーでは元ソースのCommitを `FK_BUILD_COMMIT` で渡します。値は40桁または64桁の16進文字列です。`FK_BUILD_DIRTY` はtrue/falseまたは1/0を指定できます。指定のないGitコピーでは追跡ファイルの差分から判定します。取得できない場合はCommit=unversioned、Dirty=trueと明記します。

同じ識別情報を再生成する場合はUTCのUnix秒を `SOURCE_DATE_EPOCH` に指定します。不正な値は準備時に拒否します。これはメタデータの再現性を提供するもので、Unityの全出力がバイト単位で一致する保証ではありません。

## 保管

製品、FKBuildInfo.json、対象Commitのソースと.meta、3つのバージョン固定ファイル、セーブfixture、テスト・ビルドログをまとめます。Dirty=trueにはCommitだけで復元できない変更が含まれるため、差分も保管してください。タグやブランチの作成・公開は運用担当者が行います。
