using Fabolus.Core.Common;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Moulds;

public abstract record MouldDefinition : IMeshCommand
{
    public Guid TargetMeshId { get; init; }
    public IReadOnlyList<AirChannelModel> AirChannels { get; init; } = Array.Empty<AirChannelModel>();

    /// <summary>
    /// How deep the trough - the basin excess silicone pools in while the mould fills - is
    /// recessed into the top of the mould. 0 leaves the top face solid.
    /// </summary>
    public double TroughHeight { get; init; }

    /// <summary>
    /// For <see cref="TroughShapeType.Footprint"/>, how far the trough stops short of the
    /// mould wall - the thickness of the rim holding the silicone in. For
    /// <see cref="TroughShapeType.Channels"/>, how far it spreads past the channel exits
    /// (and, as with any trough, how close it may come to the wall).
    /// </summary>
    public double TroughOffset { get; init; } = 2.5;

    public TroughShapeType TroughShape { get; init; } = TroughShapeType.Footprint;

    /// <summary>
    /// A channel trough with nothing to pool around is treated as no trough at all, so the
    /// mould doesn't silently grow taller for a basin that can't be carved.
    /// </summary>
    private bool HasTrough =>
        TroughHeight > 0 && (TroughShape != TroughShapeType.Channels || AirChannels.Count > 0);

    public int Priority => CommandPriority.Mould;

    /// <summary>
    /// Generates just the mould shell shape - the bolus and air channels aren't subtracted
    /// yet, so it stays cheap enough for live preview while the user is still adjusting
    /// settings. (A trough does cut the shell here, but only against a simple prism.)
    /// </summary>
    public abstract Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh);

    /// <summary>
    /// The full committed pipeline: the shell from <see cref="Generate"/>, then subtract the
    /// target mesh, then subtract each air channel. Does not take ownership of
    /// <paramref name="mesh"/>; intermediates created along the way are disposed here.
    /// </summary>
    public Result<IMesh> Apply(IGeometryEngine engine, IMesh mesh)
    {
        var generateResult = Generate(engine, mesh);
        if (generateResult.IsFailure) return generateResult.Error;

        var mouldMesh = generateResult.Value;

        var targetSubtractedResult = engine.Booleans.Subtract(mouldMesh, mesh);
        if (targetSubtractedResult.IsFailure) return targetSubtractedResult.Error;

        mouldMesh = targetSubtractedResult.Value;

        foreach (var channel in AirChannels)
        {
            // Pass the target mesh so channels that snap to the surface (painted paths)
            // bake with the same raycast-fitted bottom the live preview showed.
            var channelMeshResult = channel.DomainModel.Generate(engine, AirChannelRenderMode.Full, mesh);
            if (channelMeshResult.IsFailure)
            {
                return channelMeshResult.Error;
            }

            var channelMesh = channelMeshResult.Value;
            var subtractedResult = engine.Booleans.Subtract(mouldMesh, channelMesh);
            if (subtractedResult.IsFailure) return subtractedResult.Error;

            mouldMesh = subtractedResult.Value;
        }

        return Result<IMesh>.Success(mouldMesh);
    }

    /// <summary>
    /// Extrudes the mould body from its footprint, then recesses the trough into the top of
    /// it. <paramref name="coverTopZ"/> is where the body would have ended without a trough
    /// - the body grows upwards by <see cref="TroughHeight"/> so the cover over the bolus
    /// keeps its full thickness and becomes the floor of the basin.
    /// </summary>
    protected Result<IMesh> ExtrudeBody(IGeometryEngine engine, Polygon2D footprint, float zMin, float coverTopZ)
    {
        var bodyTopZ = HasTrough ? coverTopZ + (float)TroughHeight : coverTopZ;

        var body = engine.Generators.ExtrudePolygon(footprint, zMin, bodyTopZ);
        if (body.IsFailure || !HasTrough) return body;

        return MouldTrough.Carve(engine, body.Value, footprint, coverTopZ, bodyTopZ, this);
    }
}

public sealed record ConvexMouldDefinition(double OffsetXY = 2.0, double OffsetBottom = 2.0, double OffsetTop = 2.0) : MouldDefinition
{
    public override Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh)
    {
        var statsResult = engine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsFailure)
            return statsResult.Error;

        var bounds = statsResult.Value;
        
        var hull = engine.Generators.GetConvexHull(mesh);
        if (hull.IsFailure) return hull.Error;
        
        var offset = engine.Generators.OffsetPolygon(hull.Value, (float)OffsetXY);
        if (offset.IsFailure) return offset.Error;

        return ExtrudeBody(engine, offset.Value,
            (float)bounds.MinZ - (float)OffsetBottom,
            (float)bounds.MaxZ + (float)OffsetTop);
    }
}

public sealed record ConcaveMouldDefinition(double OffsetXY = 2.0, double OffsetBottom = 2.0, double OffsetTop = 2.0) : MouldDefinition
{
    public override Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh)
    {
        var statsResult = engine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsFailure)
            return statsResult.Error;

        var bounds = statsResult.Value;

        var shadow = engine.Generators.GetMeshShadow(mesh);
        if (shadow.IsFailure) return shadow.Error;
        
        var offset = engine.Generators.OffsetPolygon(shadow.Value, (float)OffsetXY);
        if (offset.IsFailure) return offset.Error;

        return ExtrudeBody(engine, offset.Value,
            (float)bounds.MinZ - (float)OffsetBottom,
            (float)bounds.MaxZ + (float)OffsetTop);
    }
}

public sealed record ContouredMouldDefinition(double OffsetXY = 2.0) : MouldDefinition
{
    // No trough here: this shell follows the bolus surface, so there's no flat top face to
    // recess a basin into - a cut would just open a hole through the shell.
    public override Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh)
    {
        return engine.Modifiers.Offset(mesh, (float)OffsetXY, 0);
    }
}
