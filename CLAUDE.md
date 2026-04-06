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
1. `MapCreate.noisegenerater()` — generate Perlin noise heightmap
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
| `MoveGenerator` | Computes valid move destinations per `Kind` using pattern matching over `MapCreate.SetPos`; instantiates `MovePoint` markers; tracks occupied cells in `UnitPointData` |
| `AttackGenerator` | Computes valid attack positions per `Kind`; instantiates `AttackPoint` markers |
| `BattleSystem` | Applies damage: `ATK - DEF`, clamped to 0; manages crystal shield (activation/tick/reset) |
| `BuildSystem` | Building placement, validation (delegates to `BuildValidator`), AI placement, upgrade |
| `BuildCursorController` | Cursor rendering, mouse raycast, grid snapping, visibility control for build mode |
| `UnitClick` | Handles raycasting for unit selection (`Click1`), movement (`Click2`), and attack target selection (`AttackClick`) |
| `VisionGenerator` | Fog of War — computes per-unit vision via Raycast on the "Block" layer; controls fog object visibility and enemy renderer visibility |

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
- **FogExploard** — explored-but-not-currently-visible overlay (shown in explored-but-dark areas)
- **FogExploardBoard** — board version of explored overlay

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

## Adding a New Unit Kind

1. Add the kind to the `Kind` enum in `Status.cs`
2. Add a movement pattern entry in `MovePatterns.cs`
3. Add an attack pattern entry in `AttackPatterns.cs`
4. Add vision data in `VisionGenerator.VisionDataMap`
