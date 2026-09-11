namespace SharpBastion.Options;

/// <summary>
/// Resolved, validated highlight configuration. Mirrors AssistantProfile:
/// there is no embedded fallback theme — SharpBastion refuses to start
/// colorized diff rendering with an unconfigured theme rather than silently
/// defaulting to one baked into source.
/// </summary>
public class HighlightProfile
{
    public string Theme { get; }

    private HighlightProfile(string theme)
    {
        Theme = theme;
    }

    public static HighlightProfile Load(HighlightOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Theme))
            throw new InvalidOperationException(
                "[CONFIG] 'Highlight:Theme' is not set — SharpBastion has no diff-highlighting theme to load.\n" +
                "Fix by adding it to appsettings.json:\n" +
                "  { \"Highlight\": { \"Theme\": \"github\" } }\n" +
                "or by setting the 'Highlight__Theme' environment variable.");

        return new HighlightProfile(options.Theme);
    }
}