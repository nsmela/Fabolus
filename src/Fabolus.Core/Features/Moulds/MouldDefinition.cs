using Fabolus.Core.Common;
using Fabolus.Core.Features.AirChannels;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;

namespace Fabolus.Core.Features.Moulds;

public abstract record MouldDefinition : IMeshCommand
{
    public Guid TargetMeshId { get; init; }
    public IReadOnlyList<AirChannelModel> AirChannels { get; init; } = Array.Empty<AirChannelModel>();

    public int Priority => CommandPriority.Mould;

    /// <summary>
    /// Generates just the mould shell shape (no boolean subtraction) - cheap enough for
    /// live preview while the user is still adjusting settings.
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
        
        var extrude = engine.Generators.ExtrudePolygon(offset.Value, 
            (float)bounds.MinZ - (float)OffsetBottom, 
            (float)bounds.MaxZ + (float)OffsetTop);
            
        return extrude;
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

        var extrude = engine.Generators.ExtrudePolygon(offset.Value,
            (float)bounds.MinZ - (float)OffsetBottom,
            (float)bounds.MaxZ + (float)OffsetTop);

        return extrude;
    }
}

public sealed record ContouredMouldDefinition(double OffsetXY = 2.0) : MouldDefinition
{
    public override Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh)
    {
        return engine.Modifiers.Offset(mesh, (float)OffsetXY, 0);
    }
}
