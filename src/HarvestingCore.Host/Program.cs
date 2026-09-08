using System;
using System.Threading;
using System.Threading.Tasks;
using HarvestingCore.Host;
using HarvestingCore.Transport;

// ── Configuration ────────────────────────────────────────────────────────────

int port = 8765;
if (args.Length > 0 && int.TryParse(args[0], out int parsedPort))
    port = parsedPort;

// ── Start WebSocket server ───────────────────────────────────────────────────
//
// The connected client is the sole author of the world: it sends a single
// init_request after the handshake, and the host builds its SimulationWorld from
// that payload. The server holds no world until then.

var host   = new SimulationHostAdapter();
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
