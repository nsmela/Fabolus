using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Emboss;

/// <summary>
/// Service interface for extracting 2D outline polygons and measuring typography.
/// </summary>
public interface IGlyphOutlineSource
{
    /// <summary>
    /// Extracts centered 2D polygon contours (outer boundaries and holes) for the specified text.
    /// </summary>
    Result<IReadOnlyList<Polygon2D>> GetOutlines(string text, DecalFont font, float capHeight, float tracking);

    /// <summary>
    /// Measures exact bounding width, height, and per-glyph advances in millimetres.
    /// </summary>
    TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking);
}

public static class GlyphOutlineSourceProvider
{
    public static IGlyphOutlineSource? Default { get; set; }
}
