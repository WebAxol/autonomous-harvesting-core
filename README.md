# HarvestingCore

Multi-agent harvesting simulation with a WebSocket transport layer.

## Projects

| Project | Description |
|---|---|
| `HarvestingCore` | Core simulation library (netstandard2.1, Unity-compatible) |
| `HarvestingCore.Transport` | WebSocket server + message protocol (net8.0) |
| `HarvestingCore.Host` | Console entry point that wires the simulation to the WebSocket server (net8.0) |

## Running the WebSocket server

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Build

```bash
dotnet build HarvestingCore.sln
```

### Start the server

```bash
dotnet run --project src/HarvestingCore.Host
```

The server starts on `ws://localhost:8765/` by default.

#### Optional arguments

```
dotnet run --project src/HarvestingCore.Host -- [port] [seed]
```

| Argument | Default | Description |
|---|---|---|
| `port` | `8765` | TCP port the WebSocket server listens on |
| `seed` | `20240101` | Deterministic RNG seed for grid generation |

Example — port 9000, seed 42:

```bash
dotnet run --project src/HarvestingCore.Host -- 9000 42
```

Press **Ctrl+C** to shut down gracefully.

## WebSocket protocol

Connect to `ws://localhost:<port>/` with any WebSocket client.

### Client → Server

**Request ticks**
```json
{ "type": "tick_request", "count": 1 }
```

**Request current state without ticking**
```json
{ "type": "state_request" }
```

### Server → Client

**After each tick**
```json
{
  "type": "tick_response",
  "tick": 3,
  "snapshot": { ... }
}
```

**Reply to state_request**
```json
{
  "type": "state_response",
  "tick": 3,
  "snapshot": { ... }
}
```

**Error**
```json
{ "type": "error_response", "code": "...", "message": "..." }
```

### Snapshot shape

```json
{
  "tick": 3,
  "isHalted": false,
  "dischargedTotal": 12,
  "agents": [
    { "id": "H1", "role": "Harvester", "state": "Harvest", "x": 2, "y": 3, "fuel": 980, "load": 5 }
  ],
  "cells": [
    { "x": 0, "y": 0, "state": "Empty", "ownerId": "H1" }
  ]
}
```

## Running tests

```bash
dotnet test HarvestingCore.sln
```
