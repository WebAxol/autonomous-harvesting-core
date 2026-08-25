# Implementation Plan: Harvesting Multi-Agent System

## Overview

Bottom-up build of the pure agentic logic layer in C# with zero external dependencies. One project only: `src/HarvestingCore/` (netstandard2.1 class library). No test project; testing is out of scope for this build.

Order is dictated by the design's layering. Each group ends with a build verification step.

Conventions used throughout:

- `_Requirements:` cites granular acceptance criteria from `requirements.md`. `_Properties:` cites the numbered correctness properties from `design.md` (informational only, since no property tests are implemented).

## Tasks

- [x] 1. Solution and project scaffolding
  - [x] 1.1 Create the solution and both project files
    - Create `HarvestingCore.sln` referencing the library project
    - Create `src/HarvestingCore/HarvestingCore.csproj`: `<TargetFramework>netstandard2.1</TargetFramework>`, `LangVersion` set to a level that compiles without runtime-side attributes (no `record`/`init`), nullable disabled, **zero `PackageReference` elements**
    - _Requirements: 18.1, 19.1_

  - [x] 1.2 Verify the empty solution builds
    - Run `dotnet build HarvestingCore.sln` and confirm zero warnings related to framework or dependency resolution
    - _Requirements: 18.1, 19.1_

- [-] 4. World model layer
  - [x] 4.1 Implement `CellState`, `GridPosition`, and `MoveOrder`
    - Create `src/HarvestingCore/World/CellState.cs` with `Empty = 0`, `Crop = 1`, `Blocked = 2`, `Harvested = 3`
    - Create `src/HarvestingCore/World/GridPosition.cs` as a readonly struct implementing `IEquatable<GridPosition>` with `X`, `Y`, `Offset`, `IsNeighbourOf`, `Equals`/`GetHashCode`/`ToString`/`==`/`!=`, and `static int CompareRowMajor(a, b)` ordering by `y` then `x`
    - Create `src/HarvestingCore/World/MoveOrder.cs` with `Offsets` in the exact sequence `(0,1), (1,0), (-1,0), (0,-1), (-1,1), (-1,-1), (1,1), (1,-1)` and `Count = 8`
    - _Requirements: 4.4, 11.2, 18.4_

  - [x] 4.2 Implement `Cell`
    - Create `src/HarvestingCore/World/Cell.cs` with `NoOwner = ""`, `State`, `Popularity`, `OwnerId`, and the operations `Harvest`, `Plant`, `IsOwnedBy`, `AssignOwner`, `ClearOwner`, `RegisterEntry` (increments popularity and returns the new value), plus `internal SetStateForGeneration`
    - Initialise popularity to zero and owner to `NoOwner`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_

  - [x] 4.3 Implement `WorldModel` storage and queries
    - Create `src/HarvestingCore/World/WorldModel.cs` with a flat `Cell[]` indexed `y * Width + x`, `List<GridPosition>` backing fields for refuel stations and dump sites, and read-only projections `Cells`, `RefuelStations`, `DumpSites`
    - Constructor validates `width < 1` / `height < 1` with `ArgumentOutOfRangeException` naming the dimension, and rejects out-of-bounds or duplicated station/dump positions with `ArgumentException` naming the collection and position
    - Implement `InBounds`, `IndexOf`, `PositionOf`, `CellAt` (throws `ArgumentOutOfRangeException` naming `x` or `y`), `TryGetCell` (no-throw, leaves the matrix untouched), `IsPassable`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [x] 4.4 Implement grid generation, serialisation, and parsing
    - Add `Generate(IRandomSource)` to `WorldModel`: returns `false` and leaves the matrix untouched when `IsGenerated` is already true; otherwise walks the flat array in row-major index order drawing once per cell from the random source using `CropDensity` and `BlockedDensity`, forces refuel and dump positions to `Empty` afterwards, sets `IsGenerated`, returns `true`
    - Add `Serialize()` producing the char-grid form (`.` empty, `W` crop, `#` blocked, `_` harvested) and `static Parse(text, refuel, dumps)` as its inverse
    - _Requirements: 1.6, 1.7, 1.8_

  - [x] 4.7 Verify world model group
    - Run `dotnet build HarvestingCore.sln`
    - _Requirements: 1.1, 2.1_

- [x] 7. Pathfinding layer
  - [x] 7.1 Implement `HeapEntry` and `DeterministicMinHeap`
    - Create `src/HarvestingCore/Pathfinding/HeapEntry.cs` as an internal readonly struct with `CellIndex`, `Priority`, `Sequence`
    - Create `src/HarvestingCore/Pathfinding/DeterministicMinHeap.cs` as an array-backed binary heap; `Push` stamps a monotonically increasing sequence number, and `Less(a,b)` is the strict total order `a.Priority < b.Priority || (equal priority && a.Sequence < b.Sequence)`
    - Implement `Count`, `Push`, `Pop`, `Clear` with `Clear` reusing the backing array
    - _Requirements: 13.7, 18.4_

  - [x] 7.3 Implement `CostField` and `Heuristics`
    - Create `src/HarvestingCore/Pathfinding/CostField.cs` with `Unreachable = int.MaxValue`, `Width`, `Height`, `Origin`, read-only `Costs`, `IsReachable`, `CostAt`, and internal `MutableCosts`/`Predecessors` (`-1` for none)
    - Create `src/HarvestingCore/Pathfinding/Heuristics.cs` with `Zero`, `Octile(a, b, minCost) = minCost * max(|dx|, |dy|)`, and `SquaredEuclidean(a, b) = dx*dx + dy*dy`
    - _Requirements: 14.1, 14.7_

  - [x] 7.4 Implement the shared search skeleton in `PathFinder`
    - Create `src/HarvestingCore/Pathfinding/PathFinder.cs` holding `WorldModel`, `SimulationConfig`, and reused scratch state (`DeterministicMinHeap`, `int[] _costs`, `int[] _predecessors`, `bool[] _closed`) version-stamped rather than cleared so reuse stays `O(1)`
    - Implement `internal int StepCostInto(GridPosition)` attaching cost to the entered cell and returning the `Unreachable` sentinel for `Blocked`
    - Implement the shared loop: pop cheapest, skip stale closed entries, test the termination predicate on **pop**, expand neighbours in `MoveOrder` sequence skipping out-of-bounds and `Blocked`, never relax from a sentinel cost, relax on strict improvement recording the predecessor
    - Implement `Reconstruct(targetIndex)` walking the predecessor chain and reversing, so element 0 is the origin
    - _Requirements: 13.1, 13.2, 13.3, 14.1, 14.3, 14.6_

  - [x] 7.5 Implement the four public search entry points
    - Add `PathToBestCell(origin, targetState, ownerFilter = null)` terminating on the first popped cell holding the target state and passing the owner filter; returns an empty list when none is reachable
    - Add `PathToCell(origin, target, heuristicOverride = null)` terminating on the target index, short-circuiting `origin == target` to a single-element path, and returning an empty list for out-of-bounds, `Blocked`, or unreachable targets
    - Add `ComputeCostField(origin)` running the loop with no termination predicate and no heuristic, copying the finalised costs and predecessors into a `CostField` snapshot
    - Add `TryCostToNearest(origin, targets, out best, out cost)` scanning a single cost field
    - Never return null: an empty `IReadOnlyList<GridPosition>` is the sole failure representation
    - _Requirements: 13.4, 13.5, 13.6, 14.2, 14.4, 14.5_

  - [x] 7.12 Verify pathfinding group
    - Run `dotnet build HarvestingCore.sln`
    - _Requirements: 13.1, 14.1_

- [ ] 8. Checkpoint - pathfinding
  - Ensure the solution builds clean, ask the user if questions arise.
  - _Requirements: 13.8, 14.8_

- [x] 9. Agent layer
  - [x] 9.1 Implement `PendingMutations`, `AgentContext`, and registration-only `AgentManager`
    - Create `src/HarvestingCore/Coordination/PendingMutations.cs` with deduplicated, insertion-ordered `TransferReadyAgentIds` and `AssistanceCleanupAgentIds`, `RedistributionRequested`, the matching `Enqueue*`/`RequestRedistribution` methods, and `Clear`
    - Create `src/HarvestingCore/Coordination/AgentManager.cs` with the `List<Agent>`/`List<Harvester>`/`List<Tractor>` registration-ordered collections, the id lookup dictionary (never iterated), `Register` assigning `RegistrationIndex` and the initial `IDLE` state, rejecting a duplicate id with `InvalidOperationException` naming the id and a null agent with `ArgumentNullException`, plus `TryGetAgent`; the coordination methods land in task 12
    - Create `src/HarvestingCore/Agents/AgentContext.cs` exposing `Model`, `Config`, `PathFinder`, `Manager`, `Pending`, `TickIndex`, and a discharge sink so `DumpLoad` can accumulate the total without `AgentContext` referencing `World` (keep `World.DischargedTotal` as the read-only projection over that sink)
    - _Requirements: 16.3, 16.4, 16.5_

  - [x] 9.2 Create the state abstraction and registry with hook-only shells
    - Create `src/HarvestingCore/Agents/StateId.cs` and `src/HarvestingCore/Agents/AgentRole.cs`
    - Create `src/HarvestingCore/Agents/States/AgentState.cs` with abstract `Id`, virtual `OnEnter`, abstract `Execute`, virtual `OnExit`
    - Create the eight concrete classes `IdleState`, `HarvestState`, `GoToRefuelState`, `GoToDumpState`, `GoToMeetingPointState`, `WaitTractorState`, `WaitHarvesterState`, `InactiveState` as immutable, hook-only shells; behaviour bodies land in task 10
    - Create `src/HarvestingCore/Agents/States/AgentStateRegistry.cs` holding exactly one singleton per `StateId` with `Get(StateId)`
    - _Requirements: 8.1, 9.1_

  - [x] 9.3 Implement the `Agent` base mechanics
    - Create `src/HarvestingCore/Agents/Agent.cs` with the public surface from the design (`Id`, `RegistrationIndex`, `Position`, `Fuel`, `Load`, `MaxLoad`, `MaxFuel`, `FuelConsumption`, `CurrentState`, read-only `Path`, `MeetingPoint`, `PathInvalidatedThisTick`, `ArrivedAtDestination`, `InactiveSinceTick`, abstract `Role`)
    - Constructor validation: `maxLoad < 1`, `maxFuel < 1`, `fuelConsumption < 1`, null/empty/whitespace `id`, and a start position out of bounds or `Blocked`, each with an exception naming the offending value; unspecified limits fall back to the `SimulationConfig` defaults
    - Implement `Transition(next, context)`: return immediately when `next == CurrentState`, otherwise `OnExit` on the outgoing state, set `CurrentState`, `OnEnter` on the incoming state, in that order
    - Implement `Move(context)`: no-op returning the current position on an empty path; on a `Blocked` next cell leave the position, clear the path and set `PathInvalidatedThisTick`; otherwise accept only a position exactly one `MoveOrder` offset away, advance, debit `FuelConsumption`, call `RegisterEntry` on the entered cell, and set `ArrivedAtDestination` when the final path position is reached
    - Implement `Refuel`, `TryEstimateFuelReserve` (nearest-station path cost × `FuelConsumption`, `false` when no station is reachable or the collection is empty), `DumpLoad`, `ReceiveLoad` (accepts `min(offered, free capacity)` and returns the accepted amount), `RemoveLoad`, `SetPath`, `ClearPath`
    - Implement `SetFuel`/`SetLoad` clamping to `[0, MaxFuel]` and `[0, MaxLoad]`, and the internal per-tick flags `RefuelledThisTick`/`DumpedThisTick` set by `Refuel`/`DumpLoad`
    - Leave `Execute` and the abstract `TransitionTable` member to task 11
    - _Requirements: 3.1, 3.2, 3.5, 3.6, 3.7, 3.8, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4, 6.1, 6.2, 9.10_

  - [x] 9.4 Implement `Harvester` and `Tractor`
    - Create `src/HarvestingCore/Agents/Harvester.cs` with `TryHarvest` (succeeds only on a `Crop` cell at the harvester position with `Load < MaxLoad`, setting the cell to `Harvested` and raising load by one), `IsAreaFinished` (no owned cell holds `Crop`), `HasAssignedCrop`, `AssistanceRequested`
    - Create `src/HarvestingCore/Agents/Tractor.cs` with `AssignedHarvesterId` (null when unpaired)
    - _Requirements: 7.1, 7.2, 7.3, 7.5, 8.3, 9.4_

  - [x] 9.11 Verify agent group
    - Run `dotnet build HarvestingCore.sln`
    - _Requirements: 3.1, 4.1_

- [x] 10. State behaviours
  - [x] 10.1 Implement the movement-and-action states
    - Fill `HarvestState.Execute`: `TryHarvest`, and on failure request or follow a path to the best owned `Crop` cell and `Move`
    - Fill `GoToRefuelState`: `OnEnter` plans `PathToCell` to the nearest refuel station, `Execute` moves and refuels on arrival, `OnExit` clears the path
    - Fill `GoToDumpState`: `OnEnter` plans `PathToCell` to the nearest dump site, `Execute` moves and dumps on arrival, `OnExit` clears the path
    - Fill `GoToMeetingPointState`: `OnEnter` plans `PathToCell` to `MeetingPoint`, `Execute` moves, `OnExit` clears the path
    - _Requirements: 5.1, 6.1, 7.1, 7.4_

  - [x] 10.2 Implement the idle, waiting, and inactive states
    - Fill `IdleState.OnEnter` to clear the path; `Execute` does nothing and waits for a guard
    - Fill `WaitTractorState` and `WaitHarvesterState`: `OnEnter` clears the path and enqueues `EnqueueTransferReady`; `Execute` does nothing so the transfer is resolved after all agents run
    - Fill `InactiveState.OnEnter`: clear the path, record `InactiveSinceTick`, `EnqueueAssistanceCleanup`, and `RequestRedistribution` when the agent is a harvester; `Execute` does nothing so position and load are frozen
    - _Requirements: 12.7, 15.1, 15.2, 15.3, 16.2_

  - [x] 10.3 Verify state behaviours
    - Run `dotnet build HarvestingCore.sln`
    - _Requirements: 5.1, 6.1_

- [ ] 11. FSM tables and tick-level agent execution
  - [ ] 11.1 Implement `TransitionRule` and `TransitionTable`
    - Create `src/HarvestingCore/Agents/TransitionRule.cs` as a readonly struct with `Source`, `Target`, `Guard`, `RequirementRef`
    - Create `src/HarvestingCore/Agents/TransitionTable.cs` holding one flat priority-ordered `TransitionRule[]`, with `Evaluate` returning the first matching rule's target so the array index *is* the priority index and at most one transition happens per tick
    - _Requirements: 8.13, 9.11_

  - [ ] 11.2 Implement the two role transition tables
    - Create `src/HarvestingCore/Agents/TransitionTables.cs` with the harvester table's 13 rows and the tractor table's 10 rows in exactly the design's documented order, each row carrying its `RequirementRef`
    - Guards are pure predicates that never mutate; the refuel- and dump-completion rows read the `RefuelledThisTick`/`DumpedThisTick` per-tick flags rather than re-deriving from `Fuel == MaxFuel`
    - Include the station-suppression conjuncts `RefuelStations.Count > 0` and `DumpSites.Count > 0` so an empty collection suppresses the transition
    - Wire `Harvester.TransitionTable` and `Tractor.TransitionTable` to the corresponding tables
    - _Requirements: 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 8.10, 8.11, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 5.4, 6.4_

  - [ ] 11.3 Implement `Agent.Execute`
    - Clear `PathInvalidatedThisTick`, `ArrivedAtDestination`, `RefuelledThisTick`, `DumpedThisTick` at the top
    - Pre-empt with the fuel guard: a non-inactive agent at zero fuel transitions to `INACTIVE` and returns; an already-inactive agent returns immediately since `INACTIVE` is terminal
    - Run the current state's `Execute` exactly once, re-check fuel afterwards because a `Move` may have drained it, then evaluate the transition table and apply the single resulting transition
    - _Requirements: 3.3, 3.4, 8.12, 9.9, 15.1_

  - [ ] 11.6 Verify FSM group
    - Run `dotnet build HarvestingCore.sln`
    - _Requirements: 8.13, 9.11_

- [ ] 12. Checkpoint - agents and state machines
  - Ensure the solution builds clean, ask the user if questions arise.
  - _Requirements: 3.3, 8.1, 9.1_

- [ ] 13. Coordination layer
  - [ ] 13.1 Implement `AreaDistributor`
    - Create `src/HarvestingCore/Coordination/AreaDistributor.cs` with `Distribute(model, harvesters)`
    - Clear every owner first, then seed all non-`INACTIVE` harvesters in registration order (skipping `Blocked` or already-owned seed cells, assigning the seed cell to its own harvester), then run one FIFO BFS expanding through `MoveOrder` in sequence and claiming only unowned non-`Blocked` cells
    - With zero active harvesters nothing is seeded and every owner stays unassigned
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.9_

  - [ ] 13.6 Implement tractor selection and meeting point negotiation
    - Add `TrySelectTractor(harvester, harvesterField, out best)` to `AgentManager`: scan tractors in registration order, keep only `IDLE`, unpaired and non-`INACTIVE` candidates that the cost field reaches, pick the minimum cost, tie-break on the lowest id using `string.CompareOrdinal`
    - Add `TryNegotiateMeetingPoint(harvester, tractor, ctx, out meetingPoint)`: short-circuit to the harvester position when `Load == MaxLoad`; otherwise compute both cost fields once, scan cells in row-major order skipping `Blocked` and jointly unreachable cells, and take the strict minimum of the summed cost so the first (lowest `y`, then lowest `x`) minimum wins; report failure when no cell is jointly reachable
    - _Requirements: 10.1, 10.4, 10.5, 11.1, 11.2, 11.3, 11.4, 11.5, 15.4_

  - [ ] 13.7 Implement the assistance mapping and its lifecycle
    - Add the `_tractorToHarvester` / `_harvesterToTractor` dictionaries maintained as exact inverses, mutated only through private `LinkPair`/`UnlinkPair` which write or erase both sides together
    - Add `RequestAssistance(harvester, ctx, out tractor, out meetingPoint)` composing `ComputeCostField`, `TrySelectTractor` and `TryNegotiateMeetingPoint`, recording exactly one pair on success and leaving the mapping untouched on failure; on negotiation failure release any pre-existing pair
    - Add `ReleasePair`, `IsPaired`, `TryGetPartner`, and `AllInactive`
    - Add `ResolveTransfers(pending, ctx)` firing only when the two agents are paired, co-located at the negotiated meeting point, and hold `WAIT_TRACTOR` / `WAIT_HARVESTER`; use the single `ReceiveLoad` return value for both sides, mark the per-tick transfer flags, then release the pair
    - Add `ResolveAssistanceCleanup(pending, ctx)` draining cleanup requests in insertion order, releasing the pair and forcing a still-active partner to `IDLE` with `MeetingPoint` and `AssignedHarvesterId` reset, while leaving an already-inactive partner untouched
    - Add `ExecuteTick(ctx)` invoking each registered agent's `Execute` exactly once in registration order
    - _Requirements: 10.2, 10.3, 10.6, 10.7, 10.8, 15.4, 15.6, 16.1_

  - [ ] 13.12 Verify coordination group
    - Run `dotnet build HarvestingCore.sln`
    - _Requirements: 10.1, 11.1, 12.1_

- [ ] 14. World façade and tick pipeline
  - [ ] 14.1 Implement the `World` façade
    - Create `src/HarvestingCore/World.cs` (namespace `HarvestingCore`) owning the `WorldModel`, `AgentManager`, `PathFinder`, `SimulationConfig`, `IRandomSource`, `AreaDistributor` and `PendingMutations`
    - Expose read-only `TickIndex`, `DischargedTotal`, `IsHalted` (delegating to `AllInactive`), `Agents`, `Cells`
    - Implement `Register`, `GenerateGrid`, `RedistributeAreas`, and `internal AddDischarged` backing the discharge sink `AgentContext` writes through
    - _Requirements: 6.3, 15.6, 16.6, 18.2, 18.6_

  - [ ] 14.2 Implement `World.Tick` as the four ordered phases
    - Build one `AgentContext` per tick so every agent observes the same `TickIndex`
    - Phase 1: execute every registered agent exactly once in registration order, with cross-agent effects recorded in `PendingMutations` only
    - Phase 2: `ResolveAssistanceCleanup` before `ResolveTransfers`, so a pair whose member went inactive cannot transfer this tick
    - Phase 3: run `AreaDistributor.Distribute` at most once, only when redistribution was requested
    - Phase 4: clear pending mutations, then increment `TickIndex`
    - Hook `Harvester.IsAreaFinished` reporting into `RequestRedistribution` so a finished area triggers redistribution at the end of the current tick
    - _Requirements: 12.6, 12.7, 16.1, 16.2_

## Notes

- No test project is part of this build. All property/unit test sub-tasks and the hand-rolled test harness have been removed from scope.
- Checkpoints sit after the foundation, pathfinding, agent, and integration groups so failures surface where the cause is still local. "Ensure the solution builds clean" replaces test-based checkpoint verification.

## Coverage note

Every requirement 1 through 19 is covered by at least one implementation task, verified by build success only. Requirements map through the `_Requirements:` citations: 1 → 4.3/4.4, 2 → 4.2, 3 → 9.3/11.3, 4 → 9.3/9.6, 5 → 9.3/10.1, 6 → 9.3/10.1/14.1, 7 → 9.4/10.1, 8 and 9 → 11.2/11.3/11.4, 10 → 13.6/13.7, 11 → 13.6, 12 → 13.1/14.2, 13 and 14 → 7.4/7.5, 15 → 10.2/11.3/13.7, 16 → 9.1/14.1/14.2, 17 → 3.1, 18 → 3.2/14.1/15.2, 19 → n/a (test-harness requirement, out of scope).
