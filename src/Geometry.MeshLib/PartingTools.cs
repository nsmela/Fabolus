using Clipper2Lib;
using Fabolus.Core.Common;
using Fabolus.Core.Geometry;
using System.Runtime.InteropServices;
using System.Numerics;

namespace GeometryMeshLib;

/// <summary>
/// Parting-line detection (isoline marching against the pull direction) and split-tool solid
/// generation. The isoline pass walks MeshLib's native mesh directly - vertex positions,
/// per-vertex normals and triangle indices - rather than round-tripping through IMesh's flat
/// arrays, since that data is already sitting in native form once the mesh is loaded.
/// </summary>
internal sealed class PartingTools : IPartingTools
{
    private readonly GeometryEngine _engine;

    /// <summary>
    /// Overlap, in mm, by which the wavefront flange runs inward past the parting line. Cutting the
    /// band exactly on that contour leaves the flange flush with it, where a single face rounding the
    /// wrong way opens a seam; the bleed makes it overlap instead. Kept small so the inward offset
    /// cannot reach through a thin neck of the parting line and cross to its far side.
    /// </summary>
    private const float BleedMm = 2.5f;

    /// <summary>
    /// How far, in mm, the flange's inner rim is driven inside the body when a seal mesh is supplied
    /// (see <see cref="SealInnerRimAgainstBody"/>). It only has to beat the accuracy of the footprint
    /// arithmetic that placed the rim - the measured worst-case leak is under 1mm on both chin.3mf and
    /// scalp.3mf - while staying small enough that the rim doesn't intrude visibly into the cavity.
    /// </summary>
    private const float SealMarginMm = 0.5f;

    /// <summary>
    /// Height-relaxation strength applied to the wavefront flange's innermost free ring. Kept low so
    /// the band next to the parting line still carries the anatomy's undulation - raising it pulls the
    /// flange away from the silhouette it is supposed to hug.
    /// </summary>
    private const float InnerSmoothingFactor = 0.25f;

    /// <summary>
    /// Height-relaxation strength applied at the flange's outer rim, interpolated up to from
    /// <see cref="InnerSmoothingFactor"/> across the intervening rings. High enough that the far field
    /// reaches its crease-free harmonic limit inside the iteration budget. Must stay below 1 for the
    /// Jacobi iteration to remain stable.
    /// </summary>
    private const float OuterSmoothingFactor = 0.9f;

    /// <summary>
    /// Target surface slope (degrees from horizontal) the overhang relaxation caps the flange at. Set a
    /// few degrees under the 45-degree FDM support-free limit on purpose: capping exactly at 45 leaves a
    /// broad band of faces hovering just above it (they converge *to* the target), whereas targeting 40
    /// pushes the whole flange body under the real limit with margin - on chin.3mf that cut faces over
    /// 45 degrees from ~500 to ~30 (the remainder are irreducible, hard against the fixed parting edge
    /// where it plunges). <see cref="RelaxSteepSlopesWorld"/> eases faces past this back down toward it.
    /// </summary>
    private const float MaxFlangeSlopeDeg = 40f;

    /// <summary>
    /// How firmly the flange's inner (parting) edge is held to the true parting-line height during
    /// overhang relaxation, in [0, 1]. 1 holds the seal exactly but leaves steep faces wherever the
    /// parting line itself plunges; lower lets the seal edge ease along the pull axis to shed those
    /// faces. See <see cref="RelaxSteepSlopesWorld"/>.
    /// </summary>
    private const float FlangeInnerSealHold = 0.5f;

    public PartingTools(GeometryEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
    }

    public Result<PartingLine> GeneratePartingLine(
        IMesh mesh, Vector3 pullDirection, float noiseThreshold = 0.1f, PartingNeutralBand neutralBand = default)
    {
        if (mesh is null)
            return GeometryErrors.NullMesh;
        if (mesh.IsEmpty)
            return GeometryErrors.InvalidMesh;
        if (pullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        var direction = Vector3.Normalize(pullDirection);

        using var mrMesh = mesh.ToMRMesh();
        using var validVerts = mrMesh.topology.getValidVerts();
        var pts = mrMesh.points.vec;
        ulong ptsCount = pts.size();

        using var normals = MR.computePerVertNormals(mrMesh);

        // Scalar field: how aligned each vertex's normal is with the pull direction.
        // Zero crossings of this field, walked triangle by triangle, are the silhouette.
        var scalars = new double[(int)ptsCount];
        for (ulong i = 0; i < ptsCount; i++)
        {
            var vid = new MR.VertId((int)i);
            if (!validVerts.test(vid))
                continue;

            var n = normals[vid];
            scalars[(int)i] = n.x * direction.X + n.y * direction.Y + n.z * direction.Z;
        }

        // Trace at the neutral band's midpoint rather than at exactly perpendicular, so an asymmetric
        // band biases the parting line toward the side the user opened up. A default (zero-width) band
        // gives a midpoint of 0, i.e. the plain silhouette.
        double iso = neutralBand.Midpoint;

        var graph = new IsolineGraph();

        using var validFaces = mrMesh.topology.getValidFaces();
        ulong faceCap = mrMesh.topology.faceCapacity();

        for (ulong i = 0; i < faceCap; i++)
        {
            var fid = new MR.FaceId((int)i);
            if (!validFaces.test(fid))
                continue;

            var tri = mrMesh.topology.getTriVerts(fid);
            int a = tri.elems._0.get();
            int b = tri.elems._1.get();
            int c = tri.elems._2.get();

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

            var pa = pts[(ulong)a];
            var pb = pts[(ulong)b];
            var pc = pts[(ulong)c];

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
                if (!uniqueCrossings.Any(u => Vector3.DistanceSquared(u, cr) < 1e-6f))
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
            var s = statsResult.Value;
            maxDim = Math.Max(s.MaxX - s.MinX, Math.Max(s.MaxY - s.MinY, s.MaxZ - s.MinZ));
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
            return GeometryErrors.NullMesh;
        if (surface.IsEmpty)
            return GeometryErrors.InvalidMesh;
        if (line is null || !line.IsValid)
            return MeshErrors.InvalidPartingLine;
        if (pullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        var direction = Vector3.Normalize(pullDirection);

        // One native mesh for the whole run: the smoother calls back once per point per iteration
        // (order 10k times on a head-sized bolus), and rebuilding the AABB tree per call would dwarf
        // the smoothing itself.
        using var mrMesh = surface.ToMRMesh();

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
            var query = new MR.Vector3f(candidate.X, candidate.Y, candidate.Z);
            using var projection = MR.findProjection(in query, mrMesh, null, null, null, null, null);
            var closest = projection.proj.point;
            return new Vector3(closest.x, closest.y, closest.z);
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
    private const float NormalNeighbourhoodFraction = 0.05f;

    public Result<CutContourReport> InspectCutContours(IMesh mould, IMesh cutter, Vector3 shiftCutter = default)
    {
        if (mould is null || cutter is null) return GeometryErrors.NullMesh;
        if (mould.IsEmpty || cutter.IsEmpty) return GeometryErrors.InvalidMesh;

        try
        {
            using var mrMould = mould.ToMRMesh();
            using var mrCutter = cutter.ToMRMesh();
            using var partMould = new MR.MeshPart(mrMould);
            using var partCutter = new MR.MeshPart(mrCutter);

            MR.AffineXf3f? rigid = null;
            if (shiftCutter != Vector3.Zero)
            {
                var translation = new MR.Vector3f(shiftCutter.X, shiftCutter.Y, shiftCutter.Z);
                rigid = MR.AffineXf3f.translation(in translation);
            }

            using var converters = MR.getVectorConverters(partMould, partCutter, rigid);
            using var colliding = MR.findCollidingEdgeTrisPrecise(
                partMould, partCutter, converters.toInt, rigid, null);
            using var contours = MR.orderIntersectionContours(
                mrMould.topology, mrCutter.topology, colliding);

            int total = (int)contours.size();
            int closed = 0;
            for (ulong i = 0; i < contours.size(); i++)
            {
                if (MR.isClosed(contours[i])) closed++;
            }

            return Result.Success(new CutContourReport(total, closed));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.InspectCutContoursFailed", ex.Message);
        }
    }

    public Result<IReadOnlyList<Vector3>> SampleSurfaceNormals(IMesh mesh, IReadOnlyList<Vector3> points)
    {
        if (mesh is null) return GeometryErrors.NullMesh;
        if (mesh.IsEmpty) return GeometryErrors.InvalidMesh;
        if (points is null) return GeometryErrors.InvalidPolygon;

        try
        {
            return Result.Success<IReadOnlyList<Vector3>>(SmoothNormalsAt(mesh, points));
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
    private static Vector3[] SmoothNormalsAt(IMesh mesh, IReadOnlyList<Vector3> points)
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

                var faceNormal = Vector3.Cross(b - a, c - a);
                vertexNormals[tris[t]] += faceNormal;
                vertexNormals[tris[t + 1]] += faceNormal;
                vertexNormals[tris[t + 2]] += faceNormal;
            }

            // Radius the neighbourhood is gathered over, relative to the body so it means the same
            // thing on a nose as on a scalp.
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var v in verts) { min = Vector3.Min(min, v); max = Vector3.Max(max, v); }
            float radius = (max - min).Length() * NormalNeighbourhoodFraction;
            float radiusSq = radius * radius;

            using var mrMesh = mesh.ToMRMesh();
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
                    if (Vector3.DistanceSquared(verts[v], points[i]) > radiusSq) continue;
                    summed += vertexNormals[v];
                }

                // Nothing in range - a body coarse enough that the radius spans no vertex at all.
                // Fall back to the face beneath the point, which is always there.
                if (summed.LengthSquared() < 1e-18f)
                {
                    var query = new MR.Vector3f(points[i].X, points[i].Y, points[i].Z);
                    using var projection = MR.findProjection(in query, mrMesh, null, null, null, null, null);

                    int face = projection.proj.face.get();
                    if (face < 0 || (face * 3) + 2 >= tris.Length) continue;

                    summed = vertexNormals[tris[face * 3]]
                           + vertexNormals[tris[(face * 3) + 1]]
                           + vertexNormals[tris[(face * 3) + 2]];
                }

                if (summed.LengthSquared() > 1e-18f) sampled[i] = Vector3.Normalize(summed);
            }

            return sampled;
        }
    }

    public Result<ISurfaceProjector> CreateSurfaceProjector(IMesh mesh)
    {
        if (mesh is null) return GeometryErrors.NullMesh;
        if (mesh.IsEmpty) return GeometryErrors.InvalidMesh;

        try
        {
            return Result.Success<ISurfaceProjector>(new SurfaceProjector(mesh.ToMRMesh()));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.SurfaceProjectorFailed", ex.Message);
        }
    }

    /// <summary>
    /// Holds the native mesh - and with it the AABB tree that MeshLib builds lazily on the first
    /// query - so a run of projections pays for the index once rather than once per point.
    /// </summary>
    private sealed class SurfaceProjector : ISurfaceProjector
    {
        private readonly MR.Mesh _mesh;

        public SurfaceProjector(MR.Mesh mesh) => _mesh = mesh;

        public Vector3 Project(Vector3 point)
        {
            var query = new MR.Vector3f(point.X, point.Y, point.Z);
            using var projection = MR.findProjection(in query, _mesh, null, null, null, null, null);
            var closest = projection.proj.point;
            return new Vector3(closest.x, closest.y, closest.z);
        }

        public void Dispose() => _mesh.Dispose();
    }

    public Result<ISurfaceGeodesic> CreateSurfaceGeodesic(IMesh mesh)
    {
        if (mesh is null) return GeometryErrors.NullMesh;
        if (mesh.IsEmpty) return GeometryErrors.InvalidMesh;

        try
        {
            return Result.Success<ISurfaceGeodesic>(new SurfaceGeodesic(mesh.ToMRMesh()));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.SurfaceGeodesicFailed", ex.Message);
        }
    }

    /// <summary>
    /// MeshLib's geodesic: an approximate path found first, then shortened by flipping the edges it
    /// crosses until it stops getting shorter. Holds the native mesh, and with it the AABB tree the two
    /// end projections need.
    /// </summary>
    private sealed class SurfaceGeodesic : ISurfaceGeodesic
    {
        /// <summary>
        /// How the path is seeded before the flipping pass shortens it.
        ///
        /// <para>
        /// A* rather than the library's default of fast marching. Fast marching solves the distance
        /// field over the whole mesh to find one path, which is the right trade when the answer is
        /// wanted from every point at once and the wrong one here: this is asked for two paths between
        /// two known points on every frame of a drag, against a body of hundreds of thousands of faces.
        /// A* visits what lies between the two ends. What the flipping pass then converges to is the
        /// same path either way - the seed only decides how much flipping it takes to get there.
        /// </para>
        /// </summary>
        private const MR.GeodesicPathApprox Seed = MR.GeodesicPathApprox.DijkstraAStar;

        /// <summary>
        /// How many flips the shortening pass may make. MeshLib's own default; raising it buys a path
        /// that is shorter by less than the mesh's own resolution on the bodies here.
        /// </summary>
        private const int MaxFlips = 100;

        private readonly MR.Mesh _mesh;

        public SurfaceGeodesic(MR.Mesh mesh) => _mesh = mesh;

        public IReadOnlyList<Vector3>? Path(Vector3 from, Vector3 to)
        {
            var start = OnSurface(from);
            var end = OnSurface(to);
            if (start is null || end is null) return null;

            using (start)
            using (end)
            {
                // Crossings only - the ends are not among them, so they are added here. A path between
                // two points in the same triangle has no crossings at all and comes back empty, which is
                // a correct answer rather than a failure.
                using var crossings = MR.computeGeodesicPath(_mesh, start, end, Seed, MaxFlips);
                if (crossings is null) return null;

                ulong count = crossings.size();
                var path = new List<Vector3>((int)count + 2) { Point(start) };

                for (ulong i = 0; i < count; i++)
                {
                    var at = _mesh.edgePoint(crossings[i]);
                    path.Add(new Vector3(at.x, at.y, at.z));
                }

                path.Add(Point(end));
                return path;
            }
        }

        /// <summary>The nearest place on the surface to a point, as the form the path finder takes.</summary>
        private MR.MeshTriPoint? OnSurface(Vector3 point)
        {
            var query = new MR.Vector3f(point.X, point.Y, point.Z);
            using var projection = MR.findProjection(in query, _mesh, null, null, null, null, null);
            return new MR.MeshTriPoint(projection.mtp);
        }

        private Vector3 Point(MR.MeshTriPoint at)
        {
            var p = _mesh.triPoint(at);
            return new Vector3(p.x, p.y, p.z);
        }

        public void Dispose() => _mesh.Dispose();
    }

    public Result<IReadOnlyList<Vector3>> GenerateInnerConcaveContour(IMesh referenceMesh, PartingLine partingLine, float offset = 0)
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
        IMesh referenceMesh, Vector3 pullDirection, float offset = 10.0f)
    {
        if (referenceMesh is null)
            return MeshErrors.NullSource;
        if (referenceMesh.IsEmpty)
            return GeometryErrors.InvalidMesh;
        if (pullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        // Measured from the mesh's own vertices rather than read from Metadata.MeshStats: those are
        // only written on import/repair/cut, so a generated mould either carries none (reported,
        // misleadingly, as corrupt topology) or carries the bounds of the body it was derived from.
        //
        // Bounds are taken in the footprint plane, not in world axes. A world-axis box is only the
        // tight enclosure of the mesh's shadow when the pull direction is a world axis; off-axis it
        // both over-hangs on some sides and, worse, isn't the plane the flange is triangulated in.
        var direction = Vector3.Normalize(pullDirection);
        var (u, v) = PartingFrame.Basis(direction);

        float minU = float.MaxValue, maxU = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;
        foreach (var p in referenceMesh.Vertices)
        {
            float pu = Vector3.Dot(p, u);
            float pv = Vector3.Dot(p, v);
            if (pu < minU) minU = pu;
            if (pu > maxU) maxU = pu;
            if (pv < minV) minV = pv;
            if (pv > maxV) maxV = pv;
        }

        if (minU > maxU || minV > maxV)
            return GeometryErrors.InvalidMesh;

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

    private static Vector3 Interpolate(MR.Vector3f a, MR.Vector3f b, double sa, double sb)
    {
        float t = (float)(Math.Abs(sa) / (Math.Abs(sa) + Math.Abs(sb)));
        return new Vector3(a.x + t * (b.x - a.x), a.y + t * (b.y - a.y), a.z + t * (b.z - a.z));
    }

    private static double LoopLength(IReadOnlyList<Vector3> loop)
    {
        double len = 0;
        for (int i = 0; i < loop.Count; i++)
            len += Vector3.Distance(loop[i], loop[(i + 1) % loop.Count]);
        return len;
    }

    // --- Contour winding ---
    //
    // MR.PlanarTriangulation.triangulateContours has no "this contour is a hole" flag: it decides
    // filled-vs-empty with a NON-ZERO WINDING RULE over contour direction alone, and a contour's
    // position in the list means nothing. A region is left open only when the winding numbers of
    // every contour enclosing it sum to zero. Two consequences drive the helpers below:
    //
    //   1. Winding must be normalized. Loops arriving from the isoline walk or from Clipper have
    //      whatever direction they happened to be built with, so without this the hole appears or
    //      not depending on the input mesh.
    //   2. Every nested same-wound contour (i.e. each ribbon) adds 1 to the winding number of
    //      everything inside it, so a single reversed inner loop can only ever cancel one of them.
    //      To reach zero the inner loop is pushed once per enclosing contour - see
    //      PushInnerHoleContours.

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
    /// Pushes <paramref name="innerLoop"/> as a hole, wound clockwise (opposite the CCW-normalized
    /// outer boundary and ribbons) and repeated once per contour in <paramref name="enclosing"/>
    /// that actually contains it. Each enclosing contour contributes +1 to the winding number
    /// inside the loop, so N copies at -1 each are what bring the total to zero and leave the
    /// region untriangulated. The caller owns disposing the returned native vectors.
    /// </summary>
    private static List<MR.Std.Vector_MRVector2f> PushInnerHoleContours(
        MR.Std.Vector_StdVectorMRVector2f contours,
        IReadOnlyList<Vector2> innerLoop,
        IEnumerable<IReadOnlyList<Vector2>> enclosing)
    {
        // Any vertex of the inner loop works as the containment probe: the outer boundary and every
        // ribbon are built as outward offsets of this loop, so its vertices lie strictly inside them.
        var probe = innerLoop[0];
        int multiplicity = enclosing.Count(c => ContainsPoint(c, probe));

        // Degenerate inputs (a probe outside everything) would otherwise emit zero copies and
        // silently fill the hole; one reversed copy is the sane floor.
        if (multiplicity < 1)
            multiplicity = 1;

        var innerCw = AsWinding(innerLoop, ccw: false);
        var handles = new List<MR.Std.Vector_MRVector2f>(multiplicity);
        for (int i = 0; i < multiplicity; i++)
        {
            var vec = CreateNativeContour(innerCw);
            handles.Add(vec);
            contours.pushBack(vec);
        }
        return handles;
    }

    /// <summary>
    /// Rotation that maps world +Z onto <paramref name="target"/>. Delegates to
    /// <see cref="PartingFrame"/> rather than keeping a local copy: the outer contour is flattened
    /// through that same frame, and a second implementation here is exactly how the two came to
    /// disagree about which plane the flange lives in.
    /// </summary>
    private static Quaternion RotationFromZTo(Vector3 target) => PartingFrame.RotationFromZTo(target);

    /// <summary>
    /// Stitches isoline segments (pairs of 3D points, one per triangle zero-crossing) into
    /// closed loops via a simple adjacency-graph walk. Direct C# port of the marching-triangles
    /// isoline extraction used elsewhere for parting-line generation - the geometry is engine-
    /// agnostic, only the scalar-field sampling above is MeshLib-specific.
    /// </summary>
    private sealed class IsolineGraph
    {
        private const float ToleranceSq = 0.001f * 0.001f;
        private readonly List<Vector3> _nodes = new();
        private readonly Dictionary<int, List<int>> _adjacency = new();

        public void AddSegment(Vector3 p0, Vector3 p1)
        {
            if (Vector3.DistanceSquared(p0, p1) < ToleranceSq)
                return;

            int id0 = GetOrAddNode(p0);
            int id1 = GetOrAddNode(p1);
            AddEdge(id0, id1);
            AddEdge(id1, id0);
        }

        private readonly Dictionary<(int x, int y, int z), int> _spatialHash = new();
        private const float QuantizeScale = 1000f; // 1mm / 1000 = 0.001mm resolution

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

    private static MR.Std.Vector_MRVector2f CreateNativeContour(IReadOnlyList<Vector2> polygon)
    {
        var vec = new MR.Std.Vector_MRVector2f();
        int n = polygon.Count;

        for (int i = 0; i < n; i++)
        {
            vec.pushBack(new MR.Vector2f(polygon[i].X, polygon[i].Y));
        }

        // MeshLib triangulateContours requires explicitly closed loops (first point == last point)
        if (Vector2.DistanceSquared(polygon[0], polygon[n - 1]) > 1e-6f)
        {
            vec.pushBack(new MR.Vector2f(polygon[0].X, polygon[0].Y));
        }

        return vec;
    }

    private static float GetDistanceToPolygon(Vector2 p, IReadOnlyList<Vector2> poly)
    {
        float minD = float.MaxValue;
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            minD = Math.Min(minD, DistancePointToSegment(p, a, b));
        }
        return minD;
    }

    private static float DistancePointToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        var l2 = Vector2.DistanceSquared(a, b);
        if (l2 == 0)
            return Vector2.Distance(p, a);
        var t = Math.Clamp(Vector2.Dot(p - a, b - a) / l2, 0f, 1f);
        var projection = a + t * (b - a);
        return Vector2.Distance(p, projection);
    }

    /// <summary>
    /// Offsets a 2D polygon outward and strictly constrains (clips) the result against an outer bounding 
    /// frame using Clipper2 Boolean intersection. Prevents expansion ribbons from overshooting the tooling box.
    /// </summary>
    private static Result<IReadOnlyList<Vector2>> GenerateConstrainedOffset(
        IReadOnlyList<Vector2> contour,
        float offsetMm,
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
                (float)(dominantPath[i].X / ClipperScale),
                (float)(dominantPath[i].Y / ClipperScale));
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
        float stepDistanceMm = 3.0f,
        int maxRibbonRings = 200,
        float concaveBandWidthMm = 3.0f,
        float overhangTargetSlopeDeg = MaxFlangeSlopeDeg,
        float innerSealHold = FlangeInnerSealHold,
        float innerBleedMm = BleedMm,
        IMesh? sealAgainst = null,
        float sealMarginMm = SealMarginMm,
        IMesh? launchSurface = null,
        float launchHoldMm = LaunchHoldMm,
        bool rawFlange = false,
        int launchSmoothingPasses = 0)
    {
        if (inner3DLoop is null || inner3DLoop.Count < 3)
            return GeometryErrors.InvalidPolygon;
        if (outerPlanarBox is null || outerPlanarBox.Count < 3)
            return GeometryErrors.InvalidPolygon;
        if (planeNormal == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;

        // 1. Establish Local Coordinate Frame (Map plane normal -> Local +Z, which represents World Y)
        var direction = Vector3.Normalize(planeNormal);
        var rotation = RotationFromZTo(direction);
        var inverseRotation = Quaternion.Inverse(rotation);

        var local3D = new Vector3[inner3DLoop.Count];
        var local2D = new Vector2[inner3DLoop.Count];
        for (int i = 0; i < inner3DLoop.Count; i++)
        {
            var transformed = Vector3.Transform(inner3DLoop[i], inverseRotation);
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

        // 3. Assemble all planar contours for MeshLib triangulation.
        //
        // Every contour - the anatomy loop included - is wound CCW, so triangulateContours simply
        // fills the whole footprint out to the outermost ribbon. We deliberately do NOT try to make
        // the anatomy loop a hole here (see the "Contour winding" helpers and PushInnerHoleContours):
        // the winding-cancellation trick depends on getting the enclosure multiplicity exactly right,
        // and any miscount silently fills or erases regions. Instead the band is carved AFTER
        // triangulation by keeping only the faces that fall between the contours - see
        // ExtractBandTriangles. Every contour is a constrained edge of the triangulation, so no
        // triangle straddles one and a centroid containment test classifies each face exactly.
        using var allContours = new MR.Std.Vector_StdVectorMRVector2f();

        var nativeContours = new List<MR.Std.Vector_MRVector2f>();
        foreach (var layer in ribbonLayers)
        {
            foreach (var island in layer)
            {
                var rVec = CreateNativeContour(AsWinding(island, ccw: true));
                nativeContours.Add(rVec);
                allContours.pushBack(rVec);
            }
        }

        // Ring 0 (anatomy) goes in wound the same way as the ribbons - a structural edge, not a hole.
        // It stays a contour even though the band now runs past it, so the triangulation carries
        // vertices exactly on the parting line for LiftWavefrontToWorldSpace to pin.
        var anatomyVec = CreateNativeContour(AsWinding(local2D, ccw: true));
        nativeContours.Add(anatomyVec);
        allContours.pushBack(anatomyVec);

        // The inner bleed guard, which is the band's actual inner edge.
        var innerBleedVec = CreateNativeContour(AsWinding(innerBleed, ccw: true));
        nativeContours.Add(innerBleedVec);
        allContours.pushBack(innerBleedVec);

        // 4. Triangulate strictly in local 2D space
        using var nativeMesh = MR.PlanarTriangulation.triangulateContours(allContours, null);
        foreach (var rVec in nativeContours)
            rVec.Dispose();

        if (nativeMesh is null || nativeMesh.topology.getValidFaces().count() == 0)
            return new Error("Geometry.FlangeTriangulationFailed", "Failed to triangulate 2D wavefront flange.");

        // 4b. Launch directions, when a surface was supplied: the body's outward normal at each
        // point of the parting line, carried into the local frame. Null leaves the lift flattening
        // straight off the line as it always did.
        var launchLocal = launchSurface is null
            ? null
            : LocalLaunchDirections(inner3DLoop, launchSurface, inverseRotation);

        // 5. Execute Inside-Out Wavefront Lift & Height Relaxation
        var lifted = LiftWavefrontToWorldSpace(
            nativeMesh, local3D, local2D, ribbonLayers, innerBleed, concaveBandWidthMm, rotation,
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
        var relaxed = RelaxSteepSlopesWorld(lifted.Value, direction, overhangTargetSlopeDeg, iterations: 2000, rate: 0.7f, innerHold: innerSealHold, pinnedVerts: launchedVertices);
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
            return GeometryErrors.NullMesh;
        if (flange.IsEmpty || body.IsEmpty)
            return GeometryErrors.InvalidMesh;
        if (pullDirection == Vector3.Zero)
            return MeshErrors.InvalidPullDirection;
        if (partingLoop is null || partingLoop.Count < 3)
            return MeshErrors.InvalidPartingLine;

        var direction = Vector3.Normalize(pullDirection);
        var rim = InnerRimVertexIndices(flange, direction, partingLoop);
        if (rim.Count == 0)
            return Result.Success<IReadOnlyList<FlangeSealPoint>>(Array.Empty<FlangeSealPoint>());

        using var bodyMr = body.ToMRMesh();
        var vertices = flange.Vertices;

        var points = new List<FlangeSealPoint>(rim.Count);
        foreach (int index in rim)
        {
            var v = vertices[index];
            var query = new MR.Vector3f(v.X, v.Y, v.Z);
            using var signed = MR.findSignedDistance(in query, bodyMr, null, null);
            if (signed is null)
                continue;

            using var hit = signed.value();
            if (hit is null)
                continue;

            points.Add(new FlangeSealPoint(v, hit.dist));
        }

        return Result.Success<IReadOnlyList<FlangeSealPoint>>(points);
    }

    private Result<IMesh> SealInnerRimAgainstBody(
        IMesh flange, IMesh body, Vector3 pullDirection, IReadOnlyList<Vector3> partingLoop, float marginMm)
    {
        try
        {
            var vertices = flange.Vertices.ToArray();
            var rim = InnerRimVertexIndices(flange, pullDirection, partingLoop);
            if (rim.Count == 0)
                return Result.Success(flange);

            using var bodyMr = body.ToMRMesh();
            var bodyVerts = body.Vertices;
            var bodyTris = body.Triangles;

            // Faces touching each rim vertex, so a move can be checked against what it does to them.
            var incident = new Dictionary<int, List<int>>(rim.Count);
            foreach (int index in rim) incident[index] = new List<int>(6);
            var flangeTris = flange.Triangles;
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
                    var query = new MR.Vector3f(v.X, v.Y, v.Z);
                    using var signed = MR.findSignedDistance(in query, bodyMr, null, null);
                    if (signed is null) continue;

                    using var hit = signed.value();
                    if (hit is null) continue;

                    // Only what is actually outside. The margin is where an offending vertex is
                    // sent, not a depth every vertex has to reach: treating it as a threshold moved
                    // roughly half the rim - the median rim point sits about 0.5mm in - and all that
                    // extra shoving is what left the flange steep enough to self-intersect once
                    // extruded. A vertex already inside bridges nothing and is left alone.
                    if (hit.dist < 0f)
                        continue;

                    var onFace = hit.proj;
                    var closest = new Vector3(onFace.point.x, onFace.point.y, onFace.point.z);

                    int face = onFace.face.get();
                    if (face < 0 || (face * 3) + 2 >= bodyTris.Length)
                        continue;

                    var a = bodyVerts[bodyTris[face * 3]];
                    var b = bodyVerts[bodyTris[(face * 3) + 1]];
                    var c = bodyVerts[bodyTris[(face * 3) + 2]];
                    var normal = Vector3.Cross(b - a, c - a);
                    if (normal.LengthSquared() < 1e-12f)
                        continue;

                    // Outward normal, so stepping against it from the surface point goes into the body.
                    offset[i] = closest - (Vector3.Normalize(normal) * marginMm) - v;
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

            var rebuilt = _engine.CreateMesh(flat, flange.Triangles);
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
    private const float SealDiffusion = 0.35f;

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

            blended[i] = Vector3.Lerp(offsets[i], mean, SealDiffusion);
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
                folded = after == Vector3.Zero || Vector3.Dot(before[i], after) <= 0f;
            }

            if (!folded) return vertices[index];
            step *= 0.5f;
        }

        return original;
    }

    private static Vector3 FaceNormal(Vector3[] vertices, int[] triangles, int firstIndex)
    {
        var a = vertices[triangles[firstIndex]];
        var b = vertices[triangles[firstIndex + 1]];
        var c = vertices[triangles[firstIndex + 2]];

        var normal = Vector3.Cross(b - a, c - a);
        return normal.LengthSquared() < 1e-18f ? Vector3.Zero : Vector3.Normalize(normal);
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
        var d = Vector3.Normalize(pullDirection);
        var seed = MathF.Abs(d.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(seed, d));
        var w = Vector3.Cross(d, u);

        var loop2D = new Vector2[partingLoop.Count];
        for (int i = 0; i < partingLoop.Count; i++)
            loop2D[i] = new Vector2(Vector3.Dot(partingLoop[i], u), Vector3.Dot(partingLoop[i], w));

        var vertices = flange.Vertices;
        var inner = new List<int>(boundary.Count);
        foreach (int index in boundary)
        {
            var p = new Vector2(Vector3.Dot(vertices[index], u), Vector3.Dot(vertices[index], w));
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
    public Result<IMesh> ExtrudeFlange(IMesh surface, Vector3 direction, float depth)
    {
        if (depth <= 0f || float.IsNaN(depth) || float.IsInfinity(depth))
            return new Error("Geometry.InvalidDepth", "Extrusion depth must be a positive finite number.");

        return ExtrudeByNormals(surface, depth);
    }

    /// <summary>
    /// Most voxels an offset may use. Bounds what is otherwise cubic in the flange's extent over the
    /// cell size, which on a large body with a thin cutter runs to hundreds of millions.
    /// </summary>
    private const double MaxOffsetVoxels = 60e6;

    public Result<IMesh> ThickenFlange(
        IMesh surface, Vector3 direction, float thicknessMm, float voxelSizeMm)
    {
        if (surface is null) return MeshErrors.NullSource;
        if (surface.IsEmpty) return GeometryErrors.InvalidMesh;
        if (thicknessMm <= 0f) return new Error("Geometry.InvalidDepth", "Thickness must be positive.");
        if (voxelSizeMm <= 0f) return new Error("Geometry.InvalidVoxelSize", "Voxel size must be positive.");

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
        var span = (boxMax - boxMin) + new Vector3(thicknessMm * 2f);
        double cells = ((double)span.X / voxelSizeMm) * (span.Y / voxelSizeMm) * (span.Z / voxelSizeMm);
        if (cells > MaxOffsetVoxels)
            return new Error("Geometry.OffsetTooFine",
                $"Thickening this parting mesh needs about {cells / 1e6:F0} million voxels, over the " +
                $"{MaxOffsetVoxels / 1e6:F0} million allowed. Increase the parting mesh depth - the grid " +
                "is sized relative to it, so a thicker cutter is a coarser and much cheaper grid.");

        float seed = MathF.Min(thicknessMm * 0.25f, voxelSizeMm);
        var slab = ExtrudeByNormals(surface, seed);
        if (slab.IsFailure) return slab;

        try
        {
            using var mr = slab.Value.ToMRMesh();
            using var part = new MR.MeshPart(mr);

            using var settings = new MR.OffsetParameters
            {
                voxelSize = voxelSizeMm,

                // Winding rule rather than OpenVDB: the slab is closed but not necessarily clean, and
                // this is the mode that still yields a coherent inside for one that self-intersects.
                signDetectionMode = MR.SignDetectionMode.WindingRule,
            };

            using var offset = MR.offsetMesh(part, (thicknessMm - seed) * 0.5f, settings);
            if (offset is null) return new Error("Geometry.ThickenFailed", "Offsetting produced no mesh.");

            return Result.Success(offset.ToIMesh(surface.Metadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.ThickenFailed", ex.Message);
        }
    }

    public Result<IMesh> ExtrudeFlangeToSolid(
        IMesh surface, Vector3 direction, float topAlongAxis, float roundingMm, float voxelSizeMm)
    {
        if (surface is null) return MeshErrors.NullSource;
        if (surface.IsEmpty) return GeometryErrors.InvalidMesh;

        var axis = Vector3.Normalize(direction);
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
            vertices.Add(p + (axis * (topAlongAxis - Vector3.Dot(p, axis))));
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

        var built = _engine.CreateMesh(flat, triangles.ToArray());
        if (built.IsFailure || roundingMm <= 0f) return built;

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

    private Result<IMesh> OffsetSolid(IMesh solid, float offsetMm, float voxelSizeMm)
    {
        if (voxelSizeMm <= 0f) return new Error("Geometry.InvalidVoxelSize", "Voxel size must be positive.");

        var (boxMin, boxMax) = Bounds(solid.Vertices);
        var span = (boxMax - boxMin) + new Vector3(MathF.Abs(offsetMm) * 2f);
        double cells = ((double)span.X / voxelSizeMm) * (span.Y / voxelSizeMm) * (span.Z / voxelSizeMm);
        if (cells > MaxOffsetVoxels)
            return new Error("Geometry.OffsetTooFine",
                $"Rounding this parting mesh needs about {cells / 1e6:F0} million voxels, over the " +
                $"{MaxOffsetVoxels / 1e6:F0} million allowed. Increase the parting mesh depth - the " +
                "grid is sized relative to it, so a thicker cutter is a coarser and much cheaper grid.");

        try
        {
            using var mr = solid.ToMRMesh();
            using var part = new MR.MeshPart(mr);
            using var settings = new MR.OffsetParameters
            {
                voxelSize = voxelSizeMm,
                signDetectionMode = MR.SignDetectionMode.WindingRule,
            };

            using var offset = MR.offsetMesh(part, offsetMm, settings);
            if (offset is null) return new Error("Geometry.OffsetFailed", "Offsetting produced no mesh.");

            return Result.Success(offset.ToIMesh(solid.Metadata));
        }
        catch (Exception ex)
        {
            return new Error("Geometry.OffsetFailed", ex.Message);
        }
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
    private Result<IMesh> ExtrudeByNormals(IMesh surface, float depth)
    {
        if (surface is null)
            return MeshErrors.NullSource;
        if (surface.IsEmpty)
            return GeometryErrors.InvalidMesh;

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
            var normal = Vector3.Cross(v1 - v0, v2 - v0);
            normals[a] += normal;
            normals[b] += normal;
            normals[c] += normal;
        }

        for (int i = 0; i < n; i++)
        {
            if (normals[i] != Vector3.Zero)
                normals[i] = Vector3.Normalize(normals[i]);
        }

        // Two vertex copies: the top sheet occupies [0, n), the bottom sheet [n, 2n).
        var verts = new double[n * 2 * 3];
        float halfDepth = depth * 0.5f;
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

        var result = _engine.CreateMesh(verts.AsSpan(), CollectionsMarshal.AsSpan(tris));
        if (result.IsFailure) return result.Error;

        return Result.Success(result.Value.WithMetadata(surface.Metadata));
    }



    public Result<IMesh> GenerateSurfaceSweepFlangeMesh(
        IReadOnlyList<Vector3> inner3DLoop,
        IReadOnlyList<Vector2> outerPlanarBox,
        Vector3 planeNormal,
        IMesh body,
        float stepDistanceMm = 3.0f,
        int maxRings = 200,
        float innerBleedMm = BleedMm,
        float boundsMarginMm = 10f)
    {
        if (inner3DLoop is null || inner3DLoop.Count < 3) return GeometryErrors.InvalidPolygon;
        if (outerPlanarBox is null || outerPlanarBox.Count < 3) return GeometryErrors.InvalidPolygon;
        if (planeNormal == Vector3.Zero) return MeshErrors.InvalidPullDirection;
        if (body is null) return GeometryErrors.NullMesh;
        if (body.IsEmpty) return GeometryErrors.InvalidMesh;

        var axis = Vector3.Normalize(planeNormal);
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
        boundsMin -= new Vector3(boundsMarginMm);
        boundsMax += new Vector3(boundsMarginMm);

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
                    binormal = Vector3.Normalize(radial);
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
        float stepDistanceMm = 3.0f,
        int maxRings = 200,
        float innerBleedMm = BleedMm,
        float boundsMarginMm = 10f)
    {
        if (inner3DLoop is null || inner3DLoop.Count < 3) return GeometryErrors.InvalidPolygon;
        if (planeNormal == Vector3.Zero) return MeshErrors.InvalidPullDirection;
        if (body is null) return GeometryErrors.NullMesh;
        if (body.IsEmpty) return GeometryErrors.InvalidMesh;

        var axis = Vector3.Normalize(planeNormal);
        int n = inner3DLoop.Count;

        // Outward direction per point: the body's normal at the rim, made perpendicular to the loop
        var surfaceNormals = OutwardNormalsAlong(inner3DLoop, body);
        var outward = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var tangent = inner3DLoop[(i + 1) % n] - inner3DLoop[(i - 1 + n) % n];
            outward[i] = Vector3.Normalize(Perpendicular(surfaceNormals[i], tangent, axis));
        }

        // The volume the flange has to reach and no further: the body's own bounds opened up by the margin.
        var (boundsMin, boundsMax) = Bounds(body.Vertices);
        boundsMin -= new Vector3(boundsMarginMm);
        boundsMax += new Vector3(boundsMarginMm);

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
                    binormal = Vector3.Normalize(radial);
                }
                heading[i] = Perpendicular(binormal, tangent, axis);
            }

            SmoothDirections(heading, marching);

            bool allInside = true;

            for (int i = 0; i < n; i++)
            {
                float stepDist = Outside(current[i], boundsMin, boundsMax) ? 1.0f : stepDistanceMm;
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
    /// How hard each swept ring is relaxed toward its neighbours' midpoint before the next step is
    /// taken from it. This is the sweep's only defence against the rings crowding where the parting
    /// line is concave, so it cannot be timid; it is applied to the ring's shape, never to the
    /// parting line itself, which is ring zero and never moves. Measured on chin and scalp, raising
    /// this from 0.35 to 0.6 cut the swept surface's self-intersections by roughly four fifths.
    /// </summary>
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
        float innerBleedMm = BleedMm,
        float outerMarginMm = 10f,
        int rings = 16,
        int heightSmoothing = 6)
    {
        if (partingLine is null || partingLine.Count < 8) return GeometryErrors.InvalidPolygon;
        if (planeNormal == Vector3.Zero) return MeshErrors.InvalidPullDirection;
        if (body is null || body.IsEmpty) return GeometryErrors.InvalidMesh;
        if (mould is null || mould.IsEmpty) return GeometryErrors.InvalidMesh;
        if (rings < 2) rings = 2;

        var axis = Vector3.Normalize(planeNormal);
        int n = partingLine.Count;

        var (u, v) = LoftFrame(axis);

        // The bleed ring, offset into the body along its own surface, is what carries the seal. Placed
        // exactly as the marching sweep places it, because that part was never the problem.
        var surfaceNormals = OutwardNormalsAlong(partingLine, body);
        var bleed = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var tangent = partingLine[(i + 1) % n] - partingLine[(i - 1 + n) % n];
            var outward = Vector3.Normalize(Perpendicular(surfaceNormals[i], tangent, axis));
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
        float turning = 0f;
        for (int i = 1; i <= n; i++)
            turning += LoftWrap(
                LoftBearing(partingLine[i % n], u, v, centre)
                - LoftBearing(partingLine[i - 1], u, v, centre));

        float winding = turning >= 0f ? 1f : -1f;

        var sweep = new float[n];
        float running = 0f;
        float previous = LoftBearing(partingLine[0], u, v, centre);

        for (int i = 1; i < n; i++)
        {
            float here = LoftBearing(partingLine[i], u, v, centre);
            running += MathF.Max(winding * LoftWrap(here - previous), 0f);
            sweep[i] = running;
            previous = here;
        }

        // A line whose bearings barely advance has nothing to map round the outline, and is refused
        // rather than folded onto a point.
        float span = sweep[n - 1];
        if (span < 1e-3f) return GeometryErrors.InvalidPolygon;

        var hull = LoftHull(mould.Vertices, u, v);
        if (hull.Count < 3) return GeometryErrors.InvalidPolygon;

        float start = LoftBearing(partingLine[0], u, v, centre);

        // Carried past the outline rather than stopped on it. A cutter that ends exactly where the
        // mould ends does not sever it - the boolean comes back with two pieces that are each still
        // the whole mould, which is what "halves 99.8% / 99.8%" means when it happens.
        var outerFlat = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float bearing = start + (winding * MathF.Tau * sweep[i] / span);
            var direction = new Vector2(MathF.Cos(bearing), MathF.Sin(bearing));
            outerFlat[i] = LoftRayHit(hull, centre, bearing) + (direction * outerMarginMm);
        }

        // Height from the line at the same bearing, then eased round the ring. The match is exact but
        // it steps wherever the nearest point changes, and a stepped ring puts a crease in the surface
        // at every step.
        var heights = new float[n];
        for (int i = 0; i < n; i++) heights[i] = Vector3.Dot(partingLine[i], axis);
        LoftSmooth(heights, heightSmoothing);

        var stack = new List<Vector3[]>(rings + 2) { bleed, partingLine.ToArray() };

        for (int r = 1; r <= rings; r++)
        {
            float t = (float)r / rings;
            var ring = new Vector3[n];

            for (int i = 0; i < n; i++)
            {
                float inner = Vector3.Dot(partingLine[i], axis);
                var innerFlat = partingLine[i] - (axis * inner);
                var outFlat = (u * outerFlat[i].X) + (v * outerFlat[i].Y);

                ring[i] = Vector3.Lerp(innerFlat, outFlat, t)
                        + (axis * ((inner * (1f - t)) + (heights[i] * t)));
            }

            RepairFolds(ring);
            stack.Add(ring);
        }

        return StitchRings(stack, body.Metadata);
    }

    private static (Vector3 U, Vector3 V) LoftFrame(Vector3 axis)
    {
        var seed = MathF.Abs(Vector3.Dot(axis, Vector3.UnitY)) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(seed, axis));
        return (u, Vector3.Cross(axis, u));
    }

    private static Vector2 LoftFlat(Vector3 p, Vector3 u, Vector3 v) =>
        new(Vector3.Dot(p, u), Vector3.Dot(p, v));

    private static float LoftBearing(Vector3 p, Vector3 u, Vector3 v, Vector2 centre)
    {
        var d = LoftFlat(p, u, v) - centre;
        return MathF.Atan2(d.Y, d.X);
    }

    private static float LoftWrap(float angle)
    {
        while (angle > MathF.PI) angle -= MathF.Tau;
        while (angle < -MathF.PI) angle += MathF.Tau;
        return angle;
    }

    private static void LoftSmooth(float[] values, int passes)
    {
        int n = values.Length;
        if (n < 3) return;

        for (int pass = 0; pass < passes; pass++)
        {
            var next = new float[n];
            for (int i = 0; i < n; i++)
                next[i] = (values[(i - 1 + n) % n] + (2f * values[i]) + values[(i + 1) % n]) * 0.25f;
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

    private static float LoftTurn(Vector2 a, Vector2 b, Vector2 c) =>
        ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));

    /// <summary>Where a ray from <paramref name="centre"/> leaves the hull.</summary>
    private static Vector2 LoftRayHit(List<Vector2> hull, Vector2 centre, float angle)
    {
        var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

        float furthest = 0f;
        for (int i = 0; i < hull.Count; i++)
        {
            var a = hull[i];
            var b = hull[(i + 1) % hull.Count];
            var edge = b - a;

            float denominator = (dir.X * edge.Y) - (dir.Y * edge.X);
            if (MathF.Abs(denominator) < 1e-12f) continue;

            var offset = a - centre;
            float t = ((offset.X * edge.Y) - (offset.Y * edge.X)) / denominator;
            float s = ((offset.X * dir.Y) - (offset.Y * dir.X)) / denominator;

            if (t <= 0f || s < 0f || s > 1f) continue;
            furthest = MathF.Max(furthest, t);
        }

        return centre + (dir * furthest);
    }

    private const float SweepRelaxation = 0.6f;

    /// <summary>Laplacian smoothing of a closed ring, in place.</summary>
    private static void Relax(Vector3[] ring, float factor)
    {
        int n = ring.Length;
        if (n < 4) return;

        var blended = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            var midpoint = (ring[(i - 1 + n) % n] + ring[(i + 1) % n]) * 0.5f;
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

        var cumulative = new float[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + Vector3.Distance(ring[i], ring[(i + 1) % n]);

        float perimeter = cumulative[n];
        if (perimeter < 1e-4f) return;

        var spaced = new Vector3[n];
        int segment = 0;
        for (int k = 0; k < n; k++)
        {
            float target = perimeter * k / n;
            while (segment < n - 1 && cumulative[segment + 1] < target) segment++;

            float span = cumulative[segment + 1] - cumulative[segment];
            float t = span > 1e-6f ? Math.Clamp((target - cumulative[segment]) / span, 0f, 1f) : 0f;
            spaced[k] = Vector3.Lerp(ring[segment], ring[(segment + 1) % n], t);
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
        if (tangent.LengthSquared() > 1e-12f)
        {
            var t = Vector3.Normalize(tangent);
            var projected = normal - (t * Vector3.Dot(normal, t));
            if (projected.LengthSquared() > 1e-12f) return Vector3.Normalize(projected);

            var fallback = Vector3.Cross(t, axis);
            if (fallback.LengthSquared() > 1e-12f) return Vector3.Normalize(fallback);
        }

        return normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : axis;
    }

    /// <summary>
    /// How far a ring may turn at one point before it counts as folded. A ring of any reasonable
    /// resolution turns a few degrees per point, so a right angle is far outside anything the shape
    /// itself produces and only a doubling-back reaches it.
    /// </summary>
    private const float SweepFoldAngleDeg = 90f;

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

        float limit = MathF.Cos(SweepFoldAngleDeg * MathF.PI / 180f);
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
                float lengthIn = incoming.Length(), lengthOut = outgoing.Length();

                // A collapsed segment has no direction to judge, and is itself a fold in the limit -
                // two points of the ring have arrived at the same place.
                bool folded = lengthIn < 1e-5f || lengthOut < 1e-5f
                    || Vector3.Dot(incoming / lengthIn, outgoing / lengthOut) < limit;

                if (!folded) continue;

                ring[i] = (before + after) * 0.5f;
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
                var sum = directions[i] * 4f;
                
                // Immediate neighbors
                foreach (int j in new[] { (i - 1 + n) % n, (i + 1) % n })
                    if (marching[j]) sum += directions[j] * 3f;
                    
                // +/- 2
                foreach (int j in new[] { (i - 2 + n) % n, (i + 2) % n })
                    if (marching[j]) sum += directions[j] * 2f;
                    
                // +/- 3
                foreach (int j in new[] { (i - 3 + n) % n, (i + 3) % n })
                    if (marching[j]) sum += directions[j];

                blended[i] = sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : directions[i];
            }

            Array.Copy(blended, directions, n);
        }
    }


    private static (Vector3 Min, Vector3 Max) Bounds(IReadOnlyList<Vector3> points)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
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
    private Result<IMesh> StitchRings(List<Vector3[]> rings, Fabolus.Core.Geometry.Metadata.MeshMetadata metadata)
    {
        if (rings.Count < 2) return GeometryErrors.InvalidMesh;

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

        var mesh = _engine.CreateMesh(vertices.AsSpan(), CollectionsMarshal.AsSpan(triangles));
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
            float best = float.MaxValue;
            for (int j = 0; j < n; j++)
            {
                float d = Vector3.DistanceSquared(inner[0], outer[j]);
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
                    || (i < n && Vector3.DistanceSquared(inner[nextI], outer[kk])
                             <= Vector3.DistanceSquared(inner[ii], outer[nextK]));

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
            if (Vector3.Cross(outer[kk] - inner[ii], thirdPoint - inner[ii]).LengthSquared() < 1e-14f)
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
    private const float MaxLaunchSlope = 50.0f;

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
    private const float LaunchHoldMm = 15.0f;

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
                float d1 = float.MaxValue, d2 = float.MaxValue;
                var pi = new Vector2(positions[i].X, positions[i].Y);

                foreach (int j in members)
                {
                    if (j == i) continue;
                    float d = Vector2.DistanceSquared(pi, new Vector2(positions[j].X, positions[j].Y));
                    if (d < d1) { d2 = d1; n2 = n1; d1 = d; n1 = j; }
                    else if (d < d2) { d2 = d; n2 = j; }
                }

                neighbours[i] = n2 >= 0 ? [n1, n2] : n1 >= 0 ? [n1] : [];
            }
        }

        var blended = new float[vertCount];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < vertCount; i++) blended[i] = positions[i].Z;

            foreach (var members in byRing.Values)
            {
                foreach (int i in members)
                {
                    var nb = neighbours[i];
                    if (nb is null || nb.Length == 0) continue;

                    float sum = 0f;
                    foreach (int j in nb) sum += positions[j].Z;
                    blended[i] = (positions[i].Z + (sum / nb.Length)) * 0.5f;
                }
            }

            for (int i = 0; i < vertCount; i++) positions[i].Z = blended[i];
        }
    }

    private static float[]? SmoothedLaunchSlopes(Vector3[]? launchLocal, int passes)
    {
        if (launchLocal is null) return null;

        int n = launchLocal.Length;
        var slopes = new float[n];
        for (int i = 0; i < n; i++) slopes[i] = LaunchSlope(launchLocal[i]);
        if (n < 3) return slopes;

        var blended = new float[n];
        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < n; i++)
                blended[i] = (slopes[(i - 1 + n) % n] + (slopes[i] * 2f) + slopes[(i + 1) % n]) * 0.25f;

            Array.Copy(blended, slopes, n);
        }

        return slopes;
    }

    private static float LaunchSlope(Vector3 launch)
    {
        float inPlane = new Vector2(launch.X, launch.Y).Length();
        if (inPlane < 1e-4f) return launch.Z >= 0f ? MaxLaunchSlope : -MaxLaunchSlope;

        return Math.Clamp(launch.Z / inPlane, -MaxLaunchSlope, MaxLaunchSlope);
    }

    /// <summary>
    /// The body's outward surface normal at each point of the parting line, expressed in the local
    /// frame the flange is built in. This is the direction "straight out of the body" at the rim,
    /// which is what both new sweeps launch along.
    /// </summary>
    private static Vector3[] LocalLaunchDirections(
        IReadOnlyList<Vector3> loop, IMesh body, Quaternion inverseRotation)
    {
        var directions = new Vector3[loop.Count];
        var world = OutwardNormalsAlong(loop, body);
        for (int i = 0; i < loop.Count; i++)
            directions[i] = Vector3.Transform(world[i], inverseRotation);

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
    private static Vector3[] OutwardNormalsAlong(IReadOnlyList<Vector3> loop, IMesh body)
    {
        int n = loop.Count;

        // The same normals the view draws along the line - see SmoothNormalsAt. This used to take the
        // raw normal of whichever single face the point projected onto, which on a rim that is a
        // crease is a different answer by up to a right angle, and it was the direction the flange
        // actually left in. The arrows on screen said one thing and the flange did another.
        var normals = SmoothNormalsAt(body, loop);

        // Two smoothing passes along the loop, then re-normalize.
        for (int pass = 0; pass < 2; pass++)
        {
            var blended = new Vector3[n];
            for (int i = 0; i < n; i++)
                blended[i] = normals[(i - 1 + n) % n] + (normals[i] * 2f) + normals[(i + 1) % n];
            for (int i = 0; i < n; i++)
                normals[i] = blended[i].LengthSquared() > 1e-12f
                    ? Vector3.Normalize(blended[i]) : normals[i];
        }

        return normals;
    }

    // --- Private Wavefront Offsetting & Inside-Out Helpers ---

    /// <summary>
    /// How fast the ring step is allowed to grow from one ring to the next while it is still ramping
    /// up to <c>stepMm</c>. Each ring is at most this much wider than the one inside it, so the
    /// triangles across consecutive bands change size gradually instead of in one jump.
    /// </summary>
    private const float RingGrowth = 1.6f;

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
        float stepMm,
        int maxRings)
    {
        var layers = new List<List<Vector2[]>>(maxRings);

        // Layer 0 is our starting anatomy loop
        var currentIslands = new List<Vector2[]> { inner2D };

        // Ramp from the parting line's own point spacing up to the requested step.
        float step = Math.Clamp(MedianSpacing(inner2D), 0.25f, stepMm);

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

            step = MathF.Min(step * RingGrowth, stepMm);
        }

        return Result.Success(layers);
    }

    /// <summary>Median edge length of a closed contour - its typical point spacing.</summary>
    private static float MedianSpacing(IReadOnlyList<Vector2> contour)
    {
        int n = contour.Count;
        if (n < 2) return 0f;

        var lengths = new float[n];
        for (int i = 0; i < n; i++)
            lengths[i] = Vector2.Distance(contour[i], contour[(i + 1) % n]);

        Array.Sort(lengths);
        return lengths[n / 2];
    }

    /// <summary>
    /// Resamples a closed contour to a uniform arc-length spacing. Below eight points there is no
    /// shape left to preserve, so a contour that short is passed through untouched.
    /// </summary>
    private static Vector2[] ResampleRing(IReadOnlyList<Vector2> contour, float spacingMm)
    {
        int n = contour.Count;
        if (n < 8 || spacingMm <= 1e-4f) return contour.ToArray();

        var cumulative = new float[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + Vector2.Distance(contour[i], contour[(i + 1) % n]);

        float perimeter = cumulative[n];
        if (perimeter < 1e-4f) return contour.ToArray();

        int count = Math.Clamp((int)MathF.Round(perimeter / spacingMm), 8, 20000);
        var result = new Vector2[count];

        int segment = 0;
        for (int k = 0; k < count; k++)
        {
            float target = perimeter * k / count;
            while (segment < n - 1 && cumulative[segment + 1] < target) segment++;

            float span = cumulative[segment + 1] - cumulative[segment];
            float t = span > 1e-6f ? Math.Clamp((target - cumulative[segment]) / span, 0f, 1f) : 0f;
            result[k] = Vector2.Lerp(contour[segment], contour[(segment + 1) % n], t);
        }

        return result;
    }

    /// <summary>
    /// Offsets <paramref name="contour"/> by <paramref name="offsetMm"/> (negative shrinks), falling
    /// back to the contour itself if Clipper collapses it. A collapse only costs the bleed margin on
    /// that edge, which is not worth failing the whole flange over.
    /// </summary>
    private static Vector2[] OffsetOrOriginal(IReadOnlyList<Vector2> contour, float offsetMm)
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
        MR.Mesh nativeMesh,
        Vector3[] anatomy3D,
        Vector2[] anatomy2D,
        List<List<Vector2[]>> ribbonLayers,
        Vector2[] innerBleed,
        float concaveBandWidthMm,
        Quaternion worldRotation,
        Vector3[]? launchLocal,
        float launchHoldMm,
        bool rawFlange,
        int launchSmoothingPasses,
        out bool[]? launchedVertices)
    {
        var pts = nativeMesh.points.vec;
        using var validVerts = nativeMesh.topology.getValidVerts();
        int vertCount = (int)pts.size();

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
            var vid = new MR.VertId(i);
            if (!validVerts.test(vid))
                continue;

            var pt = pts[(ulong)i];
            var v2 = new Vector2(pt.x, pt.y);

            int assignedRing = IdentifyVertexRingIndex(v2, anatomy2D, ribbonLayers);
            ringIndices[currentIndex] = assignedRing;

            // Initialize Ring 0 strictly to exact 3D patient anatomy position (including Y/Z height!)
            float startZ = 0f;
            if (assignedRing == 0)
            {
                int closestAnatomyIdx = FindClosestIndex(v2, anatomy2D);
                startZ = anatomy3D[closestAnatomyIdx].Z;
            }
            else if (assignedRing >= 1 && launchLocal is not null
                     && Vector2.Distance(v2, anatomy2D[FindClosestIndex(v2, anatomy2D)]) < launchHoldMm)
            {
                // Carried out along the body's own surface direction rather than left to the
                // relaxation. The relaxation flattens the flange as soon as it leaves the line, so
                // the surface departs the rim in whatever direction the global plane dictates -
                // which is the twist. Continuing the body's slope for one ring makes it leave going
                // the way the body was going, and the rings past this one still relax to level.
                int closestAnatomyIdx = FindClosestIndex(v2, anatomy2D);
                startZ = anatomy3D[closestAnatomyIdx].Z
                       + (launchSlopes![closestAnatomyIdx]
                          * Vector2.Distance(v2, anatomy2D[closestAnatomyIdx]));
                launched![currentIndex] = true;
            }

            localPositions[currentIndex] = new Vector3(pt.x, pt.y, startZ);
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
            nativeMesh, idToIndex,
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
        var smoothingFactors = new float[currentIndex];
        float ringSpan = Math.Max(1, totalLayers);
        for (int i = 0; i < currentIndex; i++)
        {
            float t = Math.Clamp(ringIndices[i] / ringSpan, 0f, 1f);
            float eased = t * t * (3f - 2f * t); // smoothstep
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
            var worldV3 = Vector3.Transform(localPositions[i], worldRotation);
            int idx3 = i * 3;
            worldVertices[idx3] = worldV3.X;
            worldVertices[idx3 + 1] = worldV3.Y;
            worldVertices[idx3 + 2] = worldV3.Z;
        }

        return _engine.CreateMesh(worldVertices.AsSpan(), CollectionsMarshal.AsSpan(filteredTriangles));
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
        Span<(float distSq, float zHeight)> nearest = stackalloc (float, float)[3];

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
                nearest[k] = (float.MaxValue, 0f);
            var v2 = new Vector2(positions[i].X, positions[i].Y);

            for (int p = 0; p < parentIndices.Count; p++)
            {
                int parentIdx = parentIndices[p];
                var parentV2 = new Vector2(positions[parentIdx].X, positions[parentIdx].Y);
                float dSq = Vector2.DistanceSquared(v2, parentV2);

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
            float totalWeight = 0f;
            float weightedZ = 0f;
            for (int k = 0; k < 3; k++)
            {
                if (nearest[k].distSq == float.MaxValue)
                    continue;
                float weight = 1f / (float)Math.Sqrt(Math.Max(1e-5f, nearest[k].distSq));
                weightedZ += nearest[k].zHeight * weight;
                totalWeight += weight;
            }

            if (totalWeight > 0f)
            {
                positions[i].Z = weightedZ / totalWeight;
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
        float factor,
        float[]? perVertexFactor = null)
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

        var newZ = new float[vertCount];
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

                float sum = 0f;
                for (int j = 0; j < nbrs.Count; j++)
                    sum += positions[nbrs[j]].Z;

                float target = sum / nbrs.Count;
                float f = perVertexFactor is null ? factor : perVertexFactor[i];
                newZ[i] = positions[i].Z + f * (target - positions[i].Z);
            }

            for (int i = 0; i < vertCount; i++)
                positions[i].Z = newZ[i];
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
        float maxSlopeDeg,
        int iterations,
        float rate,
        float innerHold,
        bool[]? pinnedVerts = null)
    {
        try
        {
            var axis = Vector3.Normalize(heightAxis);
            var verts = mesh.Vertices;
            var tris = mesh.Triangles;
            int n = verts.Length;
            if (n == 0 || tris.Length < 3)
                return Result.Success(mesh);

            // Decompose each vertex into a height along the axis and an in-plane residual.
            var height = new float[n];
            var planar = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                height[i] = Vector3.Dot(verts[i], axis);
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

                float minR = float.MaxValue, maxR = 0f;
                foreach (int bvi in boundary)
                {
                    float r = Vector3.Distance(planar[bvi], centroid);
                    if (r < minR) minR = r;
                    if (r > maxR) maxR = r;
                }
                float split = 0.5f * (minR + maxR);

                foreach (int bvi in boundary)
                    if (Vector3.Distance(planar[bvi], centroid) < split)
                        innerBoundary[bvi] = true; // inner (parting) loop
            }

            // The inner edge is not hard-pinned: a steep plunge in the parting line itself forces steep
            // faces around a pinned edge that no interior move can relax. Instead every vertex is free to
            // slope-cap, and after each pass the inner edge is sprung back toward its original parting
            // height by innerHold. innerHold == 1 holds the seal exactly (steep faces near a plunge
            // survive); lower values let the seal edge ease a little to shed those faces. h0 is that
            // original height field; the outer rim is left fully free (its XY footprint is preserved
            // regardless, as only the height component ever moves).
            var h0 = (float[])height.Clone();
            var noPins = pinnedVerts != null ? (bool[])pinnedVerts.Clone() : new bool[n];

            float tanLimit = MathF.Tan(maxSlopeDeg * MathF.PI / 180f);
            int faceCount = tris.Length / 3;
            var delta = new float[n];
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
                    float h1 = height[b] - height[a];
                    float h2 = height[c] - height[a];

                    float g11 = Vector3.Dot(e1, e1), g12 = Vector3.Dot(e1, e2), g22 = Vector3.Dot(e2, e2);
                    float det = g11 * g22 - g12 * g12;
                    if (MathF.Abs(det) < 1e-10f)
                        continue;

                    // Gradient coordinates (u,v) in the {e1,e2} basis, then its magnitude in-plane.
                    float u = (h1 * g22 - h2 * g12) / det;
                    float v = (h2 * g11 - h1 * g12) / det;
                    float gradSq = u * u * g11 + 2f * u * v * g12 + v * v * g22;
                    float grad = MathF.Sqrt(MathF.Max(0f, gradSq));
                    if (grad <= tanLimit)
                        continue;

                    // Shrink each vertex's deviation from the face mean by tanLimit/grad -> gradient
                    // drops to exactly the limit.
                    float shrink = tanLimit / grad;
                    float mean = (height[a] + height[b] + height[c]) / 3f;
                    AccumulateShrink(a, mean, shrink, height, noPins, delta, count);
                    AccumulateShrink(b, mean, shrink, height, noPins, delta, count);
                    AccumulateShrink(c, mean, shrink, height, noPins, delta, count);
                }

                for (int i = 0; i < n; i++)
                    if (count[i] > 0)
                        height[i] += rate * (delta[i] / count[i]);

                // Spring the inner seal edge back toward the true parting-line height.
                if (innerHold > 0f)
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

            var meshResult = _engine.CreateMesh(outVerts.AsSpan(), tris.AsSpan());
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
        int vertex, float mean, float shrink, float[] height, bool[] pinned, float[] delta, int[] count)
    {
        if (pinned[vertex])
            return;
        float target = mean + shrink * (height[vertex] - mean);
        delta[vertex] += target - height[vertex];
        count[vertex]++;
    }

    private static int IdentifyVertexRingIndex(
        Vector2 v2,
        Vector2[] anatomy2D,
        List<List<Vector2[]>> ribbonLayers)
    {
        float minDistSq = float.MaxValue;
        // Default to the outermost ribbon (a free vertex) if somehow unmatched; never defaults to the
        // pinned anatomy ring.
        int bestRing = Math.Max(1, ribbonLayers.Count);

        // Check distance to Ring 0 (Anatomy)
        for (int i = 0; i < anatomy2D.Length; i++)
        {
            float dSq = Vector2.DistanceSquared(v2, anatomy2D[i]);
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
                    float dSq = Vector2.DistanceSquared(v2, island[i]);
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
        float minSq = float.MaxValue;
        for (int i = 0; i < loop.Length; i++)
        {
            float dSq = Vector2.DistanceSquared(target, loop[i]);
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
        MR.Mesh nativeMesh,
        int[] idToIndex,
        IReadOnlyList<IReadOnlyList<Vector2>> innerContours,
        IReadOnlyList<IReadOnlyList<Vector2>> outerContours)
    {
        using var validFaces = nativeMesh.topology.getValidFaces();
        ulong faceCap = nativeMesh.topology.faceCapacity();
        var pts = nativeMesh.points.vec; // Need native points for the centroid

        var kept = new List<int>((int)validFaces.count() * 3);

        for (ulong i = 0; i < faceCap; i++)
        {
            var fid = new MR.FaceId((int)i);
            if (!validFaces.test(fid))
                continue;

            var tri = nativeMesh.topology.getTriVerts(fid);
            int v0 = tri.elems._0.get();
            int v1 = tri.elems._1.get();
            int v2 = tri.elems._2.get();

            var p0 = pts[(ulong)v0];
            var p1 = pts[(ulong)v1];
            var p2 = pts[(ulong)v2];
            var centroid = new Vector2((p0.x + p1.x + p2.x) / 3f, (p0.y + p1.y + p2.y) / 3f);

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
        if (loop == null || loop.Count < 3) return GeometryErrors.InvalidPolygon;
        var direction = Vector3.Normalize(planeNormal);
        var rotation = RotationFromZTo(direction);
        var inverseRotation = Quaternion.Inverse(rotation);
        int N = loop.Count;
        var local3D = new Vector3[N];
        for (int i = 0; i < N; i++) local3D[i] = Vector3.Transform(loop[i], inverseRotation);
        using var contours = new MR.Std.Vector_StdVectorMRVector2f();
        using var contour = new MR.Std.Vector_MRVector2f();
        foreach (var p in local3D) contour.pushBack(new MR.Vector2f(p.X, p.Y));
        contours.pushBack(contour);
        var polyMesh = MR.PlanarTriangulation.triangulateContours(contours, null);
        if (polyMesh is null || polyMesh.topology.getValidFaces().count() == 0) return new Error("Geometry.TriangulationFailed", "Failed");
        int vertCount = (int)polyMesh.points.vec.size();
        var positions = new Vector3[vertCount];
        bool[] pinned = new bool[vertCount];
        for (int i = 0; i < vertCount; i++)
        {
            var p2 = polyMesh.points.vec[(ulong)i];
            float z = 0f;
            if (i < N) { z = local3D[i].Z; pinned[i] = true; }
            else { z = local3D[0].Z; pinned[i] = false; }
            positions[i] = new Vector3(p2.x, p2.y, z);
        }
        var triangles = new List<int>();
        var validFaces = polyMesh.topology.getValidFaces();
        ulong faceCap = polyMesh.topology.faceCapacity();
        for (ulong i = 0; i < faceCap; i++)
        {
            var face = new MR.FaceId((int)i);
            if (validFaces.test(face))
            {
                var tri = polyMesh.topology.getTriVerts(face);
                triangles.Add(tri.elems._0.get());
                triangles.Add(tri.elems._1.get());
                triangles.Add(tri.elems._2.get());
            }
        }
        SmoothFlangeHeights(positions, triangles, pinned, vertCount, 60, 0.5f);
        var worldVertices = new double[vertCount * 3];
        for (int i = 0; i < vertCount; i++)
        {
            var worldV3 = Vector3.Transform(positions[i], rotation);
            worldVertices[i * 3] = worldV3.X;
            worldVertices[i * 3 + 1] = worldV3.Y;
            worldVertices[i * 3 + 2] = worldV3.Z;
        }
        return _engine.CreateMesh(worldVertices.AsSpan(), System.Runtime.InteropServices.CollectionsMarshal.AsSpan(triangles));
    }
}