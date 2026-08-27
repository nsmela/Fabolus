using System.Numerics;
using Fabolus.Core.Common;

namespace Fabolus.Core.Geometry;

/// <summary>Settings for <see cref="ThicknessParting"/>.</summary>
public sealed record ThicknessPartingOptions
{
    /// <summary>
    /// How far a face's measured thickness may sit from the median and still count as surface, as a
    /// fraction of that median. Everything outside the band is the corridor the line is confined to.
    ///
    /// <para>
    /// Tighter than it looks like it needs to be, on purpose. The wall swept between the two surfaces
    /// is about as tall as the shell is thick, so a face on it frequently reads close to one wall
    /// thickness too; a loose band swallows those and joins the two surfaces around the rim, leaving
    /// nothing to part. 0.15 sits clear of that on every body measured, and 0.30 still works - the
    /// gap between the two populations is real, it just is not enormous.
    /// </para>
    /// </summary>
    public float SurfaceBand { get; init; } = 0.15f;

    /// <summary>
    /// Taubin passes applied to each loop. The seam arrives as a staircase of triangle edges, so some
    /// relaxation is wanted; this is deliberately gentle, because unlike a silhouette the seam is
    /// already where it should be and smoothing can only move it off.
    /// </summary>
    public int SmoothingPasses { get; init; } = 20;

    /// <summary>Shortest loop worth returning, as a fraction of the mesh's bounding diagonal.</summary>
    public float MinLoopFraction { get; init; } = 0.10f;

    /// <summary>
    /// How much of the surface must fall outside the thickness band for the result to mean anything,
    /// as a fraction of area. The corridor <em>is</em> the border, so a solid that has none has no
    /// border to part along.
    ///
    /// <para>
    /// Without this the method still answers, and answers plausibly, on a solid that is not a shell
    /// at all - every face of a sphere reads its diameter and pairs with the face opposite, so the
    /// sides colour into two hemispheres and the seam comes out a great circle. It is a valid parting
    /// line and its position is entirely arbitrary, which makes it worse than no answer. Real shells
    /// run 7% to 26% corridor, so anything near zero is this case.
    /// </para>
    /// </summary>
    public float MinCorridorFraction { get; init; } = 0.02f;

    public static ThicknessPartingOptions Default { get; } = new();
}

/// <summary>
/// Traces a parting line from wall thickness, without reference to a pull direction.
///
/// <para>
/// The bodies a mould is built around are a surface given thickness, so a face reading one wall
/// thickness is on one of the two surfaces and a face reading anything else is on the extruded
/// border between them. That leaves the two surfaces as separate islands with the border as a
/// corridor between. Every face is then given to whichever surface it can reach more cheaply across
/// the mesh, and the parting line is where the two territories meet.
/// </para>
///
/// <para>
/// Defining the line as a boundary rather than tracing it is what makes it well behaved:
/// </para>
/// <list type="bullet">
///   <item>It is <b>closed unconditionally</b>. The two territories partition the whole surface, and
///     the boundary between a set and its complement on a closed mesh cannot have a loose end. There
///     is no gap to bridge and nothing for a region fill to leak through - the failure that the
///     silhouette path needs its smoothing, pinch and de-looping filters to contain.</item>
///   <item>It runs down the <b>middle of the corridor</b>, because a border face goes to the nearer
///     surface and the tie falls where the two are equidistant.</item>
///   <item>It finds <b>internal holes</b> without being told to. A hole's border is another corridor,
///     so it produces another loop and nothing had to look for it.</item>
/// </list>
///
/// <para>
/// This is an alternative to <see cref="IPartingTools.GeneratePartingLine"/>, not a replacement, and
/// the two answer different questions. The silhouette tracer asks where a mould can be pulled apart
/// along a given direction; this asks where the body's own extrusion border runs. Nothing here checks
/// the result is mouldable - a line can follow the border perfectly and still leave undercuts for a
/// given pull - so a caller that cares about that still has to check it against a direction.
/// </para>
/// </summary>
public static class ThicknessParting
{
    /// <summary>Grid size, in mm, for matching coincident corners - display geometry arrives un-welded.</summary>
    private const float WeldGridMm = 0.001f;

    /// <summary>
    /// Builds the line from a thickness measurement of the same mesh.
    /// </summary>
    /// <param name="thickness">
    /// Per-face measurement from <see cref="IGeometryEvaluators.MeasureWallThickness"/>, taken on
    /// <paramref name="mesh"/> - the two are indexed together, so measuring one mesh and tracing
    /// another silently produces nonsense.
    /// </param>
    /// <param name="projector">
    /// Closest-point projection onto <paramref name="mesh"/>, applied after each relaxation pass so
    /// the returned line stays on the body rather than cutting chords across it. Optional only
    /// because this class is pure geometry and cannot build one itself; every caller that has an
    /// engine should pass it, and <see cref="IPartingTools.CreateSurfaceProjector"/> is where it
    /// comes from. Without it the line is smoothed free of the surface and drifts off it wherever the
    /// body curves.
    /// </param>
    public static Result<PartingLine> Trace(
        IMesh mesh, WallThickness thickness, ThicknessPartingOptions options,
        ISurfaceProjector? projector = null)
    {
        if (mesh is null) return MeshErrors.NullSource;
        if (thickness is null) return MeshErrors.NullSource;

        options ??= ThicknessPartingOptions.Default;

        int faceCount = mesh.Triangles.Length / 3;
        if (faceCount == 0) return MeshErrors.NullSource;
        if (thickness.PerFace.Count != faceCount)
            return new Error("Geometry.ThicknessMismatch",
                "The thickness measurement was taken on a different mesh from the one being traced.");

        float median = thickness.Statistics.Median;
        if (median <= 0f)
            return new Error("Geometry.NoWallThickness",
                "Nothing could be measured through this solid, so it has no border to part along.");

        var surface = Surface.Build(mesh);

        // Non-manifold geometry has to be refused rather than worked around. The line is traced as
        // the boundary of a set of faces, which is a closed cycle only while every edge has exactly
        // two of them; where three meet, the walk is stranded mid-loop and the loop gets closed with
        // a straight chord across the model - a line that visibly leaves the surface. One bad edge in
        // a 4,217-edge body is enough to do it, and running the mesh through Repair first fixes it
        // completely, so the useful thing to do is say so.
        if (surface.NonManifoldEdges > 0)
            return new Error("Geometry.NonManifoldBody",
                $"This body has {surface.NonManifoldEdges} non-manifold edge(s), where more than two " +
                "faces meet. A parting line cannot be traced round it reliably - repair the mesh first.");

        // 1. Surface faces read one wall thickness; the rest are the corridor.
        float low = median * (1f - options.SurfaceBand);
        float high = median * (1f + options.SurfaceBand);
        var onSurface = new bool[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            float t = thickness.PerFace[f];
            onSurface[f] = float.IsFinite(t) && t >= low && t <= high;
        }

        // The corridor is the border. No corridor, no border - and the seam that would come back is
        // arbitrary rather than wrong, which is the more misleading of the two.
        float surfaceArea = 0f, totalArea = 0f;
        for (int f = 0; f < faceCount; f++)
        {
            totalArea += surface.FaceArea[f];
            if (onSurface[f]) surfaceArea += surface.FaceArea[f];
        }
        if (totalArea <= 0f || 1f - (surfaceArea / totalArea) < options.MinCorridorFraction)
            return new Error("Geometry.NoExtrusionBorder",
                "This solid has no border between two surfaces - it is not a surface given thickness, " +
                "so any parting line found would be arbitrary.");

        // 2. Sort the surface faces into the shell's two sides.
        var sideOf = TwoSides(surface, onSurface, thickness.PartnerFace);
        if (!sideOf.Any(s => s == 0) || !sideOf.Any(s => s == 1))
            return new Error("Geometry.SingleSurface",
                "Only one side was found, so there are no two sides to part between.");

        // 3. Give every remaining face to whichever side it reaches more cheaply.
        var toFirst = Spread(surface, f => sideOf[f] == 0);
        var toSecond = Spread(surface, f => sideOf[f] == 1);

        var side = new bool[faceCount];
        for (int f = 0; f < faceCount; f++)
            side[f] = sideOf[f] >= 0 ? sideOf[f] == 0 : toFirst[f] <= toSecond[f];

        // 4. The line is the seam between the two territories, walked as directed edges.
        var seam = DirectedSeam(surface, side);
        if (seam.Count == 0)
            return new Error("Geometry.NoPartingSeam",
                "The two surfaces never meet, so there is no boundary to part along.");

        float minLength = options.MinLoopFraction * surface.Diagonal;
        var loops = new List<Vector3[]>();
        foreach (var (chain, closed) in Chain(surface, seam))
        {
            // A walk that ran out of seam never came back to its start, so joining its ends would
            // draw a chord across the body rather than trace anything.
            if (!closed) continue;

            // Resampled to an even spacing before relaxing. A traced loop steps from mesh vertex to
            // mesh vertex, so its segments run roughly ten to one longest-to-shortest, and the
            // wavefront flange sweep does not terminate on a loop like that. Done by arc length, so
            // unlike the silhouette path's equivalent it stays independent of any pull direction.
            // The trace itself is on the surface by construction - it steps from mesh vertex to mesh
            // vertex - and resampling stays on it too, since it interpolates along those same edges.
            // Only the relaxation can leave, which is why the projector is threaded into it.
            var traced = chain.Select(v => surface.Positions[v]).ToArray();
            var points = Relax(
                Resample(traced, surface.MeanEdgeLength), options.SmoothingPasses, projector);

            // Measured after relaxing, not before. A seam that pinches to a point leaves a loop of a
            // few vertices which is long enough on the mesh but collapses to nothing once relaxed;
            // judging it beforehand lets those through as specks.
            float length = 0f;
            for (int i = 0; i < points.Length; i++)
                length += Vector3.Distance(points[i], points[(i + 1) % points.Length]);
            if (length < minLength) continue;

            loops.Add(points);
        }

        return loops.Count == 0
            ? new Error("Geometry.NoPartingSeam", "Every seam found was too short to be a parting line.")
            : Result.Success(new PartingLine(loops));
    }

    // ---------------------------------------------------------------- territories

    /// <summary>
    /// Sorts the surface faces into the shell's two sides, as 0 or 1; -1 for anything not on a
    /// surface, or on a scrap that could not be tied to either side.
    ///
    /// <para>
    /// Adjacency alone will not do this. It only ever says "same side", so wherever a surface is
    /// interrupted - a stretch where the two sides are not quite a constant offset apart, and the
    /// thickness there reads outside the band - the surface arrives as several disconnected islands
    /// and adjacency has no way to tell which of them are the same side as each other. Taking the
    /// two biggest islands, which is what this used to do, then picks the far surface's largest
    /// fragment as though it were the far surface.
    /// </para>
    ///
    /// <para>
    /// The partner correspondence supplies the missing relation. A face and the face its probe came
    /// out through are the two sides of the shell at that spot, so that pairing says "opposite side"
    /// - and it says so straight across the corridor, which is exactly where adjacency cannot reach.
    /// Walking both relations together, taking neighbours as the same side and partners as the
    /// other, colours the whole surface in one pass however broken up it is.
    /// </para>
    /// </summary>
    private static int[] TwoSides(Surface surface, bool[] onSurface, IReadOnlyList<int> partner)
    {
        int faceCount = surface.FaceCount;
        var side = new int[faceCount];
        Array.Fill(side, -1);

        // Start from the biggest island, so the colouring is anchored to the largest thing present
        // rather than to whichever face happens to come first.
        int start = LargestIslandSeed(surface, onSurface);
        if (start < 0) return side;

        side[start] = 0;
        var queue = new Queue<int>();
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            int f = queue.Dequeue();
            int here = side[f];

            foreach (int g in surface.FaceNeighbours[f])
            {
                if (!onSurface[g] || side[g] >= 0) continue;
                side[g] = here;                     // adjacent surface: same side
                queue.Enqueue(g);
            }

            int across = f < partner.Count ? partner[f] : -1;
            if (across >= 0 && across < faceCount && onSurface[across] && side[across] < 0
                && FacesBack(surface, f, across))
            {
                side[across] = 1 - here;            // through the shell: the other side
                queue.Enqueue(across);
            }
        }

        return side;
    }

    /// <summary>
    /// Whether a probe genuinely crossed the shell rather than grazing out sideways. A real offset
    /// partner faces back the way it came - the two sides of a shell are antiparallel where they
    /// pair up. Near a rim a probe can leave through a face on its <em>own</em> side, and taking
    /// that as an opposite-side link inverts the colouring from there on, splitting one surface in
    /// two. Requiring the partner to face back discards those.
    /// </summary>
    private static bool FacesBack(Surface surface, int face, int partner) =>
        Vector3.Dot(surface.Normals[face], surface.Normals[partner]) < -0.5f;

    /// <summary>The first face of the largest connected island of surface, or -1 if there is none.</summary>
    private static int LargestIslandSeed(Surface surface, bool[] onSurface)
    {
        var seen = new bool[surface.FaceCount];
        int best = -1;
        float bestArea = 0f;
        var stack = new Stack<int>();

        for (int seed = 0; seed < surface.FaceCount; seed++)
        {
            if (!onSurface[seed] || seen[seed]) continue;

            seen[seed] = true;
            stack.Push(seed);
            float area = 0f;
            while (stack.Count > 0)
            {
                int f = stack.Pop();
                area += surface.FaceArea[f];
                foreach (int g in surface.FaceNeighbours[f])
                {
                    if (!onSurface[g] || seen[g]) continue;
                    seen[g] = true;
                    stack.Push(g);
                }
            }

            if (area <= bestArea) continue;
            bestArea = area;
            best = seed;
        }

        return best;
    }

    /// <summary>
    /// Cost from every face to the nearest seed, walking centroid to centroid. Distance rather than
    /// hop count so a coarse patch does not read as nearer than a finely tessellated one beside it.
    /// </summary>
    private static float[] Spread(Surface surface, Func<int, bool> isSeed)
    {
        var distance = new float[surface.FaceCount];
        Array.Fill(distance, float.MaxValue);

        var queue = new PriorityQueue<int, float>();
        for (int f = 0; f < surface.FaceCount; f++)
            if (isSeed(f))
            {
                distance[f] = 0f;
                queue.Enqueue(f, 0f);
            }

        while (queue.TryDequeue(out int face, out float cost))
        {
            if (cost > distance[face]) continue;
            foreach (int next in surface.FaceNeighbours[face])
            {
                float step = cost + Vector3.Distance(surface.Centroids[face], surface.Centroids[next]);
                if (step >= distance[next]) continue;

                distance[next] = step;
                queue.Enqueue(next, step);
            }
        }

        return distance;
    }

    // ---------------------------------------------------------------- seam to loops

    /// <summary>
    /// The seam as <em>directed</em> edges, each oriented so the first territory lies to its left -
    /// taken straight from the winding of the face on that side.
    ///
    /// <para>
    /// Direction is what makes the walk safe. Undirected, a vertex where four seam edges meet offers
    /// two ways to continue and no way to choose; guessing - by straightest turn, say - can pair them
    /// wrongly, and a later walk then dead-ends because the edge it needed has been taken. That chain
    /// never closes, and closing it anyway draws a chord clean across the model.
    /// </para>
    ///
    /// <para>
    /// Directed, the ambiguity is gone. The seam bounds a set of faces on a closed mesh, so it is a
    /// cycle: every vertex has as many seam edges leaving as arriving. Following any unused outgoing
    /// edge therefore always returns to where it started. Which loop a pinch point is split into is
    /// still arbitrary, but every loop is a loop.
    /// </para>
    /// </summary>
    private static List<(int From, int To)> DirectedSeam(Surface surface, bool[] side)
    {
        var seam = new List<(int, int)>();

        // Walked face by face rather than edge by edge, so the seam is literally the boundary of the
        // first territory. Built that way it balances at every vertex whatever the mesh is doing -
        // the boundary of a boundary is empty, and each face contributes a closed triangle whichever
        // of its edges qualify. Reading it off the edge table instead relies on every edge having
        // exactly two faces, and one non-manifold edge is enough to break the cycle and strand a
        // walk mid-loop, which is what draws a chord across the model.
        for (int face = 0; face < surface.FaceCount; face++)
        {
            if (!side[face]) continue;

            for (int corner = 0; corner < 3; corner++)
            {
                int a = surface.Triangles[(face * 3) + corner];
                int b = surface.Triangles[(face * 3) + ((corner + 1) % 3)];

                var key = a < b ? (a, b) : (b, a);
                if (!surface.Edges.TryGetValue(key, out var pair)) continue;

                int across = pair.First == face ? pair.Second : pair.First;
                if (across >= 0 && side[across]) continue;   // same territory, not a boundary

                seam.Add((a, b));
            }
        }

        return seam;
    }

    /// <summary>
    /// Walks the directed seam into closed loops. Every loop closes by construction - see
    /// <see cref="DirectedSeam"/> - so nothing here has to cope with a chain that does not.
    /// </summary>
    private static List<(List<int> Chain, bool Closed)> Chain(Surface surface, List<(int From, int To)> seam)
    {
        var outgoing = new Dictionary<int, List<int>>(seam.Count);
        for (int i = 0; i < seam.Count; i++)
        {
            if (!outgoing.TryGetValue(seam[i].From, out var list))
                outgoing[seam[i].From] = list = new List<int>(2);
            list.Add(i);
        }

        var used = new bool[seam.Count];
        var loops = new List<(List<int>, bool)>();

        for (int start = 0; start < seam.Count; start++)
        {
            if (used[start]) continue;

            int first = seam[start].From;
            var chain = new List<int> { first };
            bool closed = false;

            int step = start;
            while (step >= 0 && !used[step])
            {
                used[step] = true;
                int next = seam[step].To;
                if (next == first) { closed = true; break; }

                chain.Add(next);
                step = NextOut(outgoing, used, next);
            }

            if (chain.Count >= 3) loops.Add((chain, closed));
        }

        return loops;
    }

    /// <summary>The first unused edge leaving <paramref name="vertex"/>, or -1 if none is left.</summary>
    private static int NextOut(Dictionary<int, List<int>> outgoing, bool[] used, int vertex)
    {
        if (!outgoing.TryGetValue(vertex, out var candidates)) return -1;
        foreach (int index in candidates)
            if (!used[index]) return index;
        return -1;
    }

    /// <summary>Resamples a closed loop to an even arc-length spacing.</summary>
    private static Vector3[] Resample(Vector3[] points, float spacing)
    {
        int n = points.Length;
        if (n < 4 || spacing <= 1e-4f) return points;

        var cumulative = new float[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + Vector3.Distance(points[i], points[(i + 1) % n]);

        float perimeter = cumulative[n];
        if (perimeter < 1e-4f) return points;

        int count = Math.Clamp((int)MathF.Round(perimeter / spacing), 8, 20000);
        var result = new Vector3[count];

        int segment = 0;
        for (int k = 0; k < count; k++)
        {
            float target = perimeter * k / count;
            while (segment < n - 1 && cumulative[segment + 1] < target) segment++;

            float span = cumulative[segment + 1] - cumulative[segment];
            float t = span > 1e-6f ? Math.Clamp((target - cumulative[segment]) / span, 0f, 1f) : 0f;
            result[k] = Vector3.Lerp(points[segment], points[(segment + 1) % n], t);
        }

        return result;
    }

    /// <summary>
    /// Taubin relaxation. A plain Laplacian would drag the loop toward its own centre and shrink it
    /// off the border it is describing; alternating a shrinking pass with an inflating one smooths
    /// the triangle-scale staircase without that drift.
    /// </summary>
    private static Vector3[] Relax(Vector3[] points, int passes, ISurfaceProjector? projector)
    {
        const float Lambda = 0.55f;
        const float Mu = -0.58f;

        int n = points.Length;
        if (n < 4 || passes <= 0) return points;

        var work = points;
        var scratch = new Vector3[n];
        for (int pass = 0; pass < passes; pass++)
        {
            Sweep(work, scratch, Lambda);
            (work, scratch) = (scratch, work);
            Sweep(work, scratch, Mu);
            (work, scratch) = (scratch, work);

            // Back onto the surface after every pass, not once at the end. Relaxation moves each
            // point toward the midpoint of its neighbours, which is a chord - so on anything curved
            // it leaves the surface, and over a run of passes the loop drifts inward far enough to
            // read as jumping the gap where the body is concave (up to 1.3mm on larynx_bolus before
            // this). Projecting per pass keeps each move small, which matters for more than tidiness:
            // a point allowed to wander first and be projected afterwards can come back on the wrong
            // side of a thin wall, where one held close never leaves the sheet it started on.
            Project(work, projector);
        }

        return work;

        static void Sweep(Vector3[] source, Vector3[] destination, float factor)
        {
            int count = source.Length;
            for (int i = 0; i < count; i++)
            {
                var midpoint = (source[(i - 1 + count) % count] + source[(i + 1) % count]) * 0.5f;
                destination[i] = source[i] + (factor * (midpoint - source[i]));
            }
        }

        static void Project(Vector3[] points, ISurfaceProjector? projector)
        {
            if (projector is null) return;
            for (int i = 0; i < points.Length; i++) points[i] = projector.Project(points[i]);
        }
    }

    // ---------------------------------------------------------------- topology

    /// <summary>
    /// A welded view of the mesh: coincident corners merged, face adjacency, centroids and areas.
    /// Deliberately not shared with <see cref="RidgeDetection"/>'s equivalent - that one carries the
    /// per-edge fold measurements this has no use for, and the two would only be coupled by their
    /// shared prefix.
    /// </summary>
    private sealed class Surface
    {
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Centroids { get; init; }
        public required Vector3[] Normals { get; init; }
        public required int[] Triangles { get; init; }
        public required float[] FaceArea { get; init; }
        public required int[][] FaceNeighbours { get; init; }
        public required Dictionary<(int, int), (int First, int Second)> Edges { get; init; }
        public required float Diagonal { get; init; }
        public required float MeanEdgeLength { get; init; }
        public required int NonManifoldEdges { get; init; }

        public int FaceCount => FaceArea.Length;

        public static Surface Build(IMesh mesh)
        {
            var sourceVertices = mesh.Vertices;
            var sourceTriangles = mesh.Triangles;
            int faceCount = sourceTriangles.Length / 3;

            var lookup = new Dictionary<(int, int, int), int>(sourceVertices.Length);
            var welded = new int[sourceVertices.Length];
            for (int i = 0; i < sourceVertices.Length; i++)
            {
                var v = sourceVertices[i];
                var key = (
                    (int)MathF.Round(v.X / WeldGridMm),
                    (int)MathF.Round(v.Y / WeldGridMm),
                    (int)MathF.Round(v.Z / WeldGridMm));

                if (!lookup.TryGetValue(key, out int id))
                {
                    id = lookup.Count;
                    lookup[key] = id;
                }
                welded[i] = id;
            }

            var positions = new Vector3[lookup.Count];
            for (int i = 0; i < sourceVertices.Length; i++) positions[welded[i]] = sourceVertices[i];

            var triangles = new int[sourceTriangles.Length];
            for (int i = 0; i < sourceTriangles.Length; i++) triangles[i] = welded[sourceTriangles[i]];

            var centroids = new Vector3[faceCount];
            var areas = new float[faceCount];
            var normals = new Vector3[faceCount];
            for (int f = 0; f < faceCount; f++)
            {
                var a = positions[triangles[f * 3]];
                var b = positions[triangles[(f * 3) + 1]];
                var c = positions[triangles[(f * 3) + 2]];

                var cross = Vector3.Cross(b - a, c - a);
                float length = cross.Length();

                centroids[f] = (a + b + c) / 3f;
                areas[f] = length * 0.5f;
                normals[f] = length < 1e-12f ? Vector3.Zero : cross / length;
            }

            var edges = new Dictionary<(int, int), (int, int)>(faceCount * 2);
            int nonManifold = 0;
            var neighbours = new List<int>[faceCount];
            for (int f = 0; f < faceCount; f++) neighbours[f] = new List<int>(3);

            for (int f = 0; f < faceCount; f++)
                for (int e = 0; e < 3; e++)
                {
                    int a = triangles[(f * 3) + e];
                    int b = triangles[(f * 3) + ((e + 1) % 3)];
                    var key = a < b ? (a, b) : (b, a);

                    if (!edges.TryGetValue(key, out var pair))
                    {
                        edges[key] = (f, -1);
                        continue;
                    }

                    // A third face on the same edge is non-manifold; the first pair keeps the
                    // edge, and the count tells the caller the body cannot be traced reliably.
                    if (pair.Item2 >= 0) { nonManifold++; continue; }

                    edges[key] = (pair.Item1, f);
                    neighbours[pair.Item1].Add(f);
                    neighbours[f].Add(pair.Item1);
                }

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            double edgeTotal = 0d;
            foreach (var key in edges.Keys)
                edgeTotal += Vector3.Distance(positions[key.Item1], positions[key.Item2]);
            float meanEdge = edges.Count > 0 ? (float)(edgeTotal / edges.Count) : 1f;

            return new Surface
            {
                Positions = positions,
                Centroids = centroids,
                Normals = normals,
                Triangles = triangles,
                FaceArea = areas,
                FaceNeighbours = neighbours.Select(l => l.ToArray()).ToArray(),
                Edges = edges,
                Diagonal = (max - min).Length(),
                MeanEdgeLength = meanEdge,
                NonManifoldEdges = nonManifold,
            };
        }
    }
}
