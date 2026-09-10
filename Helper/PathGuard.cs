namespace SharpBastion.Helper;

public static class PathGuard
{
    public static bool IsWithinRoot(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);

        return relative != ".."
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar)
               && !Path.IsPathRooted(relative);
    }
}