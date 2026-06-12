# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Fantasy Kingdom is a Unity 6 (6000.3.8f1) tactical turn-based board game, similar in concept to chess. Two sides (Player and Enemy) alternate turns, moving and attacking with pieces that have unique movement and attack patterns.

## Unity Workflow

This project has no build scripts — use the Unity Editor directly:
- Open the project in Unity 6000.3.8f1
- The main scene is `Assets/Scenes/SampleScene.unity`
- Play in the Editor to test; there is no separate test framework

All code is in `Assets/Script/`. Scripts are plain C# MonoBehaviours and plain classes; there are no assembly definitions.

## Architecture

### Initialization Sequence

`GameGenerator` (on the `GameSystem` prefab) orchestrates startup in `Awake()`:
1. `MapCreate.GenerateNoise()` — generate Perlin noise heightmap
2. `MapCreate.BuildTop()` — instantiate terrain tiles and fog objects
3. `CrystalSystem.CrystalCore()` — place player and enemy crystals randomly
4. `UnitSetting.UnitSet()` — spawn initial units near each crystal
5. `TerritorySystem.Territory()` — mark territory zones around each crystal
6. `MoveGenerator.UnitPointCore()` — build the initial set of occupied cells
7. `VisionGenerator.VisionPoint(...)` — compute initial fog of war state

### Turn State Machine

`TurnGenerator` is the central hub. It holds references to all game systems and drives a `StateCore`-based state machine via `ChangeState()`.

```
PlayerStart → PlayerMove ──(1/2 key)──→ PlayerAttack ──(success)──→ PlayerMove
                         ↑                                          |
                         └──────────── (Enter/Shift ends turn) ─────┘
                                              ↓
                                        EnemyStart → EnemyMove → PlayerStart
```

- `StateCore` is an interface with default-empty `Entry()`, `Update()`, `Exit()` methods.
- Each state class is a plain C# class (not MonoBehaviour) that receives all dependencies via constructor injection from `TurnGenerator`.
- `EnemyMove` currently skips AI and immediately returns to `PlayerStart`.

### Map System (`Assets/Script/Mapsystem/`)

| Class | Responsibility |
|---|---|
| `MapCreate` | Perlin noise terrain, owns `SetPos` (list of all valid tile positions +1 in Y), manages all fog object parents |
| `CrystalSystem` | Random crystal placement; exposes `PCP` (Player Crystal Position) and `ECP` (Enemy Crystal Position) |
| `TerritorySystem` | Instantiates territory indicator tiles within radius 3 of each crystal |
| `UnitSetting` | Spawns King (player) and StrangeKing (enemy) near their respective crystals |

`MapCreate`, `CrystalSystem`, `TerritorySystem`, and `UnitSetting` all live on the same GameObject and use `GetComponent` to share data.

### Game Systems (`Assets/Script/Gamesystem/`)

| Class | Responsibility |
|---|---|
| `MoveGenerator` | Computes valid move destinations per `Kind`; instantiates `MovePoint` markers; tracks occupied cells via `IsOccupied()`/`AddOccupied()`/`RemoveOccupied()` |
| `AttackGenerator` | Computes valid attack positions per `Kind`; instantiates `AttackPoint` markers |
| `BattleSystem` | Damage processing via `ProcessDamage()`; exposes `Target`/`Attacker` as read-only properties; use `SetTarget()` to set target |
| `BuildSystem` | Building placement, validation (delegates to `BuildValidator`), AI placement, upgrade |
| `BuildCursorController` | Generic cursor controller for build/summon modes — configurable shape, scale, colors. Used by both `BuildSystem` and `SummonSystem` |
| `UnitClick` | Handles raycasting for unit selection (`Click1`), movement (`Click2`), and attack target selection (`AttackClick`) |
| `VisionGenerator` | Fog of War — vision data accessed via `IsInVision()`/`IsExplored()`/`AddExplored()` etc.; internal HashSets are private |
| `DamageCalculator` | Centralized damage formula; use `EstimateBaseDamage(atk, def)` for AI quick estimates instead of inline formulas |
| `TurnStartHelper` | Static helper for shared turn-start logic (shield tick, status effects, AP reset, fatigue, sub-crystals, timer) used by `PlayerStart`/`EnemyStart` |

### Unit Data (`Assets/Script/Unit&Battle/Status.cs`)

`Status` is a MonoBehaviour component on every unit/building/point prefab. Key enums:
- `Team`: `Player`, `Enemy`, `Obstacle`, `None`
- `Kind`: `Crystal`, `King`, `Knight`, `Archer`, `Magic`, `Assassin`, `Scout`, `Priest`, `Guardian`, `Crossbow`, `Magicsniper`, `Bomber`
- `Type`: `Unit`, `Building`, `MovePoint`, `AttackPoint`
- `PassiveSkill`: `None`, `Impregnable`, `HunterEyes`, `Destroyer`, `Assassination`, `Sniper`
- `Direction`: `N`, `S` — used by VisionGenerator to mirror vision patterns for south-facing units

### Fog of War

Four fog object types are placed over every tile at startup by `MapCreate.BuildTop()`:
- **Fog** — completely hides the tile (active when tile has never been seen)
- **FogBoard** — opaque overlay board (active when not explored)
- **FogExplored** — explored-but-not-currently-visible overlay (shown in explored-but-dark areas)
- **FogExploredBoard** — board version of explored overlay

`VisionGenerator.VisionPoint()` recalculates and updates these every time a unit moves or the turn changes. Vision shapes are defined as static `Vector3Int[]` arrays per unit kind (using `VisionBox` or `RangeVisionBox` helpers). Raycasts use the `"Block"` physics layer.

### Input

Input is handled via Unity's new Input System. `Assets/Action/PlayerAction.inputactions` defines bindings; `Assets/Action/PlayerAction.cs` is the generated wrapper. `TurnGenerator` instantiates `GameAction` and enables/disables it via `OnEnable`/`OnDisable`.

Player controls during `PlayerMove`:
- **WASD / Arrow keys** — move camera
- **Mouse scroll** — zoom (FOV 30–90)
- **Left click** — select unit (first click) / move to position (second click)
- **Right click** — cancel selection
- **Enter / Shift** — end player turn
- **1 / Numpad1** — enter Normal attack mode
- **2 / Numpad2** — enter Skill attack mode

## Naming Conventions

Class names now follow standard C# naming conventions. Legacy typos have been corrected:
- `GameGenerator` (formerly `GameGerater`)
- `MoveGenerator` (formerly `MoveGererater`)
- `AttackGenerator` (formerly `AttackPointt`)
- `VisionGenerator` (formerly `VisionGenerater`)
- `TurnGenerator` (formerly `TurnGenerater`)

Serialized field references are preserved via `[FormerlySerializedAs]` attributes.

Comments in code are written in Japanese and may appear garbled in some editors due to Shift-JIS encoding.

## Shared Utilities

### `GridHelper` (`Assets/Script/Common/GridHelper.cs`)

Static utility consolidating all grid coordinate operations. Use instead of raw `Mathf.RoundToInt`:

| Method | Purpose |
|---|---|
| `ToGrid(Vector3)` | World pos → `Vector3Int` (x,y,z rounded) |
| `ToGridXZ(Vector3)` | World pos → `Vector3Int` (y=0, XZ only) |
| `ToUnitPoint(Vector3Int)` | Grid pos → `Vector3(x, 0, z)` for UnitPointData |
| `MatchXZ(a, b)` | Compare two positions ignoring Y (multiple overloads: Vector3/Vector3Int/int,int) |
| `ChebyshevDistance(a, b)` | Max of dx/dz (8-directional distance) |
| `IsWithinRange(a, b, range)` | Chebyshev distance ≤ range |
| `TryGetHeight(setPos, x, z, out y)` | Find Y from SetPos list |
| `ExistsOnMap(setPos, x, z)` | Check tile existence |
| `SnapToNearest(positions, target)` | Snap to closest position in list |
| `BuildHeightLookup(setPos)` | Create `(x,z)→y` dictionary |
| `ContainsXZ(positions, target)` | Check if XZ match exists in list |

### `BrandGuide` Color Constants

All UI and fallback object colors are centralized in `BrandGuide`. Key additions:

- `CursorBuildValid/Invalid`, `CursorSummonValid/Invalid` — placement cursor colors
- `FallbackWall`, `FallbackSubCrystal`, `FallbackBuilding` — prefab-less building colors
- `FallbackPlayerUnit`, `FallbackEnemyUnit` — prefab-less unit colors
- `GetFacilityFallbackColor(FacilityKind)` — facility color lookup
- `GetUnitFallbackColor(Team)` — unit color lookup

### System Query Methods

Instead of accessing system internals directly, use query methods:

**`MoveGenerator`**: `IsOccupied(cellPos)`, `AddOccupied(cellPos)`, `RemoveOccupied(cellPos)`, `RemoveOccupiedWhere(predicate)`, `MovePositions` (read-only), `PlayerCrystalPos`, `EnemyCrystalPos`

**`VisionGenerator`**: `IsInVision(team, cell)`, `IsExplored(team, cell)`, `AddExplored(team, cell)`, `AddExploredRange(team, cells)`, `ClearExplored(team)`, `PlayerExplored`/`EnemyExplored` (read-only)

**`BattleSystem`**: `SetTarget(target)`, `ProcessDamage(turnGenerator)`, `Target`/`Attacker` (read-only properties)

**`TerritorySystem`**: `IsInTerritory(pos, team)`, `IsInAnyTerritory(x, z)`, `GetTerritory(team)`, `ClampToTerritory(pos, team)`

**`MapCreate`**: `HasTileAt(x, z)`, `TryGetHeight(x, z, out y)`, `SnapToSetPos(gridPos)`, `BuildHeightLookup()`

### `DamageCalculator` (`Assets/Script/Common/DamageCalculator.cs`)

Centralized damage calculation. Do **not** inline the damage formula (coefficients live in `GameConstants.Damage*` and are applied by `CalcRawBase`) — use these methods instead:

| Method | Purpose |
|---|---|
| `CalcRawBase(atk, def)` | Raw float base damage (no modifiers) |
| `EstimateBaseDamage(atk, def)` | Quick int estimate for AI (equivalent to `max(0, round(CalcRawBase))`) |
| `CalcNormal(attacker, target)` | Full normal attack damage (passives + buffs + special abilities) |
| `CalcSkill(attacker, target, skill)` | Full skill damage with multiplier |
| `CalcFromValues(atk, def, incomingMod)` | AI simulation with float inputs |

### `UnitRegistry` (`Assets/Script/Common/UnitRegistry.cs`)

Singleton cache for unit/building lookups. Prefer over `FindObjectsByType<Status>()` or `GetComponentsInChildren<Status>()`:

| Method | Purpose |
|---|---|
| `GetActiveUnits(team)` | All active units for a team |
| `CountActive(team, type)` | Count active units/buildings |
| `PlayerUnits` / `EnemyUnits` | Read-only lists of cached units |
| `PlayerBuildings` / `EnemyBuildings` | Read-only lists of cached buildings |

### `Status` Helpers (`Assets/Script/Unit&Battle/Status.cs`)

When modifying unit HP, prefer these helpers over direct field writes:

| Method/Property | Purpose |
|---|---|
| `ApplyDamage(damage)` | Apply damage, clamp to 0, returns actual damage dealt |
| `ApplyHeal(amount)` | Apply healing, clamp to MaxHP, returns actual heal |
| `HPRatio` | `HP / MaxHP` as float (1.0 if MaxHP is 0) |
| `IsAlive` | `HP > 0 && gameObject.activeInHierarchy` |
| `GridPosition` | Grid position via `GridHelper.ToGridXZ` |
| `ResetTurnFlags()` | Reset all per-turn tracking flags |

### AI Coordinate Convention

All AI files (under `Assets/Script/Gamesystem/TurnSystem/Enemy/`) use `GridHelper` for coordinate conversions:
- `AIBoardState.ToCell()` → delegates to `GridHelper.ToGrid()` (preserves Y)
- `SimBoardState.ToCell()` → delegates to `GridHelper.ToGridXZ()` (Y=0, for simulation)
- Private `ToCell()` in `AILearning`, `PlayerProfiler`, `RealtimeAdaptation` → delegates to `GridHelper.ToGridXZ()`
- Do **not** add new `Mathf.RoundToInt` grid conversions; always use `GridHelper` methods.

## Adding a New Unit Kind

1. Add the kind to the `Kind` enum in `Status.cs`
2. Add a movement pattern entry in `MovePatterns.cs`
3. Add an attack pattern entry in `AttackPatterns.cs`
4. Add vision data in `VisionGenerator.VisionDataMap`
