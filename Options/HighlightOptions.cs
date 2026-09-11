namespace SharpBastion.Options;

/// <summary>
/// Bound from the "Highlight" configuration section (appsettings.json, or the
/// "Highlight__" environment-variable prefix) via the standard .NET Options
/// pattern — mirrors AssistantOptions. Holds only the raw configured value;
/// HighlightProfile.Load validates it fail-fast, the same way
/// AssistantProfile.Load validates AssistantOptions.
/// </summary>
public class HighlightOptions
{
    public const string SectionName = "Highlight";

    /// <summary>
    /// Smdn.LibHighlightSharp theme name (e.g. "github", "edit-xcode") used to
    /// render colorized diffs in the 'review' command. Required — there is no
    /// baked-in default theme.
    /// </summary>
    public string Theme { get; set; } = string.Empty;
}