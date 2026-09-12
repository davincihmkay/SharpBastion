namespace SharpBastion.Options;

/// <summary>
/// Fully resolved assistant configuration: a display name, the loaded
/// system-prompt text, the model name, and the Kagi auto-chain cap. Built once,
/// at startup, via <see cref="Load"/>. There is no embedded fallback prompt —
/// SharpBastion refuses to start with an unconfigured persona rather than silently
/// falling back to baked-in text. Every failure mode here is a hard fail with a
/// message that names the resolved path and states the fix, not just the symptom.
///
/// Name is deliberately sourced from the configured file's own base name rather
/// than any literal in source — whatever a user locally names their
/// system-prompt file (e.g. "coolassistant.md", "smartassistant.md") becomes the CLI's
/// displayed identity (banner title, prompt string). Nothing persona-specific
/// is hardcoded in the tracked codebase.
/// </summary>
public class AssistantProfile
{
    public string Name { get; }
    public string SystemPrompt { get; }
    public string ModelName { get; }
    public int KagiSearchMaxRoundTrips { get; }

    private AssistantProfile(string name, string systemPrompt, string modelName, int kagiSearchMaxRoundTrips)
    {
        Name = name;
        SystemPrompt = systemPrompt;
        ModelName = modelName;
        KagiSearchMaxRoundTrips = kagiSearchMaxRoundTrips;
    }

    public static AssistantProfile Load(AssistantOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SystemPromptPath))
            throw new InvalidOperationException(
                "[CONFIG] 'Assistant:SystemPromptPath' is not set — SharpBastion has no persona to load.\n" +
                "Fix by adding it to appsettings.json:\n" +
                "  { \"Assistant\": { \"SystemPromptPath\": \"Config/system-prompt.md\" } }\n" +
                "or by setting the 'Assistant__SystemPromptPath' environment variable.");

        var resolvedPath = Path.IsPathRooted(options.SystemPromptPath)
            ? options.SystemPromptPath
            : Path.Combine(AppContext.BaseDirectory, options.SystemPromptPath);

        if (!File.Exists(resolvedPath))
            throw new InvalidOperationException(
                $"[CONFIG] System prompt file not found at '{resolvedPath}'.\n" +
                "This file is local/per-clone and is not versioned. SharpBastion will not start with a\n" +
                "baked-in fallback persona. Fix by one of:\n" +
                "  - Copying Config/system-prompt.example.md to your own file under Config/ (any name —\n" +
                "    e.g. Config/coolassistant.md) and editing it.\n" +
                "  - Pointing 'Assistant:SystemPromptPath' in appsettings.json at an existing file.\n" +
                "  - Setting the 'Assistant__SystemPromptPath' environment variable to an absolute path\n" +
                "    (recommended for a personal file you don't want copied into build output).");

        var systemPrompt = File.ReadAllText(resolvedPath);
        if (string.IsNullOrWhiteSpace(systemPrompt))
            throw new InvalidOperationException(
                $"[CONFIG] System prompt file at '{resolvedPath}' is empty.\n" +
                "Populate it with the assistant's persona/system-prompt text before starting SharpBastion.");

        if (string.IsNullOrWhiteSpace(options.ModelName))
            throw new InvalidOperationException(
                "[CONFIG] 'Assistant:ModelName' is not set.\n" +
                "Fix by adding it to appsettings.json:\n" +
                "  { \"Assistant\": { \"ModelName\": \"qwen/qwen3.6-35b-a3b\" } }\n" +
                "or by setting the 'Assistant__ModelName' environment variable.");

        if (options.KagiSearchMaxRoundTrips <= 0)
            throw new InvalidOperationException(
                $"[CONFIG] 'Assistant:KagiSearchMaxRoundTrips' is set to {options.KagiSearchMaxRoundTrips}, which is not positive.\n" +
                "Fix by setting a positive value in appsettings.json:\n" +
                "  { \"Assistant\": { \"KagiSearchMaxRoundTrips\": 3 } }\n" +
                "or via the 'Assistant__KagiSearchMaxRoundTrips' environment variable, or remove it to accept the default (3).");

        var name = Path.GetFileNameWithoutExtension(resolvedPath);
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException(
                $"[CONFIG] Could not derive a display name from '{resolvedPath}' — the system-prompt " +
                "file needs a non-empty base name (e.g. 'coolassistant.md', 'smartassistant.md').");

        return new AssistantProfile(name, systemPrompt, options.ModelName, options.KagiSearchMaxRoundTrips);
    }
}