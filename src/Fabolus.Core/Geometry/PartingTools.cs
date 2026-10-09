using Clipper2Lib;
using BasicResults;
using GeometryEngine.Core.Geometry.Primitives;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using GeometryEngine.Core.Geometry;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Parting-line detection (isoline marching against the pull direction) and split-tool solid
/// generation, built on GeometryEngine's primitives: its vertex normals for the isoline, the mesh's
/// own spatial index for projection and signed distance, its triangulator for the flange footprint
/// and its signed-distance remesher for offsets.
/// </summary>
public sealed class PartingTools : IPartingTools
{
    private readonly IGeometryEngine _engine;

    /// <summary>
    /// Overlap, in mm, by which the wavefront flange runs inward past the parting line. Cutting the
    /// band exactly on that contour leaves the flange flush with it, where a single face rounding the
    /// wrong way opens a seam; the bleed makes it overlap instead. Kept small so the inward offset
    /// cannot reach through a thin neck of the parting line and cross to its far side.
    /// </summary>
    private const double BleedMm = 2.5;

    /// <summary>
    /// How far, in mm, the flange's inner rim is driven inside the body when a seal mesh is supplied
    /// (see <see cref="SealInnerRimAgainstBody"/>). It only has to beat the accuracy of the footprint
    /// arithmetic that placed the rim - the measured worst-case leak is under 1mm on both chin.3mf and
    /// scalp.3mf - while staying small enough that the rim doesn't intrude visibly into the cavity.
    /// </summary>
    private const double SealMarginMm = 0.5;

    /// <summary>
    /// Height-relaxation strength applied to the wavefront flange's innermost free ring. Kept low so
    /// the band next to the parting line still carries the anatomy's undulation - raising it pulls the
    /// flange away from the silhouette it is supposed to hug.
    /// </summary>
    private const double InnerSmoothingFactor = 0.25;

    /// <summary>
    /// Height-relaxation strength applied at the flange's outer rim, interpolated up to from
    /// <see cref="InnerSmoothingFactor"/> across the intervening rings. High enough that the far field
    /// reaches its crease-free harmonic limit inside the iteration budget. Must stay below 1 for the
    /// Jacobi iteration to remain stable.
    /// </summary>
    private const double OuterSmoothingFactor = 0.9;

    /// <summary>
    /// Target surface slope (degrees from horizontal) the overhang relaxation caps the flange at. Set a
    /// few degrees under the 45-degree FDM support-free limit on purpose: capping exactly at 45 leaves a
    /// broad band of faces hovering just above it (they converge *to* the target), whereas targeting 40
    /// pushes the whole flange body under the real limit with margin - on chin.3mf that cut faces over
    /// 45 degrees from ~500 to ~30 (the remainder are irreducible, hard against the fixed parting edge
    /// where it plunges). <see cref="RelaxSteepSlopesWorld"/> eases faces past this back down toward it.
    /// </summary>
    private const double MaxFlangeSlopeDeg = 40.0;

    /// <summary>
    /// How firmly the flange's inner (parting) edge is held to the true parting-line height during
    /// overhang relaxation, in [0, 1]. 1 holds the seal exactly but leaves steep faces wherever the
    /// parting line itself plunges; lower lets the seal edge ease along the pull axis to shed those
    /// faces. See <see cref="RelaxSteepSlopesWorld"/>.
    /// </summary>
    private const double FlangeInnerSealHold = 0.5;

    public PartingTools(IGeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<PartingLine> GeneratePartingLine(
        IMesh mesh, Vector3 pullDirection, double noiseThreshold = 0.1, PartingNeutralBand neutralBand = default)
    {
        if (mesh is null)
            return MeshErrors.NullSource;
        if (mesh.IsEmpty)
            return MeshErrors.InvalidMesh;
        if (pullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        var direction = pullDirection.Normalize();

        var pts = mesh.Vertices;
        var normalsResult = _engine.Evaluators.ComputeVertexNormals(mesh);
        if (normalsResult.IsFailure) return normalsResult.Error;
        var normals = normalsResult.Value;

        // Scalar field: how aligned each vertex's normal is with the pull direction.
        // Zero crossings of this field, walked triangle by triangle, are the silhouette.
        var scalars = new double[pts.Length];
        for (int i = 0; i < pts.Length; i++)
            scalars[i] = normals[i].Dot(direction);

        // Trace at the neutral band's midpoint rather than at exactly perpendicular, so an asymmetric
        // band biases the parting line toward the side the user opened up. A default (zero-width) band
        // gives a midpoint of 0, i.e. the plain silhouette.
        double iso = neutralBand.Midpoint;

        var graph = new IsolineGraph();

        var triangles = mesh.Triangles;

        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            int a = triangles[t];
            int b = triangles[t + 1];
            int c = triangles[t + 2];

            // Offset by the band midpoint so a single test drives both the gate and the interpolation
            // below. Previously the gate compared against the threshold while the crossings were
            // interpolated at zero, so the two disagreed and the threshold only ever suppressed
            // triangles rather than moving the contour.
            double s0 = scalars[a] - iso;
            double s1 = scalars[b] - iso;
            double s2 = scalars[c] - iso;

            bool hasPos = s0 > 0 || s1 > 0 || s2 > 0;
            bool hasNeg = s0 < 0 || s1 < 0 || s2 < 0;
            if (!(hasPos && hasNeg))
                continue;

            var pa = pts[a];
            var pb = pts[b];
            var pc = pts[c];

            var crossings = new List<Vector3>(3);
            if (Math.Sign(s0) != Math.Sign(s1))
                crossings.Add(Interpolate(pa, pb, s0, s1));
            if (Math.Sign(s1) != Math.Sign(s2))
                crossings.Add(Interpolate(pb, pc, s1, s2));
            if (Math.Sign(s2) != Math.Sign(s0))
                crossings.Add(Interpolate(pc, pa, s2, s0));

            var uniqueCrossings = new List<Vector3>(3);
            foreach (var cr in crossings)
            {
                if (!uniqueCrossings.Any(u => u.DistanceSquared(cr) < 1e-6))
                    uniqueCrossings.Add(cr);
            }

            if (uniqueCrossings.Count == 2)
                graph.AddSegment(uniqueCrossings[0], uniqueCrossings[1]);
        }

        var loops = graph.ExtractLoops();

        double maxDim = 1.0;
        var statsResult = _engine.Evaluators.GetStatistics(mesh);
        if (statsResult.IsSuccess)
        {
            var size = statsResult.Value.BoundsSize;
            maxDim = Math.Max(size.X, Math.Max(size.Y, size.Z));
        }

        double threshold = maxDim * noiseThreshold;
        var validLoops = loops.Where(l => LoopLength(l) > threshold).ToList();

        if (validLoops.Count == 0)
            return MeshErrors.NoPartingLineDetected;

        return Result.Success(new PartingLine(validLoops));
    }

    public Result<PartingLine> SmoothPartingLineOnSurface(
        IMesh surface, PartingLine line, Vector3 pullDirection, PartingLineSmoothingOptions options)
    {
        if (surface is null)
            return MeshErrors.NullSource;
        if (surface.IsEmpty)
            return MeshErrors.InvalidMesh;
        if (line is null || !line.IsValid)
            return MeshErrors.InvalidPartingLine;
        if (pullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        var direction = pullDirection.Normalize();

        // The surface's own index for the whole run: the smoother calls back once per point per
        // iteration (order 10k times on a head-sized bolus), and rebuilding it per call would dwarf
        // the smoothing itself.
        var indexResult = _engine.Spatial.IndexFor(surface);
        if (indexResult.IsFailure) return indexResult.Error;
        var index = indexResult.Value;

        // The band deliberately does NOT gate the smoothing, only where the isoline is traced.
        //
        // Confining each move to draft-neutral surface sounds right and measures terribly. The isoline
        // is a zero-crossing of the *interpolated per-vertex* normal field, whereas a projected point
        // can only be tested against the *face* normal it landed on, and on a body this coarse
        // (3216 triangles on chin.3mf) those two disagree badly: the traced loop already sits on faces
        // whose normals are a median 35 degrees off perpendicular, so 162 of its 174 points read as
        // outside even a generous band before any smoothing happens. Gating on that rejects ~93% of
        // moves, which freezes points at the band edge while their neighbours keep moving and leaves
        // the footprint with 130-164 degree reversals - worse than doing nothing, and rough enough to
        // send the flange builder into a spin.
        //
        // A real constraint would have to barycentrically interpolate the same per-vertex field the
        // isoline came from, via the projection's MeshTriPoint. Worth doing only if smoothing is ever
        // shown to walk the loop somewhere invalid; measured, it does not - the footprint area stays
        // within 0.2% of the raw isoline's.
        Vector3 SnapToSurface(Vector3 candidate)
        {
            var closest = index.ClosestPoint(candidate);
            return closest.HasValue ? closest.Value.Point : candidate;
        }

        return Result.Success(
            PartingLineSmoother.Smooth(line, options, direction, SnapToSurface));
    }

    /// <summary>
    /// Radius the surface normal is averaged over, as a fraction of the body's bounding diagonal.
    /// Wide enough to reach across the extrusion rim - which is the crease the sampled points sit on
    /// and the whole reason a neighbourhood is needed - and narrow enough that the result still
    /// follows the body rather than reporting its overall facing. On a scalp-sized body this is a few
    /// millimetres. Measured on the traced line, the largest turn between neighbouring points falls from 119 degrees taking the face underneath, to 55 at 0.03, to under 45 here.
    /// </summary>
    private const double NormalNeighbourhoodFraction = 0.05;

    /// <summary>
    /// Not yet available on GeometryEngine, which has no way to order the curves two meshes cross
    /// along. It only ever explained a split that had already failed, so the caller reports the
    /// failure without the detail.
    /// </summary>
    public Result<CutContourReport> InspectCutContours(IMesh mould, IMesh cutter, Vector3 shiftCutter = default) =>
        MeshErrors.NotImplemented;

    public Result<IReadOnlyList<Vector3>> SampleSurfaceNormals(IMesh mesh, IReadOnlyList<Vector3> points)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (mesh.IsEmpty) return MeshErrors.InvalidMesh;
        if (points is null) return MeshErrors.InvalidPolygon;

        try
        {
            return SmoothNormalsAt(mesh, points);
        }
        catch (Exception ex)
        {
            return new Error("Geometry.SampleSurfaceNormalsFailed", ex.Message);
        }
    }

    /// <summary>
    /// The smooth surface normal of <paramref name="mesh"/> at each of <paramref name="points"/>:
    /// area-weighted per-vertex normals, averaged over a neighbourhood around the point.
    ///
    /// <para>
    /// Shared, and that sharing is the point. This used to exist only behind
    /// <see cref="SampleSurfaceNormals"/>, which is what the view draws its normal arrows from, while
    /// the flange builders launched along <see cref="OutwardNormalsAlong"/> - the raw normal of
    /// whichever single face the point happened to project onto. The parting line runs along the
    /// extrusion's rim, which is a crease, so those two answers differ by as much as a right angle:
    /// measured against the arrows, the per-face normal is a mean of 30 degrees off on chin and 38 on
    /// larynx, worst case 95, with 40% of larynx's line more than 45 degrees out. The flange therefore
    /// left the line at ninety degrees to the direction the user had just been shown. One sampler
    /// means the arrows are a promise about where the flange goes.
    /// </para>
    /// </summary>
    private Result<IReadOnlyList<Vector3>> SmoothNormalsAt(IMesh mesh, IReadOnlyList<Vector3> points)
    {
        {
            var verts = mesh.Vertices;
            var tris = mesh.Triangles;

            // Per-vertex normals first: each vertex takes the sum of its incident face normals, left
            // unnormalized so the sum is area-weighted (a cross product's length is twice the face
            // area). That is the standard smooth normal, and it is what "average the neighbouring
            // vertices" resolves to once the point being asked about sits inside a face.
            var vertexNormals = new Vector3[verts.Length];
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                var a = verts[tris[t]];
                var b = verts[tris[t + 1]];
                var c = verts[tris[t + 2]];

                var faceNormal = (b - a).Cross(c - a);
                vertexNormals[tris[t]] += faceNormal;
                vertexNormals[tris[t + 1]] += faceNormal;
                vertexNormals[tris[t + 2]] += faceNormal;
            }

            // Radius the neighbourhood is gathered over, relative to the body so it means the same
            // thing on a nose as on a scalp.
            var min = new Vector3(double.MaxValue, double.MaxValue, double.MaxValue);
            var max = new Vector3(double.MinValue, double.MinValue, double.MinValue);
            foreach (var v in verts) { min = min.ComponentMin(v); max = max.ComponentMax(v); }
            double radius = (max - min).Length * NormalNeighbourhoodFraction;
            double radiusSq = radius * radius;

            var indexResult = _engine.Spatial.IndexFor(mesh);
            if (indexResult.IsFailure) return indexResult.Error;
            var index = indexResult.Value;

            var sampled = new Vector3[points.Count];

            for (int i = 0; i < points.Count; i++)
            {
                // Every vertex within the radius, not just the face underneath. The parting line runs
                // along the extrusion's rim, which is a crease: the wall meets the two shell surfaces
                // at close to a right angle there, so a point sampled from the one face it happens to
                // land on flips as it crosses the crease - measured on the traced line, consecutive
                // points came back up to 119 degrees apart. Averaging the neighbourhood reads the
                // shape around the rim instead of whichever facet is nearest.
                var summed = Vector3.Zero;
                for (int v = 0; v < verts.Length; v++)
                {
                    if (verts[v].DistanceSquared(points[i]) > radiusSq) continue;
                    summed += vertexNormals[v];
                }

                // Nothing in range - a body coarse enough that the radius spans no vertex at all.
                // Fall back to the face beneath the point, which is always there.
                if (summed.LengthSquared < 1e-18)
                {
                    var projection = index.ClosestPoint(points[i]);
                    int face = projection.HasValue ? projection.Value.Triangle : -1;
                    if (face < 0 || (face * 3) + 2 >= tris.Length) continue;

                    summed = vertexNormals[tris[face * 3]]
                           + vertexNormals[tris[(face * 3) + 1]]
                           + vertexNormals[tris[(face * 3) + 2]];
                }

                if (summed.LengthSquared > 1e-18) sampled[i] = summed.Normalize();
            }

            return sampled;
        }
    }

    public Result<ISurfaceProjector> CreateSurfaceProjector(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (mesh.IsEmpty) return MeshErrors.InvalidMesh;

        try
        {
            var index = _engine.Spatial.IndexFor(mesh);
            if (index.IsFailure) return index.Error;

            return Result.Success<ISurfaceProjector>(new SurfaceProjector(index.Value));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.SurfaceProjectorFailed", ex.Message);
        }
    }

    /// <summary>
    /// Projects onto the surface through the mesh's own index, which the engine builds once and keeps
    /// with the mesh - so a run of projections pays for it once rather than once per point, and shares
    /// it with anything else that queries the same body.
    /// </summary>
    private sealed class SurfaceProjector : ISurfaceProjector
    {
        private readonly ISpatialIndex _index;

        public SurfaceProjector(ISpatialIndex index) => _index = index;

        public Vector3 Project(Vector3 point)
        {
            var closest = _index.ClosestPoint(point);
            return closest.HasValue ? closest.Value.Point : point;
        }

        /// <summary>Nothing to release: the index is the mesh's, and lives as long as it does.</summary>
        public void Dispose() { }
    }

    public Result<ISurfaceGeodesic> CreateSurfaceGeodesic(IMesh mesh)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (mesh.IsEmpty) return MeshErrors.InvalidMesh;

        return Result.Success<ISurfaceGeodesic>(new SurfaceGeodesic(_engine, mesh));
    }

    /// <summary>
    /// GeometryEngine's shortest path across the surface. The face graph it walks and the index it
    /// finds the two ends with are both kept with the mesh by the engine, so this holds nothing of its
    /// own and has nothing to release.
    /// </summary>
    private sealed class SurfaceGeodesic(IGeometryEngine engine, IMesh mesh) : ISurfaceGeodesic
    {
        public IReadOnlyList<Vector3>? Path(Vector3 from, Vector3 to)
        {
            var path = engine.Spatial.ShortestPath(mesh, from, to);
            return path.IsSuccess ? path.Value : null;
        }

        public void Dispose() { }
    }

    public Result<IReadOnlyList<Vector3>> GenerateInnerConcaveContour(IMesh referenceMesh, PartingLine partingLine, double offset = 0)
    {
        if (referenceMesh is null)
            return MeshErrors.NullSource;
        if (!partingLine.IsValid)
            return MeshErrors.InvalidPartingLine;

        var points = new List<Vector3>();
        foreach (var point in partingLine.Loops[0])
        {
            points.Add(new Vector3(point.X, 0, point.Z));
        }

        return Result<IReadOnlyList<Vector3>>.Success(points);
    }

    public Result<IReadOnlyList<Vector3>> GenerateOuterBoxContour(
        IMesh referenceMesh, Vector3 pullDirection, double offset = 10.0)
    {
        if (referenceMesh is null)
            return MeshErrors.NullSource;
        if (referenceMesh.IsEmpty)
            return MeshErrors.InvalidMesh;
        if (pullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        // Measured from the mesh's own vertices rather than read from Metadata.MeshStats: those are
        // only written on import/repair/cut, so a generated mould either carries none (reported,
        // misleadingly, as corrupt topology) or carries the bounds of the body it was derived from.
        //
        // Bounds are taken in the footprint plane, not in world axes. A world-axis box is only the
        // tight enclosure of the mesh's shadow when the pull direction is a world axis; off-axis it
        // both over-hangs on some sides and, worse, isn't the plane the flange is triangulated in.
        var direction = pullDirection.Normalize();
        var (u, v) = PartingFrame.Basis(direction);

        double minU = double.MaxValue, maxU = double.MinValue;
        double minV = double.MaxValue, maxV = double.MinValue;
        foreach (var p in referenceMesh.Vertices)
        {
            double pu = p.Dot(u);
            double pv = p.Dot(v);
            if (pu < minU) minU = pu;
            if (pu > maxU) maxU = pu;
            if (pv < minV) minV = pv;
            if (pv > maxV) maxV = pv;
        }

        if (minU > maxU || minV > maxV)
            return MeshErrors.InvalidMesh;

        minU -= offset; maxU += offset;
        minV -= offset; maxV += offset;

        return Result<IReadOnlyList<Vector3>>.Success([
            PartingFrame.ToWorld(new Vector2(minU, minV), direction),
            PartingFrame.ToWorld(new Vector2(minU, maxV), direction),
            PartingFrame.ToWorld(new Vector2(maxU, maxV), direction),
            PartingFrame.ToWorld(new Vector2(maxU, minV), direction),
        ]);
    }

    // --- Helpers ---

    private static Vector3 Interpolate(Vector3 a, Vector3 b, double sa, double sb)
    {
        double t = Math.Abs(sa) / (Math.Abs(sa) + Math.Abs(sb));
        return a.LerpTo(b, t);
    }

    private static double LoopLength(IReadOnlyList<Vector3> loop)
    {
        double len = 0;
        for (int i = 0; i < loop.Count; i++)
            len += loop[i].DistanceTo(loop[(i + 1) % loop.Count]);
        return len;
    }

    // --- Contour winding ---
    //
    // Loops arriving from the isoline walk or from Clipper have whatever direction they happened to
    // be built with, so anything that cares which way a loop runs normalises it first.

    /// <summary>Shoelace signed area; positive when <paramref name="loop"/> winds counter-clockwise.</summary>
    private static double SignedArea2D(IReadOnlyList<Vector2> loop)
    {
        double area = 0;
        for (int i = 0; i < loop.Count; i++)
        {
            var p0 = loop[i];
            var p1 = loop[(i + 1) % loop.Count];
            area += (p0.X * p1.Y) - (p1.X * p0.Y);
        }
        return area / 2.0;
    }

    /// <summary>Returns <paramref name="loop"/> wound counter-clockwise if <paramref name="ccw"/>, else clockwise.</summary>
    private static IReadOnlyList<Vector2> AsWinding(IReadOnlyList<Vector2> loop, bool ccw)
    {
        bool isCcw = SignedArea2D(loop) > 0;
        if (isCcw == ccw)
            return loop;

        var reversed = loop.ToArray();
        Array.Reverse(reversed);
        return reversed;
    }

    /// <summary>Ray-cast point-in-polygon. Winding-independent (uses a crossing count).</summary>
    private static bool ContainsPoint(IReadOnlyList<Vector2> poly, Vector2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            var a = poly[i];
            var b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) &&
                p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    /// <summary>
    /// Rotation that maps world +Z onto <paramref name="target"/>. Delegates to
    /// <see cref="PartingFrame"/> rather than keeping a local copy: the outer contour is flattened
    /// through that same frame, and a second implementation here is exactly how the two came to
    /// disagree about which plane the flange lives in.
    /// </summary>
    private static Rotation RotationFromZTo(Vector3 target) => PartingFrame.RotationFromZTo(target);

    /// <summary>
    /// Triangulates everything inside <paramref name="outermost"/>, with every ring in
    /// <paramref name="inner"/> carried as a constrained edge rather than cut out.
    ///
    /// <para>
    /// The engine's triangulator reads nesting even-odd - a ring inside one other is a hole, inside
    /// two an island - so with the rings nested one inside the next, a single call fills only every
    /// other band. Leaving the outermost ring out of a second call moves every inner ring one level
    /// shallower, and fills exactly the bands the first left empty. Between them the whole footprint
    /// is covered once, and since the triangulator adds no points of its own, a point both calls share
    /// sits on one of the rings and is merged by position.
    /// </para>
    /// </summary>
    private Result<PlanarTriangulation> TriangulateNested(
        IReadOnlyList<IReadOnlyList<Vector2>> outermost, IReadOnlyList<IReadOnlyList<Vector2>> inner)
    {
        static PlanarPolygon Ring(IReadOnlyList<Vector2> ring) => PlanarPolygon.FromOuter([.. ring]);

        var all = outermost.Concat(inner).Where(r => r.Count >= 3).Select(Ring).ToImmutableArray();
        var shifted = inner.Where(r => r.Count >= 3).Select(Ring).ToImmutableArray();

        var points = new List<Vector2>();
        var indexOf = new Dictionary<Vector2, int>();
        var triangles = new List<int>();

        foreach (var rings in new[] { all, shifted })
        {
            if (rings.IsEmpty) continue;

            var part = _engine.Polygons.Triangulate(rings);
            if (part.IsFailure)
            {
                // The second call has nothing to fill when there are no inner bands.
                if (rings == shifted) continue;
                return part.Error;
            }

            var local = new int[part.Value.Points.Length];
            for (int i = 0; i < local.Length; i++)
            {
                var p = part.Value.Points[i];
                if (!indexOf.TryGetValue(p, out int at))
                {
                    at = points.Count;
                    points.Add(p);
                    indexOf[p] = at;
                }
                local[i] = at;
            }

            foreach (int corner in part.Value.Triangles) triangles.Add(local[corner]);
        }

        if (triangles.Count == 0) return MeshErrors.InvalidPolygon;
        return Result.Success(new PlanarTriangulation([.. points], [.. triangles]));
    }

    /// <summary>
    /// A mesh from flat coordinates - x, y, z per vertex - which is how every builder here lays its
    /// vertices out. Named for what made it; callers that want the body's metadata carried across set
    /// it on the result.
    /// </summary>
    private Result<IMesh> CreateMesh(ReadOnlySpan<double> coordinates, ReadOnlySpan<int> triangles)
    {
        var vertices = ImmutableArray.CreateBuilder<Vector3>(coordinates.Length / 3);
        for (int i = 0; i + 2 < coordinates.Length; i += 3)
            vertices.Add(new Vector3(coordinates[i], coordinates[i + 1], coordinates[i + 2]));

        return _engine.CreateMesh(vertices.MoveToImmutable(), [.. triangles], MeshMetadata.Named("parting flange"));
    }

    /// <summary>
    /// Stitches isoline segments (pairs of 3D points, one per triangle zero-crossing) into
    /// closed loops via a simple adjacency-graph walk. Direct C# port of the marching-triangles
    /// isoline extraction used elsewhere for parting-line generation.
    /// </summary>
    private sealed class IsolineGraph
    {
        private const double ToleranceSq = 0.001 * 0.001;
        private readonly List<Vector3> _nodes = new();
        private readonly Dictionary<int, List<int>> _adjacency = new();

        public void AddSegment(Vector3 p0, Vector3 p1)
        {
            if (p0.DistanceSquared(p1) < ToleranceSq)
                return;

            int id0 = GetOrAddNode(p0);
            int id1 = GetOrAddNode(p1);
            AddEdge(id0, id1);
            AddEdge(id1, id0);
        }

        private readonly Dictionary<(int x, int y, int z), int> _spatialHash = new();
        private const double QuantizeScale = 1000.0; // 1mm / 1000 = 0.001mm resolution

        private int GetOrAddNode(Vector3 p)
        {
            var key = ((int)(p.X * QuantizeScale), (int)(p.Y * QuantizeScale), (int)(p.Z * QuantizeScale));

            if (_spatialHash.TryGetValue(key, out int existingId))
                return existingId;

            int newId = _nodes.Count;
            _nodes.Add(p);
            _adjacency[newId] = new List<int>(2); // Isoline nodes typically have degree 2
            _spatialHash[key] = newId;
            return newId;
        }

        private void AddEdge(int from, int to)
        {
            if (!_adjacency[from].Contains(to))
                _adjacency[from].Add(to);
        }

        public List<List<Vector3>> ExtractLoops()
        {
            var loops = new List<List<Vector3>>();
            var visited = new HashSet<int>();

            foreach (var startNode in _adjacency.Keys)
            {
                if (visited.Contains(startNode))
                    continue;

                var loop = new List<Vector3>();
                int curr = startNode;
                int prev = -1;
                bool closed = false;

                while (true)
                {
                    visited.Add(curr);
                    loop.Add(_nodes[curr]);

                    int next = -1;
                    foreach (var n in _adjacency[curr])
                    {
                        if (n == prev)
                            continue;
                        if (n == startNode && loop.Count > 2)
                        { closed = true; break; }
                        if (!visited.Contains(n))
                        { next = n; break; }
                    }

                    if (closed || next == -1)
                        break;

                    prev = curr;
                    curr = next;
                }

                if (closed)
                    loops.Add(loop);
            }

            return loops;
        }
    }


    /// <summary>
    /// Precision scaling factor for Clipper2's 64-bit integer grid. 
    /// 10,000.0 provides 0.1-micron resolution, preventing quantization drift on medical molds.
    /// </summary>
    private const double ClipperScale = 10000.0;

    /// <summary>
    /// Chord tolerance, in mm, for the arcs Clipper lays down at a round join - how far the polyline
    /// it emits may sit from the true arc.
    ///
    /// <para>
    /// Worth stating rather than leaving to Clipper, whose default is derived from the coordinate
    /// magnitude: on <see cref="ClipperScale"/>'s 0.1-micron grid that lands near a ten-thousandth of
    /// a millimetre, and a single round join then emits an arc in the thousands of points. This is a
    /// cost control, not a correctness one - the per-ring resample in
    /// <see cref="GenerateIterativeRibbons"/> is what bounds the ring that comes out either way - but
    /// it keeps the offsetter from building an enormous contour just to have it resampled back down.
    /// </para>
    ///
    /// <para>
    /// 0.2mm is well under the finest ring spacing any parting line produces, so nothing downstream
    /// can express the difference in any case.
    /// </para>
    /// </summary>
    private const double OffsetArcToleranceMm = 0.2;

    // --- Private Helper Methods ---

    private static double GetDistanceToPolygon(Vector2 p, IReadOnlyList<Vector2> poly)
    {
        double minD = double.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            minD = Math.Min(minD, DistancePointToSegment(p, a, b));
        }
        return minD;
    }

    private static double DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var l2 = a.DistanceSquared(b);
        if (l2 == 0)
            return p.DistanceTo(a);
        var t = Math.Clamp((p - a).Dot(b - a) / l2, 0.0, 1.0);
        var projection = a + t * (b - a);
        return p.DistanceTo(projection);
    }

    /// <summary>
    /// Offsets a 2D polygon outward and strictly constrains (clips) the result against an outer bounding 
    /// frame using Clipper2 Boolean intersection. Prevents expansion ribbons from overshooting the tooling box.
    /// </summary>
    private static Result<IReadOnlyList<Vector2>> GenerateConstrainedOffset(
        IReadOnlyList<Vector2> contour,
        double offsetMm,
        IReadOnlyList<Vector2>? constrainingBoundary = null)
    {
        if (contour is null || contour.Count < 3)
            return new Error("Geometry.InvalidPolygon", "Input contour must contain at least 3 vertices.");

        // 1. Map input contour to precision integer grid
        var inputPath = new Path64(contour.Count);
        for (int i = 0; i < contour.Count; i++)
        {
            inputPath.Add(new Point64(
                Math.Round(contour[i].X * ClipperScale),
                Math.Round(contour[i].Y * ClipperScale)));
        }

        var inputPaths = new Paths64 { inputPath };
        double scaledDelta = offsetMm * ClipperScale;

        // 2. Perform Inflation (Offsetting). The arc tolerance is passed explicitly - see
        // OffsetArcToleranceMm for why leaving it to Clipper's default is so expensive here.
        var inflated = Clipper.InflatePaths(
            inputPaths, scaledDelta, JoinType.Round, EndType.Polygon,
            miterLimit: 2.0, arcTolerance: OffsetArcToleranceMm * ClipperScale);
        if (inflated.Count == 0)
            return new Error("Geometry.OffsetFailed", "Clipper2 offset collapsed or failed to generate geometry.");

        Paths64 finalPaths = inflated;

        // 3. Apply Boundary Constraint via Boolean Intersection
        if (constrainingBoundary is not null && constrainingBoundary.Count >= 3)
        {
            var boundaryPath = new Path64(constrainingBoundary.Count);
            for (int i = 0; i < constrainingBoundary.Count; i++)
            {
                boundaryPath.Add(new Point64(
                    Math.Round(constrainingBoundary[i].X * ClipperScale),
                    Math.Round(constrainingBoundary[i].Y * ClipperScale)));
            }

            var constraintPaths = new Paths64 { boundaryPath };

            // Strictly intersect the offset against the frame! Anything outside is sliced off.
            finalPaths = Clipper.Intersect(inflated, constraintPaths, FillRule.NonZero);

            if (finalPaths.Count == 0)
                return new Error("Geometry.ConstraintCollapsed", "The offset contour was entirely outside the constraining boundary.");
        }

        // 4. Select the dominant exterior loop if the intersection fragmented into islands
        var dominantPath = finalPaths.OrderByDescending(p => Math.Abs(Clipper.Area(p))).First();

        var result = new Vector2[dominantPath.Count];
        for (int i = 0; i < dominantPath.Count; i++)
        {
            result[i] = new Vector2(
                (double)(dominantPath[i].X / ClipperScale),
                (double)(dominantPath[i].Y / ClipperScale));
        }

        return Result.Success<IReadOnlyList<Vector2>>(result);
    }

    /// <summary>
    /// Generates a parting flange using iterative wavefront offsetting and inside-out height relaxation.
    /// Each ring is offset a fixed distance outward from the previous ring - unconstrained - until a ring
    /// has expanded entirely past the boundary; that ring becomes the flange's outer edge. The result is
    /// triangulated in 2D and lifted into 3D, pinning only the inner anatomy and Laplacian-smoothing the
    /// pull-axis height of everything else. <paramref name="maxRibbonRings"/> is only a safety cap.
    /// Inside the anatomy's concave pockets the flange keeps only a band <paramref name="concaveBandWidthMm"/>
    /// wide hugging the parting line, leaving deeper notches open rather than webbing across them.
    /// A notch only opens if the band is narrower than half the notch width, so this must be kept small;
    /// set it to 0 to drop concave-pocket fill entirely (fully open notches), or negative to disable.
    /// </summary>
    public Result<IMesh> GenerateWavefrontFlangeMesh(
        IReadOnlyList<Vector3> inner3DLoop,
        IReadOnlyList<Vector2> outerPlanarBox,
        Vector3 planeNormal,
        double stepDistanceMm = 3.0,
        int maxRibbonRings = 200,
        double concaveBandWidthMm = 3.0,
        double overhangTargetSlopeDeg = MaxFlangeSlopeDeg,
        double innerSealHold = FlangeInnerSealHold,
        double innerBleedMm = BleedMm,
        IMesh? sealAgainst = null,
        double sealMarginMm = SealMarginMm,
        IMesh? launchSurface = null,
        double launchHoldMm = LaunchHoldMm,
        bool rawFlange = false,
        int launchSmoothingPasses = 0)
    {
        if (inner3DLoop is null || inner3DLoop.Count < 3)
            return MeshErrors.InvalidPolygon;
        if (outerPlanarBox is null || outerPlanarBox.Count < 3)
            return MeshErrors.InvalidPolygon;
        if (planeNormal == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        // 1. Establish Local Coordinate Frame (Map plane normal -> Local +Z, which represents World Y)
        var direction = planeNormal.Normalize();
        var rotation = RotationFromZTo(direction);
        var inverseRotation = rotation.Inverse();

        var local3D = new Vector3[inner3DLoop.Count];
        var local2D = new Vector2[inner3DLoop.Count];
        for (int i = 0; i < inner3DLoop.Count; i++)
        {
            var transformed = inverseRotation.Apply(inner3DLoop[i]);
            local3D[i] = transformed;
            local2D[i] = new Vector2(transformed.X, transformed.Y);
        }

        // 2. Generate wavefront ribbons: offset outward from the anatomy, ring by ring, until one has
        // grown entirely past the boundary (that ring is the outer edge). outerPlanarBox is used only
        // as the stop boundary here - not for clipping.
        var wavefrontResult = GenerateIterativeRibbons(local2D, outerPlanarBox, stepDistanceMm, maxRibbonRings);
        if (wavefrontResult.IsFailure)
            return wavefrontResult.Error;

        var ribbonLayers = wavefrontResult.Value; // List of layers, where each layer contains 1+ polygon islands

        // 2b. Inner bleed contour. Cutting the band exactly on the parting line leaves the flange
        // meeting it hairline-flush, so a face that rounds the wrong way opens a seam. Selecting
        // against a contour shrunk BleedMm inward instead makes the flange overlap the parting line
        // by that margin. The outer edge needs no such guard - nothing has to meet it.
        //
        // It stays wound CCW like everything else. Reversing it would change which regions
        // triangulateContours fills, but not which faces survive ExtractBandTriangles, so it buys
        // nothing here.
        var innerBleed = OffsetOrOriginal(local2D, -innerBleedMm);

        // 3. Triangulate the whole footprint with every contour a constrained edge.
        //
        // The anatomy loop is not made a hole here. The band is carved AFTER triangulation by keeping
        // only the faces that fall between the contours - see ExtractBandTriangles. Every contour is a
        // constrained edge of the triangulation, so no triangle straddles one and a centroid
        // containment test classifies each face exactly. Ring 0 (anatomy) stays a contour even though
        // the band runs past it, so the triangulation carries vertices exactly on the parting line for
        // LiftWavefrontToWorldSpace to pin.
        var outermost = ribbonLayers.Count > 0 ? ribbonLayers[^1] : new List<Vector2[]>();
        var inner = new List<IReadOnlyList<Vector2>>();
        foreach (var layer in ribbonLayers.Take(ribbonLayers.Count - 1))
            inner.AddRange(layer);
        inner.Add(local2D);
        inner.Add(innerBleed);

        var triangulation = TriangulateNested(outermost, inner);
        if (triangulation.IsFailure)
            return new Error("Geometry.FlangeTriangulationFailed", "Failed to triangulate 2D wavefront flange.");

        // 4b. Launch directions, when a surface was supplied: the body's outward normal at each
        // point of the parting line, carried into the local frame. Null leaves the lift flattening
        // straight off the line as it always did.
        var launchLocal = launchSurface is null
            ? null
            : LocalLaunchDirections(inner3DLoop, launchSurface, inverseRotation);

        // 5. Execute Inside-Out Wavefront Lift & Height Relaxation
        var lifted = LiftWavefrontToWorldSpace(
            triangulation.Value, local3D, local2D, ribbonLayers, innerBleed, concaveBandWidthMm, rotation,
            launchLocal, launchHoldMm, rawFlange, launchSmoothingPasses, out var launchedVertices);
        if (lifted.IsFailure)
            return lifted;

        // Raw: the sweep's own shape, with nothing run over it afterwards. The three passes below and
        // the height relaxation inside the lift are each capable of moving the surface a long way, so
        // when the flange looks wrong the first question is whether the sweep produced it or a repair
        // did. This answers that. It is a diagnostic - the seal is skipped with everything else, so
        // the flange is not guaranteed to sever the mould in this mode.
        if (rawFlange)
            return lifted;

        // 6. No remesh. The rings arrive at a spacing matched to the step that produced them, so
        // the triangulation is already near-equilateral - see GenerateIterativeRibbons. A uniform
        // remesh used to stand here to repair the slivers the old dense rings produced, and it is
        // actively harmful now: on every real body measured MeshLib's remesh returned a mesh whose
        // vertices largely coincided (chin_bolus: 5,856 vertices at 1,473 distinct positions), which
        // is thousands of zero-area faces and exactly the "mesh B has self-intersections" the mould
        // boolean then refuses to cut with. Building the triangulation well beats repairing it.

        // 7. Overhang cleanup: ease any face steeper than 45 degrees back toward the printable limit
        // by lowering/raising vertices along the pull axis, so the flange has no steep support-needing
        // walls where the parting line plunges. Scoped to the flange.
        var relaxed = RelaxSteepSlopesWorld(lifted.Value, direction, overhangTargetSlopeDeg, iterations: 2000, rate: 0.7, innerHold: innerSealHold, pinnedVerts: launchedVertices);
        if (relaxed.IsFailure || sealAgainst is null)
            return relaxed;

        // 8. Guarantee the seal. Everything above places the inner rim by footprint arithmetic - it is
        // offset inward in plan and given the height of the nearest parting point - and that is not
        // enough to keep it inside the body. Where the parting line's height changes quickly, "nearest
        // in plan" hands a vertex a height sampled from a stretch of line that is somewhere else
        // entirely, and the vertex surfaces outside the body. Measured on the shipping 1.5mm bleed:
        // 1 rim vertex of 153 pokes out on chin.3mf (by 0.141mm) and 3 of 256 on scalp.3mf (by
        // 0.935mm). Each one is a hairline bridge of mould material that survives the cut.
        //
        // Widening the bleed does not fix this - the leaks are local, so a uniform inward offset just
        // relocates them (2.5mm closes chin's but opens a worse one on scalp). Pushing each offending
        // vertex individually onto the far side of the surface does, and it is the only form of this
        // that is a guarantee rather than a margin that usually holds.
        return SealInnerRimAgainstBody(relaxed.Value, sealAgainst, direction, inner3DLoop, sealMarginMm);
    }

    /// <summary>
    /// Pushes every inner-rim vertex of <paramref name="flange"/> to at least <paramref name="marginMm"/>
    /// inside <paramref name="body"/>, so the flange cannot leave a bridge where it meets the mould
    /// cavity. Only the rim is touched: boundary vertices whose footprint falls inside the parting
    /// loop. The outer rim (outside the loop) is left alone - nothing has to seal against it.
    ///
    /// <para>
    /// A vertex is moved by projecting it onto the body and stepping <paramref name="marginMm"/> past
    /// that point along the inward surface normal, which lands it inside regardless of which way it
    /// was out. On failure the flange is returned as-is: an unsealed flange still cuts most of the
    /// mould, and is a better outcome than failing the whole parting.
    /// </para>
    /// </summary>
    public Result<IReadOnlyList<FlangeSealPoint>> InspectFlangeSeal(
        IMesh flange, IMesh body, Vector3 pullDirection, IReadOnlyList<Vector3> partingLoop)
    {
        if (flange is null || body is null)
            return MeshErrors.NullSource;
        if (flange.IsEmpty || body.IsEmpty)
            return MeshErrors.InvalidMesh;
        if (pullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;
        if (partingLoop is null || partingLoop.Count < 3)
            return MeshErrors.InvalidPartingLine;

        var direction = pullDirection.Normalize();
        var rim = InnerRimVertexIndices(flange, direction, partingLoop);
        if (rim.Count == 0)
            return Result.Success<IReadOnlyList<FlangeSealPoint>>(Array.Empty<FlangeSealPoint>());

        var bodyIndex = _engine.Spatial.IndexFor(body);
        if (bodyIndex.IsFailure) return bodyIndex.Error;

        var vertices = flange.Vertices;

        var points = new List<FlangeSealPoint>(rim.Count);
        foreach (int index in rim)
        {
            var v = vertices[index];
            points.Add(new FlangeSealPoint(v, bodyIndex.Value.SignedDistance(v)));
        }

        return Result.Success<IReadOnlyList<FlangeSealPoint>>(points);
    }

    private Result<IMesh> SealInnerRimAgainstBody(
        IMesh flange, IMesh body, Vector3 pullDirection, IReadOnlyList<Vector3> partingLoop, double marginMm)
    {
        try
        {
            var vertices = flange.Vertices.ToArray();
            var rim = InnerRimVertexIndices(flange, pullDirection, partingLoop);
            if (rim.Count == 0)
                return Result.Success(flange);

            var bodyIndex = _engine.Spatial.IndexFor(body);
            if (bodyIndex.IsFailure) return Result.Success(flange);

            var bodyVerts = body.Vertices;
            var bodyTris = body.Triangles;

            // Faces touching each rim vertex, so a move can be checked against what it does to them.
            var incident = new Dictionary<int, List<int>>(rim.Count);
            foreach (int index in rim) incident[index] = new List<int>(6);
            var flangeTris = flange.Triangles.ToArray();
            for (int t = 0; t + 2 < flangeTris.Length; t += 3)
            {
                for (int k = 0; k < 3; k++)
                    if (incident.TryGetValue(flangeTris[t + k], out var list)) list.Add(t);
            }

            var neighbours = RimNeighbours(flangeTris, rim);
            var offset = new Vector3[rim.Count];

            // Pushed in over several rounds rather than in one move each, and spread along the rim
            // between rounds. Sealing a vertex on its own is what used to crease the flange: each one
            // lands on whichever body face is nearest it, so two neighbours get sent in quite
            // different directions and the face between them is left folded - and the fold survives
            // into the extrusion, which offsets both sheets along one axis and drives them through
            // each other. Sharing each push with the rim either side of it bends the flange instead
            // of kinking it, and re-measuring every round is what still gets the stubborn ones in:
            // whatever a diffused push leaves short is simply pushed again.
            for (int round = 0; round < SealRounds; round++)
            {
                bool anyOutstanding = false;
                for (int i = 0; i < rim.Count; i++)
                {
                    offset[i] = Vector3.Zero;

                    var v = vertices[rim[i]];
                    double distance = bodyIndex.Value.SignedDistance(v);

                    // Only what is actually outside. The margin is where an offending vertex is
                    // sent, not a depth every vertex has to reach: treating it as a threshold moved
                    // roughly half the rim - the median rim point sits about 0.5mm in - and all that
                    // extra shoving is what left the flange steep enough to self-intersect once
                    // extruded. A vertex already inside bridges nothing and is left alone.
                    if (distance < 0.0)
                        continue;

                    var onFace = bodyIndex.Value.ClosestPoint(v);
                    if (!onFace.HasValue) continue;

                    var closest = onFace.Value.Point;
                    int face = onFace.Value.Triangle;
                    if (face < 0 || (face * 3) + 2 >= bodyTris.Length)
                        continue;

                    var a = bodyVerts[bodyTris[face * 3]];
                    var b = bodyVerts[bodyTris[(face * 3) + 1]];
                    var c = bodyVerts[bodyTris[(face * 3) + 2]];
                    var normal = (b - a).Cross(c - a);
                    if (normal.LengthSquared < 1e-12)
                        continue;

                    // Outward normal, so stepping against it from the surface point goes into the body.
                    offset[i] = closest - (normal.Normalize() * marginMm) - v;
                    anyOutstanding = true;
                }

                if (!anyOutstanding) break;

                Diffuse(offset, neighbours);

                for (int i = 0; i < rim.Count; i++)
                {
                    if (offset[i] == Vector3.Zero) continue;

                    // Still guarded. Diffusion makes a fold far less likely rather than impossible,
                    // and where the flange genuinely cannot bend far enough the vertex is left short
                    // of the body - reported as a breached seal point rather than silently folded.
                    vertices[rim[i]] = LargestSafeStep(
                        vertices, incident[rim[i]], flangeTris, rim[i], vertices[rim[i]] + offset[i]);
                }
            }

            var flat = new double[vertices.Length * 3];
            for (int i = 0; i < vertices.Length; i++)
            {
                flat[i * 3] = vertices[i].X;
                flat[(i * 3) + 1] = vertices[i].Y;
                flat[(i * 3) + 2] = vertices[i].Z;
            }

            var rebuilt = CreateMesh(flat, flange.Triangles.AsSpan());
            return rebuilt.IsSuccess ? rebuilt : Result.Success(flange);
        }
        catch (Exception)
        {
            return Result.Success(flange);
        }
    }

    /// <summary>
    /// How many times the seal step is halved looking for one the flange can take. Six leaves the
    /// smallest attempt at about 1.5% of the move, which is close enough to not moving that going
    /// finer buys nothing.
    /// </summary>
    private const int SealBackoffAttempts = 6;

    /// <summary>
    /// Rounds of measure-diffuse-push the seal runs. Each round only has to recover what diffusion
    /// took off the last one's peak, so this converges quickly; six clears every rim measured that
    /// can be cleared at all, and the rest are vertices the flange cannot reach without folding.
    /// </summary>
    private const int SealRounds = 6;

    /// <summary>
    /// How much of a rim vertex's push is shared with its two neighbours, in [0, 0.5]. This is the
    /// whole point of diffusing - it turns a single vertex's move into a bend spread over its
    /// neighbourhood - so it wants to be substantial; at 0.5 the vertex keeps none of its own push
    /// and the field just smears along the rim without ever seating.
    /// </summary>
    private const double SealDiffusion = 0.35;

    /// <summary>
    /// Shares each rim vertex's pending push with the vertices either side of it, so the flange bends
    /// over a stretch of rim rather than kinking at one vertex. A vertex with no neighbours recorded
    /// (a rim that did not come out as a clean loop) keeps its own push untouched.
    /// </summary>
    private static void Diffuse(Vector3[] offsets, List<int>[] neighbours)
    {
        var blended = new Vector3[offsets.Length];
        for (int i = 0; i < offsets.Length; i++)
        {
            var adjacent = neighbours[i];
            if (adjacent.Count == 0) { blended[i] = offsets[i]; continue; }

            var mean = Vector3.Zero;
            foreach (int j in adjacent) mean += offsets[j];
            mean /= adjacent.Count;

            blended[i] = offsets[i].LerpTo(mean, SealDiffusion);
        }

        Array.Copy(blended, offsets, offsets.Length);
    }

    /// <summary>
    /// Neighbours of each rim vertex along the rim itself, as indices into <paramref name="rim"/>.
    /// Taken from the flange's boundary edges - an edge used by one face whose ends are both on the
    /// rim - so this follows the rim loop rather than cutting across the flange's interior.
    /// </summary>
    private static List<int>[] RimNeighbours(int[] triangles, List<int> rim)
    {
        var position = new Dictionary<int, int>(rim.Count);
        for (int i = 0; i < rim.Count; i++) position[rim[i]] = i;

        var use = new Dictionary<(int, int), int>(triangles.Length);
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Count(triangles[t], triangles[t + 1]);
            Count(triangles[t + 1], triangles[t + 2]);
            Count(triangles[t + 2], triangles[t]);
        }

        var neighbours = new List<int>[rim.Count];
        for (int i = 0; i < rim.Count; i++) neighbours[i] = new List<int>(2);

        foreach (var edge in use)
        {
            if (edge.Value != 1) continue; // interior edge - shared by two faces
            if (!position.TryGetValue(edge.Key.Item1, out int a)) continue;
            if (!position.TryGetValue(edge.Key.Item2, out int b)) continue;

            neighbours[a].Add(b);
            neighbours[b].Add(a);
        }

        return neighbours;

        void Count(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            use[key] = use.TryGetValue(key, out int seen) ? seen + 1 : 1;
        }
    }

    /// <summary>
    /// Moves <paramref name="index"/> as far toward <paramref name="target"/> as it can go without
    /// turning any face it belongs to by more than a right angle, halving the step until it fits.
    /// Returns the vertex unmoved if even the smallest step folds something.
    /// </summary>
    private static Vector3 LargestSafeStep(
        Vector3[] vertices, List<int> incidentFaces, int[] triangles, int index, Vector3 target)
    {
        var original = vertices[index];
        if (incidentFaces.Count == 0) return target;

        var before = new Vector3[incidentFaces.Count];
        for (int i = 0; i < incidentFaces.Count; i++)
            before[i] = FaceNormal(vertices, triangles, incidentFaces[i]);

        var step = target - original;
        for (int attempt = 0; attempt < SealBackoffAttempts; attempt++)
        {
            vertices[index] = original + step;

            bool folded = false;
            for (int i = 0; i < incidentFaces.Count && !folded; i++)
            {
                var after = FaceNormal(vertices, triangles, incidentFaces[i]);

                // A face that had no area to begin with has no orientation to preserve, and one that
                // has lost all of its area has been collapsed - which is a fold in the limit.
                if (before[i] == Vector3.Zero) continue;
                folded = after == Vector3.Zero || before[i].Dot(after) <= 0.0;
            }

            if (!folded) return vertices[index];
            step *= 0.5;
        }

        return original;
    }

    private static Vector3 FaceNormal(Vector3[] vertices, int[] triangles, int firstIndex)
    {
        var a = vertices[triangles[firstIndex]];
        var b = vertices[triangles[firstIndex + 1]];
        var c = vertices[triangles[firstIndex + 2]];

        var normal = (b - a).Cross(c - a);
        return normal.LengthSquared < 1e-18 ? Vector3.Zero : normal.Normalize();
    }

    /// <summary>
    /// Indices of the flange's boundary vertices that lie inside the parting loop's footprint - the
    /// rim that has to seal against the mould cavity, as opposed to the outer rim beyond it.
    /// </summary>
    private static List<int> InnerRimVertexIndices(
        IMesh flange, Vector3 pullDirection, IReadOnlyList<Vector3> partingLoop)
    {
        var triangles = flange.Triangles;
        var edgeUse = new Dictionary<(int, int), int>(triangles.Length);
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            CountEdge(edgeUse, triangles[i], triangles[i + 1]);
            CountEdge(edgeUse, triangles[i + 1], triangles[i + 2]);
            CountEdge(edgeUse, triangles[i + 2], triangles[i]);
        }

        var boundary = new HashSet<int>();
        foreach (var use in edgeUse)
        {
            if (use.Value != 1) continue; // an interior edge is shared by two faces
            boundary.Add(use.Key.Item1);
            boundary.Add(use.Key.Item2);
        }

        // Footprint of the loop and of each candidate, in a frame perpendicular to the pull axis.
        var d = pullDirection.Normalize();
        var seed = Math.Abs(d.Y) < 0.9 ? Vector3.UnitY : Vector3.UnitX;
        var u = seed.Cross(d).Normalize();
        var w = d.Cross(u);

        var loop2D = new Vector2[partingLoop.Count];
        for (int i = 0; i < partingLoop.Count; i++)
            loop2D[i] = new Vector2(partingLoop[i].Dot(u), partingLoop[i].Dot(w));

        var vertices = flange.Vertices;
        var inner = new List<int>(boundary.Count);
        foreach (int index in boundary)
        {
            var p = new Vector2(vertices[index].Dot(u), vertices[index].Dot(w));
            if (ContainsPoint(loop2D, p)) inner.Add(index);
        }
        return inner;

        static void CountEdge(Dictionary<(int, int), int> map, int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            map[key] = map.TryGetValue(key, out int seen) ? seen + 1 : 1;
        }
    }

    /// <summary>
    /// Thickens the open, single-sided parting flange into a closed solid slab <paramref name="depth"/>
    /// mm thick, so the shut-off surface has a printable wall. The surface is copied to two sheets
    /// offset +/- half the depth along <paramref name="direction"/> (the pull axis), and every boundary
    /// edge - the inner parting hole and the outer rim both - is closed with a vertical side wall. The
    /// two sheets and the walls are wound so the result is a watertight, outward-facing solid. Cheap
    /// enough to re-run on a depth-slider drag.
    /// </summary>
    public Result<IMesh> ExtrudeFlange(IMesh surface, Vector3 direction, double depth)
    {
        if (depth <= 0.0 || double.IsNaN(depth) || double.IsInfinity(depth))
            return new Error("Geometry.InvalidDepth", "Extrusion depth must be a positive finite number.");

        return ExtrudeByNormals(surface, depth);
    }

    /// <summary>
    /// Most voxels an offset may use. Bounds what is otherwise cubic in the flange's extent over the
    /// cell size, which on a large body with a thin cutter runs to hundreds of millions.
    /// </summary>
    private const double MaxOffsetVoxels = 60e6;

    public Result<IMesh> ThickenFlange(
        IMesh surface, Vector3 direction, double thicknessMm, double voxelSizeMm)
    {
        if (surface is null) return MeshErrors.NullSource;
        if (surface.IsEmpty) return MeshErrors.InvalidMesh;
        if (thicknessMm <= 0.0) return new Error("Geometry.InvalidDepth", "Thickness must be positive.");
        if (voxelSizeMm <= 0.0) return new Error("Geometry.InvalidVoxelSize", "Voxel size must be positive.");

        // Extruded to a token thickness first, then offset. Offsetting the open surface directly
        // needs the winding-number sign mode, and what that returns is not closed - which the mould
        // boolean will not take. A closed input can be offset from a signed field instead, and a
        // signed field's isosurface is closed by construction.
        //
        // The thin slab this starts from may well cross itself; that is the whole point. The offset
        // is read off a distance field sampled on a grid, and a field has no memory of the surface
        // having passed through itself, so the crossings do not survive into the result.
        // The grid spans the flange, which reaches past the mould on every side, and its cost is
        // cubic in extent over cell size. Estimated before anything is allocated: a body that needs
        // more cells than this does not run slowly, it exhausts memory and takes the application with
        // it, and the user can act on being told to coarsen the cutter.
        var (boxMin, boxMax) = Bounds(surface.Vertices);
        var span = (boxMax - boxMin) + new Vector3(thicknessMm * 2.0, thicknessMm * 2.0, thicknessMm * 2.0);
        double cells = ((double)span.X / voxelSizeMm) * (span.Y / voxelSizeMm) * (span.Z / voxelSizeMm);
        if (cells > MaxOffsetVoxels)
            return new Error("Geometry.OffsetTooFine",
                $"Thickening this parting mesh needs about {cells / 1e6:F0} million voxels, over the " +
                $"{MaxOffsetVoxels / 1e6:F0} million allowed. Increase the parting mesh depth - the grid " +
                "is sized relative to it, so a thicker cutter is a coarser and much cheaper grid.");

        double seed = Math.Min(thicknessMm * 0.25, voxelSizeMm);
        var slab = ExtrudeByNormals(surface, seed);
        if (slab.IsFailure) return slab;

        var offset = _engine.Modifiers.Offset(slab.Value, (thicknessMm - seed) * 0.5, voxelSizeMm);
        if (offset.IsFailure) return new Error("Geometry.ThickenFailed", offset.Error.Description);

        return Result.Success(offset.Value.WithMetadata(surface.Metadata));
    }

    public Result<IMesh> ExtrudeFlangeToSolid(
        IMesh surface, Vector3 direction, double topAlongAxis, double roundingMm, double voxelSizeMm)
    {
        if (surface is null) return MeshErrors.NullSource;
        if (surface.IsEmpty) return MeshErrors.InvalidMesh;

        var axis = direction.Normalize();
        var loops = BoundaryLoops(surface);
        if (loops.Count == 0) return new Error("Geometry.FlangeNotOpen",
            "The flange has no boundary to build a solid from - it is already closed.");

        // The rim enclosing the largest footprint is the outer one; the rest are the hole the parting
        // line runs through.
        int outer = 0;
        double widest = -1;
        for (int i = 0; i < loops.Count; i++)
        {
            var footprint = loops[i].Select(v => PartingFrame.ToPlane(surface.Vertices[v], axis)).ToList();
            double area = Math.Abs(SignedArea2D(footprint));
            if (area <= widest) continue;
            widest = area;
            outer = i;
        }

        var vertices = surface.Vertices.ToList();
        var triangles = surface.Triangles.ToList();

        // 1. The parting surface itself is the lid, so it is kept exactly as it is - it is the shape
        // the mould has to be cut along, and nothing here is entitled to move it. Only its holes are
        // filled, because a lid with a hole in it leaves a tube rather than a solid.
        for (int i = 0; i < loops.Count; i++)
        {
            if (i == outer) continue;

            var loop = loops[i];
            var centre = Vector3.Zero;
            foreach (int v in loop) centre += surface.Vertices[v];
            centre /= loop.Count;

            int hub = vertices.Count;
            vertices.Add(centre);
            for (int k = 0; k < loop.Count; k++)
            {
                triangles.Add(loop[k]);
                triangles.Add(loop[(k + 1) % loop.Count]);
                triangles.Add(hub);
            }
        }

        // 2. Only the outer contour is carried up, to a flat plane past the mould. Sweeping every
        // vertex instead - which is what this did - builds a prism as tall as the sweep and as wide as
        // the flange, and offsetting a volume that size is what put the voxel grid over its budget on
        // three bodies out of four. The wall is the same shape whichever way it is built.
        var rim = loops[outer];
        int wallBase = vertices.Count;
        foreach (int v in rim)
        {
            var p = surface.Vertices[v];
            vertices.Add(p + (axis * (topAlongAxis - p.Dot(axis))));
        }

        for (int k = 0; k < rim.Count; k++)
        {
            int a = rim[k], b = rim[(k + 1) % rim.Count];
            int c = wallBase + k, d = wallBase + ((k + 1) % rim.Count);

            triangles.Add(a); triangles.Add(c); triangles.Add(b);
            triangles.Add(b); triangles.Add(c); triangles.Add(d);
        }

        // 3. Cap the top, and the solid is closed.
        var lidCentre = Vector3.Zero;
        for (int k = 0; k < rim.Count; k++) lidCentre += vertices[wallBase + k];
        lidCentre /= rim.Count;

        int lidHub = vertices.Count;
        vertices.Add(lidCentre);
        for (int k = 0; k < rim.Count; k++)
        {
            triangles.Add(wallBase + k);
            triangles.Add(wallBase + ((k + 1) % rim.Count));
            triangles.Add(lidHub);
        }

        var flat = new double[vertices.Count * 3];
        for (int i = 0; i < vertices.Count; i++)
        {
            flat[i * 3] = vertices[i].X;
            flat[(i * 3) + 1] = vertices[i].Y;
            flat[(i * 3) + 2] = vertices[i].Z;
        }

        var built = CreateMesh(flat, triangles.ToArray());
        if (built.IsFailure || roundingMm <= 0.0) return built;

        // 4. Grow then shrink. Each pass samples a distance field onto a grid and re-extracts it, and
        // a field cannot represent the surface having crossed itself, so the crossings do not survive.
        // Out and back leaves the shape where it was, less detail finer than the rounding.
        // Both failures are reported rather than absorbed. Returning the un-rounded solid instead -
        // which is what this did - hands back a cutter that still has every crossing the rounding was
        // asked to remove, and says nothing: on larynx_bolus that produced a tool identical to the
        // raw one, indistinguishable from a rounded result until its triangle count was compared
        // against a deliberately unrounded build. A caller that would rather have the unrounded solid
        // can ask for it by passing no rounding.
        var grown = OffsetSolid(built.Value, roundingMm, voxelSizeMm);
        if (grown.IsFailure) return grown.Error;

        return OffsetSolid(grown.Value, -roundingMm, voxelSizeMm);
    }

    private Result<IMesh> OffsetSolid(IMesh solid, double offsetMm, double voxelSizeMm)
    {
        if (voxelSizeMm <= 0.0) return new Error("Geometry.InvalidVoxelSize", "Voxel size must be positive.");

        var (boxMin, boxMax) = Bounds(solid.Vertices);
        var span = (boxMax - boxMin) + new Vector3(Math.Abs(offsetMm) * 2.0, Math.Abs(offsetMm) * 2.0, Math.Abs(offsetMm) * 2.0);
        double cells = ((double)span.X / voxelSizeMm) * (span.Y / voxelSizeMm) * (span.Z / voxelSizeMm);
        if (cells > MaxOffsetVoxels)
            return new Error("Geometry.OffsetTooFine",
                $"Rounding this parting mesh needs about {cells / 1e6:F0} million voxels, over the " +
                $"{MaxOffsetVoxels / 1e6:F0} million allowed. Increase the parting mesh depth - the " +
                "grid is sized relative to it, so a thicker cutter is a coarser and much cheaper grid.");

        var offset = _engine.Modifiers.Offset(solid, offsetMm, voxelSizeMm);
        if (offset.IsFailure) return new Error("Geometry.OffsetFailed", offset.Error.Description);

        return Result.Success(offset.Value.WithMetadata(solid.Metadata));
    }

    /// <summary>Ordered boundary loops: edges used by exactly one face, walked end to end.</summary>
    private static List<List<int>> BoundaryLoops(IMesh mesh)
    {
        var triangles = mesh.Triangles;
        var use = new Dictionary<(int, int), int>(triangles.Length);
        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            Count(triangles[t], triangles[t + 1]);
            Count(triangles[t + 1], triangles[t + 2]);
            Count(triangles[t + 2], triangles[t]);
        }

        var neighbours = new Dictionary<int, List<int>>();
        foreach (var edge in use)
        {
            if (edge.Value != 1) continue;
            Link(edge.Key.Item1, edge.Key.Item2);
            Link(edge.Key.Item2, edge.Key.Item1);
        }

        var loops = new List<List<int>>();
        var visited = new HashSet<int>();
        foreach (int start in neighbours.Keys)
        {
            if (visited.Contains(start)) continue;
            visited.Add(start);

            var loop = new List<int> { start };
            int current = start, previous = -1;
            while (true)
            {
                int step = -1;
                foreach (int option in neighbours[current])
                {
                    if (option == previous) continue;
                    step = option;
                    break;
                }

                if (step < 0 || step == start) break;
                if (visited.Contains(step)) break;

                visited.Add(step);
                loop.Add(step);
                previous = current;
                current = step;
            }

            if (loop.Count >= 3) loops.Add(loop);
        }

        return loops;

        void Count(int a, int b)
        {
            var key = a < b ? (a, b) : (b, a);
            use[key] = use.TryGetValue(key, out int seen) ? seen + 1 : 1;
        }

        void Link(int from, int to)
        {
            if (!neighbours.TryGetValue(from, out var list)) neighbours[from] = list = new List<int>(2);
            list.Add(to);
        }
    }

    /// <summary>
    /// The shared body of both extrusions: two copies of <paramref name="surface"/> at
    /// <paramref name="lower"/> and <paramref name="upper"/> along <paramref name="direction"/>, with
    /// every boundary edge closed by a side wall, wound into a watertight outward-facing solid.
    ///
    /// <para>
    /// Neither copy can meet the other while the surface is a height field over the plane
    /// perpendicular to the direction - two copies of a single-valued surface translated apart along
    /// its own axis stay apart however steeply it falls. That holds for the planar wavefront by
    /// construction, since it is built as heights over a 2D triangulation, and it is why that flange
    /// extrudes to a clean solid while a swept one, which genuinely overhangs, does not.
    /// </para>
    /// </summary>
    private Result<IMesh> ExtrudeByNormals(IMesh surface, double depth)
    {
        if (surface is null)
            return MeshErrors.NullSource;
        if (surface.IsEmpty)
            return MeshErrors.InvalidMesh;

        var srcVerts = surface.Vertices;
        var srcTris = surface.Triangles;
        int n = srcVerts.Length;

        // Compute area-weighted per-vertex normals
        var normals = new Vector3[n];
        for (int t = 0; t + 2 < srcTris.Length; t += 3)
        {
            int a = srcTris[t], b = srcTris[t + 1], c = srcTris[t + 2];
            var v0 = srcVerts[a];
            var v1 = srcVerts[b];
            var v2 = srcVerts[c];
            var normal = (v1 - v0).Cross(v2 - v0);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }

        for (int i = 0; i < n; i++)
        {
            if (normals[i] != Vector3.Zero)
                normals[i] = normals[i].Normalize();
        }

        // Two vertex copies: the top sheet occupies [0, n), the bottom sheet [n, 2n).
        var verts = new double[n * 2 * 3];
        double halfDepth = depth * 0.5;
        for (int i = 0; i < n; i++)
        {
            var offset = normals[i] * halfDepth;
            var top = srcVerts[i] + offset;
            var bot = srcVerts[i] - offset;

            verts[i * 3] = top.X; verts[i * 3 + 1] = top.Y; verts[i * 3 + 2] = top.Z;
            int bOffset = n + i;
            verts[bOffset * 3] = bot.X; verts[bOffset * 3 + 1] = bot.Y; verts[bOffset * 3 + 2] = bot.Z;
        }

        var tris = new List<int>(srcTris.Length * 2 + 64);
        var directed = new HashSet<(int, int)>();
        for (int t = 0; t + 2 < srcTris.Length; t += 3)
        {
            int a = srcTris[t], b = srcTris[t + 1], c = srcTris[t + 2];

            // Top sheet keeps the surface winding; bottom sheet is reversed so its normal faces the
            // opposite way, and its vertices are the +n copies.
            tris.Add(a); tris.Add(b); tris.Add(c);
            tris.Add(n + a); tris.Add(n + c); tris.Add(n + b);

            directed.Add((a, b));
            directed.Add((b, c));
            directed.Add((c, a));
        }

        // Boundary edge = a directed edge whose reverse is absent. Close each with a wall wound b->a on
        // top (opposite the top face's a->b) so every edge is shared by exactly two oppositely-wound
        // faces - i.e. the solid stays watertight and manifold.
        foreach (var (a, b) in directed)
        {
            if (directed.Contains((b, a)))
                continue;

            tris.Add(b); tris.Add(a); tris.Add(n + a);
            tris.Add(b); tris.Add(n + a); tris.Add(n + b);
        }

        var result = CreateMesh(verts.AsSpan(), CollectionsMarshal.AsSpan(tris));
        if (result.IsFailure) return result.Error;

        return Result.Success(result.Value.WithMetadata(surface.Metadata));
    }



    public Result<IMesh> GenerateSurfaceSweepFlangeMesh(
        IReadOnlyList<Vector3> inner3DLoop,
        IReadOnlyList<Vector2> outerPlanarBox,
        Vector3 planeNormal,
        IMesh body,
        double stepDistanceMm = 3.0,
        int maxRings = 200,
        double innerBleedMm = BleedMm,
        double boundsMarginMm = 10.0)
    {
        if (inner3DLoop is null || inner3DLoop.Count < 3) return MeshErrors.InvalidPolygon;
        if (outerPlanarBox is null || outerPlanarBox.Count < 3) return MeshErrors.InvalidPolygon;
        if (planeNormal == Vector3.Zero) return MeshErrors.InvalidPullDirection;
        if (body is null) return MeshErrors.NullSource;
        if (body.IsEmpty) return MeshErrors.InvalidMesh;

        var axis = planeNormal.Normalize();
        int n = inner3DLoop.Count;

        // Outward direction per point: the body's normal at the rim, made perpendicular to the loop
        // so the march is across the line rather than along it. This is the "directly out" the whole
        // sweep is built on.
        var surfaceNormals = OutwardNormalsAlong(inner3DLoop, body);
        var outward = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var tangent = inner3DLoop[(i + 1) % n] - inner3DLoop[(i - 1 + n) % n];
            outward[i] = Perpendicular(surfaceNormals[i], tangent, axis);
        }

        // The volume the flange has to reach and no further: the body's own bounds opened up by the
        // same margin the outer contour uses, which clears the mould around it.
        var (boundsMin, boundsMax) = Bounds(body.Vertices);
        boundsMin -= new Vector3(boundsMarginMm, boundsMarginMm, boundsMarginMm);
        boundsMax += new Vector3(boundsMarginMm, boundsMarginMm, boundsMarginMm);

        var heading = new Vector3[n];
        outward.CopyTo(heading, 0);

        var current = inner3DLoop.ToArray();
        var next = new Vector3[n];

        // Marching outward: stops at the bounding box, rather than walking forever. The flange doesn't
        // have to close back on itself, just reach far enough to sever the mould.
        var marching = new bool[n];
        Array.Fill(marching, true);
        var rings = new List<Vector3[]> { current };

        for (int step = 0; step < maxRings; step++)
        {
            bool anyMarching = false;

            // Heading is the direction the ring advances in. The original parting line took its normal
            // directly from the body surface, so this started straight outward. At every step past that
            // it takes its normal from the ring being marched, so it is the surface normal of the
            // flange itself.
            //
            // Without this the whole sweep would just be a flat plane dragged out from the line.
            for (int i = 0; i < n; i++)
            {
                var tangent = current[(i + 1) % n] - current[(i - 1 + n) % n];
                var binormal = outward[i];
                if (step > 0)
                {
                    var radial = current[i] - rings[step - 1][i];
                    binormal = radial.Normalize();
                }
                heading[i] = Perpendicular(binormal, tangent, axis);
            }

            // The points in the ring are spaced unevenly, so the tangents used to compute the heading
            // above skew towards whichever neighbour happens to be closer. The surface normal itself
            // is derived from its own point's normal and its own local tangent, so it carries all the
            // noise of both, and stepping along them unsmoothed prints that noise into the ring as
            // waves - which the next ring then takes its tangents from and amplifies.
            SmoothDirections(heading, marching);

            for (int i = 0; i < n; i++)
            {
                if (!marching[i]) { next[i] = current[i]; continue; }

                next[i] = current[i] + (heading[i] * stepDistanceMm);

                if (Outside(next[i], boundsMin, boundsMax)) marching[i] = false;
                else anyMarching = true;
            }

            if (!anyMarching)
            {
                rings.Add(next);
                break;
            }

            // A concave stretch still crowds points together, so the ring is smoothed and respaced
            // before it becomes the next one's basis. Smoothing bleeds the crowding out along the
            // ring; respacing stops a bunched stretch from being sampled far more finely than the
            // rest and dominating the next step's tangents.
            //
            // Both are held off the points that have stopped: moving them would undo the limit, and
            // respacing in particular redistributes every point around the ring.
            if (Array.TrueForAll(marching, m => m))
            {
                Relax(next, SweepRelaxation);
                Respace(next);
            }

            // Every ring is checked for folds and repaired before the next one is taken from it. A
            // fold left in place is not a local blemish: the next ring's directions are derived from
            // this one's tangents, so a reversed stretch seeds reversed directions, and the twist
            // grows outward instead of washing out. Repairing as we go is what keeps it from
            // compounding - the planar sweep gets the same guarantee for free, because Clipper's
            // offsetting cannot return a self-crossing contour.
            RepairFolds(next);

            rings.Add(next);
            current = next;
        }

        return StitchRings(rings, body.Metadata);
    }

    public Result<IMesh> GenerateSurfaceSweepFlangeMesh3D(
        IReadOnlyList<Vector3> inner3DLoop,
        Vector3 planeNormal,
        IMesh body,
        double stepDistanceMm = 3.0,
        int maxRings = 200,
        double innerBleedMm = BleedMm,
        double boundsMarginMm = 10.0)
    {
        if (inner3DLoop is null || inner3DLoop.Count < 3) return MeshErrors.InvalidPolygon;
        if (planeNormal == Vector3.Zero) return MeshErrors.InvalidPullDirection;
        if (body is null) return MeshErrors.NullSource;
        if (body.IsEmpty) return MeshErrors.InvalidMesh;

        var axis = planeNormal.Normalize();
        int n = inner3DLoop.Count;

        // Outward direction per point: the body's normal at the rim, made perpendicular to the loop
        var surfaceNormals = OutwardNormalsAlong(inner3DLoop, body);
        var outward = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var tangent = inner3DLoop[(i + 1) % n] - inner3DLoop[(i - 1 + n) % n];
            outward[i] = Perpendicular(surfaceNormals[i], tangent, axis).Normalize();
        }

        // The volume the flange has to reach and no further: the body's own bounds opened up by the margin.
        var (boundsMin, boundsMax) = Bounds(body.Vertices);
        boundsMin -= new Vector3(boundsMarginMm, boundsMarginMm, boundsMarginMm);
        boundsMax += new Vector3(boundsMarginMm, boundsMarginMm, boundsMarginMm);

        var rings3D = new List<Vector3[]>();
        
        // Inner bleed contour
        var bleed3D = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            bleed3D[i] = inner3DLoop[i] - (outward[i] * innerBleedMm);
        }
        RepairFolds(bleed3D);
        rings3D.Add(bleed3D);
        
        // Original contour
        var originalLoop = inner3DLoop.ToArray();
        rings3D.Add(originalLoop);

        // March outward
        var current = inner3DLoop.ToArray();
        var heading = new Vector3[n];
        outward.CopyTo(heading, 0);
        var marching = new bool[n];
        Array.Fill(marching, true);
        
        for (int step = 0; step < maxRings; step++)
        {
            var next = new Vector3[n];
            bool anyInside = false;

            for (int i = 0; i < n; i++)
            {
                var tangent = current[(i + 1) % n] - current[(i - 1 + n) % n];
                var binormal = outward[i];
                if (step > 0)
                {
                    var radial = current[i] - rings3D[^2][i];
                    binormal = radial.Normalize();
                }
                heading[i] = Perpendicular(binormal, tangent, axis);
            }

            SmoothDirections(heading, marching);

            bool allInside = true;

            for (int i = 0; i < n; i++)
            {
                double stepDist = Outside(current[i], boundsMin, boundsMax) ? 1.0 : stepDistanceMm;
                next[i] = current[i] + (heading[i] * stepDist);
                if (!Outside(next[i], boundsMin, boundsMax))
                    anyInside = true;
                else
                    allInside = false;
            }

            if (allInside)
            {
                Relax(next, SweepRelaxation);
                Respace(next);
            }

            RepairFolds(next);

            rings3D.Add(next);
            current = next;
            if (!anyInside)
                break;
        }

        return StitchRings(rings3D, body.Metadata);
    }

    /// <summary>
    /// Builds the flange by lofting the parting line out to a ring on the mould, rather than by
    /// marching along the body's normals.
    ///
    /// <para>
    /// The sweep this stands beside never asks the mould anything: it takes the body's surface normal
    /// at the line and marches, so the body's undulation is carried the whole way to the outer wall and
    /// the mating face inherits it. But only the inner edge has to follow anatomy - it is the rim the
    /// two halves meet the bolus on. Where the outer edge comes out is free, so it is taken from the
    /// mould.
    /// </para>
    ///
    /// <para>
    /// The ring is the mould's own outline seen along the pull axis, and - this is the part that does
    /// the work - it takes its height from the parting line at the same bearing. Matched that way every
    /// radial of the loft runs out level, so there is no climb from the line back to a ledge and no
    /// slope to make it with. What is left tilts only around the ring, at the rate the line's own height
    /// changes, spread over the mould's longer perimeter. Measured against the marching sweep on the
    /// same bodies, the median face goes from 58 degrees off the parting plane to 2 on chin, and from
    /// 67 to 26 on scalp.
    /// </para>
    /// </summary>
    /// <param name="mould">The mould being split - what the outer ring is taken from.</param>
    /// <param name="innerBleedMm">
    /// How far inside the body the flange starts, so the cut has something to bite on. The seal is the
    /// reason this is not simply lofted from the line itself.
    /// </param>
    public Result<IMesh> GenerateMouldLoftFlangeMesh(
        IReadOnlyList<Vector3> partingLine,
        Vector3 planeNormal,
        IMesh body,
        IMesh mould,
        double innerBleedMm = BleedMm,
        double outerMarginMm = 10.0,
        int rings = 16,
        int heightSmoothing = 6)
    {
        if (partingLine is null || partingLine.Count < 8) return MeshErrors.InvalidPolygon;
        if (planeNormal == Vector3.Zero) return MeshErrors.InvalidPullDirection;
        if (body is null || body.IsEmpty) return MeshErrors.InvalidMesh;
        if (mould is null || mould.IsEmpty) return MeshErrors.InvalidMesh;
        if (rings < 2) rings = 2;

        var axis = planeNormal.Normalize();
        int n = partingLine.Count;

        var (u, v) = LoftFrame(axis);

        // The bleed ring, offset into the body along its own surface, is what carries the seal. Placed
        // exactly as the marching sweep places it, because that part was never the problem.
        var surfaceNormals = OutwardNormalsAlong(partingLine, body);
        var bleed = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var tangent = partingLine[(i + 1) % n] - partingLine[(i - 1 + n) % n];
            var outward = Perpendicular(surfaceNormals[i], tangent, axis).Normalize();
            bleed[i] = partingLine[i] - (outward * innerBleedMm);
        }
        RepairFolds(bleed);

        var centre = Vector2.Zero;
        for (int i = 0; i < n; i++) centre += LoftFlat(partingLine[i], u, v);
        centre /= n;

        // Bearings taken as a cumulative angle that is never allowed to go backwards. Read raw, a
        // parting line that doubles back hands several of its points the same stretch of outline, and
        // the loft pinches into a fan there - which is what a nearest-bearing match produced. Forcing
        // the sweep to advance makes the correspondence one-to-one all the way round.
        // Which way the line winds has to be read before any of this means anything. Nothing fixes the
        // order a traced loop comes back in, and against the wrong sign every step counts as backwards,
        // clamps to zero, and the whole sweep comes out empty - which read as "the polygon is invalid"
        // on three of four bodies while the fourth, wound the other way, worked.
        double turning = 0.0;
        for (int i = 1; i <= n; i++)
            turning += LoftWrap(
                LoftBearing(partingLine[i % n], u, v, centre)
                - LoftBearing(partingLine[i - 1], u, v, centre));

        double winding = turning >= 0.0 ? 1.0 : -1.0;

        var sweep = new double[n];
        double running = 0.0;
        double previous = LoftBearing(partingLine[0], u, v, centre);

        for (int i = 1; i < n; i++)
        {
            double here = LoftBearing(partingLine[i], u, v, centre);
            running += Math.Max(winding * LoftWrap(here - previous), 0.0);
            sweep[i] = running;
            previous = here;
        }

        // The closing step, from the last point back to the first, is part of the loop too. Left out,
        // the last point is mapped a full turn round to exactly where the first one is, and the seam
        // span between them comes out with no length on the outer ring.
        double closing = Math.Max(
            winding * LoftWrap(LoftBearing(partingLine[0], u, v, centre) - previous), 0.0);

        // A line whose bearings barely advance has nothing to map round the outline, and is refused
        // rather than folded onto a point.
        double span = running + closing;
        if (span < 1e-3) return MeshErrors.InvalidPolygon;

        var hull = LoftHull(mould.Vertices, u, v);
        if (hull.Count < 3) return MeshErrors.InvalidPolygon;

        double start = LoftBearing(partingLine[0], u, v, centre);

        // Carried past the outline rather than stopped on it. A cutter that ends exactly where the
        // mould ends does not sever it - the boolean comes back with two pieces that are each still
        // the whole mould, which is what "halves 99.8% / 99.8%" means when it happens.
        var outerFlat = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            double bearing = start + (winding * Math.Tau * sweep[i] / span);
            var direction = new Vector2(Math.Cos(bearing), Math.Sin(bearing));
            outerFlat[i] = LoftRayHit(hull, centre, bearing) + (direction * outerMarginMm);
        }

        // Height from the line at the same bearing, then eased round the ring. The match is exact but
        // it steps wherever the nearest point changes, and a stepped ring puts a crease in the surface
        // at every step.
        var heights = new double[n];
        for (int i = 0; i < n; i++) heights[i] = partingLine[i].Dot(axis);
        LoftSmooth(heights, heightSmoothing);

        var stack = new List<Vector3[]>(rings + 2) { bleed, partingLine.ToArray() };

        for (int r = 1; r <= rings; r++)
        {
            double t = (double)r / rings;
            var ring = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                double inner = partingLine[i].Dot(axis);
                var innerFlat = partingLine[i] - (axis * inner);
                var outFlat = (u * outerFlat[i].X) + (v * outerFlat[i].Y);

                ring[i] = innerFlat.LerpTo(outFlat, t)
                        + (axis * ((inner * (1.0 - t)) + (heights[i] * t)));
            }

            RepairFolds(ring);
            stack.Add(ring);
        }

        return StitchRings(stack, body.Metadata);
    }

    private static (Vector3 U, Vector3 V) LoftFrame(Vector3 axis)
    {
        var seed = Math.Abs(axis.Dot(Vector3.UnitY)) < 0.9 ? Vector3.UnitY : Vector3.UnitX;
        var u = seed.Cross(axis).Normalize();
        return (u, axis.Cross(u));
    }

    private static Vector2 LoftFlat(Vector3 p, Vector3 u, Vector3 v) =>
        new(p.Dot(u), p.Dot(v));

    private static double LoftBearing(Vector3 p, Vector3 u, Vector3 v, Vector2 centre)
    {
        var d = LoftFlat(p, u, v) - centre;
        return Math.Atan2(d.Y, d.X);
    }

    private static double LoftWrap(double angle)
    {
        while (angle > Math.PI) angle -= Math.Tau;
        while (angle < -Math.PI) angle += Math.Tau;
        return angle;
    }

    private static void LoftSmooth(double[] values, int passes)
    {
        int n = values.Length;
        if (n < 3) return;

        for (int pass = 0; pass < passes; pass++)
        {
            var next = new double[n];
            for (int i = 0; i < n; i++)
                next[i] = (values[(i - 1 + n) % n] + (2.0 * values[i]) + values[(i + 1) % n]) * 0.25;
            Array.Copy(next, values, n);
        }
    }

    /// <summary>
    /// The mould's outline in the plane perpendicular to the pull axis, as its convex hull there.
    /// Taken in that plane rather than in world XY, since the pull axis is not a world axis; and as the
    /// outline rather than the bounding rectangle, because a rectangle sends the loft out to four sharp
    /// corners it then has to fan across.
    /// </summary>
    private static List<Vector2> LoftHull(IReadOnlyList<Vector3> vertices, Vector3 u, Vector3 v)
    {
        var points = new List<Vector2>(vertices.Count);
        foreach (var p in vertices) points.Add(LoftFlat(p, u, v));

        points.Sort((a, b) => a.X == b.X ? a.Y.CompareTo(b.Y) : a.X.CompareTo(b.X));

        var hull = new List<Vector2>(points.Count + 1);
        for (int pass = 0; pass < 2; pass++)
        {
            int floor = hull.Count;
            for (int k = 0; k < points.Count; k++)
            {
                var p = pass == 0 ? points[k] : points[points.Count - 1 - k];
                while (hull.Count >= floor + 2 && LoftTurn(hull[^2], hull[^1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    private static double LoftTurn(Vector2 a, Vector2 b, Vector2 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    /// <summary>Where a ray from <paramref name="centre"/> leaves the hull.</summary>
    private static Vector2 LoftRayHit(List<Vector2> hull, Vector2 centre, double angle)
    {
        var dir = new Vector2(Math.Cos(angle), Math.Sin(angle));

        double furthest = 0.0;
        for (int i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            var edge = b - a;

            double denominator = (dir.X * edge.Y) - (dir.Y * edge.X);
            if (Math.Abs(denominator) < 1e-12) continue;

            var offset = a - centre;
            double t = ((offset.X * edge.Y) - (offset.Y * edge.X)) / denominator;
            double s = ((offset.X * dir.Y) - (offset.Y * dir.X)) / denominator;

            if (t <= 0.0 || s < 0.0 || s > 1.0) continue;
            furthest = Math.Max(furthest, t);
        }

        return centre + (dir * furthest);
    }

    /// <summary>
    /// How hard each swept ring is relaxed toward its neighbours' midpoint before the next step is
    /// taken from it. This is the sweep's only defence against the rings crowding where the parting
    /// line is concave, so it cannot be timid; it is applied to the ring's shape, never to the
    /// parting line itself, which is ring zero and never moves. Measured on chin and scalp, raising
    /// this from 0.35 to 0.6 cut the swept surface's self-intersections by roughly four fifths.
    /// </summary>
    private const double SweepRelaxation = 0.6;

    /// <summary>Laplacian smoothing of a closed ring, in place.</summary>
    private static void Relax(Vector3[] ring, double factor)
    {
        int n = ring.Length;
        if (n < 4) return;

        var blended = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var midpoint = (ring[(i - 1 + n) % n] + ring[(i + 1) % n]) * 0.5;
            blended[i] = ring[i] + ((midpoint - ring[i]) * factor);
        }

        Array.Copy(blended, ring, n);
    }

    /// <summary>
    /// Redistributes a closed ring's points to even arc-length spacing, in place and keeping the
    /// count. The march has to hold its point count - the stitching pairs point i of one ring with
    /// point i of the next - so this respaces rather than resamples.
    /// </summary>
    private static void Respace(Vector3[] ring)
    {
        int n = ring.Length;
        if (n < 4) return;

        var cumulative = new double[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + ring[i].DistanceTo(ring[(i + 1) % n]);

        double perimeter = cumulative[n];
        if (perimeter < 1e-4) return;

        var spaced = new Vector3[n];
        int segment = 0;
        for (int k = 0; k < n; k++)
        {
            double target = perimeter * k / n;
            while (segment < n - 1 && cumulative[segment + 1] < target) segment++;

            double span = cumulative[segment + 1] - cumulative[segment];
            double t = span > 1e-6 ? Math.Clamp((target - cumulative[segment]) / span, 0.0, 1.0) : 0.0;
            spaced[k] = ring[segment].LerpTo(ring[(segment + 1) % n], t);
        }

        Array.Copy(spaced, ring, n);
    }

    /// <summary>
    /// <paramref name="normal"/> with any component along <paramref name="tangent"/> removed, so it
    /// points across the loop rather than along it. Falls back to a direction built from the axis
    /// when the two are parallel and the projection has nothing left.
    /// </summary>
    private static Vector3 Perpendicular(Vector3 normal, Vector3 tangent, Vector3 axis)
    {
        if (tangent.LengthSquared > 1e-12)
        {
            var t = tangent.Normalize();
            var projected = normal - (t * normal.Dot(t));
            if (projected.LengthSquared > 1e-12) return projected.Normalize();

            var fallback = t.Cross(axis);
            if (fallback.LengthSquared > 1e-12) return fallback.Normalize();
        }

        return normal.LengthSquared > 1e-12 ? normal.Normalize() : axis;
    }

    /// <summary>
    /// How far a ring may turn at one point before it counts as folded. A ring of any reasonable
    /// resolution turns a few degrees per point, so a right angle is far outside anything the shape
    /// itself produces and only a doubling-back reaches it.
    /// </summary>
    private const double SweepFoldAngleDeg = 90.0;

    /// <summary>
    /// Passes of fold repair per ring. Each pass pulls the offending points onto the line between
    /// their neighbours, which can expose a neighbour that was hidden behind the first fold, so it
    /// takes a few; the count is bounded because a ring that will not come apart in this many is one
    /// that needs a different fix, not more of this one.
    /// </summary>
    private const int SweepFoldPasses = 12;

    /// <summary>
    /// Flattens out the places a ring doubles back on itself, in place. Returns how many points had
    /// to be moved.
    ///
    /// <para>
    /// Points are pulled onto the midpoint of their neighbours rather than deleted, which is what the
    /// planar path does with a footprint crossing. Deleting is not available here: the stitching pairs
    /// point i of one ring with point i of the next, so the count has to hold all the way out.
    /// </para>
    /// </summary>
    private static int RepairFolds(Vector3[] ring)
    {
        int n = ring.Length;
        if (n < 6) return 0;

        double limit = Math.Cos(SweepFoldAngleDeg * Math.PI / 180.0);
        int repaired = 0;

        for (int pass = 0; pass < SweepFoldPasses; pass++)
        {
            bool anyFolded = false;
            for (int i = 0; i < n; i++)
            {
                var before = ring[(i - 1 + n) % n];
                var after = ring[(i + 1) % n];

                var incoming = ring[i] - before;
                var outgoing = after - ring[i];
                double lengthIn = incoming.Length, lengthOut = outgoing.Length;

                // A collapsed segment has no direction to judge, and is itself a fold in the limit -
                // two points of the ring have arrived at the same place.
                bool folded = lengthIn < 1e-5 || lengthOut < 1e-5
                    || (incoming / lengthIn).Dot(outgoing / lengthOut) < limit;

                if (!folded) continue;

                ring[i] = (before + after) * 0.5;
                anyFolded = true;
                repaired++;
            }

            if (!anyFolded) break;
        }

        return repaired;
    }

    /// <summary>Passes of direction averaging per ring.</summary>
    private const int SweepDirectionSmoothing = 2;

    /// <summary>
    /// Averages each marching direction with its neighbours around the ring, renormalising after, so
    /// the ring advances as a front rather than as a row of independently aimed points. Directions
    /// belonging to points that have stopped are left out of the average - they are no longer part of
    /// the front and their last heading is stale.
    /// </summary>
    private static void SmoothDirections(Vector3[] directions, bool[] marching)
    {
        int n = directions.Length;
        if (n < 4) return;

        for (int pass = 0; pass < SweepDirectionSmoothing; pass++)
        {
            var blended = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                var sum = directions[i] * 4.0;
                
                // Immediate neighbors
                foreach (int j in new[] { (i - 1 + n) % n, (i + 1) % n })
                    if (marching[j]) sum += directions[j] * 3.0;
                    
                // +/- 2
                foreach (int j in new[] { (i - 2 + n) % n, (i + 2) % n })
                    if (marching[j]) sum += directions[j] * 2.0;
                    
                // +/- 3
                foreach (int j in new[] { (i - 3 + n) % n, (i + 3) % n })
                    if (marching[j]) sum += directions[j];

                blended[i] = sum.LengthSquared > 1e-12 ? sum.Normalize() : directions[i];
            }

            Array.Copy(blended, directions, n);
        }
    }


    private static (Vector3 Min, Vector3 Max) Bounds(IReadOnlyList<Vector3> points)
    {
        var min = new Vector3(double.MaxValue, double.MaxValue, double.MaxValue);
        var max = new Vector3(double.MinValue, double.MinValue, double.MinValue);
        foreach (var p in points)
        {
            min = min.ComponentMin(p);
            max = max.ComponentMax(p);
        }
        return (min, max);
    }

    private static bool Outside(Vector3 point, Vector3 min, Vector3 max) =>
        point.X < min.X || point.X > max.X ||
        point.Y < min.Y || point.Y > max.Y ||
        point.Z < min.Z || point.Z > max.Z;

    /// <summary>The ring seen looking along <paramref name="axis"/>, for the stop test.</summary>
    private static Vector2[] Flatten(Vector3[] ring, Vector3 axis)
    {
        var flattened = new Vector2[ring.Length];
        for (int i = 0; i < ring.Length; i++)
            flattened[i] = PartingFrame.ToPlane(ring[i], axis);

        return flattened;
    }

    /// <summary>
    /// Sews consecutive rings into a triangle strip. Every ring carries the same point count and the
    /// same correspondence - point i of one ring marched from point i of the last - so the stitching
    /// is a quad grid with no matching to work out.
    /// </summary>
    private Result<IMesh> StitchRings(List<Vector3[]> rings, MeshMetadata metadata)
    {
        if (rings.Count < 2) return MeshErrors.InvalidMesh;

        int n = rings[0].Length;
        var vertices = new double[rings.Count * n * 3];
        for (int r = 0; r < rings.Count; r++)
        {
            for (int i = 0; i < n; i++)
            {
                int at = ((r * n) + i) * 3;
                vertices[at] = rings[r][i].X;
                vertices[at + 1] = rings[r][i].Y;
                vertices[at + 2] = rings[r][i].Z;
            }
        }

        var triangles = new List<int>((rings.Count - 1) * n * 6);
        for (int r = 0; r + 1 < rings.Count; r++)
            StitchPair(triangles, rings[r], rings[r + 1], r * n, (r + 1) * n);

        var mesh = CreateMesh(vertices.AsSpan(), CollectionsMarshal.AsSpan(triangles));
        return mesh.IsSuccess ? Result.Success(mesh.Value.WithMetadata(metadata)) : mesh;

        // Walks the two rings together, at each step advancing whichever one leaves the shorter
        // diagonal - the standard way to sew two closed contours.
        //
        // Pairing point i of one ring with point i of the next, which is what this did, is only right
        // while the two rings are indexed the same way round. They are not: every ring is respaced
        // after it is marched, which slides all its points along it, so index i drifts further from
        // its true continuation the further out the sweep goes. The quads then span sideways across
        // the band and cross each other - a twist that no amount of per-ring repair could find,
        // because neither ring is wrong on its own.
        static void StitchPair(List<int> into, Vector3[] inner, Vector3[] outer, int innerBase, int outerBase)
        {
            int n = inner.Length;

            // Where on the outer ring the inner ring's first point actually continues to.
            int start = 0;
            double best = double.MaxValue;
            for (int j = 0; j < n; j++)
            {
                double d = inner[0].DistanceSquared(outer[j]);
                if (d >= best) continue;
                best = d;
                start = j;
            }

            int i = 0, k = 0;
            while (i < n || k < n)
            {
                int ii = i % n, kk = (start + k) % n;
                int nextI = (i + 1) % n, nextK = (start + k + 1) % n;

                bool advanceInner = k >= n
                    || (i < n && inner[nextI].DistanceSquared(outer[kk])
                             <= inner[ii].DistanceSquared(outer[nextK]));

                if (advanceInner)
                {
                    Emit(into, inner, outer, innerBase, outerBase, ii, kk, innerBase + nextI, inner[nextI]);
                    i++;
                }
                else
                {
                    Emit(into, inner, outer, innerBase, outerBase, ii, kk, outerBase + nextK, outer[nextK]);
                    k++;
                }
            }
        }

        // A face with no area is what the points that have stopped marching produce, since they
        // repeat unchanged from one ring to the next - and it is exactly what the mould boolean
        // refuses to cut with, so it is dropped rather than emitted.
        static void Emit(
            List<int> into, Vector3[] inner, Vector3[] outer, int innerBase, int outerBase,
            int ii, int kk, int third, Vector3 thirdPoint)
        {
            if ((outer[kk] - inner[ii]).Cross(thirdPoint - inner[ii]).LengthSquared < 1e-14)
                return;

            into.Add(innerBase + ii);
            into.Add(third);
            into.Add(outerBase + kk);
        }
    }

    /// <summary>
    /// Steepest launch the flange is allowed to take off the parting line, as a rise over the
    /// distance travelled outward. The body's normal at the rim can point almost straight along the
    /// pull axis - the rim is the wall of an extrusion, and where that wall is undercut its normal
    /// tips right over - and continuing such a slope would send the first ring far above the rest of
    /// the flange. One-to-one is a 45 degree launch, already at the limit of what the overhang pass
    /// downstream is willing to leave standing.
    /// </summary>
    private const double MaxLaunchSlope = 50.0;

    /// <summary>
    /// How far out from the parting line, in mm, the body's slope is carried before the flange is
    /// left to relax to level.
    ///
    /// <para>
    /// Has to be a distance rather than a ring count. The rings start at the parting line's own point
    /// spacing and widen from there, so holding "the first ring" holds a band about two millimetres
    /// wide - measured on chin and scalp that moved the flange 0.12mm on average and 1.8mm at most,
    /// which is nothing against the 35mm the rim itself swings. 15mm is wide enough to set the
    /// direction the flange leaves in and still well inside the outer rim.
    /// </para>
    /// </summary>
    private const double LaunchHoldMm = 15.0;

    /// <summary>
    /// Rise per unit of outward travel implied by a launch direction in the local frame, where local
    /// Z is the pull axis. A direction with almost no in-plane component would divide by nearly
    /// nothing, so the result is clamped rather than the input rejected.
    /// </summary>
    /// <summary>
    /// The rise-per-outward-mm each point of the parting line implies, averaged around the loop
    /// <paramref name="passes"/> times. Null in, null out - a build with no launch surface has no
    /// slopes to smooth.
    ///
    /// <para>
    /// Smoothing runs on the slope rather than on the direction vector because the slope is the
    /// quantity that reaches the surface: two normals can differ a lot in 3D and still imply nearly
    /// the same rise, and averaging the vectors would blur that difference the wrong way round. A
    /// [1,2,1] kernel per pass, wrapped, so the loop stays closed.
    /// </para>
    /// </summary>
    /// <summary>
    /// Averages the flange's heights along each contour, in place: every vertex is blended toward the
    /// two nearest vertices on its own ring, never toward the rings inside or outside it. Ring zero -
    /// the parting line - is left alone.
    ///
    /// <para>
    /// This is the pass that takes the corrugation out without undoing the normal-following, and it
    /// works because the two live on different axes. Ripples run ALONG the contours; the slope that
    /// follows the normals runs ACROSS them. An ordinary Laplacian over the triangulation cannot tell
    /// them apart - it averages a vertex against its neighbours around the ring and across it in one
    /// go, so it flattens the radial slope while it is smoothing the ripple, which is why holding the
    /// launch meant pinning every vertex and disabling it entirely. Restricting each average to the
    /// vertex's own ring removes that conflict: the surface can be made laterally coherent and still
    /// leave the line at whatever angle the normals ask for.
    /// </para>
    /// </summary>
    private static void SmoothHeightsAlongContours(
        Vector3[] positions, int[] ringIndices, int vertCount, int passes)
    {
        if (passes <= 0) return;

        var byRing = new Dictionary<int, List<int>>();
        for (int i = 0; i < vertCount; i++)
        {
            if (ringIndices[i] <= 0) continue; // the parting line is the one fixed thing
            if (!byRing.TryGetValue(ringIndices[i], out var list))
                byRing[ringIndices[i]] = list = new List<int>();
            list.Add(i);
        }

        // Two nearest on the same ring, taken in the footprint so "along the contour" means what it
        // says whatever height the vertices have reached.
        var neighbours = new int[vertCount][];
        foreach (var members in byRing.Values)
        {
            foreach (int i in members)
            {
                int n1 = -1, n2 = -1;
                double d1 = double.MaxValue, d2 = double.MaxValue;
                var pi = new Vector2(positions[i].X, positions[i].Y);

                foreach (int j in members)
                {
                    if (j == i) continue;
                    double d = pi.DistanceSquared(new Vector2(positions[j].X, positions[j].Y));
                    if (d < d1) { d2 = d1; n2 = n1; d1 = d; n1 = j; }
                    else if (d < d2) { d2 = d; n2 = j; }
                }

                neighbours[i] = n2 >= 0 ? [n1, n2] : n1 >= 0 ? [n1] : [];
            }
        }

        var blended = new double[vertCount];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < vertCount; i++) blended[i] = positions[i].Z;

            foreach (var members in byRing.Values)
            {
                foreach (int i in members)
                {
                    var nb = neighbours[i];
                    if (nb is null || nb.Length == 0) continue;

                    double sum = 0.0;
                    foreach (int j in nb) sum += positions[j].Z;
                    blended[i] = (positions[i].Z + (sum / nb.Length)) * 0.5;
                }
            }

            for (int i = 0; i < vertCount; i++) positions[i] = positions[i] with { Z = blended[i] };
        }
    }

    private static double[]? SmoothedLaunchSlopes(Vector3[]? launchLocal, int passes)
    {
        if (launchLocal is null) return null;

        int n = launchLocal.Length;
        var slopes = new double[n];
        for (int i = 0; i < n; i++) slopes[i] = LaunchSlope(launchLocal[i]);
        if (n < 3) return slopes;

        var blended = new double[n];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < n; i++)
                blended[i] = (slopes[(i - 1 + n) % n] + (slopes[i] * 2.0) + slopes[(i + 1) % n]) * 0.25;

            Array.Copy(blended, slopes, n);
        }

        return slopes;
    }

    private static double LaunchSlope(Vector3 launch)
    {
        double inPlane = new Vector2(launch.X, launch.Y).Length;
        if (inPlane < 1e-4) return launch.Z >= 0.0 ? MaxLaunchSlope : -MaxLaunchSlope;

        return Math.Clamp(launch.Z / inPlane, -MaxLaunchSlope, MaxLaunchSlope);
    }

    /// <summary>
    /// The body's outward surface normal at each point of the parting line, expressed in the local
    /// frame the flange is built in. This is the direction "straight out of the body" at the rim,
    /// which is what both new sweeps launch along.
    /// </summary>
    private Vector3[] LocalLaunchDirections(
        IReadOnlyList<Vector3> loop, IMesh body, Rotation inverseRotation)
    {
        var directions = new Vector3[loop.Count];
        var world = OutwardNormalsAlong(loop, body);
        for (int i = 0; i < loop.Count; i++)
            directions[i] = inverseRotation.Apply(world[i]);

        return directions;
    }

    /// <summary>
    /// Outward unit normal of <paramref name="body"/> at each point of <paramref name="loop"/>, taken
    /// from the face the point projects onto and smoothed along the loop.
    ///
    /// <para>
    /// Smoothed because a raw per-face normal is piecewise constant: neighbouring points of the line
    /// often land on different faces of a coarse body and get normals tens of degrees apart, and a
    /// flange launched along those would leave the rim in a fan rather than a surface. Averaging
    /// along the loop costs nothing here and is what makes the launch continuous.
    /// </para>
    /// </summary>
    private Vector3[] OutwardNormalsAlong(IReadOnlyList<Vector3> loop, IMesh body)
    {
        int n = loop.Count;

        // The same normals the view draws along the line - see SmoothNormalsAt. This used to take the
        // raw normal of whichever single face the point projected onto, which on a rim that is a
        // crease is a different answer by up to a right angle, and it was the direction the flange
        // actually left in. The arrows on screen said one thing and the flange did another.
        // Only fails where the engine cannot index the body at all, which nothing before this survives.
        var normals = SmoothNormalsAt(body, loop).Value.ToArray();

        // Two smoothing passes along the loop, then re-normalize.
        for (int pass = 0; pass < 2; pass++)
        {
            var blended = new Vector3[n];
            for (int i = 0; i < n; i++)
                blended[i] = normals[(i - 1 + n) % n] + (normals[i] * 2.0) + normals[(i + 1) % n];
            for (int i = 0; i < n; i++)
                normals[i] = blended[i].LengthSquared > 1e-12
                    ? blended[i].Normalize() : normals[i];
        }

        return normals;
    }

    // --- Private Wavefront Offsetting & Inside-Out Helpers ---

    /// <summary>
    /// How fast the ring step is allowed to grow from one ring to the next while it is still ramping
    /// up to <c>stepMm</c>. Each ring is at most this much wider than the one inside it, so the
    /// triangles across consecutive bands change size gradually instead of in one jump.
    /// </summary>
    private const double RingGrowth = 1.6;

    /// <summary>
    /// Builds the wavefront rings, and this is where the flange's triangle quality is decided.
    ///
    /// <para>
    /// Every ring is resampled to an arc-length spacing equal to the step that produced it. That is
    /// the whole trick: the band between two rings is <em>stepMm</em> wide radially, so spacing its
    /// points <em>stepMm</em> apart tangentially makes the triangles stitched across it equilateral.
    /// A ring left at whatever density Clipper emitted gives slivers instead - the offsetter lays
    /// round joins down as dense arcs, and it compounds, since each ring is offset from the last.
    /// </para>
    ///
    /// <para>
    /// The step ramps rather than starting at <paramref name="stepMm"/>, because ring 0 is the
    /// parting line and arrives at whatever spacing the tracer left it at - a couple of millimetres. Measured on the traced line, the largest turn between neighbouring points falls from 119 degrees taking the face underneath, to 55 at 0.03, to under 45 here.
    /// well under the step. Jumping straight to the full step would leave the innermost band
    /// stretched by that ratio, and that band alone was over 8% of the flange's faces as slivers.
    /// Starting at the parting line's own spacing and growing by <see cref="RingGrowth"/> per ring
    /// costs two or three extra rings and removes them.
    /// </para>
    /// </summary>
    private Result<List<List<Vector2[]>>> GenerateIterativeRibbons(
        Vector2[] inner2D,
        IReadOnlyList<Vector2> boundary,
        double stepMm,
        int maxRings)
    {
        var layers = new List<List<Vector2[]>>(maxRings);

        // Layer 0 is our starting anatomy loop
        var currentIslands = new List<Vector2[]> { inner2D };

        // Ramp from the parting line's own point spacing up to the requested step.
        double step = Math.Clamp(MedianSpacing(inner2D), 0.25, stepMm);

        for (int ring = 1; ring <= maxRings; ring++)
        {
            var nextIslands = new List<Vector2[]>();

            foreach (var island in currentIslands)
            {
                // Fixed-step outward offset from the PREVIOUS ring. No constraining/clipping against
                // the boundary - the wavefront is allowed to grow freely past it.
                var offsetResult = GenerateConstrainedOffset(island, step);
                if (offsetResult.IsSuccess)
                    nextIslands.Add(ResampleRing(offsetResult.Value, step));
            }

            if (nextIslands.Count == 0)
                break; // Offsetting collapsed - nothing more to add.

            layers.Add(nextIslands);
            currentIslands = nextIslands;

            // Stop as soon as every island of this ring lies entirely outside the boundary. That ring
            // fully encloses the boundary (the flange covers the whole footprint and spills a little
            // past it) and becomes the outer edge - no box, no extension, no clipping.
            if (nextIslands.All(isl => IsEntirelyOutside(isl, boundary)))
                break;

            step = Math.Min(step * RingGrowth, stepMm);
        }

        return Result.Success(layers);
    }

    /// <summary>Median edge length of a closed contour - its typical point spacing.</summary>
    private static double MedianSpacing(IReadOnlyList<Vector2> contour)
    {
        int n = contour.Count;
        if (n < 2) return 0.0;

        var lengths = new double[n];
        for (int i = 0; i < n; i++)
            lengths[i] = contour[i].DistanceTo(contour[(i + 1) % n]);

        Array.Sort(lengths);
        return lengths[n / 2];
    }

    /// <summary>
    /// Resamples a closed contour to a uniform arc-length spacing. Below eight points there is no
    /// shape left to preserve, so a contour that short is passed through untouched.
    /// </summary>
    private static Vector2[] ResampleRing(IReadOnlyList<Vector2> contour, double spacingMm)
    {
        int n = contour.Count;
        if (n < 8 || spacingMm <= 1e-4) return contour.ToArray();

        var cumulative = new double[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + contour[i].DistanceTo(contour[(i + 1) % n]);

        double perimeter = cumulative[n];
        if (perimeter < 1e-4) return contour.ToArray();

        int count = Math.Clamp((int)Math.Round(perimeter / spacingMm), 8, 20000);
        var result = new Vector2[count];

        int segment = 0;
        for (int k = 0; k < count; k++)
        {
            double target = perimeter * k / count;
            while (segment < n - 1 && cumulative[segment + 1] < target) segment++;

            double span = cumulative[segment + 1] - cumulative[segment];
            double t = span > 1e-6 ? Math.Clamp((target - cumulative[segment]) / span, 0.0, 1.0) : 0.0;
            result[k] = contour[segment].LerpTo(contour[(segment + 1) % n], t);
        }

        return result;
    }

    /// <summary>
    /// Offsets <paramref name="contour"/> by <paramref name="offsetMm"/> (negative shrinks), falling
    /// back to the contour itself if Clipper collapses it. A collapse only costs the bleed margin on
    /// that edge, which is not worth failing the whole flange over.
    /// </summary>
    private static Vector2[] OffsetOrOriginal(IReadOnlyList<Vector2> contour, double offsetMm)
    {
        var result = GenerateConstrainedOffset(contour, offsetMm);
        return result.IsSuccess ? result.Value.ToArray() : contour.ToArray();
    }

    /// <summary>True when no vertex of <paramref name="ring"/> lies inside <paramref name="boundary"/>.</summary>
    private static bool IsEntirelyOutside(IReadOnlyList<Vector2> ring, IReadOnlyList<Vector2> boundary)
    {
        for (int i = 0; i < ring.Count; i++)
        {
            if (ContainsPoint(boundary, ring[i]))
                return false;
        }
        return true;
    }

    private Result<IMesh> LiftWavefrontToWorldSpace(
        PlanarTriangulation triangulation,
        Vector3[] anatomy3D,
        Vector2[] anatomy2D,
        List<List<Vector2[]>> ribbonLayers,
        Vector2[] innerBleed,
        double concaveBandWidthMm,
        Rotation worldRotation,
        Vector3[]? launchLocal,
        double launchHoldMm,
        bool rawFlange,
        int launchSmoothingPasses,
        out bool[]? launchedVertices)
    {
        var pts = triangulation.Points;
        int vertCount = pts.Length;

        var localPositions = new Vector3[vertCount];
        launchedVertices = launchLocal is null ? null : new bool[vertCount];
        var launched = launchedVertices;
        var ringIndices = new int[vertCount];
        var idToIndex = new int[vertCount];
        int currentIndex = 0;

        // Ring layout: Anatomy = 0, Ribbons = 1..N (the outermost ribbon is the flange's edge).
        int totalLayers = ribbonLayers.Count; // == N

        // The launch slope for every point of the parting line, smoothed around the loop before any
        // of it is applied.
        //
        // This is what stops the flange corrugating. Each point's slope comes from its own normal, and
        // on a rim that is a crease those disagree sharply from one point to the next - measured on
        // scalp, neighbouring points ask the flange to leave at angles 8.9 degrees apart on average
        // and 40 at worst, over 189 points about a millimetre apart. Held rigidly that difference is
        // printed straight into the surface as ripples, and holding it is exactly what following the
        // normals over the whole flange does, because every launched vertex is pinned against the
        // height relaxation that used to absorb it.
        //
        // Smoothing the slopes rather than the finished surface is what keeps the two apart: the
        // corrugation is variation ALONG the loop, the normal-following is variation ACROSS it, so
        // averaging around the loop removes the first and leaves the second. And because every ring's
        // vertices take their slope from the nearest parting point, smoothing here smooths every
        // contour at once instead of ring by ring.
        var launchSlopes = SmoothedLaunchSlopes(launchLocal, launchSmoothingPasses);

        // 1. Assign Ring Indices to every triangulated vertex
        for (int i = 0; i < vertCount; i++)
        {
            var v2 = pts[i];

            int assignedRing = IdentifyVertexRingIndex(v2, anatomy2D, ribbonLayers);
            ringIndices[currentIndex] = assignedRing;

            // Initialize Ring 0 strictly to exact 3D patient anatomy position (including Y/Z height!)
            double startZ = 0.0;
            if (assignedRing == 0)
            {
                int closestAnatomyIdx = FindClosestIndex(v2, anatomy2D);
                startZ = anatomy3D[closestAnatomyIdx].Z;
            }
            else if (assignedRing >= 1 && launchLocal is not null
                     && v2.DistanceTo(anatomy2D[FindClosestIndex(v2, anatomy2D)]) < launchHoldMm)
            {
                // Carried out along the body's own surface direction rather than left to the
                // relaxation. The relaxation flattens the flange as soon as it leaves the line, so
                // the surface departs the rim in whatever direction the global plane dictates -
                // which is the twist. Continuing the body's slope for one ring makes it leave going
                // the way the body was going, and the rings past this one still relax to level.
                int closestAnatomyIdx = FindClosestIndex(v2, anatomy2D);
                startZ = anatomy3D[closestAnatomyIdx].Z
                       + (launchSlopes![closestAnatomyIdx]
                          * v2.DistanceTo(anatomy2D[closestAnatomyIdx]));
                launched![currentIndex] = true;
            }

            localPositions[currentIndex] = new Vector3(v2.X, v2.Y, startZ);
            idToIndex[i] = currentIndex++;
        }

        // 2. INSIDE-OUT PASS 1: Propagate pull-axis heights outward, ring by ring, so every vertex
        // starts near the surface height its inner neighbour reached. This is only an initial guess
        // that seeds the relaxation below; the smoothing pass is what actually shapes the transition.
        for (int targetRing = 1; targetRing <= totalLayers; targetRing++)
        {
            PropagateWavefrontHeights(localPositions, ringIndices, currentIndex, targetRing, launched);
        }

        // Ripples out, slope kept - see SmoothHeightsAlongContours. Runs before the band is carved and
        // before any pinning, because it is not repairing the surface, it is finishing the lift: the
        // heights it averages are the ones the launch just laid down.
        SmoothHeightsAlongContours(localPositions, ringIndices, currentIndex, launchSmoothingPasses);

        // 3. Extract topology triangles, keeping only the band BETWEEN the contours: inside the
        // outermost wavefront layer and outside the inward-bled parting line. The outer edge is the
        // wavefront ring itself - no bleed guard, since nothing has to meet it. That layer can be
        // several islands (the wavefront fragments around concavities), so every island of it bounds
        // the flange, not just the largest.
        var outerRings = totalLayers > 0
            ? ribbonLayers[totalLayers - 1]
            : new List<Vector2[]>();

        var filteredTriangles = ExtractBandTriangles(
            triangulation, idToIndex,
            innerContours: [innerBleed],
            outerContours: outerRings);

        // 3b. Concave-notch masking is disabled for now. Its convex-hull pocket test flagged every
        // gently-recessed stretch of the parting line (i.e. most of an anatomical loop) as a pocket, so
        // with the default band (3 mm) narrower than the ribbon step (7.5 mm) it stripped the outer half
        // of the first offset band and disconnected the flange from the parting line. Keep the full
        // flange - including any webs across concave notches - until the pocket detector is reworked.
        // See MaskConcaveNotchWebs (still present) to re-enable.

        // 4. Pin only the inner boundary, then relax everything else into a smooth membrane.
        // The sole fixed constraint is the inner anatomy ring (Ring 0), locked to its true 3D
        // parting-line height. Every other vertex floats. A Laplacian pass on the pull-axis height
        // alone then lets the surface ramp gradually outward from the undulating anatomy; with a free
        // outer boundary the far field relaxes toward a level continuation on its own, so the rim
        // flattens without being pinned and there are no hard height steps (and therefore no
        // near-vertical, 90-degree-to-Z triangles). XY is never touched, so ring footprints and offsets
        // are preserved exactly.
        var pinned = new bool[currentIndex];
        for (int i = 0; i < currentIndex; i++)
        {
            if (ringIndices[i] == 0)
                pinned[i] = true; // inner anatomy follows the parting line exactly

            // The launched band is held too, otherwise the relaxation simply undoes it on its first
            // pass and the flange leaves the rim exactly as it did before. Recovered from the height
            // rather than re-derived: LaunchedHeights marks what it set.
            if (launched is not null && launched[i])
                pinned[i] = true;
        }

        // Smoothing strength ramps with distance from the parting line. A single uniform factor has to
        // serve two opposing needs at once: the inner rings must hold the anatomy's undulation (smooth
        // them hard and the flange pulls away from the parting line), while the outer rings want to
        // flatten out. Splitting the difference is what leaves the creases - PropagateWavefrontHeights
        // seeds each ring by inverse-distance-weighting its three nearest parents, which is piecewise
        // and lays down ridges, and a mid-strength uniform pass does not fully relax them before it
        // runs out of iterations.
        //
        // So the factor is interpolated from InnerSmoothingFactor at ring 1 to OuterSmoothingFactor at
        // the outermost ring, on a smoothstep so there is no visible band where the rate jumps. Higher
        // factor is more relaxation per pass, so the far field converges toward its harmonic (crease-
        // free) limit within the iteration budget while the inner band stays faithful.
        var smoothingFactors = new double[currentIndex];
        double ringSpan = Math.Max(1, totalLayers);
        for (int i = 0; i < currentIndex; i++)
        {
            double t = Math.Clamp(ringIndices[i] / ringSpan, 0.0, 1.0);
            double eased = t * t * (3.0 - 2.0 * t); // smoothstep
            smoothingFactors[i] = InnerSmoothingFactor + (OuterSmoothingFactor - InnerSmoothingFactor) * eased;
        }

        // The height relaxation is itself post-processing - it is what pulls the surface toward a
        // smooth membrane and away from whatever the rings were given - so raw skips it too.
        if (!rawFlange)
        {
            SmoothFlangeHeights(
                localPositions, filteredTriangles, pinned, currentIndex,
                iterations: 60, factor: OuterSmoothingFactor, perVertexFactor: smoothingFactors);
        }

        // Note: overhang (>45-degree slope) cleanup is deliberately NOT done here. A height-Laplacian
        // over this triangulation averages a vertex against its neighbours around the same ring as
        // much as across rings, so it cannot reduce the radial slope, which is the one that matters.
        // It is done afterwards by a pass that works on edges directly - see
        // GenerateWavefrontFlangeMesh step 7 / RelaxSteepSlopesWorld.

        // 5. Un-project from Local +Z frame back to World Space (maps Local Z back to World Y!)
        var worldVertices = new double[currentIndex * 3];
        for (int i = 0; i < currentIndex; i++)
        {
            var worldV3 = worldRotation.Apply(localPositions[i]);
            int idx3 = i * 3;
            worldVertices[idx3] = worldV3.X;
            worldVertices[idx3 + 1] = worldV3.Y;
            worldVertices[idx3 + 2] = worldV3.Z;
        }

        return CreateMesh(worldVertices.AsSpan(), CollectionsMarshal.AsSpan(filteredTriangles));
    }

    private static void PropagateWavefrontHeights(
        Vector3[] positions,
        int[] ringIndices,
        int vertCount,
        int currentRing,
        bool[]? keep = null)
    {
        // Collect all available parent vertices from the immediately preceding ring (currentRing - 1)
        var parentIndices = new List<int>();
        for (int i = 0; i < vertCount; i++)
        {
            if (ringIndices[i] == currentRing - 1)
                parentIndices.Add(i);
        }
        if (parentIndices.Count == 0)
            return;

        // For each vertex on the current ring, average the Y-heights of the 3 nearest parent vertices
        Span<(double distSq, double zHeight)> nearest = stackalloc (double, double)[3];

        for (int i = 0; i < vertCount; i++)
        {
            if (ringIndices[i] != currentRing)
                continue;

            // A vertex whose height was already set deliberately keeps it, and still serves as a
            // parent for the ring beyond - which is how the launch direction carries outward instead
            // of being flattened at the first ring. Without this the propagation overwrote the
            // launched band unconditionally, before the pinning below ever saw it, which is why the
            // launch hold distance measured identical at 15mm and at 1000mm.
            if (keep is not null && keep[i])
                continue;

            for (int k = 0; k < 3; k++)
                nearest[k] = (double.MaxValue, 0.0);
            var v2 = new Vector2(positions[i].X, positions[i].Y);

            for (int p = 0; p < parentIndices.Count; p++)
            {
                int parentIdx = parentIndices[p];
                var parentV2 = new Vector2(positions[parentIdx].X, positions[parentIdx].Y);
                double dSq = v2.DistanceSquared(parentV2);

                if (dSq < nearest[2].distSq)
                {
                    nearest[2] = (dSq, positions[parentIdx].Z);
                    for (int j = 2; j > 0 && nearest[j].distSq < nearest[j - 1].distSq; j--)
                    {
                        var temp = nearest[j];
                        nearest[j] = nearest[j - 1];
                        nearest[j - 1] = temp;
                    }
                }
            }

            // Calculate Inverse Distance Weighted average of the nearest parents from Ring (k-1)
            double totalWeight = 0.0;
            double weightedZ = 0.0;
            for (int k = 0; k < 3; k++)
            {
                if (nearest[k].distSq == double.MaxValue)
                    continue;
                double weight = 1.0 / (double)Math.Sqrt(Math.Max(1e-5, nearest[k].distSq));
                weightedZ += nearest[k].zHeight * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0.0)
            {
                positions[i] = positions[i] with { Z = weightedZ / totalWeight };
            }
        }
    }

    /// <summary>
    /// Jacobi Laplacian smoothing of the pull-axis height (local Z) over the flange triangulation.
    /// Pinned vertices (the inner anatomy ring and the outer sealing rim) are held fixed; every free
    /// vertex is eased toward the average height of its topological neighbours. Only Z changes - the
    /// in-plane XY from the 2D triangulation is preserved exactly, so ring footprints/offsets are not
    /// disturbed. Relaxing between the two fixed boundaries yields a gradual height ramp, which is what
    /// keeps triangle normals off the 90-degree-to-pull orientation that breaks printing.
    ///
    /// <paramref name="perVertexFactor"/> overrides <paramref name="factor"/> per vertex, so relaxation
    /// strength can vary across the surface - the wavefront flange ramps it up with distance from the
    /// parting line, holding the anatomy near the line while flattening the rim. Null applies
    /// <paramref name="factor"/> uniformly.
    /// </summary>
    private static void SmoothFlangeHeights(
        Vector3[] positions,
        List<int> triangles,
        bool[] pinned,
        int vertCount,
        int iterations,
        double factor,
        double[]? perVertexFactor = null)
    {
        // Build unique vertex adjacency from the final (hole-filtered) triangle topology.
        var adjacency = new List<int>[vertCount];
        for (int i = 0; i < vertCount; i++)
            adjacency[i] = new List<int>(6);

        void Link(int a, int b)
        {
            if (a != b && !adjacency[a].Contains(b))
                adjacency[a].Add(b);
        }

        for (int t = 0; t + 2 < triangles.Count; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            Link(a, b);
            Link(a, c);
            Link(b, a);
            Link(b, c);
            Link(c, a);
            Link(c, b);
        }

        var newZ = new double[vertCount];
        for (int pass = 0; pass < iterations; pass++)
        {
            for (int i = 0; i < vertCount; i++)
            {
                var nbrs = adjacency[i];
                if (pinned[i] || nbrs.Count == 0)
                {
                    newZ[i] = positions[i].Z;
                    continue;
                }

                double sum = 0.0;
                for (int j = 0; j < nbrs.Count; j++)
                    sum += positions[nbrs[j]].Z;

                double target = sum / nbrs.Count;
                double f = perVertexFactor is null ? factor : perVertexFactor[i];
                newZ[i] = positions[i].Z + f * (target - positions[i].Z);
            }

            for (int i = 0; i < vertCount; i++)
                positions[i] = positions[i] with { Z = newZ[i] };
        }
    }

    /// <summary>
    /// Overhang-reduction pass. After the general height relaxation,
    /// interior bands of the flange still fall faster than <paramref name="maxSlopeDeg"/> from
    /// horizontal - steep, print-unfriendly walls, concentrated in the low stretches where the parting
    /// line plunges (the "bottom"). Plain Laplacian smoothing can't fix these: a constant-slope ramp is
    /// harmonic, so it equals its own neighbour average and doesn't move. Instead this caps slope
    /// directly, thermal-erosion style: every edge steeper than the limit has its endpoints' heights
    /// pulled together (along <paramref name="heightAxis"/>) until it just meets the limit, iterated to a
    /// near-fixed point. That flattens the wall by spreading its height change out across the flange
    /// width. Boundary vertices (the inner parting edge - which keeps the seal - and the outer rim) are
    /// held fixed; only the height component moves, so the XY footprint is preserved.
    /// Returns the input unchanged on any failure.
    /// </summary>
    private Result<IMesh> RelaxSteepSlopesWorld(
        IMesh mesh,
        Vector3 heightAxis,
        double maxSlopeDeg,
        int iterations,
        double rate,
        double innerHold,
        bool[]? pinnedVerts = null)
    {
        try
        {
            var axis = heightAxis.Normalize();
            var verts = mesh.Vertices;
            var tris = mesh.Triangles;
            int n = verts.Length;
            if (n == 0 || tris.Length < 3)
                return Result.Success(mesh);

            // Decompose each vertex into a height along the axis and an in-plane residual.
            var height = new double[n];
            var planar = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                height[i] = verts[i].Dot(axis);
                planar[i] = verts[i] - height[i] * axis;
            }

            // Boundary detection: an edge used by a single triangle is a border edge. The flange is an
            // annulus, so its border is two loops - the inner one around the parting hole and the outer
            // rim. Only the INNER loop is pinned: it carries the seal against the mould, so its height
            // must stay on the parting line. The outer rim is left free in height (its XY footprint is
            // preserved regardless, since this pass only ever moves the height component). Pinning both
            // would fix the total height drop across a fixed-width strip, making the average radial
            // slope un-reducible; freeing the rim lets it rise toward the inner edge so a steep strip
            // relaxes to a gentle ramp.
            var edgeUse = new Dictionary<(int, int), int>();
            void CountEdge(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                edgeUse[key] = edgeUse.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                CountEdge(a, b); CountEdge(b, c); CountEdge(c, a);
            }

            var boundary = new List<int>();
            foreach (var kv in edgeUse)
            {
                if (kv.Value == 1)
                {
                    boundary.Add(kv.Key.Item1);
                    boundary.Add(kv.Key.Item2);
                }
            }

            // Classify border vertices by in-plane radius from the footprint centroid: the inner loop
            // sits at the smaller radius. Split at the midpoint between the closest and farthest border
            // vertex (the annulus leaves a clear gap between the two loops).
            var innerBoundary = new bool[n];
            if (boundary.Count > 0)
            {
                var centroid = Vector3.Zero;
                for (int i = 0; i < n; i++)
                    centroid += planar[i];
                centroid /= n;

                double minR = double.MaxValue, maxR = 0.0;
                foreach (int bvi in boundary)
                {
                    double r = planar[bvi].DistanceTo(centroid);
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;
                }
                double split = 0.5 * (minR + maxR);

                foreach (int bvi in boundary)
                    if (planar[bvi].DistanceTo(centroid) < split)
                        innerBoundary[bvi] = true; // inner (parting) loop
            }

            // The inner edge is not hard-pinned: a steep plunge in the parting line itself forces steep
            // faces around a pinned edge that no interior move can relax. Instead every vertex is free to
            // slope-cap, and after each pass the inner edge is sprung back toward its original parting
            // height by innerHold. innerHold == 1 holds the seal exactly (steep faces near a plunge
            // survive); lower values let the seal edge ease a little to shed those faces. h0 is that
            // original height field; the outer rim is left fully free (its XY footprint is preserved
            // regardless, as only the height component ever moves).
            var h0 = (double[])height.Clone();
            var noPins = pinnedVerts != null ? (bool[])pinnedVerts.Clone() : new bool[n];

            double tanLimit = Math.Tan(maxSlopeDeg * Math.PI / 180.0);
            int faceCount = tris.Length / 3;
            var delta = new double[n];
            var count = new int[n];

            // Jacobi slope capping on faces (the quantity actually measured as overhang). Each pass, for
            // every face whose in-plane height gradient exceeds the limit, shrink its three vertices'
            // heights toward the face mean by exactly the factor that brings the gradient down to the
            // limit; accumulate those targets per vertex and apply the damped average. Averaging per
            // vertex (rather than applying each face in place) is what keeps a vertex shared by several
            // steep faces from being over-corrected into a new spike.
            for (int pass = 0; pass < iterations; pass++)
            {
                Array.Clear(delta);
                Array.Clear(count);

                for (int f = 0; f < faceCount; f++)
                {
                    int a = tris[f * 3], b = tris[f * 3 + 1], c = tris[f * 3 + 2];

                    // In-plane gradient of the linear height field over this triangle. Solve for
                    // grad in the face's 2D in-plane basis so we cap the true steepest-ascent slope,
                    // not just the per-edge slopes (a face can out-slope all three of its edges).
                    var e1 = planar[b] - planar[a];
                    var e2 = planar[c] - planar[a];
                    double h1 = height[b] - height[a];
                    double h2 = height[c] - height[a];

                    double g11 = e1.Dot(e1), g12 = e1.Dot(e2), g22 = e2.Dot(e2);
                    double det = g11 * g22 - g12 * g12;
                    if (Math.Abs(det) < 1e-10)
                        continue;

                    // Gradient coordinates (u,v) in the {e1,e2} basis, then its magnitude in-plane.
                    double u = (h1 * g22 - h2 * g12) / det;
                    double v = (h2 * g11 - h1 * g12) / det;
                    double gradSq = u * u * g11 + 2.0 * u * v * g12 + v * v * g22;
                    double grad = Math.Sqrt(Math.Max(0.0, gradSq));
                    if (grad <= tanLimit)
                        continue;

                    // Shrink each vertex's deviation from the face mean by tanLimit/grad -> gradient
                    // drops to exactly the limit.
                    double shrink = tanLimit / grad;
                    double mean = (height[a] + height[b] + height[c]) / 3.0;
                    AccumulateShrink(a, mean, shrink, height, noPins, delta, count);
                    AccumulateShrink(b, mean, shrink, height, noPins, delta, count);
                    AccumulateShrink(c, mean, shrink, height, noPins, delta, count);
                }

                for (int i = 0; i < n; i++)
                    if (count[i] > 0)
                        height[i] += rate * (delta[i] / count[i]);

                // Spring the inner seal edge back toward the true parting-line height.
                if (innerHold > 0.0)
                    for (int i = 0; i < n; i++)
                        if (innerBoundary[i])
                            height[i] += innerHold * (h0[i] - height[i]);
            }

            var outVerts = new double[n * 3];
            for (int i = 0; i < n; i++)
            {
                var v = planar[i] + height[i] * axis;
                outVerts[i * 3] = v.X;
                outVerts[i * 3 + 1] = v.Y;
                outVerts[i * 3 + 2] = v.Z;
            }

            var meshResult = CreateMesh(outVerts.AsSpan(), tris.AsSpan());
            return meshResult.IsSuccess ? Result.Success(meshResult.Value.WithMetadata(mesh.Metadata)) : Result.Success(mesh);
        }
        catch (Exception)
        {
            return Result.Success(mesh);
        }
    }

    /// <summary>
    /// Records the height a vertex would take if its deviation from <paramref name="mean"/> were scaled
    /// by <paramref name="shrink"/> (the per-face target that flattens an over-steep triangle), into the
    /// per-vertex accumulators. Pinned vertices are skipped so they stay put.
    /// </summary>
    private static void AccumulateShrink(
        int vertex, double mean, double shrink, double[] height, bool[] pinned, double[] delta, int[] count)
    {
        if (pinned[vertex])
            return;
        double target = mean + shrink * (height[vertex] - mean);
        delta[vertex] += target - height[vertex];
        count[vertex]++;
    }

    private static int IdentifyVertexRingIndex(
        Vector2 v2,
        Vector2[] anatomy2D,
        List<List<Vector2[]>> ribbonLayers)
    {
        double minDistSq = double.MaxValue;
        // Default to the outermost ribbon (a free vertex) if somehow unmatched; never defaults to the
        // pinned anatomy ring.
        int bestRing = Math.Max(1, ribbonLayers.Count);

        // Check distance to Ring 0 (Anatomy)
        for (int i = 0; i < anatomy2D.Length; i++)
        {
            double dSq = v2.DistanceSquared(anatomy2D[i]);
            if (dSq < minDistSq)
            { minDistSq = dSq; bestRing = 0; }
        }

        // Check distances to wavefront ribbon layers (Ring 1..N). The outermost is the flange edge.
        for (int layerIdx = 0; layerIdx < ribbonLayers.Count; layerIdx++)
        {
            int ringNum = layerIdx + 1;
            foreach (var island in ribbonLayers[layerIdx])
            {
                for (int i = 0; i < island.Length; i++)
                {
                    double dSq = v2.DistanceSquared(island[i]);
                    if (dSq < minDistSq)
                    { minDistSq = dSq; bestRing = ringNum; }
                }
            }
        }

        return bestRing;
    }

    private static int FindClosestIndex(Vector2 target, Vector2[] loop)
    {
        int bestIdx = 0;
        double minSq = double.MaxValue;
        for (int i = 0; i < loop.Length; i++)
        {
            double dSq = target.DistanceSquared(loop[i]);
            if (dSq < minSq)
            { minSq = dSq; bestIdx = i; }
        }
        return bestIdx;
    }

    /// <summary>
    /// Keeps the triangles lying in the band BETWEEN two sets of contours: a face is kept when its
    /// centroid is inside at least one of <paramref name="outerContours"/> and inside none of
    /// <paramref name="innerContours"/>.
    ///
    /// This replaces the older approach of triangulating a pre-punched region and then deleting the
    /// centre-most faces. Every contour handed to triangulateContours becomes a constrained edge, so
    /// no triangle can straddle one: each face lies wholly on one side of every contour, and a single
    /// centroid containment test classifies it exactly. That makes the band a property we can simply
    /// select for afterwards, instead of something the winding rule has to be coaxed into producing
    /// (which needed the enclosure-multiplicity counting in PushInnerHoleContours, and produced a
    /// filled hole whenever that count was off). It also generalizes for free: any number of contours
    /// on either side, in any winding, including a wavefront that has fragmented into islands.
    ///
    /// Because the classification is exact, no ring-membership guard is needed to protect faces in
    /// concave stretches - a face tucked into a notch is outside the anatomy loop and is kept.
    /// </summary>
    private static List<int> ExtractBandTriangles(
        PlanarTriangulation triangulation,
        int[] idToIndex,
        IReadOnlyList<IReadOnlyList<Vector2>> innerContours,
        IReadOnlyList<IReadOnlyList<Vector2>> outerContours)
    {
        var pts = triangulation.Points;
        var triangles = triangulation.Triangles;

        var kept = new List<int>(triangles.Length);

        for (int t = 0; t + 2 < triangles.Length; t += 3)
        {
            int v0 = triangles[t];
            int v1 = triangles[t + 1];
            int v2 = triangles[t + 2];

            var centroid = (pts[v0] + pts[v1] + pts[v2]) / 3.0;

            // Outside the flange's outer edge - beyond the band.
            if (outerContours.Count > 0 && !outerContours.Any(c => ContainsPoint(c, centroid)))
                continue;

            // Inside the parting line (or any other hole) - short of the band.
            if (innerContours.Any(c => ContainsPoint(c, centroid)))
                continue;

            kept.Add(idToIndex[v0]);
            kept.Add(idToIndex[v1]);
            kept.Add(idToIndex[v2]);
        }

        return kept;
    }

    public Result<IMesh> GenerateHolePatch(IReadOnlyList<Vector3> loop, Vector3 planeNormal)
    {
        if (loop == null || loop.Count < 3) return MeshErrors.InvalidPolygon;
        var direction = planeNormal.Normalize();
        var rotation = RotationFromZTo(direction);
        var inverseRotation = rotation.Inverse();
        int N = loop.Count;
        var local3D = new Vector3[N];
        for (int i = 0; i < N; i++) local3D[i] = inverseRotation.Apply(loop[i]);
        var outline = PlanarPolygon.FromOuter([.. local3D.Select(p => new Vector2(p.X, p.Y))]);
        var polyMesh = _engine.Polygons.Triangulate([outline]);
        if (polyMesh.IsFailure) return new Error("Geometry.TriangulationFailed", "Failed");

        // The loop's own points are pinned to their heights. Matched by position rather than taken
        // as the first N, since the triangulator drops repeated points and so owes no order.
        var height = new Dictionary<Vector2, double>(N);
        foreach (var p in local3D) height.TryAdd(new Vector2(p.X, p.Y), p.Z);

        int vertCount = polyMesh.Value.Points.Length;
        var positions = new Vector3[vertCount];
        bool[] pinned = new bool[vertCount];
        for (int i = 0; i < vertCount; i++)
        {
            var p2 = polyMesh.Value.Points[i];
            pinned[i] = height.TryGetValue(p2, out double z);
            positions[i] = new Vector3(p2.X, p2.Y, pinned[i] ? z : local3D[0].Z);
        }
        var triangles = polyMesh.Value.Triangles.ToList();
        SmoothFlangeHeights(positions, triangles, pinned, vertCount, 60, 0.5);
        var worldVertices = new double[vertCount * 3];
        for (int i = 0; i < vertCount; i++)
        {
            var worldV3 = rotation.Apply(positions[i]);
            worldVertices[i * 3] = worldV3.X;
            worldVertices[i * 3 + 1] = worldV3.Y;
            worldVertices[i * 3 + 2] = worldV3.Z;
        }
        return CreateMesh(worldVertices.AsSpan(), CollectionsMarshal.AsSpan(triangles));
    }
}