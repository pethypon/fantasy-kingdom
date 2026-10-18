# Unity終了時nativeメモリ診断（2026-10-18 JST）

Unity 6000.3.8f1、隔離cloneでWindows非Developmentビルド。`-diag-job-temp-memory-leak-validation`を付けたnative stack診断。raw logは保持し、大量のhexdumpは本文へ転載しない。

| 終了条件 | JobTempAlloc警告 | allocation記録数 | ユニークallocation | ユニークbyte | shader variant経路 |
|---|---:|---:|---:|---:|---:|
| BuildPlayer直後 | 2 | 5584 | 2792 | 1,071,928 | 2792 |
| 3秒Editor idle後 | 2 | 5584 | 2792 | 1,071,928 | 2792 |

即時終了logは2節。各節は2792件・1,071,928bytesで、アドレス/size/stack等の集合は同じ（age=1→2）。5584記録を2倍のリーク量として数えず、ユニーク1,071,928bytes（1.022MiB）とする。

## stackの分類

| native経路 | ユニーク件数 | byte |
|---|---:|---:|
| Shader variant compilation / build usage vertex calculation | 335 | 123,406 |
| Shader variant compilation / worker/native buffer | 603 | 550,994 |
| Shader variant compilation / serialized shader parameters | 1854 | 397,528 |

全allocationのstackに`ShaderCompilation::CompileVariantJob`があり、非shader経路は0件。最初の504bytesは次のbuild経路を含む。

```text
ShaderCompilation::CompileVariantJob
→ ShaderCompilation::ProcessCompilationResults
→ ShaderCompilation::CompileVariants
→ ShaderCompilation::CalculateUsedVertexComponents
→ Shader::CalculateUsedVertexComponents
→ ComputeBuildUsageTagOnObjects
→ CompileGameScene
→ BuildPlayerData
```

他の大多数はshader serialized parametersの読込・matrix/vector/native bufferをworkerで生成したstack。C#ゲームロジックやAIランタイムのallocation元を示すstackは出ていない。nativeのテンプレートsymbol名だけを、同名のゲーム機能が使われた証拠とは扱わない。

## 判定できる範囲

この再現条件の終了警告は、Unityのビルド時shader compilerが保持しているnativeメモリに局所化できた。具体的shader/materialや解放されない根本理由まではこのstackから特定できない。Unity全般の既知不具合と断定せず、アプリの長期メモリリーク全てが解決したとも言わない。

BuildReportはSucceeded/errors=0/warnings=0。その後のEditor shutdownでJobTempAllocが報告される。`state=OK`、frameIndex=0、lifetime=15というログ値を記録し、`state=OK`を警告無効化の根拠にはしない。ageは即時終了が1→2、idle後は2→3だった。

ログ末尾のnative UTP `MemoryLeaks.allocatedMemory`は複数memory labelの別計測。JobTempAlloc 1,071,928bytesと合計したり、同じ値とみなしたりしない。

### r1-build-memory-immediate-20261018.log

- L5453: `[ReleaseValidation] result=Succeeded errors=0 warnings=0 seconds=9.9 mode=immediate requestedIdleSeconds=0.0`

native UTP allocatedMemory=18,950,011bytes。上位label: AssetDatabase=4,848,857, HashMap=4,286,873, Thread=4,230,344, Utility=2,660,134, BaseObject=1,620,272。

raw log SHA256: `fb519779ddf6647fb5a3dac9f567077340d4947c531e86e3cc9c99a8f3adb355`。全allocationのアドレス/byte/stackID/各警告位置、重複判定、stack分類はJSONを参照。

### r1-build-memory-idle-20261018.log

- L5351: `[ReleaseValidation] result=Succeeded errors=0 warnings=0 seconds=5.0 mode=editor-idle requestedIdleSeconds=3.0`
- L5379: `[ReleaseValidation] idle_completed mode=editor-idle requestedIdleSeconds=3.0 actualIdleSeconds=3.000 editorUpdates=12230`

native UTP allocatedMemory=18,952,799bytes。上位label: AssetDatabase=4,843,189, HashMap=4,280,593, Thread=4,230,344, Utility=2,660,134, BaseObject=1,620,272。

raw log SHA256: `a55959930d636a86a3758b6667db4b8163454197633409e5d16c305854a51e31`。全allocationのアドレス/byte/stackID/各警告位置、重複判定、stack分類はJSONを参照。

## 3秒Editor idleとの比較

3秒idle後は警告2件・ユニークallocation 2792件・1,071,928bytes。即時終了は警告2件・2792件・1,071,928bytes。

同じclone/sourceで順次実施したincremental buildなので、build cacheや実行順序も異なる。各1回の結果を待機だけの因果効果とは扱わない。ログのactualIdleSeconds/editorUpdatesを根拠にし、wall秒数だけで特定のフレーム寿命を満たしたと断定しない。
