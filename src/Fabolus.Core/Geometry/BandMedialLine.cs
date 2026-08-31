using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>Settings for <see cref="BandMedialLine"/>.</summary>
public sealed record BandMedialOptions
{
    /// <summary>
    /// How narrow the wall may read, as a fraction of its own median width, before that stretch is
    /// left out of the solve.
    ///
    /// <para>
    /// One-sided on purpose. The quantity that says where the middle is divides by the width, so a
    /// width near zero is the one case with no answer at all rather than a hard one. A <em>wide</em>
    /// stretch has a perfectly good middle, and excluding it to bridge straight across replaces a
    /// medial line with a chord - measured, doing that took <c>standard</c> from 3.4% of its points off
    /// centre to 11.0%, and <c>ear</c> from 2.5% to 17.6%.
    /// </para>
    /// </summary>
    public float NarrowestWidth { get; init; } = 0.65f;

    /// <summary>
    /// How much of the wall may be pinched before the medial line is refused altogether.
    ///
    /// <para>
    /// A gate on whether the wall is a wall, and the one that decides which bodies this method suits.
    /// It separates the sample set cleanly: the finely meshed bodies pinch 0% to 5% of their length and
    /// the medial line beats the corrected trace on every measure there, while the coarse STL bodies
    /// pinch 19% to 24% - their bands are two or three faces across, so the field that locates the
    /// middle is quantised to a handful of values and the level set through it is noise. Measured on
    /// those, the medial line puts <em>more</em> points off centre than the correction it would replace
    /// - 24% against 2.8% on <c>eye_bolus</c> - and on <c>scalp_bolus</c> it does not close at all.
    /// </para>
    ///
    /// <para>
    /// Expressed as pinched fraction rather than as a face count across the band because it is the
    /// quantity that actually matters and it is already being computed. A band can be coarse and even,
    /// and this lets that through; what it stops is a band whose width the mesh cannot resolve.
    /// </para>
    ///
    /// <para>
    /// Zero, for now, which is stricter than the evidence alone requires and is a statement about the
    /// bridge rather than about the method. Where nothing is pinched the level set closes on its own
    /// and is the best line by every measure taken. Where anything is pinched it arrives in arcs and
    /// the gaps have to be walked, and that walk is still a path from one face centre to the next: on
    /// <c>chin</c>, 2% pinched is enough for it to detour and come back within 0.65mm of the line it
    /// just laid, a 160 degree turn that no amount of smoothing afterwards will take out. Raise this
    /// when the bridge is a curve rather than a walk.
    /// </para>
    /// </summary>
    public float MostPinched { get; init; } = 0f;

    /// <summary>
    /// How many faces must span the wall before a level set through it means anything.
    ///
    /// <para>
    /// The field is sampled at vertices, so a wall three faces across carries three or four distinct
    /// values and the 0.5 contour drawn through them is quantisation rather than geometry. Measured on
    /// the sample set, how well the line ends up centred tracks this number and nothing else: at 5.6
    /// faces across the medial line puts 3.8% of its points off centre against the correction's 8.6%,
    /// at 3.2 to 3.6 the two trade places body by body, and at 1.8 to 2.1 - the coarse STL bodies - it
    /// reaches 15% to 24% against the correction's 0% to 10%.
    /// </para>
    ///
    /// <para>
    /// Set above that middle ground rather than through it. The band from 3.2 to 3.6 is where the two
    /// methods are worth about the same and which one wins depends on the body; there is no reading of
    /// the evidence that says the medial line is reliably better there, so it does not run there.
    /// </para>
    /// </summary>
    public float FewestFacesAcross { get; init; } = 4.5f;

    /// <summary>Taubin passes applied to the extracted curve, with a projection after each.</summary>
    public int SmoothingPasses { get; init; } = 4;

    public static BandMedialOptions Default { get; } = new();
}

/// <summary>
/// The line down the middle of a rim wall, found as the set equidistant from the wall's two creases
/// with distance measured across the wall's own faces.
///
/// <para>
/// An alternative to correcting the traced line rather than an addition to it. Every correction-based
/// attempt has to defend a threshold - which points to move, how far, how to blend where the
/// correction stops - and each of those is a place for a kink. Measured against the correction on
/// <c>standard</c>: a third of the off-centre points, the worst turn down from 85 degrees to 36, even
/// sampling instead of 1.7x, and three times the clearance from itself. It is smoother than the line
/// it replaces because it is not a line that has been pushed about, it is a level set.
/// </para>
///
/// <para>
/// What it cannot do is find a middle where the wall has none. Where a rim tapers to a knife edge the
/// two creases converge and the equidistant set is undefined; those stretches are left out and the gap
/// walked across instead. On the sample set that costs nothing on six bodies - four skip no faces at
/// all - and is the whole story on <c>larynx-large</c>, which pinches over a quarter of its length.
/// </para>
/// </summary>
public static class BandMedialLine
{
    /// <summary>
    /// Traces the medial line of one rim wall, or null when the wall cannot yield a closed one.
    /// </summary>
    /// <param name="faces">The band mask - <see cref="RidgeSurfaces.Band"/>, not <c>Faces</c>.</param>
    /// <param name="faceRims">
    /// Which rim each face belongs to, so a body with two walls solves each separately. A face with no
    /// rim is kept: the mask is closed after the rims are assigned, so the faces that closing added
    /// carry -1 and dropping them would punch the holes back in.
    /// </param>
    public static IReadOnlyList<Vector3>? Trace(
        IMesh mesh, bool[] faces, int[] faceRims, int rim, PartingBand band,
        BandMedialOptions? options = null, ISurfaceProjector? projector = null)
    {
        options ??= BandMedialOptions.Default;

        var surface = Wall.Build(mesh, faces, faceRims, rim, band);
        if (surface is null) return null;

        var width = Width(surface);

        // Refused before anything is computed if the mesh cannot resolve the wall. Cheaper than
        // finding out afterwards, and there is no afterwards to find out from: the curve that comes
        // back from an under-resolved band still closes and still looks like a curve.
        var spans = surface.FaceList.Where(f => !float.IsPositiveInfinity(width[f]))
            .Select(f => width[f]).OrderBy(v => v).ToArray();
        if (spans.Length == 0) return null;

        float median = spans[spans.Length / 2];
        if (median / surface.MeanEdge < options.FewestFacesAcross) return null;

        var narrow = Narrow(surface, width, options.NarrowestWidth);

        var kept = new List<int>(surface.FaceList.Length);
        foreach (int f in surface.FaceList) if (!narrow[f]) kept.Add(f);
        if (kept.Count < 16) return null;

        // Refused rather than attempted on a wall this broken. Past this the level set is being taken
        // through a field the mesh cannot resolve, and the curve that comes back is worse than the one
        // it would replace - which is the failure mode worth guarding, because it still looks like a
        // curve and still closes.
        float pinched = 1f - ((float)kept.Count / surface.FaceList.Length);
        if (pinched > options.MostPinched) return null;

        var field = Field(surface, kept);
        var arcs = Extract(surface, kept, field);
        if (arcs.Count == 0) return null;

        var loop = Assemble(surface, arcs);
        return loop is null ? null : Smooth(loop, surface.MeanEdge, options.SmoothingPasses, projector);
    }

    // ---------------------------------------------------------------- the wall

    private sealed class Wall
    {
        public required IMesh Mesh { get; init; }
        public required int[] FaceList { get; init; }
        public required Vector3[] Centroid { get; init; }
        public required List<int>[] Neighbours { get; init; }
        public required int[] Side { get; init; }
        public required PartingBand Band { get; init; }
        public required float MeanEdge { get; init; }
        public required int FaceCount { get; init; }

        public static Wall? Build(IMesh mesh, bool[] band, int[] faceRims, int rim, PartingBand pair)
        {
            var triangles = mesh.Triangles;
            var vertices = mesh.Vertices;
            int faceCount = triangles.Length / 3;
            if (band.Length != faceCount) return null;

            var list = new List<int>();
            for (int f = 0; f < faceCount; f++)
            {
                if (!band[f]) continue;
                if (faceRims.Length == faceCount && faceRims[f] >= 0 && faceRims[f] != rim) continue;
                list.Add(f);
            }

            if (list.Count < 16) return null;

            var edges = new Dictionary<(int, int), List<int>>(list.Count * 2);
            foreach (int f in list)
                for (int e = 0; e < 3; e++)
                {
                    int a = triangles[(f * 3) + e];
                    int b = triangles[(f * 3) + ((e + 1) % 3)];
                    var key = a < b ? (a, b) : (b, a);
                    if (!edges.TryGetValue(key, out var shared)) edges[key] = shared = new List<int>(2);
                    shared.Add(f);
                }

            var neighbours = new List<int>[faceCount];
            foreach (int f in list) neighbours[f] = new List<int>(3);
            foreach (var shared in edges.Values)
                for (int i = 0; i < shared.Count; i++)
                    for (int j = 0; j < shared.Count; j++)
                        if (i != j) neighbours[shared[i]].Add(shared[j]);

            var centroid = new Vector3[faceCount];
            foreach (int f in list)
                centroid[f] = (vertices[triangles[f * 3]]
                    + vertices[triangles[(f * 3) + 1]]
                    + vertices[triangles[(f * 3) + 2]]) / 3f;

            // A vertex on the wall's own edge belongs to whichever crease it is nearer. That pins the
            // two ends of the field, and it is the only place the creases enter the calculation.
            var side = new int[vertices.Length];
            Array.Fill(side, -1);

            double edgeTotal = 0d;
            foreach (var (key, shared) in edges)
            {
                edgeTotal += Vector3.Distance(vertices[key.Item1], vertices[key.Item2]);
                if (shared.Count >= 2) continue;

                foreach (int v in new[] { key.Item1, key.Item2 })
                    side[v] = Distance(vertices[v], pair.First) <= Distance(vertices[v], pair.Second)
                        ? 0 : 1;
            }

            if (!side.Any(s => s == 0) || !side.Any(s => s == 1)) return null;

            return new Wall
            {
                Mesh = mesh,
                FaceList = list.ToArray(),
                Centroid = centroid,
                Neighbours = neighbours,
                Side = side,
                Band = pair,
                MeanEdge = edges.Count == 0 ? 1f : (float)(edgeTotal / edges.Count),
                FaceCount = faceCount,
            };
        }
    }

    private static float Distance(Vector3 from, RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        float best = float.MaxValue;
        for (int i = 0; i < spans; i++)
        {
            var a = points[i];
            var ab = points[(i + 1) % points.Count] - a;
            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared < 1e-12f
                ? 0f
                : Math.Clamp(Vector3.Dot(from - a, ab) / lengthSquared, 0f, 1f);
            best = MathF.Min(best, Vector3.Distance(from, a + (ab * t)));
        }
        return best;
    }

    // ---------------------------------------------------------------- width and field

    /// <summary>
    /// How far apart the two creases run beside each face - the separation of the nearest point on one
    /// from the nearest point on the other.
    ///
    /// <para>
    /// Measured between the creases rather than from the face to each of them, and the difference is
    /// not a detail. Summing two distances from the face inflates wherever the face sits off the line
    /// joining its two crease points, and summing two <em>geodesics</em> inflates again wherever the
    /// path curves or detours round a notch - so a wall of perfectly even thickness reads as varying.
    /// Measured three ways on the same faces of <c>standard</c>: geodesic sums put 23.5% of the wall
    /// outside a quarter of its median, chords 11.3%, and this 0.2%. The wall is even; the first two
    /// rulers were not. Everything that reads a width off this - the pinch gate, the tolerance flag -
    /// was measuring its own detours before.
    /// </para>
    /// </summary>
    private static float[] Width(Wall wall)
    {
        var vertices = wall.Mesh.Vertices;
        var triangles = wall.Mesh.Triangles;

        var width = new float[wall.FaceCount];
        Array.Fill(width, float.PositiveInfinity);

        foreach (int f in wall.FaceList)
        {
            var centre = (vertices[triangles[f * 3]]
                + vertices[triangles[(f * 3) + 1]]
                + vertices[triangles[(f * 3) + 2]]) / 3f;

            width[f] = Vector3.Distance(
                ClosestPoint(centre, wall.Band.First), ClosestPoint(centre, wall.Band.Second));
        }

        return width;
    }

    private static Vector3 ClosestPoint(Vector3 from, RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        var best = from;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < spans; i++)
        {
            var a = points[i];
            var ab = points[(i + 1) % points.Count] - a;
            float lengthSquared = ab.LengthSquared();
            float t = lengthSquared < 1e-12f
                ? 0f
                : Math.Clamp(Vector3.Dot(from - a, ab) / lengthSquared, 0f, 1f);

            var on = a + (ab * t);
            float distance = Vector3.Distance(from, on);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = on;
        }

        return best;
    }

    private static bool[] Narrow(Wall wall, float[] width, float low)
    {
        var measured = wall.FaceList.Where(f => !float.IsPositiveInfinity(width[f]))
            .Select(f => width[f]).ToArray();

        var narrow = new bool[wall.FaceCount];
        if (measured.Length == 0) return narrow;

        Array.Sort(measured);
        float median = measured[measured.Length / 2];
        if (median < 1e-6f) return narrow;

        foreach (int f in wall.FaceList)
            if (float.IsPositiveInfinity(width[f]) || width[f] < median * low) narrow[f] = true;

        return narrow;
    }

    private static float[] Spread(Wall wall, int side)
    {
        var triangles = wall.Mesh.Triangles;
        var distance = new float[wall.FaceCount];
        Array.Fill(distance, float.PositiveInfinity);

        var queue = new PriorityQueue<int, float>();
        foreach (int f in wall.FaceList)
        {
            bool seed = false;
            for (int e = 0; e < 3; e++)
                if (wall.Side[triangles[(f * 3) + e]] == side) { seed = true; break; }

            if (!seed) continue;
            distance[f] = 0f;
            queue.Enqueue(f, 0f);
        }

        while (queue.TryDequeue(out int face, out float cost))
        {
            if (cost > distance[face]) continue;
            foreach (int next in wall.Neighbours[face])
            {
                float step = cost + Vector3.Distance(wall.Centroid[face], wall.Centroid[next]);
                if (step >= distance[next]) continue;
                distance[next] = step;
                queue.Enqueue(next, step);
            }
        }

        return distance;
    }

    /// <summary>The share of the way across the wall, carried to the vertices so the level set is smooth.</summary>
    private static (float[] Value, int[] Count) Field(Wall wall, List<int> kept)
    {
        var toFirst = Spread(wall, 0);
        var toSecond = Spread(wall, 1);

        var triangles = wall.Mesh.Triangles;
        var total = new float[wall.Mesh.Vertices.Length];
        var count = new int[wall.Mesh.Vertices.Length];

        foreach (int f in kept)
        {
            float a = toFirst[f], b = toSecond[f];
            if (float.IsPositiveInfinity(a) || float.IsPositiveInfinity(b) || a + b < 1e-6f) continue;

            float ratio = a / (a + b);
            for (int e = 0; e < 3; e++)
            {
                int v = triangles[(f * 3) + e];
                total[v] += ratio;
                count[v]++;
            }
        }

        var value = new float[total.Length];
        for (int v = 0; v < value.Length; v++) value[v] = count[v] > 0 ? total[v] / count[v] : 0.5f;
        return (value, count);
    }

    // ---------------------------------------------------------------- level set

    private static List<Vector3[]> Extract(Wall wall, List<int> kept, (float[] Value, int[] Count) field)
    {
        var vertices = wall.Mesh.Vertices;
        var triangles = wall.Mesh.Triangles;
        var segments = new List<(Vector3, Vector3)>(kept.Count);

        foreach (int f in kept)
        {
            var crossings = new List<Vector3>(2);
            bool measurable = true;

            for (int e = 0; e < 3; e++)
            {
                int i = triangles[(f * 3) + e];
                int j = triangles[(f * 3) + ((e + 1) % 3)];
                if (field.Count[i] == 0 || field.Count[j] == 0) { measurable = false; break; }

                float a = field.Value[i], b = field.Value[j];
                if ((a < 0.5f && b < 0.5f) || (a >= 0.5f && b >= 0.5f)) continue;
                if (MathF.Abs(b - a) < 1e-9f) continue;

                crossings.Add(Vector3.Lerp(vertices[i], vertices[j], (0.5f - a) / (b - a)));
            }

            if (measurable && crossings.Count == 2) segments.Add((crossings[0], crossings[1]));
        }

        return Walk(segments, wall.MeanEdge * 0.05f);
    }

    /// <summary>Welds segment ends on a grid and walks them into runs, closed or open.</summary>
    private static List<Vector3[]> Walk(List<(Vector3 A, Vector3 B)> segments, float weld)
    {
        var points = new List<Vector3>();
        var lookup = new Dictionary<(int, int, int), int>(segments.Count * 2);

        int Key(Vector3 p)
        {
            var cell = ((int)MathF.Round(p.X / weld), (int)MathF.Round(p.Y / weld),
                        (int)MathF.Round(p.Z / weld));
            if (lookup.TryGetValue(cell, out int found)) return found;

            lookup[cell] = points.Count;
            points.Add(p);
            return points.Count - 1;
        }

        var links = new Dictionary<int, List<int>>();
        foreach (var (a, b) in segments)
        {
            int i = Key(a), j = Key(b);
            if (i == j) continue;

            if (!links.TryGetValue(i, out var fi)) links[i] = fi = new List<int>(2);
            if (!links.TryGetValue(j, out var fj)) links[j] = fj = new List<int>(2);
            if (!fi.Contains(j)) fi.Add(j);
            if (!fj.Contains(i)) fj.Add(i);
        }

        var used = new HashSet<int>();
        var runs = new List<Vector3[]>();

        // Free ends first, so an open run is walked from its end rather than from the middle - which
        // would otherwise split one arc into two facing each other.
        foreach (int start in links.Keys.Where(k => links[k].Count == 1).Concat(links.Keys))
        {
            if (used.Contains(start)) continue;

            var chain = new List<int> { start };
            used.Add(start);

            int current = start;
            while (true)
            {
                int next = -1;
                foreach (int candidate in links[current])
                    if (!used.Contains(candidate)) { next = candidate; break; }

                if (next < 0) break;
                used.Add(next);
                chain.Add(next);
                current = next;
            }

            if (chain.Count >= 4) runs.Add(chain.Select(i => points[i]).ToArray());
        }

        return runs;
    }

    // ---------------------------------------------------------------- assembly

    private static List<Vector3>? Assemble(Wall wall, List<Vector3[]> runs)
    {
        if (runs.Count == 0) return null;
        if (runs.Count == 1) return runs[0].ToList();

        // Ordered around the rim rather than by nearest loose end. Nearest-end looks reasonable and is
        // not: at a gap the two arcs facing each other are close, but so are the two ends of the same
        // arc where the rim doubles back, and taking one of those sends the walk back along the line it
        // just laid - measured at 164 degree turns before this was ordered properly.
        var crease = wall.Band.First.Points;
        var ordered = runs
            .Select(run => (Run: run, At: NearestIndex(run[run.Length / 2], crease)))
            .OrderBy(entry => entry.At)
            .ToList();

        var assembled = new List<Vector3>();
        foreach (var (run, _) in ordered)
        {
            var arc = Orient(run, crease);
            if (assembled.Count > 0) assembled.AddRange(Bridge(wall, assembled[^1], arc[0]));
            assembled.AddRange(arc);
        }

        if (assembled.Count < 16) return null;

        assembled.AddRange(Bridge(wall, assembled[^1], assembled[0]));
        return assembled;
    }

    private static Vector3[] Orient(Vector3[] run, IReadOnlyList<Vector3> crease)
    {
        if (run.Length < 2) return run;

        float head = NearestIndex(run[0], crease);
        float tail = NearestIndex(run[^1], crease);
        float forward = (tail - head + crease.Count) % crease.Count;

        return forward <= crease.Count * 0.5f ? run : run.Reverse().ToArray();
    }

    private static float NearestIndex(Vector3 point, IReadOnlyList<Vector3> crease)
    {
        int best = 0;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < crease.Count; i++)
        {
            float d = Vector3.DistanceSquared(point, crease[i]);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = i;
        }

        return best;
    }

    /// <summary>The shortest walk across the wall between two points, pinched faces included.</summary>
    private static List<Vector3> Bridge(Wall wall, Vector3 from, Vector3 to)
    {
        int start = NearestFace(wall, from);
        int goal = NearestFace(wall, to);
        if (start < 0 || goal < 0 || start == goal) return new List<Vector3>();

        var previous = new Dictionary<int, int> { [start] = -1 };
        var distance = new Dictionary<int, float> { [start] = 0f };
        var queue = new PriorityQueue<int, float>();
        queue.Enqueue(start, 0f);

        while (queue.TryDequeue(out int face, out float cost))
        {
            if (face == goal) break;
            if (cost > distance[face]) continue;

            foreach (int next in wall.Neighbours[face])
            {
                float step = cost + Vector3.Distance(wall.Centroid[face], wall.Centroid[next]);
                if (distance.TryGetValue(next, out float known) && step >= known) continue;

                distance[next] = step;
                previous[next] = face;
                queue.Enqueue(next, step);
            }
        }

        if (!previous.ContainsKey(goal)) return new List<Vector3>();

        var path = new List<Vector3>();
        for (int at = goal; at >= 0 && at != start; at = previous[at]) path.Add(wall.Centroid[at]);
        path.Reverse();
        return path;
    }

    private static int NearestFace(Wall wall, Vector3 point)
    {
        int best = -1;
        float bestDistance = float.MaxValue;

        foreach (int f in wall.FaceList)
        {
            float d = Vector3.DistanceSquared(wall.Centroid[f], point);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = f;
        }

        return best;
    }

    // ---------------------------------------------------------------- finishing

    private static Vector3[] Smooth(
        List<Vector3> loop, float spacing, int passes, ISurfaceProjector? projector)
    {
        var points = Resample(loop.ToArray(), spacing);
        if (points.Length < 8) return points;

        var scratch = new Vector3[points.Length];
        for (int pass = 0; pass < passes; pass++)
        {
            Sweep(points, scratch, 0.55f);
            Sweep(scratch, points, -0.58f);
            if (projector is not null)
                for (int i = 0; i < points.Length; i++) points[i] = projector.Project(points[i]);
        }

        // The bridges need this and the level set does not. A bridge is a walk from one face centre to
        // the next, so where it turns a corner of the mesh it turns it in one step - measured at 178
        // degrees on chin, with the line coming back within 0.65mm of itself. A few passes of Taubin
        // will not shift a spike that sharp because its neighbours are already where they should be;
        // easing the spike alone will. Level-set stretches never trip it, so on a body with nothing
        // pinched this does nothing at all.
        Unkink(points, projector);
        return points;

        static void Sweep(Vector3[] source, Vector3[] destination, float factor)
        {
            int count = source.Length;
            for (int i = 0; i < count; i++)
            {
                var midpoint = (source[(i - 1 + count) % count] + source[(i + 1) % count]) * 0.5f;
                destination[i] = source[i] + (factor * (midpoint - source[i]));
            }
        }
    }

    /// <summary>
    /// Eases the samples where the curve doubles back, and leaves the rest of it alone. Chosen once
    /// from the curve as it arrives, not re-decided each round: re-deciding sets off a cascade in which
    /// easing one spike tips its neighbour over the threshold and the correction walks away along the
    /// line rewriting stretches that were never kinked.
    /// </summary>
    private static void Unkink(
        Vector3[] points, ISurfaceProjector? projector, float limit = 45f, int passes = 24)
    {
        int count = points.Length;
        if (count < 8) return;

        var kinked = new bool[count];
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            if (Turn(points, i) < limit) continue;
            kinked[i] = true;
            found = true;
        }

        if (!found) return;

        for (int pass = 0; pass < passes; pass++)
        {
            bool moved = false;

            for (int i = 0; i < count; i++)
            {
                if (!kinked[i] || Turn(points, i) < limit) continue;

                var midpoint = (points[(i - 1 + count) % count] + points[(i + 1) % count]) * 0.5f;
                points[i] += (midpoint - points[i]) * 0.5f;
                if (projector is not null) points[i] = projector.Project(points[i]);
                moved = true;
            }

            if (!moved) break;
        }
    }

    private static float Turn(Vector3[] points, int index)
    {
        int count = points.Length;
        var incoming = points[index] - points[(index - 1 + count) % count];
        var outgoing = points[(index + 1) % count] - points[index];

        if (incoming.LengthSquared() < 1e-12f || outgoing.LengthSquared() < 1e-12f) return 0f;

        return MathF.Acos(Math.Clamp(
            Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f))
            * 180f / MathF.PI;
    }

    private static Vector3[] Resample(Vector3[] points, float spacing)
    {
        int n = points.Length;
        if (n < 4 || spacing <= 1e-4f) return points;

        var cumulative = new float[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + Vector3.Distance(points[i], points[(i + 1) % n]);

        float perimeter = cumulative[n];
        if (perimeter < 1e-4f) return points;

        int count = Math.Clamp((int)MathF.Round(perimeter / spacing), 16, 20000);
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
}
