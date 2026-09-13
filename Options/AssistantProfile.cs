namespace SharpBastion.Options;

/// <summary>
/// Fully resolved assistant configuration: a display name, the loaded
/// system-prompt text, the model name, the configured workspace root, and the
/// Kagi auto-chain cap. Built once, at startup, via <see cref="Load"/>. There
/// is no embedded fallback prompt — SharpBastion refuses to start with an
/// unconfigured persona rather than silently falling back to baked-in text.
/// Every failure mode here is a hard fail with a message that names the
/// resolved path and states the fix, not just the symptom.
///
/// Name is deliberately sourced from the configured file's own base name rather
/// than any literal in source — whatever a user locally names their
/// system-prompt file (e.g. "coolassistant.md", "smartassistant.md") becomes the CLI's
/// displayed identity (banner title, prompt string). Nothing persona-specific
/// is hardcoded in the tracked codebase.
/// </summary>
public class AssistantProfile
{
    /// <summary>
    /// Placeholder token substituted, verbatim, in the loaded system-prompt text
    /// with the resolved <see cref="WorkspaceRoot"/>. A no-op if the persona file
    /// doesn't contain it — existing per-clone persona files are not required to
    /// adopt it.
    /// </summary>
    private const string WorkspaceRootToken = "{{WORKSPACE_ROOT}}";

    public string Name { get; }
    public string SystemPrompt { get; }
    public string ModelName { get; }
    public string WorkspaceRoot { get; }
    public int KagiSearchMaxRoundTrips { get; }

    private AssistantProfile(string name, string systemPrompt, string modelName, string workspaceRoot, int kagiSearchMaxRoundTrips)
    {
        Name = name;
        SystemPrompt = systemPrompt;
        ModelName = modelName;
        WorkspaceRoot = workspaceRoot;
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

        if (string.IsNullOrWhiteSpace(options.WorkspaceRoot))
            throw new InvalidOperationException(
                "[CONFIG] 'Assistant:WorkspaceRoot' is not set — SharpBastion has no configured root to\n" +
                "discover or resolve repositories under, and will not silently assume one.\n" +
                "Fix by adding it to appsettings.json:\n" +
                "  { \"Assistant\": { \"WorkspaceRoot\": \"~/git\" } }\n" +
                "or by setting the 'Assistant__WorkspaceRoot' environment variable.");

        var homepath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var expandedWorkspaceRoot = options.WorkspaceRoot.StartsWith("~/")
            ? Path.Combine(homepath, options.WorkspaceRoot[2..])
            : options.WorkspaceRoot;
        var workspaceRoot = Path.GetFullPath(expandedWorkspaceRoot);

        if (!Directory.Exists(workspaceRoot))
            throw new InvalidOperationException(
                $"[CONFIG] 'Assistant:WorkspaceRoot' resolves to '{workspaceRoot}', which does not exist.\n" +
                "SharpBastion will not start with an unresolvable workspace root. Create the directory,\n" +
                "or point 'Assistant:WorkspaceRoot' (or the 'Assistant__WorkspaceRoot' environment\n" +
                "variable) at one that does.");

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

        systemPrompt = systemPrompt.Replace(WorkspaceRootToken, workspaceRoot);

        return new AssistantProfile(name, systemPrompt, options.ModelName, workspaceRoot, options.KagiSearchMaxRoundTrips);
    }
}