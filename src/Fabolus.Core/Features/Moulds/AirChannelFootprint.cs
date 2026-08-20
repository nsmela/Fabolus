using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Geometry;
using System.Numerics;

namespace Fabolus.Core.Features.Moulds;

/// <summary>
/// The ground each air channel covers in XY, which the mould has to account for twice over:
/// the outer contour has to contain it (or the channel slices the wall open on its way out)
/// and the trough has to pool around where it surfaces.
/// </summary>
internal readonly record struct AirChannelFootprint(IReadOnlyList<Vector2> Path, float Radius)
{
    public static readonly AirChannelFootprint Empty = new(Array.Empty<Vector2>(), 0f);
}

internal static class AirChannelFootprints
{
    /// <summary>
    /// The channel's centreline flattened into XY, along with the radius it carries.
    /// </summary>
    public static AirChannelFootprint Of(IGeometryEngine engine, IAirChannel channel)
    {
        switch (channel)
        {
            case StraightAirChannel straight:
                // Rises straight up, so it never leaves the point it was placed at.
                return new AirChannelFootprint(
                    new[] { Flatten(straight.StartPoint) },
                    straight.CylinderDiameter / 2f);

            case AngledAirChannel angled:
            {
                // Leaves along the surface normal and only then arcs back to vertical, so it
                // travels a fair way in XY before it starts climbing. Walking the same arc
                // the channel mesh is built from beats approximating it.
                var normal = Vector3.Normalize(angled.Normal);
                var coneEnd = angled.StartPoint + normal * angled.TipLength;

                var path = new List<Vector2> { Flatten(angled.StartPoint), Flatten(coneEnd) };
                foreach (var point in engine.Generators.Arc3d(angled.Radius, coneEnd, normal, Vector3.UnitZ, 16))
                    path.Add(Flatten(point));

                return new AirChannelFootprint(path, angled.Radius);
            }

            case PaintedAirChannel painted when painted.Path.Count > 0:
                // Extruded straight up from the whole painted path.
                return new AirChannelFootprint(
                    painted.Path.Select(Flatten).ToList(),
                    painted.Radius);

            default:
                return AirChannelFootprint.Empty;
        }
    }

    /// <summary>
    /// Where the channel breaks the top face of the mould. Every channel finishes running
    /// vertically, so that's the end of its path - except a painted one, which rises from
    /// all of it at once.
    /// </summary>
    public static IReadOnlyList<Vector2> ExitPoints(IGeometryEngine engine, IAirChannel channel)
    {
        var footprint = Of(engine, channel);
        if (footprint.Path.Count == 0)
            return Array.Empty<Vector2>();

        return channel is PaintedAirChannel
            ? footprint.Path
            : new[] { footprint.Path[^1] };
    }

    private static Vector2 Flatten(Vector3 point) => new(point.X, point.Y);
}
