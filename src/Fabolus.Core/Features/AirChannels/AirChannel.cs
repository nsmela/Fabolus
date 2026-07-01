using System.Numerics;
using Fabolus.Core.Common;
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

public interface IAirChannel
{
    IAirChannel SetPreview(Vector3 startPoint, Vector3 direction);
    Result<IMesh> Generate(IGeometryEngine engine, AirChannelRenderMode renderMode);
}

public sealed record StraightAirChannel(
    Vector3 StartPoint,
    float ConeLength,
    float TotalLength,
    float TipDiameter,
    float CylinderDiameter,
    float PenetrationDepth = 1.0f) : IAirChannel
{
    public IAirChannel SetPreview(Vector3 startPoint, Vector3 direction) =>
    this with { StartPoint = startPoint };

    public Result<IMesh> Generate(IGeometryEngine engine, AirChannelRenderMode renderMode) => renderMode switch
    {
        AirChannelRenderMode.Full => GenerateFull(engine),
        AirChannelRenderMode.Cone => GenerateCone(engine),
        AirChannelRenderMode.Point => GeneratePoint(engine),
        _ => new Error("StraightAirChannel.InvalidRenderMode", "Unknown render mode")
    };

    private Result<IMesh> GeneratePoint(IGeometryEngine engine) =>
        engine.Generators.GenerateTube(new TubeParameters
        {
            Path = new[] { StartPoint + Vector3.UnitZ * -PenetrationDepth, StartPoint + Vector3.UnitZ },
            Radii = new[] { TipDiameter / 2f, TipDiameter / 2f },
        });

    private Result<IMesh> GenerateCone(IGeometryEngine engine) =>
        engine.Generators.GenerateTube(new TubeParameters
        {
            Path = new[] { StartPoint + Vector3.UnitZ * -PenetrationDepth, StartPoint + Vector3.UnitZ * ConeLength },
            Radii = new[] { TipDiameter / 2f, CylinderDiameter / 2f },
        });

    private Result<IMesh> GenerateFull(IGeometryEngine engine)
    {
        var coneStart = StartPoint + Vector3.UnitZ * -PenetrationDepth; // brought into the mesh
        var coneEnd = StartPoint + Vector3.UnitZ * ConeLength;
        var endPoint = StartPoint + Vector3.UnitZ * TotalLength;

        return engine.Generators.GenerateTube(new TubeParameters
        {
            Path = new[] { coneStart, coneEnd, endPoint },
            Radii = new[] { TipDiameter / 2f, CylinderDiameter / 2f, CylinderDiameter / 2f }
        });
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
    public IAirChannel SetPreview(Vector3 startPoint, Vector3 direction) =>
        this with { StartPoint = startPoint, Normal = direction };

    public Result<IMesh> Generate(IGeometryEngine engine, AirChannelRenderMode renderMode)
    {
        var normal = Vector3.Normalize(Normal);
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

            var arcPoints = engine.Generators.Arc3d(Radius, coneEnd, normal, Vector3.UnitZ, 16);
            if (arcPoints.Count > 0)
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

        var radii = new float[path.Count];
        Array.Fill(radii, Radius);
        radii[0] = TipDiameter / 2f;

        var parameters = new TubeParameters
        {
            Path = path,
            Radii = radii,
            Segments = 16,
            Capped = true
        };

        return engine.Generators.GenerateTube(parameters);
    }
}

public sealed record PaintedAirChannel(
    IReadOnlyList<Vector3> Path,
    float Radius,
    float TotalLength,
    float PenetrationDepth,
    IMesh? TargetMesh = null) : IAirChannel
{
    public IAirChannel SetPreview(Vector3 startPoint, Vector3 direction) =>
        this with { Path = new[] { startPoint } };

    public Result<IMesh> Generate(IGeometryEngine engine, AirChannelRenderMode renderMode)
    {
        if (Path.Count == 0)
        {
            return new Error("PaintedAirChannel.InvalidPath", "Path is empty.");
        }

        if (Path.Count == 1 || renderMode == AirChannelRenderMode.Point)
        {
            // Point mode or just hovering: show diameter as a sphere
            return engine.Generators.GenerateSphere(Path.Last(), Radius, 16);
        }

        if (renderMode == AirChannelRenderMode.Cone)
        {
            // Cone mode: show path along the surface
            var radii = new float[Path.Count];
            Array.Fill(radii, Radius);
            return engine.Generators.GenerateTube(new TubeParameters
            {
                Path = Path.ToList(),
                Radii = radii,
                Segments = 12,
                Capped = true
            });
        }

        // Full mode: extruded solid contoured along the path
        var parameters = new ExtrudedPathParameters
        {
            Path = Path,
            Radius = Radius,
            ZMin = PenetrationDepth, // passed as depth
            ZMax = Path[0].Z + TotalLength,  // pass absolute Z for the top
            TargetMesh = TargetMesh  // pass mesh down for raycasting
        };

        return engine.Generators.GenerateExtrudedPath(parameters);
    }
}
