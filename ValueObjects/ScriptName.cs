namespace SharpBastion.ValueObjects;

/// <summary>
/// A validated scheduled-script filename (must end in .py). Also owns
/// case-insensitive identity/equality — used directly as the dictionary/
/// set key for job lookups and dedupe (JobSchedulerService, PendingJobRunQueue).
/// </summary>
public class ScriptName : IEquatable<ScriptName>
{
    public readonly string Value;

    public ScriptName(string value)
    {
        if (!string.Equals(Path.GetExtension(value), ".py", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Only .py scripts may be scheduled. Got: '{value}'.");

        Value = value;
    }

    public bool Equals(ScriptName? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as ScriptName);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    public override string ToString() => Value;

    public static bool operator ==(ScriptName? left, ScriptName? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(ScriptName? left, ScriptName? right) => !(left == right);
}