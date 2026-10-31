# A1受入80項目

PASSは記載範囲の実行証拠。通常対戦で未確認の統合効果は未検証です。

|項目|判定|要求・範囲|
|---|---|---|
|CR01|PASS|敵領土未発見ならメインクリスタル位置を知らない。`OpponentCrystalPosition`等の真の位置が戦略へ漏れない。<br>Both observation adapters hide unknown objective coordinate/HP/attack target.|
|CR02|NOT RUN|敵領土の最初の合法な観測で `TerritoryLead` へ遷移し、重点探索方針を保持する。<br>A named helper check alone does not certify a broader full integration scenario.|
|CR03|NOT RUN|サブクリスタル・遠隔採掘施設を主クリスタルと誤認しない。<br>A named helper check alone does not certify a broader full integration scenario.|
|CR04|NOT RUN|既知領土外周の未探索セルを優先し、同じ無発見セルを無意味に巡回し続けない。<br>A named helper check alone does not certify a broader full integration scenario.|
|CR05|NOT RUN|斥候と主力の行動が分担され、主力が未知の敵クリスタル座標を直接目指さない。<br>A named helper check alone does not certify a broader full integration scenario.|
|CR06|PASS|可視範囲にメインクリスタルが入った時に初めて`Observed`になる。<br>Observed stage requires official sight for each main faction.|
|CR07|PASS|メインクリスタルを見失った時は記憶に留め、現在HPや位置の透視を行わない。<br>Hidden live relocation/HP changes cannot refresh last-known facts.|
|CR08|PASS|再観測で旧位置に目標がいなければ記憶を適切に失効。<br>Official reobservation of the empty old cell invalidates the memory.|
|CR09|NOT RUN|経済回復と偵察任務が衝突した場合、危機回避と任務継続の優先が一貫。<br>A named helper check alone does not certify a broader full integration scenario.|
|CR10|NOT RUN|複数マップSeed・視界障害・高低差でも、探索手順がルールを破らず進行する。<br>A named helper check alone does not certify a broader full integration scenario.|
|AT01|PASS|メインクリスタル不可視時にそのオブジェクトへの攻撃候補を生成しない。<br>A hidden live main crystal reference cannot become an attack target.|
|AT02|NOT RUN|可視・射程内・AP足りる・攻撃許可のとき正規攻撃候補が生成される。<br>A named helper check alone does not certify a broader full integration scenario.|
|AT03|NOT RUN|可視でも射程外/高低差/遮蔽/向き不適合/移動障害などで不合法なら攻撃しない。<br>A named helper check alone does not certify a broader full integration scenario.|
|AT04|PASS|生成候補が棄却された場合、判定段階と拒否理由を記録できる。<br>Full native decisions.csv and rejection summaries are packaged for every final case.|
|AT05|NOT RUN|緊急防衛と確定勝利攻撃が競合する場合、既存優先度・生存例外を破らず評価。<br>A named helper check alone does not certify a broader full integration scenario.|
|AT06|NOT RUN|敵本陣へ向けた主力の前進が、戦闘のない数ターンでも合理的に継続する。<br>A named helper check alone does not certify a broader full integration scenario.|
|AT07|NOT RUN|主目標に阻害する敵がいる場合、脅威排除・迂回・増援の複数候補を比較。<br>A named helper check alone does not certify a broader full integration scenario.|
|AT08|PASS|別部隊がAPを消費し予測攻撃が不可能になった場合、再計画する。<br>Coordinator invalidates its sequence after shared AP changes; does not spend future AP.|
|AT09|NOT RUN|クリスタルの保護/バリア/固有反撃の予測が実際の戦闘に一致。<br>A named helper check alone does not certify a broader full integration scenario.|
|AT10|PASS|主王・メインクリスタルへの攻撃および勝利を決める統合対戦ケースを少なくとも一件検証。<br>Controlled native finishing position only: actual Player AI normal selection/executor/end-state. Ordinary natural-match victory remains unverified.|
|HS01|PASS|市民が少なくても容量に余裕があれば、住宅を無意味に増設しない。<br>Both factions: empty citizen stock with adequate capacity rejects extra housing.|
|HS02|PASS|市民費の高い必要兵種が既存容量で購入不能なら、合法かつ維持可能な住宅を投資候補にする。<br>Both factions: genuine expensive recruit capacity shortage produces a paid housing candidate.|
|HS03|PASS|AP目標が既存の市民容量では達成不能なら、住宅の必要性を検討する。<br>Actual legal pending actions and formal reset inversion determine bounded desiredAP/citizens.|
|HS04|NOT RUN|AP最大15を超える住宅投資効果を架空に見積もらない。<br>NOT RUN as literally written:15 conflicts with authoritative60. Actual60/authored lower caps and zero bonus are tested in core-native.txt.|
|HS05|PASS|市民の回復が遅いだけの状態で、住宅容量不足と誤判定しない。<br>Slow stock replenishment is separated from capacity; growth horizon defers unnecessary capacity.|
|HS06|NOT RUN|食糧・維持費・原料が危機状態なら、何を先に回復するか予測して選ぶ。<br>A named helper check alone does not certify a broader full integration scenario.|
|HS07|NOT RUN|住宅の通常建築候補・先行建築・フォールバック・改築・高級住宅で、必要性判断が整合する。<br>A named helper check alone does not certify a broader full integration scenario.|
|HS08|NOT RUN|正規の住宅建築費/AP/合法位置/建物個数/領土制限を迂回しない。<br>A named helper check alone does not certify a broader full integration scenario.|
|HS09|PASS|住宅建築または強化直後に、容量が実際に増えたことを確認して計画を更新。<br>Formal paid house/upgrade increases actual capacity and refreshed planner suppresses redundant capacity.|
|HS10|NOT RUN|住宅を作らなかった理由がログに出て、誤った永久待機・住宅無限建築を防げる。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL01|PASS|回復能力のないPriestを回復役として誤認しない。受動回復は正式能力なら認識する。<br>No-heal Priest is not a healer; valid passive aura and active support healing remain recognized.|
|RL02|NOT RUN|異なる移動・攻撃パターンを持つ兵種を同じ固定ポジションへ無理に押し込まない。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL03|NOT RUN|前衛と遠距離の組合せで、遠距離が不必要に敵の隣へ突っ込まない。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL04|NOT RUN|斥候が偵察を進め、主力が護衛/前線進行の役割を持てる。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL05|NOT RUN|兵種欠員を検知し、正規の生産・費用・種別上限が許す場合だけ補充する。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL06|NOT RUN|兵種不足時に全行動が停止せず、既存戦力で可能な代替作戦を選べる。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL07|NOT RUN|同じLifeIDの駒が複数部隊へ重複所属しない。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL08|NOT RUN|王・クリスタルの敗北条件保全を通常の前衛編成より優先できる。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL09|NOT RUN|役割評価が通常ユニットLv/XPに依存しない。<br>A named helper check alone does not certify a broader full integration scenario.|
|RL10|NOT RUN|R2工兵が完成後、個別視界・建築2倍・中立建築・敵領土禁止・護衛の任務を連携できる。<br>Dedicated engineer escort and shield/rear-line handoff are still not implemented/certified. Construction legality alone does not pass this row.|
|CQ01|NOT RUN|単発の最大ダメージと、後続合法攻撃の確定撃破を比較して後者を選べる。<br>A named helper check alone does not certify a broader full integration scenario.|
|CQ02|NOT RUN|有効なデバフ→味方の通常攻撃を、AP予算内で正しく並べられる。<br>A named helper check alone does not certify a broader full integration scenario.|
|CQ03|PASS|実際に無効なスキルやバフは、連携効果として過大評価しない。<br>Guaranteed effective setup only; immunity, already applied effects, nullification, nonfinite/probabilistic control and obsolete DEF-break excluded.|
|CQ04|PASS|ターゲット死亡・消滅・視界消失時は、古い行動列を実行しない。<br>Death, movement, HP/protection, generation and visibility changes invalidate the cached chain.|
|CQ05|PASS|同じターン内の疲労増加・連続行動のAP増加を正しく計算。<br>Official fatigue/AP preview parity, duplicate-actor exclusion, follow-up and return costs.|
|CQ06|NOT RUN|味方の移動が別の味方の射線・合法セルを塞ぐ場合、その行動順を再評価。<br>A named helper check alone does not certify a broader full integration scenario.|
|CQ07|NOT RUN|勝利対象を攻撃できる局面では、不要な局地戦に引っ張られない。<br>A named helper check alone does not certify a broader full integration scenario.|
|CQ08|NOT RUN|作戦進捗のある準備移動を選ぶケースがあり、単発攻撃に常に負けない。<br>A named helper check alone does not certify a broader full integration scenario.|
|CQ09|NOT RUN|知らない敵の真の座標・HP・技能を戦術評価へ混入しない。<br>A named helper check alone does not certify a broader full integration scenario.|
|CQ10|NOT RUN|緊急防衛・王保全・正規AP支払い・命中/ダメージ・既存行動実行経路をすべて保持。<br>A named helper check alone does not certify a broader full integration scenario.|
|RT01|PASS|撤退候補は一歩だけでなく、その先の合法な安全経路も評価する。<br>Bounded current/next-owner AP route and real-map authored-movement adapter contracts.|
|RT02|NOT RUN|本陣以外の安全な遮蔽/高台/回復地点へ合法に退却できる。<br>A named helper check alone does not certify a broader full integration scenario.|
|RT03|NOT RUN|致命的な敵反撃が分かっている時、成果のない単独突撃を抑止できる。<br>A named helper check alone does not certify a broader full integration scenario.|
|RT04|NOT RUN|盾役が撤退する際、後衛への攻撃危険を考慮し支援行動を選択できる。<br>Dedicated engineer escort and shield/rear-line handoff are still not implemented/certified. Construction legality alone does not pass this row.|
|RT05|PASS|負傷者が同じ退路マスを重複予約して衝突しない。<br>Distinct first-hop reservations and occupied projected cells are tested.|
|RT06|PASS|撤退のためのAPが足りない場合は虚偽の安全経路と判断しない。<br>Unaffordable first hop or continuation cannot be certified by future AP.|
|RT07|NOT RUN|撤退不能時は合法な防衛/脅威排除/攻撃などの代替行動を選ぶ。<br>A named helper check alone does not certify a broader full integration scenario.|
|RT08|PASS|HPが満タンへ回復した後、R2の3ターン待機とバリア再生予測が一致。<br>Formal full-HP wait and rounded20% barrier transition match actual recovery helper without mutating clock.|
|RT09|NOT RUN|回復後に状況が改善したら同じまたは別の作戦へ再出撃できる。<br>A named helper check alone does not certify a broader full integration scenario.|
|RT10|NOT RUN|成果のない往復運動を学習上の正の進捗と誤算しない。<br>A named helper check alone does not certify a broader full integration scenario.|
|AD01|NOT RUN|敵領土発見を機に、敵主クリスタル捜索を中長期目標に設定。<br>A named helper check alone does not certify a broader full integration scenario.|
|AD02|NOT RUN|サブクリスタルをメインと誤認せず、新たな探索地域を選ぶ。<br>A named helper check alone does not certify a broader full integration scenario.|
|AD03|NOT RUN|敵の攻勢により自国の王/主クリスタルが危険なら優先して緊急防衛。<br>A named helper check alone does not certify a broader full integration scenario.|
|AD04|PASS|経済破綻時は維持費未払いHP20%の損傷リスクを予測。<br>Official unpaid HP-loss and prospective HP/death forecast; no fictitious barrier-funded maintenance.|
|AD05|PASS|鉄/魔石が足りなければ、既知の合法な領土外採掘を検討。<br>Both factions: actual paid outside mining, formal recipes, current and planned ore gaps; final ordinary games record supply bootstrap.|
|AD06|NOT RUN|敵工兵や鉱山を視認した場合にだけ襲撃候補を作る。<br>A named helper check alone does not certify a broader full integration scenario.|
|AD07|NOT RUN|作戦停滞が続いたら、経路・編成・攻撃対象を見直し同じ無意味な往復を減らす。<br>A named helper check alone does not certify a broader full integration scenario.|
|AD08|NOT RUN|危機解除後に一時停止した部隊計画へ戻れる。<br>A named helper check alone does not certify a broader full integration scenario.|
|AD09|PASS|時間制限がないため残り時間勝ちを目標にしない。AI内部の安全予算は残る。<br>R2 timer rules stay disabled while bounded AI thinking and route node limits remain.|
|AD10|NOT RUN|R4 Shadow/有効で、Fog・合法性・経済・作戦目的の整合を保持。<br>A named helper check alone does not certify a broader full integration scenario.|
|IT01|NOT RUN|PR #175の修正前後で重複機能を実装せず、作戦/斥候/回復認識の回帰を確認。<br>A named helper check alone does not certify a broader full integration scenario.|
|IT02|NOT RUN|旧セーブからロードしても作戦ID・ユニットLifeID・部隊が合法に復元される。<br>A named helper check alone does not certify a broader full integration scenario.|
|IT03|PASS|40×40目標環境でマップの設定食い違いを検出し、ユーザー指定のマップ仕様を勝手に変更せず報告。<br>Read-only connectivity artifacts report actual35x35; project map/scene dimensions unchanged.|
|IT04|NOT RUN|低脅威・中脅威・高脅威で、防衛・攻勢・偵察・住宅の基本判断が破綻しない。<br>A named helper check alone does not certify a broader full integration scenario.|
|IT05|NOT RUN|少数兵・多兵・高低差・Fog・障害物・工兵採掘拠点で正規の移動・攻撃制限を守る。<br>A named helper check alone does not certify a broader full integration scenario.|
|IT06|NOT RUN|長時間・複数Seed・同一条件の比較で、王/主クリスタルに攻撃が届く試合と届かない原因を記録。<br>A named helper check alone does not certify a broader full integration scenario.|
|IT07|PASS|住宅の建築成功数と失敗理由、市民/容量/AP推移が同じ対戦記録で追跡可能。<br>Same native match tracks housing reasons/counts and citizen/capacity/desiredAP/currentAP/food/stock series.|
|IT08|NOT RUN|作戦連携、退路、進捗学習、損失・再編・再出撃を記録できる。<br>A named helper check alone does not certify a broader full integration scenario.|
|IT09|NOT RUN|AI計算・ログのメモリ/フレーム・思考予算を無制限に増やさず回帰性能を測る。<br>A named helper check alone does not certify a broader full integration scenario.|
|IT10|PASS|Unity EditMode/PlayMode/ネイティブ対戦の結果を各件PASS/FAIL/NOT RUNで明記し、非実行を合格と偽らない。<br>This complete80-row ledger distinguishes native contracts, ordinary matches and NOT RUN.|
