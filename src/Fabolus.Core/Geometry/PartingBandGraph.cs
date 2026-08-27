using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The rim wall as something a line can be walked along: its faces, how they join, and where each one
/// sits round the rim.
///
/// <para>
/// This is what makes a parting line editable rather than only computable. Every operation a user can
/// perform on the line - drag a handle, delete one, add one - comes down to re-tracing the stretch
/// between two points, and that trace has to stay on the wall. A shortest path across the body would
/// take the obvious short cut over one of the shells; a straight line in space would go through the
/// solid. Confining the walk to the band faces is what stops both, and it is the only way to guarantee
/// that a hand-edited line is still a line the mould can part along.
/// </para>
///
/// <para>
/// Position round the rim is carried alongside, as a fractional index into the wall's own crease. A
/// walk between two points on a closed rim has two ways to go and the shortest is not always the one
/// meant - two handles three quarters of the way apart are joined the short way by any unconstrained
/// search, which reverses that stretch of the line. Knowing where each face sits round the rim is what
/// lets the walk be told which way to go.
/// </para>
/// </summary>
public sealed class PartingBandGraph
{
    private readonly int[] _faces;
    private readonly Vector3[] _centroid;
    private readonly float[] _rimIndex;
    private readonly List<int>[] _neighbours;

    /// <summary>
    /// The three corners of each band face, so a point can be pinned to the wall's surface rather than
    /// to the nearest face's middle. Kept for the band alone rather than for the whole mesh: a band is
    /// a few thousand faces where the body is hundreds of thousands.
    /// </summary>
    private readonly Dictionary<int, (Vector3 A, Vector3 B, Vector3 C)> _corners;

    /// <summary>The edge shared by each adjacent pair of band faces - the gates a walk passes through.</summary>
    private readonly Dictionary<(int, int), (Vector3 A, Vector3 B)> _portals;

    private readonly Dictionary<(int, int, int), List<int>> _cells = new();
    private readonly float _cell;

    /// <summary>How many samples the crease this graph's rim positions are indexed against has.</summary>
    public int RimSamples { get; }

    /// <summary>The wall's two creases.</summary>
    public PartingBand Band { get; }

    public float MeanEdge { get; }

    private PartingBandGraph(
        int[] faces, Vector3[] centroid, float[] rimIndex, List<int>[] neighbours,
        Dictionary<int, (Vector3, Vector3, Vector3)> corners,
        Dictionary<(int, int), (Vector3, Vector3)> portals,
        PartingBand band, float meanEdge, int rimSamples)
    {
        _portals = portals;
        _faces = faces;
        _centroid = centroid;
        _rimIndex = rimIndex;
        _neighbours = neighbours;
        _corners = corners;
        Band = band;
        MeanEdge = meanEdge;
        RimSamples = rimSamples;
        _cell = MathF.Max(meanEdge * 2f, 1e-4f);

        foreach (int f in faces)
        {
            var key = Cell(centroid[f]);
            if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
            list.Add(f);
        }
    }

    /// <param name="band">The band mask - <see cref="RidgeSurfaces.Band"/>, not <c>Faces</c>.</param>
    /// <param name="faceRims">
    /// Which rim each face belongs to. A face with no rim is kept: the mask is closed after the rims
    /// are assigned, so the faces closing it added carry -1 and dropping them punches the holes back in.
    /// </param>
    public static PartingBandGraph? Build(
        IMesh mesh, bool[] band, int[] faceRims, int rim, PartingBand pair)
    {
        if (mesh is null || pair?.First is null || pair.Second is null) return null;

        var triangles = mesh.Triangles;
        var vertices = mesh.Vertices;
        int faceCount = triangles.Length / 3;
        if (band is null || band.Length != faceCount) return null;

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

        // The edge two adjacent band faces have in common, keyed by the pair. This is what a walk
        // actually crosses, and holding it is what lets a walk be pulled taut - see WalkGeodesic. Only
        // edges with exactly two band faces qualify: an edge with one is the band's own border, which
        // no walk crosses, and an edge with more is non-manifold and has no single crossing to place.
        var portals = new Dictionary<(int, int), (Vector3, Vector3)>(edges.Count);
        foreach (var (key, shared) in edges)
        {
            if (shared.Count != 2 || shared[0] == shared[1]) continue;
            portals[Pair(shared[0], shared[1])] = (vertices[key.Item1], vertices[key.Item2]);
        }

        var centroid = new Vector3[faceCount];
        var rimIndex = new float[faceCount];
        var corners = new Dictionary<int, (Vector3, Vector3, Vector3)>(list.Count);
        var crease = pair.First.Points;

        foreach (int f in list)
        {
            var a = vertices[triangles[f * 3]];
            var b = vertices[triangles[(f * 3) + 1]];
            var c = vertices[triangles[(f * 3) + 2]];

            corners[f] = (a, b, c);
            centroid[f] = (a + b + c) / 3f;

            rimIndex[f] = NearestIndex(centroid[f], crease);
        }

        double edgeTotal = 0d;
        foreach (var key in edges.Keys)
            edgeTotal += Vector3.Distance(vertices[key.Item1], vertices[key.Item2]);

        float meanEdge = edges.Count == 0 ? 1f : (float)(edgeTotal / edges.Count);

        return new PartingBandGraph(
            list.ToArray(), centroid, rimIndex, neighbours, corners, portals, pair, meanEdge,
            crease.Count);
    }

    private static (int, int) Pair(int a, int b) => a < b ? (a, b) : (b, a);

    private (int, int, int) Cell(Vector3 p) => (
        (int)MathF.Floor(p.X / _cell), (int)MathF.Floor(p.Y / _cell), (int)MathF.Floor(p.Z / _cell));

    /// <summary>The band face nearest a point, or -1 if the band is nowhere near it.</summary>
    public int NearestFace(Vector3 point)
    {
        var (cx, cy, cz) = Cell(point);

        for (int radius = 1; radius <= 6; radius++)
        {
            int best = -1;
            float bestDistance = float.MaxValue;

            for (int x = cx - radius; x <= cx + radius; x++)
                for (int y = cy - radius; y <= cy + radius; y++)
                    for (int z = cz - radius; z <= cz + radius; z++)
                    {
                        if (!_cells.TryGetValue((x, y, z), out var faces)) continue;

                        foreach (int f in faces)
                        {
                            float d = Vector3.DistanceSquared(_centroid[f], point);
                            if (d >= bestDistance) continue;

                            bestDistance = d;
                            best = f;
                        }
                    }

            if (best >= 0) return best;
        }

        // As in Snap: the grid only reaches so far, and a point beyond it is not a point with no
        // nearest face - it is one the grid cannot see. Answering -1 there strands the callers that
        // cannot do anything useful with it, WalkFaces above all, which returns null and leaves a
        // dragged handle with its spans un-rewalked.
        int nearest = -1;
        float nearestDistance = float.MaxValue;

        foreach (int f in _faces)
        {
            float d = Vector3.DistanceSquared(_centroid[f], point);
            if (d >= nearestDistance) continue;

            nearestDistance = d;
            nearest = f;
        }

        return nearest;
    }

    /// <summary>
    /// The nearest point on the wall to an arbitrary point, which is what a handle dragged by hand has
    /// to be pinned to. A handle the user has pulled off the wall is not an instruction to leave the
    /// wall - there is nowhere else a parting line may go - so it is taken as the nearest place on the
    /// wall they could have meant.
    ///
    /// <para>
    /// The point is on the wall's surface, not at the middle of the nearest face. Returning the centroid
    /// - which this did - quantises a drag to one face: the handle sits still while the cursor crosses a
    /// triangle and then jumps the width of one to the next, which reads as the handle resisting the
    /// mouse rather than following it, and on a coarse band the jump is millimetres.
    /// </para>
    /// </summary>
    public Vector3 Snap(Vector3 point)
    {
        var (cx, cy, cz) = Cell(point);

        var best = point;
        float bestDistance = float.MaxValue;
        int firstHit = -1;

        // Measured face by face rather than by taking the face with the nearest centroid and working
        // from there. A centroid is not a stand-in for a surface on a band of long thin triangles - the
        // point can sit on one face while another's middle is nearer - and the difference matters here
        // in a way it does not for <see cref="NearestFace"/>: this result is a position rather than a
        // choice of face, and it has to come back unchanged when it is fed in again. A snapped handle is
        // re-snapped by every subsequent edit, so a snap that drifted would walk the line off the wall
        // one edit at a time.
        for (int radius = 1; radius <= 7; radius++)
        {
            for (int x = cx - radius; x <= cx + radius; x++)
                for (int y = cy - radius; y <= cy + radius; y++)
                    for (int z = cz - radius; z <= cz + radius; z++)
                    {
                        if (!_cells.TryGetValue((x, y, z), out var faces)) continue;

                        if (firstHit < 0) firstHit = radius;

                        foreach (int f in faces)
                        {
                            var candidate = ClosestOnFace(f, point);
                            float d = Vector3.DistanceSquared(candidate, point);
                            if (d >= bestDistance) continue;

                            bestDistance = d;
                            best = candidate;
                        }
                    }

            // Settled as soon as nothing unscanned could beat what is in hand. A face is filed under its
            // centroid, so one whose centroid is this far out still reaches about an edge length nearer
            // than that - hence the allowance. A point already on the wall settles at the first ring,
            // which is the case that matters: every edit re-snaps the anchors it carries across.
            float reach = MathF.Max((radius * _cell) - MeanEdge, 0f);
            if (bestDistance <= reach * reach) break;

            // Nothing near enough to settle it. Carry on, but never past one ring beyond the first cell
            // that held anything - a face's surface reaches into cells it was never filed in, and past
            // that there is nothing left to find.
            if (firstHit >= 0 && radius > firstHit) break;
        }

        // The grid reaches about seven cells, which is a little over a dozen edge lengths, and the wall
        // is a narrow strip on a body far bigger than that. A handle thrown clear across the body lands
        // outside it, and returning the point as it came - which this did - puts an anchor somewhere
        // that is not on the wall at all, silently: every guarantee the editor makes rests on Snap
        // answering with a point on the band, and nothing downstream re-checks it. So the miss falls
        // back to measuring every band face. It is a few thousand triangles on the paths that reach it,
        // and it only ever runs when the grid found nothing, which no drag on or near the rim does.
        return firstHit >= 0 ? best : Furthest(point);
    }

    /// <summary>
    /// The nearest point on the wall by measuring every face - the answer <see cref="Snap"/> falls back
    /// to when its grid search comes up empty. Linear on purpose: it is the correct answer at any
    /// distance, which is the whole reason it is here.
    /// </summary>
    private Vector3 Furthest(Vector3 point)
    {
        var best = point;
        float bestDistance = float.MaxValue;

        foreach (int f in _faces)
        {
            var candidate = ClosestOnFace(f, point);
            float d = Vector3.DistanceSquared(candidate, point);
            if (d >= bestDistance) continue;

            bestDistance = d;
            best = candidate;
        }

        return best;
    }

    /// <summary>
    /// <see cref="Snap(Vector3)"/> for a caller moving a point in small steps, seeded with the face the
    /// point came back on last time.
    ///
    /// <para>
    /// The plain overload searches the grid, which is a hundred and fifty cells of triangle tests, and
    /// that is the right price for a query out of nowhere - a cursor position, a handle thrown across
    /// the body. It is the wrong price paid tens of thousands of times by a relaxation that moves each
    /// point a fraction of an edge per pass and already knows where it was: smoothing the largest body
    /// in the set spent 1.6 seconds almost entirely here.
    /// </para>
    ///
    /// <para>
    /// Two rings are searched and the answer is accepted only if it lands in the first, so it is
    /// surrounded on all sides by faces it beat. That is what makes the shortcut safe rather than merely
    /// fast; anything else falls through to the full search, and a caller that jumps its point somewhere
    /// new pays the ordinary price for it and gets a usable hint back.
    /// </para>
    /// </summary>
    /// <param name="hint">
    /// In: where to start, or -1 for no idea. Out: the face the answer is on, to seed the next call.
    /// </param>
    public Vector3 Snap(Vector3 point, ref int hint)
    {
        if (hint >= 0 && hint < _neighbours.Length && _neighbours[hint] is not null)
        {
            int best = -1;
            var bestPoint = point;
            float bestDistance = float.MaxValue;
            bool bestIsNear = false;

            Consider(hint, near: true);
            foreach (int first in _neighbours[hint])
            {
                Consider(first, near: true);
                foreach (int second in _neighbours[first]) Consider(second, near: false);
            }

            if (best >= 0 && bestIsNear)
            {
                hint = best;
                return bestPoint;
            }

            void Consider(int face, bool near)
            {
                var candidate = ClosestOnFace(face, point);
                float d = Vector3.DistanceSquared(candidate, point);
                if (d >= bestDistance) return;

                bestDistance = d;
                bestPoint = candidate;
                best = face;
                bestIsNear = near;
            }
        }

        var snapped = Snap(point);
        hint = NearestFace(snapped);
        return snapped;
    }

    private Vector3 ClosestOnFace(int face, Vector3 point) =>
        _corners.TryGetValue(face, out var corner)
            ? ClosestOnTriangle(point, corner.A, corner.B, corner.C)
            : _centroid[face];

    /// <summary>
    /// The point of a triangle nearest a given point, by the standard region test: the answer is inside
    /// the triangle, on one of its edges, or at one of its corners, and which of those is settled from
    /// the barycentric coordinates without ever dividing by a degenerate area.
    /// </summary>
    private static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;

        float d1 = Vector3.Dot(ab, ap);
        float d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0f && d2 <= 0f) return a;

        var bp = p - b;
        float d3 = Vector3.Dot(ab, bp);
        float d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0f && d4 <= d3) return b;

        float vc = (d1 * d4) - (d3 * d2);
        if (vc <= 0f && d1 >= 0f && d3 <= 0f)
            return a + (ab * (d1 / (d1 - d3)));

        var cp = p - c;
        float d5 = Vector3.Dot(ab, cp);
        float d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0f && d5 <= d6) return c;

        float vb = (d5 * d2) - (d1 * d6);
        if (vb <= 0f && d2 >= 0f && d6 <= 0f)
            return a + (ac * (d2 / (d2 - d6)));

        float va = (d3 * d6) - (d5 * d4);
        if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
            return b + ((c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));

        float denominator = va + vb + vc;
        if (denominator <= 1e-20f) return a;

        return a + (ab * (vb / denominator)) + (ac * (vc / denominator));
    }

    /// <summary>Where a point sits round the rim, as a fractional index into the first crease.</summary>
    public float RimPosition(Vector3 point)
    {
        int face = NearestFace(point);
        return face < 0 ? 0f : _rimIndex[face];
    }

    /// <summary>
    /// Which way round the rim a walk from <paramref name="from"/> to <paramref name="to"/> has to go
    /// to pass through <paramref name="through"/>.
    ///
    /// <para>
    /// Needed because a walk is confined to one of the two arcs and nothing about the two ends says
    /// which. Callers assumed forward, and that is right exactly when the line happens to run the same
    /// way round the rim as the crease it is indexed against - which is a coin toss, since a crease's
    /// point order comes from whichever way the contour tracer happened to walk it. Measured on
    /// <c>scalp</c>, where it comes out the other way, every one of the thirteen spans was re-walked the
    /// long way round: 5,849mm of parting line where the two ends are 38mm apart.
    /// </para>
    ///
    /// <para>
    /// Settled from a point the span already passes through rather than from the shorter arc, because
    /// shorter is not the question. A span that has been dragged most of the way round its rim is
    /// legitimately the long arc, and re-walking it the short way does not shorten the line - it swaps
    /// which side of the mould that stretch belongs to.
    /// </para>
    /// </summary>
    public bool ArcForward(Vector3 from, Vector3 to, Vector3 through)
    {
        float fromIndex = RimPosition(from);
        return Forward(fromIndex, RimPosition(through), true)
            <= Forward(fromIndex, RimPosition(to), true);
    }

    /// <summary>
    /// Walks the wall from one point to another, the way round given by <paramref name="forward"/>.
    ///
    /// <para>
    /// Confined to the arc between the two ends rather than left to find the shortest route, because on
    /// a closed rim the shortest route between two handles well apart is the other way round - and a
    /// stretch traced the wrong way round is not a worse line, it is a line that has swapped which side
    /// of the mould it belongs to.
    /// </para>
    /// </summary>
    /// <param name="slack">
    /// How far outside the arc, in crease samples, the walk may stray. Not zero: the arc is defined by
    /// a face's nearest crease sample, which steps rather than varies smoothly, so a walk held exactly
    /// inside it can be blocked by a single face whose nearest sample happens to sit a step outside.
    /// </param>
    public IReadOnlyList<Vector3>? Walk(Vector3 from, Vector3 to, bool forward = true, float slack = 8f)
    {
        var faces = WalkFaces(from, to, forward, slack);
        if (faces is null) return null;

        var path = new Vector3[faces.Count];
        for (int i = 0; i < faces.Count; i++) path[i] = _centroid[faces[i]];
        return path;
    }

    /// <summary>
    /// The corridor of band faces the walk passes through, in order. Consecutive entries always share
    /// an edge, so this is a triangle strip - which is what <see cref="WalkGeodesic"/> needs, and it is
    /// the reason the search is separated from the points it used to return directly.
    /// </summary>
    private List<int>? WalkFaces(Vector3 from, Vector3 to, bool forward, float slack)
    {
        int start = NearestFace(from);
        int goal = NearestFace(to);
        if (start < 0 || goal < 0) return null;
        if (start == goal) return new List<int> { start };

        float fromIndex = _rimIndex[start];
        float toIndex = _rimIndex[goal];

        var previous = new Dictionary<int, int> { [start] = -1 };
        var distance = new Dictionary<int, float> { [start] = 0f };
        var queue = new PriorityQueue<int, float>();
        queue.Enqueue(start, 0f);

        while (queue.TryDequeue(out int face, out float cost))
        {
            if (face == goal) break;
            if (cost > distance[face]) continue;

            foreach (int next in _neighbours[face])
            {
                if (next != goal && !InArc(_rimIndex[next], fromIndex, toIndex, forward, slack)) continue;

                float step = cost + Vector3.Distance(_centroid[face], _centroid[next]);
                if (distance.TryGetValue(next, out float known) && step >= known) continue;

                distance[next] = step;
                previous[next] = face;
                queue.Enqueue(next, step);
            }
        }

        if (!previous.ContainsKey(goal)) return null;

        var corridor = new List<int>();
        for (int at = goal; at >= 0; at = previous[at]) corridor.Add(at);
        corridor.Reverse();
        return corridor;
    }

    /// <summary>
    /// Walks the wall as <see cref="Walk"/> does, then pulls the result taut - so what comes back is the
    /// shortest path across the wall between the two ends rather than a chain of face middles.
    ///
    /// <para>
    /// The difference is not cosmetic. <see cref="Walk"/> returns one point per face it crossed, each at
    /// that face's centre, and a chain of triangle centres zigzags by construction: it steps to the
    /// middle of the next triangle whether or not the route wanted to go there, so a stretch that should
    /// read as a straight run across the wall comes back with a turn at every face - and the flange is
    /// swept along exactly these points. Re-walking a span on every frame of a drag made that the shape
    /// the user was editing.
    /// </para>
    ///
    /// <para>
    /// The path is taken to be the geodesic <em>within the corridor</em> the walk found, not on the mesh
    /// at large. That is deliberate and it is what an unconstrained geodesic cannot give: the corridor
    /// is confined to the band and to the arc the caller asked for, so every point of the result lies on
    /// an edge between two band faces - on the wall by construction, and on the side of the rim the
    /// caller meant. A geodesic free to find its own way does neither. It would cut the corner over a
    /// crease wherever that is shorter, and between two handles well apart on a closed rim it would take
    /// the short way round, which reverses that stretch of the parting line.
    /// </para>
    ///
    /// <para>
    /// Pulled taut by local unfolding rather than by edge flipping. Each crossing is placed where a
    /// straight line would cross it once its two faces are unfolded flat, which is the geodesic
    /// condition - equal angles either side - and it is a closed form per crossing rather than a search.
    /// Sweeping the chain a few times converges, and the corridor is fixed throughout, which is what
    /// keeps the guarantee above. Flipping edges would let the path leave the corridor to shorten
    /// itself, which is the one thing it must not do.
    /// </para>
    /// </summary>
    /// <param name="passes">
    /// Sweeps of the taut-string relaxation. It stops early once nothing moves by more than a thousandth
    /// of a mean edge, which on the bodies here happens well inside the default.
    /// </param>
    public IReadOnlyList<Vector3>? WalkGeodesic(
        Vector3 from, Vector3 to, bool forward = true, float slack = 8f, int passes = 24)
    {
        var corridor = WalkFaces(from, to, forward, slack);
        if (corridor is null) return null;

        var start = Snap(from);
        var end = Snap(to);

        if (corridor.Count < 2) return new[] { start, end };

        // The gates between consecutive faces. A missing one means the corridor stepped across an edge
        // that is not shared by exactly two band faces, which the walk cannot produce - but rather than
        // trust that, the walk's own answer is handed back.
        var gates = new (Vector3 A, Vector3 B)[corridor.Count - 1];
        for (int i = 0; i < gates.Length; i++)
        {
            if (!_portals.TryGetValue(Pair(corridor[i], corridor[i + 1]), out var gate))
                return Walk(from, to, forward, slack);

            gates[i] = gate;
        }

        // Started at the middle of each gate. Any starting point on the gate converges to the same
        // place; the middle is simply the one furthest from having to be clamped on the first sweep.
        var crossings = new Vector3[gates.Length];
        for (int i = 0; i < gates.Length; i++) crossings[i] = (gates[i].A + gates[i].B) * 0.5f;

        float settled = MeanEdge * 1e-3f;

        for (int pass = 0; pass < passes; pass++)
        {
            float moved = 0f;

            // Swept in place, so a crossing sees its predecessor's new position within the same pass.
            // Converges in roughly half the sweeps of a version that works off a copy.
            for (int i = 0; i < crossings.Length; i++)
            {
                var before = crossings[i];
                var previous = i == 0 ? start : crossings[i - 1];
                var next = i == crossings.Length - 1 ? end : crossings[i + 1];

                crossings[i] = Straighten(gates[i], previous, next);
                moved = MathF.Max(moved, Vector3.Distance(before, crossings[i]));
            }

            if (moved <= settled) break;
        }

        // Crossings that have collapsed onto each other are dropped: a taut path routed round a vertex
        // puts several consecutive gates at that same vertex, and repeated points are zero-length
        // segments, which turn up downstream as a division by zero in anything normalising a direction
        // along the line.
        var path = new List<Vector3>(crossings.Length + 2) { start };
        foreach (var crossing in crossings)
            if (Vector3.DistanceSquared(path[^1], crossing) > settled * settled) path.Add(crossing);

        if (Vector3.DistanceSquared(path[^1], end) > settled * settled) path.Add(end);
        else path[^1] = end;

        return path.Count >= 2 ? path : new[] { start, end };
    }

    /// <summary>
    /// Where a straight line from <paramref name="previous"/> to <paramref name="next"/> crosses
    /// <paramref name="gate"/>, once the two faces meeting at that gate are unfolded into one plane.
    ///
    /// <para>
    /// Unfolding is what makes this a closed form. In the unfolded plane the two points sit either side
    /// of the gate line, and the straight segment between them crosses it at the position that divides
    /// the gate in proportion to their distances from it - which is also the point that minimises the
    /// two lengths summed, and so the geodesic condition. Neither face's plane is needed: the two
    /// distances and the two positions along the gate are all the unfolding preserves.
    /// </para>
    /// </summary>
    private static Vector3 Straighten((Vector3 A, Vector3 B) gate, Vector3 previous, Vector3 next)
    {
        var along = gate.B - gate.A;
        float length = along.LengthSquared();
        if (length < 1e-12f) return gate.A;

        along /= MathF.Sqrt(length);

        Measure(previous, out float previousAlong, out float previousOff);
        Measure(next, out float nextAlong, out float nextOff);

        float total = previousOff + nextOff;

        // Both ends sitting on the gate line leaves nothing to divide in proportion to. That is a
        // degenerate crossing rather than a wrong one, so it is left where the midpoint of the two
        // would put it.
        float at = total < 1e-9f
            ? (previousAlong + nextAlong) * 0.5f
            : ((previousAlong * nextOff) + (nextAlong * previousOff)) / total;

        return gate.A + (along * Math.Clamp(at, 0f, MathF.Sqrt(length)));

        void Measure(Vector3 point, out float distanceAlong, out float distanceOff)
        {
            var offset = point - gate.A;
            distanceAlong = Vector3.Dot(offset, along);
            distanceOff = (offset - (along * distanceAlong)).Length();
        }
    }

    /// <summary>Whether a rim position lies on the way from one index to another, going the given way.</summary>
    private bool InArc(float at, float from, float to, bool forward, float slack)
    {
        float span = Forward(from, to, forward);
        float offset = Forward(from, at, forward);
        return offset <= span + slack;
    }

    private float Forward(float from, float to, bool forward)
    {
        float delta = forward ? to - from : from - to;
        while (delta < 0f) delta += RimSamples;
        while (delta >= RimSamples) delta -= RimSamples;
        return delta;
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
}
