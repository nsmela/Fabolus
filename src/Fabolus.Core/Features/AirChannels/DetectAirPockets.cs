using BasicResults;
using Fabolus.Core.Geometry;

namespace Fabolus.Core.Features.AirChannels;

/// <summary>
/// How deep a pocket has to be before it is worth venting, and how close two vents may stand.
/// </summary>
/// <param name="MinimumDepth">
/// How far below its peak the air in a pocket has to pool before it could spill over towards
/// somewhere higher. Shallower pockets are mesh noise, or trap too little air to matter.
/// </param>
/// <param name="MinimumSpacing">
/// The closest two channels may meet the surface. Two closer than this would overlap or leave a
/// wall between them too thin to print; only the higher one is kept. Measured in 3D rather than
/// in plan, so a channel low on a wall does not crowd out one on the top above it.
/// </param>
public sealed record AirPocketSettings(double MinimumDepth, double MinimumSpacing);

/// <summary>
/// Where a channel should be placed to vent one pocket: a point on the surface at the top of the
/// pocket, the surface normal there, and how much air the pocket would otherwise hold back.
/// </summary>
/// <param name="Depth">How far below the peak the pocket spills; infinite for the highest pocket, which never spills.</param>
public readonly record struct AirPocket(Vector3 Point, Vector3 Normal, double Depth);

/// <summary>
/// Finds the places a casting would trap air: the high points of the bolus surface, which are the
/// ceiling of the mould cavity once it is poured. Silicone rises through the cavity and pushes air
/// up ahead of it, so every local peak ends up holding a bubble unless a channel vents it.
/// </summary>
/// <remarks>
/// <para>A peak's depth is its topographic prominence. Sweeping a level down from the top of the
/// mesh, each peak starts its own region of surface above the level; when two regions meet, the
/// lower peak's pocket has just spilled into the higher one, and the distance from that peak down
/// to the level is how much air it held. Only the highest peak of each shell never spills.</para>
///
/// <para>A pocket already vented by an existing channel is skipped. A channel only vents the part
/// of a pocket above where it enters - once the silicone rises past its tip, whatever air is left
/// higher up is sealed in - so it counts only if it stands within the minimum depth of the peak.</para>
/// </remarks>
public sealed class DetectAirPockets(IGeometryEngine Engine)
{
    /// <summary>
    /// The pockets that still need a channel. A one-off shortcut for <see cref="Analyze"/>
    /// followed by <see cref="AirPocketMap.Unvented"/>.
    /// </summary>
    /// <param name="existingVents">
    /// Points where channels already meet the surface. Pockets they vent are skipped, and no new
    /// channel is placed within the minimum spacing of one.
    /// </param>
    public Result<IReadOnlyList<AirPocket>> Execute(IMesh mesh, AirPocketSettings settings, IReadOnlyList<Vector3> existingVents)
    {
        var map = Analyze(mesh);
        if (map.IsFailure)
            return map.Error;

        return map.Value.Unvented(settings, existingVents);
    }

    /// <summary>
    /// Finds every peak of the mesh and how deep its pocket is. This is the part that walks the
    /// whole mesh; the map it returns answers <see cref="AirPocketMap.Unvented"/> cheaply, so a
    /// caller asking again whenever the channels change should keep the map rather than call
    /// <see cref="Execute"/>.
    /// </summary>
    public Result<AirPocketMap> Analyze(IMesh mesh)
    {
        if (mesh.IsEmpty)
            return Result<AirPocketMap>.Success(AirPocketMap.Empty);

        var normalsResult = Engine.Evaluators.ComputeVertexNormals(mesh);
        if (normalsResult.IsFailure)
            return normalsResult.Error;

        var surface = Surface.Weld(mesh, normalsResult.Value);

        // A local peak facing down is the top of something poking up into the cavity from
        // below - the silicone flows over it, and air has nowhere to collect.
        // Highest first, so when two peaks crowd each other the higher one keeps its channel.
        var peaks = FindPeaks(surface)
            .Where(p => surface.Normals[p.Vertex].Z > 0)
            .OrderByDescending(p => surface.Positions[p.Vertex].Z)
            .ToList();

        return Result<AirPocketMap>.Success(new AirPocketMap(surface, peaks));
    }

    /// <summary>
    /// Every local peak of the surface, with its prominence: the elder-rule sweep described on
    /// the class, run over a union-find of vertices in descending height.
    /// </summary>
    private static List<(int Vertex, double Depth)> FindPeaks(Surface surface)
    {
        var count = surface.Positions.Length;

        // Descending height; ties broken by index so the sweep has one fixed order.
        var order = Enumerable.Range(0, count)
            .OrderByDescending(v => surface.Positions[v].Z)
            .ThenBy(v => v)
            .ToArray();

        var rank = new int[count];
        for (var i = 0; i < order.Length; i++)
            rank[order[i]] = i;

        var parent = new int[count];
        var peakOf = new int[count];
        var visited = new bool[count];
        var peaks = new List<(int Vertex, double Depth)>();

        int Find(int v)
        {
            while (parent[v] != v)
            {
                parent[v] = parent[parent[v]];
                v = parent[v];
            }
            return v;
        }

        foreach (var v in order)
        {
            visited[v] = true;
            parent[v] = v;
            peakOf[v] = v;

            var root = -1;
            foreach (var n in surface.Neighbours(v))
            {
                if (!visited[n]) continue;

                var other = Find(n);
                if (root == -1)
                {
                    root = other;
                    continue;
                }
                if (other == root) continue;

                // Two pockets meet at v: the one with the lower peak spills into the other here.
                var (elder, younger) = rank[peakOf[root]] < rank[peakOf[other]] ? (root, other) : (other, root);
                var spilled = peakOf[younger];
                peaks.Add((spilled, surface.Positions[spilled].Z - surface.Positions[v].Z));

                parent[younger] = elder;
                root = elder;
            }

            if (root == -1)
                continue; // nothing above v touches it: v is a peak, and starts its own pocket

            parent[v] = root;
        }

        // Whatever is left never spilled anywhere: the top of each shell.
        for (var v = 0; v < count; v++)
        {
            if (Find(v) == v)
                peaks.Add((peakOf[v], double.PositiveInfinity));
        }

        return peaks;
    }

    /// <summary>
    /// The mesh as a graph of welded vertices. An imported STL repeats every corner once per
    /// triangle, and unwelded it would be a soup of separate triangles, each one its own peak.
    /// </summary>
    internal sealed class Surface
    {
        public required Vector3[] Positions { get; init; }
        public required Vector3[] Normals { get; init; }
        public required List<(int A, int B, int C)> Triangles { get; init; }

        // Compressed adjacency: the neighbours of v are _adjacency[_offsets[v] .. _offsets[v + 1]].
        private int[] _offsets = [];
        private int[] _adjacency = [];

        public ReadOnlySpan<int> Neighbours(int v) => _adjacency.AsSpan(_offsets[v], _offsets[v + 1] - _offsets[v]);

        public static Surface Weld(IMesh mesh, IReadOnlyList<Vector3> vertexNormals)
        {
            var ids = new Dictionary<Vector3, int>();
            var remap = new int[mesh.VertexCount];
            var positions = new List<Vector3>();
            var normalSums = new List<Vector3>();

            for (var i = 0; i < mesh.VertexCount; i++)
            {
                var position = mesh.Vertices[i];
                if (!ids.TryGetValue(position, out var id))
                {
                    id = positions.Count;
                    ids[position] = id;
                    positions.Add(position);
                    normalSums.Add(Vector3.Zero);
                }

                remap[i] = id;
                normalSums[id] += vertexNormals[i];
            }

            var triangles = new List<(int, int, int)>(mesh.TriangleCount);
            var degree = new int[positions.Count + 1];
            for (var t = 0; t < mesh.TriangleCount; t++)
            {
                var a = remap[mesh.Triangles[t * 3]];
                var b = remap[mesh.Triangles[(t * 3) + 1]];
                var c = remap[mesh.Triangles[(t * 3) + 2]];
                if (a == b || b == c || c == a) continue;

                triangles.Add((a, b, c));
                degree[a] += 2;
                degree[b] += 2;
                degree[c] += 2;
            }

            // Each edge is listed once per triangle using it, so neighbours repeat - harmless for
            // both the sweep and the flood fill, and cheaper than removing them.
            var offsets = new int[positions.Count + 1];
            for (var v = 0; v < positions.Count; v++)
                offsets[v + 1] = offsets[v] + degree[v];

            var adjacency = new int[offsets[^1]];
            var fill = (int[])offsets.Clone();
            foreach (var (a, b, c) in triangles)
            {
                adjacency[fill[a]++] = b; adjacency[fill[a]++] = c;
                adjacency[fill[b]++] = c; adjacency[fill[b]++] = a;
                adjacency[fill[c]++] = a; adjacency[fill[c]++] = b;
            }

            var normals = normalSums
                .Select(n => n.LengthSquared > 0 ? n.Normalize() : Vector3.Zero)
                .ToArray();

            return new Surface
            {
                Positions = [.. positions],
                Normals = normals,
                Triangles = triangles,
                _offsets = offsets,
                _adjacency = adjacency,
            };
        }

        /// <summary>The vertices connected to <paramref name="start"/> through surface no lower than <paramref name="floor"/>.</summary>
        public HashSet<int> RegionAbove(int start, double floor)
        {
            var region = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                var v = queue.Dequeue();
                foreach (var n in Neighbours(v))
                {
                    if (Positions[n].Z >= floor && region.Add(n))
                        queue.Enqueue(n);
                }
            }

            return region;
        }

        public int NearestVertex(Vector3 point)
        {
            var best = 0;
            var bestDistance = double.MaxValue;
            for (var v = 0; v < Positions.Length; v++)
            {
                var distance = Positions[v].DistanceSquared(point);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = v;
                }
            }
            return best;
        }
    }
}

/// <summary>
/// The peaks of one mesh and how deep each pocket is, from <see cref="DetectAirPockets.Analyze"/>.
/// Asking which of them still need a channel is cheap, so keep one of these per mesh and ask
/// again whenever the channels or the settings change.
/// </summary>
/// <remarks>Not safe to query from several threads at once: it caches each summit as it finds it.</remarks>
public sealed class AirPocketMap
{
    // How far below a peak the surface still counts as its summit. A flat top is summit all over,
    // and the channel goes in the middle of it rather than at whichever corner sorted first.
    private const double SummitTolerance = 0.05;

    /// <summary>A map with no pockets in it, as of an empty mesh.</summary>
    public static readonly AirPocketMap Empty = new(null, []);

    private readonly DetectAirPockets.Surface? _surface;

    // Every upward-facing peak, highest first, with how far its pocket holds air.
    private readonly IReadOnlyList<(int Vertex, double Depth)> _peaks;

    // Where the channel for each peak goes; depends only on the mesh, so worked out once.
    private readonly Dictionary<int, (Vector3 Point, Vector3 Normal)> _summits = [];

    internal AirPocketMap(DetectAirPockets.Surface? surface, IReadOnlyList<(int Vertex, double Depth)> peaks)
    {
        _surface = surface;
        _peaks = peaks;
    }

    /// <summary>
    /// The pockets at least <see cref="AirPocketSettings.MinimumDepth"/> deep that no existing
    /// channel vents, thinned out to <see cref="AirPocketSettings.MinimumSpacing"/>, highest first.
    /// </summary>
    /// <param name="existingVents">
    /// Points where channels already meet the surface. Pockets they vent are skipped, and no new
    /// channel is placed within the minimum spacing of one.
    /// </param>
    public Result<IReadOnlyList<AirPocket>> Unvented(AirPocketSettings settings, IReadOnlyList<Vector3> existingVents)
    {
        if (settings is null)
            return new Error("AirPockets.NullSettings", "Air pocket settings must be provided.");

        if (settings.MinimumDepth <= 0)
            return new Error("AirPockets.InvalidDepth", "The minimum pocket depth must be greater than zero.");

        var surface = _surface;
        if (surface is null)
            return Result<IReadOnlyList<AirPocket>>.Success(Array.Empty<AirPocket>());

        var ventVertices = existingVents.Select(surface.NearestVertex).ToList();
        var placed = new List<AirPocket>();

        foreach (var (peak, depth) in _peaks)
        {
            if (depth < settings.MinimumDepth)
                continue;

            var floor = surface.Positions[peak].Z - settings.MinimumDepth;
            var cap = surface.RegionAbove(peak, floor);

            // Both tests are needed: the vent has to stand high enough, and on this pocket rather
            // than on some other surface at that height. Its nearest vertex can sit higher than it
            // on a coarse mesh, so the height comes from the vent itself.
            var vented = existingVents
                .Where((vent, i) => vent.Z >= floor && cap.Contains(ventVertices[i]))
                .Any();
            if (vented)
                continue;

            var (point, normal) = Summit(surface, peak);
            var pocket = new AirPocket(point, normal, depth);

            var crowded = existingVents.Concat(placed.Select(p => p.Point))
                .Any(other => other.DistanceTo(pocket.Point) < settings.MinimumSpacing);
            if (crowded)
                continue;

            placed.Add(pocket);
        }

        return Result<IReadOnlyList<AirPocket>>.Success(placed);
    }

    /// <summary>
    /// Where the channel goes: the middle of the summit when the top of the pocket is flat, so a
    /// channel on a level top sits in the centre rather than on its edge, otherwise the peak.
    /// </summary>
    private (Vector3 Point, Vector3 Normal) Summit(DetectAirPockets.Surface surface, int peak)
    {
        if (_summits.TryGetValue(peak, out var cached))
            return cached;

        var summit = FindSummit(surface, peak);
        _summits[peak] = summit;
        return summit;
    }

    private static (Vector3 Point, Vector3 Normal) FindSummit(DetectAirPockets.Surface surface, int peak)
    {
        var peakPosition = surface.Positions[peak];
        var fallback = (peakPosition, surface.Normals[peak]);

        var inSummit = surface.RegionAbove(peak, peakPosition.Z - SummitTolerance);
        if (inSummit.Count < 3)
            return fallback;

        var centreX = inSummit.Average(v => surface.Positions[v].X);
        var centreY = inSummit.Average(v => surface.Positions[v].Y);

        foreach (var (a, b, c) in surface.Triangles)
        {
            if (!inSummit.Contains(a) || !inSummit.Contains(b) || !inSummit.Contains(c))
                continue;

            var pa = surface.Positions[a];
            var pb = surface.Positions[b];
            var pc = surface.Positions[c];

            if (!Barycentric(pa, pb, pc, centreX, centreY, out var u, out var w, out var t))
                continue;

            var point = new Vector3(centreX, centreY, (u * pa.Z) + (w * pb.Z) + (t * pc.Z));
            var normal = (pb - pa).Cross(pc - pa);

            // A summit shaped like a ring has its middle over the hole; the triangle under the
            // centre then faces down or sits well below the top, and the peak is the better spot.
            if (normal.Z <= 0 || point.Z < peakPosition.Z - SummitTolerance)
                continue;

            return (point, normal.Normalize());
        }

        return fallback;
    }

    // Barycentric coordinates of (x, y) in the triangle's XY shadow; false when outside it.
    private static bool Barycentric(Vector3 a, Vector3 b, Vector3 c, double x, double y, out double u, out double w, out double t)
    {
        u = w = t = 0;

        var area = ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
        if (Math.Abs(area) < 1e-12)
            return false;

        w = (((x - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (y - a.Y))) / area;
        t = (((b.X - a.X) * (y - a.Y)) - ((x - a.X) * (b.Y - a.Y))) / area;
        u = 1 - w - t;

        const double slack = -1e-9;
        return u >= slack && w >= slack && t >= slack;
    }
}
