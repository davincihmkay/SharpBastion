namespace SharpBastion.Helper;

/// <summary>
/// Maps a file extension (as returned by Path.GetExtension) to a
/// Smdn.LibHighlightSharp / highlight.js-style language identifier for
/// ConsoleHighlighterService.SetSyntax. Deliberately scoped to the same
/// extensions Domain.File.AllowedExtensions gates write proposals with, so
/// this table can't silently drift from what the system actually allows onto
/// disk. Anything unmapped (or an allowlisted extensionless filename like
/// LICENSE/NOTICE) falls back to "plaintext".
///
/// NOTE: these language ids are best-effort — not verified against
/// Smdn.LibHighlightSharp's actual supported-syntax list (no network access
/// at authoring time). If SetSyntax throws on an unrecognized id,
/// ConsoleHighlighterService's catch-all falls back to the plain-text diff,
/// so a wrong id here degrades gracefully rather than crashing 'review' —
/// but the mapping should still be spot-checked against the library's real
/// syntax names before relying on it.
/// </summary>
public static class LanguageMap
{
    private static readonly IReadOnlyDictionary<string, string> _map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".cs"] = "csharp",
            [".json"] = "json",
            [".yaml"] = "yaml",
            [".yml"] = "yaml",
            [".xml"] = "xml",
            [".md"] = "markdown",
            [".config"] = "xml",
            [".toml"] = "toml",
            [".ini"] = "ini",
            [".sql"] = "sql",
            [".html"] = "html",
            [".css"] = "css",
            [".js"] = "javascript",
            [".ts"] = "typescript",
            [".proto"] = "protobuf",
            [".sh"] = "bash",
            [".ps1"] = "powershell",
            [".bat"] = "dos",
            [".py"] = "python",
        };

    public static string Resolve(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        return _map.TryGetValue(extension, out var languageId) ? languageId : "plaintext";
    }
}