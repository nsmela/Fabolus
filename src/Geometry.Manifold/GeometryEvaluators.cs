using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using GeometryManifold.Internal;
using GeometryManifold.Internal.Native;
using System.Numerics;

namespace GeometryManifold;

/// <summary>
/// Mesh measurement and inspection. Manifold answers volume, area and connected components; the
/// rest (normals, topology tallies, ray and distance queries) it does not expose, so they are
/// computed here directly from the vertex and triangle arrays.
/// </summary>
internal sealed class GeometryEvaluators : IGeometryEvaluators
{
    /// <summary>A triangle with less area than this is degenerate, matching the MeshLib engine's threshold.</summary>
    private const double DegenerateTriangleArea = 1e-6;

    /// <summary>Components below this volume are debris from a boolean, not parts the user wants back.</summary>
    private const double MinComponentVolume = 0.1;

    private readonly GeometryEngine _engine;

    public GeometryEvaluators(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<IReadOnlyList<Vector3>> ComputeVertexNormals(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        var normals = AreaWeightedNormals(mesh.Vertices, mesh.Triangles);
        return Result.Success<IReadOnlyList<Vector3>>(normals);
    }

    public Result<TopologyValidation> ValidateTopology(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        // Weld first: an STL's per-triangle vertices make every edge look like a boundary, so
        // measuring topology on the raw arrays would call every imported mesh open.
        var (weldedVertices, weldedTriangles) = MeshExtensions.Weld(vertices, triangles);

        // Directed edge tally. An edge used once is a boundary edge; used more than twice, the
        // surface is non-manifold there.
        var edgeUse = new Dictionary<(int Low, int High), int>(weldedTriangles.Length);
        for (int i = 0; i + 2 < weldedTriangles.Length; i += 3)
        {
            CountEdge(edgeUse, weldedTriangles[i], weldedTriangles[i + 1]);
            CountEdge(edgeUse, weldedTriangles[i + 1], weldedTriangles[i + 2]);
            CountEdge(edgeUse, weldedTriangles[i + 2], weldedTriangles[i]);
        }

        int boundaryEdges = 0;
        int nonManifoldEdges = 0;
        foreach (var use in edgeUse.Values)
        {
            if (use == 1) boundaryEdges++;
            else if (use > 2) nonManifoldEdges++;
        }

        bool hasDegenerate = false;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            if (TriangleArea(vertices[triangles[i]], vertices[triangles[i + 1]], vertices[triangles[i + 2]]) < DegenerateTriangleArea)
            {
                hasDegenerate = true;
                break;
            }
        }

        var referenced = new bool[vertices.Length];
        foreach (int index in triangles) referenced[index] = true;
        bool hasOrphaned = referenced.Any(r => !r);

        int selfIntersections = CountSelfIntersections(weldedVertices, weldedTriangles);

        return new TopologyValidation
        {
            HasCorruptTopology = false,
            IsWatertight = boundaryEdges == 0,
            IsManifold = nonManifoldEdges == 0,
            HasOrphanedVertices = hasOrphaned,
            HasDegenerateTriangles = hasDegenerate,
            VertexCount = mesh.VertexCount,
            TriangleCount = mesh.TriangleCount,
            BoundaryEdgeCount = boundaryEdges,
            NonManifoldEdgeCount = nonManifoldEdges,
            SelfIntersectionCount = selfIntersections,
        };
    }

    public Result<MeshStatistics> GetStatistics(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var vertex in vertices)
        {
            min = Vector3.Min(min, vertex);
            max = Vector3.Max(max, vertex);
        }
        if (vertices.Length == 0)
        {
            min = Vector3.Zero;
            max = Vector3.Zero;
        }

        var (weldedVertices, weldedTriangles) = MeshExtensions.Weld(vertices, triangles);

        var edgeUse = new Dictionary<(int Low, int High), int>(weldedTriangles.Length);
        for (int i = 0; i + 2 < weldedTriangles.Length; i += 3)
        {
            CountEdge(edgeUse, weldedTriangles[i], weldedTriangles[i + 1]);
            CountEdge(edgeUse, weldedTriangles[i + 1], weldedTriangles[i + 2]);
            CountEdge(edgeUse, weldedTriangles[i + 2], weldedTriangles[i]);
        }

        int boundaryEdges = edgeUse.Values.Count(use => use == 1);

        double surfaceArea = 0;
        double signedVolume = 0;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]];
            var b = vertices[triangles[i + 1]];
            var c = vertices[triangles[i + 2]];
            surfaceArea += TriangleArea(a, b, c);
            signedVolume += Vector3.Dot(a, Vector3.Cross(b, c)) / 6.0;
        }

        // Volume is reported in millilitres, as the MeshLib engine did; only meaningful when
        // the surface actually encloses something.
        double volume = boundaryEdges == 0 ? Math.Abs(signedVolume) / 1000.0 : 0.0;

        return new MeshStatistics
        {
            VertexCount = mesh.VertexCount,
            TriangleCount = mesh.TriangleCount,
            EdgeCount = edgeUse.Count,
            BoundaryEdgeCount = boundaryEdges,
            Volume = volume,
            SurfaceArea = surfaceArea,
            MinX = min.X,
            MinY = min.Y,
            MinZ = min.Z,
            MaxX = max.X,
            MaxY = max.Y,
            MaxZ = max.Z,
        };
    }

    public Result<RenderData> GetRenderData(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        var positions = new double[vertices.Length * 3];
        for (int i = 0; i < vertices.Length; i++)
        {
            positions[i * 3] = vertices[i].X;
            positions[i * 3 + 1] = vertices[i].Y;
            positions[i * 3 + 2] = vertices[i].Z;
        }

        var vertexNormals = AreaWeightedNormals(vertices, triangles);
        var normals = new double[vertices.Length * 3];
        for (int i = 0; i < vertexNormals.Count; i++)
        {
            normals[i * 3] = vertexNormals[i].X;
            normals[i * 3 + 1] = vertexNormals[i].Y;
            normals[i * 3 + 2] = vertexNormals[i].Z;
        }

        return new RenderData
        {
            Vertices = positions,
            Triangles = (int[])triangles.Clone(),
            Normals = normals,
        };
    }

    public Result<double[]> CalculateDeviationColors(IMesh current, IMesh original, double maxDeviation = 0.4)
    {
        if (current is null || original is null) return MeshErrors.NullSource;

        var gradient = Fabolus.Core.Features.Overhangs.ColourGradient.SmoothingDeviation;

        double scale = Math.Max(maxDeviation, 0.001);
        var colors = new double[current.VertexCount * 3];

        // One call for every vertex where the shim is available; it answers the batch in
        // parallel. Falling back is a per-vertex managed walk, which is the same answer at a
        // fraction of the throughput.
        var distances = NativeDistanceField.IsAvailable
            ? NativeDistanceField.QueryAll(original, current.Vertices, NativeDistanceField.ChooseSignMode(original))
            : null;

        MeshBvh? bvh = distances is null ? new MeshBvh(original.Vertices, original.Triangles) : null;

        for (int i = 0; i < current.Vertices.Length; i++)
        {
            double distance = distances is not null
                ? distances[i]
                : bvh!.SignedDistance(current.Vertices[i]);

            // Map [-scale, scale] onto the gradient, so an unchanged surface lands mid-ramp.
            double t = Math.Clamp((distance + scale) / (2.0 * scale), 0.0, 1.0);
            var colour = gradient.Sample((float)t);

            colors[i * 3] = colour.R;
            colors[i * 3 + 1] = colour.G;
            colors[i * 3 + 2] = colour.B;
        }

        return colors;
    }

    public Result<bool> HasMultipleComponents(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        try
        {
            var (weldedVertices, weldedTriangles) = MeshExtensions.Weld(mesh.Vertices, mesh.Triangles);
            return ConnectedComponents(weldedVertices.Length, weldedTriangles).Count > 1;
        }
        catch (Exception ex)
        {
            return new Error("Geometry.EvaluatorFailed", ex.Message);
        }
    }

    public Result<IEnumerable<IMesh>> SeparateComponents(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;

        try
        {
            var (weldedVertices, weldedTriangles) = MeshExtensions.Weld(mesh.Vertices, mesh.Triangles);
            var components = ConnectedComponents(weldedVertices.Length, weldedTriangles);

            if (components.Count <= 1)
                return Result.Success<IEnumerable<IMesh>>(new[] { mesh });

            var results = new List<IMesh>(components.Count);
            for (int i = 0; i < components.Count; i++)
            {
                var componentTriangles = new int[components[i].Count * 3];
                for (int t = 0; t < components[i].Count; t++)
                {
                    int triangle = components[i][t];
                    componentTriangles[t * 3] = weldedTriangles[triangle * 3];
                    componentTriangles[t * 3 + 1] = weldedTriangles[triangle * 3 + 1];
                    componentTriangles[t * 3 + 2] = weldedTriangles[triangle * 3 + 2];
                }

                var (componentVertices, compacted) = MeshExtensions.Compact(weldedVertices, componentTriangles);

                // Same cubic-millimetre threshold the MeshLib engine used, before the
                // millilitre conversion GetStatistics applies.
                double signedVolume = 0;
                for (int t = 0; t + 2 < compacted.Length; t += 3)
                {
                    signedVolume += Vector3.Dot(
                        componentVertices[compacted[t]],
                        Vector3.Cross(componentVertices[compacted[t + 1]], componentVertices[compacted[t + 2]])) / 6.0;
                }
                if (Math.Abs(signedVolume) < MinComponentVolume) continue;

                var metadata = new MeshMetadata().WithProperties(m =>
                    m.Set(CoreKeys.Id, Guid.NewGuid())
                     .Set(CoreKeys.Name, $"{mesh.Metadata.Name} Component {i + 1}"));

                results.Add(new ManifoldMesh(componentVertices, compacted, metadata));
            }

            return Result.Success<IEnumerable<IMesh>>(results);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.EvaluatorFailed", ex.Message);
        }
    }

    public Result<RaycastHit> Raycast(IMesh mesh, Vector3 rayOrigin, Vector3 rayDirection)
    {
        if (mesh is null) return MeshErrors.NullSource;

        float length = rayDirection.Length();
        if (length < 1e-6f)
            return new Error("Raycast.InvalidDirection", "Ray direction must be a non-zero vector.");

        var direction = rayDirection / length;

        if (mesh.Vertices is null || mesh.Triangles is null || mesh.Vertices.Length == 0 || mesh.Triangles.Length == 0)
            return MeshErrors.RaycastMiss;

        var bvh = new MeshBvh(mesh.Vertices, mesh.Triangles);
        if (!bvh.Raycast(rayOrigin, direction, out float distance, out int triangle))
            return MeshErrors.RaycastMiss;

        var normal = bvh.TriangleNormal(triangle);
        if (normal == Vector3.Zero) normal = -direction;
        else if (Vector3.Dot(normal, direction) > 0f) normal = -normal;

        return Result.Success(new RaycastHit(rayOrigin + distance * direction, normal, distance));
    }

    private static void CountEdge(Dictionary<(int Low, int High), int> edges, int a, int b)
    {
        var key = a < b ? (a, b) : (b, a);
        edges[key] = edges.TryGetValue(key, out int count) ? count + 1 : 1;
    }

    private static double TriangleArea(Vector3 a, Vector3 b, Vector3 c) =>
        Vector3.Cross(b - a, c - a).Length() * 0.5;

    private static List<Vector3> AreaWeightedNormals(Vector3[] vertices, int[] triangles)
    {
        var accumulated = new Vector3[vertices.Length];
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            var a = vertices[triangles[i]];
            var b = vertices[triangles[i + 1]];
            var c = vertices[triangles[i + 2]];

            // The un-normalised cross product is twice the triangle's area, so summing it
            // weights each face by its size - the same thing MeshLib's per-vertex normals did.
            var faceNormal = Vector3.Cross(b - a, c - a);
            accumulated[triangles[i]] += faceNormal;
            accumulated[triangles[i + 1]] += faceNormal;
            accumulated[triangles[i + 2]] += faceNormal;
        }

        var normals = new List<Vector3>(vertices.Length);
        foreach (var normal in accumulated)
        {
            float length = normal.Length();
            normals.Add(length > 1e-12f ? normal / length : Vector3.Zero);
        }
        return normals;
    }

    /// <summary>Groups triangles into islands that share at least one vertex.</summary>
    private static List<List<int>> ConnectedComponents(int vertexCount, int[] triangles)
    {
        int triangleCount = triangles.Length / 3;
        var parent = new int[vertexCount];
        for (int i = 0; i < vertexCount; i++) parent[i] = i;

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }
            return x;
        }

        void Union(int a, int b)
        {
            int rootA = Find(a);
            int rootB = Find(b);
            if (rootA != rootB) parent[rootB] = rootA;
        }

        for (int i = 0; i < triangleCount; i++)
        {
            Union(triangles[i * 3], triangles[i * 3 + 1]);
            Union(triangles[i * 3 + 1], triangles[i * 3 + 2]);
        }

        var byRoot = new Dictionary<int, List<int>>();
        for (int i = 0; i < triangleCount; i++)
        {
            int root = Find(triangles[i * 3]);
            if (!byRoot.TryGetValue(root, out var list))
            {
                list = new List<int>();
                byRoot[root] = list;
            }
            list.Add(i);
        }

        return byRoot.Values.ToList();
    }

    /// <summary>
    /// Counts triangles that pass through another triangle of the same mesh, standing in for
    /// MeshLib's SelfIntersections.getFaces. Bounding-box overlap on a BVH does the broad phase,
    /// so this stays usable on the tens-of-thousands-of-triangle meshes the app validates on
    /// import rather than being the n-squared test the definition implies.
    /// </summary>
    private static int CountSelfIntersections(Vector3[] vertices, int[] triangles)
    {
        int triangleCount = triangles.Length / 3;
        if (triangleCount == 0) return 0;

        var bvh = new MeshBvh(vertices, triangles);
        var intersecting = new bool[triangleCount];

        for (int i = 0; i < triangleCount; i++)
        {
            int i0 = triangles[i * 3];
            int i1 = triangles[i * 3 + 1];
            int i2 = triangles[i * 3 + 2];

            var a0 = vertices[i0];
            var a1 = vertices[i1];
            var a2 = vertices[i2];

            var min = Vector3.Min(a0, Vector3.Min(a1, a2));
            var max = Vector3.Max(a0, Vector3.Max(a1, a2));

            int self = i;
            bvh.QueryBox(min, max, candidate =>
            {
                // Each unordered pair only needs testing once, and a pair sharing any vertex is
                // adjacent geometry rather than a crossing.
                if (candidate <= self) return;
                if (intersecting[self] && intersecting[candidate]) return;

                int j0 = triangles[candidate * 3];
                int j1 = triangles[candidate * 3 + 1];
                int j2 = triangles[candidate * 3 + 2];

                if (i0 == j0 || i0 == j1 || i0 == j2 ||
                    i1 == j0 || i1 == j1 || i1 == j2 ||
                    i2 == j0 || i2 == j1 || i2 == j2) return;

                if (TriangleIntersection.Intersects(a0, a1, a2, vertices[j0], vertices[j1], vertices[j2]))
                {
                    intersecting[self] = true;
                    intersecting[candidate] = true;
                }
            });
        }

        return intersecting.Count(flag => flag);
    }
}
