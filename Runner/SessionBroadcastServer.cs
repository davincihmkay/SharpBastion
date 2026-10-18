using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using SharpBastion.Helper;
using SharpBastion.Interface;
using SharpBastion.Manifest;

namespace SharpBastion.Runner;

public class SessionBroadcastServer : BackgroundService
{
    private readonly IOutputBroadcaster _outputBroadcaster;
    private readonly ISessionListenProtocol _sessionListenProtocol;

    public string? SocketPath { get; private set; }

    public SessionBroadcastServer(IOutputBroadcaster outputBroadcaster, ISessionListenProtocol sessionListenProtocol)
    {
        _outputBroadcaster = outputBroadcaster;
        _sessionListenProtocol = sessionListenProtocol;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_sessionListenProtocol.IsOverridden)
            return; // Closed by default — no socket is ever created.

        Directory.CreateDirectory(SessionPaths.SharpBastionHomeDir);
        HardenToOwnerOnly(SessionPaths.SharpBastionHomeDir);

        var pid = Environment.ProcessId;
        SocketPath = SessionPaths.SocketPathFor(pid);
        var manifestPath = SessionPaths.ManifestPathFor(pid);

        // A stale socket/manifest pair from an earlier run, should be deleted
        if (File.Exists(SocketPath))
            File.Delete(SocketPath);
        if (File.Exists(manifestPath))
            File.Delete(manifestPath);

        using var listenSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listenSocket.Bind(new UnixDomainSocketEndPoint(SocketPath));
        HardenToOwnerOnly(SocketPath);
        listenSocket.Listen(backlog: 4);

        WriteManifestStub(pid, manifestPath);

        Console.WriteLine($"\n[SESSION-LISTEN] Read-only attach socket open: {SocketPath}");
        Console.WriteLine($"[SESSION-LISTEN] Observable via 'listen' from another local Bastion session's REPL (pid {pid}).");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var accepted = await listenSocket.AcceptAsync(stoppingToken);
                _outputBroadcaster.Subscribe(new NetworkStream(accepted, ownsSocket: true));
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
        finally
        {
            // Best-effort cleanup — both the socket and its manifest, the
            // same pair this method created together above. Leaves nothing
            // discoverable behind once the process is gone.
            try { File.Delete(SocketPath); } catch { /* best-effort cleanup */ }
            try { File.Delete(manifestPath); } catch { /* best-effort cleanup */ }
        }
    }

    private static void WriteManifestStub(int pid, string manifestPath)
    {
        try
        {
            var manifest = new SessionManifest { Pid = pid, FirstIngestedRepository = null };
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[NOTICE] Could not write session manifest stub: {ex.Message}");
        }
    }

    private static void HardenToOwnerOnly(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;

        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}