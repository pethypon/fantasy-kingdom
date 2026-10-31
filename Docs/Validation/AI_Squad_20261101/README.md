# 検証証跡（2026-11-01）

ゲーム実行はUnity 6000.3.8f1、分離プロジェクト `UnityValidation`。通常対戦は `squad-release-*` の4試合のみ。全試合は自軍ターン数の上限で停止し、勝利ではない。

`formal-squad-playmode.txt` は20フレーム以上進めた実Play Mode上での制御された戦術サブシステム試験。3回の自軍ターン機会は試験が手動設定する。同一作戦・正式移動・実AP支払い・中断再開・保存復元・通常攻撃による複数駒撃破を確認する。自律指揮による自然な勝利の証拠ではない。撃破対象は試験用施設（HP30）であり、本番クリスタルのHPを下げた試験ではない。

`rules-and-mines.txt` は正式な建築／経済処理を通す制御試験。鉱山は両陣営で設置・実資源とAP支払い・生産・領地変化後の維持を確認する。ゲーム画面を人が操作した試験とは区別する。

`core.txt` はネイティブEditor内の契約・コア試験。`static-compile` は静的C#コンパイルのみで、プレイ試験には数えない。

各 `run.json` は実行コードSHA・runtime入力SHA・ゲームデータSHAを記録する。通常対戦コードは `b0b4c1f1e1bc9b30bc1b9df9e2f5192dc6b37638`。その後の `ac14628` はEditorの職種ポップアップのみ変更し、runtime・ゲームデータは同一。Player開発AIは15固定、Enemy脅威度は3・15・35。R4高次自律プランナーは既定の無効／shadow状態。

CSVの `ObjectiveAttacks` は勝利対象専用ではない。勝利対象への攻撃は `VictoryObjectiveAttacks` / `targetsOpposingCommander` / `targetsOpposingMainCrystal` を使う。`operation-orders.csv` は各選択イベントの作戦状態。`units.csv` の作戦欄は次の作戦更新前のスナップショットで空欄になり得る。候補サンプル数は独立した攻撃機会の数ではない。

フレーム値はバッチEditorと計測を含む実行値であり、リリースビルドのFPSではない。長時間リーク解消・全18職種バランス・勝率改善の証明には使わない。

途中で部隊の重複登録が見つかった旧run、手動中断run、資源や軍を追加した支援runは最終結果に含めない。実行時の認証引数を含み得るEditor生ログは公開せず、テスト出力は該当assert行に限定する。

`sha256.json` はこのフォルダの証跡ファイルのSHA256。`summarize-original-runs.py` は元のwork配置に対する集計スクリプト。
