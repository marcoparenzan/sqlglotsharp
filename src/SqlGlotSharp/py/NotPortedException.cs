namespace SqlGlotSharp;

/// <summary>Explicit failure for an upstream member not yet translated.</summary>
public sealed class NotPortedException(string anchor) : NotSupportedException($"Not ported: {anchor}")
{
    public string Anchor { get; } = anchor;
}
