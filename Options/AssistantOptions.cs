namespace SharpBastion.Options;

/// <summary>
/// Bound from the "Assistant" configuration section (appsettings.json, or the
/// "Assistant__" environment-variable prefix) via the standard .NET Options
/// pattern. Holds only pointers/identifiers — the system-prompt *text* itself
/// lives in an external file referenced by SystemPromptPath (see AssistantProfile),
/// so the persona can be edited without recompiling.
/// </summary>
public class AssistantOptions
{
    public const string SectionName = "Assistant";

    /// <summary>
    /// Path to the system-prompt file. May be absolute, or relative to the
    /// application's base directory (AppContext.BaseDirectory) — deliberately
    /// not the process's current working directory, which varies across
    /// `dotnet run`, a published binary, and the Docker image.
    /// </summary>
    public string SystemPromptPath { get; set; } = string.Empty;

    /// <summary>Backend model identifier passed to the LLM client (e.g. LmStudio).</summary>
    public string ModelName { get; set; } = string.Empty;
}