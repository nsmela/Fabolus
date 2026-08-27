using System.Numerics;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.PartingSplit;

/// <summary>What the body is, topologically, in the terms that decide how it can be parted.</summary>
public enum PartingBodyShape
{
    /// <summary>Not watertight. The genus is undefined and no count taken off the surface can be trusted.</summary>
    Open,

    /// <summary>A sphere - two surfaces joined at a rim, which is what a bolus normally is.</summary>
    Shell,

    /// <summary>A shell with something passing through it: a tracheostomy, an ear canal.</summary>
    Torus,

    /// <summary>More than one hole through the body.</summary>
    MultipleHoles,
}

/// <summary>
/// One closed ridge contour, and whether it cuts the body in two.
/// </summary>
/// <param name="SecondShare">
/// The second largest piece as a share of the surface. A curve that shaves a sliver off is not
/// dividing the body in any useful sense, so a piece has to be worth having before it counts.
/// </param>
/// <param name="Components">
/// Every piece the walls leave, including the slivers. Walling both sides of a rim at once isolates the
/// band strip between them as a piece of its own, and a body with four rim contours leaves a dozen or
/// more of those - true, and useless as a description of what the split produces.
/// </param>
/// <param name="SubstantialPieces">
/// Pieces worth <see cref="MinimumPieceShare"/> of the surface or more, which is the count that means
/// "the halves". This is the one to report; <paramref name="Components"/> is kept because a body whose
/// two counts diverge wildly is one whose rims are being walled twice over, and that is worth being
/// able to see.
/// </param>
public sealed record PartingContourSeparation(
    int ContourIndex, int Components, int SubstantialPieces, float LargestShare, float SecondShare)
{
    /// <summary>
    /// How much of the surface the smaller piece has to be worth before a curve counts as having
    /// divided the body, rather than as having shaved a sliver off it.
    /// </summary>
    public const float MinimumPieceShare = 0.05f;

    public bool Separates => SubstantialPieces >= 2;
}

/// <summary>
/// One rim, and whether it is a wall or a knife edge.
///
/// <para>
/// A rim is normally a wall: two creases with the shell's thickness between them, which come back as
/// two contours running parallel about a wall apart. Where a shell tapers until its two creases meet
/// that stops being true - the rim is a single ridge, there is no band between anything, and the
/// contour is the parting line rather than one boundary of it. The distinction is not a defect either
/// way; it decides what can be done with the rim.
/// </para>
/// </summary>
/// <param name="Id">The band group behind the rim, which is what gives it an identity.</param>
/// <param name="ContourIndices">Its closed contours: two for a wall, one for a single ridge.</param>
/// <param name="Spacing">
/// How far its contours run from each other. Meaningless for a rim with one contour, which is reported
/// as infinity rather than zero so it cannot be mistaken for a very tight wall.
/// </param>
/// <param name="IsWall">Null when no wall thickness was supplied to judge the spacing against.</param>
public sealed record PartingRim(
    int Id, IReadOnlyList<int> ContourIndices, int Points, float Spacing, bool? IsWall)
{
    /// <summary>
    /// How far apart two contours may be, as a multiple of the wall, and still be the two sides of one
    /// rim. Paired rims measure 0.92 to 1.07 across the sample set and unpaired ones 1.56 upwards, so
    /// this sits in a wide empty gap rather than on a judgement call.
    /// </summary>
    public const float WallPairingTolerance = 1.6f;

    public bool IsSingleRidge => Kind == PartingRimKind.SingleRidge;

    /// <summary>
    /// What shape this rim is, from how many contours it came back as.
    ///
    /// <para>
    /// Two is the healthy answer - a wall seen from both sides. One means the shell tapered until the
    /// two creases met and there is no wall left. More than two means two rims have been counted as
    /// one, which happens where their walls touch: the band groups that name the rims merge into a
    /// single group, and nothing downstream can then tell which contour belongs to which rim.
    /// </para>
    /// </summary>
    public PartingRimKind Kind =>
        ContourIndices.Count == 1 ? PartingRimKind.SingleRidge
        : ContourIndices.Count > 2 ? PartingRimKind.Merged
        : IsWall is null ? PartingRimKind.Unknown
        : IsWall.Value ? PartingRimKind.Wall
        : PartingRimKind.SingleRidge;

    /// <summary>Where this rim's parting line runs.</summary>
    public string Line => Kind switch
    {
        PartingRimKind.Wall => "between its two contours",
        PartingRimKind.SingleRidge => "the contour itself",
        PartingRimKind.Merged =>
            $"undecidable - {ContourIndices.Count} contours on one band group, so two rims have merged",
        _ => "unknown - no wall thickness supplied",
    };
}

public enum PartingRimKind
{
    Unknown,

    /// <summary>Two contours a wall apart: the normal case, and the line runs between them.</summary>
    Wall,

    /// <summary>One contour, the shell having tapered to a knife edge. The contour is the line.</summary>
    SingleRidge,

    /// <summary>
    /// More contours than a rim has sides, because two rims' walls touch and share a band group. The
    /// contours are real; what is missing is which rim each belongs to.
    /// </summary>
    Merged,
}

/// <summary>
/// Which way a body's parting line should be found, and the evidence behind the answer.
/// </summary>
/// <param name="Recommended">Null when neither source has an answer for this body.</param>
public sealed record PartingStrategyReport(
    PartingBodyShape Shape, int Genus, int EulerCharacteristic, bool IsClosed,
    int ClosedContours, int SeparatingContours, int NonSeparatingContours,
    IReadOnlyList<PartingContourSeparation> Separations,
    PartingContourSeparation Combined,
    IReadOnlyList<PartingRim> Rims,
    bool SeamAvailable, string? SeamError,
    PartingLineSource? Recommended, string Summary)
{
    public static PartingStrategyReport Unavailable(string reason) => new(
        PartingBodyShape.Open, -1, 0, false, 0, 0, 0,
        Array.Empty<PartingContourSeparation>(), new PartingContourSeparation(-1, 0, 0, 0f, 0f),
        Array.Empty<PartingRim>(), false, reason, null, reason);

    /// <summary>
    /// How many cuts this body needs before it comes apart: one for a shell, and one more for every
    /// hole through it.
    ///
    /// <para>
    /// This is why a single rim is the wrong thing to ask a torus for. On a shell any closed curve
    /// divides the surface, so one rim is a parting line. On a body with a hole, a curve round the
    /// outside divides nothing on its own - the two sides still meet by going round through the hole -
    /// and the same is true of a curve round the hole. Only the two together part it. So a per-contour
    /// test reports failure on a body whose rims are perfectly good, and the count it should be
    /// measured against is this one.
    /// </para>
    /// </summary>
    public int CutsNeeded => Genus < 0 ? 0 : Genus + 1;

    /// <summary>
    /// How many closed curves may fail to divide this body before something is wrong. One per hole
    /// through it, and none at all on a shell.
    /// </summary>
    public int NonSeparatingBudget => Genus > 0 ? Genus : 0;

    /// <summary>Whether more curves failed to divide the body than its shape accounts for.</summary>
    public bool OverBudget => NonSeparatingContours > NonSeparatingBudget;

    /// <summary>
    /// Whether parting this body takes more than one cut, so the split needs a parting mesh per rim
    /// rather than one for the whole body.
    /// </summary>
    public bool NeedsHybrid => Recommended == PartingLineSource.ExtrusionBorder && CutsNeeded > 1;

    /// <summary>How many rims are a single ridge rather than a wall with two sides.</summary>
    public int SingleRidgeRims => Rims.Count(r => r.IsSingleRidge);

    /// <summary>
    /// Whether every rim is a knife edge, which is the case the band model has no answer for: there is
    /// no wall to bound, so each contour is a parting line in itself rather than one side of one.
    /// </summary>
    public bool AllRimsAreSingleRidges => Rims.Count > 0 && SingleRidgeRims == Rims.Count;

    /// <summary>Rims whose contours could not be told apart because two rims share a band group.</summary>
    public int MergedRims => Rims.Count(r => r.Kind == PartingRimKind.Merged);
}

/// <summary>
/// Decides how a body's parting line should be found, before any of it is computed.
///
/// <para>
/// There are two sources and they answer different questions.
/// <see cref="PartingLineSource.ExtrusionBorder"/> asks where the body's own edge runs and needs no
/// pull direction; <see cref="PartingLineSource.Silhouette"/> asks where the surface turns away from a
/// given axis and cannot be computed without one. The first is the better answer wherever it applies,
/// because a bolus is a shell and its rim is where a mould naturally wants to part - but it only
/// applies to a body that has such a rim, and not every body does.
/// </para>
///
/// <para>
/// The test for "has a rim" is whether any closed ridge contour cuts the shell into two substantial
/// pieces. That is a stricter question than whether the ridge looks right, and the two come apart on
/// real bodies: the body with the worst band statistics in the sample set has a rim that separates
/// cleanly, while one with better statistics and four closed contours has not one that divides
/// anything. Quality measures rank those two the same way and the wrong way round. Separation does
/// not, and it needs no threshold to be argued over.
/// </para>
/// </summary>
public static class PartingStrategy
{
    /// <param name="seamAvailable">Whether the extrusion border could actually be traced on this body.</param>
    /// <param name="wallThickness">
    /// The shell's wall, used only to decide whether a rim's two contours are the two sides of one rim
    /// or two unrelated rims. Optional because it costs a ray cast the rest of this needs no part of;
    /// without it the pairing distances are still reported and only the wall-or-knife-edge call is left
    /// open.
    /// </param>
    public static PartingStrategyReport Evaluate(
        IMesh body, RidgeDetectionOptions? options = null,
        bool seamAvailable = true, string? seamError = null, float wallThickness = float.NaN)
    {
        if (body is null || body.Triangles.Length == 0)
            return PartingStrategyReport.Unavailable("no body mesh");

        var topology = RidgeDetection.MeasureTopology(body);
        var shape = Describe(topology);

        var contours = RidgeDetection.FindRidgeContours(body, options ?? RidgeDetectionOptions.Default);
        var closed = contours.Where(c => c.IsClosed).ToList();

        var index = new SurfaceIndex(body);
        var separations = new List<PartingContourSeparation>(closed.Count);
        for (int i = 0; i < closed.Count; i++) separations.Add(index.Separate(closed[i], i));

        int separating = separations.Count(s => s.Separates);
        int nonSeparating = separations.Count - separating;

        // All of them at once, which is the question a body with a hole can actually answer.
        var combined = index.Separate(closed, -1);

        var report = new PartingStrategyReport(
            shape, topology.Genus, topology.EulerCharacteristic, topology.IsClosed,
            closed.Count, separating, nonSeparating, separations, combined,
            GroupRims(closed, wallThickness), seamAvailable, seamError, null, "");

        var (source, summary) = Recommend(report);
        return report with { Recommended = source, Summary = summary };
    }

    /// <summary>
    /// Pairs each closed contour with its nearest neighbour, which is what says whether its rim is a
    /// wall or a knife edge. A wall's two sides run parallel about a thickness apart; a single ridge's
    /// nearest neighbour is a different rim somewhere else on the body, which is far.
    /// </summary>
    /// <summary>
    /// Sorts closed contours into the rims they belong to. Public because anything that draws the ridge
    /// has to draw the rims apart, and re-deriving the grouping beside this one is how the picture and
    /// the report come to disagree.
    /// </summary>
    /// <param name="wallThickness">
    /// Optional. Without it a rim with two contours cannot be told from two unrelated ones, so its kind
    /// comes back <see cref="PartingRimKind.Unknown"/>; the one-contour and more-than-two cases need no
    /// thickness and are decided either way.
    /// </param>
    public static IReadOnlyList<PartingRim> Rims(
        IReadOnlyList<RidgeContour> closed, float wallThickness = float.NaN) =>
        GroupRims(closed, wallThickness);

    private static IReadOnlyList<PartingRim> GroupRims(
        IReadOnlyList<RidgeContour> closed, float wallThickness)
    {
        bool judgeable = !float.IsNaN(wallThickness) && wallThickness > 1e-3f;

        // Grouped by the band behind them rather than by proximity. Two rims of a body with a hole can
        // run close together where they converge, so distance alone would merge them; the wall each
        // belongs to is a different connected group of regions and cannot.
        var byRim = new Dictionary<int, List<int>>();
        for (int i = 0; i < closed.Count; i++)
        {
            int id = closed[i].Rim;
            if (!byRim.TryGetValue(id, out var list)) byRim[id] = list = new List<int>(2);
            list.Add(i);
        }

        var rims = new List<PartingRim>(byRim.Count);
        foreach (var (id, indices) in byRim.OrderBy(g => g.Key))
        {
            int points = indices.Sum(i => closed[i].Points.Count);

            // The mean gap between the rim's own contours. A wall has two running a thickness apart; a
            // single ridge has one and nothing to measure against.
            float spacing = float.PositiveInfinity;
            for (int a = 0; a < indices.Count; a++)
                for (int b = 0; b < indices.Count; b++)
                {
                    if (a == b) continue;

                    float mean = closed[indices[a]].Points.Average(p => DistanceTo(p, closed[indices[b]]));
                    spacing = MathF.Min(spacing, mean);
                }

            bool? isWall = indices.Count < 2 ? false
                : !judgeable ? null
                : spacing < wallThickness * PartingRim.WallPairingTolerance;

            rims.Add(new PartingRim(id, indices, points, spacing, isWall));
        }

        return rims;
    }

    private static float DistanceTo(Vector3 point, RidgeContour contour)
    {
        var pts = contour.Points;
        int spans = contour.IsClosed ? pts.Count : pts.Count - 1;

        float best = float.MaxValue;
        for (int i = 0; i < spans; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var ab = b - a;
            float length = ab.LengthSquared();
            float t = length < 1e-12f ? 0f : Math.Clamp(Vector3.Dot(point - a, ab) / length, 0f, 1f);
            best = MathF.Min(best, Vector3.Distance(point, a + (ab * t)));
        }
        return best;
    }

    private static PartingBodyShape Describe(MeshTopology topology) =>
        !topology.IsClosed ? PartingBodyShape.Open
        : topology.Genus switch
        {
            <= 0 => PartingBodyShape.Shell,
            1 => PartingBodyShape.Torus,
            _ => PartingBodyShape.MultipleHoles,
        };

    /// <summary>
    /// The rule, in one place so it reads as a rule rather than as a table of results.
    ///
    /// <para>
    /// Watertightness is asked first, because on an open mesh the genus is undefined and every count
    /// below it is being read off a surface with a border. Separation is asked next, because it decides
    /// which question the body can answer at all. The seam comes last, because it only decides whether
    /// the preferred answer is available - and a body with a dividing rim whose trace failed is called
    /// out rather than quietly demoted: the rim says the border is there to be found, so a failure to
    /// find it is a fault to fix and not a reason to reach for a pull direction.
    /// </para>
    /// </summary>
    private static (PartingLineSource? Source, string Summary) Recommend(PartingStrategyReport r)
    {
        if (!r.IsClosed)
            return (null, "Open mesh - not watertight, so neither source can be trusted.");

        string shape = r.Shape switch
        {
            PartingBodyShape.Torus => "Torus (one hole through the body)",
            PartingBodyShape.MultipleHoles => $"{r.Genus} holes through the body",
            _ => "Shell",
        };

        // Asked of the rims together rather than one at a time, because that is what parting the body
        // actually does and it is the only form of the question a body with a hole can answer. A single
        // rim on a torus divides nothing however good it is - the two sides still meet by going round
        // through the hole - so a per-contour test condemns rims that are perfectly correct. Reading
        // the two the wrong way round is exactly the mistake this replaced.
        bool parts = r.Combined.Separates;

        if (parts && r.SeamAvailable)
            return (PartingLineSource.ExtrusionBorder, r.CutsNeeded > 1
                ? $"{shape}. Its {r.ClosedContours} rims part the body together - " +
                  $"{r.Combined.LargestShare:P0} and {r.Combined.SecondShare:P0} of the surface " +
                  "either side of the rim wall - so it takes one parting mesh per rim."
                : $"{shape}. {r.SeparatingContours} of {r.ClosedContours} closed rim contours divide " +
                  "the body, so it parts along its own edge.");

        if (parts)
            return (PartingLineSource.ExtrusionBorder,
                $"{shape}. The rims part the body, so the extrusion border is there to be found - but " +
                $"tracing it failed{(r.SeamError is null ? "" : $": {r.SeamError}")}. Fix that rather " +
                "than falling back to a pull direction.");

        if (r.ClosedContours < r.CutsNeeded)
            return (PartingLineSource.Silhouette,
                $"{shape}. Needs {r.CutsNeeded} rims to come apart and only {r.ClosedContours} were " +
                "found, so at least one was missed. Needs a pull direction until that is fixed.");

        return (PartingLineSource.Silhouette,
            $"{shape}. Its {r.ClosedContours} rims do not part the body even together, so it has to " +
            "be pulled along an axis.");
    }

    /// <summary>
    /// Face centroids in a uniform grid plus face adjacency, built once per body: the nearest-face
    /// lookup and the flood are both per-contour, and rebuilding this for each would turn a moment's
    /// work into a long one.
    /// </summary>
    private sealed class SurfaceIndex
    {
        private readonly int[] _triangles;
        private readonly Vector3[] _centroids;
        private readonly float[] _areas;
        private readonly float _totalArea;
        private readonly Dictionary<(int, int), List<int>> _edges = new();
        private readonly Dictionary<(int, int, int), List<int>> _cells = new();
        private readonly List<int>[] _faceNeighbours;
        private readonly float _cell;

        public SurfaceIndex(IMesh body)
        {
            var vertices = body.Vertices;
            _triangles = body.Triangles;
            int faceCount = _triangles.Length / 3;

            _centroids = new Vector3[faceCount];
            _areas = new float[faceCount];

            double edgeTotal = 0d;
            for (int f = 0; f < faceCount; f++)
            {
                var a = vertices[_triangles[f * 3]];
                var b = vertices[_triangles[(f * 3) + 1]];
                var c = vertices[_triangles[(f * 3) + 2]];

                _centroids[f] = (a + b + c) / 3f;
                _areas[f] = Vector3.Cross(b - a, c - a).Length() * 0.5f;
                _totalArea += _areas[f];
                edgeTotal += Vector3.Distance(a, b);

                for (int e = 0; e < 3; e++)
                {
                    int i = _triangles[(f * 3) + e];
                    int j = _triangles[(f * 3) + ((e + 1) % 3)];
                    var key = i < j ? (i, j) : (j, i);
                    if (!_edges.TryGetValue(key, out var list)) _edges[key] = list = new List<int>(2);
                    list.Add(f);
                }
            }

            _cell = MathF.Max((float)(edgeTotal / Math.Max(faceCount, 1)) * 2f, 1e-4f);
            for (int f = 0; f < faceCount; f++)
            {
                var key = Cell(_centroids[f]);
                if (!_cells.TryGetValue(key, out var list)) _cells[key] = list = new List<int>(4);
                list.Add(f);
            }

            _faceNeighbours = new List<int>[faceCount];
            for (int f = 0; f < faceCount; f++) _faceNeighbours[f] = new List<int>(3);
            foreach (var shared in _edges.Values)
                for (int i = 0; i < shared.Count; i++)
                    for (int j = 0; j < shared.Count; j++)
                        if (i != j) _faceNeighbours[shared[i]].Add(shared[j]);
        }

        private (int, int, int) Cell(Vector3 p) => (
            (int)MathF.Floor(p.X / _cell), (int)MathF.Floor(p.Y / _cell), (int)MathF.Floor(p.Z / _cell));

        /// <summary>
        /// Marks the faces the contour runs across as a wall and floods what is left, so "does this
        /// curve divide the body" is answered without cutting the mesh.
        /// </summary>
        public PartingContourSeparation Separate(RidgeContour contour, int index) =>
            Separate(new[] { contour }, index);

        /// <summary>
        /// The same question asked of several curves at once. A body with a hole through it is not
        /// parted by any one of its rims, so asking them one at a time reports failure on a body whose
        /// rims are exactly right; asking them together is what a hybrid parting actually does.
        /// </summary>
        public PartingContourSeparation Separate(IReadOnlyList<RidgeContour> contours, int index)
        {
            int faceCount = _areas.Length;
            var wall = new bool[faceCount];

            foreach (var contour in contours) Mark(contour, wall);
            return Flood(wall, index);
        }

        private void Mark(RidgeContour contour, bool[] wall)
        {

            // Marking the nearest face to each contour point is not enough on its own. Consecutive
            // points frequently land on the same face and then skip one entirely where the curve
            // crosses a corner, and a ribbon with a single face missing does not block a flood at all -
            // it reports one component and looks exactly like a curve that genuinely divides nothing.
            // Walking the gap closes it.
            var marks = new List<int>(contour.Points.Count);
            foreach (var point in contour.Points)
            {
                int nearest = Nearest(point);
                if (nearest < 0) continue;

                wall[nearest] = true;
                marks.Add(nearest);
            }

            if (marks.Count == 0) return;

            int spans = contour.IsClosed ? marks.Count : marks.Count - 1;
            for (int i = 0; i < spans; i++) Connect(wall, marks[i], marks[(i + 1) % marks.Count]);
        }

        /// <summary>Counts what the walled surface falls into.</summary>
        private PartingContourSeparation Flood(bool[] wall, int index)
        {
            int faceCount = _areas.Length;
            var seen = new bool[faceCount];
            var componentArea = new List<float>();
            var stack = new Stack<int>();

            for (int seed = 0; seed < faceCount; seed++)
            {
                if (seen[seed] || wall[seed]) continue;

                float area = 0f;
                seen[seed] = true;
                stack.Push(seed);

                while (stack.Count > 0)
                {
                    int face = stack.Pop();
                    area += _areas[face];

                    for (int e = 0; e < 3; e++)
                    {
                        int a = _triangles[(face * 3) + e];
                        int b = _triangles[(face * 3) + ((e + 1) % 3)];
                        var key = a < b ? (a, b) : (b, a);

                        foreach (int across in _edges[key])
                        {
                            if (across == face || wall[across] || seen[across]) continue;

                            seen[across] = true;
                            stack.Push(across);
                        }
                    }
                }

                componentArea.Add(area);
            }

            componentArea.Sort();
            componentArea.Reverse();

            int substantial = _totalArea <= 0f
                ? 0
                : componentArea.Count(a => a / _totalArea >= PartingContourSeparation.MinimumPieceShare);

            return new PartingContourSeparation(
                index, componentArea.Count, substantial,
                componentArea.Count > 0 && _totalArea > 0f ? componentArea[0] / _totalArea : 0f,
                componentArea.Count > 1 && _totalArea > 0f ? componentArea[1] / _totalArea : 0f);
        }

        /// <summary>
        /// Widens until something is found: a contour is lifted off the surface, so the cell it lands
        /// in is occasionally empty even though a face is right beside it.
        /// </summary>
        private int Nearest(Vector3 point)
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
                                float d = Vector3.DistanceSquared(_centroids[f], point);
                                if (d >= bestDistance) continue;

                                bestDistance = d;
                                best = f;
                            }
                        }

                if (best >= 0) return best;
            }

            return -1;
        }

        /// <summary>Walls the shortest run of faces between two marks, closing a skipped corner.</summary>
        private void Connect(bool[] wall, int from, int to, int maxSteps = 24)
        {
            if (from == to) return;

            var previous = new Dictionary<int, int> { [from] = -1 };
            var frontier = new Queue<int>();
            frontier.Enqueue(from);

            for (int step = 0; step < maxSteps && frontier.Count > 0; step++)
            {
                int wide = frontier.Count;
                for (int i = 0; i < wide; i++)
                {
                    int current = frontier.Dequeue();
                    foreach (int next in _faceNeighbours[current])
                    {
                        if (previous.ContainsKey(next)) continue;

                        previous[next] = current;
                        if (next == to)
                        {
                            for (int at = to; at >= 0; at = previous[at]) wall[at] = true;
                            return;
                        }

                        frontier.Enqueue(next);
                    }
                }
            }
        }
    }
}
