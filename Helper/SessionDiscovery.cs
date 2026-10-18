using System.Text.Json;
using SharpBastion.Manifest;

namespace SharpBastion.Helper;

public static class SessionDiscovery
{
    private const string SocketPrefix = "session-";
    private const string SocketExtension = ".sock";

    public static IReadOnlyList<SessionCandidate> ListCandidates()
    {
        if (!Directory.Exists(SessionPaths.SharpBastionHomeDir))
            return Array.Empty<SessionCandidate>();

        var ownPid = Environment.ProcessId;
        var candidates = new List<SessionCandidate>();

        foreach (var socketPath in Directory.EnumerateFiles(SessionPaths.SharpBastionHomeDir, $"{SocketPrefix}*{SocketExtension}"))
        {
            var fileName = Path.GetFileNameWithoutExtension(socketPath); // "session-<pid>"
            var pidText = fileName[SocketPrefix.Length..];

            if (!int.TryParse(pidText, out var pid) || pid == ownPid)
                continue;

            if (!IsProcessAlive(pid))
                continue; // Stale socket — not this helper's job to clean up, just don't offer it.

            candidates.Add(new SessionCandidate(pid, TryReadFirstIngestedRepository(pid)));
        }

        return candidates.OrderBy(c => c.Pid).ToList();
    }

    private static bool IsProcessAlive(int pid)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryReadFirstIngestedRepository(int pid)
    {
        var manifestPath = SessionPaths.ManifestPathFor(pid);
        if (!File.Exists(manifestPath)) return null;

        try
        {
            var json = File.ReadAllText(manifestPath);
            return JsonSerializer.Deserialize<SessionManifest>(json)?.FirstIngestedRepository;
        }
        catch
        {
            // Manifest mid-write, truncated, or otherwise unreadable —
            // degrade to "no repository info", not a crash of the whole
            // `listen` listing.
            return null;
        }
    }
}

public record SessionCandidate(int Pid, string? FirstIngestedRepository);