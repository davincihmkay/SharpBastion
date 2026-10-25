using System.Text;
using System.Text.Json.Serialization;

namespace SharpBastion.ClientResponseObject;

public class GitHubFileContentResponseObject
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    /// <summary>Git blob SHA of this file version.</summary>
    [JsonPropertyName("sha")]
    public string Sha { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("encoding")]
    public string? Encoding { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    /// <summary>
    /// Decodes inline content as UTF-8. Throws when the response is not an
    /// inline base64 file (directory, symlink, submodule, or content too large
    /// for the contents endpoint) — callers should skip such files.
    /// </summary>
    public string DecodeContent()
    {
        if (!string.Equals(Type, "file", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"'{Path}' is not a file (type '{Type}').");

        if (!string.Equals(Encoding, "base64", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrEmpty(Content) && Size > 0))
            throw new InvalidOperationException(
                $"'{Path}' has no inline base64 content (encoding '{Encoding ?? "none"}', size {Size}).");

        var base64 = (Content ?? string.Empty).Replace("\n", string.Empty).Replace("\r", string.Empty);
        return System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }
}