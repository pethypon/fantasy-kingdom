# AI subsystem

AICommanderはターンのファサード。AIBoardStateは視界に従う観測、Generator/Evaluator/Searchは候補と評価、Executorは共通ルール経由の実行。Operation・Exploration・Reflection/MLの各正本と保存を分ける。日記/UIは読取用snapshotで、学習の正本にしない。

UI/TMP/Camera/Keyboardを新しい判断クラスから参照しない。TurnStateのバナー表示やBattle/Skillの実行アダプターを判断ロジックへ混ぜない。チート観測、領土外の探索禁止、候補数・深度を削った軽量化を再導入しない。

長期状態には既存Config上限、runtime参照にはScene/battleの寿命を使う。4位置履歴は評価に使用する4件を維持し、引退参照を5own-turnごとに掃除する。表示キューとConsole levelは判断結果を変えない。

実seed/trait/own-turnを保存し、旧実seedがない形式では再現性を主張しない。詳細は `Docs/Architecture.md`、`Docs/AI_LongSession_20261011.md`、`Docs/AI_Operations_20261010.md`。検証はSceneSmoke、探索/作戦/自己評価/経済の回帰、AILongSessionSoak、LongTermRuntimeTests。
