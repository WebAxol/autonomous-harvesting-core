using System;
using System.Threading;
using System.Threading.Tasks;
using HarvestingCore;
using HarvestingCore.Agents;
using HarvestingCore.Configuration;
using HarvestingCore.Host;
using HarvestingCore.Transport;
using HarvestingCore.World;

// ── Configuration ────────────────────────────────────────────────────────────

int port = 8765;
if (args.Length > 0 && int.TryParse(args[0], out int parsedPort))
    port = parsedPort;

int seed = 20240101;
if (args.Length > 1 && int.TryParse(args[1], out int parsedSeed))
    seed = parsedSeed;

// ── Build simulation ─────────────────────────────────────────────────────────

var config = new SimulationConfig(seed: seed);
var rng    = new DeterministicRandom(seed);

var refuelStations = new[] { new GridPosition(0, 0), new GridPosition(9, 9) };
var dumpSites      = new[] { new GridPosition(0, 9), new GridPosition(9, 0) };

var model  = new WorldModel(10, 10, refuelStations, dumpSites);
var world  = new SimulationWorld(model, config, rng);

world.GenerateGrid();
world.RedistributeAreas();

// Register 2 harvesters and 2 tractors at the refuel/dump corners
world.Register(new Harvester("H1", new GridPosition(0, 0), model, config));
world.Register(new Harvester("H2", new GridPosition(9, 9), model, config));
world.Register(new Tractor  ("T1", new GridPosition(0, 9), model, config));
world.Register(new Tractor  ("T2", new GridPosition(9, 0), model, config));

// ── Start WebSocket server ───────────────────────────────────────────────────

var host   = new SimulationHostAdapter(world);
var server = new TransportServer(port, host);

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

Console.WriteLine($"HarvestingCore simulation server starting on ws://localhost:{port}/");
Console.WriteLine("Press Ctrl+C to stop.");

await server.StartAsync(cts.Token);

Console.WriteLine("Server running. Waiting for connections...");

try
{
    await Task.Delay(Timeout.Infinite, cts.Token);
}
catch (OperationCanceledException) { }

Console.WriteLine("Shutting down...");
await server.StopAsync();
Console.WriteLine("Done.");
