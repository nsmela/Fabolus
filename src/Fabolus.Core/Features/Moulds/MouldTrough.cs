using BasicResults;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Geometry;
using System.Numerics;

namespace Fabolus.Core.Features.Moulds;

/// <summary>
/// Carves the trough - the basin recessed into the top of the mould that excess silicone
/// pools in while the mould fills, instead of running off the outside.
/// </summary>
internal static class MouldTrough
{
    // The cutter has to break the top surface rather than land flush with it, or the
    // boolean can leave a zero-thickness skin over the basin.
    private const float Overshoot = 1.0f;

    /// <summary>
    /// Subtracts the basin from an already-extruded mould body. <paramref name="floorZ"/> is
    /// where the basin bottoms out (the top of the cover over the bolus) and
    /// <paramref name="bodyTopZ"/> is the top of the mould.
    /// </summary>
    public static Result<IMesh> Carve(
        IGeometryEngine engine,
        IMesh body,
        Polygon2D footprint,
        float floorZ,
        float bodyTopZ,
        MouldDefinition definition)
    {
        var basinResult = BasinOutline(engine, footprint, definition);
        if (basinResult.IsFailure) return basinResult.Error;

        // Usually one region; a channel trough clipped by a concave rim can leave several.
        var cutters = new List<IMesh>(basinResult.Value.Count);
        foreach (var region in basinResult.Value)
        {
            var cutterResult = engine.Polygons.Extrude(region, floorZ, bodyTopZ + Overshoot);
            if (cutterResult.IsFailure) return cutterResult.Error;

            cutters.Add(cutterResult.Value);
        }

        return engine.Booleans.Subtract(body, [.. cutters]);
    }

    /// <summary>
    /// How much the basin holds, in cubic millimetres - the material <see cref="Carve"/> takes out
    /// of the mould top, and so the extra silicone needed to fill it.
    /// </summary>
    /// <remarks>
    /// The basin is a prism with vertical walls, so this is its outline's area times its depth -
    /// exact, and with no need to build the cutter, let alone the mould.
    ///
    /// The result is the capacity of the empty basin. Air channels surfacing through it are not
    /// discounted - they are thin next to the pool, and they are the mould's business rather than
    /// the trough's.
    /// </remarks>
    public static Result<double> Capacity(
        IGeometryEngine engine,
        Polygon2D footprint,
        MouldDefinition definition)
    {
        var depth = definition.TroughHeight;
        if (depth <= 0) return 0.0;

        var basinResult = BasinOutline(engine, footprint, definition);
        if (basinResult.IsFailure) return basinResult.Error;

        return basinResult.Value.Sum(region => region.Area) * depth;
    }

    /// <summary>
    /// The outline of the basin seen from above: the regions the trough is recessed over.
    /// </summary>
    private static Result<IReadOnlyList<Polygon2D>> BasinOutline(
        IGeometryEngine engine,
        Polygon2D footprint,
        MouldDefinition definition)
    {
        // Every trough stops short of the mould wall - that rim is what holds the silicone.
        var rimResult = engine.Polygons.Offset(footprint, -(float)definition.TroughOffset);
        if (rimResult.IsFailure)
            return TroughErrors.RimTooWide;

        if (definition.TroughShape != TroughShapeType.Channels)
            return Result<IReadOnlyList<Polygon2D>>.Success([rimResult.Value]);

        var localResult = ChannelFootprint(engine, definition);
        if (localResult.IsFailure) return localResult.Error;

        // Clipped against the rim so a channel painted out near the edge can't open it and let
        // the silicone escape.
        var clippedResult = engine.Polygons.Intersect(rimResult.Value, localResult.Value);
        if (clippedResult.IsFailure) return clippedResult.Error;

        if (clippedResult.Value.IsEmpty)
            return TroughErrors.ChannelsOutsideRim;

        return Result<IReadOnlyList<Polygon2D>>.Success(clippedResult.Value);
    }

    /// <summary>
    /// The area the channels surface over, spread out by the trough margin.
    /// </summary>
    private static Result<Polygon2D> ChannelFootprint(IGeometryEngine engine, MouldDefinition definition)
    {
        var exits = ChannelExits(engine, definition.AirChannels);
        if (exits.Count == 0)
            return TroughErrors.NoChannelExits;

        var hull = engine.Polygons.ConvexHull([.. exits]);
        if (hull.IsSuccess)
            return engine.Polygons.Offset(hull.Value, definition.TroughOffset);

        // A single channel, or several in a line, have no hull with area. Buffering the exits
        // gives what offsetting a hull would have: a disc round one, a stadium along a line.
        return engine.Polygons.BufferPath([.. exits], definition.TroughOffset);
    }

    /// <summary>
    /// Where the channels break the top of the mould, in XY.
    /// </summary>
    private static IReadOnlyList<Vector2> ChannelExits(IGeometryEngine engine, IReadOnlyList<AirChannelModel> channels) =>
        channels
            .SelectMany(channel => AirChannelFootprints.ExitPoints(engine, channel.DomainModel))
            .ToList();
}

internal static class TroughErrors
{
    public static readonly Error RimTooWide = new(
        "Mould.TroughRimTooWide",
        "The trough margin leaves no room inside the mould wall. Lower it, or widen the mould.");

    public static readonly Error NoChannelExits = new(
        "Mould.TroughNoChannels",
        "A channel trough needs at least one air channel to pool around.");

    public static readonly Error ContouredHasNoFootprint = new(
        "Mould.ContouredHasNoFootprint",
        "A contoured mould is offset from the bolus surface, not extruded from an outline.");

    public static readonly Error ChannelsOutsideRim = new(
        "Mould.TroughChannelsOutsideRim",
        "The air channels sit outside the trough rim, leaving nothing to carve. Lower the trough margin.");
}
