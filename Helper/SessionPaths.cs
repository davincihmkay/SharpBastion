namespace SharpBastion.Helper;

public static class SessionPaths
{
    private static readonly string _homepath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static readonly string SharpBastionHomeDir = Path.Combine(_homepath, ".sharpbastion");

    public static string SocketPathFor(int pid) => Path.Combine(SharpBastionHomeDir, $"session-{pid}.sock");

    public static string ManifestPathFor(int pid) => Path.Combine(SharpBastionHomeDir, $"session-{pid}.json");
}