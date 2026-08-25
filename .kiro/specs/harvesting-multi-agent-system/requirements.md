# Requirements Document

## Introduction

This feature delivers the pure agentic logic for a harvesting multi-agent system: a deterministic, tick-driven simulation core written in C# with zero external dependencies (plain .NET class library plus a plain, runnable test project). The system models a grid world containing crops, obstacles, refuel stations and dump sites, populated by two agent types: Harvesters, which harvest crop cells and accumulate load, and Tractors, which meet Harvesters to receive load and transport that load to a dump site.

Each agent is driven by an explicit finite state machine. Coordination (assistance requests, Tractor-to-Harvester assignment, meeting point negotiation) and territory partitioning (multi-source BFS area distribution) are owned by an Agent_Manager, while path planning is owned by a Path_Finder that provides a cost-aware Dijkstra search to the nearest crop cell and an A* search to a specific cell.

Scope is strictly the engine-agnostic logic layer. The system exposes no rendering, no physics, no engine types, and no wall-clock time: all progression happens through discrete ticks. The layer is intended for later integration into a Unity project, so this specification forbids engine-coupled constructs while explicitly requiring an integration-ready surface (deterministic behavior, injectable configuration, seeded pseudo-random generation, observable state).

## Glossary

- **Harvesting_Core**: The complete .NET class library that contains all logic described in this document.
- **World**: The top-level façade component that owns a World_Model and an Agent_Manager and exposes the tick entry point plus coordination operations.
- **World_Model**: The component that owns grid dimensions, the cell matrix, refuel station positions, and dump site positions.
- **Cell**: The unit of the grid, holding a Cell_State, a popularity counter, and an owner identifier.
- **Cell_State**: The enumeration of cell contents with values EMPTY, CROP, BLOCKED, HARVESTED.
- **Agent_Manager**: The component that owns the agent collections, the assistance mapping, tick ordering, assistance handling, and meeting point negotiation.
- **Agent**: The abstract base component holding identifier, position, fuel, load, capacity limits, fuel consumption, current path, and the state machine.
- **Harvester**: The Agent subtype that harvests crop cells and requests assistance.
- **Tractor**: The Agent subtype that receives load from a Harvester and transports that load to a dump site.
- **State_Id**: The enumeration of agent states with values IDLE, HARVEST, GO_TO_REFUEL, GO_TO_DUMP, GO_TO_MEETING_POINT, WAIT_TRACTOR, WAIT_HARVESTER, INACTIVE.
- **Agent_State**: The abstract behavior component for one State_Id, exposing OnEnter, Execute, and OnExit operations.
- **State_Machine**: The part of an Agent that holds the current State_Id and performs transitions.
- **Path_Finder**: The component that computes paths over the World_Model grid.
- **Area_Distributor**: The component that partitions grid cells among Harvesters using multi-source breadth-first search.
- **Simulation_Config**: The immutable, injectable value object that supplies all tunable constants to the Harvesting_Core.
- **Random_Source**: The injectable, seeded pseudo-random number generator used by the Harvesting_Core.
- **Tick**: One discrete simulation step, the only unit of time in the Harvesting_Core.
- **Move_Order**: The fixed, ordered list of the eight neighbour offsets `(0,1), (1,0), (-1,0), (0,-1), (-1,1), (-1,-1), (1,1), (1,-1)` expressed as `(dx, dy)`.
- **Path**: An ordered sequence of grid positions where each consecutive pair differs by one offset from Move_Order.
- **Terrain_Cost**: The cost of entering a Cell, determined by the Cell_State of the entered Cell.
- **Refuel_Station**: A grid position at which an Agent restores fuel.
- **Dump_Site**: A grid position at which an Agent discharges load.
- **Meeting_Point**: A grid position at which a Harvester and an assigned Tractor transfer load.
- **Assistance_Mapping**: The Agent_Manager relation that pairs one Tractor with one Harvester.
- **Capacity_Factor**: The Simulation_Config ratio that decides whether a Tractor returns to IDLE or proceeds to GO_TO_DUMP after receiving load.
- **Dump_Preference_Factor**: The Simulation_Config factor named `k` used by a Harvester to compare dump distance against tractor distance.
- **Fuel_Reserve_Multiplier**: The Simulation_Config multiplier applied to the estimated fuel cost of reaching the nearest Refuel_Station.

## Assumptions

The following defaults were selected to keep the specification complete. Each default is a Simulation_Config value or a documented modelling choice, and each remains open to revision.

1. Grid coordinates use `(x, y)` where `x` is the column index and `y` is the row index, with origin `(0, 0)` at the top-left; the cell matrix is indexed `matrix[y][x]`.
2. A Harvester harvests the single Cell it currently occupies, at most one Cell per Tick.
3. An Agent consumes `FuelConsumption` fuel units per completed move between adjacent cells and consumes zero fuel while stationary.
4. The estimated fuel cost of reaching the nearest Refuel_Station equals the Path cost returned by the Path_Finder for that station multiplied by `FuelConsumption`.
5. `Capacity_Factor` defaults to `0.5`, `Dump_Preference_Factor` defaults to `1.0`, the Harvester Fuel_Reserve_Multiplier defaults to `1.2`, and the Tractor Fuel_Reserve_Multiplier defaults to `2.5`.
6. Terrain_Cost defaults are `CROP = 1`, `EMPTY = 2`, `HARVESTED = 10`, and BLOCKED cells are impassable.
7. INACTIVE is terminal within the Harvesting_Core: the state diagrams define no transition leaving INACTIVE, so no in-simulation recovery path exists.
8. Refuel_Station and Dump_Site collections belong to the World_Model even though the reference class diagram omits them, because the GO_TO_REFUEL and GO_TO_DUMP states require target positions.
9. Load and fuel are integer quantities, consistent with the reference class diagram.
10. Grid generation is the only consumer of the Random_Source; agent decision making is fully deterministic without random input.
11. A single tick executes every registered Agent exactly once, in registration order.

## Requirements

### Requirement 1: Grid World Model

**User Story:** As a simulation developer, I want a grid world model with typed cells, so that agents reason about crops, obstacles, and terrain cost over a well-defined space.

#### Acceptance Criteria

1. WHEN a World_Model is constructed with a width greater than zero and a height greater than zero, THE World_Model SHALL expose a cell matrix containing exactly `width * height` Cell instances.
2. IF a World_Model is constructed with a width less than one or a height less than one, THEN THE World_Model SHALL reject the construction with an argument error that names the invalid dimension.
3. WHEN a position with `x` in `[0, width)` and `y` in `[0, height)` is queried, THE World_Model SHALL return the Cell stored at `matrix[y][x]`.
4. IF a position outside the range `[0, width) x [0, height)` is queried, THEN THE World_Model SHALL report the position as out of bounds without modifying the cell matrix.
5. THE World_Model SHALL expose the Refuel_Station positions and the Dump_Site positions as read-only ordered collections.
6. WHEN grid generation is invoked on a World_Model whose generated flag is false, THE World_Model SHALL populate every Cell with a Cell_State, set the generated flag to true, and report success.
7. WHEN grid generation is invoked on a World_Model whose generated flag is true, THE World_Model SHALL leave the cell matrix unchanged and report failure.
8. WHEN grid generation is invoked twice with two Random_Source instances created from an identical seed and identical dimensions, THE World_Model SHALL produce two cell matrices with identical Cell_State values at every position.

### Requirement 2: Cell Semantics

**User Story:** As a simulation developer, I want cells to own their state transitions, ownership, and traffic counters, so that harvesting and area distribution operate on consistent data.

#### Acceptance Criteria

1. WHEN a harvest operation is applied to a Cell whose Cell_State is CROP, THE Cell SHALL set the Cell_State to HARVESTED and report success.
2. IF a harvest operation is applied to a Cell whose Cell_State is EMPTY, HARVESTED, or BLOCKED, THEN THE Cell SHALL leave the Cell_State unchanged and report failure.
3. WHEN a plant operation is applied to a Cell whose Cell_State is EMPTY or HARVESTED, THE Cell SHALL set the Cell_State to CROP and report success.
4. IF a plant operation is applied to a Cell whose Cell_State is CROP or BLOCKED, THEN THE Cell SHALL leave the Cell_State unchanged and report failure.
5. WHEN an owner identifier is assigned to a Cell, THE Cell SHALL report ownership for that identifier and SHALL report absence of ownership for every other identifier.
6. WHEN an Agent enters a Cell, THE Cell SHALL increase the popularity counter by exactly one and SHALL return the updated popularity value.
7. THE Cell SHALL initialise the popularity counter to zero and the owner identifier to the unassigned value.

### Requirement 3: Common Agent Attributes and Tick Execution

**User Story:** As a simulation developer, I want a common agent base with a single deterministic tick entry point, so that both agent types share movement, fuel, and load mechanics.

#### Acceptance Criteria

1. THE Agent SHALL expose a non-empty identifier, a grid position, a fuel value, a load value, a maximum load value, a maximum fuel value, a fuel consumption value, a current Path, and a current State_Id.
2. IF an Agent is constructed with a maximum load less than one, a maximum fuel less than one, a fuel consumption less than one, or an empty identifier, THEN THE Agent SHALL reject the construction with an argument error that names the invalid value.
3. WHEN an Agent execution is invoked for one Tick, THE Agent SHALL execute the Agent_State associated with the current State_Id exactly once.
4. THE Agent SHALL hold exactly one current State_Id at the end of every Tick.
5. WHEN a transition to a new State_Id is requested, THE Agent SHALL invoke OnExit on the outgoing Agent_State, set the current State_Id to the new State_Id, and invoke OnEnter on the incoming Agent_State, in that order.
6. WHEN a transition to the current State_Id is requested, THE Agent SHALL leave the current State_Id unchanged and skip the OnExit and OnEnter invocations.
7. THE Agent SHALL constrain the load value to the inclusive range `[0, maxLoad]` after every operation that changes the load value.
8. THE Agent SHALL constrain the fuel value to the inclusive range `[0, maxFuel]` after every operation that changes the fuel value.

### Requirement 4: Movement and Path Following

**User Story:** As a simulation developer, I want agents to follow assigned paths one step per tick with eight-directional movement, so that traversal is discrete, fuel-accurate, and engine-independent.

#### Acceptance Criteria

1. WHEN an Agent move is invoked WHILE the current Path contains at least one position, THE Agent SHALL set the Agent position to the first position of the current Path, remove that position from the current Path, and return the updated Agent position.
2. WHEN an Agent move is invoked WHILE the current Path is empty, THE Agent SHALL leave the Agent position unchanged and return the current Agent position.
3. WHEN an Agent completes a move to a new position, THE Agent SHALL decrease the fuel value by the fuel consumption value and SHALL increase the popularity counter of the entered Cell by one.
4. THE Agent SHALL accept a move only to a position that differs from the current Agent position by exactly one offset contained in Move_Order.
5. IF a move target Cell has the Cell_State BLOCKED, THEN THE Agent SHALL leave the Agent position unchanged, clear the current Path, and record a path invalidation event for the current Tick.
6. WHEN an Agent reaches the final position of the current Path, THE Agent SHALL report arrival at the Path destination for the remainder of the Tick.

### Requirement 5: Refuelling

**User Story:** As a simulation developer, I want agents to refuel at stations, so that operational range is bounded and fuel planning is observable.

#### Acceptance Criteria

1. WHEN a refuel operation is invoked WHILE the Agent position equals a Refuel_Station position, THE Agent SHALL set the fuel value to the maximum fuel value and report success.
2. IF a refuel operation is invoked WHILE the Agent position differs from every Refuel_Station position, THEN THE Agent SHALL leave the fuel value unchanged and report failure.
3. WHEN a fuel reserve estimate is requested for an Agent, THE Agent SHALL return the Terrain_Cost of the Path to the nearest Refuel_Station multiplied by the fuel consumption value.
4. IF the World_Model contains zero Refuel_Station positions, THEN THE Agent SHALL report the fuel reserve estimate as unavailable and SHALL suppress every GO_TO_REFUEL transition.

### Requirement 6: Load Discharge

**User Story:** As a simulation developer, I want agents to discharge load at dump sites, so that harvested volume is accounted for and capacity is released.

#### Acceptance Criteria

1. WHEN a dump load operation is invoked WHILE the Agent position equals a Dump_Site position, THE Agent SHALL increase the recorded discharged total by the load value, set the load value to zero, and report success.
2. IF a dump load operation is invoked WHILE the Agent position differs from every Dump_Site position, THEN THE Agent SHALL leave the load value unchanged and report failure.
3. THE World SHALL expose the discharged total as a read-only value.
4. IF the World_Model contains zero Dump_Site positions, THEN THE Agent SHALL suppress every GO_TO_DUMP transition.

### Requirement 7: Harvesting

**User Story:** As a farm operator, I want harvesters to convert crop cells into load, so that the field is progressively harvested.

#### Acceptance Criteria

1. WHEN a harvest operation is invoked WHILE the Cell at the Harvester position has the Cell_State CROP AND the load value is less than the maximum load value, THE Harvester SHALL set that Cell_State to HARVESTED, increase the load value by one, and report success.
2. IF a harvest operation is invoked WHILE the Cell at the Harvester position has a Cell_State other than CROP, THEN THE Harvester SHALL leave the load value unchanged and report failure.
3. IF a harvest operation is invoked WHILE the load value equals the maximum load value, THEN THE Harvester SHALL leave the Cell_State and the load value unchanged and report failure.
4. WHERE area ownership is assigned, THE Harvester SHALL request a Path only to Cells whose owner identifier equals the Harvester identifier.
5. WHEN every Cell owned by a Harvester holds a Cell_State other than CROP, THE Harvester SHALL report the assigned area as finished.

### Requirement 8: Harvester State Machine

**User Story:** As a farm operator, I want harvesters to follow the specified harvester state machine, so that harvesting, refuelling, dumping, and tractor rendezvous follow predictable rules.

#### Acceptance Criteria

1. THE Harvester SHALL support exactly the states IDLE, HARVEST, GO_TO_REFUEL, GO_TO_DUMP, GO_TO_MEETING_POINT, WAIT_TRACTOR, and INACTIVE.
2. WHEN a Harvester in the HARVEST state reports the assigned area as finished, THE Harvester SHALL transition to the IDLE state.
3. WHEN a Harvester in the IDLE state receives an area assignment containing at least one Cell with the Cell_State CROP, THE Harvester SHALL transition to the HARVEST state.
4. WHEN a Harvester in the HARVEST state holds a fuel value less than or equal to the fuel reserve estimate multiplied by the Harvester Fuel_Reserve_Multiplier, THE Harvester SHALL transition to the GO_TO_REFUEL state.
5. WHEN a Harvester in the GO_TO_REFUEL state completes a refuel operation, THE Harvester SHALL transition to the HARVEST state.
6. WHEN a Harvester in the HARVEST state receives a Tractor assignment together with a negotiated Meeting_Point that differs from the Harvester position, THE Harvester SHALL transition to the GO_TO_MEETING_POINT state.
7. WHEN a Harvester in the GO_TO_MEETING_POINT state reaches the negotiated Meeting_Point, THE Harvester SHALL transition to the WAIT_TRACTOR state.
8. WHEN a Harvester in the HARVEST state holds a load value equal to the maximum load value, THE Harvester SHALL transition to the WAIT_TRACTOR state at the current Harvester position.
9. WHEN a Harvester in the WAIT_TRACTOR state transfers the load value to an assigned Tractor, THE Harvester SHALL transition to the HARVEST state.
10. WHEN a Harvester in the HARVEST state holds a load value greater than zero AND the Terrain_Cost of the Path to the nearest Dump_Site is less than the Terrain_Cost of the Path to the nearest available Tractor multiplied by the Dump_Preference_Factor, THE Harvester SHALL transition to the GO_TO_DUMP state.
11. WHEN a Harvester in the GO_TO_DUMP state completes a dump load operation, THE Harvester SHALL transition to the HARVEST state.
12. WHILE a Harvester holds a fuel value equal to zero, THE Harvester SHALL transition to the INACTIVE state from every other state.
13. WHERE two or more transition conditions of a single Harvester state hold within one Tick, THE Harvester SHALL apply the transition with the lowest index in the configured transition priority order.

### Requirement 9: Tractor State Machine

**User Story:** As a farm operator, I want tractors to follow the specified tractor state machine, so that load collection and transport follow predictable rules.

#### Acceptance Criteria

1. THE Tractor SHALL support exactly the states IDLE, GO_TO_REFUEL, GO_TO_MEETING_POINT, WAIT_HARVESTER, GO_TO_DUMP, and INACTIVE.
2. WHEN a Tractor in the IDLE state holds a fuel value less than or equal to the fuel reserve estimate multiplied by the Tractor Fuel_Reserve_Multiplier, THE Tractor SHALL transition to the GO_TO_REFUEL state.
3. WHEN a Tractor in the GO_TO_REFUEL state completes a refuel operation, THE Tractor SHALL transition to the IDLE state.
4. WHEN a Tractor in the IDLE state receives a Harvester assignment, THE Tractor SHALL transition to the GO_TO_MEETING_POINT state.
5. WHEN a Tractor in the GO_TO_MEETING_POINT state reaches the negotiated Meeting_Point, THE Tractor SHALL transition to the WAIT_HARVESTER state.
6. WHEN a Tractor in the WAIT_HARVESTER state receives load resulting in a load value less than the maximum load value multiplied by the Capacity_Factor, THE Tractor SHALL transition to the IDLE state.
7. WHEN a Tractor in the WAIT_HARVESTER state receives load resulting in a load value greater than or equal to the maximum load value multiplied by the Capacity_Factor, THE Tractor SHALL transition to the GO_TO_DUMP state.
8. WHEN a Tractor in the GO_TO_DUMP state completes a dump load operation, THE Tractor SHALL transition to the IDLE state.
9. WHILE a Tractor holds a fuel value equal to zero, THE Tractor SHALL transition to the INACTIVE state from every other state.
10. WHEN a receive load operation is invoked on a Tractor with an offered load amount, THE Tractor SHALL increase the load value by the smaller of the offered amount and the remaining free capacity, and SHALL return the accepted amount.
11. WHERE two or more transition conditions of a single Tractor state hold within one Tick, THE Tractor SHALL apply the transition with the lowest index in the configured transition priority order.

### Requirement 10: Assistance Requests and Tractor Assignment

**User Story:** As a farm operator, I want harvesters to request tractor assistance through a central coordinator, so that each tractor serves one harvester and load transfer is unambiguous.

#### Acceptance Criteria

1. WHEN a Harvester requests assistance, THE Agent_Manager SHALL select the Tractor with the lowest Path Terrain_Cost to the Harvester position among Tractors in the IDLE state that hold no entry in the Assistance_Mapping.
2. WHEN the Agent_Manager selects a Tractor for a Harvester, THE Agent_Manager SHALL record exactly one Assistance_Mapping entry pairing that Tractor with that Harvester.
3. THE Agent_Manager SHALL hold at most one Assistance_Mapping entry for any single Tractor and at most one Assistance_Mapping entry for any single Harvester.
4. IF a Harvester requests assistance WHILE zero Tractors satisfy the selection conditions, THEN THE Agent_Manager SHALL report assistance as unavailable and SHALL leave the Assistance_Mapping unchanged.
5. WHERE two or more candidate Tractors share the lowest Path Terrain_Cost, THE Agent_Manager SHALL select the candidate Tractor with the lowest identifier in ordinal order.
6. WHEN a load transfer between a paired Harvester and Tractor completes, THE Agent_Manager SHALL remove the Assistance_Mapping entry for that pair.
7. WHEN an Agent that holds an Assistance_Mapping entry transitions to the INACTIVE state, THE Agent_Manager SHALL remove the Assistance_Mapping entry for that Agent and SHALL transition the remaining paired Agent to the IDLE state.
8. WHEN a paired Harvester and Tractor occupy the same negotiated Meeting_Point WHILE the Harvester is in the WAIT_TRACTOR state AND the Tractor is in the WAIT_HARVESTER state, THE Agent_Manager SHALL transfer the accepted amount returned by the Tractor receive load operation and SHALL decrease the Harvester load value by that accepted amount.

### Requirement 11: Meeting Point Negotiation

**User Story:** As a farm operator, I want a negotiated meeting point between a harvester and its tractor, so that the rendezvous minimises combined travel cost and is reproducible.

#### Acceptance Criteria

1. WHEN meeting point negotiation is invoked for a paired Harvester and Tractor, THE Agent_Manager SHALL return the position that minimises the sum of the Harvester Path Terrain_Cost and the Tractor Path Terrain_Cost to that position among positions whose Cell_State differs from BLOCKED.
2. WHERE two or more candidate positions share the minimum combined Terrain_Cost, THE Agent_Manager SHALL return the candidate position with the lowest `y` value, and among those the candidate position with the lowest `x` value.
3. IF zero candidate positions are reachable by both the Harvester and the Tractor, THEN THE Agent_Manager SHALL report negotiation as failed and SHALL remove the Assistance_Mapping entry for that pair.
4. WHEN meeting point negotiation is invoked twice with identical World_Model contents and identical Agent positions, THE Agent_Manager SHALL return identical positions for both invocations.
5. WHILE a Harvester holds a load value equal to the maximum load value, THE Agent_Manager SHALL return the current Harvester position as the negotiated Meeting_Point.

### Requirement 12: Area Distribution and Redistribution

**User Story:** As a farm operator, I want the field partitioned among active harvesters, so that harvesters cover disjoint territories without duplicated effort.

#### Acceptance Criteria

1. WHEN area distribution is invoked, THE Area_Distributor SHALL perform a breadth-first search seeded simultaneously from the position of every Harvester that holds a State_Id other than INACTIVE, expanding through the offsets of Move_Order in Move_Order sequence.
2. WHEN the Area_Distributor visits an unassigned Cell whose Cell_State differs from BLOCKED, THE Area_Distributor SHALL assign the owner identifier of the expanding Harvester to that Cell.
3. THE Area_Distributor SHALL assign at most one owner identifier to any single Cell during one distribution.
4. THE Area_Distributor SHALL leave the owner identifier unassigned for every Cell whose Cell_State is BLOCKED and for every Cell that no seed reaches.
5. WHEN area distribution is invoked, THE Area_Distributor SHALL clear every owner identifier assigned by a previous distribution before assigning new owner identifiers.
6. WHEN a Harvester reports the assigned area as finished, THE World SHALL invoke area redistribution at the end of the current Tick.
7. WHEN a Harvester transitions to the INACTIVE state, THE World SHALL invoke area redistribution at the end of the current Tick.
8. WHEN area distribution is invoked twice with an identical grid and an identical ordered Harvester collection, THE Area_Distributor SHALL produce identical owner identifier assignments for every Cell.
9. IF zero Harvesters hold a State_Id other than INACTIVE, THEN THE Area_Distributor SHALL leave every owner identifier unassigned.

### Requirement 13: Path To Best Cell

**User Story:** As a simulation developer, I want a cost-aware search to the nearest crop cell, so that harvesters move toward productive terrain rather than straight lines.

#### Acceptance Criteria

1. WHEN a path to best cell is requested for an Agent and a target Cell_State, THE Path_Finder SHALL perform a uniform-cost search from the Agent position using the offsets of Move_Order and SHALL terminate at the first expanded Cell holding the target Cell_State.
2. THE Path_Finder SHALL apply the Terrain_Cost of the entered Cell as the step cost, using the CROP, EMPTY, and HARVESTED costs supplied by the Simulation_Config.
3. THE Path_Finder SHALL exclude every Cell whose Cell_State is BLOCKED from expansion.
4. WHEN the Path_Finder terminates at a target Cell, THE Path_Finder SHALL return a Path whose first position equals the Agent position and whose last position equals that target Cell position.
5. IF zero Cells holding the target Cell_State are reachable from the Agent position, THEN THE Path_Finder SHALL return an empty Path.
6. WHERE an area ownership filter is supplied, THE Path_Finder SHALL terminate only at Cells whose owner identifier equals the supplied identifier.
7. WHERE two or more frontier entries share the lowest accumulated cost, THE Path_Finder SHALL expand the entry with the lowest insertion sequence number.
8. WHEN a path to best cell is requested twice with an identical grid and an identical Agent position, THE Path_Finder SHALL return identical Paths for both invocations.

### Requirement 14: Path To Specific Cell

**User Story:** As a simulation developer, I want a search to a specific target cell, so that agents travel to refuel stations, dump sites, and meeting points.

#### Acceptance Criteria

1. WHEN a path to cell is requested for an Agent and a target position, THE Path_Finder SHALL perform a best-first search from the Agent position using the offsets of Move_Order, the Terrain_Cost step cost, and the heuristic supplied by the Simulation_Config.
2. WHEN the Path_Finder expands the target position, THE Path_Finder SHALL return a Path whose first position equals the Agent position and whose last position equals the target position.
3. THE Path_Finder SHALL exclude every Cell whose Cell_State is BLOCKED from expansion.
4. IF the target position is out of bounds, holds the Cell_State BLOCKED, or is unreachable from the Agent position, THEN THE Path_Finder SHALL return an empty Path.
5. WHEN the target position equals the Agent position, THE Path_Finder SHALL return a Path containing exactly the Agent position.
6. THE Path_Finder SHALL return a Path in which every consecutive pair of positions differs by exactly one offset contained in Move_Order.
7. WHERE the Simulation_Config selects the zero heuristic, THE Path_Finder SHALL return a Path whose accumulated Terrain_Cost is the minimum among all Paths from the Agent position to the target position.
8. WHEN a path to cell is requested twice with an identical grid, an identical Agent position, and an identical target position, THE Path_Finder SHALL return identical Paths for both invocations.

### Requirement 15: Fuel Exhaustion and Inactive Agents

**User Story:** As a farm operator, I want agents that run out of fuel to stop cleanly, so that the remaining fleet continues operating on a consistent world state.

#### Acceptance Criteria

1. WHEN an Agent fuel value reaches zero, THE Agent SHALL transition to the INACTIVE state and SHALL clear the current Path.
2. WHILE an Agent holds the INACTIVE state, THE Agent SHALL retain the Agent position recorded at the moment of the transition for every subsequent Tick.
3. WHILE an Agent holds the INACTIVE state, THE Agent SHALL retain the load value recorded at the moment of the transition for every subsequent Tick.
4. WHILE an Agent holds the INACTIVE state, THE Agent_Manager SHALL exclude that Agent from Tractor selection, from meeting point negotiation, and from area distribution seeding.
5. WHILE an Agent holds the INACTIVE state, THE Agent SHALL treat the Cell at the Agent position as passable for every other Agent.
6. WHEN every registered Agent holds the INACTIVE state, THE World SHALL report the simulation as halted.

### Requirement 16: Simulation Orchestration

**User Story:** As a simulation developer, I want a single tick entry point with defined ordering, so that the core integrates into any host loop without hidden scheduling.

#### Acceptance Criteria

1. WHEN a Tick is executed on the World, THE Agent_Manager SHALL invoke the Agent execution of every registered Agent exactly once, in registration order.
2. WHEN a Tick is executed on the World, THE World SHALL resolve pending load transfers after every Agent execution completes and SHALL apply pending area redistribution after load transfer resolution.
3. WHEN a Tractor is registered, THE Agent_Manager SHALL append the Tractor to the Tractor collection and SHALL assign the Tractor the IDLE state.
4. WHEN a Harvester is registered, THE Agent_Manager SHALL append the Harvester to the Harvester collection and SHALL assign the Harvester the IDLE state.
5. IF an Agent is registered with an identifier equal to the identifier of an already registered Agent, THEN THE Agent_Manager SHALL reject the registration with a duplicate identifier error.
6. THE World SHALL expose the current Tick index, the registered Agents, the discharged total, and the cell matrix as read-only observations.
7. WHEN two World instances are constructed from an identical Simulation_Config, an identical seed, and an identical registration sequence, THE World SHALL produce identical observations after an identical number of Ticks.

### Requirement 17: Configuration

**User Story:** As a simulation developer, I want every tunable constant supplied through injectable configuration, so that behavior is adjustable without editing core logic.

#### Acceptance Criteria

1. THE Simulation_Config SHALL expose the Dump_Preference_Factor, the Capacity_Factor, the Harvester Fuel_Reserve_Multiplier, the Tractor Fuel_Reserve_Multiplier, the CROP Terrain_Cost, the EMPTY Terrain_Cost, the HARVESTED Terrain_Cost, the heuristic selection, the default maximum load, the default maximum fuel, the default fuel consumption, and the Random_Source seed.
2. THE Simulation_Config SHALL provide a default instance whose values match the defaults recorded in the Assumptions section.
3. IF a Simulation_Config is constructed with a Capacity_Factor outside the inclusive range `[0, 1]`, a negative Dump_Preference_Factor, a negative Fuel_Reserve_Multiplier, or a Terrain_Cost less than one, THEN THE Simulation_Config SHALL reject the construction with an argument error that names the invalid value.
4. THE Simulation_Config SHALL remain immutable after construction.
5. THE Harvesting_Core SHALL read every tunable constant from the injected Simulation_Config.

### Requirement 18: Determinism and Engine Independence

**User Story:** As a Unity developer, I want the core to be deterministic and free of engine types, so that later integration requires no changes to the logic layer.

#### Acceptance Criteria

1. THE Harvesting_Core SHALL reference only assemblies included in the target .NET class library framework.
2. THE Harvesting_Core SHALL express time exclusively as integer Tick indices.
3. WHERE pseudo-random values are required, THE Harvesting_Core SHALL obtain those values from the injected Random_Source seeded by the Simulation_Config.
4. THE Harvesting_Core SHALL order every iteration over agents, cells, and frontier entries by an explicit deterministic key.
5. WHEN identical inputs are supplied to a sequence of Harvesting_Core operations, THE Harvesting_Core SHALL produce identical outputs and identical observable state.
6. THE Harvesting_Core SHALL expose agent decisions, state transitions, and path assignments through public read-only members so that a host application observes behavior without modifying the Harvesting_Core.

### Requirement 19: Runnable Test Project

**User Story:** As a developer, I want a plain test project I can run locally, so that I verify core behavior and correctness properties without installing packages.

#### Acceptance Criteria

1. THE Harvesting_Core solution SHALL include a test project that references only assemblies included in the target .NET framework and the Harvesting_Core library.
2. WHEN the test project is executed, THE test project SHALL run every registered test case and SHALL report the count of passed cases and the count of failed cases.
3. IF at least one test case fails, THEN THE test project SHALL terminate with a non-zero exit code and SHALL print the name of every failed case together with the observed values.
4. THE test project SHALL include a property-based test runner that generates inputs from a seeded Random_Source and prints the failing input for every failed property.
5. WHEN a property-based test case fails, THE test project SHALL print the seed that reproduces the failing input.

## Correctness Properties for Property-Based Testing

These properties are derived from the acceptance criteria above and are candidates for the property-based test runner defined in Requirement 19.

1. **Load bound invariant**: For all agents at all ticks, `0 <= load <= maxLoad`. (Requirements 3.7, 9.10)
2. **Fuel monotonicity**: For all agents, fuel is non-increasing across ticks except on a tick containing a successful refuel operation, and `0 <= fuel <= maxFuel`. (Requirements 3.8, 5.1)
3. **Harvest conservation**: For all reachable states, `crop cells harvested = discharged total + sum of current agent loads`. (Requirements 6.1, 7.1)
4. **Single state invariant**: For all agents at the end of every tick, exactly one State_Id is current. (Requirement 3.4)
5. **Inactive immobility**: For all agents in INACTIVE, position and load are identical to the values recorded at the transition tick. (Requirements 15.2, 15.3)
6. **Partition disjointness**: After area distribution, every Cell carries at most one owner identifier. (Requirement 12.3)
7. **Partition reachability**: After area distribution, every owned Cell is reachable from the position of its owning Harvester through non-BLOCKED cells using Move_Order offsets. (Requirements 12.1, 12.2)
8. **Partition determinism**: Two distributions over identical inputs produce identical assignments. (Requirement 12.8)
9. **Path well-formedness**: Every non-empty returned Path starts at the agent position, ends at the requested target, contains only in-bounds non-BLOCKED cells, and every consecutive pair differs by exactly one Move_Order offset. (Requirements 13.4, 14.2, 14.6)
10. **Path emptiness on unreachability**: If the target is unreachable, the returned Path is empty; if the returned Path is empty, no path of finite Terrain_Cost exists (verified against a reference flood fill). (Requirements 13.5, 14.4)
11. **Path optimality under zero heuristic**: With the zero heuristic selected, path to cell returns a Path whose accumulated Terrain_Cost equals the cost computed by a reference Dijkstra implementation (model-based test). (Requirement 14.7)
12. **Path idempotence**: Requesting a path twice without mutating the grid returns identical Paths. (Requirements 13.8, 14.8)
13. **Assignment exclusivity**: At every tick, each Tractor appears in at most one Assistance_Mapping entry and each Harvester appears in at most one Assistance_Mapping entry. (Requirement 10.3)
14. **Transfer conservation**: For every load transfer, the amount added to the Tractor load equals the amount removed from the Harvester load. (Requirement 10.8)
15. **Meeting point determinism and symmetry**: Negotiation over identical inputs returns identical positions, and swapping the argument order of the two agents returns the same position. (Requirement 11.4)
16. **Simulation determinism**: Two worlds built from an identical config, seed, and registration sequence hold identical observable state after N ticks, for all N. (Requirement 16.7)
17. **Grid generation round trip**: Generating a grid from a seed, serialising the cell matrix, parsing that serialisation, and comparing yields an equal cell matrix. (Requirements 1.8, 19.4)
18. **Cell state machine soundness**: Repeated harvest on a single Cell succeeds at most once until a plant succeeds, and plant then harvest returns the Cell to HARVESTED (idempotence and round trip). (Requirements 2.1 through 2.4)
19. **Error conditions**: Out-of-bounds positions, invalid config values, duplicate agent identifiers, and empty station collections produce the specified failures without mutating world state. (Requirements 1.2, 1.4, 5.4, 6.4, 16.5, 17.3)

## Open Decisions and Risks

1. **A\* heuristic admissibility (carried over from the reference implementation)**: `reference/algorithms/path_to_cell.cpp` uses `h = dx*dx + dy*dy` with step costs of 1, 2, or 10. That heuristic is not admissible for these step costs, so the search is not guaranteed to return a minimum-cost Path, and the reference path reconstruction (walking down the cost field) can fail when the cost field is inconsistent. Requirement 14 therefore makes the heuristic a Simulation_Config selection and states the optimality guarantee only for the zero heuristic. Decision needed: keep the squared-Euclidean heuristic as the default for speed and accept suboptimal paths, switch the default to an admissible octile heuristic scaled by the minimum Terrain_Cost, or default to the zero heuristic and accept full Dijkstra cost.
2. **Cost ceiling**: The reference implementations initialise the cost field to `1e3`, which silently blocks paths whose true cost exceeds that ceiling on larger grids. This specification assumes an unbounded sentinel instead. Confirm this deviation.
3. **Path reconstruction strategy**: The reference implementations reconstruct paths by walking the cost field. A predecessor map is deterministic and independent of heuristic consistency. This specification assumes a predecessor map. Confirm this deviation.
4. **Crop regrowth**: The Cell plant operation exists in the reference class diagram, but no diagram defines a regrowth trigger. This specification exposes plant without an automatic trigger. Confirm whether regrowth belongs in scope.
5. **Concurrent occupancy**: No diagram states whether two agents may occupy the same Cell. This specification places no occupancy restriction. Confirm whether collision avoidance belongs in scope.
6. **Harvester movement while waiting**: Requirement 8.8 keeps a full Harvester stationary at the current position, matching the "load = capacity" transition in the harvester diagram. Confirm that a full Harvester never walks to a negotiated Meeting_Point.
