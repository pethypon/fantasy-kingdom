# ターン復帰・探索と増援の改善（2026-10-01）

## ターン復帰

- EnemyMove の終了処理で例外が出ても、終了済み状態に取り残されず次の状態へ進むよう finally で遷移する。
- 中立・強敵の一時状態も同様に保護。ゲーム終了状態は上書きしない。
- AI の終了待ちに思考予算 + 500 ms の期限を設けた。判定は次のフレームで行うため、同期処理そのものを強制中断するものではない。
- AI iterator は切り離してから破棄し、重複破棄を防ぐ。
- 敵ターン表示と読み取り専用の選択表示を、実際の状態遷移に合わせて解除する。

いただいた Turn 11 の停止そのものは再現できていない。直近 Editor.log には PlayerMove へ復帰した記録もあったため、特定の原因と断定しない。通常遷移に加えて、終了しない iterator、MoveNext の例外、Dispose の例外を注入し、プレイヤー状態・タイマー再開・表示解除・1回だけのターン加算と破棄を検証した。

## AI の行動順

1. 敵の接触情報がない場合は、経済が不足していれば建築を師団移動より先に行う。
2. 1棟ごとに需要と購入可能候補を再評価し、古い評価による重複建築を避ける。
3. 安全な偵察駒を最大3体、各1回先行移動する。使用APはフェーズ開始時の1/3以内。発見内容は毎回更新する。
4. 視認中の敵、または7ターン未満の有効な記憶がある場合、戦闘駒1体の増援を通常移動より先に試みる。AP・資源・駒数制限・召喚位置のルールは既存の検証を通す。
5. 師団・通常判断へ進む。追加召喚などは残り資源と評価に従う。

偵察の価値を上げ、戦闘駒も接触位置へ近づく安全な移動を評価する。高い脅威度では観測した進行方向から最大4マス先までの位置を推定する。推定地点を視認して空だと分かった場合や記憶が古い場合は目標から外す。予測は配置用であり、隠れた敵の実位置・能力値を参照したり、視界外の敵への攻撃を許可したりしない。

## 編集箇所

- `Assets/Script/Gamesystem/TurnSystem/Enemy/EnemyMove.cs`: 終了処理、終了待ち上限
- `Assets/Script/Gamesystem/TurnSystem/TurnGenerator.cs`: 状態に応じた表示解除
- `Assets/Script/Gamesystem/TurnSystem/Enemy/AICommander.Recon.cs`: 先行探索、増援
- `Assets/Script/Gamesystem/TurnSystem/Enemy/AIReconnaissance.cs`: 情報評価、記憶・推定目標
- `Assets/Script/Gamesystem/TurnSystem/Enemy/AIBuildPlanner.cs`: 建築需要の再評価
- `Assets/Script/Gamesystem/TurnSystem/Enemy/AIBuildEvaluator.cs`: 接触時の召喚評価

## 検証

ユーザーが開いているプロジェクトとは別の Unity 6000.3.8f1 検証コピーで実施。

- SceneSmoke: 598 checks PASS。実召喚・AP消費・領土制限、情報漏れ防止、既存回帰テストを含む。
- TurnCycleRunner: 6サイクル、33 checks PASS。実際の Update とフレーム経過を使い、手動終了・タイマー切れ・強制期限・例外時の復帰を検証。
- 人為的な例外2件は TurnCycleRunner の期待するテスト入力。通常の SceneSmoke に例外なし。
- 脅威度100の同期テスト用3秒予算: 約3006 ms。250 ms の探索テストの最大スライス約3.083 ms。これは検証環境の測定であり、全フレームの最大時間を保証しない。
- テストソース: `Tests~/TurnCycleRunner.cs`, `Tests~/InformationGrowthTests.cs`, `Tests~/SceneSmoke.cs`。

実際の見た目・長時間プレイでの難易度調整は、このヘッドレス検証の範囲外。

Windows ビルド成功: E:/fantasykingdom/Builds/Windows-20261001-TurnRecon/FantasyKingdom.exe
