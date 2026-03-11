# GameScene Item Sync Design

## Current GameScene
- Scene object: `SpawnPointGroup`
- Scene object: `ItemSceneBootstrap`
- Item logic is mostly not scene-fixed; it is attached per `NetworkPlayer`
- Character-side runtime path:
  - `NetworkPlayer`
  - `ItemRuntimeHost`
  - `ItemFieldInteractionService`
  - `ItemCharacterHeldItemPresenter`
  - `ItemCharacterBuffApplier`

## Step 1. Recommended Pattern
- Input layer: `NetworkPlayer`
- Authoritative state layer: `ItemRuntimeHost` and `ItemRuntimeController`
- Replication layer: `NetworkPlayer` networked item state
- Presentation layer:
  - player buffs: `ItemCharacterBuffApplier`
  - world effects: `NetworkPlayer.ItemWorldEffects`

## Step 2. Buff Items
- Target items:
  - `GrowthItem`
  - `ShrinkItem`
  - `Americano`
  - `InvisibilityItem`
- Rule:
  - authority writes final buff snapshot once
  - every client reads the replicated snapshot
  - local presenter applies scale, collider, movement, invisibility visual

## Step 3. World Effect Items
- Target items:
  - `BlackholeBomb`
  - `SatelliteStrike`
- Rule:
  - authority receives runtime event from its own `ItemRuntimeHost`
  - authority writes effect request into `NetworkPlayer` networked state
  - each client detects sequence change and replays local effect presentation
  - gameplay force and damage stay authority-only

## Notes
- `ItemGameplayRunner` remains useful for `ItemScene` test flow
- `GameScene` should rely on player-owned replication, not a single shared scene runner
