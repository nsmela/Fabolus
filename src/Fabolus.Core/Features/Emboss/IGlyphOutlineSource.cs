using Fabolus.Core.Common;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Decal;

/// <summary>
/// Turns text into 2D outline polygons and measures typography.
/// </summary>
/// <remarks>
/// Deliberately not part of <see cref="IGeometryEngine"/>, for two reasons.
/// <para>
/// The engine's sub-interfaces are all implemented by Geometry.MeshLib, which wraps geometry
/// libraries (MeshLib, geometry3Sharp, Clipper) and references no UI framework - that is what
/// lets the engine be swapped for a different backend. Glyph outlines, by contrast, can only
/// come from a font stack: the one implementation here is built on WPF's FormattedText and
/// PathGeometry. Hanging it off the engine would either drag WPF into the geometry assembly or
/// leave one engine member that no engine can actually implement.
/// </para>
/// <para>
/// It is also not a geometry operation. It is a typography one whose output happens to be
/// <see cref="Polygon2D"/>. The engine takes over at exactly that boundary: the polygons this
/// produces are fed to IGeometryEngine.Generators.BuildTextPrism and
/// IGeometryEngine.Polygons. Keeping the seam here means the geometry side never has to know
/// what a font is.
/// </para>
/// </remarks>
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

/// <summary>
/// Ambient glyph source for command replay.
/// </summary>
/// <remarks>
/// A service locator, and a deliberate one. Commands are rebuilt from save files by
/// <see cref="Geometry.Metadata.MeshCommandRegistry"/> with no DI container in scope, and
/// IMeshCommand.Apply is handed only an engine and a mesh - so a replayed decal command has no
/// constructor-injected way to reach a font stack. Set once at application startup from the
/// registered singleton.
/// <para>
/// Removing it means giving command replay a context object carrying the services commands may
/// need, rather than passing the engine alone. Worth doing if a second command ever needs an
/// ambient service; not worth it for one.
/// </para>
/// </remarks>
public static class GlyphOutlineSourceProvider
{
    public static IGlyphOutlineSource? Default { get; set; }
}
