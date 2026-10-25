using System.Text.RegularExpressions;

namespace SharpBastion.ValueObjects;

/// <summary>
/// Identifies a single GitHub pull request. Owner and repository are validated
/// against GitHub's allowed character set so they can never carry path
/// separators or other content into an API URL. Owns case-insensitive
/// identity for owner/repo, the same way ScriptName owns its own equality.
/// </summary>
public class PullRequestReference : IEquatable<PullRequestReference>
{
    private static readonly Regex SegmentPattern =
        new(@"^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

    public readonly string Owner;
    public readonly string Repository;
    public readonly int Number;

    public PullRequestReference(string owner, string repository, int number)
    {
        if (string.IsNullOrWhiteSpace(owner) || !SegmentPattern.IsMatch(owner))
            throw new ArgumentException($"Invalid GitHub owner: '{owner}'.");

        if (string.IsNullOrWhiteSpace(repository) || !SegmentPattern.IsMatch(repository))
            throw new ArgumentException($"Invalid GitHub repository: '{repository}'.");

        if (number <= 0)
            throw new ArgumentException($"Pull request number must be positive. Got: {number}.");

        Owner = owner;
        Repository = repository;
        Number = number;
    }

    public bool Equals(PullRequestReference? other) =>
        other is not null &&
        Number == other.Number &&
        string.Equals(Owner, other.Owner, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Repository, other.Repository, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as PullRequestReference);

    public override int GetHashCode() =>
        HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(Owner),
            StringComparer.OrdinalIgnoreCase.GetHashCode(Repository),
            Number);

    public override string ToString() => $"{Owner}/{Repository}#{Number}";

    public static bool operator ==(PullRequestReference? left, PullRequestReference? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(PullRequestReference? left, PullRequestReference? right) =>
        !(left == right);
}