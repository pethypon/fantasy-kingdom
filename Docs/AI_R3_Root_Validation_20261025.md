# AI根本改善R3：改修・再現・実行検証記録

2026-10-25。**制御テストで修正を確認した項目はあるが、通常対戦を含むR3の総合受入は未完了。** 60ラウンドの変更前後3組と、旧版の300ラウンド2試合を実行した。最新の追加修正を含む変更後長期対戦、30試合の難易度別マトリクスは未実行であり、AI品質の総合点・勝率・全面改善を確定しない。

コード解析、観測入力を与えるPlay Mode制御テスト、実経済トランザクション、通常SampleScene自動対戦を区別する。UI画面の視認性・デザイン評価、18職種の全組合せバランス評価は本記録の対象外。

## 1. 対象と版の区別

| 対象 | 識別・状態 |
| --- | --- |
| R3調査基準 | `833c461ba7cf49df47779390d72a135202f1d11f` |
| 根本機能のユーザー統合 | `c70071a`の探索・個体保全・中長期住宅需要を、ユーザーが[PR #171](https://github.com/pethypon/fantasy-kingdom/pull/171)経由で`master`の`7f95f18d91b9888b9c030e0c09259e05a559215e`へ統合 |
| 本記録の追加修正 | `codex/ai-r3-validation-fixes`。市民Critical誤判定、選定召喚の市民費二重予約、保全保存DTOのコピー、緊急防衛ゲート、観測とテストの追加。PR #171にすべて含まれていたとは扱わない |
| 実行環境 | Unity `6000.3.8f1 (1c7db571dde0)`、Windows、隔離`UnityValidation`。通常SampleSceneの「はじめから」コールバックと開発用Player AIを使用 |
| ゲームルール | このAI改修でHP・ATK・AP・費用・市民増加速度・霧・勝敗条件を変更していない。開始前からのCrystal Prefab / SubCrystal / フォント変更を保持 |

Crystal Prefabの表示値と初期化後の実HPは別に記録する。今回の通常対戦のメインクリスタル実HPは初期15000であり、Prefabの5000を実戦HPと誤記しない。主晶のHP増加がある試合は、単純な初期値との差を被害量とは扱わない。

**基準版ラベルの訂正：** 長期Beforeのraw `CodeCommit=7f95f18`は復元先checkout HEADのラベルで、実コンパイル製品は`833c461`。元ログを変更せず、`work/r3-baseline-metadata-correction.json`のソース照合で訂正した。製品ソースと最新版の観測ハーネスは別に識別する。

## 2. 検証データと条件

ローカル証拠の基点は`C:/Users/pethi/Documents/Codex/2026-09-19/ko/work`。以下のログ行はそのファイルの1始まり行番号。ローカルrawログはこの文書に全文同梱していないため、別環境での再監査には該当成果物の受け渡しが必要。

| データ | 実行した範囲 | 判定上の扱い |
| --- | --- | --- |
| `ai-r3baseline-smoke-matches` | seed161803/271828/424242、敵脅威15・Combat、各60R/120所有者ターン、Player AI知能15 | 変更前スモーク3試合、全て上限終了・未決着 |
| `ai-r3after-smoke-matches` | 同じ3シード・脅威・人格・Player AI条件、各60R/120所有者ターン | 根本機能追加後のスモーク3試合。後述の市民・保存DTO・Defender追加修正前 |
| `ai-r3before-final-t1-matches` | 基準版製品833c461、敵脅威1・Combat、Player AI知能15。161803/271828の2試合が各300R/600所有者ターン完走 | 最新版の長期Afterではない。両試合未決着 |
| 同長期バッチの424242 | R96/所有者ターン191で中断 | 完走・勝率・長期比較の母数から除外 |
| `ai-r3regression-fixed-controls.log` | Play Modeの制御・既存回帰26段階 | 25段階PASS、個体保全の新緊急防衛2ケースを含む1段階FAIL。全スイート成功ではない |
| `HousingDevelopment80EconomyTurns` | 両陣営それぞれT20→100、80回の実`EconomySystem.ProcessTurn` | 実経済統合テスト。敵ターン・戦闘の80R対戦ではない |

長期バッチは、新たに実証した製品不具合の高速回帰を優先するため中断した。`r3before-t1-interruption.json`に完走2seed、中断seed424242、計画10seed、raw保存を記録している。「30試合実行済み」「30試合失敗」ではない。

### 検証入力のdigest

全てゲーム入力資産digestは`dc99b9ce65c23f259d6f7d3a476e1d7e2f7d8810617f03ce069a411988d6cdec`。モデル・フォント・画像全体の同一性を保証するdigestではない。

| 実行 | コンパイル製品ソースdigest | 注記 |
| --- | --- | --- |
| 長期Before | `36ec3194a8a0f1bb985e2ff8dd9fffdf9865a9e07e748222d741cec09158bf0b` | 833c461の保存ソースと照合。改行差だけを正規化して比較 |
| 60R Afterスモーク | `b50a6c880a6bd3d47ff132cb8c4f2ce572cef81c7a40fd9068f1bdb9ecd06ca3` | `ai-r3after-smoke-matches-manifest.json` |
| 市民修正後の回帰 | `a6a5e50800e12660d1f776249b88a5c3ea923339fe76dd6cdc083f2182667ba3` | `ai-r3regression-fixed-controls-manifest.json`、17:52採取 |
| Defender修正後の次回帰 | `46cb590f78918255671ca6d783599f36073a421449676de84c2e8db6f2d2fde4` | `ai-r3defender-fixed-controls-manifest.json`、17:54採取。新テストのcast例外が残る |

各manifestにEditorハーネスdigestとコンパイル対象ファイル一覧もある。`sourceMatchesCurrentMain=true`は採取時点の一致であり、その後のobserver emit・評価観測変更を含む現在の全作業ツリーへ遡及して適用しない。

## 3. [Issue ID] P0-A：成果のない偵察往復

**症状・再現：** 旧実戦seed271828、Enemy Scout life ID `f102e86f167f4803aa9186d1eebac8f4`。`ai-r2recovered-matches/02-271828/units.csv`のT18–34でregion3、目標`(12,18)` / `(15,29)`を切替、累計開示199が増えなかった。同一個体の3往復に新規開示0、記録AP256という読取結果。これは通常対戦で観測した個別例。

**原因：** `Exploration/AIExplorationPlanner.cs`の`MaintainObjective`が、前のlegで得た累計開示を新waypointの完了判定にも使用した。`TryContinueRegion`は情報ゼロの目標切替自体で`LastProgressTurn`を更新し、停滞検出を延命した。`history.Clear()`だけを単独原因と断定しない。Recon Withdrawは対象の実行ログに現れず、定義だけの偵察phaseを実行原因にはしていない。

**修正：** legごとの開示基準を分離。実到着して情報がなかったviewpointをactor life ID・region・cellで最大256件保持し、短期移動履歴と分けた。目標切替だけでは停滞時計を進めない。別の既知frontierを比較し、観測状況の変化による正当再訪と安全撤収を残す。全地域を回数だけで恒久禁止しない。

**Before→After証拠：** `ai-r3-expedition-before.log`は6失敗ケース、前提10チェック成功。`ai-r3green-controls.log:9024`では32チェック成功。30所有者ターンの観測入力replayで両陣営とも同viewpointの反復到着0（8396/8752行）。これは制御replayであり、30ターンの通常自動対戦ではない。

**副作用・判定：** 現在合法可視の敵情勢と既知地域だけを参照し、新全権AI・全マップ走査を追加しない。制御テストはPASS。通常3組の短期比較では無情報往復の同症状を十分再現できず、通常対戦での発生率低下とR3総合受入はUNVERIFIED。

## 4. [Issue ID] P0-B：個体の撤退・回復・再合流

**症状・再現：** Lv1/20/50/100 × HP20/35/50/60% × 両陣営の32条件。観測された合法攻撃の損失13/22/31/37は一撃死ではないが、安全な横退避候補が存在した。旧版では撤退P4・攻撃P2によりスコア比較前に退避が脱落。別に、HP35%・被害8・負傷兵1体だけの両陣営2条件も再現。

**証拠：** `ai-r3-preservation-valid-red.log:12090`は前提72チェック成功、34ケース失敗。例は8714/8814/8914行。最初のfixtureの視界外倍率・raw ATKと実効被害の取り違えを是正してからRedを採取した。

**原因・修正：** 全軍再編の「負傷2体以上」では個体損失を扱えず、非致死損耗回避が低優先だった。`AIStrategicGovernor.UnitRecovery.cs`でEmergency / WithdrawalRecommended / HoldPositionOrFightと、Withdrawing / Recovering / Readyを既存Governorへ統合。現在の観測情報から最大2交換分の被害を値スナップショットで評価し、バリア・1回無効・生命接続の有限性を考慮する。未来の敵AP・意思決定を確定予測したものではない。退避候補は最大4を保ち、安全な側方・味方援護・回復経路を比較。合理的な脅威除去攻撃や回復との比較を残す。

**After証拠：** 初期Greenは184チェック・失敗0（`ai-r3green-controls.log:14232`）。保存統合後は213チェック・失敗0（`ai-r3save-longhousing-controls.log:15183`）。実移動でAPを払い、`NaturalRecoverySystem.ProcessActor`の実HP回復後に再出撃する契約を含む。HPを回復値へ直接書き換えて成功扱いにはしていない。

**実戦例と限界：** Afterスモークseed271828のPlayer Archer life ID `292a4eba03574d088e9a54608ce91bd5`はT35にHP11/24でRetreat（`actions.csv:558/559`）。T40→43でHP13→15→17→19、Lv1/MaxHP24を維持し、T42以降Moveを再開した。CSVはHP変化原因を保存していないので、この増加を自然回復ハンドラ起因と断定しない。全候補の退避可能性・戦死原因が不足し、救命率・高Lv戦死率は未算出。

**副作用・判定：** 保全記録は上限128、life IDと陣営で分離、死者・終了時に解放。非致死制御と保存契約はPASSだが、後述の緊急Defenderゲートを追加Redで発見した。P0-B全体と通常対戦受入はPASS未確定。

### [Issue ID] P0-B-SAVE：保全保存DTOの参照共有

**症状・原因：** `DeveloperPlayerAIController`のpending stateと`TurnGenerator.savedPlayerPreservation`が呼出元DTOを参照のまま保持・返却していた。復元後に呼出元のlistを変更すると状態が逆流し得る。これはコードレビューと追加保存制御で追跡したもので、通常プレイのセーブ破損を観測したという主張ではない。

**修正：** `AIUnitPreservationState.CopyForOwner`へ有界の検証・deep copyを集約。owner不一致・未知version・129件以上を拒否し、life ID・座標・phaseを検証。capture/restore双方をコピーし、旧presence flag欠如では両側の状態をクリア、release時にpendingを破棄する。

**テスト：** 実`CollectGameState`→`ParseGame`→敵Restore/Player pendingとactive controller→別Unityインスタンスへの`ApplyStatusFields`でlife IDを戻し、回復継続を確認。保存統合後213チェックPASSの一部。入力DTOを書き換える境界、owner不一致、過大payloadも含む。通常保存全世代・全破損パターンを網羅した証明ではない。

### [Issue ID] P0-B-DEFENDER：緊急防衛中に唯一の救命退避が消える

**症状・再現：** 両陣営の緊急防衛盤面。守備に選ばれた駒が、現在観測された攻撃で致死になる。唯一安全な合法側方退避は防衛地点からの距離が増えるため、`defender_pursuit_limit`で拒否された。

**Before証拠：** `ai-r3regression-fixed-controls.log:12414/15473`で両ケース失敗。段階は候補生成後のGovernor Rejectで、P0救命のPriority比較に届かない。全体は個体保全223チェック・2失敗（15527行）。

**修正：** `AIStrategicGovernor.cs`の守備追跡ゲートで、既存の`IsImmediateRescue`を満たす合法救命を距離だけで拒否しない。`AIStrategicGovernor.Preservation.cs`では、観測された本拠致死脅威を合法に倒して止める攻撃もP0救援とし、守備兵の退避P0によって本拠救命攻撃P1が比較前に排除される矛盾を避ける。通常の無目的追跡・拡張は緊急時に引き続き制限する。

**After状態：** 次回`ai-r3defender-fixed-controls.log:12296/15293`は新テストの`Specified cast is not valid`で失敗。製品判定の成功証明として扱わず、fixture修正後のネイティブ再実行待ち。現時点の判定はUNVERIFIED。

## 5. [Issue ID] P0-C：中長期需要・住宅・実召喚

**症状・原因：** 従来の戦闘員目標2〜6と短期購入最大3件では、先の役割・損耗補充・必要時期に基づく容量要求を保持できない。市民は消費する在庫であり、軍全体の市民費を同時収容する規則ではない。普通の市民費1の連続召喚に住宅が増えないだけでは不具合と判定しない。

**修正：** `AIHousingConfig`の日本語Inspectorへ、準備期間、軍備目標の見直し間隔・追加目標、兵種ごとの必要現存数・時期を追加。既存Recruitmentの最大3件と別に有界の中長期需要を保持し、必要な役割が期限内に入ると容量・維持予測へ接続。住宅の数上限4と定義候補枠16で、実際に必要・安全な住宅を失わないよう1件を保持し、候補枠自体は拡張しない。食糧回復の延期開始・最終進捗・停滞を診断するが、期限だけで危険な住宅を強行しない。

**Before→After：** `ai-r3-housing-before.log:10383`は4チェック成功・8ケース失敗。初期Green40チェックは将来需要のT20/40/60/80/100スナップショット、不要住宅、候補上限、回復後再開、実建築→自然増→召喚を確認した。これは初めから80ターン実経済進行を確認した記録ではない。

**追加実経済テスト：** `ai-r3regression-fixed-controls.log:18839/18856`で88チェック・失敗0。両陣営T20→100に80回の経済処理を通し、実住宅2回で容量5→13→21、自然市民増加46（最大1/ターン）、T60白魔導士・T80Guardian・T90制御損失後の白魔導士補充を標準選択・実行器で購入した。T100は市民15/21・住宅2・召喚3（Player17051、Enemy18692行）。T90のHP0操作は損耗補充を作る制御条件であり、戦闘撃破とは数えない。

**副作用・判定：** 正式費用・APと市民消費を検査し、住宅で市民を即時付与しない。80回すべてで飢餓と駒維持費未払いを確認。制御はPASS。一般対戦での必要住宅局面20機会・長期戦力形成の受入はUNVERIFIED。

### [Issue ID] P0-C-SUPPLY：市民の自然補充待ちで交戦を停止

**再現・Before：** T60、市民0/容量5、食料が1/ターン補充を支え、実視認敵がいる。Scout/Archer/Knightの3購入予定を作ると、`ai-r3supply-detail-controls.log`の診断はcontact=true、EconomyBuild、予約AP2、EconomicRecovery。市民はincome1・planned2.3・reserve3・coverage0.76・blocked1.69・Critical。

**原因：** `AIBasicResourceEconomy.RefreshCitizenSupply`が「今日買えない」3件をblockedへ加算し、予測期間内に自然補充で到達できる事実を反映しなかった。架空Criticalで回復modeに入り、別資源Warningの供給候補へAP予約が割り当てられた。

**修正：** 食料が支える正式な補充と現在の容量の範囲で、期間内到達在庫に基づきblockedだけを再評価。合計計画費・予備・coverage・実際の支払いは維持。食料なし・容量超過・期間不足を無条件に解消しない。

**After証拠：** 同条件の両陣営でContactEngage・予約AP0・StrategicAttack。coverage0.76は保持してblocked0/Shortage、basicCritical=false（`ai-r3regression-fixed-controls.log:24915/24931/25662/25678`）。供給警告を消した成功判定ではない。SupplyReservation70、R2役割補充60、食料復旧28、旧住宅50、基礎資源157件のPASSログとALL PASSEDを確認。制御はPASS、最新長期対戦の結果は未検証。

### [Issue ID] P0-C-RESERVE：選定購入の市民費を二重予約

**再現・Before：** 80回経済テストのT80、Guardian市民費16・実予備3に対し住宅で容量21を用意し、市民21へ補充済み。`ai-r3save-longhousing-controls.log:17768/20341`では両陣営とも候補が戦略召喚へ残らず失敗した。

**原因：** BasicResourceEconomyがその購入費16の半分8を、購入後にも残すべき資本予備とした。住宅必要stock19と、実購入に要求するstock24が不整合になった。

**修正：** Governor短期注文に実登録されたSummonだけ、市民の床を実維持義務のSafetyReserveと設定minimumReserveの最大へ戻す。材料・食料・必須供給資本は保持し、注文外購入は対象外。

**After証拠：** `ai-r3regression-fixed-controls.log:16792/16814/16836/16858/16880`とEnemy18433以降で、選定購入可、無関係Ninja不可、市民残2不可、パン予備割れ不可、木材未払い不可を確認。T80の実支払い・実召喚も通過（16902/16924、18543/18565行）。制御はPASS、備蓄の全面解除はしていない。

## 6. 通常スモーク6試合：観測結果と限界

元集計は`work/AI_R3_smoke_comparison.json` / `.md`。各条件1試行で、時間予算・フレーム順の影響があり、同シードだけで統計的な因果を保証しない。Afterは最新追加修正前の版である。

| seed | 変更前：行動/失敗/攻撃/召喚/建築 | 根本機能After：行動/失敗/攻撃/召喚/建築 | 両版の終了 |
| --- | --- | --- | --- |
| 161803 | 1078 / 5 / 109 / 23 / 92 | 1086 / 5 / 110 / 27 / 95 | 60R・未決着 |
| 271828 | 1105 / 3 / 220 / 38 / 98 | 1068 / 0 / 186 / 27 / 88 | 60R・未決着 |
| 424242 | 1073 / 5 / 141 / 28 / 57 | 1178 / 4 / 108 / 30 / 56 | 60R・未決着 |

両陣営合計。行動は試行数であり、成功数とは別。6試合すべて実Player AI mode利用、native Errors/Exceptions/Assertionsとrecorded faultは0。失敗行動はBefore13/3256、After9/3332だが、1回ずつの3seedで性能向上や失敗率の一般的改善とは断定しない。

- **探索：** 全Scoutの新規既知セル/記録成功APはBefore3467/5951（約0.583）、After3398/6023（約0.564）。seed271828 Playerの個別比は0.537→1.335だが、全体改善を裏付けていない。同地点再訪patternは両版それぞれ1件、解析定義上の0開示往復は両版0。短期ログで症状が出なかったことは長期反復の解消証明ではない。
- **住宅・経済：** 両版ともseed161803/271828は各陣営House1、容量7。424242はHouse0、容量5。Afterで家が量産されたわけではない。全sideで飢餓counter観測0、Bread/Citizenの0観測なし。一方、維持費未払いを観測したラウンドはAfterにもPlayer2/1/1、Enemy0/2/4があり、経済安定を全面合格にしない。
- **回復役：** After Enemyは3seedすべて回復役あり（13/6/22ラウンド）。Playerは3seedすべて回復役あり観測0。Beforeでは424242の両陣営に回復役がいた。役割充足の一般的改善や召喚0の単一原因を、3試合だけで断定しない。
- **本拠：** 両版とも主晶・王の視認ラウンド0、生成本拠候補samples0、実本拠攻撃0。見えている合法本拠攻撃を無視した証拠ではなく、60Rでは発見・接近に届いていない。
- **撤退：** Afterの実Retreatはseedごとに13/13/27、DefenseReposは8/0/4。candidate却下件数は独立救命機会ではなく、救命成功率の母数にしない。個体消失を戦死にしない。
- **性能：** Editor先頭最大20000フレームのP95はBefore9.82/9.18/10.68ms、After12.73/9.59/14.94ms。Peak Editorメモリも両版差があるが、AI単独CPU・製品FPS・リーク・省電力の測定ではない。「軽量化した」とは評価しない。
- **記録：** この6試合のstatusではdetail cap到達なし・上限後drop/skip0。ただしBeforeとAfterでobserverの生成段階・採択枝・内訳列の取り方が違う。候補拒否数を同一母集団の改善率へ変換しない。

## 7. [Issue ID] P1：本拠未発見と攻略進捗

旧版833c461の長期Before2試合は、両方300Rで未決着。敵脅威1と開発者Player AI知能15は対称難易度ではない。全難易度の勝率比較へ転用しない。

| seed / side | 敵領土初認識R | 主晶初視認R / 視認R数 | 本拠候補samples 生成→Governor後→選択前 | 実本拠攻撃 / 与HPダメージ |
| --- | --- | --- | --- | --- |
| 161803 Player | 47 | 96 / 27 | 92→92→92 | 17 / 687 |
| 161803 Enemy | 23 | 未観測 / 0 | 0→0→0 | 0 / 0 |
| 271828 Player | 47 | 212 / 2 | 4→4→4 | 4 / 146 |
| 271828 Enemy | 48 | 未観測 / 0 | 0→0→0 | 0 / 0 |

全て王の視認0で、本拠実攻撃は主晶のみ。候補samplesは再評価を含み、独立した合法攻撃機会ではない。未決着を敗北・勝率0%に変換しない。

**観測した残課題：** Enemyは領土認識後も本拠を一度も視認しなかった。161803のEnemy Scout life ID `d498c0328d45484e82233b9b90ab7070`はR113–162の50snapshotに61実行（Move44ほか）があり新規開示0。全快41/50、記録近似incoming0が45/50で、APが全く割り当てられないだけでは説明できない。ただし近似incomingは合法なGovernor脅威と同じではない。

**原因候補：** 安全な探索進捗P3に`!HasActiveTerritoryAdvance`条件があり、領土進軍中ScoutがP4へ落ちる一方、主力進軍はScoutを除外する結合。静的な候補であり、当該61行動すべての因果を確定していない。本拠通常攻撃を一律昇格する変更は行っていない。低効用の供儀スキルを避けた妥当な未採択も含まれる。

**次の再現：** 既知敵領土・実際に安全なScout・未探索frontier・主力進軍を同時に置き、生成→知覚→Reject→Priority→選択→AP支払いを比較する。最新After長期対戦で初発見・接近・合法候補・実攻撃・勝敗を追う。P1はUNVERIFIED。

**欠損：** 長期Beforeのdecision詳細はseed161803でR114、271828でR107にcap到達。actions・objectives・unitsの記録範囲と分け、詳細がない後半の射程/AP/経路失敗原因を推定で埋めない。最終snapshotのCrystalDefense名称は再試行fallbackで上書きされることがあり、「そのRの全行動が防衛」と解釈しない。

## 8. 保存・公平性・観測の確認範囲

- 探索成果は既知地形・実視認の情報更新だけで作り、観測用の実盤面HP・隠れ敵位置をAIへ戻さない。専用のFog/情報変化制御は通すが、全ゲームの情報漏洩不存在を通常6試合だけで証明しない。
- saveは既存の形式へ任意payloadを追加し、旧欠落をunknown/空へ戻す。到着前目標のID保持と、到着済み無成果目標を別frontierへ進める契約は別に検証する。
- 旧探索保存テストは、保存時に既に目標セルに到着していたのにID保持を要求した。中央の未到達セルへfixtureを是正して前提assertを追加し、ID/target/life ID/別runtimeの照合は維持。到着済みロードも別caseへ追加した。`ai-r3regression-fixed-controls.log:32614`で281チェックPASS。製品動作を旧assertに合わせて戻してはいない。
- score観測は`AISafety / 情報 / 作戦 / 経済 / AP / 反復`等の意味を後付けで数値分解した確定値とせず、既存評価の段階差分を読み取り専用で記録する。`AIR3ScoreObserverTests`は候補とscoreを変えないことを確認した（`ai-r3defender-fixed-controls.log:18731/18748/18765`、両陣営2チェック）。外部品質点とは別。

## 9. 外部品質点：現時点では確定採点しない

R3仕様の旧暫定33/100を最新版の点数として流用しない。制御テスト数や実装量による加点を行わない。下表の「—」は0点でも合格でもなく、機会数と通常対戦での結果が不足したUNVERIFIED。

| 項目 | 配点 | 新規厳格点 | 現在の根拠と不足 |
| --- | ---: | ---: | --- |
| 偵察効率 | 15 | — | 反復Red→制御Greenあり。6スモーク全体の情報/AP改善は確認できず、長距離反復30機会不足 |
| 負傷兵の撤退・再合流 | 15 | — | 制御・実移動/回復/復帰・保存あり。緊急Defender最終回帰と通常救命30機会不足 |
| 住宅・市民 | 15 | — | 88制御チェック・両陣営80経済処理。通常の真の容量不足20機会と最新After長期未確認 |
| 資源・軍拡 | 15 | — | 市民予約不整合を修正。Afterスモークに未払い観測が残り、最新修正による持続効果未確認 |
| 戦闘・集団連携 | 10 | — | 攻撃・生産は実行。集結・孤立・救命との比較の機会母数不足 |
| 勝利対象攻略 | 20 | — | スモークで本拠未視認。旧長期Player21主晶攻撃、Enemy未発見。最新Afterで10攻略機会・勝敗未確認 |
| 観測・再現・データ整合性 | 10 | — | manifest・Red/Green・未完バッチ記録あり。長期decision cap・旧ラベル訂正・版間の観測差が残る |
| **合計** | **100** | **—** | **R3総合受入は未完了** |

## 10. 主な変更ファイルと理由

パスはUnityプロジェクトルートからの相対表記。

| 範囲 | 主なファイル | 理由 |
| --- | --- | --- |
| PR #171の探索根本機能 | `Enemy/Exploration/AIExplorationPlanner*.cs`、`ExplorationModels.cs`、`AIPlanManager.cs` | leg基準・成果なしviewpoint・正当再訪・観測記憶 |
| PR #171の個体保全 | `Enemy/AIStrategicGovernor.UnitRecovery.cs`、`AIUnitPreservationConfig.cs`、`AIUnitPreservationState.cs`、`AICommander.Preservation.cs`、`AIActionGenerator.cs` | 個体別危険・安全候補・回復と復帰・保存寿命 |
| PR #171の住宅需要 | `Enemy/AIHousingConfig.cs`、`AIStrategicGovernor.Development.cs`、`.Recruitment.cs`、`.Housing.cs`、`AIActionGenerator.cs` | authorable将来需要、実容量に対する投資、候補枠維持 |
| 今回の経済追加修正 | `Enemy/AIBasicResourceEconomy.cs` | 自然補充待ちのCritical誤判定、選定購入の市民費二重予約 |
| 今回の保存追加修正 | `AIUnitPreservationState.cs`、`AIStrategicGovernor.UnitRecovery.cs`、`TurnGenerator.cs`、`DeveloperPlayerAIController.cs` | owner検証・deep copy・pendingとcaptureの分離 |
| 今回の救命ゲート修正 | `Enemy/AIStrategicGovernor.cs`、`.Preservation.cs` | 守備距離ゲートとP0救命・本拠救命攻撃の整合 |
| 観測と実行検証 | `Tests~/AIR2LiveValidation.cs`、`AIClosingPlaytest.cs`、`AIR2PlaytestTelemetry.cs`、`AIR3*Tests.cs`、`ExplorationR2IntegrationTests.cs`、`SupplyReservationTests.cs` | 生成段階、Priority、資源、保存、正式実行の証拠と境界 |

`Enemy/`の略記は`Assets/Script/Gamesystem/TurnSystem/Enemy/`を指す。見た目/UIや難易度数値の調整は本改修の根拠に混ぜない。

## 11. 再実行入口と残課題

検証は主プロジェクトを二重起動せず、必要な`Tests~/`をEditorテストとして配置した隔離`UnityValidation`で行う。使用した入口は`AIR2LiveValidation.Run`と`AIClosingPlaytest.Run`。実行済みPowerShellハーネスは`work/run_ai_r2.ps1`。`-OwnerTurns 600`は通常2陣営の実300Rを狙う上限だが、実際の`Round / OwnerTurns / EndCondition`を必ず出力で確認する。

```powershell
# 製品・Editorソースの同期とmanifest採取対象を先に固定する。
$env:FK_R3_STRICT_SUITE = '1'
& 'C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/run_ai_r2.ps1' `
    -Step controls -Label r3-retest

# smoke用。長期受入の代用にはしない。
& 'C:/Users/pethi/Documents/Codex/2026-09-19/ko/work/run_ai_r2.ps1' `
    -Step matches -Label r3-retest-smoke -Seeds '161803,271828,424242' `
    -Threat 15 -Personality Combat -OwnerTurns 120 -WallSeconds 300
```

この記録作成時には、次の順に未完事項を残す。

1. **緊急Defenderの最終回帰**：新fixtureのcast例外を修正し、唯一安全な横退避・本拠救命攻撃の共存を、既存全回帰とともにネイティブ確認する。現在の後続未実行コードを過去Greenで合格扱いにしない。
2. **P1の探索・進軍結合を制御再現**：領土進軍中ScoutのPriorityと、未探索frontierへ合法に到達できるかを確認。発見できない問題と、発見後攻撃しない問題を分離する。
3. **最新修正後の同条件長期After**：まず完走Before2seed×300Rとの比較。初視認、本拠攻撃、住宅必要局面、救命機会、資源未払いを追い、全体の停滞が残れば未完了のまま原因を絞る。
4. **難易度別30試合×最大300R**：未完。各難易度10試合、人格・seed・Player AI条件を固定してBefore/Afterを比較し、上限未決着と中断を分ける。任意の500R追加5試合も未実行。
5. **必要機会母数と性能**：偵察30、救命30、真の住宅不足20、本拠攻略10機会を確認。死亡原因・HP変化源・合法脅威と近似脅威の違いを保存し、CPU/GC/長時間メモリは専用計測を行う。
6. **証拠の配布と最終採点**：最新manifest・比較CSV/JSON・raw根拠を共有可能な形で添付し、機会不足の項目を空欄のまま残して厳格点を更新する。UI画面評価は別の実画面調査で行う。

本記録の制御PASSと実プレイ記録は、長期通常対戦で同じ症状が消えたことの代用にはしない。
