# Implementation Plan: Harvesting Multi-Agent System

## Overview

Bottom-up build of the pure agentic logic layer in C# with zero external dependencies. Two projects only: `src/HarvestingCore/` (netstandard2.1 class library) and `tests/HarvestingCore.Tests/` (net8.0 console app).

Order is dictated by the design's layering, with one deliberate inversion: the hand-rolled test harness and the reference oracles are built before the components they verify, so every later group ships with executable evidence. Within each group, implementation precedes its property tests, and each group ends with a build/run verification step.

Conventions used throughout:

- Sub-tasks marked `*` are optional (unit tests, property tests, reference-parity scenarios). Everything unmarked is required.
- Every property test lives in `tests/HarvestingCore.Tests/Properties/PropertyNN.cs`, opens with the comment `// Feature: harvesting-multi-agent-system, Property {n}`, is registered in `TestRegistry`, and runs a **minimum of 100 iterations**.
- `_Requirements:` cites granular acceptance criteria from `requirements.md`. `_Properties:` cites the numbered correctness properties from `design.md`.

## Tasks

- [ ] 1. Solution and project scaffolding
  - [ ] 1.1 Create the solution and both project files
    - Create `HarvestingCore.sln` referencing both projects
    - Create `src/HarvestingCore/HarvestingCore.csproj`: `<TargetFramework>netstandard2.1</TargetFramework>`, `LangVersion` set to a level that compiles without runtime-side attributes (no `record`/`init`), nullable disabled, **zero `PackageReference` elements**
    - Create `tests/HarvestingCore.Tests/HarvestingCore.Tests.csproj`: `net8.0`, `OutputType=Exe`, a single `ProjectReference` to `src/HarvestingCore/HarvestingCore.csproj`, **zero `PackageReference` elements**
    - Create the folder skeleton from the design's test project structure: `Framework/`, `Generators/`, `Reference/`, `Properties/`, `Units/`
    - _Requirements: 18.1, 19.1_

  - [ ] 1.2 Verify the empty solution builds
    - Add a temporary placeholder type in the library and a `Program.Main` stub in the test project so both compile
    - Run `dotnet build HarvestingCore.sln` and confirm zero warnings related to framework or dependency resolution
    - _Requirements: 18.1, 19.1_

- [ ] 2. Hand-rolled test harness
  - [ ] 2.1 Implement assertions and the failure carrier
    - Create `tests/HarvestingCore.Tests/Framework/AssertionException.cs` carrying `Expected`, `Actual`, and `Context` strings
    - Create `tests/HarvestingCore.Tests/Framework/Assert.cs` with `True`, `False`, `Equal<T>`, `NotEqual<T>`, `SequenceEqual<T>`, `Throws<TException>` (returning the caught exception so message text can be asserted), and `Fail`; every helper throws `AssertionException` with observed values populated
    - _Requirements: 19.2, 19.3_

  - [ ] 2.2 Implement the test registry and result model
    - Create `tests/HarvestingCore.Tests/Framework/TestCase.cs` (`Name`, `Kind` of Unit or Property, `Body` as `Action<int,int>` receiving seed and iteration count)
    - Create `tests/HarvestingCore.Tests/Framework/TestResult.cs` (`Name`, `Passed`, `Expected`, `Actual`, `FailingInput`, `Seed`)
    - Create `tests/HarvestingCore.Tests/Framework/TestRegistry.cs` exposing `IReadOnlyList<TestCase> All` in fixed registration order, plus `Register` helpers for unit and property cases
    - Create `tests/HarvestingCore.Tests/Framework/TestRunner.cs` with `Run(TestCase, seed, iterations)` catching `AssertionException` and unexpected exceptions into a `TestResult`
    - _Requirements: 19.2, 19.3_

  - [ ] 2.3 Implement `Program.Main` with argument parsing and reporting
    - Create `tests/HarvestingCore.Tests/Program.cs` replacing the 1.2 stub
    - Parse `--seed <int>` (default 20240101), `--iterations <int>` (default 200), `--only <name>` filter
    - Print `seed=… iterations=…`, then for each failure print the name, expected, actual, failing input, and the reproduction line `dotnet run --project tests/HarvestingCore.Tests -- --seed {seed} --only {name}`
    - Print `passed=… failed=…` and return exit code 0 when `failed == 0`, otherwise 1
    - _Requirements: 19.2, 19.3, 19.5_

  - [ ] 2.4 Implement the property runner
    - Create `tests/HarvestingCore.Tests/Framework/PropertyRunner.cs` with the generate → run → shrink → report loop
    - Derive each property's stream as `rootRandom.Fork(propertyIndex)` so adding a property never perturbs existing inputs
    - On failure, capture the failing input rendering, the pre-shrink input, and the reproducing seed into the `TestResult`
    - Enforce a minimum of 100 iterations regardless of a lower `--iterations` argument
    - Note: depends on `IRandomSource` from task 3.2; sequence 3.2 before wiring this in, or stage against the interface and compile after 3.2
    - _Requirements: 19.4, 19.5_

  - [ ] 2.5 Write the harness self-consistency property test
    - **Property 29: Test harness self-consistency**
    - Create `tests/HarvestingCore.Tests/Properties/Property29.cs`
    - Generator: synthetic `TestRegistry` instances with a generated number of passing cases and planted failing cases, including one planted failing property whose input depends on the seed
    - Assert reported pass/fail counts equal the registry composition, the computed exit code is non-zero iff at least one case failed, every failed case name and its observed values appear in the captured output, and replaying the printed seed reproduces the identical failing input
    - Route runner output through an injectable `TextWriter` so the test can capture it without touching the console
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 29`
    - _Requirements: 19.2, 19.3, 19.4, 19.5_
    - _Properties: 29_

  - [ ] 2.6 Verify the harness runs green
    - Run `dotnet build HarvestingCore.sln` then `dotnet run --project tests/HarvestingCore.Tests`
    - Confirm the summary line prints and the exit code is 0; deliberately plant one failing case, confirm exit code 1 and the reproduction line, then remove it
    - _Requirements: 19.2, 19.3_

- [ ] 3. Configuration and determinism support
  - [ ] 3.1 Implement `HeuristicKind` and `SimulationConfig`
    - Create `src/HarvestingCore/Configuration/HeuristicKind.cs` with `Zero`, `Octile`, `SquaredEuclidean`
    - Create `src/HarvestingCore/Configuration/SimulationConfig.cs` as a sealed immutable class with get-only properties for all tunables from the design table and a constructor with the documented defaults (`dumpPreferenceFactor 1.0`, `capacityFactor 0.5`, `harvesterFuelReserveMultiplier 1.2`, `tractorFuelReserveMultiplier 2.5`, `cropCost 1`, `emptyCost 2`, `harvestedCost 10`, `heuristic Octile`, `defaultMaxLoad 100`, `defaultMaxFuel 1000`, `defaultFuelConsumption 1`, `seed 20240101`, `cropDensity 0.55`, `blockedDensity 0.10`)
    - Validate in the constructor: `capacityFactor` outside `[0,1]`, negative `dumpPreferenceFactor`, negative reserve multipliers, any terrain cost below 1 → `ArgumentOutOfRangeException` naming the offending parameter
    - Add `static SimulationConfig Default`, `MinimumTerrainCost`, and `TerrainCost(CellState)` throwing on `Blocked` as a programmer-error tripwire
    - _Requirements: 17.1, 17.2, 17.3, 17.4, 17.5_

  - [ ] 3.2 Implement `IRandomSource` and `DeterministicRandom`
    - Create `src/HarvestingCore/Configuration/IRandomSource.cs` with `Seed`, `NextInt(minInclusive, maxExclusive)`, `NextDouble()`, `Fork(int salt)`
    - Create `src/HarvestingCore/Configuration/DeterministicRandom.cs` implementing xorshift128+ with state seeded deterministically from the integer seed; `Fork(salt)` returns an independent stream that is a pure function of `(Seed, salt)`
    - Do not use `System.Random` anywhere; add a source comment stating why (unstable seeded algorithm across runtimes)
    - _Requirements: 18.3, 18.5_

  - [ ] 3.3 Wire `PropertyRunner` onto `IRandomSource`
    - Replace the staged random dependency in `Framework/PropertyRunner.cs` with `DeterministicRandom`, using `Fork(propertyIndex)` per property
    - _Requirements: 19.4, 19.5_

  - [ ]* 3.4 Write unit tests for `SimulationConfig.Default`
    - Create `tests/HarvestingCore.Tests/Units/SimulationConfigTests.cs` asserting every `Default` field value literally, and asserting each invalid-construction message names its parameter
    - _Requirements: 17.2, 17.3_

  - [ ] 3.5 Verify configuration group
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
    - _Requirements: 17.1, 18.3_

- [ ] 4. World model layer
  - [ ] 4.1 Implement `CellState`, `GridPosition`, and `MoveOrder`
    - Create `src/HarvestingCore/World/CellState.cs` with `Empty = 0`, `Crop = 1`, `Blocked = 2`, `Harvested = 3`
    - Create `src/HarvestingCore/World/GridPosition.cs` as a readonly struct implementing `IEquatable<GridPosition>` with `X`, `Y`, `Offset`, `IsNeighbourOf`, `Equals`/`GetHashCode`/`ToString`/`==`/`!=`, and `static int CompareRowMajor(a, b)` ordering by `y` then `x`
    - Create `src/HarvestingCore/World/MoveOrder.cs` with `Offsets` in the exact sequence `(0,1), (1,0), (-1,0), (0,-1), (-1,1), (-1,-1), (1,1), (1,-1)` and `Count = 8`
    - _Requirements: 4.4, 11.2, 18.4_

  - [ ] 4.2 Implement `Cell`
    - Create `src/HarvestingCore/World/Cell.cs` with `NoOwner = ""`, `State`, `Popularity`, `OwnerId`, and the operations `Harvest`, `Plant`, `IsOwnedBy`, `AssignOwner`, `ClearOwner`, `RegisterEntry` (increments popularity and returns the new value), plus `internal SetStateForGeneration`
    - Initialise popularity to zero and owner to `NoOwner`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5, 2.6, 2.7_

  - [ ] 4.3 Implement `WorldModel` storage and queries
    - Create `src/HarvestingCore/World/WorldModel.cs` with a flat `Cell[]` indexed `y * Width + x`, `List<GridPosition>` backing fields for refuel stations and dump sites, and read-only projections `Cells`, `RefuelStations`, `DumpSites`
    - Constructor validates `width < 1` / `height < 1` with `ArgumentOutOfRangeException` naming the dimension, and rejects out-of-bounds or duplicated station/dump positions with `ArgumentException` naming the collection and position
    - Implement `InBounds`, `IndexOf`, `PositionOf`, `CellAt` (throws `ArgumentOutOfRangeException` naming `x` or `y`), `TryGetCell` (no-throw, leaves the matrix untouched), `IsPassable`
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5_

  - [ ] 4.4 Implement grid generation, serialisation, and parsing
    - Add `Generate(IRandomSource)` to `WorldModel`: returns `false` and leaves the matrix untouched when `IsGenerated` is already true; otherwise walks the flat array in row-major index order drawing once per cell from the random source using `CropDensity` and `BlockedDensity`, forces refuel and dump positions to `Empty` afterwards, sets `IsGenerated`, returns `true`
    - Add `Serialize()` producing the char-grid form (`.` empty, `W` crop, `#` blocked, `_` harvested) and `static Parse(text, refuel, dumps)` as its inverse
    - _Requirements: 1.6, 1.7, 1.8_

  - [ ] 4.5 Write the cell state machine property test
    - **Property 18: Cell state machine soundness**
    - Create `tests/HarvestingCore.Tests/Properties/Property18.cs`
    - Generator: random initial `CellState` plus a random sequence of harvest/plant/assign-owner operations (built with `Gen` once task 5.1 lands; until then generate inline from `DeterministicRandom` and refactor onto `Gen` in 5.1)
    - Oracle: the transition table transcribed from Requirements 2.1–2.4 as test-local data
    - Assert each success flag matches the table, a failed operation leaves state unchanged, repeated harvest succeeds at most once until a plant succeeds, plant-then-harvest returns the cell to `Harvested`, and ownership is reported for the assigned identifier only
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 18`
    - _Requirements: 2.1, 2.2, 2.3, 2.4, 2.5_
    - _Properties: 18_

  - [ ]* 4.6 Write unit tests for a fresh `Cell` and bounds behaviour
    - Create `tests/HarvestingCore.Tests/Units/CellTests.cs` and `tests/HarvestingCore.Tests/Units/WorldModelTests.cs`
    - Assert a fresh cell reports popularity zero and no owner; assert `TryGetCell` on an out-of-bounds position returns false without mutating the matrix; assert `IndexOf`/`PositionOf` round-trip over every index of a small grid
    - _Requirements: 1.3, 1.4, 2.7_

  - [ ] 4.7 Verify world model group
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
    - _Requirements: 1.1, 2.1_

- [ ] 5. Test generators, oracles, and shrinking
  - [ ] 5.1 Implement `Gen` combinators
    - Create `tests/HarvestingCore.Tests/Framework/Gen.cs` with `Int`, `Choose<T>`, `Array<T>`, `Frequency<T>`, `Bool`, and `Double`, all driven by `IRandomSource` so the corpus is a pure function of the run seed
    - Refactor the inline generation in `Properties/Property18.cs` onto these combinators
    - _Requirements: 19.4_

  - [ ] 5.2 Implement `GridGen`, `AgentGen`, `ConfigGen`, and `PathGen`
    - Create `tests/HarvestingCore.Tests/Generators/GridGen.cs` producing random grids plus the adversarial shapes the design calls for: fully blocked, single cell, a solid blocking wall splitting the grid, all-`Harvested` (path costs exceeding the reference `1e3` ceiling), and crops only in a far corner
    - Create `Generators/AgentGen.cs` producing fleets with random positions, fuel and load states, including near-exhausted fuel to force `INACTIVE`
    - Create `Generators/ConfigGen.cs` producing valid configs and one generator per invalid class from Property 19
    - Create `Generators/PathGen.cs` producing origin/target pairs (including out-of-bounds and `Blocked` targets) and heuristic selections
    - _Requirements: 19.4_

  - [ ] 5.3 Implement the reference oracles
    - Create `tests/HarvestingCore.Tests/Reference/ReferenceDijkstra.cs` as a naive `O(V^2)` linear-scan Dijkstra with **no heap** and no early termination, returning a full cost array
    - Create `Reference/ReferenceFloodFill.cs` as a plain BFS over non-`Blocked` cells returning a reachability bitmap, with no cost model
    - Create `Reference/GridSerializer.cs` rendering a cell matrix as a human-readable char block for failure printing and for the Property 17 round trip
    - _Requirements: 19.4_

  - [ ] 5.4 Implement the shrinker
    - Create `tests/HarvestingCore.Tests/Framework/Shrinker.cs` with the greedy `Shrink<T>(failing, candidates, stillFails, maxRounds = 50)` loop restarting from each smaller input
    - Implement per-type candidate strategies ordered smallest-first: `int` → `0`, `value/2`, `value-1`; grid → crop a row/column off each edge, `Blocked`→`Empty`, `Crop`→`Empty`; fleet → remove one agent per index, reduce one agent's fuel or load toward zero; tick count → `0`, `N/2`, `N-1`; config → replace each field with its default in turn
    - Wire the shrinker into `PropertyRunner` so failures print the shrunk input, the pre-shrink input, and the original seed
    - _Requirements: 19.4, 19.5_

  - [ ] 5.5 Write the grid generation round trip property test
    - **Property 17: Grid generation round trip and seed determinism**
    - Create `tests/HarvestingCore.Tests/Properties/Property17.cs`
    - Generator: random seeds and valid dimensions from `GridGen`; oracle: `Reference/GridSerializer.cs`
    - Assert `Parse(Serialize(g))` equals `g` cell-for-cell, two models generated from same-seeded random sources hold identical states at every position, and a second `Generate` on the same model returns false and leaves the matrix equal to its prior serialised snapshot
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 17`
    - _Requirements: 1.6, 1.7, 1.8, 19.4_
    - _Properties: 17_

  - [ ] 5.6 Verify generators and oracles
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
    - Confirm Properties 17, 18 and 29 each report at least 100 iterations
    - _Requirements: 19.2, 19.4_

- [ ] 6. Checkpoint - foundation
  - Ensure all tests pass, ask the user if questions arise.
  - Confirm neither `.csproj` contains a `PackageReference` and the library builds clean against `netstandard2.1`
  - _Requirements: 18.1, 19.1_

- [ ] 7. Pathfinding layer
  - [ ] 7.1 Implement `HeapEntry` and `DeterministicMinHeap`
    - Create `src/HarvestingCore/Pathfinding/HeapEntry.cs` as an internal readonly struct with `CellIndex`, `Priority`, `Sequence`
    - Create `src/HarvestingCore/Pathfinding/DeterministicMinHeap.cs` as an array-backed binary heap; `Push` stamps a monotonically increasing sequence number, and `Less(a,b)` is the strict total order `a.Priority < b.Priority || (equal priority && a.Sequence < b.Sequence)`
    - Implement `Count`, `Push`, `Pop`, `Clear` with `Clear` reusing the backing array
    - _Requirements: 13.7, 18.4_

  - [ ] 7.2 Write the heap ordering property test
    - **Property 27: Deterministic heap ordering**
    - Create `tests/HarvestingCore.Tests/Properties/Property27.cs`
    - Generator: random push sequences from `Gen` with forced priority collisions (a small priority alphabet so duplicates are frequent); no external oracle
    - Assert popping until empty yields non-decreasing priorities and that entries sharing a priority pop in strictly increasing insertion sequence order
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 27`
    - _Requirements: 13.7_
    - _Properties: 27_

  - [ ] 7.3 Implement `CostField` and `Heuristics`
    - Create `src/HarvestingCore/Pathfinding/CostField.cs` with `Unreachable = int.MaxValue`, `Width`, `Height`, `Origin`, read-only `Costs`, `IsReachable`, `CostAt`, and internal `MutableCosts`/`Predecessors` (`-1` for none)
    - Create `src/HarvestingCore/Pathfinding/Heuristics.cs` with `Zero`, `Octile(a, b, minCost) = minCost * max(|dx|, |dy|)`, and `SquaredEuclidean(a, b) = dx*dx + dy*dy`
    - _Requirements: 14.1, 14.7_

  - [ ] 7.4 Implement the shared search skeleton in `PathFinder`
    - Create `src/HarvestingCore/Pathfinding/PathFinder.cs` holding `WorldModel`, `SimulationConfig`, and reused scratch state (`DeterministicMinHeap`, `int[] _costs`, `int[] _predecessors`, `bool[] _closed`) version-stamped rather than cleared so reuse stays `O(1)`
    - Implement `internal int StepCostInto(GridPosition)` attaching cost to the entered cell and returning the `Unreachable` sentinel for `Blocked`
    - Implement the shared loop: pop cheapest, skip stale closed entries, test the termination predicate on **pop**, expand neighbours in `MoveOrder` sequence skipping out-of-bounds and `Blocked`, never relax from a sentinel cost, relax on strict improvement recording the predecessor
    - Implement `Reconstruct(targetIndex)` walking the predecessor chain and reversing, so element 0 is the origin
    - _Requirements: 13.1, 13.2, 13.3, 14.1, 14.3, 14.6_

  - [ ] 7.5 Implement the four public search entry points
    - Add `PathToBestCell(origin, targetState, ownerFilter = null)` terminating on the first popped cell holding the target state and passing the owner filter; returns an empty list when none is reachable
    - Add `PathToCell(origin, target, heuristicOverride = null)` terminating on the target index, short-circuiting `origin == target` to a single-element path, and returning an empty list for out-of-bounds, `Blocked`, or unreachable targets
    - Add `ComputeCostField(origin)` running the loop with no termination predicate and no heuristic, copying the finalised costs and predecessors into a `CostField` snapshot
    - Add `TryCostToNearest(origin, targets, out best, out cost)` scanning a single cost field
    - Never return null: an empty `IReadOnlyList<GridPosition>` is the sole failure representation
    - _Requirements: 13.4, 13.5, 13.6, 14.2, 14.4, 14.5_

  - [ ] 7.6 Write the path well-formedness property test
    - **Property 9: Path well-formedness**
    - Create `tests/HarvestingCore.Tests/Properties/Property09.cs`
    - Generators: `GridGen` plus `PathGen` for origins, targets, target states, owner filters and heuristic kinds; oracle: `MoveOrder.Offsets` adjacency
    - Assert every non-empty path starts at the origin, ends at a cell satisfying the request, contains only in-bounds non-`Blocked` cells, and has every consecutive pair exactly one `MoveOrder` offset apart
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 9`
    - _Requirements: 13.3, 13.4, 13.6, 14.2, 14.3, 14.6, 4.4_
    - _Properties: 9_

  - [ ] 7.7 Write the path emptiness property test
    - **Property 10: Path emptiness matches unreachability**
    - Create `tests/HarvestingCore.Tests/Properties/Property10.cs`
    - Generators: `GridGen` walled, fully blocked and single-cell shapes plus `PathGen` out-of-bounds and `Blocked` targets; oracle: `Reference/ReferenceFloodFill.cs`
    - Assert the returned path is empty if and only if the flood fill reports the target unreachable
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 10`
    - _Requirements: 13.5, 14.4_
    - _Properties: 10_

  - [ ] 7.8 Write the path optimality property test
    - **Property 11: Path optimality under an admissible heuristic**
    - Create `tests/HarvestingCore.Tests/Properties/Property11.cs`
    - Generators: `GridGen`, `PathGen`, `ConfigGen` valid configs, parameterised over `Zero` and `Octile`; oracle: `Reference/ReferenceDijkstra.cs`
    - Assert the accumulated terrain cost of the `PathToCell` result equals the oracle cost, and that the `PathToBestCell` target is of minimum cost among all cells satisfying the target-state and owner-filter predicates
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 11`
    - _Requirements: 13.1, 13.2, 14.7_
    - _Properties: 11_

  - [ ] 7.9 Write the path idempotence property test
    - **Property 12: Path idempotence**
    - Create `tests/HarvestingCore.Tests/Properties/Property12.cs`
    - Generators: `GridGen` and `PathGen`; no oracle
    - Assert two consecutive calls on an unmutated grid return sequence-equal paths, covering both `PathToBestCell` and `PathToCell`, and that scratch-array reuse does not leak state between calls
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 12`
    - _Requirements: 13.8, 14.8_
    - _Properties: 12_

  - [ ]* 7.10 Write unit tests for pathfinding boundaries
    - Create `tests/HarvestingCore.Tests/Units/PathFinderTests.cs`
    - Assert `PathToCell(p, p)` returns exactly `[p]`, that an all-`Harvested` corridor longer than cost 1000 still returns a path (the `int.MaxValue` sentinel deviation), and that a `Blocked` target returns an empty list
    - _Requirements: 14.4, 14.5_

  - [ ]* 7.11 Port the reference C++ pathfinding scenarios as parity tests
    - Create `tests/HarvestingCore.Tests/Units/ReferenceParityPathTests.cs`
    - Transcribe the 12x10 char grid shared by `reference/algorithms/path_to_best.cpp` and `path_to_cell.cpp` and the agent origin `(1,1)`
    - Assert `PathToBestCell` reproduces the reference Dijkstra outcome, and `PathToCell` to `(8,5)` under `HeuristicKind.SquaredEuclidean` reproduces the reference A* outcome
    - _Requirements: 13.1, 14.1_

  - [ ] 7.12 Verify pathfinding group
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
    - _Requirements: 13.1, 14.1_

- [ ] 8. Checkpoint - pathfinding
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 13.8, 14.8_

- [ ] 9. Agent layer
  - [ ] 9.1 Implement `PendingMutations`, `AgentContext`, and registration-only `AgentManager`
    - Create `src/HarvestingCore/Coordination/PendingMutations.cs` with deduplicated, insertion-ordered `TransferReadyAgentIds` and `AssistanceCleanupAgentIds`, `RedistributionRequested`, the matching `Enqueue*`/`RequestRedistribution` methods, and `Clear`
    - Create `src/HarvestingCore/Coordination/AgentManager.cs` with the `List<Agent>`/`List<Harvester>`/`List<Tractor>` registration-ordered collections, the id lookup dictionary (never iterated), `Register` assigning `RegistrationIndex` and the initial `IDLE` state, rejecting a duplicate id with `InvalidOperationException` naming the id and a null agent with `ArgumentNullException`, plus `TryGetAgent`; the coordination methods land in task 12
    - Create `src/HarvestingCore/Agents/AgentContext.cs` exposing `Model`, `Config`, `PathFinder`, `Manager`, `Pending`, `TickIndex`, and a discharge sink so `DumpLoad` can accumulate the total without `AgentContext` referencing `World` (keep `World.DischargedTotal` as the read-only projection over that sink)
    - _Requirements: 16.3, 16.4, 16.5_

  - [ ] 9.2 Create the state abstraction and registry with hook-only shells
    - Create `src/HarvestingCore/Agents/StateId.cs` and `src/HarvestingCore/Agents/AgentRole.cs`
    - Create `src/HarvestingCore/Agents/States/AgentState.cs` with abstract `Id`, virtual `OnEnter`, abstract `Execute`, virtual `OnExit`
    - Create the eight concrete classes `IdleState`, `HarvestState`, `GoToRefuelState`, `GoToDumpState`, `GoToMeetingPointState`, `WaitTractorState`, `WaitHarvesterState`, `InactiveState` as immutable, hook-only shells; behaviour bodies land in task 10
    - Create `src/HarvestingCore/Agents/States/AgentStateRegistry.cs` holding exactly one singleton per `StateId` with `Get(StateId)`
    - _Requirements: 8.1, 9.1_

  - [ ] 9.3 Implement the `Agent` base mechanics
    - Create `src/HarvestingCore/Agents/Agent.cs` with the public surface from the design (`Id`, `RegistrationIndex`, `Position`, `Fuel`, `Load`, `MaxLoad`, `MaxFuel`, `FuelConsumption`, `CurrentState`, read-only `Path`, `MeetingPoint`, `PathInvalidatedThisTick`, `ArrivedAtDestination`, `InactiveSinceTick`, abstract `Role`)
    - Constructor validation: `maxLoad < 1`, `maxFuel < 1`, `fuelConsumption < 1`, null/empty/whitespace `id`, and a start position out of bounds or `Blocked`, each with an exception naming the offending value; unspecified limits fall back to the `SimulationConfig` defaults
    - Implement `Transition(next, context)`: return immediately when `next == CurrentState`, otherwise `OnExit` on the outgoing state, set `CurrentState`, `OnEnter` on the incoming state, in that order
    - Implement `Move(context)`: no-op returning the current position on an empty path; on a `Blocked` next cell leave the position, clear the path and set `PathInvalidatedThisTick`; otherwise accept only a position exactly one `MoveOrder` offset away, advance, debit `FuelConsumption`, call `RegisterEntry` on the entered cell, and set `ArrivedAtDestination` when the final path position is reached
    - Implement `Refuel`, `TryEstimateFuelReserve` (nearest-station path cost × `FuelConsumption`, `false` when no station is reachable or the collection is empty), `DumpLoad`, `ReceiveLoad` (accepts `min(offered, free capacity)` and returns the accepted amount), `RemoveLoad`, `SetPath`, `ClearPath`
    - Implement `SetFuel`/`SetLoad` clamping to `[0, MaxFuel]` and `[0, MaxLoad]`, and the internal per-tick flags `RefuelledThisTick`/`DumpedThisTick` set by `Refuel`/`DumpLoad`
    - Leave `Execute` and the abstract `TransitionTable` member to task 11
    - _Requirements: 3.1, 3.2, 3.5, 3.6, 3.7, 3.8, 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 5.3, 5.4, 6.1, 6.2, 9.10_

  - [ ] 9.4 Implement `Harvester` and `Tractor`
    - Create `src/HarvestingCore/Agents/Harvester.cs` with `TryHarvest` (succeeds only on a `Crop` cell at the harvester position with `Load < MaxLoad`, setting the cell to `Harvested` and raising load by one), `IsAreaFinished` (no owned cell holds `Crop`), `HasAssignedCrop`, `AssistanceRequested`
    - Create `src/HarvestingCore/Agents/Tractor.cs` with `AssignedHarvesterId` (null when unpaired)
    - _Requirements: 7.1, 7.2, 7.3, 7.5, 8.3, 9.4_

  - [ ]* 9.5 Write the transition mechanics property test
    - **Property 22: Transition mechanics**
    - Create `tests/HarvestingCore.Tests/Properties/Property22.cs`
    - Generator: every `(source, target)` `StateId` pair including equal pairs, from `Gen.Choose`; oracle: an instrumented hook call log
    - Assert a transition to a different target logs `OnExit` on the source then `OnEnter` on the target with the state change between them, and that a transition to the current state logs neither hook and changes nothing
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 22`
    - _Requirements: 3.5, 3.6_
    - _Properties: 22_

  - [ ]* 9.6 Write the movement mechanics property test
    - **Property 23: Movement mechanics**
    - Create `tests/HarvestingCore.Tests/Properties/Property23.cs`
    - Generators: `GridGen` plus `PathGen`-derived valid paths, including paths whose next step is `Blocked`; oracle: the input path itself
    - Assert stepping visits exactly the path positions in order, shrinks the remaining path by one per call, debits `FuelConsumption` per completed step, raises the entered cell's popularity by exactly one, reports arrival at the final position, leaves the position unchanged once exhausted, and on a `Blocked` next step leaves the position, clears the path and records invalidation
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 23`
    - _Requirements: 4.1, 4.2, 4.3, 4.5, 4.6, 2.6_
    - _Properties: 23_

  - [ ]* 9.7 Write the station operations property test
    - **Property 24: Station operations succeed exactly at stations**
    - Create `tests/HarvestingCore.Tests/Properties/Property24.cs`
    - Generators: `GridGen` and `AgentGen` random positions against random station and dump sets, random fuel and load; oracle: `Reference/ReferenceDijkstra.cs`
    - Assert refuel succeeds iff the agent is on a refuel station and sets fuel to `MaxFuel` in exactly that case, dump succeeds iff the agent is on a dump site and in exactly that case zeroes load and raises the discharged total by the previous load, and the fuel reserve estimate equals the oracle minimum cost to the nearest station × `FuelConsumption`, reported unavailable when no station is reachable
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 24`
    - _Requirements: 5.1, 5.2, 5.3, 6.1, 6.2_
    - _Properties: 24_

  - [ ]* 9.8 Write the harvesting property test
    - **Property 25: Harvesting and area completion**
    - Create `tests/HarvestingCore.Tests/Properties/Property25.cs`
    - Generators: `GridGen` with owner stamping plus `AgentGen` load states; oracle: a direct owned-cell scan
    - Assert harvest succeeds iff the cell holds `Crop` and `Load < MaxLoad`, that on success the cell becomes `Harvested` and load rises by exactly one, that on failure neither changes, that every path target requested during a run is a cell the harvester owns, and that the area reports finished iff no owned cell holds `Crop`
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 25`
    - _Requirements: 7.1, 7.2, 7.3, 7.4, 7.5_
    - _Properties: 25_

  - [ ]* 9.9 Write the resource bound property test
    - **Property 1: Resource bound invariant**
    - Create `tests/HarvestingCore.Tests/Properties/Property01.cs`
    - Generators: `AgentGen` fleets and `ConfigGen` configs driving random sequences of `SetLoad`/`SetFuel`/`ReceiveLoad`/`RemoveLoad`/`Move`/`Refuel`/`DumpLoad`, extended to full tick runs once `World` exists; no oracle
    - Assert `0 <= Load <= MaxLoad` and `0 <= Fuel <= MaxFuel` immediately after every operation and at the end of every tick
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 1`
    - _Requirements: 3.7, 3.8, 9.10_
    - _Properties: 1_

  - [ ]* 9.10 Write unit tests for agent boundaries
    - Create `tests/HarvestingCore.Tests/Units/AgentTests.cs`
    - Assert `Move` on an empty path returns the position unchanged, harvest at full load fails leaving the cell `Crop`, and each construction-validation message names its offending value with the literal expected text
    - _Requirements: 3.2, 4.2, 7.3_

  - [ ] 9.11 Verify agent group
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
    - _Requirements: 3.1, 4.1_

- [ ] 10. State behaviours
  - [ ] 10.1 Implement the movement-and-action states
    - Fill `HarvestState.Execute`: `TryHarvest`, and on failure request or follow a path to the best owned `Crop` cell and `Move`
    - Fill `GoToRefuelState`: `OnEnter` plans `PathToCell` to the nearest refuel station, `Execute` moves and refuels on arrival, `OnExit` clears the path
    - Fill `GoToDumpState`: `OnEnter` plans `PathToCell` to the nearest dump site, `Execute` moves and dumps on arrival, `OnExit` clears the path
    - Fill `GoToMeetingPointState`: `OnEnter` plans `PathToCell` to `MeetingPoint`, `Execute` moves, `OnExit` clears the path
    - _Requirements: 5.1, 6.1, 7.1, 7.4_

  - [ ] 10.2 Implement the idle, waiting, and inactive states
    - Fill `IdleState.OnEnter` to clear the path; `Execute` does nothing and waits for a guard
    - Fill `WaitTractorState` and `WaitHarvesterState`: `OnEnter` clears the path and enqueues `EnqueueTransferReady`; `Execute` does nothing so the transfer is resolved after all agents run
    - Fill `InactiveState.OnEnter`: clear the path, record `InactiveSinceTick`, `EnqueueAssistanceCleanup`, and `RequestRedistribution` when the agent is a harvester; `Execute` does nothing so position and load are frozen
    - _Requirements: 12.7, 15.1, 15.2, 15.3, 16.2_

  - [ ] 10.3 Verify state behaviours
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
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

  - [ ] 11.4 Write the transition table conformance property test
    - **Property 20: Transition table conformance and priority order**
    - Create `tests/HarvestingCore.Tests/Properties/Property20.cs`
    - Generators: `Gen.Choose` over roles and source states plus generated subsets of that state's guards forced true via constructed agent and world state; oracle: the ordered transition table itself
    - Assert the applied transition is the target of the lowest-index matching rule, that at most one transition occurs per tick, that no transition occurs when no guard holds, and that the tractor `WAIT_HARVESTER` dichotomy resolves to `GO_TO_DUMP` when `Load >= MaxLoad * CapacityFactor` and `IDLE` otherwise
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 20`
    - _Requirements: 8.2, 8.3, 8.4, 8.5, 8.6, 8.7, 8.8, 8.9, 8.10, 8.11, 8.13, 9.2, 9.3, 9.4, 9.5, 9.6, 9.7, 9.8, 9.11_
    - _Properties: 20_

  - [ ]* 11.5 Write the fuel pre-emption property test
    - **Property 21: Fuel exhaustion pre-empts every transition**
    - Create `tests/HarvestingCore.Tests/Properties/Property21.cs`
    - Generators: `Gen.Choose` over roles and every non-`INACTIVE` source state, with `AgentGen` forcing arbitrary guard configurations at zero fuel; no oracle
    - Assert an agent at zero fuel ends the tick in `INACTIVE` with an empty path regardless of which other guards hold, and that no later tick changes its state
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 21`
    - _Requirements: 8.12, 9.9, 15.1_
    - _Properties: 21_

  - [ ] 11.6 Verify FSM group
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
    - _Requirements: 8.13, 9.11_

- [ ] 12. Checkpoint - agents and state machines
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 3.3, 8.1, 9.1_

- [ ] 13. Coordination layer
  - [ ] 13.1 Implement `AreaDistributor`
    - Create `src/HarvestingCore/Coordination/AreaDistributor.cs` with `Distribute(model, harvesters)`
    - Clear every owner first, then seed all non-`INACTIVE` harvesters in registration order (skipping `Blocked` or already-owned seed cells, assigning the seed cell to its own harvester), then run one FIFO BFS expanding through `MoveOrder` in sequence and claiming only unowned non-`Blocked` cells
    - With zero active harvesters nothing is seeded and every owner stays unassigned
    - _Requirements: 12.1, 12.2, 12.3, 12.4, 12.5, 12.9_

  - [ ] 13.2 Write the partition disjointness property test
    - **Property 6: Partition disjointness**
    - Create `tests/HarvestingCore.Tests/Properties/Property06.cs`; generators: `GridGen` plus `AgentGen` harvester lists; no oracle
    - Assert every cell carries at most one owner after distribution and that no `Blocked` cell carries any owner
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 6`
    - _Requirements: 12.3, 12.4_
    - _Properties: 6_

  - [ ] 13.3 Write the partition reachability property test
    - **Property 7: Partition reachability and coverage**
    - Create `tests/HarvestingCore.Tests/Properties/Property07.cs`; generators: `GridGen` plus `AgentGen`; oracle: `Reference/ReferenceFloodFill.cs`
    - Assert every owned cell is flood-fill reachable from its owning harvester through non-`Blocked` cells, that every non-`Blocked` cell reachable from some active seed carries an owner, and that no owner from a previous distribution survives
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 7`
    - _Requirements: 12.1, 12.2, 12.4, 12.5_
    - _Properties: 7_

  - [ ]* 13.4 Write the partition determinism property test
    - **Property 8: Partition determinism**
    - Create `tests/HarvestingCore.Tests/Properties/Property08.cs`; generators: `GridGen` plus ordered `AgentGen` harvester lists; no oracle
    - Assert two freshly built models produce identical assignments and two consecutive distributions on the same model produce identical assignments
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 8`
    - _Requirements: 12.5, 12.8_
    - _Properties: 8_

  - [ ]* 13.5 Port the reference C++ distribution scenario as a parity test
    - Create `tests/HarvestingCore.Tests/Units/ReferenceParityDistributionTests.cs`
    - Transcribe the 30x30 open grid and the five agent seeds from `reference/algorithms/area_distribution.cpp`, mapping the reference `char` ids onto string ids
    - Assert our multi-source BFS reproduces the reference territory shape, allowing for the documented deviation that the seed cell is owned by its own harvester rather than stamped `'X'`
    - _Requirements: 12.1, 12.2_

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

  - [ ] 13.8 Write the assignment exclusivity property test
    - **Property 13: Assignment exclusivity and lifecycle**
    - Create `tests/HarvestingCore.Tests/Properties/Property13.cs`
    - Generators: `AgentGen` fleets with forced fuel exhaustion plus a generated tick count; no oracle
    - Assert at the end of every tick that each tractor and each harvester appears in at most one entry, that the two maps are exact inverses, that no pair survives the tick its transfer completed, and that no pair survives the tick either member went `INACTIVE` with the survivor holding `IDLE`
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 13`
    - _Requirements: 10.2, 10.3, 10.6, 10.7_
    - _Properties: 13_

  - [ ] 13.9 Write the transfer conservation property test
    - **Property 14: Transfer conservation**
    - Create `tests/HarvestingCore.Tests/Properties/Property14.cs`
    - Generators: `AgentGen` pairs with random loads and capacities including offers exceeding free capacity; no oracle
    - Assert the tractor's load delta equals the harvester's load reduction, equals the value returned by `ReceiveLoad`, and equals `min(offered, MaxLoad - Load)`
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 14`
    - _Requirements: 9.10, 10.8_
    - _Properties: 14_

  - [ ]* 13.10 Write the meeting point determinism and symmetry property test
    - **Property 15: Meeting point determinism and symmetry**
    - Create `tests/HarvestingCore.Tests/Properties/Property15.cs`
    - Generators: `GridGen` plus `AgentGen` harvester-tractor pairs, including walled grids and full-load harvesters; no oracle
    - Assert two negotiations over identical inputs return identical positions and that swapping the argument order returns the same position
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 15`
    - _Requirements: 11.4_
    - _Properties: 15_

  - [ ]* 13.11 Write the selection and negotiation optimality property test
    - **Property 26: Tractor selection and meeting point optimality**
    - Create `tests/HarvestingCore.Tests/Properties/Property26.cs`
    - Generators: `GridGen` including walled and disconnected shapes plus `AgentGen` fleets with full-load and inactive members; oracle: two `Reference/ReferenceDijkstra.cs` cost arrays
    - Assert selection equals the oracle argmin over eligible tractors with the ordinal id tie-break, that an unsuccessful request leaves the mapping unchanged, that the meeting point equals the oracle argmin of summed cost over non-`Blocked` cells with the `y`-then-`x` tie-break, that a full-load harvester yields its own position, that a pair with no jointly reachable cell yields failure with the pair removed, and that inactive agents are never selected, never negotiated for and never seed a distribution
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 26`
    - _Requirements: 10.1, 10.4, 10.5, 11.1, 11.2, 11.3, 11.5, 15.4_
    - _Properties: 26_

  - [ ] 13.12 Verify coordination group
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
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

  - [ ] 14.3 Write the tick pipeline ordering property test
    - **Property 28: Tick pipeline ordering and registration**
    - Create `tests/HarvestingCore.Tests/Properties/Property28.cs`
    - Generators: `AgentGen` fleets, generated registration sequences and tick counts; oracle: an instrumented phase event log
    - Assert registration appends each agent to its role collection in order with initial `IDLE`, that each tick logs exactly one execute event per agent in registration index order, that every transfer event follows every execute event of that tick, that every redistribution event follows every transfer event, that at most one redistribution occurs per tick and only in ticks where a harvester finished its area or went `INACTIVE`, that `TickIndex` advances by exactly `N`, and that `IsHalted` is true iff every agent holds `INACTIVE`
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 28`
    - _Requirements: 12.6, 12.7, 15.6, 16.1, 16.2, 16.3, 16.4_
    - _Properties: 28_

  - [ ] 14.4 Write the simulation determinism property test
    - **Property 16: Simulation determinism**
    - Create `tests/HarvestingCore.Tests/Properties/Property16.cs`
    - Generators: `ConfigGen` configs, generated seeds, generated registration sequences and tick counts; oracle: the twin world
    - Assert two worlds built from identical inputs hold identical observable state after `N` ticks: tick index, discharged total, per-agent position, fuel, load, state and path, and per-cell state, popularity and owner
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 16`
    - _Requirements: 16.7, 18.5_
    - _Properties: 16_

  - [ ] 14.5 Write the harvest conservation property test
    - **Property 3: Harvest conservation**
    - Create `tests/HarvestingCore.Tests/Properties/Property03.cs`
    - Generators: `GridGen`, `AgentGen` fleets and a generated tick count; oracle: a grid diff counting `Crop → Harvested` transitions
    - Assert the transition count equals `DischargedTotal` plus the summed load of every registered agent including inactive ones
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 3`
    - _Requirements: 6.1, 7.1_
    - _Properties: 3_

  - [ ]* 14.6 Write the fuel monotonicity property test
    - **Property 2: Fuel monotonicity**
    - Create `tests/HarvestingCore.Tests/Properties/Property02.cs`; generators: `AgentGen` fleets plus a tick count, with per-tick snapshots; no oracle
    - Assert fuel is non-increasing between consecutive ticks unless the later tick set `RefuelledThisTick`, and that `0 <= Fuel <= MaxFuel` throughout
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 2`
    - _Requirements: 3.8, 5.1_
    - _Properties: 2_

  - [ ]* 14.7 Write the single state invariant property test
    - **Property 4: Single state invariant**
    - Create `tests/HarvestingCore.Tests/Properties/Property04.cs`; generators: `AgentGen` fleets plus a tick count; oracle: the per-role permitted state sets
    - Assert `Enum.IsDefined` holds for `CurrentState` at the end of every tick and that the value belongs to the set permitted for the agent's role
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 4`
    - _Requirements: 3.4, 8.1, 9.1_
    - _Properties: 4_

  - [ ]* 14.8 Write the inactive immobility property test
    - **Property 5: Inactive immobility**
    - Create `tests/HarvestingCore.Tests/Properties/Property05.cs`; generators: `AgentGen` fleets with low fuel to force exhaustion plus a tick count; no oracle
    - Snapshot position and load at the transition tick and assert both are identical on every later tick
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 5`
    - _Requirements: 15.2, 15.3_
    - _Properties: 5_

  - [ ]* 14.9 Write the error conditions property test
    - **Property 19: Error conditions**
    - Create `tests/HarvestingCore.Tests/Properties/Property19.cs`; generators: one `ConfigGen`/`GridGen`/`AgentGen` generator per invalid class; no oracle
    - Cover width or height below one, out-of-bounds queries, agent `maxLoad`/`maxFuel`/`fuelConsumption` below one, empty and duplicate agent identifiers, `capacityFactor` outside `[0,1]`, negative dump preference factor, negative reserve multiplier, terrain cost below one, and empty refuel or dump collections
    - Assert the expected exception type is thrown, the message names the offending value, no observable world state changes, and that across a full run the corresponding `GO_TO_REFUEL` / `GO_TO_DUMP` transition never occurs when the matching collection is empty
    - Minimum 100 iterations; tag `// Feature: harvesting-multi-agent-system, Property 19`
    - _Requirements: 1.2, 1.4, 3.2, 5.4, 6.4, 16.5, 17.3_
    - _Properties: 19_

  - [ ]* 14.10 Extend Property 1 to full tick runs
    - Update `tests/HarvestingCore.Tests/Properties/Property01.cs` to also drive `World.Tick` over a generated tick count and assert both bounds at the end of every tick
    - _Requirements: 3.7, 3.8_
    - _Properties: 1_

  - [ ]* 14.11 Write unit tests for the façade
    - Create `tests/HarvestingCore.Tests/Units/WorldTests.cs`
    - Assert zero active harvesters leaves every owner unassigned, that a duplicate registration throws with the literal expected message, and that `IsHalted` flips only once every agent is inactive
    - _Requirements: 12.9, 15.6, 16.5_

  - [ ] 14.12 Verify façade group
    - Run `dotnet build HarvestingCore.sln` and `dotnet run --project tests/HarvestingCore.Tests`
    - _Requirements: 16.1, 16.2_

- [ ] 15. Final integration pass
  - [ ] 15.1 Confirm full property coverage and iteration counts
    - Run `dotnet run --project tests/HarvestingCore.Tests` and confirm all 29 properties are registered in `TestRegistry` and each reports at least 100 iterations
    - Add a registry self-check asserting exactly one registered property test per property number 1 through 29, so a missing property fails the suite rather than passing silently
    - _Requirements: 19.2_
    - _Properties: 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29_

  - [ ] 15.2 Enforce the dependency and engine-independence constraints in code
    - Add a test that reads both `.csproj` files and asserts neither contains a `PackageReference` element
    - Add a test that scans every `.cs` file under `src/HarvestingCore/` and asserts no occurrence of `UnityEngine`, `System.Random`, `DateTime.Now`, `DateTime.UtcNow`, `Stopwatch`, or `Environment.TickCount`
    - Assert `SimulationConfig`, `WorldModel`, `Agent` and `World` expose no public setters on observable state
    - _Requirements: 18.1, 18.2, 18.3, 18.6_

  - [ ] 15.3 Final build and run verification
    - Run `dotnet build HarvestingCore.sln` and confirm the library compiles clean against `netstandard2.1` with no warnings
    - Run `dotnet run --project tests/HarvestingCore.Tests` and confirm exit code 0 with `failed=0`
    - _Requirements: 18.1, 19.1, 19.2, 19.3_

- [ ] 16. Final checkpoint
  - Ensure all tests pass, ask the user if questions arise.
  - _Requirements: 19.2, 19.3_

## Notes

- Sub-tasks marked `*` are optional and can be skipped for a faster MVP. The required set is everything the acceptance criteria mandate directly: the harness (Requirement 19.2, 19.3), the property runner with shrinking and seed reproduction (Requirement 19.4, 19.5), all production components, and the property tests for the properties whose criteria have no other executable check — 3, 6, 7, 9, 10, 11, 12, 13, 14, 16, 17, 18, 20, 27, 28, 29.
- The ported reference C++ parity scenarios (7.11, 13.5) are optional: they verify translation fidelity, which no acceptance criterion mandates, and the design lists them under example-level unit coverage.
- Checkpoints sit after the foundation, pathfinding, agent, and integration groups so failures surface where the cause is still local.

## Coverage note

Every requirement 1 through 19 and every correctness property 1 through 29 is covered by at least one task. Requirements map through the `_Requirements:` citations: 1 → 4.3/4.4/5.5, 2 → 4.2/4.5, 3 → 9.3/11.3, 4 → 9.3/9.6, 5 → 9.3/10.1, 6 → 9.3/10.1/14.1, 7 → 9.4/10.1, 8 and 9 → 11.2/11.3/11.4, 10 → 13.6/13.7, 11 → 13.6, 12 → 13.1/14.2, 13 and 14 → 7.4/7.5, 15 → 10.2/11.3/13.7, 16 → 9.1/14.1/14.2, 17 → 3.1, 18 → 3.2/14.1/15.2, 19 → 2.1 through 2.5 and 5.4. Properties map through the `_Properties:` citations, one dedicated task per property, enumerated and machine-checked in task 15.1.
