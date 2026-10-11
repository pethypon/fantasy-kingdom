# ADR-004: 乱数と入力を識別できる境界に置く

2026-10-11 / 採用。

AIの乱数規則は既存System.Random+own-turn seedを維持し、IRandomSourceを入口とする。`system-random-v1` を保存して、将来違うアルゴリズムへ無言で切り替えない。新規対戦の実seedを記録し、性格生成とAI初期化で同じseedを使う。保存したtraitはランダム再生成しない。

キーボード・ポインターはTurnInputHandlerに集約し、PlayerMoveはGameContextの入力コマンドを読む。Cameraとinspectionも同じpointer snapshotを使う。カメラ角度/ズームはルール・学習へ入力しない。

マップ・初期配置・一部スキルのUnity.Randomは既存互換を維持する。全面的に乱数を置き換える場合は生成規則version、旧seed、Golden Replayを伴う独立した変更にする。
