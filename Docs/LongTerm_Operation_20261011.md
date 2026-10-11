# 長期運用・保存互換・復旧性の実装報告

2026-10-11 / Unity 6000.3.8f1。仕様書の56項目。長時間AIの先行報告は `AI_LongSession_20261011.md`。

セーブの直接上書き、未来形式の再保存、保存失敗の成功表示、ロード失敗時の新規開始、実seed/性格/同一ターン効果の復元漏れを修正した。正常データと旧形式を保護する変更を優先し、エンジンやパッケージの更新・全面Rewriteは行っていない。

## 1. 基準Commit SHA

開始時のmaster/origin/masterは `d03dc55205830e1336ca4297d43fd36f7795a565`。作業中に別のCommit `edd8b7a2cd848fc554cdfe143e2374bedb2808f4` が作成されている。こちらからgit commit/pushは実行していない。検証製品には後者のSHAとDirty=trueを埋め込んだ。固定enum fixtureは開始時SHAから取得した。

## 2. 現在のArchitecture概要

Turn/State、Faction/Nation/Status、Map/Vision、AI観測/判断/実行、永続DTO、UI/Camera/Input、保存/ビルド境界。詳細は `Architecture.md`。既存のUnity実行アダプターを維持した。

## 3. Game Stateの正本

資源/AP/国家はFactionState/NationState、駒はStatus、ターンはGameContext、地形はMapCreate、視界はVisionGenerator。UI、AI Board、Saveはコピー。各所有者の表をArchitectureへ追加。

## 4. Save Format

UTF-8 JSON。旧Versionを残しSchemaVersion、GameVersion、CreatedVersion、LastSavedVersionを追加。Unity ObjectやCoroutineを保存しない。`Save_Format.md`に形式と復旧方法を記載。

## 5. Save Schema Version

対戦7、通算Profile1。Reflection2、Operation1、ML重み2、ML Buffer/Predictor/Profile1、PlayerModel1は既存番号を維持。追加の製品メタデータは互換の任意フィールド。

## 6. Migration構造

v0→1→2→3→4→5→6→7の独立段階を順に実行し、欠番/段階数を検証。v2→3の既存クリスタル増分を維持。欠損データの補完、旧seed不明の明示、廃棄済み建物shellの除去をv6→7に限定。

## 7. Backup方式

Current / `.previous` / `.bak` の3世代。順に復旧し、理由を返す。未対応未来形式を破損扱いせず保護する。MLの既存 `.bak1..3` も読込/保護する。

## 8. Atomic Save方式

AtomicSaveStorageでtmp作成→Flush(true)→再解析/検証→世代更新→File.Replace。初回File.Move。非対応/失敗時は旧データを維持し、非原子的delete/copy fallbackを使わない。学習保存も同じ境界を使用。

## 9. Golden Save Test

v0〜6の7つの固定JSONでLoad→Migration→Validate→Save→Reloadを検証。歴史的形式の合成fixtureで、実プレイヤーの旧ファイルを取得したものではない。今後の正式リリースから実生成Saveも追加する。

## 10. Persistent Data一覧

個体/定義ID、位置、HP/Lv/経験、効果/CT/シールド、移動・初回スキル・状態無効化・迎撃の使用済みフラグ、維持費未払い、施設稼働状態、資源/AP/国家、ターン/残時間、地形生成値、探索、第三陣営、作戦/反省/探索、長期学習。

## 11. Runtime Data一覧

Transform等の参照、Camera、選択、Undo、Coroutine、候補buffer、Profiler、Mesh/Material/TMP、表示キュー、Scene検索cache。召喚個体のvisual rootもruntimeだけで、JSONへ含めない。

## 12. Stable ID方式

definitionId / ReflectionLifeId / EventId / Skillの明示整数IDを維持。標準の保存文字列はStableSaveKeysに明示登録し、enum.ToStringから切り離した。表示名とIDを分離する。

## 13. 危険なName/Index依存

旧復元のKind/Teamによる初期配置照合、Prefab/Resources/Sceneパス、旧enum数値が残る。互換層として扱い、名称・階層変更にmigration/参照検証を伴わせる。InstanceIDは永続データへ新規追加していない。

## 14. Enum Serialization

main saveの11種類は固定文字列。既存AI/アセットの数値enumは45種類/464値を固定fixtureで検証。並べ替え・再利用を禁止する。既存の数値payloadを一括変換して破壊していない。

## 15. Random管理方式

IRandomSource / AIDeterministicRandom。System.Randomによる既存のown-turn sub-seed規則を維持し、`system-random-v1` を識別する。Unity.Randomの全面置換は行っていない。

## 16. Seed管理

以前はRngSeedに脅威度を保存していた。実seedを公開読取APIから保存し、zeroも正しく扱う。保存した性格/6trait/脅威度をAI構築前に渡す。初期traitも同じseedで生成する。旧実seedは復元できないと明示。

## 17. CoreのUnity依存

純粋な保存キー/保存境界/乱数interfaceを追加。既存の座標、物理、地形、Status依存は残る。完全な独立Coreという報告はしない。

## 18. AIのUnity依存

Board/ExecutorはUnityアダプター。判断クラスへTMP/UI/Cameraを追加していない。Status/Vector3を含むため別エンジン移植には追加の観測DTO化が必要。

## 19. UI依存

UIは表示snapshot/正式定義を読む。Battle/Skillの既存実行通知は残す。隠し日記は読取専用。保存成否/復旧理由/ビルド識別をメニューで確認できる。

## 20. Input依存

PlayerMoveのC/Tab/Zを中央Inputへ移動。左右Shift/キー変更/押下edge/メニュー遮断を実InputSystemで検証。Inspectionも中央pointer snapshotを使用。

## 21. Camera依存

Cameraはビュー状態だけを変更。Ctrl+Spaceの完全復元100回、Scene/Camera差替えの検証を維持。ルールへのカメラ座標の書戻しなし。

## 22. Platform依存

ファイル保存はSystem.IO境界、ルートはpersistentDataPath。Git/ハッシュ/BuildReportはEditor限定。OS DLLやオンラインサービスを新たに本編へ導入していない。

## 23. 外部Package一覧

InputSystem1.18.0、UGUI2.0.0、Timeline1.8.10、VisualScripting1.9.9、CollabProxy2.11.3、MultiplayerCenter1.0.1、DevelopmentFeature1.0.2。モジュール/間接依存はmanifest/lockに固定。Dependency_Policyに一覧/分類。

## 24. 外部Plugin一覧

Gentleland SteampunkUI、Honeti GUI、TMP/日本語フォント等は表示素材。ルール/IDへ依存させず、元素材/GUID/ライセンス記録を維持する。

## 25. Binary Only Plugin

Assets内の外部DLL/SO/A/DYLIBは監査時なし。Unity内蔵のネイティブ機能は別。今後追加するものは入手/代替/OS対応を記録する。

## 26. Package Version固定状況

ProjectVersion、manifest、packages-lockを維持。今回新しいPackageを追加していない。

## 27. Scene依存Game State

Status/Transform/Map/VisionがSceneにあり、Save DTOで再構成する。Sceneだけを唯一の保存にしない。Prefab生成の完全rollbackは未実装。

## 28. Global State

AIConfig、Catalog cache、CombatRegistry、開発用静的UI入口など既存globalを監査。SubsystemRegistration、Scene破棄、明示Disposeを使用。新しい共有mutable Game Stateは作らない。

## 29. Singleton一覧

UnitRegistry、ObjectPool、MatchStats、AchievementSystem、GameMenu/Title/Toast/Manual/ActionLog/FloatingDamage等。既存互換のため維持し、所有者/寿命をArchitectureに記載。

## 30. Static Event一覧

独自の新しいstatic gameplay eventなし。Unity SceneManager/EditorApplicationの購読を明示解除。Input、AP、AI反省はinstance event。

## 31. Event Leak Risk

MoveUndoの匿名AP購読をIDisposableへ変更。10回のハブ生成/破棄でInput/Camera/AP/AI参照を検証。完全な対戦10回の勝敗までを実施したという意味ではない。

## 32. God Class候補

AICommander、AIBoardState、AIOperationManager、WildBossSystem、Status。必要な境界から分離し、行数だけを理由に全体を書き直さない。

## 33. 巨大File候補

上記の大きい本体と各partial。保存ファサードはDataTypes/Collect/Restore/Migration/Validation、操作・表示はInput/Camera/Inspectionへ責務を分ける。

## 34. Save Migration Risk

旧seed・フェーズ・一部地形設定の欠損は復元できない。合成fixtureだけで全実プレイヤーデータを保証しない。旧Custom256超は原本保持/明示的拒否。Save_Formatに限界を記載。

## 35. Stable ID Risk

参照先コンテンツの削除/ID変更、旧numeric enum、初期配置照合が残る。ビルド前の重複/参照/型検証と固定fixtureで保護する。

## 36. Deprecated System

既存HierarchicalMode/division互換APIは保持。Migration/旧Reader/旧固定enumを不要コードとして削除していない。

## 37. Tests一覧

SaveCompatibility、LegacyEnumCompatibility、LongTermContent、LongTermRuntime、AttackCandidatePerformance、BuildingLifetimeRunner、既存SceneSmoke各suite、AILongSessionSoak。

## 38. Save互換Test

126項目。旧7形式、冪等migration、HP0維持、3世代復旧、future保護、I/O失敗、サイズ/数値/ID、ML shape/部分適用防止/訓練統計、各学習保存を確認。

## 39. AI Regression Test

既存の経済、基礎資源、探索397、反省/作戦/高度戦略を含むSceneSmoke成功。攻撃候補は旧実装の固定oracleと747比較が一致。

## 40. Long Session Test

先行の500 own-turn比較に加え、建物500生成/破壊、召喚個体500生成/死亡を別フレームで検証。保持参照0、親の子数/登録数が元へ戻る。通常の対戦500ラウンドと区別する。

## 41. Determinism状況

固定seed/trait/個体IDの先行16行動trace一致。新形式の実seed/own-turn/trait再開と独立乱数oracleを検証。全AI状態・Unity.Random・物理・旧未知seedを含む完全Replay保証はない。

## 42. Build Version管理

製品version0.1を維持。FKBuildInfoのread-only DTOを製品内TextAssetとsidecarへ保存し、メニューにも表示。SemanticVersion/Release管理手順はBuild_Manual。

## 43. Git Commit埋込

40/64文字SHA、Dirty状態を記録。隔離コピーはFK_BUILD_COMMIT/FK_BUILD_DIRTYで元コードを指定。RuntimeはGitを起動しない。

## 44. Unity Version管理

6000.3.8f1 / revision1c7db571dde0。ProjectVersionと同梱metadataへ記録。更新していない。

## 45. Documentation追加

Architecture、Save_Format、Stable_ID_Policy、Dependency_Policy、Build_Manual、Upgrade_Checklist、本報告、先行性能報告/グラフ/CSV。制作入口READMEも更新。

## 46. ADR一覧

001 JSON/atomic世代、002正本/checkpoint、003固定ID、004乱数/入力境界、005オフライン、006ビルド識別/検証。

## 47. Subsystem README一覧

SaveLoad、Enemy、Common、_FantasyKingdomの制作入口。入口/正本/依存/永続/寿命/テストの参照を記載。

## 48. 今回変更したFile

SaveSystem各partial、GameGenerator/SystemInitializer/MapInitializer/SaveGameApplier、AICommander/乱数/学習保存/ML、GameContext/Input/PlayerMove/Inspection、GameMenu、MoveUndo、Actor/建物寿命、攻撃候補、関連回帰。FacilityCatalogの存在しないGUID1件を除去（原本をworkspaceへ保管）。同時に編集されたユーザーの制作値/フォントは保持。

## 49. 新規File

AtomicSaveStorage、SaveSystem.Validation、SaveCheckpoint、StableSaveKeys、IRandomSource、FKBuildInfo、LongTermContentValidation、FKBuildMetadata、各新規Testsと固定JSON、各文書。先行Performance/Camera/Diary追加は別報告。

## 50. Compatibilityへ影響した変更

schema6→7の明示migration。旧Version、ID、enum数値、Prefab/Scene GUIDを維持。未来形式は安全に拒否。対応外の敵ターン保存をプレイヤー再開と偽らず理由を表示。壊れたロードから無言の新規ゲームへ進まない。

## 51. 未解決Risk

完全headless/別エンジンCore、完全transactional apply、プラットフォーム横断Replay、全実リリースの本物golden、長時間ML/最大編成の実戦負荷は追加検証が必要。未来のハード故障まで保証しない。

## 52. 10年運用上のHIGH Risk

Unity/asset形式/Shader/描画素材の寿命、未保存だった旧情報、旧numeric enum、mutable global、巨大クラス、実機別の性能差。依存記録と小さい更新単位で管理する。

## 53. 10年運用上のCRITICAL Risk

ID再利用、旧Migration削除、fixture書換、future dataのdowngrade、正式Save直接上書き。禁止方針と自動検証/原子的保存を追加した。

## 54. 今後推奨する改善

リリース毎の実生成golden/製品/Editor/SDK保管、CIで同じsuite、旧版修正用branch、ルール/観測DTOの小規模な独立化、最大編成/ML実戦の性能基準。今回無断でPR/Release/Tagを公開していない。

## 55. Compile結果

Editor compile成功。通常Windows build成功、errors0 / warnings0 / 13.5秒。メインEditorを止めず隔離UnityValidationで検証。

## 56. Test結果

`work/longterm-final-regression3.log`でSceneSmoke ALL PASSED。Save126、Content39、Legacy enum464、Runtime148、攻撃候補747一致。攻撃候補200回の同条件計測:旧3076.668ms→40.741ms（169actor/1071cell）。候補数/範囲/順序を削っていない。`work/actor-lifetime.log`で建物500+召喚個体500の寿命検証成功。製品は `work/ReleaseValidationBuild/FantasyKingdom.exe`、ビルドログは `work/longterm-release.log`。数値は隔離Editorの測定で全PC/全局面の保証ではない。

追加依頼のスタジオ反映・住宅投資・敵拠点攻略の仕様は、この長期運用の基盤の上へ実装する。新しい判断機能は先行最適化の行動一致試験と分けて検証する。
