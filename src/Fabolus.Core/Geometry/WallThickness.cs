using BasicResults;

namespace Fabolus.Core.Geometry;

/// <summary>
/// The distribution of a <see cref="WallThickness"/> measurement, over the faces that could be
/// measured. Faces whose probe never came out the far side are excluded from every figure here -
/// they contribute to <see cref="UnmeasuredFraction"/> instead - because a face looking lengthwise
/// down a shell is not reporting a thickness at all, and letting those into the statistics drags
/// every one of them upward.
/// </summary>
public sealed record WallThicknessStatistics
{
    /// <summary>
    /// The shell's wall thickness. The median rather than the mean: even after excluding the faces
    /// that never exited, the ones near a rim read long, and there is no upper bound on how long.
    /// </summary>
    public required double Median { get; init; }

    public required double Mean { get; init; }
    public required double Minimum { get; init; }
    public required double Maximum { get; init; }

    /// <summary>
    /// Spread about the <see cref="Mean"/>. Small next to the median means a shell of even
    /// thickness; large means the two surfaces are not parallel, which is worth knowing before
    /// trusting anything that assumes a constant offset.
    /// </summary>
    public required double StandardDeviation { get; init; }

    /// <summary>
    /// Fifth and ninety-fifth percentiles - the working range of the wall, ignoring the tails. Useful
    /// for choosing a band around the median without having to sort the per-face values again.
    /// </summary>
    public required double FifthPercentile { get; init; }
    public required double NinetyFifthPercentile { get; init; }

    /// <summary>How many faces returned a thickness, and how many there were in total.</summary>
    public required int MeasuredFaces { get; init; }
    public required int TotalFaces { get; init; }

    /// <summary>
    /// Share of faces whose probe never exited, in [0, 1]. On a shell this is roughly the rim: the
    /// faces of the wall swept between the two surfaces, whose normals run along the shell rather
    /// than across it.
    /// </summary>
    public double UnmeasuredFraction =>
        TotalFaces > 0 ? 1.0 - ((double)MeasuredFaces / TotalFaces) : 0.0;

    public static WallThicknessStatistics Empty { get; } = new()
    {
        Median = 0.0,
        Mean = 0.0,
        Minimum = 0.0,
        Maximum = 0.0,
        StandardDeviation = 0.0,
        FifthPercentile = 0.0,
        NinetyFifthPercentile = 0.0,
        MeasuredFaces = 0,
        TotalFaces = 0,
    };
}

/// <summary>
/// How thick a solid is, measured face by face, with the distribution of that measurement alongside.
///
/// <para>
/// The bodies a mould is built around are a surface given thickness: an anatomy surface, an offset
/// copy of it, and the wall swept between their boundaries. The thickness of that shell is the one
/// dimension that describes the whole piece, and until now nothing recorded it - it was known only
/// as "5 to 10mm, whatever the operator chose".
/// </para>
///
/// <para>
/// The measurement also says which part of the shell a face belongs to, which is what makes the
/// per-face array worth keeping rather than just the summary. A face on either surface looks
/// straight across the wall and reads one thickness; a face on the rim looks along the shell and
/// reads far more, or nothing at all. That is a statement about the shape, so unlike any measure of
/// curvature it reads the same however coarsely the mesh is tessellated.
/// </para>
/// </summary>
public sealed record WallThickness
{
    /// <summary>
    /// Distance through the solid along each face's own inward normal, indexed by triangle - the
    /// same order as <see cref="IMesh.Triangles"/> in groups of three.
    /// <see cref="double.PositiveInfinity"/> where the probe never came out the far side within
    /// <see cref="WallThicknessOptions.MaxThicknessMm"/>.
    /// </summary>
    public required IReadOnlyList<double> PerFace { get; init; }

    /// <summary>
    /// The same measurement carried to the vertices, indexed to match <see cref="IMesh.Vertices"/>,
    /// as the area-weighted mean of the measured faces around each one. Area-weighted so a fan of
    /// slivers cannot outvote the one broad face beside it.
    /// <see cref="double.PositiveInfinity"/> where no face around the vertex could be measured.
    ///
    /// <para>
    /// Indexed against the mesh exactly as given: display geometry arrives un-welded, so a corner
    /// shared by several faces appears several times and each copy carries only its own face.
    /// </para>
    /// </summary>
    public required IReadOnlyList<double> PerVertex { get; init; }

    /// <summary>
    /// The face each probe came out through, indexed by triangle; -1 where nothing was measured.
    ///
    /// <para>
    /// This is the offset correspondence made explicit. On a surface given thickness, a face and the
    /// face its probe exits through are the same place on opposite sides of the shell - that is what
    /// "offset" means. Knowing the pairing is worth more than the distance alone: the distance says
    /// how thick the shell is there, the pairing says which two patches of surface are the two sides
    /// of it, and that holds across a gap in either patch.
    /// </para>
    /// </summary>
    public required IReadOnlyList<int> PartnerFace { get; init; }

    public required WallThicknessStatistics Statistics { get; init; }

    /// <summary>
    /// The settings that produced this, so the result carries its own caveats - what counts as
    /// unmeasured depends entirely on how far the search was allowed to look.
    /// </summary>
    public required WallThicknessOptions Options { get; init; }

    /// <summary>Shorthand for <see cref="WallThicknessStatistics.Median"/>, the wall thickness.</summary>
    public double Median => Statistics.Median;

    /// <summary>
    /// Probes inward from each face along its own normal and reports where the probe came out the
    /// far side. Inside the solid the signed distance is negative, outside it is positive, so the
    /// crossing is the point where that flips - the search brackets it on a coarse sweep and then
    /// bisects, rather than stepping finely all the way, which is what keeps this to a couple of
    /// dozen probes a face instead of hundreds.
    /// </summary>
    public static Result<WallThickness> Measure(IGeometryEngine engine, IMesh mesh, WallThicknessOptions? options = null)
    {
        if (mesh is null) return MeshErrors.NullSource;
        options ??= WallThicknessOptions.Default;

        if (options.MaxThicknessMm <= 0.0 || options.CoarseSteps < 1 || options.ToleranceMm <= 0.0)
            return new Error("Geometry.InvalidThicknessOptions",
                "Search distance, step count and tolerance must all be positive.");

        var indexResult = engine.Spatial.IndexFor(mesh);
        if (indexResult.IsFailure) return indexResult.Error;
        var index = indexResult.Value;

        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;
        int faceCount = triangles.Length / 3;

        var perFace = new double[faceCount];
        var partner = new int[faceCount];
        var faceArea = new double[faceCount];
        Array.Fill(partner, -1);
        double coarse = options.MaxThicknessMm / options.CoarseSteps;
        var measured = new List<double>(faceCount);

        for (int f = 0; f < faceCount; f++)
        {
            var a = vertices[triangles[f * 3]];
            var b = vertices[triangles[(f * 3) + 1]];
            var c = vertices[triangles[(f * 3) + 2]];

            var cross = (b - a).Cross(c - a);
            faceArea[f] = cross.Length * 0.5;
            if (cross.LengthSquared < 1e-12)
            {
                perFace[f] = double.PositiveInfinity;   // degenerate face, no normal to probe along
                continue;
            }

            var origin = (a + b + c) / 3.0;
            var inward = -cross.Normalize();

            // Bracket: walk out until the probe reads outside.
            double previous = 0.0;
            double exit = double.PositiveInfinity;
            for (int step = 1; step <= options.CoarseSteps; step++)
            {
                double t = step * coarse;
                if (index.SignedDistance(origin + (inward * t)) > 0.0) { exit = t; break; }
                previous = t;
            }

            if (double.IsPositiveInfinity(exit))
            {
                perFace[f] = double.PositiveInfinity;
                continue;
            }

            // Bisect the bracket down to the requested tolerance.
            double low = previous, high = exit;
            while (high - low > options.ToleranceMm)
            {
                double mid = (low + high) * 0.5;
                if (index.SignedDistance(origin + (inward * mid)) > 0.0) high = mid;
                else low = mid;
            }

            perFace[f] = high;
            var exitFace = index.ClosestPoint(origin + (inward * high));
            partner[f] = exitFace.HasValue ? exitFace.Value.Triangle : -1;
            measured.Add(high);
        }

        return new WallThickness
        {
            PerFace = perFace,
            PerVertex = CarryToVertices(perFace, faceArea, triangles, vertices.Length),
            PartnerFace = partner,
            Statistics = Summarise(measured, faceCount),
            Options = options,
        };
    }

    /// <summary>
    /// Spreads the per-face measurement onto the vertices, weighted by face area so a fan of slivers
    /// around a vertex cannot outweigh the one broad face beside it. Faces that never exited carry no
    /// weight at all rather than counting as very thick.
    /// </summary>
    private static double[] CarryToVertices(
        double[] perFace, double[] faceArea, IReadOnlyList<int> triangles, int vertexCount)
    {
        var weighted = new double[vertexCount];
        var weight = new double[vertexCount];

        for (int f = 0; f < perFace.Length; f++)
        {
            if (!double.IsFinite(perFace[f])) continue;

            double w = faceArea[f];
            for (int corner = 0; corner < 3; corner++)
            {
                int v = triangles[(f * 3) + corner];
                weighted[v] += perFace[f] * w;
                weight[v] += w;
            }
        }

        var perVertex = new double[vertexCount];
        for (int v = 0; v < vertexCount; v++)
            perVertex[v] = weight[v] > 0.0 ? weighted[v] / weight[v] : double.PositiveInfinity;

        return perVertex;
    }

    /// <summary>
    /// Summarises the faces that returned a thickness. <paramref name="measured"/> is sorted here
    /// rather than by the caller, since every figure below wants it in order.
    /// </summary>
    private static WallThicknessStatistics Summarise(List<double> measured, int faceCount)
    {
        if (measured.Count == 0)
            return WallThicknessStatistics.Empty with { TotalFaces = faceCount };

        measured.Sort();

        double total = 0.0;
        foreach (double value in measured) total += value;
        double mean = total / measured.Count;

        double variance = 0.0;
        foreach (double value in measured)
        {
            double d = value - mean;
            variance += d * d;
        }

        return new WallThicknessStatistics
        {
            Median = measured[measured.Count / 2],
            Mean = mean,
            Minimum = measured[0],
            Maximum = measured[^1],
            StandardDeviation = Math.Sqrt(variance / measured.Count),
            FifthPercentile = measured[(int)(measured.Count * 0.05)],
            NinetyFifthPercentile = measured[Math.Min(measured.Count - 1, (int)(measured.Count * 0.95))],
            MeasuredFaces = measured.Count,
            TotalFaces = faceCount,
        };
    }
}

/// <summary>Search settings for <see cref="WallThickness.Measure"/>.</summary>
public sealed record WallThicknessOptions
{
    /// <summary>
    /// How far to look through the solid before giving up, in mm. Faces that reach this are reported
    /// as unmeasured rather than as very thick, because past this depth the probe is no longer
    /// crossing a wall - it is running the length of the body.
    /// </summary>
    public double MaxThicknessMm { get; init; } = 25.0;

    /// <summary>
    /// How precisely to locate the far surface, in mm. The search brackets the crossing coarsely and
    /// then bisects, so halving this costs one extra probe per face rather than doubling the work.
    /// </summary>
    public double ToleranceMm { get; init; } = 0.1;

    /// <summary>
    /// Probes taken on the first sweep. The bracket has to be found before it can be bisected, so
    /// this sets the coarsest wall the search can still see: a wall thinner than
    /// <see cref="MaxThicknessMm"/> over this many steps is stepped straight over.
    /// </summary>
    public int CoarseSteps { get; init; } = 24;

    public static WallThicknessOptions Default { get; } = new();
}
