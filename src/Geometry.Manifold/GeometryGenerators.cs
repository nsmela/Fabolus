using System.Numerics;
using Clipper2Lib;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryManifold.Internal;

namespace GeometryManifold;

/// <summary>
/// Procedural mesh generation. Nearly all of this is plain arithmetic that carried over from the
/// MeshLib engine untouched; only the extruded-path builder needed replacing, since it leaned on
/// MeshLib's planar triangulator and spatial index.
/// </summary>
internal sealed class GeometryGenerators : IGeometryGenerators
{
    private readonly GeometryEngine _engine;

    public GeometryGenerators(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<IMesh> GenerateTube(TubeParameters parameters)
    {
        if (parameters.Path.Count < 2)
            return GeometryErrors.InvalidPath;

        if (parameters.Radii.Count != parameters.Path.Count)
            return GeometryErrors.InvalidRadii;

        if (parameters.Radii.Any(r => r <= 0))
            return GeometryErrors.InvalidRadius;

        if (parameters.Segments < 3)
            return GeometryErrors.InvalidSegments;

        var vertices = new List<double>();
        var triangles = new List<int>();

        int segments = parameters.Segments;

        // Generate ring vertices at each path point
        Vector3 currentU = Vector3.Zero;
        Vector3 currentW = Vector3.Zero;

        for (int pathIndex = 0; pathIndex < parameters.Path.Count; pathIndex++)
        {
            var position = parameters.Path[pathIndex];
            float radius = parameters.Radii[pathIndex];
            var direction = ComputeDirection(parameters.Path, pathIndex);

            if (pathIndex == 0)
            {
                (currentU, currentW) = ComputeOrthogonalBasis(direction);
            }
            else
            {
                var prevDirection = ComputeDirection(parameters.Path, pathIndex - 1);
                var axis = Vector3.Cross(prevDirection, direction);

                if (axis.LengthSquared() > 1e-8f)
                {
                    var angle = (float)Math.Acos(Math.Clamp(Vector3.Dot(prevDirection, direction), -1f, 1f));
                    var rot = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), angle);
                    currentU = Vector3.Transform(currentU, rot);
                }

                currentU = Vector3.Normalize(currentU - direction * Vector3.Dot(currentU, direction));
                currentW = Vector3.Cross(direction, currentU);
            }

            for (int seg = 0; seg < segments; seg++)
            {
                double angle = 2.0 * Math.PI * seg / segments;
                float c = (float)Math.Cos(angle);
                float s = (float)Math.Sin(angle);
                Vector3 radial = currentU * c + currentW * s;
                Vector3 point = position + radial * radius;

                vertices.Add(point.X);
                vertices.Add(point.Y);
                vertices.Add(point.Z);
            }
        }

        // Generate side wall triangles between consecutive rings
        for (int pathIndex = 0; pathIndex < parameters.Path.Count - 1; pathIndex++)
        {
            int ringStart = pathIndex * segments;
            int nextRingStart = (pathIndex + 1) * segments;

            for (int seg = 0; seg < segments; seg++)
            {
                int next = (seg + 1) % segments;

                int curr = ringStart + seg;
                int currNext = ringStart + next;
                int aboveCurr = nextRingStart + seg;
                int aboveNext = nextRingStart + next;

                triangles.Add(curr);
                triangles.Add(currNext);
                triangles.Add(aboveCurr);

                triangles.Add(currNext);
                triangles.Add(aboveNext);
                triangles.Add(aboveCurr);
            }
        }

        // Generate end caps
        if (parameters.Capped)
        {
            int startCenterIdx = vertices.Count / 3;
            var startPos = parameters.Path[0];
            vertices.Add(startPos.X);
            vertices.Add(startPos.Y);
            vertices.Add(startPos.Z);

            for (int seg = 0; seg < segments; seg++)
            {
                int next = (seg + 1) % segments;
                triangles.Add(next);
                triangles.Add(seg);
                triangles.Add(startCenterIdx);
            }

            int endCenterIdx = vertices.Count / 3;
            var endPos = parameters.Path[^1];
            vertices.Add(endPos.X);
            vertices.Add(endPos.Y);
            vertices.Add(endPos.Z);

            int lastRingStart = (parameters.Path.Count - 1) * segments;
            for (int seg = 0; seg < segments; seg++)
            {
                int next = (seg + 1) % segments;
                triangles.Add(lastRingStart + seg);
                triangles.Add(lastRingStart + next);
                triangles.Add(endCenterIdx);
            }
        }

        var meshResult = _engine.CreateMesh(
            vertices.ToArray().AsSpan(),
            triangles.ToArray().AsSpan());

        if (meshResult.IsFailure)
            return meshResult;

        var mesh = meshResult.Value;
        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, "Generated Tube")
             .Set(CoreKeys.CreatedBy, $"GenerateTube(segments={parameters.Segments}, points={parameters.Path.Count})"));

        return Result.Success(mesh.WithMetadata(metadata));
    }

    public Result<IMesh> GenerateSphere(Vector3 center, double radius, int slices = 16)
    {
        if (radius <= 0)
            return GeometryErrors.InvalidRadius;

        var vertices = new List<double>();
        var triangles = new List<int>();

        // North pole
        vertices.Add(center.X);
        vertices.Add(center.Y + radius);
        vertices.Add(center.Z);

        int stacks = slices;

        // Rings
        for (int j = 1; j < stacks; j++)
        {
            double phi = Math.PI * j / stacks;
            float cosPhi = (float)Math.Cos(phi);
            float sinPhi = (float)Math.Sin(phi);

            for (int i = 0; i < slices; i++)
            {
                double theta = 2.0 * Math.PI * i / slices;
                float cosTheta = (float)Math.Cos(theta);
                float sinTheta = (float)Math.Sin(theta);

                float x = (float)radius * sinPhi * cosTheta;
                float y = (float)radius * cosPhi;
                float z = (float)radius * sinPhi * sinTheta;

                vertices.Add(center.X + x);
                vertices.Add(center.Y + y);
                vertices.Add(center.Z + z);
            }
        }

        // South pole
        vertices.Add(center.X);
        vertices.Add(center.Y - radius);
        vertices.Add(center.Z);

        int southPoleIndex = vertices.Count / 3 - 1;

        // Top cap triangles
        for (int i = 0; i < slices; i++)
        {
            int nextI = (i + 1) % slices;
            triangles.Add(0); // North pole
            triangles.Add(1 + nextI);
            triangles.Add(1 + i);
        }

        // Middle rings
        for (int j = 0; j < stacks - 2; j++)
        {
            int currRowStart = 1 + j * slices;
            int nextRowStart = 1 + (j + 1) * slices;

            for (int i = 0; i < slices; i++)
            {
                int nextI = (i + 1) % slices;

                int v1 = currRowStart + i;
                int v2 = currRowStart + nextI;
                int v3 = nextRowStart + i;
                int v4 = nextRowStart + nextI;

                triangles.Add(v1);
                triangles.Add(v2);
                triangles.Add(v3);

                triangles.Add(v2);
                triangles.Add(v4);
                triangles.Add(v3);
            }
        }

        // Bottom cap triangles
        int lastRowStart = 1 + (stacks - 2) * slices;
        for (int i = 0; i < slices; i++)
        {
            int nextI = (i + 1) % slices;
            triangles.Add(southPoleIndex); // South pole
            triangles.Add(lastRowStart + i);
            triangles.Add(lastRowStart + nextI);
        }

        return _engine.CreateMesh(
            vertices.ToArray().AsSpan(),
            triangles.ToArray().AsSpan());
    }

    public IReadOnlyList<Vector3> Arc3d(float bendRadius, Vector3 startPoint, Vector3 startDirection, Vector3 endDirection, int segmentsCount)
    {
        var points = new List<Vector3>();
        var d1 = Vector3.Normalize(startDirection);
        var d2 = Vector3.Normalize(endDirection);

        if (Vector3.Dot(d1, d2) >= 0.999f)
        {
            points.Add(startPoint);
            return points;
        }

        var n = Vector3.Cross(d1, d2);
        if (n.LengthSquared() < 1e-6f)
        {
            var temp = Math.Abs(d1.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            n = Vector3.Cross(d1, temp);
        }
        n = Vector3.Normalize(n);

        var toCenter = Vector3.Normalize(Vector3.Cross(n, d1));
        var center = startPoint + toCenter * bendRadius;

        var dot = Math.Clamp(Vector3.Dot(d1, d2), -1f, 1f);
        var totalAngle = (float)Math.Acos(dot);

        var r0 = startPoint - center;

        for (int i = 0; i <= segmentsCount; i++)
        {
            float t = (float)i / segmentsCount;
            float angle = t * totalAngle;

            var rot = Quaternion.CreateFromAxisAngle(n, angle);
            var radial = Vector3.Transform(r0, rot);
            points.Add(center + radial);
        }

        return points;
    }

    public Result<IMesh> GenerateExtrudedPath(ExtrudedPathParameters parameters)
    {
        if (parameters.Path.Count < 2)
            return GeometryErrors.InvalidPath;

        if (parameters.Radius <= 0)
            return GeometryErrors.InvalidRadius;

        // 1. Project the path to 2D and buffer it into a closed outline.
        const double scale = 100000.0;
        var path2d = new Path64();
        foreach (var pt in parameters.Path)
        {
            path2d.Add(new Point64((long)Math.Round(pt.X * scale), (long)Math.Round(pt.Y * scale)));
        }

        var offsetter = new ClipperOffset();
        offsetter.AddPaths(new Paths64 { path2d }, JoinType.Round, EndType.Round);
        var solution = new Paths64();
        offsetter.Execute(parameters.Radius * scale, solution);

        if (solution.Count == 0)
            return new Error("Geometry.OffsetFailed", "Failed to generate offset polygon.");

        // 2. Triangulate it. Manifold's Extrude would triangulate and extrude in one call, but it
        // can only raise a flat top from a flat bottom - here every vertex takes its own Z off the
        // target surface, so the triangles are needed on their own.
        var contours = solution
            .Select(contour => (IReadOnlyList<Vector2>)contour
                .Select(pt => new Vector2((float)(pt.X / scale), (float)(pt.Y / scale)))
                .ToList())
            .ToList();

        var (points2D, faces2D) = PolygonTriangulator.Triangulate(contours);
        if (faces2D.Count == 0)
            return new Error("Geometry.TriangulationFailed", "Failed to triangulate buffered path.");

        // 3. Extrude vertically, contouring the bottom onto the target mesh where there is one.
        var penetration = parameters.ZMin; // Used as depth below the surface.
        var flatTopZ = parameters.ZMax;    // Used as an absolute top Z.

        var targetBvh = parameters.TargetMesh is not null
            ? new MeshBvh(parameters.TargetMesh.Vertices, parameters.TargetMesh.Triangles)
            : null;

        var vertices = new List<double>(points2D.Count * 6);
        var triangles = new List<int>(faces2D.Count * 6 + points2D.Count * 6);

        var bottomMap = new int[points2D.Count];
        var topMap = new int[points2D.Count];

        for (int i = 0; i < points2D.Count; i++)
        {
            var v = points2D[i];
            float z = GetInterpolatedZ(parameters.Path, v.X, v.Y);

            if (targetBvh is not null)
            {
                // Drop a ray from well above onto the surface, as the MeshLib path did.
                var origin = new Vector3(v.X, v.Y, z + 1000.0f);
                if (targetBvh.Raycast(origin, -Vector3.UnitZ, out float distance, out _))
                {
                    z = origin.Z - distance;
                }
            }

            bottomMap[i] = vertices.Count / 3;
            vertices.Add(v.X);
            vertices.Add(v.Y);
            vertices.Add(z - penetration);

            topMap[i] = vertices.Count / 3;
            vertices.Add(v.X);
            vertices.Add(v.Y);

            // A flat top, but never below the surface it rises from.
            vertices.Add(Math.Max(z + 1.0f, flatTopZ));
        }

        var edgeCounts = new Dictionary<(int, int), int>();

        foreach (var (a, b, c) in faces2D)
        {
            // The bottom face points down, so it is wound the other way from the top.
            triangles.Add(bottomMap[a]); triangles.Add(bottomMap[c]); triangles.Add(bottomMap[b]);
            triangles.Add(topMap[a]); triangles.Add(topMap[b]); triangles.Add(topMap[c]);

            EdgeCounter.Add(edgeCounts, a, b);
            EdgeCounter.Add(edgeCounts, b, c);
            EdgeCounter.Add(edgeCounts, c, a);
        }

        // Side walls close the solid along every edge with no opposing twin.
        foreach (var kvp in edgeCounts)
        {
            var (a, b) = kvp.Key;
            if (edgeCounts.ContainsKey((b, a))) continue;

            triangles.Add(bottomMap[a]); triangles.Add(bottomMap[b]); triangles.Add(topMap[b]);
            triangles.Add(bottomMap[a]); triangles.Add(topMap[b]); triangles.Add(topMap[a]);
        }

        var meshResult = _engine.CreateMesh(
            vertices.ToArray().AsSpan(),
            triangles.ToArray().AsSpan());

        if (meshResult.IsFailure)
            return meshResult;

        var metadata = new MeshMetadata().WithProperties(m =>
            m.Set(CoreKeys.Id, Guid.NewGuid())
             .Set(CoreKeys.Name, "Painted Air Channel")
             .Set(CoreKeys.CreatedBy, $"GenerateExtrudedPath(radius={parameters.Radius}, points={parameters.Path.Count})"));

        return Result.Success(meshResult.Value.WithMetadata(metadata));
    }

    private static float GetInterpolatedZ(IReadOnlyList<Vector3> path, double x, double y)
    {
        float minZ = path[0].Z;
        double minDistSq = double.MaxValue;

        for (int i = 0; i < path.Count - 1; i++)
        {
            var p1 = path[i];
            var p2 = path[i + 1];

            double l2 = Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2);
            if (l2 < 1e-8) continue;

            double t = Math.Max(0, Math.Min(1, ((x - p1.X) * (p2.X - p1.X) + (y - p1.Y) * (p2.Y - p1.Y)) / l2));
            double projX = p1.X + t * (p2.X - p1.X);
            double projY = p1.Y + t * (p2.Y - p1.Y);

            double distSq = Math.Pow(x - projX, 2) + Math.Pow(y - projY, 2);
            if (distSq < minDistSq)
            {
                minDistSq = distSq;
                minZ = p1.Z + (float)t * (p2.Z - p1.Z);
            }
        }
        
        if (minDistSq == double.MaxValue)
            return path[0].Z;
            
        return minZ;
    }

    private static Vector3 ComputeDirection(IReadOnlyList<Vector3> path, int index)
    {
        if (index == 0)
            return Vector3.Normalize(path[1] - path[0]);

        if (index == path.Count - 1)
            return Vector3.Normalize(path[index] - path[index - 1]);

        // Average of incoming and outgoing directions for smooth transitions
        var incoming = Vector3.Normalize(path[index] - path[index - 1]);
        var outgoing = Vector3.Normalize(path[index + 1] - path[index]);
        return Vector3.Normalize(incoming + outgoing);
    }

    private static (Vector3 u, Vector3 w) ComputeOrthogonalBasis(Vector3 direction)
    {
        Vector3 temp = Math.Abs(direction.X) < 0.9f
            ? new Vector3(1, 0, 0)
            : new Vector3(0, 1, 0);
        var u = Vector3.Normalize(Vector3.Cross(direction, temp));
        var w = Vector3.Cross(direction, u);
        return (u, w);
    }

    public Result<IReadOnlyList<Vector3>> ResampleOpenPath(IReadOnlyList<Vector3> path, float targetSpacing, int smoothingIterations = 2)
    {
        if (path is null || path.Count == 0)
            return GeometryErrors.InvalidPath;

        if (path.Count < 3 || targetSpacing <= 0)
            return Result<IReadOnlyList<Vector3>>.Success(path);

        var first = path[0];
        var last = path[^1];

        var pts = path.Select(p => new g3.Vector3d(p.X, p.Y, p.Z)).ToList();
        var curve = new g3.DCurve3(pts, false);

        var resampler = new g3.CurveResampler();
        var newPoints = resampler.SplitCollapseResample(curve, targetSpacing, targetSpacing / 4.0f);
        if (newPoints is not null && newPoints.Count >= 2)
            curve = new g3.DCurve3(newPoints, false);

        if (smoothingIterations > 0 && curve.VertexCount > 2)
        {
            var smoother = new g3.InPlaceIterativeCurveSmooth(curve, 0.15f);
            smoother.UpdateDeformation(smoothingIterations);
            curve = smoother.Curve;
        }

        var result = curve.Vertices.Select(v => new Vector3((float)v.x, (float)v.y, (float)v.z)).ToList();

        // The smoother moves every vertex; the stroke must still start and end exactly
        // where the user painted.
        result[0] = first;
        result[^1] = last;

        return Result<IReadOnlyList<Vector3>>.Success(result);
    }

    public Result<IMesh> BuildTextPrism(IReadOnlyList<Polygon2D> outlines, Fabolus.Core.Features.Decal.DecalFrame frame, float depth, float sink, float overshoot, float maxEdgeLength = 0f, IMesh? targetMesh = null) =>
        Text.TextMeshBuilder.BuildPrism(_engine, outlines, frame, depth, sink, overshoot, maxEdgeLength, targetMesh);

    public Result<IMesh> ProjectTextPrism(IMesh targetMesh, Fabolus.Core.Features.Decal.DecalFrame frame, IMesh prismMesh, List<string>? warnings = null) =>
        Text.TextMeshBuilder.ProjectPrism(_engine, targetMesh, frame, prismMesh, warnings);
}
