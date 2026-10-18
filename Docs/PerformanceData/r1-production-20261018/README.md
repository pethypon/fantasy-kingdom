# 検証原本

2026-10-18 JST。baselineは修正前3試合、confirmationは移動候補・作戦寿命修正後1試合。新しい脅威度進行と16職種モデルはその後のUnity回帰・ビルドで確認。

ログは原本をgzip圧縮して保管。evidence-manifest.jsonのsourceSHA256で解凍後の原本を照合できる。native-memory.json.gzはallocationごとの記録と重複判定を含む。ヘッダやrawの絶対作業パスは観測当時のまま保持した。

frame/GC/UnityメモリはEditor・描画・UI・観測器を含む。host-process.csvは同じUnityプロセスを外部OS計測。Unity内のprocess APIの0は未提供。ログ中のMemoryLeaks UTP集計とJobTempAlloc量を合算しない。

final-production-sha256.jsonは最終反映データであり、全対戦の同一source版を意味しない。Unity終了時警告は未解消。詳しい結果と限界は../../R1_Production_Environment_Playtest_20261018.mdを参照。
