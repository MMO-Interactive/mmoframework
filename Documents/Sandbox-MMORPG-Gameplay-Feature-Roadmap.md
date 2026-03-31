# Sandbox MMORPG Gameplay Feature Roadmap

This roadmap is aimed at evolving the current networking foundation into a **player-driven sandbox loop** inspired by Wurm Online and Ultima Online:

- long-horizon progression by doing (skills improve through use)
- economy driven by gathering/crafting/trading
- territorial friction (housing, claims, crime/conflict)
- meaningful loss/recovery loops (durability, loot risk, reputation)

## 0) What exists today (starting point)

Your current stack already has several pieces that are ideal foundations for sandbox systems:

- **Gateway + zone topology** with hot handoff between zones.
- **Session + transfer token workflow** for secure-ish movement between zone hosts.
- **Realtime UDP input/snapshot loop** with AOI filtering and ghosting near seams.
- **Server-authoritative position updates** in `ZoneHost` (movement is processed server-side).

This means you can begin building gameplay features **without changing core deployment shape first**.

## 1) Target gameplay pillars (Wurm/UO style)

Build features around these pillars so systems reinforce each other:

1. **Skill-based character growth (use-based, not class-locked)**
   - Skills: mining, woodcutting, blacksmithing, carpentry, cooking, taming, healing, magery.
   - Skill gains from action resolution with anti-macro constraints.
2. **Resource world + transformation chains**
   - Nodes and world objects -> raw resources -> refined materials -> crafted goods.
3. **Itemization depth**
   - Quality, durability, weight, stackability, ownership metadata.
4. **Player economy**
   - Vendor contracts, local markets, hauling/logistics pressure.
5. **Territory + social structures**
   - Plots, houses, permissions (owner/friend/guild/public), decay/upkeep.
6. **Risk model**
   - Guarded vs lawless zones, theft/crime flags, insurance/decay/full-loot variants.

## 2) Architecture additions you should make first

Add these server-side capabilities before attempting advanced crafting/combat:

### 2.1 Authoritative gameplay state service

Create a `GameplayState` boundary (can be in-memory first, then DB-backed) that owns:

- characters (stats, skills, reputation)
- inventories and equipment
- world entities (resource nodes, placeables, containers, corpses)
- claims/housing records

`ZoneHost` should call gameplay-domain APIs for simulation outcomes rather than mutating raw structs directly.

### 2.2 Reliable action command pipeline

Your UDP path is good for movement/snapshots; use **TCP or reliable UDP channel** for transactional gameplay actions:

- gather attempt
- craft begin/complete/cancel
- move item/split stack
- equip/unequip
- lockpick/crime actions

Each command needs:

- action id / idempotency key
- validation context (distance, permissions, stamina/tools)
- deterministic server result event

### 2.3 Event-sourced gameplay log (lightweight)

Record high-value events (skill gain, craft completion, item creation/destruction, property changes) to an append log.

Benefits:

- exploit investigation
- rollback/rebuild options
- analytics for balancing progression curves


### 2.4 How gameplay features get added to the Unity-based Zone Server

Add gameplay features directly inside the Unity zone runtime by introducing **domain modules** and keeping transport thin.

Recommended implementation map:

1. **`UnityZoneServerRuntime` becomes orchestration only**
   - Keep socket IO, attach/transfer/prewarm, and tick scheduling here.
   - Move gameplay logic out of direct UDP handlers into dedicated systems.
2. **Create gameplay systems under `Server/ZoneServer/Assets/Scripts/MMONetworking/ZoneServer/Gameplay/`**
   - `GameplayStateService`: players, inventories, skills, world entities.
   - `GatherSystem`, `CraftSystem`, `CombatSystem`, `HousingSystem`.
   - `GameplayCommandRouter`: maps incoming gameplay commands to systems.
3. **Add a command stream on TCP**
   - Parse `GameplayCommandMessage` from the control stream (`HandleTcpClientAsync`).
   - Return `GameplayResultMessage` with authoritative outcomes.
4. **Drive AOI replication from tick loop**
   - `RunTickAsync` already emits snapshots; extend it with entity deltas from gameplay systems.
   - Keep high-frequency positional snapshots on UDP; send inventory/crafting/stateful results on TCP.
5. **Persist on action completion boundaries**
   - On successful gather/craft/trade/loot actions, write to persistence adapters (`IGameplayRepository`).

Concrete hooks in existing Unity zone server code:

- `HandleTcpClientAsync(...)`: add gameplay command read loop after attach acceptance.
- `RunUdpAsync(...)`: keep movement + probe handling only; avoid inventory/economy mutation here.
- `RunTickAsync(...)`: call `GameplayStateService.Tick(...)` then publish world/entity deltas.
- `ZonePlayer`: keep network/session fields only; store gameplay state in domain services.

This preserves determinism and lets Unity zone runtime host rich gameplay while still interoperating with the external control plane.

## 3) Data model you need to introduce

Start with pragmatic relational tables (or equivalent document model):

- `characters`
- `character_skills`
- `items`
- `item_instances` (quality/durability/owner/container)
- `inventories` / `inventory_slots`
- `recipes` / `recipe_requirements`
- `resource_nodes`
- `claims` / `claim_permissions`
- `action_events`

Key design rule: prefer **instance-based items** over simplistic stack IDs when quality/durability matters.

## 4) Network contract extensions

Extend shared message contracts in `Shared/MMONetworking` with gameplay packets.

Recommended additions:

- `GameplayCommandMessage` (TCP): typed command envelope
- `GameplayResultMessage` (TCP): authoritative action outcome
- `InventorySnapshotMessage` (TCP): delta or full snapshot on container open/change
- `EntityDeltaMessage` (UDP): lightweight non-player world entity updates in AOI
- `CombatEventMessage` (TCP/UDP hybrid based on criticality)

Keep `WorldSnapshotMessage` focused on high-frequency positional state; do not overload it with complete inventory/crafting payloads.

## 5) Incremental implementation plan (vertical slices)

Build in slices that each deliver a player-visible loop.

### Slice A: Harvest loop (2-3 weeks)

**Goal:** player can gather wood/ore and carry resources.

- Add resource node entities (spawn, depletion, regen).
- Add inventory with weight/capacity.
- Add `Gather` command -> server validation -> item grant + skill gain chance.
- Show inventory UI in Unity client and node interaction prompts.

Done criteria:

- two node types (tree/ore), at least three resource items
- gather anti-spam cooldown and stamina/tool checks
- persistence of inventory and skills across relog

### Slice B: Basic crafting loop (2-3 weeks)

**Goal:** convert gathered resources into useful tools.

- Recipe system with station/tool requirements.
- Craft success chance based on skill + recipe difficulty.
- Item quality and durability generated on craft.
- Tool durability loss on use.

Done criteria:

- 8-12 recipes covering 2 professions
- crafted tools improve gather efficiency
- failed craft consumes partial resources

### Slice C: Housing + permissions (3-4 weeks)

**Goal:** player places small house/container with access rules.

- Land claim data + placement validation.
- House/container entities replicated in AOI.
- Permission ACL (owner/friend/guild/public) on interactions.
- Upkeep/decay tick.

Done criteria:

- claim, place, lock/unlock, grant/revoke access
- decay warning + eventual collapse if upkeep ignored

### Slice D: Risk + conflict (3-5 weeks)

**Goal:** introduce consequences and PvP-adjacent systems.

- Region rule sets (guarded/lawless).
- Flagging, criminal actions, witness/guard response rules.
- Corpse/drop container flow on death (variant by region).
- Basic melee/magic action system with cooldowns and LOS checks.

Done criteria:

- at least one safe and one risky ruleset zone
- kill/death pipeline with loot resolution
- reputation/karma change events

## 6) Anti-exploit requirements from day one

For sandbox MMOs this is non-optional. Add early:

- server-authoritative validation for all economy-impacting actions
- per-action rate limits and distance checks
- tamper-resistant action sequencing
- audit trail for item mint/burn and transfers
- anomaly detection jobs (duplication, impossible skill gain rates)

## 7) Client UX priorities (so systems are legible)

Add minimal but clear interfaces:

- interaction reticle + action progress bars
- inventory/containers with drag/split
- crafting panel with requirement validation hints
- contextual error messaging from `GameplayResult`
- local combat/status log (hits, misses, skill ticks, reputation changes)

Sandbox depth fails if players cannot read state transitions quickly.

## 8) Team execution model

Use a three-lane cadence each sprint:

1. **Simulation lane**: server gameplay/domain logic + tests.
2. **Protocol lane**: shared contracts and compatibility checks.
3. **UX lane**: Unity interactions and presentation.

Gate each merge by:

- protocol version compatibility check
- deterministic simulation tests for commands
- migration-safe persistence changes

## 9) Concrete next 14 days

1. Define gameplay command/result envelopes in `Shared/MMONetworking`.
2. Add `GameplayState` abstraction in server host and route one command (`Gather`) through it.
3. Implement persistence for: character skills, inventory, item instances.
4. Add tree/ore node spawners to zone runtime.
5. Add Unity inventory panel + gather interaction.
6. Add telemetry counters: gather attempts, success %, item creation rate.

If you execute only the above, you'll have the first meaningful sandbox loop and enough telemetry to tune the progression economy.

## 10) Design guardrails to stay true to Wurm/UO-style depth

- Prefer **interdependent systems** over isolated mini-games.
- Make geography matter (resource rarity and logistics).
- Allow specialization and trade to emerge naturally.
- Keep loss meaningful but not terminal (recovery paths are essential).
- Tune for long-term identity progression, not short-session reward spam.
