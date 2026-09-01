# Requirements Document

## Introduction

This feature adds a WebSocket transport layer to the HarvestingCore solution. The transport layer acts as a bridge between external clients (e.g. a dashboard or monitoring tool) and the `SimulationWorld` façade, without introducing any dependency on the core simulation logic inside the core library. Clients connect over WebSocket to request simulation ticks and read simulation state as structured JSON messages.

The transport layer is a separate project (`HarvestingCore.Transport`) targeting `net8.0` (or later). It references `HarvestingCore` as a project dependency and depends on `System.Net.WebSockets` from the BCL — no third-party libraries. It exposes an `ISimulationHost` abstraction so the transport never instantiates or mutates `SimulationWorld` directly.

## Glossary

- **Transport_Server**: The WebSocket server component that accepts connections and routes messages.
- **Client**: An external process that opens a WebSocket connection to the Transport_Server.
- **ISimulationHost**: The interface the Transport_Server uses to tick the simulation and read state; implemented by the host (not by HarvestingCore).
- **SimulationSnapshot**: The serializable data-transfer object (DTO) that represents simulation state at a given tick.
- **AgentSnapshot**: A DTO containing the observable properties of a single agent at a given tick.
- **CellSnapshot**: A DTO containing the observable properties of a single grid cell at a given tick.
- **TickRequest**: A JSON message sent by a Client requesting one or more simulation ticks.
- **TickResponse**: A JSON message sent by the Transport_Server after each tick, containing the current SimulationSnapshot.
- **StateRequest**: A JSON message sent by a Client requesting the current SimulationSnapshot without advancing the simulation.
- **StateResponse**: A JSON message sent by the Transport_Server in reply to a StateRequest, containing the current SimulationSnapshot.
- **ErrorResponse**: A JSON message sent by the Transport_Server when a request cannot be fulfilled.
- **Message_Type**: A string field present on every JSON message that identifies the message kind (e.g. `"tick_request"`, `"state_request"`, `"tick_response"`, `"state_response"`, `"error_response"`).

---

## Requirements

### Requirement 1: WebSocket Server Lifecycle

**User Story:** As a developer integrating an external dashboard, I want the Transport_Server to start and stop cleanly, so that I can control the server from a host process without resource leaks.

#### Acceptance Criteria

1. THE Transport_Server SHALL accept a TCP port number and a bound `ISimulationHost` instance as constructor parameters.
2. WHEN `StartAsync` is called, THE Transport_Server SHALL begin accepting WebSocket connections on the configured port.
3. WHEN `StopAsync` is called, THE Transport_Server SHALL stop accepting new connections and close all active connections with WebSocket close code 1001 (Going Away).
4. IF `StartAsync` is called while the Transport_Server is already running, THEN THE Transport_Server SHALL throw an `InvalidOperationException`.
5. IF the configured port is unavailable, THEN THE Transport_Server SHALL propagate the underlying socket exception to the caller of `StartAsync`.
6. THE Transport_Server SHALL implement `IAsyncDisposable` and SHALL call `StopAsync` from `DisposeAsync` when not already stopped.

---

### Requirement 2: Client Connection Management

**User Story:** As a developer, I want the server to handle multiple simultaneous client connections, so that more than one consumer can observe the simulation at the same time.

#### Acceptance Criteria

1. WHEN a Client establishes a WebSocket connection, THE Transport_Server SHALL register the connection and begin reading messages from it.
2. WHEN a Client closes its connection, THE Transport_Server SHALL remove the connection from the active set and release associated resources.
3. IF a Client disconnects unexpectedly, THEN THE Transport_Server SHALL remove the connection from the active set without affecting other active connections.
4. THE Transport_Server SHALL support at least 10 simultaneous Client connections.
5. WHILE the Transport_Server is stopped, THE Transport_Server SHALL reject new connection attempts with HTTP 503.

---

### Requirement 3: Tick Request Handling

**User Story:** As a dashboard client, I want to request that the simulation advance by a given number of ticks, so that I can drive the simulation step-by-step or in batches.

#### Acceptance Criteria

1. WHEN a Client sends a TickRequest with a `count` field of 1 or more, THE Transport_Server SHALL call `ISimulationHost.TickAsync` exactly `count` times in sequence.
2. WHEN each tick completes, THE Transport_Server SHALL send a TickResponse to the requesting Client containing the SimulationSnapshot for that tick.
3. IF the `count` field is absent or less than 1, THEN THE Transport_Server SHALL send an ErrorResponse with code `"invalid_count"` and SHALL NOT advance the simulation.
4. IF `ISimulationHost.IsHalted` is `true` when a TickRequest arrives, THEN THE Transport_Server SHALL send an ErrorResponse with code `"simulation_halted"` and SHALL NOT call `TickAsync`.
5. IF `ISimulationHost.TickAsync` throws an exception, THEN THE Transport_Server SHALL send an ErrorResponse with code `"tick_error"` and SHALL NOT send a TickResponse for that tick.
6. THE Transport_Server SHALL process TickRequests from the same Client in the order they are received.

---

### Requirement 4: State Request Handling

**User Story:** As a dashboard client, I want to read the current simulation state at any time without advancing it, so that I can refresh my view independently of ticking.

#### Acceptance Criteria

1. WHEN a Client sends a StateRequest, THE Transport_Server SHALL call `ISimulationHost.GetSnapshot()` and send a StateResponse containing the resulting SimulationSnapshot.
2. THE Transport_Server SHALL respond to a StateRequest within 500 ms under normal load (no concurrent tick loop running).
3. IF `ISimulationHost.GetSnapshot()` throws an exception, THEN THE Transport_Server SHALL send an ErrorResponse with code `"snapshot_error"`.

---

### Requirement 5: Message Protocol

**User Story:** As a developer building a client, I want all messages to follow a documented JSON schema, so that I can parse responses reliably.

#### Acceptance Criteria

1. THE Transport_Server SHALL encode every outbound message as a UTF-8 JSON text WebSocket frame.
2. EVERY inbound message SHALL contain a `type` field; IF the `type` field is absent or unrecognised, THEN THE Transport_Server SHALL send an ErrorResponse with code `"unknown_type"` and SHALL NOT modify simulation state.
3. THE TickRequest message SHALL conform to the schema: `{ "type": "tick_request", "count": <integer> }`.
4. THE TickResponse message SHALL conform to the schema: `{ "type": "tick_response", "tick": <integer>, "snapshot": <SimulationSnapshot> }`.
5. THE StateRequest message SHALL conform to the schema: `{ "type": "state_request" }`.
6. THE StateResponse message SHALL conform to the schema: `{ "type": "state_response", "tick": <integer>, "snapshot": <SimulationSnapshot> }`.
7. THE ErrorResponse message SHALL conform to the schema: `{ "type": "error_response", "code": <string>, "message": <string> }`.
8. THE SimulationSnapshot SHALL contain: `tick` (integer), `isHalted` (boolean), `dischargedTotal` (integer), `agents` (array of AgentSnapshot), `cells` (array of CellSnapshot).
9. THE AgentSnapshot SHALL contain: `id` (string), `role` (string), `state` (string), `x` (integer), `y` (integer), `fuel` (integer), `load` (integer).
10. THE CellSnapshot SHALL contain: `x` (integer), `y` (integer), `state` (string), `ownerId` (string).

---

### Requirement 6: ISimulationHost Abstraction

**User Story:** As a developer, I want the transport to depend on an interface rather than on `SimulationWorld` directly, so that the core simulation library remains decoupled from any networking concerns.

#### Acceptance Criteria

1. THE Transport_Server SHALL reference simulation state and mutation operations exclusively through `ISimulationHost`; THE Transport_Server SHALL NOT reference `SimulationWorld`, `AgentManager`, `WorldModel`, or any other type from the `HarvestingCore` assembly directly.
2. THE `ISimulationHost` interface SHALL declare: `Task TickAsync(CancellationToken ct)`, `SimulationSnapshot GetSnapshot()`, and `bool IsHalted`.
3. THE `HarvestingCore.Transport` project SHALL reference `HarvestingCore` only for the DTO types (`SimulationSnapshot`, `AgentSnapshot`, `CellSnapshot`) and for `ISimulationHost`; all other simulation types SHALL remain internal to the core library.
4. WHERE a host wraps `SimulationWorld`, THE host implementation SHALL map `SimulationWorld.Tick()` to `ISimulationHost.TickAsync` and SHALL project `SimulationWorld` state into a `SimulationSnapshot`.

---

### Requirement 7: JSON Serialization Round-Trip

**User Story:** As a developer, I want the snapshot DTOs to survive a JSON round-trip without data loss, so that client deserialisation is always consistent with what the server sent.

#### Acceptance Criteria

1. THE Serializer SHALL serialize a `SimulationSnapshot` to a UTF-8 JSON string.
2. THE Deserializer SHALL deserialize a UTF-8 JSON string back into a `SimulationSnapshot`.
3. FOR ALL valid `SimulationSnapshot` objects, serializing then deserializing SHALL produce an object that is structurally equal to the original (round-trip property).
4. IF the JSON string is malformed, THEN THE Deserializer SHALL return a descriptive error and SHALL NOT throw an unhandled exception.

---

### Requirement 8: Concurrency and Thread Safety

**User Story:** As a developer, I want simultaneous read and tick requests from multiple clients to be handled safely, so that simulation state is never corrupted.

#### Acceptance Criteria

1. THE Transport_Server SHALL serialise all calls to `ISimulationHost.TickAsync` so that at most one tick executes at a time, regardless of the number of connected Clients.
2. WHILE a tick is in progress, THE Transport_Server SHALL queue incoming TickRequests and process them after the current tick completes.
3. THE Transport_Server SHALL allow `GetSnapshot()` calls to proceed concurrently with other read operations, provided no tick is executing.
4. IF two TickRequests arrive simultaneously from different Clients, THEN THE Transport_Server SHALL process them sequentially in arrival order.
