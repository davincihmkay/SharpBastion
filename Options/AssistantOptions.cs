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

    /// <summary>
    /// Max number of automatic Kagi search round-trips (search executed → results
    /// fed back → model replies) SharpBastion will auto-chain within a single
    /// ask/search-resolution call while IKagiSearchProtocol is overridden. Once the
    /// budget is spent mid-chain, further proposals fall back to the safe default —
    /// queued in PendingKagiSearchQueue for manual 'review' — rather than continuing
    /// unattended. Has no effect while the protocol is not overridden: every
    /// proposal is already queued for manual review in that state, uncapped.
    /// Defaults to 3 when unset.
    /// </summary>
    public int KagiSearchMaxRoundTrips { get; set; } = 3;
}