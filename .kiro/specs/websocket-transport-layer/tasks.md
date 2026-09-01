# Implementation Plan: WebSocket Transport Layer

## Overview

Implement `HarvestingCore.Transport` as a new `net8.0` project alongside the existing `HarvestingCore` library. Tasks build incrementally: DTOs and serialisation first, then the simulation host abstraction, then the message dispatch layer, and finally the WebSocket server lifecycle and connection management.

## Tasks

- [x] 1. Create the `HarvestingCore.Transport` project and DTO types
  - Create `src/HarvestingCore.Transport/HarvestingCore.Transport.csproj` targeting `net8.0` with a `<ProjectReference>` to `HarvestingCore`; no third-party `PackageReference` items
  - Add the project to `HarvestingCore.sln`
  - Create `Dto/SimulationSnapshot.cs`, `Dto/AgentSnapshot.cs`, `Dto/CellSnapshot.cs` with `[JsonPropertyName]` attributes matching the schema in Req 5.8–5.10
  - _Requirements: 5.8, 5.9, 5.10, 6.3_

- [x] 2. Implement `SnapshotSerializer` and JSON round-trip
  - [x] 2.1 Create `SnapshotSerializer.cs` with a cached `JsonSerializerOptions` (camelCase policy), `Serialize(object message) → byte[]`, and `Deserialize(string json) → SimulationSnapshot`; handle malformed input without throwing unhandled exceptions
    - _Requirements: 5.1, 7.1, 7.2, 7.4_

  - [x] 2.2 Write property test for JSON round-trip (Property 1: round-trip consistency)
    - **Property 1: For all structurally valid `SimulationSnapshot` instances, `Deserialize(Serialize(snapshot))` produces an object structurally equal to the original**
    - **Validates: Requirements 7.3**

  - [x] 2.3 Write unit tests for `SnapshotSerializer`
    - Test UTF-8 encoding, camelCase field names, malformed JSON returning a descriptive error
    - _Requirements: 5.1, 7.4_

- [ ] 3. Define `ISimulationHost` and inbound/outbound message types
  - Create `ISimulationHost.cs` declaring `bool IsHalted`, `Task TickAsync(CancellationToken ct)`, `SimulationSnapshot GetSnapshot()`
  - Create internal inbound types `TickRequest` and `StateRequest` and outbound types `TickResponse`, `StateResponse`, `ErrorResponse` with correct `type` string values and `[JsonPropertyName]` attributes matching Req 5.3–5.7
  - _Requirements: 5.3, 5.4, 5.5, 5.6, 5.7, 6.2_

- [ ] 4. Implement `MessageDispatcher`
  - [ ] 4.1 Create `MessageDispatcher.cs`; deserialise the `type` field via `JsonDocument`; route to `HandleTickRequestAsync`, `HandleStateRequestAsync`, or return an `unknown_type` ErrorResponse (Req 5.2)
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 3.6, 4.1, 4.3, 5.2_

  - [ ] 4.2 Implement `HandleTickRequestAsync`: validate `count ≥ 1` (return `invalid_count` on failure), check `IsHalted` (return `simulation_halted`), acquire `SemaphoreSlim(1,1)` tick lock, call `TickAsync` in a loop `count` times, send a `TickResponse` after each tick; catch exceptions and return `tick_error`
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5, 8.1, 8.2, 8.4_

  - [ ] 4.3 Implement `HandleStateRequestAsync`: call `GetSnapshot()` without acquiring the tick lock; catch exceptions and return `snapshot_error`
    - _Requirements: 4.1, 4.3, 8.3_

  - [ ]* 4.4 Write property test for tick-lock serialisation (Property 2: at-most-one-tick-at-a-time)
    - **Property 2: When N concurrent `HandleTickRequestAsync` calls (count=1) are issued, `ISimulationHost.TickAsync` is never called more than once simultaneously**
    - **Validates: Requirements 8.1, 8.4**

  - [ ]* 4.5 Write unit tests for `MessageDispatcher`
    - Test `unknown_type`, `invalid_count`, `simulation_halted`, `tick_error`, `snapshot_error` error paths; test happy-path tick and state responses
    - _Requirements: 3.3, 3.4, 3.5, 4.3, 5.2_

- [ ] 5. Checkpoint — Ensure all tests pass
  - Ensure all tests pass; ask the user if questions arise.

- [ ] 6. Implement `ClientHandler`
  - [ ] 6.1 Create `ClientHandler.cs`; implement `RunAsync(CancellationToken serverCt)`: read frames in a loop (8 KB segments joined into a `MemoryStream`), pass UTF-8 text payload to `MessageDispatcher.HandleAsync`, write the response bytes back through the `WebSocket`
    - _Requirements: 2.1, 2.2, 2.3_

  - [ ] 6.2 Handle `WebSocketState.CloseReceived` and unexpected disconnects: perform the closing handshake, remove itself from the server's active-connection map, and release resources without affecting other active connections
    - _Requirements: 2.2, 2.3_

  - [ ]* 6.3 Write unit tests for `ClientHandler`
    - Test normal close, unexpected disconnect, and message-passthrough to `MessageDispatcher` using a mock/fake `WebSocket`
    - _Requirements: 2.2, 2.3_

- [ ] 7. Implement `TransportServer`
  - [ ] 7.1 Create `TransportServer.cs` implementing `IAsyncDisposable`; accept `int port` and `ISimulationHost host` in the constructor; maintain a `ConcurrentDictionary<Guid, ClientHandler>` of active connections
    - _Requirements: 1.1, 1.6, 2.4_

  - [ ] 7.2 Implement `StartAsync`: validate not-already-running (throw `InvalidOperationException` if so), start `HttpListener` on the configured port (let socket exceptions propagate), launch the accept loop that upgrades HTTP connections to WebSocket, spawns a `ClientHandler` task per connection, and rejects new connections with 503 when stopped
    - _Requirements: 1.2, 1.4, 1.5, 2.1, 2.5_

  - [ ] 7.3 Implement `StopAsync`: cancel the `CancellationTokenSource`, close the `HttpListener`, send WebSocket close code 1001 to each active connection, and await all `ClientHandler` tasks
    - _Requirements: 1.3, 2.2_

  - [ ] 7.4 Implement `DisposeAsync`: call `StopAsync` when not already stopped
    - _Requirements: 1.6_

  - [ ]* 7.5 Write property test for connection-count bound (Property 3: concurrent connections)
    - **Property 3: For any N ∈ [1, 10], a `TransportServer` accepts and independently handles N simultaneous WebSocket connections**
    - **Validates: Requirements 2.4**

  - [ ]* 7.6 Write integration tests for `TransportServer`
    - Test start/stop lifecycle, duplicate `StartAsync` exception, 503 rejection when stopped, and clean shutdown with close-code 1001
    - _Requirements: 1.2, 1.3, 1.4, 1.5, 2.5_

- [ ] 8. Create test project and wire everything together
  - Create `tests/HarvestingCore.Transport.Tests/HarvestingCore.Transport.Tests.csproj` with references to `HarvestingCore.Transport`, `Microsoft.NET.Test.Sdk`, `xunit` (or NUnit), and `FsCheck` (or `CsCheck`) for property-based tests
  - Ensure all test files from tasks 2, 4, 6, and 7 compile and run against the final implementation
  - _Requirements: 1.1–1.6, 2.1–2.5, 3.1–3.6, 4.1–4.3, 5.1–5.10, 6.1–6.4, 7.1–7.4, 8.1–8.4_

- [ ] 9. Final checkpoint — Ensure all tests pass
  - Ensure all tests pass; ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for a faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties; unit tests validate specific examples and edge cases
