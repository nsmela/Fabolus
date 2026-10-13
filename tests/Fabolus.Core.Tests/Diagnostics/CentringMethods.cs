using Fabolus.Core.Geometry;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Candidate ways of putting a curve down the middle of the rim wall, written to be compared against
/// each other on the same band and the same measurements.
///
/// <para>
/// Three of the four build the line from the band itself rather than correcting the traced one. That
/// is the point of trying them: every correction-based attempt so far has had to defend a threshold -
/// which points to move, how far, how to blend the join - and each of those thresholds has been a
/// place for a kink to appear. A curve defined as a level set has no joins to blend.
/// </para>
/// </summary>
internal static class CentringMethods
{
    /// <summary>
    /// The 0.5 level set of a harmonic field pinned to 0 on one crease and 1 on the other.
    ///
    /// <para>
    /// The field is smooth everywhere between, so its level set is smooth without anything being
    /// smoothed, and it is closed because the two creases are closed and the level set separates them.
    /// Where the wall narrows the field simply steepens, so a pinch needs no special case - which is
    /// the thing the correction-based method could not do and had to detect and skip instead.
    /// </para>
    /// </summary>
    public static List<Vector3[]> Harmonic(BandSurface band, int iterations = 1500)
    {
        var vertices = band.Mesh.Vertices;
        var value = new double[vertices.Length];
        var fixedAt = new bool[vertices.Length];

        foreach (int v in band.Vertices)
        {
            if (band.Side[v] < 0) { value[v] = 0.5; continue; }
            value[v] = band.Side[v];
            fixedAt[v] = true;
        }

        // Gauss-Seidel. Slow to converge in theory and entirely adequate here: the band is about ten
        // vertices across, so information only has to cross ten hops.
        for (int pass = 0; pass < iterations; pass++)
            foreach (int v in band.Vertices)
            {
                if (fixedAt[v]) continue;

                var neighbours = band.VertexNeighbours[v];
                if (neighbours.Count == 0) continue;

                double sum = 0.0;
                foreach (int n in neighbours) sum += value[n];
                value[v] = sum / neighbours.Count;
            }

        return LevelSet(band, value, 0.5);
    }

    /// <summary>
    /// The set equidistant from the two creases, by distance measured across the band's own faces
    /// rather than through space.
    ///
    /// <para>
    /// The honest version of what the traced seam already approximates: <c>ThicknessParting</c> spreads
    /// from the two surfaces and takes the tie, which is this quantity computed over the whole body
    /// instead of over the wall. Restricting it to the wall is what makes it a middle rather than a
    /// watershed.
    /// </para>
    /// </summary>
    public static List<Vector3[]> GeodesicMedial(BandSurface band)
    {
        var toFirst = Spread(band, 0);
        var toSecond = Spread(band, 1);

        // Carried to the vertices so the level set can be extracted the same way as the harmonic one,
        // which keeps the two comparable rather than one being a smooth curve and the other a staircase
        // of mesh edges.
        var vertices = band.Mesh.Vertices;
        var triangles = band.Mesh.Triangles;
        var total = new double[vertices.Length];
        var count = new int[vertices.Length];

        foreach (int f in band.FaceList)
        {
            double a = toFirst[f], b = toSecond[f];
            if (double.IsPositiveInfinity(a) || double.IsPositiveInfinity(b)) continue;
            if (a + b < 1e-6) continue;

            double ratio = a / (a + b);
            for (int e = 0; e < 3; e++)
            {
                int v = triangles[(f * 3) + e];
                total[v] += ratio;
                count[v]++;
            }
        }

        var value = new double[vertices.Length];
        foreach (int v in band.Vertices) value[v] = count[v] > 0 ? total[v] / count[v] : 0.5;

        return LevelSet(band, value, 0.5);
    }

    /// <summary>
    /// The midpoint of the wall's cross-section, taken slice by slice and measured along the surface
    /// rather than across the chord.
    ///
    /// <para>
    /// The only one of the four that measures the middle the way the wall is actually shaped. A rim
    /// wraps over its lip, so a straight line between the two creases cuts through the body and its
    /// midpoint is not on the surface at all - the arc midpoint is, and on a strongly wrapped rim the
    /// two differ. Against that, a slice needs an orientation, and the plane has to be square to a rim
    /// that twists as it goes round.
    /// </para>
    /// </summary>
    public static List<Vector3[]> CrossSections(BandSurface band, int stations = 240)
    {
        var points = band.Band.First.Points;
        if (points.Count < 8) return new List<Vector3[]>();

        var midpoints = new List<Vector3>();
        int step = Math.Max(1, points.Count / stations);

        for (int i = 0; i < points.Count; i += step)
        {
            var here = points[i];
            var ahead = points[(i + 1) % points.Count];
            var behind = points[(i - 1 + points.Count) % points.Count];

            var tangent = ahead - behind;
            if (tangent.LengthSquared < 1e-12) continue;
            tangent = tangent.Normalize();

            var slice = Slice(band, here, tangent);
            if (slice.Count == 0) continue;

            var midpoint = ArcMidpoint(slice, here);
            if (midpoint is not null) midpoints.Add(midpoint.Value);
        }

        return midpoints.Count >= 16
            ? new List<Vector3[]> { midpoints.ToArray() }
            : new List<Vector3[]>();
    }

    /// <summary>
    /// How wide the wall is at each of its faces, measured across the band's own surface: the distance
    /// to one crease plus the distance to the other. Infinity where a face cannot reach both.
    /// </summary>
    public static double[] Width(BandSurface band)
    {
        var vertices = band.Mesh.Vertices;
        var triangles = band.Mesh.Triangles;

        var width = new double[band.Faces.Length];
        Array.Fill(width, double.PositiveInfinity);

        foreach (int f in band.FaceList)
        {
            var centre = (vertices[triangles[f * 3]]
                + vertices[triangles[(f * 3) + 1]]
                + vertices[triangles[(f * 3) + 2]]) / 3.0;

            width[f] = PartingBand.Closest(centre, band.Band.First).Point.DistanceTo(PartingBand.Closest(centre, band.Band.Second).Point);
        }

        return width;
    }

    /// <summary>
    /// Faces where the wall's width departs from what the rest of the wall is doing, by the same
    /// thresholds the band-width report judges outliers by.
    ///
    /// <para>
    /// These are the places a middle cannot be found rather than the places it is hard to find. Where
    /// the rim tapers to a knife edge the two creases converge, so there is no width to be in the
    /// middle of and every method that divides by it is dividing by nothing. Marking them out is what
    /// lets the rest of the wall be solved cleanly and the gap bridged afterwards, instead of the
    /// pinch corrupting the field everywhere it can reach.
    /// </para>
    /// </summary>
    public static bool[] OutOfTolerance(
        BandSurface band, double[] width, double low = 0.65, double high = 1.25)
    {
        var measured = band.FaceList.Where(f => !double.IsPositiveInfinity(width[f]))
            .Select(f => width[f]).ToArray();

        var suspect = new bool[band.Faces.Length];
        if (measured.Length == 0) return suspect;

        Array.Sort(measured);
        double median = measured[measured.Length / 2];
        if (median < 1e-6) return suspect;

        foreach (int f in band.FaceList)
        {
            double w = width[f];
            if (double.IsPositiveInfinity(w) || w < median * low || w > median * high) suspect[f] = true;
        }

        return suspect;
    }

    /// <summary>
    /// Faces where the wall has pinched to the point of having no middle. The only exclusion the solve
    /// makes, and the reason it is one-sided: the quantity that decides where the middle is divides by
    /// the width, so a width near zero is the one case with no answer rather than a hard one.
    /// </summary>
    public static bool[] TooNarrow(BandSurface band, double[] width, double low = 0.65)
    {
        var measured = band.FaceList.Where(f => !double.IsPositiveInfinity(width[f]))
            .Select(f => width[f]).ToArray();

        var narrow = new bool[band.Faces.Length];
        if (measured.Length == 0) return narrow;

        Array.Sort(measured);
        double median = measured[measured.Length / 2];
        if (median < 1e-6) return narrow;

        foreach (int f in band.FaceList)
            if (double.IsPositiveInfinity(width[f]) || width[f] < median * low) narrow[f] = true;

        return narrow;
    }

    /// <summary>
    /// The geodesic middle, solved only where the wall has a width to have a middle in, with the
    /// resulting gaps bridged by the shortest path across the excluded stretch.
    ///
    /// <para>
    /// The bridge runs over the whole band, pinched faces included - it has to, since that is what it
    /// is crossing. What it does not do is let those faces influence where the line sits either side
    /// of the gap, which is the difference between carrying the pinch and being deformed by it.
    /// </para>
    /// </summary>
    public static List<Vector3[]> GeodesicBridged(
        BandSurface band, out bool[] outOfTolerance, out bool[] skipped)
    {
        var width = Width(band);

        // Two different questions, and conflating them was the mistake. Out of tolerance is a fact
        // about the body worth showing: the wall is not the even thickness it is supposed to be, wide
        // or narrow. What has to be skipped is only the narrow, because only there is the middle
        // undefined - a wide stretch has a perfectly good middle, and dropping it to bridge straight
        // across replaces a medial line with a chord. Measured: skipping the wide as well took
        // standard from 3.4% off centre to 11.0% and ear from 2.5% to 17.6%.
        outOfTolerance = OutOfTolerance(band, width);
        skipped = TooNarrow(band, width);

        var kept = new List<int>(band.FaceList.Length);
        foreach (int f in band.FaceList) if (!skipped[f]) kept.Add(f);

        if (kept.Count < 16) return new List<Vector3[]>();

        var trimmed = new bool[band.Faces.Length];
        foreach (int f in kept) trimmed[f] = true;

        var toFirst = Spread(band, 0);
        var toSecond = Spread(band, 1);

        var triangles = band.Mesh.Triangles;
        var vertices = band.Mesh.Vertices;
        var total = new double[vertices.Length];
        var count = new int[vertices.Length];

        foreach (int f in kept)
        {
            double a = toFirst[f], b = toSecond[f];
            if (double.IsPositiveInfinity(a) || double.IsPositiveInfinity(b) || a + b < 1e-6) continue;

            double ratio = a / (a + b);
            for (int e = 0; e < 3; e++)
            {
                int v = triangles[(f * 3) + e];
                total[v] += ratio;
                count[v]++;
            }
        }

        var value = new double[vertices.Length];
        for (int v = 0; v < vertices.Length; v++) value[v] = count[v] > 0 ? total[v] / count[v] : 0.5;

        // Marching over the kept faces only, so the set stops at the edge of each excluded stretch.
        var segments = new List<(Vector3, Vector3)>(kept.Count);
        foreach (int f in kept)
        {
            var crossings = new List<Vector3>(2);
            bool measurable = true;

            for (int e = 0; e < 3; e++)
            {
                int i = triangles[(f * 3) + e];
                int j = triangles[(f * 3) + ((e + 1) % 3)];
                if (count[i] == 0 || count[j] == 0) { measurable = false; break; }

                double a = value[i], b = value[j];
                if ((a < 0.5 && b < 0.5) || (a >= 0.5 && b >= 0.5)) continue;
                if (Math.Abs(b - a) < 1e-9) continue;

                crossings.Add(vertices[i].LerpTo(vertices[j], (0.5 - a) / (b - a)));
            }

            if (measurable && crossings.Count == 2) segments.Add((crossings[0], crossings[1]));
        }

        var arcs = SegmentChains.ChainAll(segments, Weld(band));
        if (arcs.Count == 0) return new List<Vector3[]>();

        var closed = arcs.Where(a => a.Closed).Select(a => a.Points).ToList();
        var open = arcs.Where(a => !a.Closed).Select(a => a.Points).OrderByDescending(p => p.Length).ToList();

        if (open.Count == 0) return closed;

        // Ordered around the rim rather than by whichever loose end is nearest. Nearest-end looks
        // reasonable and is not: at a gap the two arcs facing each other across it are close, but so
        // are the two ends of the *same* arc where the rim doubles back, and picking one of those sends
        // the walk backwards along the line it just laid. That is what produced the 164 degree turns.
        // The crease is a closed curve with a natural order, so projecting each arc onto it gives the
        // order the rim itself has.
        var crease = band.Band.First.Points;
        var ordered = open
            .Select(arc => (Arc: arc, At: RimParameter(arc, crease)))
            .OrderBy(entry => entry.At)
            .ToList();

        var assembled = new List<Vector3>();
        for (int i = 0; i < ordered.Count; i++)
        {
            var arc = Orient(ordered[i].Arc, crease);

            if (assembled.Count > 0) assembled.AddRange(Bridge(band, assembled[^1], arc[0]));
            assembled.AddRange(arc);
        }

        if (assembled.Count < 16) return closed;

        assembled.AddRange(Bridge(band, assembled[^1], assembled[0]));

        closed.Add(assembled.ToArray());
        return closed;
    }

    /// <summary>
    /// Where an arc sits around the rim, as the index of the crease point its midpoint is nearest.
    /// Taken from the arc's midpoint rather than an end, because an end sits in a gap where the crease
    /// is least reliable.
    /// </summary>
    private static double RimParameter(Vector3[] arc, IReadOnlyList<Vector3> crease)
        => NearestIndex(arc[arc.Length / 2], crease);

    /// <summary>Turns an arc so it runs the same way round the rim as the crease does.</summary>
    private static Vector3[] Orient(Vector3[] arc, IReadOnlyList<Vector3> crease)
    {
        if (arc.Length < 2) return arc;

        double head = NearestIndex(arc[0], crease);
        double tail = NearestIndex(arc[^1], crease);

        // The crease is a ring, so "increasing" has to be read the short way round.
        double forward = (tail - head + crease.Count) % crease.Count;
        return forward <= crease.Count * 0.5 ? arc : arc.Reverse().ToArray();
    }

    private static double NearestIndex(Vector3 point, IReadOnlyList<Vector3> crease)
    {
        int best = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < crease.Count; i++)
        {
            double d = point.DistanceSquared(crease[i]);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = i;
        }

        return best;
    }

    /// <summary>The shortest walk across the band between two points, as the centroids it passes through.</summary>
    private static List<Vector3> Bridge(BandSurface band, Vector3 from, Vector3 to)
    {
        int start = NearestFace(band, from);
        int goal = NearestFace(band, to);
        if (start < 0 || goal < 0 || start == goal) return new List<Vector3>();

        var previous = new Dictionary<int, int> { [start] = -1 };
        var distance = new Dictionary<int, double> { [start] = 0.0 };
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(start, 0.0);

        while (queue.TryDequeue(out int face, out double cost))
        {
            if (face == goal) break;
            if (cost > distance[face]) continue;

            foreach (int next in band.Neighbours[face])
            {
                double step = cost + band.Centroid[face].DistanceTo(band.Centroid[next]);
                if (distance.TryGetValue(next, out double known) && step >= known) continue;

                distance[next] = step;
                previous[next] = face;
                queue.Enqueue(next, step);
            }
        }

        if (!previous.ContainsKey(goal)) return new List<Vector3>();

        var path = new List<Vector3>();
        for (int at = goal; at >= 0 && at != start; at = previous[at]) path.Add(band.Centroid[at]);
        path.Reverse();
        return path;
    }

    private static int NearestFace(BandSurface band, Vector3 point)
    {
        int best = -1;
        double bestDistance = double.MaxValue;

        foreach (int f in band.FaceList)
        {
            double d = band.Centroid[f].DistanceSquared(point);
            if (d >= bestDistance) continue;
            bestDistance = d;
            best = f;
        }

        return best;
    }

    /// <summary>
    /// The middle of the wall taken from which way the surface faces, rather than from how far it is
    /// from either crease.
    ///
    /// <para>
    /// The body is three regions: the two surfaces and the rim between them. Each surface has a mean
    /// direction it faces, and across the rim the normal swings continuously from one to the other -
    /// so the place where it is equally aligned with both is a middle of the wall, arrived at without
    /// asking where either crease is.
    /// </para>
    ///
    /// <para>
    /// Worth trying precisely because of what the crease strengths showed. On <c>standard</c> both
    /// creases are the faintest in the sample set, 23 and 31 degrees against 45 to 57 elsewhere, so
    /// every method that measures from them inherits that softness. The two surfaces either side are
    /// not soft at all - a bowl knows which way it faces - and this reads the middle off them instead.
    /// </para>
    /// </summary>
    public static List<Vector3[]> NormalSplit(BandSurface band)
    {
        var mesh = band.Mesh;
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;
        int faceCount = triangles.Length / 3;

        var normal = new Vector3[faceCount];
        var area = new double[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            var a = vertices[triangles[f * 3]];
            var b = vertices[triangles[(f * 3) + 1]];
            var c = vertices[triangles[(f * 3) + 2]];

            var cross = (b - a).Cross(c - a);
            double length = cross.Length;
            area[f] = length * 0.5;
            normal[f] = length < 1e-12 ? Vector3.Zero : cross / length;
        }

        // The two surfaces are what the band separates. Taken as the mean direction each faces,
        // weighted by area so a finely tessellated patch does not outvote a broad one.
        var onBand = band.Faces;
        var first = Vector3.Zero;
        var second = Vector3.Zero;

        var side = SurfaceSides(mesh, onBand, band);
        for (int f = 0; f < faceCount; f++)
        {
            if (side[f] == 0) first += normal[f] * area[f];
            else if (side[f] == 1) second += normal[f] * area[f];
        }

        // Local references, not the global means. A global mean is what a first attempt at this used
        // and it does not work: on a bowl the outer surface's mean normal is dominated by the bottom,
        // nowhere near what the surface does at the lip, so the swing across the rim is measured
        // against a direction the rim never faces. Measured that way <c>standard</c> came out 62%
        // off centre and seven of eleven bodies produced no closed loop at all. What each band face
        // needs is the direction the surface faces *beside it*, which is the normal of the nearest
        // surface face on each side.
        var nearFirst = NearestSurfaceNormal(mesh, band, side, normal, 0);
        var nearSecond = NearestSurfaceNormal(mesh, band, side, normal, 1);

        var total = new double[vertices.Length];
        var count = new int[vertices.Length];

        foreach (int f in band.FaceList)
        {
            var a0 = nearFirst[f];
            var b0 = nearSecond[f];
            if (a0 == Vector3.Zero || b0 == Vector3.Zero) continue;

            double a = Math.Acos(Math.Clamp(normal[f].Dot(a0), -1.0, 1.0));
            double b = Math.Acos(Math.Clamp(normal[f].Dot(b0), -1.0, 1.0));
            if (a + b < 1e-6) continue;

            double ratio = a / (a + b);
            for (int e = 0; e < 3; e++)
            {
                int v = triangles[(f * 3) + e];
                total[v] += ratio;
                count[v]++;
            }
        }

        var value = new double[vertices.Length];
        foreach (int v in band.Vertices) value[v] = count[v] > 0 ? total[v] / count[v] : 0.5;

        return LevelSet(band, value, 0.5);
    }

    /// <summary>
    /// For each band face, the normal of the nearest surface face on the given side - the direction
    /// the surface is facing just beside it, rather than on average across the whole body.
    /// </summary>
    private static Vector3[] NearestSurfaceNormal(
        IMesh mesh, BandSurface band, int[] side, Vector3[] normal, int which)
    {
        int faceCount = normal.Length;
        var triangles = mesh.Triangles;
        var vertices = mesh.Vertices;

        var centroid = new Vector3[faceCount];
        for (int f = 0; f < faceCount; f++)
            centroid[f] = (vertices[triangles[f * 3]]
                + vertices[triangles[(f * 3) + 1]]
                + vertices[triangles[(f * 3) + 2]]) / 3.0;

        var edges = new Dictionary<(int, int), List<int>>(faceCount * 2);
        for (int f = 0; f < faceCount; f++)
            for (int e = 0; e < 3; e++)
            {
                int a = triangles[(f * 3) + e];
                int b = triangles[(f * 3) + ((e + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var shared)) edges[key] = shared = new List<int>(2);
                shared.Add(f);
            }

        var neighbours = new List<int>[faceCount];
        for (int f = 0; f < faceCount; f++) neighbours[f] = new List<int>(3);
        foreach (var shared in edges.Values)
            for (int i = 0; i < shared.Count; i++)
                for (int j = 0; j < shared.Count; j++)
                    if (i != j) neighbours[shared[i]].Add(shared[j]);

        var carried = new Vector3[faceCount];
        var distance = new double[faceCount];
        Array.Fill(distance, double.PositiveInfinity);

        var queue = new PriorityQueue<int, double>();
        for (int f = 0; f < faceCount; f++)
        {
            if (side[f] != which) continue;
            distance[f] = 0.0;
            carried[f] = normal[f];
            queue.Enqueue(f, 0.0);
        }

        while (queue.TryDequeue(out int face, out double cost))
        {
            if (cost > distance[face]) continue;
            foreach (int next in neighbours[face])
            {
                double step = cost + centroid[face].DistanceTo(centroid[next]);
                if (step >= distance[next]) continue;

                distance[next] = step;
                carried[next] = carried[face];
                queue.Enqueue(next, step);
            }
        }

        return carried;
    }

    /// <summary>
    /// Which of the two surfaces each non-band face belongs to: the two largest pieces the band leaves
    /// behind. Found by flooding rather than by proximity to a crease, which is the whole point.
    /// </summary>
    private static int[] SurfaceSides(IMesh mesh, bool[] onBand, BandSurface band)
    {
        var triangles = mesh.Triangles;
        int faceCount = triangles.Length / 3;

        var edges = new Dictionary<(int, int), List<int>>(faceCount * 2);
        for (int f = 0; f < faceCount; f++)
            for (int e = 0; e < 3; e++)
            {
                int a = triangles[(f * 3) + e];
                int b = triangles[(f * 3) + ((e + 1) % 3)];
                var key = a < b ? (a, b) : (b, a);
                if (!edges.TryGetValue(key, out var shared)) edges[key] = shared = new List<int>(2);
                shared.Add(f);
            }

        var neighbours = new List<int>[faceCount];
        for (int f = 0; f < faceCount; f++) neighbours[f] = new List<int>(3);
        foreach (var shared in edges.Values)
            for (int i = 0; i < shared.Count; i++)
                for (int j = 0; j < shared.Count; j++)
                    if (i != j) neighbours[shared[i]].Add(shared[j]);

        var owner = new int[faceCount];
        Array.Fill(owner, -1);
        var sizes = new List<int>();

        for (int seed = 0; seed < faceCount; seed++)
        {
            if (onBand[seed] || owner[seed] >= 0) continue;

            int id = sizes.Count;
            int members = 0;
            var stack = new Stack<int>();
            owner[seed] = id;
            stack.Push(seed);

            while (stack.Count > 0)
            {
                int f = stack.Pop();
                members++;
                foreach (int n in neighbours[f])
                {
                    if (onBand[n] || owner[n] >= 0) continue;
                    owner[n] = id;
                    stack.Push(n);
                }
            }

            sizes.Add(members);
        }

        var largest = sizes.Select((count, id) => (count, id))
            .OrderByDescending(entry => entry.count).Take(2).Select(entry => entry.id).ToList();

        var side = new int[faceCount];
        Array.Fill(side, -1);
        for (int f = 0; f < faceCount; f++)
        {
            if (owner[f] < 0) continue;
            int at = largest.IndexOf(owner[f]);
            if (at >= 0) side[f] = at;
        }

        return side;
    }

    /// <summary>
    /// Straightens a line by smoothing it as hard as it will take, and keeps it honest by pushing it
    /// back whenever it leaves the wall.
    ///
    /// <para>
    /// A different objective from everything above, and a better-aimed one. A parting line is not for
    /// bisecting a wall, it is for sweeping a flange along - and a flange cares about curvature, not
    /// about whether the curve sits at 0.50 or 0.62 across. So this stops asking for the middle and
    /// asks only that the line stay inside the wall, which leaves the smoothing free to take out
    /// everything the middle was forcing it to follow.
    /// </para>
    ///
    /// <para>
    /// Plain Laplacian rather than Taubin, deliberately. Taubin alternates a shrinking pass with an
    /// inflating one precisely so a loop keeps its size, which is the right thing when the loop's
    /// position is the answer and the wrong thing here: the shrinkage <em>is</em> the straightening.
    /// What stops it collapsing is the wall, not a counter-pass.
    /// </para>
    /// </summary>
    public static Vector3[] Straighten(
        IReadOnlyList<Vector3> loop, PartingBand band, ISurfaceProjector? projector,
        double margin = 0.15, int passes = 60, double strength = 0.5)
    {
        int count = loop.Count;
        var points = loop.ToArray();
        if (count < 8) return points;

        var scratch = new Vector3[count];

        for (int pass = 0; pass < passes; pass++)
        {
            for (int i = 0; i < count; i++)
            {
                var midpoint = (points[(i - 1 + count) % count] + points[(i + 1) % count]) * 0.5;
                scratch[i] = points[i] + ((midpoint - points[i]) * strength);
            }

            // The wall pushes back, rather than the line being clipped to it. A clamp is a step
            // function - it does nothing at all until the point crosses the margin and then moves it
            // bodily - so it puts a corner in exactly where it engages. Measured with a clamp, nose
            // went from a worst turn of 36 degrees to 63 and chin_bolus from 65 to 73, while the
            // stretches nowhere near a crease improved. A force that grows from zero as the margin is
            // approached leaves the smoothing to resolve the two against each other.
            for (int i = 0; i < count; i++)
            {
                var a = PartingBand.Closest(scratch[i], band.First).Point;
                var b = PartingBand.Closest(scratch[i], band.Second).Point;

                var across = b - a;
                double span = across.LengthSquared;
                if (span < 1e-9) continue;

                double t = (scratch[i] - a).Dot(across) / span;
                double over = t < margin ? margin - t : t > 1.0 - margin ? t - (1.0 - margin) : 0.0;
                if (over <= 0.0) continue;

                double target = t < margin ? margin : 1.0 - margin;
                scratch[i] += across * ((target - t) * Math.Min(over / margin, 1.0));
            }

            Array.Copy(scratch, points, count);

            if (projector is not null)
                for (int i = 0; i < count; i++) points[i] = projector.Project(points[i]);
        }

        return points;
    }

    // ---------------------------------------------------------------- level sets

    /// <summary>
    /// Marching triangles: every band face whose vertex values straddle the level contributes one
    /// segment, and the segments meet exactly at shared edges, so the result chains into closed rings.
    /// </summary>
    private static List<Vector3[]> LevelSet(BandSurface band, double[] value, double level)
    {
        var vertices = band.Mesh.Vertices;
        var triangles = band.Mesh.Triangles;
        var segments = new List<(Vector3, Vector3)>(band.FaceList.Length);

        foreach (int f in band.FaceList)
        {
            var crossings = new List<Vector3>(2);

            for (int e = 0; e < 3; e++)
            {
                int i = triangles[(f * 3) + e];
                int j = triangles[(f * 3) + ((e + 1) % 3)];

                double a = value[i], b = value[j];
                if ((a < level && b < level) || (a >= level && b >= level)) continue;
                if (Math.Abs(b - a) < 1e-9) continue;

                double t = (level - a) / (b - a);
                crossings.Add(vertices[i].LerpTo(vertices[j], t));
            }

            if (crossings.Count == 2) segments.Add((crossings[0], crossings[1]));
        }

        double weld = Weld(band);
        return SegmentChains.Chain(segments, weld);
    }

    private static double Weld(BandSurface band)
    {
        var vertices = band.Mesh.Vertices;
        var triangles = band.Mesh.Triangles;

        double total = 0d;
        int count = 0;
        foreach (int f in band.FaceList)
            for (int e = 0; e < 3; e++)
            {
                total += vertices[triangles[(f * 3) + e]].DistanceTo(vertices[triangles[(f * 3) + ((e + 1) % 3)]]);
                count++;
            }

        return count == 0 ? 1e-3 : (double)(total / count) * 0.05;
    }

    /// <summary>Cost across the band's faces from every face touching the given crease.</summary>
    private static double[] Spread(BandSurface band, int side)
    {
        var triangles = band.Mesh.Triangles;
        var distance = new double[band.Faces.Length];
        Array.Fill(distance, double.PositiveInfinity);

        var queue = new PriorityQueue<int, double>();
        foreach (int f in band.FaceList)
        {
            bool seed = false;
            for (int e = 0; e < 3; e++)
                if (band.Side[triangles[(f * 3) + e]] == side) { seed = true; break; }

            if (!seed) continue;
            distance[f] = 0.0;
            queue.Enqueue(f, 0.0);
        }

        while (queue.TryDequeue(out int face, out double cost))
        {
            if (cost > distance[face]) continue;
            foreach (int next in band.Neighbours[face])
            {
                double step = cost + band.Centroid[face].DistanceTo(band.Centroid[next]);
                if (step >= distance[next]) continue;
                distance[next] = step;
                queue.Enqueue(next, step);
            }
        }

        return distance;
    }

    // ---------------------------------------------------------------- slices

    /// <summary>The band's intersection with a plane, as loose segments.</summary>
    private static List<(Vector3 A, Vector3 B)> Slice(BandSurface band, Vector3 origin, Vector3 normal)
    {
        var vertices = band.Mesh.Vertices;
        var triangles = band.Mesh.Triangles;
        var segments = new List<(Vector3, Vector3)>();

        foreach (int f in band.FaceList)
        {
            var crossings = new List<Vector3>(2);

            for (int e = 0; e < 3; e++)
            {
                var p = vertices[triangles[(f * 3) + e]];
                var q = vertices[triangles[(f * 3) + ((e + 1) % 3)]];

                double a = (p - origin).Dot(normal);
                double b = (q - origin).Dot(normal);
                if ((a < 0.0 && b < 0.0) || (a >= 0.0 && b >= 0.0)) continue;
                if (Math.Abs(b - a) < 1e-9) continue;

                crossings.Add(p.LerpTo(q, a / (a - b)));
            }

            if (crossings.Count == 2) segments.Add((crossings[0], crossings[1]));
        }

        return segments;
    }

    /// <summary>
    /// The arc midpoint of the slice's run through the wall, starting from the crease point the slice
    /// was taken at. Walks the segments end to end so the midpoint is by length along the surface.
    /// </summary>
    private static Vector3? ArcMidpoint(List<(Vector3 A, Vector3 B)> segments, Vector3 from)
    {
        // Only the run containing the crease point matters - a plane through a rim can also clip the
        // far side of the body, and that piece is a different part of the wall entirely.
        var remaining = new List<(Vector3 A, Vector3 B)>(segments);
        var path = new List<Vector3>();

        var current = from;
        double bestStart = double.MaxValue;
        int startIndex = -1;

        for (int i = 0; i < remaining.Count; i++)
        {
            double d = Math.Min(
                from.DistanceTo(remaining[i].A), from.DistanceTo(remaining[i].B));
            if (d >= bestStart) continue;
            bestStart = d;
            startIndex = i;
        }

        if (startIndex < 0) return null;

        var seed = remaining[startIndex];
        current = from.DistanceTo(seed.A) <= from.DistanceTo(seed.B) ? seed.A : seed.B;
        path.Add(current);
        remaining.RemoveAt(startIndex);

        var head = current;
        var tail = current.DistanceTo(seed.A) < 1e-6 ? seed.B : seed.A;
        path.Add(tail);
        head = tail;

        while (true)
        {
            int next = -1;
            bool flip = false;
            double best = double.MaxValue;

            for (int i = 0; i < remaining.Count; i++)
            {
                double da = head.DistanceTo(remaining[i].A);
                double db = head.DistanceTo(remaining[i].B);

                if (da < best) { best = da; next = i; flip = false; }
                if (db < best) { best = db; next = i; flip = true; }
            }

            if (next < 0 || best > 1e-3) break;

            var segment = remaining[next];
            head = flip ? segment.A : segment.B;
            path.Add(head);
            remaining.RemoveAt(next);
        }

        if (path.Count < 2) return null;

        double length = 0.0;
        for (int i = 1; i < path.Count; i++) length += path[i - 1].DistanceTo(path[i]);
        if (length < 1e-6) return null;

        double target = length * 0.5;
        double walked = 0.0;
        for (int i = 1; i < path.Count; i++)
        {
            double span = path[i - 1].DistanceTo(path[i]);
            if (walked + span >= target)
            {
                double t = span < 1e-9 ? 0.0 : (target - walked) / span;
                return path[i - 1].LerpTo(path[i], t);
            }
            walked += span;
        }

        return path[^1];
    }

    // ---------------------------------------------------------------- shared finishing

    /// <summary>
    /// Resample to an even spacing, relax gently, and hold on the surface - applied identically to
    /// every candidate so the comparison is between where they put the line, not how tidy each one
    /// happened to leave it.
    /// </summary>
    public static PartingLine Finish(
        List<Vector3[]> loops, double spacing, ISurfaceProjector? projector, int passes = 4)
    {
        var finished = new List<Vector3[]>(loops.Count);

        foreach (var loop in loops)
        {
            var points = Resample(loop, spacing);
            if (points.Length < 8) continue;

            var scratch = new Vector3[points.Length];
            for (int pass = 0; pass < passes; pass++)
            {
                Sweep(points, scratch, 0.55);
                Sweep(scratch, points, -0.58);
                if (projector is not null)
                    for (int i = 0; i < points.Length; i++) points[i] = projector.Project(points[i]);
            }

            finished.Add(points);
        }

        return new PartingLine(finished);

        static void Sweep(Vector3[] source, Vector3[] destination, double factor)
        {
            int count = source.Length;
            for (int i = 0; i < count; i++)
            {
                var midpoint = (source[(i - 1 + count) % count] + source[(i + 1) % count]) * 0.5;
                destination[i] = source[i] + (factor * (midpoint - source[i]));
            }
        }
    }

    private static Vector3[] Resample(Vector3[] points, double spacing)
    {
        int n = points.Length;
        if (n < 4 || spacing <= 1e-4) return points;

        var cumulative = new double[n + 1];
        for (int i = 0; i < n; i++)
            cumulative[i + 1] = cumulative[i] + points[i].DistanceTo(points[(i + 1) % n]);

        double perimeter = cumulative[n];
        if (perimeter < 1e-4) return points;

        int count = Math.Clamp((int)Math.Round(perimeter / spacing), 16, 20000);
        var result = new Vector3[count];

        int segment = 0;
        for (int k = 0; k < count; k++)
        {
            double target = perimeter * k / count;
            while (segment < n - 1 && cumulative[segment + 1] < target) segment++;

            double span = cumulative[segment + 1] - cumulative[segment];
            double t = span > 1e-6 ? Math.Clamp((target - cumulative[segment]) / span, 0.0, 1.0) : 0.0;
            result[k] = points[segment].LerpTo(points[(segment + 1) % n], t);
        }

        return result;
    }
}
