namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Metric measurements of rendered glyphs in millimetres.
/// </summary>
public sealed record TextMetrics(
    float WidthMm,
    float HeightMm,
    IReadOnlyList<float> Advances)
{
    private const float DefaultGlyphAspectWidthRatio = 0.60f;

    public static TextMetrics Empty => new(0f, 0f, Array.Empty<float>());

    /// <summary>
    /// Fast estimate of text bounding width used when exact metrics are not yet available.
    /// glyphCount * capHeight * DefaultGlyphAspectWidthRatio + (glyphCount - 1) * tracking
    /// </summary>
    public static TextMetrics Approximate(string text, float capHeight, float tracking)
    {
        if (string.IsNullOrEmpty(text))
            return Empty;

        int count = text.Length;
        float width = count * capHeight * DefaultGlyphAspectWidthRatio + Math.Max(0, count - 1) * tracking;
        var advances = new List<float>(count);
        for (int i = 0; i < count; i++)
            advances.Add(capHeight * DefaultGlyphAspectWidthRatio + tracking);

        return new TextMetrics(width, capHeight, advances);
    }
}
