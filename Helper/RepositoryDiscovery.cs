namespace SharpBastion.Helper;

public static class RepositoryDiscovery
{
    public static IReadOnlyList<string> ListCandidates(string root)
    {
        if (!Directory.Exists(root))
            return Array.Empty<string>();

        return Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Where(dir => !Path.GetFileName(dir).StartsWith('.'))
            .OrderBy(dir => dir, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}