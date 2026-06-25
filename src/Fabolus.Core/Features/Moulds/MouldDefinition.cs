using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Features.AirChannels;

namespace Fabolus.Core.Features.Moulds;

public abstract record MouldDefinition
{
    public Guid TargetMeshId { get; init; }
    public IReadOnlyList<AirChannelModel> AirChannels { get; init; } = Array.Empty<AirChannelModel>();

    public abstract Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh);
}

public sealed record ConvexMouldDefinition(double OffsetXY, double OffsetBottom, double OffsetTop) : MouldDefinition
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

public sealed record ConcaveMouldDefinition(double OffsetXY, double OffsetBottom, double OffsetTop) : MouldDefinition
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

public sealed record ContouredMouldDefinition(double OffsetXY) : MouldDefinition
{
    public override Result<IMesh> Generate(IGeometryEngine engine, IMesh mesh)
    {
        return engine.Modifiers.Offset(mesh, (float)OffsetXY, 0);
    }
}
