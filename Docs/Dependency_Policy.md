# 依存関係とオフライン運用

更新日: 2026-10-11。バージョンはこのリポジトリの設定から取得した値です。

| 依存 | 現在のバージョン | 主な用途・境界 |
| --- | --- | --- |
| Unity Editor | 6000.3.8f1 / revision 1c7db571dde0 | エンジン、アセット、描画、物理、JSON、ビルド |
| Input System | 1.18.0 | TurnInputHandlerとUI入力。ルールへキー名を渡さない |
| UGUI | 2.0.0 | 表示と操作画面 |
| Timeline | 1.8.10 | Unityの演出用パッケージ |
| Visual Scripting | 1.9.9 | Unity制作環境 |
| Collab Proxy | 2.11.3 | 制作環境のVCS連携 |
| Multiplayer Center | 1.0.1 | 制作環境。本編の必須通信先ではない |
| Development feature | 1.0.2 | IDE、テスト、Profilerなどの制作環境 |

直接依存は `Packages/manifest.json`、解決済みの間接依存は `Packages/packages-lock.json`、Editorは `ProjectSettings/ProjectVersion.txt` を正とします。3ファイルを合わせて保存してください。Unityモジュールもmanifestに固定されています。今回の改修ではパッケージを追加・更新・削除していません。

開発機能の間接依存には Test Framework 1.6.0、Profile Analyzer 1.3.1、Code Coverage 1.3.0、Editor Coroutines 1.0.1、IDE連携が含まれます。既存の検証はTests~の隔離Editorハーネスを使います。テストのために製品コードへ新しい外部パッケージを導入する必要はありません。

## 表示アセット

GentlelandのSteampunkUI、HonetiのGUI、TextMesh Pro／日本語フォントは表示を担当します。更新でPrefabやGUID参照を壊さないため、元の配置と.metaを維持してください。フォントライセンスなど、アセット内の文書も保存します。購入アセットの配布条件は購入時の記録と提供元の条件を別途保管してください。

この監査ではAssets内にDLL、SO、A、DYLIB形式の外部バイナリプラグインは見つかりませんでした。Unity自体が使う内部ネイティブ機能は対象外です。追加する外部バイナリには用途、所有者、対応OS、入手元、バージョン、置換方法を記録してください。

## 実行時と制作時

本編・敵AI・学習データの読み書きはローカルで動作します。追加したビルド情報も製品に同梱したTextAssetを読みます。Gitとpackages-lockのハッシュ計算はEditorのビルド処理に限定しています。外部APIへの接続待ちをゲーム開始・ターン進行・セーブの必須条件にしません。

既存コードは主にAssembly-CSharpで動作し、ルールにもUnityの座標・Status参照が残っています。今回、表示アセットや全ルールを別Assemblyへ移す変更は行っていません。移植では入力・表示・ファイル保存を境界として扱い、検証済みの小さな単位から分離します。

更新は `Docs/Upgrade_Checklist.md` に沿ってコピーで試します。Editor、解決済みパッケージ、アセット、ライセンス記録、製品、ビルド情報、セーブfixture、テスト結果を同じリリース記録へ保管してください。

## 維持・置換の分類

| 分類 | 対象 | 判断 |
| --- | --- | --- |
| Essential / HIGH | Unity、JsonUtility、物理・描画 | 現在の実行/アセット形式に必要。旧Reader、製品、Editor/SDKを保管する |
| Essential / Replaceable | InputSystem、UGUI、TMP | アダプターの境界で交換する。ルールから参照を増やさない |
| Replaceable | Gentleland/Honetiの表示素材 | ルールや保存IDに依存させない。素材を交換してもセーブを変更しない |
| 制作時のみ / 使用状況の確認が必要 | Timeline、VisualScripting、MultiplayerCenter、CollabProxy | 今回未使用と断定して削除しない。参照・ビルド差分を調べてから整理する |
| 制作時のみ | TestFramework、Profiler、IDE連携 | 製品の本編通信条件にしない |

新規のBinary-only/Native pluginは今回なし。今後追加する場合はHIGH扱いで、ソース/置換可能性、入手記録、OS対応、廃止時のfallbackを確認する。
