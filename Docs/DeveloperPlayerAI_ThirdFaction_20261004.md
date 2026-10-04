# 開発者用プレイヤーAIと第三陣営Director

更新日: 2026-10-04。第三陣営の判断規則は「Fantasy_Kingdom_第三陣営AI_Codex実装仕様書_R1.md」に対応。

## プレイヤーAIの使い方

Unity Editor、または **Development Build** でゲームを開始し、**F8**を押すとプレイヤーの自動操作を切り替えます。起動・ロード直後はOFFです。通常の製品ビルドではキー処理と `DeveloperPlayerAIController` 自体がコンパイルから除外されます。

- 脅威度15、Intellectの既存 `AICommander` をプレイヤー視点で使用します。
- プレイヤーのAP・資材・ユニット・視界で判断します。駒の陣営を付け替えません。
- プレイヤーの手番だけ操作します。敵・第三陣営の手番では待機します。
- 自動操作中もカメラを操作できます。手動の建築・召喚入力は停止します。
- F8でOFFにすると現在のAPと残り時間を保って手動操作へ戻ります。
- メニューを開いた間はAIの待ち時間上限も停止します。
- 思考をフレームに分け、探索1区切り3ms、1手番の思考予算3000ms、動作上限12秒で管理します。完了・上限到達時は通常のターン終了処理を使います。
- AI内部で例外が発生した場合は自動操作をOFFにし、手動操作へ戻します。

キーは `TurnGenerator` の `Player AI Toggle Key` から変更できます。標準キーを変えた場合、開発者ヒントのF8表記は `DeveloperPlayerAIController` の `Hints` も合わせて変更してください。

自動操作を一度でも使った試合は開発テストとして保存され、勝敗・脅威度進行・試合終了実績・敵AIの対戦結果学習に反映しません。ロードして手動に戻ってもこの印は残ります。プレイヤーAI用の学習プロファイルやML接続は作成しません。

主な実装: `Assets/Script/Gamesystem/TurnSystem/DeveloperPlayerAIController.cs`、`TurnGenerator.cs`、`Enemy/AICommander.cs`。陣営別の盤面・シミュレーション変換は既存AI側で共有します。

## ターンの順番

**プレイヤー → 敵の全行動終了 → 第三陣営 → 既存の強敵 → プレイヤー**。

第三陣営は `IndependentFactionState` でDirectorと魔物・乱入者の通常行動を進めます。既存の野生強敵は続く `WildBossState` で動きます。第三陣営の処理もフレームに分割し、異常や上限到達で次の手番へ進める構造です。

## APとIP

| ポイント | 用途 | 回復・上限 |
| --- | --- | --- |
| Monster / Intruder AP | 駒の移動・攻撃・スキル | それぞれ既存のAPDataで管理。手番開始時に全回復 |
| Director IP | イベントのみ | 手番開始時+5、最大50、初期15 |

APとIPの台帳は独立しています。Directorのイベントで駒のAPは減らず、通常行動でIPは減りません。IP50でもイベントを強制しません。

イベントは安全検証 → IP予約 → 非表示の生成準備 → 確定の順に実行します。失敗時は生成物・目標・占有情報を巻き戻し、未確定IPを返します。後片付けに例外が出てもIP予約を解放します。

## Inspectorで調整する場所

`Assets/Resources/AI/ThirdFaction/DirectorConfig.asset` が自動ロードされます。Unityメニュー **Fantasy Kingdom → Third Faction → Edit Config** から選択できます。

設定項目は履歴期間、予測期間、戦争・停滞判定、優位を削る最大割合、King/Crystalからの出現距離、イベント間隔、候補上限、処理予算などです。IP最大50と毎手番+5は仕様上の固定値です。

同じフォルダのEvent Definitionは初期設定として次の5種を登録しています。IPコスト・脅威度・間隔・Prefab・Stats・報酬はInspectorで変更できます。

| アセット | 目的 | 初期IPコスト |
| --- | --- | ---: |
| FrontierOpportunity | 争奪可能な討伐報酬を持つ乱入者 | 20 |
| DungeonRaid | ダンジョン付近を襲う魔物 | 25 |
| TerritoryRaid | 領域付近の襲撃 | 25 |
| StrongEnemyExpansion | 既存強敵の局所的な領土拡大 | 40 |
| SummonStrongEnemy | 対応猶予を持つ強敵の召喚 | 45 |

この数値は調整用の初期設定です。強敵の戦力が盤面に対して大きすぎれば、安全判定で召喚を拒否します。領土拡大は1回の半径増加と、試合内の累積拡大量の両方を制限します。

イベント追加はProjectの **Create → Fantasy Kingdom → Third Faction → Event Definition** で作成し、Configの `Events` に登録します。

1. 永続的かつ重複しない `EventId` を付けます。セーブ後は変更しないでください。
2. `EncounterId` を既存 `R1ContentCatalog` に結び付けるか、`Prefab` と `Stats` を直接指定します。
3. IPコスト、脅威度範囲、Minor/Medium/Major/Scenario、個別・大イベント共通Cooldownを指定します。
4. 出現方針・対象陣営・Objective・有効ターンを指定します。
5. 原則 `Can Act Immediately` はOFFにして、両陣営に対応する手番を与えます。
6. 実際のPrefab・随伴駒の戦力を含む安全判定と、Director Debugで選択理由を確認します。

既存カタログの魔物は初期の世界配置として扱います。以後の増援・乱入はDirectorイベント経由です。以前の「FirstRoundになれば無条件に乱入」方式は使いません。`FirstRound` はイベントの出現可能になる時期です。随伴PrefabにはStatusと対応するUnitDataを設定してください。Statusが子オブジェクトでも、実際の駒の位置を検証済みセルに合わせます。

## Directorの判断と戦術

Directorは手番に1度だけ全盤面のSnapshotを作ります。軍事力にはHP・攻防・レベル・射程や役割・行動不能状態を含め、経済・領土・成長・直近の戦闘・勝利対象の損傷も確認します。候補ごとに全GameObjectを収集しません。

活発な互角戦争や決着直前は原則静観します。主力の接近、同じダンジョンへの集結、King/Crystalへの接近を予測した場合はIPを温存します。予測した衝突が実現しなければ温存を解除し、停滞を再評価します。勝っている側を罰する評価項目はありません。

候補は脅威度・IP・Cooldown・出現可否・予測戦力変化・優位の反転や過度な消失を検証してから点数を付けます。不正出現やKing/Crystal隣接出現を拒否します。候補が不要・低価値・危険、または処理時間超過なら `NoEvent` です。1手番にイベントは最大1件です。

Directorは有効期限付きの高レベルObjectiveを渡します。盤上の駒は共有の移動パターン・地形・射程・LoS・攻撃/スキル・AP処理で行動します。局所視界から危険を評価し、撃破可能な対象、撤退、目標への移動を選びます。直接近づけない場合は、ノード数と時間を制限した経路探索で回り込みます。長い経路を全探索する方式ではありません。

戦争予測とイベント影響は軽量な見積もりです。将来の全戦闘を保証する完全シミュレーションではないため、Prefabや報酬を追加した際は出現距離・戦力閾値も実際の対戦で調整してください。

## セーブと調査

IP、両AP、Cooldown、反復履歴、乱数状態、予測と開始時期、Objective、活動中イベント、安定Actor ID、出現後の行動可能ターン、領土拡大基準、召喚強敵の専用情報を保存します。旧セーブで項目がない場合は安全な初期値を使います。

Unityメニュー **Fantasy Kingdom → Third Faction → Director Debug** で、Play中のIP増減、Mode、両軍と第三陣営の戦力、戦争・停滞・予測、NoEvent理由、候補のIP/点数/拒否理由、活動中のObjectiveを確認できます。

EditorとDevelopment BuildのConsoleには手番ごとの `[ThirdFaction]` と候補ごとの `[ThirdFactionCandidate]` を出します。製品ビルドではテレメトリー文字列の生成と呼び出しを除外します。

## 検証の再実行

検証用ソースは `Tests~` に置き、製品のUnityインポートから除外しています。既存方式に合わせたEditMode / PlayModeのバッチハーネスです。NUnit Test Runner形式ではありません。

隔離したUnityValidationプロジェクトへScript・Resources・Editor変更を同期し、`Tests~` の検証ソースをその `Assets/Editor` へコピーして実行します。日常のUnity編集プロジェクトにバッチ起動を重ねないでください。

- `SceneSmoke.Run`: 既存回帰に加え、IP予約/返却、AP分離、戦争保護、予測温存、Cooldown、出現、安全判定、経路、スキル、保存と乱数再現を確認。
- `PlayerAutoplayRunner.Run`: 開発者モード切り替え、手動復帰、メニュー停止、例外復帰、探索視点、複数の実フレーム自動手番を確認。
- `TurnCycleRunner.Run`: 手動終了・時間切れ・異常時の復帰、および6回連続で敵終了後に第三陣営が動く順番を確認。
- `ReleaseValidation.Run`: 通常Windowsビルドの成功と、生成Assemblyから開発者操作が除外されることを確認。

実行結果は `Tests~/PlayerThirdFactionResults.txt` を参照してください。
