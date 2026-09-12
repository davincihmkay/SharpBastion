namespace SharpBastion.ValueObjects;

public class KagiQuery
{
    public const int MaxLength = 400;

    public readonly string Value;

    public KagiQuery(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Kagi search query cannot be empty.");

        if (value.Length > MaxLength)
            throw new ArgumentException($"Kagi search query exceeds max length of {MaxLength} characters.");

        Value = value;
    }
}