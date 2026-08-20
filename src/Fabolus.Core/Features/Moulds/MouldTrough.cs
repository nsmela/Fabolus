using Fabolus.Core.Common;
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

    // A single channel (or two in a line) has no 2D hull to offset - it gets padded into a
    // polygon this small first, so the round offset turns it into a disc/stadium.
    private const float DegenerateHullPad = 0.05f;

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
        // Every trough stops short of the mould wall - that rim is what holds the silicone.
        var rimResult = engine.Generators.OffsetPolygon(footprint, -(float)definition.TroughOffset);
        if (rimResult.IsFailure)
            return TroughErrors.RimTooWide;

        var cutterResult = engine.Generators.ExtrudePolygon(rimResult.Value, floorZ, bodyTopZ + Overshoot);
        if (cutterResult.IsFailure) return cutterResult.Error;

        var cutter = cutterResult.Value;

        if (definition.TroughShape == TroughShapeType.Channels)
        {
            var localResult = ChannelFootprint(engine, definition);
            if (localResult.IsFailure) return localResult.Error;

            var localCutterResult = engine.Generators.ExtrudePolygon(localResult.Value, floorZ, bodyTopZ + Overshoot);
            if (localCutterResult.IsFailure) return localCutterResult.Error;

            // Clipped against the full-footprint basin so a channel painted out near the
            // edge can't open the rim and let the silicone escape.
            var clippedResult = engine.Booleans.Intersect(cutter, localCutterResult.Value);
            if (clippedResult.IsFailure) return clippedResult.Error;

            cutter = clippedResult.Value;
            if (cutter.IsEmpty)
                return TroughErrors.ChannelsOutsideRim;
        }

        return engine.Booleans.Subtract(body, cutter);
    }

    /// <summary>
    /// The area the channels surface over, spread out by the trough margin.
    /// </summary>
    private static Result<Polygon2D> ChannelFootprint(IGeometryEngine engine, MouldDefinition definition)
    {
        var exits = ChannelExits(engine, definition.AirChannels);
        if (exits.Count == 0)
            return TroughErrors.NoChannelExits;

        var hull = ConvexHull(exits);
        var polygon = new Polygon2D { OuterBoundary = Pad(hull) };

        return engine.Generators.OffsetPolygon(polygon, (float)definition.TroughOffset);
    }

    /// <summary>
    /// Where each channel breaks the top of the mould, in XY. Air channels all finish
    /// running straight up, so this is the XY the channel occupies at the mould's top face.
    /// </summary>
    private static IReadOnlyList<Vector2> ChannelExits(IGeometryEngine engine, IReadOnlyList<AirChannelModel> channels)
    {
        var points = new List<Vector2>();

        foreach (var channel in channels)
        {
            switch (channel.DomainModel)
            {
                case StraightAirChannel straight:
                    points.Add(Flatten(straight.StartPoint));
                    break;

                case AngledAirChannel angled:
                    // An angled channel leaves along the surface normal and only then arcs
                    // back to vertical, so it surfaces well off the point it was placed at.
                    // Walking the same arc the channel mesh is built from beats guessing.
                    var normal = Vector3.Normalize(angled.Normal);
                    var coneEnd = angled.StartPoint + normal * angled.TipLength;
                    points.Add(Flatten(coneEnd));

                    var arc = engine.Generators.Arc3d(angled.Radius, coneEnd, normal, Vector3.UnitZ, 16);
                    if (arc.Count > 0)
                        points.Add(Flatten(arc[^1]));
                    break;

                case PaintedAirChannel painted:
                    // Extrudes straight up from the whole painted path, so all of it counts.
                    foreach (var point in painted.Path)
                        points.Add(Flatten(point));
                    break;
            }
        }

        return points;
    }

    private static Vector2 Flatten(Vector3 point) => new(point.X, point.Y);

    /// <summary>
    /// Andrew's monotone chain. Channels placed in a line (or all at one point) have no hull
    /// with area, so those collapse to the two extremes for <see cref="Pad"/> to widen.
    /// </summary>
    private static IReadOnlyList<Vector2> ConvexHull(IReadOnlyList<Vector2> points)
    {
        if (points.Count < 3)
            return points;

        var sorted = points
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToList();

        var lower = BuildChain(sorted);
        var upper = BuildChain(Enumerable.Reverse(sorted).ToList());

        // Each chain ends on the point the other one starts from.
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);

        return lower.Count >= 3 ? lower : new[] { sorted[0], sorted[^1] };
    }

    private static List<Vector2> BuildChain(IReadOnlyList<Vector2> ordered)
    {
        var chain = new List<Vector2>();

        foreach (var point in ordered)
        {
            while (chain.Count >= 2 && Cross(chain[^2], chain[^1], point) <= 0)
                chain.RemoveAt(chain.Count - 1);

            chain.Add(point);
        }

        return chain;
    }

    private static float Cross(Vector2 origin, Vector2 a, Vector2 b) =>
        (a.X - origin.X) * (b.Y - origin.Y) - (a.Y - origin.Y) * (b.X - origin.X);

    /// <summary>
    /// Widens a one- or two-point "hull" into a polygon with area, so the offset that
    /// follows has something to grow.
    /// </summary>
    private static IReadOnlyList<Vector2> Pad(IReadOnlyList<Vector2> hull)
    {
        if (hull.Count >= 3)
            return hull;

        if (hull.Count == 1 || Vector2.DistanceSquared(hull[0], hull[^1]) < DegenerateHullPad * DegenerateHullPad)
        {
            var p = hull[0];
            return new[]
            {
                new Vector2(p.X - DegenerateHullPad, p.Y - DegenerateHullPad),
                new Vector2(p.X + DegenerateHullPad, p.Y - DegenerateHullPad),
                new Vector2(p.X + DegenerateHullPad, p.Y + DegenerateHullPad),
                new Vector2(p.X - DegenerateHullPad, p.Y + DegenerateHullPad),
            };
        }

        var (a, b) = (hull[0], hull[1]);
        var along = b - a;
        var side = along.LengthSquared() > 0
            ? Vector2.Normalize(new Vector2(-along.Y, along.X)) * DegenerateHullPad
            : new Vector2(DegenerateHullPad, 0);

        return new[] { a - side, b - side, b + side, a + side };
    }
}

internal static class TroughErrors
{
    public static readonly Error RimTooWide = new(
        "Mould.TroughRimTooWide",
        "The trough margin leaves no room inside the mould wall. Lower it, or widen the mould.");

    public static readonly Error NoChannelExits = new(
        "Mould.TroughNoChannels",
        "A channel trough needs at least one air channel to pool around.");

    public static readonly Error ChannelsOutsideRim = new(
        "Mould.TroughChannelsOutsideRim",
        "The air channels sit outside the trough rim, leaving nothing to carve. Lower the trough margin.");
}
