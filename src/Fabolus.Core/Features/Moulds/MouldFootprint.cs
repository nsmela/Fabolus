using Fabolus.Core.Common;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.Moulds;

/// <summary>
/// Builds the outline the mould body is extruded from.
/// </summary>
internal static class MouldFootprint
{
    /// <summary>
    /// The bolus outline grown by the wall thickness, merged with the ground each air
    /// channel covers - grown by the same wall thickness. Without the channels in it, one
    /// placed near the edge runs out through the side of the mould and slices the wall open
    /// instead of venting out the top; with them, the contour bulges out to keep the channel
    /// buried in material.
    /// </summary>
    public static Result<Polygon2D> Build(
        IGeometryEngine engine,
        Polygon2D outline,
        double wallThickness,
        IReadOnlyList<AirChannelModel> channels)
    {
        var wallResult = engine.Generators.OffsetPolygon(outline, (float)wallThickness);
        if (wallResult.IsFailure || channels.Count == 0)
            return wallResult;

        var parts = new List<Polygon2D> { wallResult.Value };

        foreach (var channel in channels)
        {
            var footprint = AirChannelFootprints.Of(engine, channel.DomainModel);
            if (footprint.Path.Count == 0)
                continue;

            var buffered = engine.Generators.BufferPath(footprint.Path, footprint.Radius + (float)wallThickness);
            if (buffered.IsFailure)
                continue;

            parts.Add(buffered.Value);
        }

        if (parts.Count == 1)
            return wallResult;

        // Channels start on the bolus surface, so their footprints always overlap its wall.
        // If one somehow doesn't, the union drops the island and the mould is no worse off
        // than it was before the channels were folded in - as it is if Clipper fails outright.
        var union = engine.Generators.UnionPolygons(parts);
        return union.IsSuccess ? union : wallResult;
    }
}
