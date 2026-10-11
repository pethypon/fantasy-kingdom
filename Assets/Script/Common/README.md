# 共通状態とアダプター

GameContextはターン・選択・入力snapshot、GameSystemsはSceneの依存参照。FactionState/Statusが実際のルール状態を持つ。UnitRegistryは索引、ObjectPoolは再利用、UIやSaveはコピー。正本をSingletonや表示値へ複製しない。

IRandomSourceは乱数の境界、FKBuildInfoは同梱された読取専用ビルド識別、DevelopmentLog/Performanceは故障してもルールを止めない診断。新規コードへOSパス、Keyboard、TMP、Camera、Gitコマンドを持ち込まない。

既存SingletonやScene依存は互換を維持しながら段階的に整理する。全体図は `Docs/Architecture.md`、更新/ビルド手順は `Docs/Upgrade_Checklist.md` と `Docs/Build_Manual.md`。
