# 保存形式8と復旧手順

## 場所と所有者

本編の保存場所は `Application.persistentDataPath/saves/`。プラットフォーム依存のパスはこの入口で解決し、ドライブ名をゲーム内へ固定しない。通常Windowsでは `%USERPROFILE%/AppData/LocalLow/<Company>/<Product>/saves/`。実際のCompany/ProductはProjectSettingsを確認する。

`SaveSystem` が対戦スロット3つと通算Profileを所有する。AIのReflection/Operation/ML/PlayerModelは別の学習Profile。学習ファイルが壊れても対戦ファイルを削除しない。日記は表示・診断用で、学習の正本ではない。

## 形式

UTF-8 JSON。`Version` は旧Readerとの互換用に保持し、`SchemaVersion` とともに現在8。`GameVersion`、`CreatedVersion`、`LastSavedVersion` は製品の版で、保存スキーマ番号とは別管理。旧版からのCreatedVersionは `legacy` と記録する。通算ProfileのSchemaVersionは1。AI学習は既存の各Schema/Versionを維持し、追加メタデータは任意フィールドとして扱う。

保存するのは個体Life ID、Definition ID、位置、HP/Lv/経験/効果/クールダウン、同一ターン中に使用済みの4フラグ、資源/AP/国家、ターン/残時間、探索済みマス、地形生成値、第三陣営、作戦/反省/探索DTO。Unity Object、Material、TMP、Coroutine、Camera、Profiler、表示キュー、辞書のInstanceIDは保存しない。

## 再開位置

正式なチェックポイントは `player_turn`。プレイヤーの通常操作中、自動操作停止、建築/召喚の配置終了後に保存する。敵AIの探索途中のIEnumeratorを保存したかのように扱わない。対応外の場合は日本語で理由を表示する。

PlayerStartの再開経路は残AP・残時間・資源・疲労・クールダウン・使用済みフラグを保持し、生産、維持費、開始ボーナスを二重実行しない。選択・表示・移動Undo履歴はruntime情報で、再開時に初期化する。

v0〜6は以前と同じプレイヤーターン再開を互換契約として維持する。旧版には保存した陣営フェーズや実際のAI seedがなかったため、それらを推測して捏造しない。旧RngSeedは脅威度を誤って保存していたので `HasDeterministicSeed=false` とする。

## 読込の順序

`Read bounded bytes → Parse header → Reject future schema → Migration chain → Validate → Apply`。

v0→1→2→3→4→5→6→7→8の各段階を残す。v2→3のクリスタルHP+5000は1回だけ適用し、HP0を復活させない。段階数と欠番を自動検査する。v6→7は欠損値を明示補完し、古いMaxHP=0をHPで補完する。欠損の反省/作戦/探索payloadは新しい経験として作成しない。未知の通常JSONフィールドはReaderが無視できるが、新しいスキーマを古い版で再保存して失うことは禁止する。

未来形式は破損扱いにせず、読込・上書きを拒否する。元のファイルを保持して対応版で開くよう案内する。

## 3世代と原子的保存

| 世代 | パス |
| --- | --- |
| Current | `save_0.json` |
| Previous | `save_0.json.previous` |
| Backup | `save_0.json.bak` |
| 作業中 | `save_0.json.tmp`（読込候補にしない） |

新しいJSONを検証し、同じディレクトリのtmpへ書いて `Flush(true)`。再読込・検証後、以前の正常なPreviousをBackupへ回し、`File.Replace` でCurrentを交換する。初回だけ `File.Move`。置換非対応/権限/共有ロック/ディスク問題があれば失敗して旧データを残す。正式ファイルへのdelete/copy上書きfallbackはない。

Currentが壊れた場合はPrevious、次にBackupから復旧する。復旧元を理由/スロット概要に表示する。未来形式を検出したらそこで停止し、過去の世代へ黙って差し替えない。書込み前にも既存世代を検証し、未来データを保護する。壊れたCurrentの再保存は原本を `.corrupt` に保持し、正常なPreviousを潰さない。

AtomicReplaceは対応する同一ファイルシステムでの交換であり、あらゆる電源故障や物理ディスク障害への保証ではない。ディレクトリごとの外部バックアップも維持する。復旧用コピーを手動で試す前にsaves全体を別の場所へコピーする。

## 検証と上限

対戦64MiB、通算16MiB。MAP1〜256×1〜256、駒エントリ65536、地形/探索座標は範囲内。有限値、非負資源、AP、タイマー、ターン、個体ID重複、既知の固定キー、Definition/Skill参照を検査する。現在形式の不正値を勝手に正常化しない。

カスタム地形のnoise値を以前の `.01..1` clampで変えない。新形式は0 seed、原点クリスタル、縦方向の生成設定も明示して保持する。旧形式で欠落した寸法は凍結した標準35、その他未保存の地形設定は互換fallbackを使う。256を超える旧Customマップは原本を保護して読込不可を説明し、黙って縮小しない。

## 互換テスト

`Tests~/Data/Saves/v0_legacy.json`〜`v7_checkpoint.json` は旧形式を表す合成fixture。実プレイヤーの保存ファイルではない。元ファイルを上書きせず、Load→Migrate→Validate→Save→Reloadを確認する。今後の正式リリースでは、実際のビルドが生成した代表Saveを新しいfixtureとして追加する。古いfixtureを更新して失敗を隠さない。

`Tests~/Data/Compatibility/LegacyEnumValues.json` は基準Commitの45enum/464数値を固定する。main saveの文字列は `StableSaveKeys` の明示表を使う。旧AI/アセットの数値enumは互換テストを残し、並び替えや再利用を禁止する。

ゲーム状態への適用前に検証を完了するが、Unity生成途中のOOMや未知のPrefab実行エラーから全オブジェクトをrollbackする完全なトランザクションは未実装。元の保存ファイルには書き戻さず、初期化失敗を記録して復旧できるようにする。

## v8: バランス編集と獲得済み強化

定義のMaxHP/ATK/DEFを保存時の基準値として記録し、再開時は最新定義へ「実能力−保存時基準」の差分を加える。HP割合、経験、死亡状態、獲得済み強化を維持する。通常メインクリスタルと専用強敵は通常定義で上書きしない。

v0〜7には強化前の基準値が存在しないため、強化と古い設定を分離できない。これらは能力合計をそのまま保持して強化を消さず、新たな保存から基準値を記録する。新規対戦およびv8以降の保存再開は最新設定を使用する。古い履歴を推測して改変しないための互換fallbackである。
