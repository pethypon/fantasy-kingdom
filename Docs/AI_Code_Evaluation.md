# Fantasy Kingdom AI Code Evaluation

## Overview

The enemy AI system spans **30 C# files** (~600KB / ~10,000+ lines of code) across three directories:

- `Assets/Script/Gamesystem/TurnSystem/Enemy/` (19 files)
- `Assets/Script/Gamesystem/TurnSystem/Enemy/ML/` (10 files)
- `Assets/Script/Common/AIConstants.cs` (1 file)

---

## 1. Intelligence (AI Smarts): 4/5

### Strengths

- **Minimax Search Engine** — Alpha-Beta pruning with iterative deepening, killer moves, and transposition table (32,768 entries). Proper optimization implemented.
- **Rich Action Generation** — Move, Attack, Skill, Retreat, Support, Surround, DefenseReposition, Build, Summon, SubCrystal. Very comprehensive candidate generation.
- **Strategy Planner** — Auto-selects from 7 strategies (Assault / CrystalDefense / RetreatRegroup / EconomyBuild / Balanced / ScoutSearch / ContactEngage) based on board state.
- **Economic Chain Management** — Diagnoses production chain deficits and prioritizes upstream facility construction.
- **Coordinated Tactics** — Flank detection, surround checks, multi-unit coordination scoring.

### Weaknesses

- Search uses greedy approach (max 10 actions/turn) limiting long-term strategic vision.
- No A* pathfinding; uses predicate-based pattern matching only.

---

## 2. Thinking Speed: 4/5

### Optimizations Implemented

- **Object Pooling** — `SimBoardPool` minimizes GC pressure for `SimBoardState`, `SimUnit`, `List<SimUnit>`, `Dictionary`, `HashSet`.
- **ThreadStatic Buffers** — Eliminates per-frame allocations.
- **Transposition Table Cache** — Reuses evaluated board positions.
- **Iterative Deepening** — Optimizes move ordering from shallow searches before deep searches.
- **Time Budget** — 5-second default cutoff with `Stopwatch` for precise measurement.
- **QuickScore Pre-sorting** — Filters candidates with lightweight heuristic before Minimax.

Practical for a turn-based game. Responsive design.

---

## 3. Machine Learning: 3/5

### Two Learning Layers

| Layer | Content | Activation |
|---|---|---|
| **AILearning (Rule-based)** | Records failed frontal attacks, successful flanks, isolated deaths; dynamically adjusts personality params (max +/-30 pts) | Growth personality only |
| **MLIntegration (Neural Net)** | 80 input -> 128 hidden -> 3 specialist heads (Strategy/Tactics/Economy). Player profiling (16 dims), behavior prediction (RNN), counter-strategy generation, real-time adaptation | Threat level 21+ |

### Neural Network Specs

- Initialization: Xavier
- Activation: ReLU (hidden) + Tanh (output)
- Optimizer: Adam + Dropout (0.15) + L2 Regularization + Gradient Clipping
- Replay Buffer: 5,000 entries
- Exploration: epsilon-greedy (15% -> 2% decay)
- Persistence: JSON save/load with 3-backup rotation (`MLPersistence.cs`)

### Limitation

- MinLevel bug (see Section 7) causes tutorial/normal tiers to be unreachable.

---

## 4. Architecture & Design: 4/5

### Strengths

- **Clean pipeline separation**: `AICommander` (orchestration) -> `AIActionGenerator` (candidates) -> `AIActionEvaluator` (scoring) -> `AISearchEngine`/`AIMinimaxEngine` (lookahead) -> `ExecuteAction` (execution)
- **Simulation layer independence**: `SimBoardState`/`SimUnit`/`SimActionGenerator` have zero dependency on GameObjects. Pure data model completely decoupled from Unity's MonoBehaviour.
- **Centralized constants**: `AIConstants.cs` consolidates piece values, weight coefficients, phase thresholds. Easy to tune.
- **Graduated complexity**: Threat level system enables simple behavior at low levels, ML/role assignment/deep search only at high levels.

### Areas for Improvement

- `AICommander.cs` is 66KB. `ExecuteTurn()` alone exceeds 250 lines. Build-early/late phases, failure recovery, strategy fallback are all packed into one method.
- `AIActionEvaluator.cs` is 82KB. Strategy bonuses, counter-penalties, BOSS conditions scattered across many static methods.

---

## 5. Code Quality: 4/5

### Strengths

- **Thorough Japanese comments** — File-level block comments, spec explanations, even verification steps.
- **Null safety** — Consistent checks for `_buildSystem`, `_summonSystem`, `unit.gameObject.activeInHierarchy`.
- **Rich debug logging** — Structured `[AICommander]` prefix logs output full turn state (strategy, AP, unit count, resources, buildings).
- **Deterministic RNG** (`AIDeterministicRandom`) — Seed + turn-number sub-seeding ensures reproducibility.

```csharp
// Good: Lazy initialization pattern
if (_buildSystem == null)
{
    _buildSystem = _turnGen.buildsystem;
    if (_buildSystem == null)
        _buildSystem = Object.FindFirstObjectByType<BuildSystem>();
}
```

### Areas for Improvement

- `failedActions` HashSet uses string keys (`$"{type}_{facility}_{kind}_{pos}"`). Not type-safe; struct or tuple preferred.
- `board.DiagnoseProductionChainDeficit()` may be called multiple times per turn without caching.
- `System.Linq` imported in `AIBoardState.cs` — potential GC pressure if used in hot paths.

---

## 6. Performance Optimization: 5/5

Excellent work in this area:

- **SimBoardPool** — 5 types pooled (Board, Unit, List, Dictionary, HashSet). Near-zero GC during search.
- **Pre-allocated buffers** — `MLBrain` inference path (`_predictAShared`, `_predictAHead`), `AIMinimaxEngine._greedyActedUnits`/`_sortScoreBuffer`.
- **Enum.GetValues cache** — `AIBoardState` line 127 caches `FacilityKind[]`/`Kind[]` as static arrays.
- **Time budget enforcement** — 5s limit for Minimax, 2s for fallback heuristic. Accurate `Stopwatch` measurement.
- **Transposition table** — Zobrist-style hash with 32K entries. Combined with Alpha-Beta pruning.
- **QuickScore pre-sort** — Lightweight scoring before full Minimax evaluation.

```csharp
// GC-zero inference path
float[] _predictAShared;     // pre-allocated
float[][] _predictAHead;     // pre-allocated
```

---

## 7. Bugs & Issues

### Critical: MinLevel = 60

**File**: `AIThreatLevel.cs` line 30

```csharp
public const int MinLevel = 60;
public const int MaxLevel = 100;
public const int TutorialEnd = 10;   // 1-10: Tutorial tier
public const int NormalEnd   = 20;   // 11-20: Normal tier
public const int HardEnd     = 30;   // 21-30: Hard tier
```

The constructor clamps to `MinLevel`:
```csharp
public AIThreatLevel(int initialLevel = 1)
{
    Level = Mathf.Clamp(initialLevel, MinLevel, MaxLevel); // Clamps 1 -> 60
}
```

**Impact**: Threat levels 1-59 are unreachable. Tutorial tier (mistake rate, shallow search), Normal tier (moderate search depth), and Hard tier features are never activated. The AI always runs in "Endgame" tier with `SearchDepth = 10`, full role assignment, and ML enabled.

**Fix**: Change `MinLevel = 60` to `MinLevel = 1`.

### Medium: Unreachable SearchDepth Tiers

**File**: `AIThreatLevel.cs` lines 84-93

The `SearchDepth` property has branches for Tutorial(3), Normal(5), Hard(8), Endgame(10). Since MinLevel=60, only `return 10` is ever reached.

### Medium: Weak Board Hash

**File**: `AIMinimaxEngine.cs` lines 74-91

```csharp
static long BoardHash(SimBoardState board)
{
    long h = 17;
    for (int i = 0; i < board.Units.Count; i++)
    {
        h = h * 31 + u.Position.x;
        h = h * 31 + u.Position.z;
        // ...
    }
}
```

This is a polynomial hash, not true Zobrist hashing. It's order-dependent (same units in different list order produce different hashes) and has high collision potential.

### Medium: Static State Sharing

**File**: `AIBoardState.cs` line 51

```csharp
static Dictionary<int, LastKnownInfo> _lastKnownPlayerPositions = new Dictionary<int, LastKnownInfo>();
```

Static fields are shared across all `AIBoardState` instances. If multiple `AICommander` instances exist (e.g., for self-play simulation), they would share and corrupt this state.

### Low: Synchronous AI Execution

**File**: `EnemyMove.cs` line 14

```csharp
public override void Entry()
{
    Systems.AICommander.ExecuteTurn(); // Blocks the main thread
}
```

If AI thinking exceeds 5+ seconds, the Unity frame stalls. Should use coroutines or `async/await` for non-blocking execution.

---

## 8. Summary

| Category | Rating | Notes |
|---|---|---|
| Intelligence | 4/5 | Minimax + personality + strategy planner is sophisticated |
| Thinking Speed | 4/5 | Pooling, caching, time budgets are thorough |
| Machine Learning | 3/5 | Solid architecture but MinLevel bug disables graduated difficulty |
| Architecture | 4/5 | Clean pipeline. Large files could be split |
| Code Quality | 4/5 | Comments, logging, null-safety are excellent |
| Performance | 5/5 | Near-zero GC during search. Professional-grade optimization |
| Robustness | 4/5 | Multi-layer fallback. Synchronous execution risk |
| **Overall** | **4/5** | **Very high quality for an indie game. Fix MinLevel immediately.** |

### Priority Fixes

1. **`AIThreatLevel.MinLevel = 60` -> `1`** — Unlock graduated difficulty (Critical)
2. **Async AI execution** — Prevent frame stalls during long searches
3. **True Zobrist hashing** — Pre-generated random table with XOR for order-independent hashing
4. **Split large files** — `AICommander.cs` (66KB) and `AIActionEvaluator.cs` (82KB) into focused classes
5. **Remove static fields in AIBoardState** — Use instance fields to support multiple AI instances
