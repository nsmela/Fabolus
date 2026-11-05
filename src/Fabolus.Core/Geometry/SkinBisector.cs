using BasicResults;
using System.Collections.Immutable;
using GeometryEngine.Core.Geometry;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The parting surface as the set of points equally far from the body's two skins - the two pieces of
/// its surface the parting line divides it into. See <c>PartingMeshSweep.SkinBisector</c>.
///
/// <para>
/// Every other flange is built outward from the line, a ring at a time, against one axis: the planar
/// sweeps offset its footprint, the mould loft maps it round the mould's outline by bearing. Each of
/// those needs the line to look like a simple loop from somewhere. larynx_large's outer line does not
/// from anywhere - it climbs 85 mm, crosses itself 19 times seen along its own axis, and the best of
/// 340 axes still leaves 69 degrees of it running backwards - so every one of them folds through the
/// body and the mould comes out in one piece.
/// </para>
///
/// <para>
/// This asks a different question, which has an answer for any line that divides the body: which skin
/// is each point of the mould nearer to? The answer is a field, the difference of the distances to the
/// two skins, and the parting surface is where it is zero. Inside a thin body that is the mid-surface;
/// past the rim it carries on out the way the body was going, and where two parts of the surface reach
/// toward each other - into a notch, across the hole through a body - the two fronts meet halfway
/// rather than passing through each other, because a field has one value at each point. So it cannot
/// fold, it spans a hole through the body without a patch of its own, and it is meshed by the engine's
/// level set rather than stitched.
/// </para>
///
/// <para>
/// What it does not do is consult the pull axis. The surface follows the body's own shape out to the
/// mould wall, so on a body the mould loft handles it is much steeper: median face 69 degrees off level
/// on chin, where the loft gives 2. It is for the bodies the loft cannot part.
/// </para>
/// </summary>
public static class SkinBisector
{
    /// <summary>
    /// How far each skin stops short of the line, in mm. The faces the line runs over are split
    /// between the skins raggedly, a face at a time, and past the rim the surface is decided by
    /// exactly that edge: left ragged, every corner of it throws a fin out to the mould wall (on
    /// larynx_large 12,452 mm² of surface in the mould's material against 8,614 trimmed). Trimming
    /// each skin along a smooth curve this far from the line removes them. Kept above the body's
    /// edge length, so a vertex on the wrong side of the line is always inside the trimmed strip.
    /// </summary>
    public const double DefaultSkinMarginMm = 2.0;

    /// <summary>Grid cell for the level set, in mm. Coarsened by the engine past 1.5 million cells.</summary>
    public const double DefaultCellMm = 1.0;

    /// <summary>Most cells the engine's level set takes before coarsening its grid.</summary>
    private const double MaxCells = 1.5e6;

    /// <summary>How far past the mould the field is meshed, so the surface leaves it on every side.</summary>
    private const double BoundsMarginMm = 5.0;

    /// <summary>
    /// Coarse field nodes per side, in fine cells. The field is the difference of two nearest-point
    /// queries, which is most of the cost; far from the surface only its sign matters, and the coarse
    /// grid settles that without asking.
    /// </summary>
    private const int CoarseStride = 4;

    public static readonly Error LineDoesNotDivideBody = new("Geometry.PartingLineDoesNotDivideBody",
        "The parting line does not divide the body's surface into two skins, so there is nothing for " +
        "the parting surface to lie between. The line has a gap, or follows a rim that merges into another.");

    /// <summary>
    /// The bisector between the skins either side of <paramref name="line"/>, as an open surface
    /// reaching past <paramref name="mould"/> (or past the body, when there is no mould) on every side.
    /// Faces deeper inside the body than <paramref name="innerBleedMm"/> are left out: the body is
    /// cut away from the mould anyway, and that keeps the flange's inner edge inside the body by the
    /// same bleed the other sweeps use.
    /// </summary>
    public static Result<IMesh> Build(
        IGeometryEngine engine,
        PartingLine line,
        IMesh body,
        IMesh? mould,
        double innerBleedMm = 2.5,
        double cellMm = DefaultCellMm,
        double skinMarginMm = DefaultSkinMarginMm)
    {
        if (engine is null || body is null) return MeshErrors.NullSource;
        if (body.IsEmpty) return MeshErrors.InvalidMesh;
        if (line is null || !line.IsValid) return MeshErrors.InvalidPartingLine;
        if (cellMm <= 0.0) return new Error("Geometry.InvalidVoxelSize", "Cell size must be positive.");

        var skins = Skins(engine, body, line, skinMarginMm);
        if (skins.IsFailure) return skins.Error;

        var a = engine.Spatial.BuildIndex(skins.Value.A);
        if (a.IsFailure) return a.Error;
        using var skinA = a.Value;

        var b = engine.Spatial.BuildIndex(skins.Value.B);
        if (b.IsFailure) return b.Error;
        using var skinB = b.Value;

        double Exact(Vector3 p) => Distance(skinA, p) - Distance(skinB, p);

        var (min, max) = Bounds((mould is { IsEmpty: false } ? mould : body).Vertices);
        var margin = new Vector3(BoundsMarginMm, BoundsMarginMm, BoundsMarginMm);
        min -= margin;
        max += margin;

        // The engine coarsens its grid past a budget rather than failing; the coarse field below has
        // to know the cell it will actually be sampled at.
        var span = max - min;
        double cell = Math.Max(cellMm, Math.Cbrt(span.X * span.Y * span.Z / MaxCells));
        var field = new CoarseField(Exact, min, max, cell * CoarseStride, cell);

        var solid = engine.Generators.GenerateLevelSet(new LevelSetSpec(field.Evaluate, min, max, cell, Name: "skin bisector"));
        if (solid.IsFailure) return solid.Error;

        var bodyIndex = engine.Spatial.IndexFor(body);
        if (bodyIndex.IsFailure) return bodyIndex.Error;

        return Surface(engine, solid.Value, min, max, bodyIndex.Value, innerBleedMm);
    }

    /// <summary>
    /// The two skins, each trimmed back from the line by <paramref name="marginMm"/>.
    ///
    /// <para>
    /// The faces the line runs over are a barrier, joined from one point's face to the next by the
    /// shortest chain across shared edges, and the rest are flooded into components across edges; the
    /// two biggest are the skins. The chain is what makes the barrier hold. Projecting the chord between
    /// two line points onto the surface instead - the obvious way to find the faces in between - leaks
    /// on four bodies out of eight: where the chord cuts inside a thin wall, the nearest surface to it
    /// jumps from one skin to the other and the faces between are never marked.
    /// </para>
    /// </summary>
    private static Result<(IMesh A, IMesh B)> Skins(IGeometryEngine engine, IMesh body, PartingLine line, double marginMm)
    {
        var vs = body.Vertices;
        var ts = body.Triangles;
        int faces = ts.Length / 3;

        var edgeFaces = new Dictionary<(int, int), List<int>>(ts.Length);
        for (int f = 0; f < faces; f++)
            for (int k = 0; k < 3; k++)
            {
                var key = Edge(ts[(3 * f) + k], ts[(3 * f) + ((k + 1) % 3)]);
                if (!edgeFaces.TryGetValue(key, out var list)) edgeFaces[key] = list = new List<int>(2);
                list.Add(f);
            }

        var centre = new Vector3[faces];
        for (int f = 0; f < faces; f++)
            centre[f] = (vs[ts[3 * f]] + vs[ts[(3 * f) + 1]] + vs[ts[(3 * f) + 2]]) / 3.0;

        var index = engine.Spatial.IndexFor(body);
        if (index.IsFailure) return index.Error;

        var barrier = new bool[faces];
        foreach (var loop in line.Loops)
        {
            var at = new int[loop.Count];
            for (int i = 0; i < loop.Count; i++)
            {
                var hit = index.Value.ClosestPoint(loop[i]);
                at[i] = hit.HasValue && hit.Value.Triangle >= 0 && hit.Value.Triangle < faces ? hit.Value.Triangle : -1;
            }

            for (int i = 0; i < loop.Count; i++)
            {
                int from = at[i], to = at[(i + 1) % loop.Count];
                if (from < 0 || to < 0) continue;

                barrier[from] = barrier[to] = true;
                if (from != to)
                    Chain(from, to, (3.0 * loop[i].DistanceTo(loop[(i + 1) % loop.Count])) + 5.0);
            }
        }

        var label = new int[faces];
        Array.Fill(label, -1);
        var sizes = new List<int>();
        var stack = new Stack<int>();
        for (int seed = 0; seed < faces; seed++)
        {
            if (barrier[seed] || label[seed] >= 0) continue;

            int id = sizes.Count, size = 0;
            label[seed] = id;
            stack.Push(seed);
            while (stack.Count > 0)
            {
                int f = stack.Pop();
                size++;
                foreach (int g in Neighbours(f))
                {
                    if (barrier[g] || label[g] >= 0) continue;
                    label[g] = id;
                    stack.Push(g);
                }
            }
            sizes.Add(size);
        }

        if (sizes.Count < 2) return LineDoesNotDivideBody;

        var order = Enumerable.Range(0, sizes.Count).OrderByDescending(i => sizes[i]).ToList();
        int first = order[0], second = order[1];

        // A line that leaves the second skin a sliver has not divided the body; it has nicked it.
        if (sizes[second] < 0.05 * sizes[first]) return LineDoesNotDivideBody;

        // Each vertex's side, from the skins its faces fell into. A vertex only barrier faces touch
        // takes its side from neighbours that have one.
        var side = new int[vs.Length];
        for (int f = 0; f < faces; f++)
        {
            int vote = label[f] == first ? 1 : label[f] == second ? -1 : 0;
            for (int k = 0; k < 3; k++) side[ts[(3 * f) + k]] += vote;
        }

        for (int pass = 0; pass < 8; pass++)
        {
            bool any = false;
            for (int f = 0; f < faces; f++)
                for (int k = 0; k < 3; k++)
                {
                    int v = ts[(3 * f) + k];
                    if (side[v] != 0) continue;
                    side[v] = Math.Sign(side[ts[(3 * f) + ((k + 1) % 3)]] + side[ts[(3 * f) + ((k + 2) % 3)]]);
                    any |= side[v] != 0;
                }
            if (!any) break;
        }

        // Distance from the line, signed by side, so each skin is cut along a curve a margin from it.
        var segments = new List<(Vector3 P, Vector3 Q)>();
        foreach (var loop in line.Loops)
            for (int i = 0; i < loop.Count; i++) segments.Add((loop[i], loop[(i + 1) % loop.Count]));

        var signedA = new double[vs.Length];
        var signedB = new double[vs.Length];
        Parallel.For(0, vs.Length, v =>
        {
            double d = double.MaxValue;
            foreach (var (p, q) in segments) d = Math.Min(d, SegmentDistance(vs[v], p, q));
            signedA[v] = Math.Sign(side[v]) * d;
            signedB[v] = -signedA[v];
        });

        var skinA = Trim(engine, vs, ts, signedA, marginMm, "skin A");
        if (skinA.IsFailure) return skinA.Error;
        var skinB = Trim(engine, vs, ts, signedB, marginMm, "skin B");
        if (skinB.IsFailure) return skinB.Error;
        if (skinA.Value.IsEmpty || skinB.Value.IsEmpty) return LineDoesNotDivideBody;

        return (skinA.Value, skinB.Value);

        IEnumerable<int> Neighbours(int f)
        {
            for (int k = 0; k < 3; k++)
                foreach (int g in edgeFaces[Edge(ts[(3 * f) + k], ts[(3 * f) + ((k + 1) % 3)])])
                    if (g != f) yield return g;
        }

        void Chain(int from, int to, double reach)
        {
            var cost = new Dictionary<int, double> { [from] = 0.0 };
            var back = new Dictionary<int, int>();
            var queue = new PriorityQueue<int, double>();
            queue.Enqueue(from, 0.0);
            while (queue.TryDequeue(out int f, out double d))
            {
                if (f == to) break;
                if (d > cost[f] || d > reach) continue;
                foreach (int g in Neighbours(f))
                {
                    double next = d + centre[f].DistanceTo(centre[g]);
                    if (cost.TryGetValue(g, out double seen) && seen <= next) continue;
                    cost[g] = next;
                    back[g] = f;
                    queue.Enqueue(g, next);
                }
            }

            for (int f = to; back.ContainsKey(f); f = back[f]) barrier[f] = true;
        }
    }

    /// <summary>
    /// The part of the surface where <paramref name="value"/> is at least <paramref name="level"/>,
    /// with the faces it runs through cut along the isoline.
    /// </summary>
    private static Result<IMesh> Trim(
        IGeometryEngine engine, ImmutableArray<Vector3> vs, ImmutableArray<int> ts, double[] value, double level, string name)
    {
        var vertices = ImmutableArray.CreateBuilder<Vector3>();
        var triangles = ImmutableArray.CreateBuilder<int>();
        var kept = new Dictionary<int, int>();
        var cuts = new Dictionary<(int, int), int>();

        for (int f = 0; f + 2 < ts.Length; f += 3)
        {
            int[] v = [ts[f], ts[f + 1], ts[f + 2]];
            bool[] inside = [value[v[0]] >= level, value[v[1]] >= level, value[v[2]] >= level];
            int count = inside.Count(x => x);
            if (count == 0) continue;
            if (count == 3)
            {
                triangles.Add(Keep(v[0])); triangles.Add(Keep(v[1])); triangles.Add(Keep(v[2]));
                continue;
            }

            // Turned so the odd corner out comes first, which keeps the winding.
            int r = Array.IndexOf(inside, count == 1);
            int a = v[r], b = v[(r + 1) % 3], c = v[(r + 2) % 3];
            if (count == 1)
            {
                triangles.Add(Keep(a)); triangles.Add(Cut(a, b)); triangles.Add(Cut(a, c));
            }
            else
            {
                int ab = Cut(a, b), ac = Cut(a, c);
                triangles.Add(ab); triangles.Add(Keep(b)); triangles.Add(Keep(c));
                triangles.Add(ab); triangles.Add(Keep(c)); triangles.Add(ac);
            }
        }

        return engine.CreateMesh(vertices.ToImmutable(), triangles.ToImmutable(), MeshMetadata.Named(name));

        int Keep(int v)
        {
            if (!kept.TryGetValue(v, out int k))
            {
                k = kept[v] = vertices.Count;
                vertices.Add(vs[v]);
            }
            return k;
        }

        int Cut(int u, int w)
        {
            var key = Edge(u, w);
            if (!cuts.TryGetValue(key, out int k))
            {
                double t = (level - value[u]) / (value[w] - value[u]);
                k = cuts[key] = vertices.Count;
                vertices.Add(vs[u].LerpTo(vs[w], Math.Clamp(t, 0.0, 1.0)));
            }
            return k;
        }
    }

    /// <summary>
    /// The level set's boundary less the faces on the box it was clipped to, and less the faces
    /// deeper inside the body than the bleed.
    /// </summary>
    private static Result<IMesh> Surface(
        IGeometryEngine engine, IMesh solid, Vector3 min, Vector3 max, ISpatialIndex body, double innerBleedMm)
    {
        var vs = solid.Vertices;
        var ts = solid.Triangles;
        const double onBox = 1e-4;
        bool OnBox(Vector3 p) =>
            Math.Abs(p.X - min.X) < onBox || Math.Abs(p.X - max.X) < onBox ||
            Math.Abs(p.Y - min.Y) < onBox || Math.Abs(p.Y - max.Y) < onBox ||
            Math.Abs(p.Z - min.Z) < onBox || Math.Abs(p.Z - max.Z) < onBox;

        var centres = ImmutableArray.CreateBuilder<Vector3>(ts.Length / 3);
        for (int f = 0; f + 2 < ts.Length; f += 3)
            centres.Add((vs[ts[f]] + vs[ts[f + 1]] + vs[ts[f + 2]]) / 3.0);
        var depth = body.SignedDistances(centres.MoveToImmutable());

        var map = new Dictionary<int, int>();
        var vertices = ImmutableArray.CreateBuilder<Vector3>();
        var triangles = ImmutableArray.CreateBuilder<int>();
        for (int f = 0; f + 2 < ts.Length; f += 3)
        {
            // Any face touching the box, not only those lying on it: the walls meet the surface through
            // a band of faces a cell wide, one corner on the surface and the others on the wall.
            if (OnBox(vs[ts[f]]) || OnBox(vs[ts[f + 1]]) || OnBox(vs[ts[f + 2]])) continue;
            if (depth[f / 3] < -innerBleedMm) continue;

            for (int k = 0; k < 3; k++)
            {
                int v = ts[f + k];
                if (!map.TryGetValue(v, out int m))
                {
                    m = map[v] = vertices.Count;
                    vertices.Add(vs[v]);
                }
                triangles.Add(m);
            }
        }

        if (triangles.Count == 0) return MeshErrors.InvalidMesh;
        return engine.CreateMesh(vertices.ToImmutable(), triangles.ToImmutable(), MeshMetadata.Named("parting flange"));
    }

    /// <summary>
    /// The field, answered from a coarse grid wherever that settles it.
    ///
    /// <para>
    /// Each distance moves at most a millimetre per millimetre, so their difference moves at most two.
    /// A coarse node's exact value therefore bounds the field anywhere near it, and wherever that bound
    /// keeps it more than a couple of fine cells clear of zero, the sign is certain and nothing finer is
    /// asked. Only the sign matters out there: the level set reads the field's value only on cell edges
    /// it changes sign along, and no such edge can reach a point whose field is that far from zero.
    /// </para>
    /// </summary>
    private sealed class CoarseField
    {
        private readonly Func<Vector3, double> _exact;
        private readonly Vector3 _min;
        private readonly double _step;
        private readonly double _clear;
        private readonly int _nx, _ny, _nz;
        private readonly double[] _values;

        public CoarseField(Func<Vector3, double> exact, Vector3 min, Vector3 max, double step, double cell)
        {
            _exact = exact;
            _min = min;
            _step = step;
            _clear = 2.5 * cell;
            var span = max - min;
            _nx = (int)Math.Ceiling(span.X / step) + 1;
            _ny = (int)Math.Ceiling(span.Y / step) + 1;
            _nz = (int)Math.Ceiling(span.Z / step) + 1;
            _values = new double[_nx * _ny * _nz];
            Parallel.For(0, _values.Length, i =>
            {
                int x = i % _nx, y = (i / _nx) % _ny, z = i / (_nx * _ny);
                _values[i] = _exact(Node(x, y, z));
            });
        }

        private Vector3 Node(int x, int y, int z) => _min + new Vector3(x * _step, y * _step, z * _step);

        public double Evaluate(Vector3 p)
        {
            var local = (p - _min) / _step;
            int x0 = Math.Clamp((int)Math.Floor(local.X), 0, _nx - 2);
            int y0 = Math.Clamp((int)Math.Floor(local.Y), 0, _ny - 2);
            int z0 = Math.Clamp((int)Math.Floor(local.Z), 0, _nz - 2);

            double best = 0.0;
            double sign = 0.0;
            for (int dz = 0; dz < 2; dz++)
            for (int dy = 0; dy < 2; dy++)
            for (int dx = 0; dx < 2; dx++)
            {
                int x = x0 + dx, y = y0 + dy, z = z0 + dz;
                double value = _values[x + (_nx * (y + (_ny * z)))];
                double bound = Math.Abs(value) - (2.0 * p.DistanceTo(Node(x, y, z)));
                if (bound <= best) continue;
                best = bound;
                sign = Math.Sign(value);
            }

            return best > _clear ? sign * best : _exact(p);
        }
    }

    private static double Distance(ISpatialIndex index, Vector3 p)
    {
        var hit = index.ClosestPoint(p);
        return hit.HasValue ? hit.Value.Distance : double.MaxValue / 4;
    }

    private static double SegmentDistance(Vector3 c, Vector3 p, Vector3 q)
    {
        var d = q - p;
        double length = d.LengthSquared;
        double t = length < 1e-12 ? 0.0 : Math.Clamp((c - p).Dot(d) / length, 0.0, 1.0);
        return c.DistanceTo(p + (d * t));
    }

    private static (int, int) Edge(int u, int w) => u < w ? (u, w) : (w, u);

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
}
