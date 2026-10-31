# A1 AI統合・工兵本番データ・実行検証（2026-10-31）

工兵の本番データを登録し、鉄不足から軍隊を再建できない循環を修正した。敵本陣の観測／記憶、住宅とAP需要、実能力による役割分担、限定された連携手順・複数手撤退も既存の指揮系統へ接続した。**A1全体は未完。通常対戦での勝利、工兵の専用護衛、前衛交代による援護撤退は未達／未検証として残す。**

## 前提と変更範囲

- 基点：`c24862e277399e05036b81ca25c122c780a1f418`。PR #176のR2を既存統合ブランチへ取り込んだコミットで、R2の`61fdf74`と同じツリー。旧PR #175の既存指揮・観測・保存系を再利用した。
- 変更後のコード：`e5fa1d636b05cd5e110214e8b48c69cb8808efb1`。通常対戦に使用したruntime入力SHAは`8fa8ab4324d5e2988b7f3487ca525a09ff6ae50d7cab68172f007a7017e53ddd`、ゲームデータSHAは`8dc72e3f5a113c7563c88acdbe04ea44a26feaed4081e94ca986d88e5d2297a4`。コード確定後の記録追記とmeta空白整形は動作変更に含めない。
- 作業ブランチ：`codex/ai-a1-strategy-coordination`。調査時のmasterは`a830495894eb444eee7255be0c32e9ef39c82802`。masterへ直接統合した意味ではない。
- Unity：6000.3.8f1。ユーザーの開いている元プロジェクトを操作せず、分離コピー`UnityValidation`で実行した。
- 正式なAP最大値60、実マップ35×35を維持。仕様書HS04の「15」と目標40×40は食い違いとして記録し、勝手にルールを変更していない。
- R2の通常兵固定能力1.75倍、固定維持費5倍、成長／XP廃止、兵舎XP廃止、ゲーム時計廃止、正式バリア／未払い損傷を維持。施設Lv・脅威度・旧セーブ互換フィールドは別扱い。
- 工兵の数値は、2026-10-31の「暫定値の作成と実戦調整を任せる」という回答に基づく調整可能な初期案。

## 工兵の初期案と調整場所

|項目|設定|
|---|---|
|識別子|`r2.unit.engineer`、Player／Enemy両側の共通カタログへ登録|
|基礎値→実効値|HP24→42、ATK0、DEF0、バリア6|
|移動／攻撃／視界|周囲8方向へ1マス／通常攻撃なし／本人中心5×5|
|制作費|木材20・石材10・水5・パン5・市民1・AP3、鉄／魔石0|
|維持費|定義はパン1。既存の全兵追加維持費と5倍規則を適用後、木材5・石材5・水5・パン5／自軍ターン|
|見た目|既存Scoutプレハブを暫定利用。工兵専用の新モデル完成とは扱わない|

編集対象：`Assets/_FantasyKingdom/GameContent/R2Production/Units/Engineer/Stats.asset`と`ActionTiles.asset`。資源・市民・AP・維持費はStats、移動／攻撃／視界タイルはActionTilesで調整できる。`UnitCatalog.asset`に登録済みなのでインストーラの実行は通常不要。

Unityメニュー「Fantasy Kingdom → 開発 → 工兵の暫定データを登録／工兵データを検証」を追加。登録処理はAssetDatabaseを使用し、再実行でInspector上の数値・タイル調整を上書きしない。専用画面の作り直しではなく既存の制作データ経路へ接続した。

## 修正内容と理由

|領域|主なファイル|変更と理由|
|---|---|---|
|本陣の観測と記憶|`AIBoardState.MainCrystal.cs`、`AIBoardState.cs`|Unknown／TerritoryLead／Observed／LastKnown／Disproved／Destroyedを区別。実際の主クリスタルを公式視界で観測したときだけ現座標・HPを公開。視界外は記憶座標とHP不明、旧位置の再観測で失効。未使用の真座標getterを除去。|
|重点偵察|`EnemyTerritoryIntel.Recon.cs`、`AIExplorationPlanner.cs`、`ExplorationAISettings.cs`|主力のwaypointがあるだけで偵察方針が消える経路を除去。観測した敵領土の周辺に重点を移す。知らない隣接マスは探索方向であって敵実体とは扱わない。目標の小変化で毎回任務を捨てることを抑止。|
|住宅／AP|`AIStrategicGovernor.Housing.cs`、`AIHousingConfig.cs`、`AIBuildPlanner.cs`|実際の合法な予定行動と正式APリセット式から必要市民容量を逆算。重複した軍備計画を足し合わせず、容量不足と回復待ちを分離。通常・補助・強化・フォールバックの判定を共有。|
|実能力と部隊|`AICapabilityRoles.cs`、`AIRoleAssigner.cs`、operation `.Squads.cs`|移動／攻撃／視界タイルと実際の回復能力から役割を判断。回復のないPriestを回復役と見なさない。工兵を攻撃隊へ混ぜず、王を通常前衛にしない。異なる射程の位置取りを区別。|
|連携と側面候補|operation `.Coordination.cs`、`.Preparation.cs`、`AIBoardState.Coordination.cs`|固定3方向の包囲を既存部隊管理へ移管。公式候補の準備移動／向き変更と、その後の攻撃・帰路APを限定的に評価。無効／重複デバフ、使えないDEF低下、同じ駒の重複攻撃を除外。対象・視界・AP・疲労の変化で古い列を破棄。|
|複数手撤退|`AIRetreatRoutePlanner.cs`、governor `.UnitRecovery.cs`、preservation config/state、`APSystem.cs`|既知の合法セルで現在と次の自軍APを分け、有限のバリア・観測反撃を引き継いだ帰路を探索。最初の移動先を重複予約しない。正式な移動／攻撃費の純粋な見積り関数を抽出し、ゲーム費用は変更しない。|
|供給再建|`AIActionGenerator.Engineer.cs`、governor `.Recruitment.cs`|生存兵だけを見た鉄需要では全滅後に供給需要も消える。実際の最大3件の補充計画から次の購入／固定維持費も確認する。計画完成後に工兵を検討し、上限いっぱいなら末尾の補充を延期。経済復旧中も、正式な費用・維持費・予備資源を満たし損失を増やさない工兵を許可。|
|記録|`AIR2PlaytestTelemetry.cs`、`AIClosingPlaytest.cs`、新テスト|予定AP／実AP／本陣知識段階を既存対戦記録へ追加。R4無効／Shadow／有効の試験設定を明記。観測処理から再計画や候補生成を呼び直さない。|

新しい独立司令官や、正式コマンドを迂回する行動実行系は作っていない。すべて既存governor・operation・exploration ownerと正式executorを通る。既存列挙番号と保存経路を維持し、追加回復状態は旧保存の省略値を扱う。

## 実行検証と限界

RoslynのRuntime／Editor／Releaseと、ネイティブEditorでの契約テストを区別した。Play Modeでは実際のSampleSceneの「はじめから」を押し、20フレーム以上進行した後に試験した。通常対戦では実際の開発者Player AIを有効化し、相手の脅威度・シード・R4モードだけを比較条件にした。AP・開始資源・視界・時間倍率を試合中に水増ししていない。

- [ネイティブcore](Validation/AI_A1_20261031/core-native.txt)：能力判定、部隊連携の無効化条件、AP費用、住宅計算、有限撤退経路。
- [Play Mode契約](Validation/AI_A1_20261031/live-native.txt)：両陣営の本陣観測、住宅の正式建築・強化、工兵建築の正式支払い・採掘予測、全軍喪失後の工兵再建。
- [R2回帰](Validation/AI_A1_20261031/r2-core-native.txt)：固定兵・成長廃止・保存／バリア／時計の既存規則。
- 途中で不正な新meta GUID、回復能力試験データの未設定クールダウン、単独観測fixtureの依存注入漏れ、試験APの固定値前提などを修正した。途中の失敗ログは作業フォルダに保存し、最終成功ログと混同していない。
- 連携テストの一部は正式ターゲットの証拠を注入した制御盤面。ヘルパーの合格を通常対戦での高度な連携成功や勝率の証明にしていない。
- 勝利試験を実施した場合も「HP1の既知主クリスタルに届く制御盤面」と通常の自動対戦を区別する。決着まで自然に進軍できた意味ではない。
- 制御勝利盤面では初期Knightの正式な無料攻撃権により見積り0AP・実消費0APだった。無料権も変更していない。このケースだけで有料攻撃の支払いを検証したとは扱わず、別の正式費用テストを併記する。

<!-- METRICS_START -->
|条件／Seed|終了R・理由|工兵 P/E|工兵の鉱山 P/E|戦闘兵補充 P/E|通常攻撃 P/E|勝利対象攻撃 P/E|
|---|---|---|---|---|---|---|
|default-t15 / 161803|114・wall_limit|3/1|7/6|14/6|126/87|0/0|
|default-t15 / 271828|120・owner_turn_limit|3/2|4/7|1/7|8/49|0/0|
|low-t3 / 161803|60・owner_turn_limit|1/1|2/1|0/0|0/0|0/0|
|low-t3 / 271828|60・owner_turn_limit|3/1|4/2|2/1|0/18|0/0|
|high-t35 / 161803|60・owner_turn_limit|1/1|2/0|1/0|53/3|0/0|
|high-t35 / 271828|60・owner_turn_limit|3/2|4/5|2/1|0/15|0/0|
|shadow-t15 / 161803|60・owner_turn_limit|1/1|3/3|3/2|18/24|0/0|
|shadow-t15 / 271828|60・owner_turn_limit|1/2|3/4|0/2|0/5|0/0|
|enabled-t15 / 161803|60・owner_turn_limit|2/1|4/3|1/2|20/9|0/0|
|enabled-t15 / 271828|60・owner_turn_limit|1/2|5/6|1/1|4/0|0/0|

P=開発者Player AI、E=敵AI。通常対戦10試合はすべて勝敗未確定。時間上限は検証ハーネスの上限であり、ゲーム時計の復活ではない。

全試合のError/Exception/Assertion集計は0。各試合P95フレームは5.80〜15.15ms、最長フレームは最大1008.32ms、終了時Unity割当メモリは199.4〜217.6MB。専用製品ビルドの値ではない。

高脅威35・Seed161803の敵側は工兵を作ったが鉱山0・終了時鉄0が残る。工兵の登録だけで全条件の供給停滞が解消したとは判断しない。

80項目の判定は{'total': 80, 'PASS': 27, 'NOT RUN': 53}。制御された勝利盤面の成功は自然な自動対戦の勝利に含めない。
<!-- METRICS_END -->

同じマップ／AI乱数シードでも、実時間の探索予算・LifeIDによる順序等があるため完全な同一行動再現は保証していない。少数試合からR4の優劣や勝率は結論しない。各対戦フォルダに実際にコンパイルしたruntime SHA、ゲームデータSHA、R4フラグ、終了理由を保存した。過去の工兵未登録試合は別フォルダで保存し、新しいデータによる結果として上書きしていない。

### 再現方法

1. 分離したUnityプロジェクトを`UnityValidation`というフォルダ名で用意し、このブランチのAssets／Packages／ProjectSettingsを配置する。`Tests~/*.cs`を分離コピーの`Assets/Editor`へコピーする。試験は設定・駒の配置を制御するため、元プロジェクトや実際のユーザー保存先で実行しない。
2. Unity6000.3.8f1で`A1CoreContracts.Run`、`R2CoreContracts.Run`、`CoreLogicTests.RunAll`をEditor実行する。例：`unity run <分離コピー> --editor-version 6000.3.8f1 -- -executeMethod A1CoreContracts.Run`。
3. `A1LiveValidation.Run`はEditor Play Modeを開始し、通常シーンの「はじめから」と20フレーム以上の進行後に契約を検証して自分で終了する。`-batchmode -executeMethod A1LiveValidation.Run`で起動し、即時終了させる`-quit`は付けない。`unity run`も自動quitを付けるので、この非同期Play Mode試験には直接Editor起動を使う。
4. 通常自動対戦は同様に`AIClosingPlaytest.Run`を直接起動。環境変数`FK_CLOSING_SEEDS=161803,271828`、`FK_CLOSING_THREAT=3/15/35`、`FK_CLOSING_OWNER_TURNS=120`（15の長期ケースは240）、`FK_CLOSING_WALL_SECONDS=90`（長期は150）、`FK_CLOSING_R4_MODE=default/shadow/enabled`、`FK_CLOSING_OUTPUT=<新しい出力先>`を指定する。各陣営の行動機会2回で概ね1ラウンド。通常シーン・開発者Player AI15・初期資源等はそのまま使う。
5. `A1VictoryValidation.Run`は通常盤面とは別の終局用試験。初期の戦闘兵を合法な射撃位置へ置き、敵主クリスタルをHP1／バリア0にしてから実際のPlayer AIに選択・攻撃・終了判定させる。出力は通常試合の成績に混ぜない。

生の候補／駒時系列は各対戦の`decision-detail.zip`へ、行動・経済・目的・拒否理由は同じフォルダのCSVへ保存した。Editor全生ログの作業コピーと、PR添付のチェック行抜粋を区別している。

## 未解決・次に必要な修正と検証

|優先度|問題と確認範囲|再現・証拠／頻度|対応と副作用|
|---|---|---|---|
|高|通常対戦で王／メインクリスタルへの攻撃・勝利を未達。本陣を観測しても攻撃候補がない時期がある。|同梱の2Seed対戦、`objectives-by-turn.csv`と`decisions.csv`。観測回数は意思決定サンプルであり別個の攻撃機会ではない。|戦闘兵の実際の到達／射程／占有／補給と任務変更を絞り込む。安全基準を一括撤廃すると王の自殺や無益な突撃を招くため実施していない。|
|高|工兵の専用護衛・採掘拠点防衛の部隊任務は未完成。工兵自身を攻撃隊から外しただけでは護衛の実装にならない。|現在のoperation配置・実行記録。RL10は未検証扱い。|同じ部隊ownerで合法な護衛／脅威排除を追加し、攻撃隊と二重配属しない。護衛を固定しすぎると全軍が進軍しなくなる。|
|中|序盤の全資源供給と鉄需要の復旧に時間がかかり、初期兵の維持費死亡が残る。|資源時系列、工兵の初回生産／鉱山／次の戦闘兵の生産ターン。|工兵の数値だけでなく資源投資順・移動／建築までの資金確保を評価する。5倍維持費や開始資源を勝手に変更していない。|
|中|前衛交代による後衛保護、洞窟を含む退路、複数ターンの高度な陽動などは未完成／未実戦確認。|RT04等の80項目台帳。|専用ケースを追加し、実際の回復と再出撃までを確認。将来の敵行動や未実行回復を確定値にすると危険な退路を安全扱いしてしまう。|
|中|P95だけでは分からない長いフレームがある。|各match.jsonのWorstFrameMsとP95FrameMs。Editor・記録・画面取得も含む測定。|原因をプロファイラーで分解してから対象処理を分割する。P95が短いことをフリーズ解消や製品版性能保証として扱わない。|
|低|工兵は既存Scoutの外観を利用。|Stats.assetのprefab参照。|専用モデルを後で差し替えられる。既存モデルを変更し他兵の外見を壊さない。|

[受入80項目の一覧](Validation/AI_A1_20261031/acceptance-ledger.md)と[機械可読台帳](Validation/AI_A1_20261031/acceptance-ledger.json)にPASS／FAIL／NOT RUN、証拠と不足範囲を記載した。ゲーム側R2の歴史的166項目台帳は書き換えていない。

[変更ファイル一覧](Validation/AI_A1_20261031/changed-files.json)、[試合集計](Validation/AI_A1_20261031/match-metrics.json)、[証拠SHA256](Validation/AI_A1_20261031/evidence-sha256.json)。採用判断のため、未達を残したドラフトPRとして提出する。
