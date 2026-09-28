using System.Numerics;
using System.Text.Json.Serialization;
using BasicResults;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.AirChannels;

public enum AirChannelType
{
    Straight,
    Angled,
    Painted
}

public enum AirChannelRenderMode
{
    Point,
    Cone,
    Full
}

[JsonDerivedType(typeof(StraightAirChannel), "straight")]
[JsonDerivedType(typeof(AngledAirChannel), "angled")]
[JsonDerivedType(typeof(PaintedAirChannel), "painted")]
public interface IAirChannel
{
    Result<IMesh> Generate(IGeometryEngine engine, AirChannelRenderMode renderMode, IMesh? targetMesh = null);
}

public sealed record StraightAirChannel(
    Vector3 StartPoint,
    float ConeLength,
    float TotalLength,
    float TipDiameter,
    float CylinderDiameter,
    float PenetrationDepth = 1.0f) : IAirChannel
{
    public Result<IMesh> Generate(IGeometryEngine engine, AirChannelRenderMode renderMode, IMesh? targetMesh = null) => renderMode switch
    {
        AirChannelRenderMode.Full => GenerateFull(engine),
        AirChannelRenderMode.Cone => GenerateCone(engine),
        AirChannelRenderMode.Point => GeneratePoint(engine),
        _ => new Error("StraightAirChannel.InvalidRenderMode", "Unknown render mode")
    };

    private Result<IMesh> GeneratePoint(IGeometryEngine engine) =>
        engine.Generators.GenerateTube(new GeometryEngine.Core.Geometry.TubeSpec(
            [StartPoint + Vector3.UnitZ * -PenetrationDepth, StartPoint + Vector3.UnitZ],
            [TipDiameter / 2.0, TipDiameter / 2.0]
        ));

    private Result<IMesh> GenerateCone(IGeometryEngine engine) =>
        engine.Generators.GenerateTube(new GeometryEngine.Core.Geometry.TubeSpec(
            [StartPoint + Vector3.UnitZ * -PenetrationDepth, StartPoint + Vector3.UnitZ * ConeLength],
            [TipDiameter / 2.0, CylinderDiameter / 2.0]
        ));

    private Result<IMesh> GenerateFull(IGeometryEngine engine)
    {
        var coneStart = StartPoint + Vector3.UnitZ * -PenetrationDepth;
        var coneEnd = StartPoint + Vector3.UnitZ * ConeLength;
        var endPoint = StartPoint + Vector3.UnitZ * TotalLength;

        return engine.Generators.GenerateTube(new GeometryEngine.Core.Geometry.TubeSpec(
            [coneStart, coneEnd, endPoint],
            [TipDiameter / 2.0, CylinderDiameter / 2.0, CylinderDiameter / 2.0]
        ));
    }
}

public sealed record AngledAirChannel(
    Vector3 StartPoint,
    Vector3 Normal,
    float TipLength,
    float TotalLength,
    float TipDiameter,
    float Radius,
    float PenetrationDepth = 1.0f) : IAirChannel
{
    public Result<IMesh> Generate(IGeometryEngine engine, AirChannelRenderMode renderMode, IMesh? targetMesh = null)
    {
        var normal = Normal.Normalize();
        var coneEnd = StartPoint + normal * TipLength;

        var path = new List<Vector3>();

        if (renderMode == AirChannelRenderMode.Point)
        {
            path.Add(StartPoint + normal * -PenetrationDepth);
            path.Add(StartPoint + normal * 1.0f);
        }
        else if (renderMode == AirChannelRenderMode.Cone)
        {
            path.Add(StartPoint + normal * -PenetrationDepth);
            path.Add(coneEnd);
        }
        else // Full
        {
            path.Add(StartPoint + normal * -PenetrationDepth); // brought into the mesh
            path.Add(coneEnd);

            var arcPoints = engine.Generators.GenerateArc(Radius, coneEnd, normal, Vector3.UnitZ, 16).Value;
            if (arcPoints.Length > 0)
            {
                // Arc3d includes the start point, skip it
                path.AddRange(arcPoints.Skip(1));
            }

            var lastArcPoint = path.Last();
            var targetZ = StartPoint.Z + TotalLength;
            if (targetZ > lastArcPoint.Z)
            {
                path.Add(new Vector3(lastArcPoint.X, lastArcPoint.Y, targetZ));
            }
            else
            {
                path.Add(lastArcPoint + Vector3.UnitZ * 10f); // Default extension
            }
        }

        if (path.Count < 2)
        {
            return Result<IMesh>.Failure(new Error("AngledAirChannel.InvalidPath", "Generated curve must contain at least 2 points."));
        }

        var radii = new double[path.Count];
        Array.Fill(radii, Radius);
        radii[0] = TipDiameter / 2.0;

        var parameters = new GeometryEngine.Core.Geometry.TubeSpec(
            [.. path],
            [.. radii],
            16,
            true
        );

        return engine.Generators.GenerateTube(parameters);
    }
}

public sealed record PaintedAirChannel(
    IReadOnlyList<Vector3> Path,
    float Radius,
    float TotalLength,
    float PenetrationDepth) : IAirChannel
{
    public Result<IMesh> Generate(IGeometryEngine engine, AirChannelRenderMode renderMode, IMesh? targetMesh = null)
    {
        if (Path.Count == 0)
        {
            return new Error("PaintedAirChannel.InvalidPath", "Path is empty.");
        }

        if (renderMode == AirChannelRenderMode.Point ||
            (Path.Count == 1 && renderMode != AirChannelRenderMode.Full))
        {
            // Point mode or just hovering: show diameter as a sphere
            return engine.Generators.GenerateSphere(Path.Last(), Radius, 16);
        }

        if (renderMode == AirChannelRenderMode.Cone)
        {
            // Cone mode: show path along the surface
            var radii = new double[Path.Count];
            Array.Fill(radii, Radius);
            return engine.Generators.GenerateTube(new GeometryEngine.Core.Geometry.TubeSpec(
                [.. Path],
                [.. radii],
                12,
                true
            ));
        }

        // Full mode: extruded solid contoured along the path. A single click without a
        // drag leaves one point; pad it so the round offset still yields a disc (a small
        // round vertical channel) rather than failing the min-point check.
        var path = Path.Count == 1
            ? new[] { Path[0], Path[0] + new Vector3(0.01f, 0f, 0f) }
            : Path;

        var surface = targetMesh is null 
            ? BasicResults.Maybe<IMesh>.None() 
            : BasicResults.Maybe<IMesh>.Some(targetMesh);

        var parameters = new GeometryEngine.Core.Geometry.DrapedPathSpec(
            [.. path],
            Radius,
            PenetrationDepth,
            Path[0].Z + TotalLength,
            surface
        );

        return engine.Generators.GenerateDrapedPath(parameters);
    }
}
