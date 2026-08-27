using System.Numerics;

namespace Fabolus.Core.Geometry;

/// <summary>
/// Thresholds for <see cref="RidgeDetection"/>, in three groups: what counts as a crease, how far a
/// break in one may be bridged, and which of the regions the creases enclose get filled.
/// </summary>
public sealed record RidgeDetectionOptions
{
    // ---- what counts as a crease ----

    /// <summary>
    /// Curvature (1/mm) at which an edge is certain enough to seed a ridge on its own. 0.45 is a
    /// crease of roughly 2mm radius - far tighter than anything the offsetting that builds a bolus
    /// shell produces on its own, so a seed is always a deliberate edge rather than surface relief.
    /// </summary>
    public float SeedCurvature { get; init; } = 0.45f;

    /// <summary>
    /// Dihedral angle (degrees) that seeds a ridge whatever the curvature works out to. A fold this
    /// steep is a corner at any scale, and on a coarse mesh curvature alone will not say so: the
    /// measured radius of a crease can never come out tighter than about half the spacing of the
    /// triangles that sample it, so a genuine 90 degree rim on a 7mm mesh reads as a 4mm fillet.
    /// The angle does not care how far apart the samples are, which is exactly what is wanted here.
    /// </summary>
    public float SeedAngleDegrees { get; init; } = 50f;

    /// <summary>
    /// Curvature (1/mm) an edge must clear to be joined onto a ridge that already has a seed. Set
    /// well below <see cref="SeedCurvature"/> deliberately: a rim traced round a mesh weakens
    /// wherever the surface turns, and thresholding at the seed level alone chops it into dozens of
    /// fragments that then fail the length test individually. Growing from seeds through weaker
    /// edges is what keeps the rim a single feature.
    /// </summary>
    public float GrowCurvature { get; init; } = 0.20f;

    /// <summary>Dihedral angle (degrees) that extends a seeded ridge, whatever the curvature.</summary>
    public float GrowAngleDegrees { get; init; } = 25f;

    /// <summary>
    /// How long a ridge must be, as a fraction of the mesh's bounding-box diagonal, to be reported.
    /// This is the filter that separates a feature from noise: the rim of a bolus runs several times
    /// the diagonal, while the creases a coarse tessellation fakes are a handful of edges. 0.3 sits
    /// in the wide empty gap between the two on every sample tested.
    /// </summary>
    public float MinLengthFraction { get; init; } = 0.30f;

    /// <summary>
    /// Bail-out valve. If more than this fraction of the mesh's edges end up in ridges, the surface
    /// is too rough for the notion of a ridge to mean anything there and nothing is reported, rather
    /// than painting most of the model as ridge and calling that a detection. Reached only when
    /// <see cref="GrowCurvature"/> lets the grow pass percolate across the whole surface.
    /// </summary>
    public float MaxRidgeEdgeFraction { get; init; } = 0.35f;

    // ---- bridging breaks in a ridge ----

    /// <summary>
    /// How far a bridge may reach to close a break in a ridge, as a fraction of the bounding
    /// diagonal. Zero disables bridging. This is a plain distance along the surface: how long a
    /// break may be before it stops being a break and starts being a genuinely open rim.
    ///
    /// <para>
    /// Generous rather than tight, because an unclosed break is far more damaging than an over-long
    /// bridge: a break lets the region fill escape and swallow the model, whereas a bridge that joins
    /// the wrong things only pinches off an extra region, which then has to pass
    /// <see cref="MaxRegionAreaFraction"/> and <see cref="MaxRegionWidthFraction"/> before it is
    /// filled anyway.
    /// </para>
    /// </summary>
    public float MaxGapFraction { get; init; } = 0.06f;

    /// <summary>
    /// How strongly bridging prefers to route along whatever crease survives inside a break, as a
    /// multiplier on the cost of a completely flat edge. A break in a rim is usually a stretch where
    /// the fold has softened rather than vanished, so following the softened fold puts the bridge
    /// where the rim actually runs instead of cutting the corner across open surface.
    ///
    /// <para>
    /// This steers the route only; how far a bridge may reach is <see cref="MaxGapFraction"/> alone.
    /// Letting the penalty count against the budget too would mean a break's allowance depended on
    /// how flat the surface inside it happened to be - a 20mm reach across open surface would be
    /// refused while the same 20mm along a surviving crease was allowed, which is backwards.
    /// </para>
    /// </summary>
    public float GapFlatPenalty { get; init; } = 3f;

    /// <summary>
    /// How many steps along the ridge from a loose end still count as the same place. Without this a
    /// loose end bridges straight back to the edge it is already attached to, closing a two-edge
    /// loop and leaving the actual break open.
    /// </summary>
    public int GapRidgeHops { get; init; } = 4;

    // ---- which enclosed regions get filled ----

    /// <summary>
    /// The largest share of the model's surface area a region may hold and still be filled. The rim
    /// band of a bolus runs 0.5-5% of the surface while the shell faces it separates run 37-50%, so
    /// anything near this threshold is one of the main surfaces and must not be painted.
    /// </summary>
    public float MaxRegionAreaFraction { get; init; } = 0.15f;

    /// <summary>
    /// The widest a region may be, as a fraction of the bounding diagonal, and still be filled.
    /// Width here is twice the area over the ridge-bounded perimeter - the mean width of the strip.
    /// Checked as well as area because the two fail independently: a rim band is narrow <em>and</em>
    /// small, a shell face is broad <em>and</em> large, and requiring both leaves no single
    /// measurement able to paint half the model on its own.
    /// </summary>
    public float MaxRegionWidthFraction { get; init; } = 0.06f;

    /// <summary>
    /// How wide a hole in the rim band may be, as a multiple of the band's own width, and still be
    /// closed over. Zero leaves every hole open.
    ///
    /// <para>
    /// A rim built from anatomy is not a clean swept wall. Where the inner surface bulges up into it
    /// the band comes back with a pocket punched through it, correctly - the creases really do run
    /// round that pocket - but a band interrupted every few centimetres reads as several features
    /// rather than as the one rim it is, and anything that follows the band has to cope with a gap
    /// that means nothing.
    /// </para>
    ///
    /// <para>
    /// Measured against the band rather than against the model, because that is the comparison that
    /// makes it a blemish: a pocket narrower than the wall it sits in is an interruption of the wall,
    /// while one wider than the wall is the wall genuinely stopping and starting again. Width is twice
    /// the area over the perimeter, the same measure the fill itself uses.
    /// </para>
    /// </summary>
    public float MaxBandHoleWidthFraction { get; init; } = 1.0f;

    /// <summary>
    /// How far below the band around it a stretch of band has to fall before it is treated as a
    /// boundary that has ridden up over wall rather than a wall that genuinely narrows, as a fraction
    /// of the local width. Zero leaves such stretches alone.
    ///
    /// <para>
    /// This exists because a rim's lower crease can simply stop. Where the shell has been offset and
    /// remeshed the fold softens, and over a few centimetres it drops to five or ten degrees - not
    /// under a threshold that could be lowered to catch it, but genuinely rounded off, with no crease
    /// there to find at any setting. The flood then runs straight out of the wall into the shell's own
    /// surface and the band comes back a third of its width, bounded above by the crease that survived
    /// and below by nothing at all.
    /// </para>
    ///
    /// <para>
    /// Judged against the band on either side rather than against the wall, for the same reason the
    /// width is: a shell that tapers has a band that narrows and there is nothing wrong with it, and
    /// only the abruptness separates the two. That also keeps the ray cast a wall thickness would need
    /// out of what is otherwise pure geometry.
    /// </para>
    /// </summary>
    /// <para>
    /// Off by default until the contour side catches up. The repair does what it is meant to - the
    /// band comes back the width of the wall and the pocket is shaded - but the boundary it hands to
    /// the trace is a fresh run of edges meeting the surviving crease at each end, and
    /// <c>ChainCreases</c> ends a chain at every such junction. So a repaired rim comes back as several
    /// open contours where it used to come back as one closed one, and closure is the property
    /// everything downstream leans on. 0.5 is the setting to use once that is fixed.
    /// </para>
    public float BandShortfallFraction { get; init; } = 0.5f;

    public static RidgeDetectionOptions Default { get; } = new();
}

/// <summary>
/// One run of a mesh's ridge, as a curve. A rim that goes all the way round comes back
/// <see cref="IsClosed"/>; a crease that starts and stops - or a run between two junctions where
/// creases meet - comes back open, and its last point must not be joined back to its first.
/// </summary>
public sealed record RidgeContour(IReadOnlyList<Vector3> Points, bool IsClosed)
{
    /// <summary>
    /// Which rim this curve belongs to, or -1 where it could not be attributed to one.
    ///
    /// <para>
    /// A body with a hole through it has more than one rim, and both are walls between the same two
    /// surfaces - so nothing about a contour's own shape, or about which surfaces it divides, tells one
    /// rim from the other. What does is the wall behind it: each rim's band is a different connected
    /// group of regions, so the group is the rim's identity. Carried here because parting a body with
    /// several rims takes one parting mesh per rim, and that needs the contours sorted into rims before
    /// anything else can be decided.
    /// </para>
    /// </summary>
    public int Rim { get; init; } = -1;
}

/// <summary>
/// The ridge in both the forms it can be shown in: the facets it covers, and the curves that outline
/// them. Both come from one pass, so they always describe the same ridge.
/// </summary>
/// <param name="Faces">
/// Everything the ridge pass touched: the band, plus the faces on either side of every crease edge.
/// Indexed by triangle, matching <see cref="IMesh.Triangles"/> in groups of three.
///
/// <para>
/// Wider than the rim wall, and by construction rather than by accident - a crease edge has one face
/// inside the band and one on the surface beyond it, so marking both pads the mask by a face on each
/// side. Measured across the sample set that is 1.3 to 1.7 times the distance between the creases,
/// and it is not symmetric, because the faces on the two surfaces are not the same size. Use
/// <see cref="Band"/> for anything that means the wall - shading this one makes a correctly centred
/// parting line look as though it hugs one edge, which is exactly what it did.
/// </para>
/// </param>
public sealed record RidgeSurfaces(bool[] Faces, IReadOnlyList<RidgeContour> Contours)
{
    public static RidgeSurfaces Empty { get; } = new(Array.Empty<bool>(), Array.Empty<RidgeContour>());

    /// <summary>
    /// The rim wall itself: the faces enclosed by the creases, with no padding. This is the band the
    /// contours bound, the band the strategy report measures, and the band the parting line is centred
    /// in - so it is the one to shade when the question is where the line sits across the wall.
    ///
    /// <para>
    /// Empty when the ridge was not resolved into regions at all, in which case there is no band to
    /// speak of and <see cref="Faces"/> is all there is.
    /// </para>
    /// </summary>
    public bool[] Band { get; init; } = Array.Empty<bool>();

    /// <summary>
    /// Which rim each face belongs to, matching <see cref="RidgeContour.Rim"/>, and -1 where it belongs
    /// to none. Empty when the ridge was not attributed to rims at all.
    ///
    /// <para>
    /// Needed because a body with more than one rim is a body that parts differently from one with a
    /// single rim, and a face mask that says only "on the ridge" cannot show that. Shading every rim
    /// alike draws a torus exactly as it draws a shell, which is the one thing a reader most needs to
    /// see is not the case.
    /// </para>
    /// </summary>
    public int[] FaceRims { get; init; } = Array.Empty<int>();
}

/// <summary>
/// A mesh's shape in the only sense that decides what can be done with it: how many holes run through
/// it, and whether it is closed at all.
///
/// <para>
/// This is worth having separately from any measurement of the surface because it bounds what the
/// measurements can mean. A closed curve on a shell always divides it; on a body with a hole through
/// it, one independent curve can run round the hole and divide nothing. So the same count of
/// non-dividing curves is a detection failure on the first and arithmetic on the second, and nothing
/// short of the genus can tell those apart.
/// </para>
/// </summary>
public sealed record MeshTopology(int Vertices, int Edges, int Faces, int BoundaryEdges)
{
    public static MeshTopology Empty { get; } = new(0, 0, 0, 0);

    /// <summary>V - E + F on the welded surface.</summary>
    public int EulerCharacteristic => Vertices - Edges + Faces;

    /// <summary>Whether every edge borders two faces, which is what makes the genus mean anything.</summary>
    public bool IsClosed => BoundaryEdges == 0 && Faces > 0;

    /// <summary>How many holes run through the body; -1 when it is not closed and the question is void.</summary>
    public int Genus => IsClosed ? (2 - EulerCharacteristic) / 2 : -1;
}

/// <summary>
/// Finds the sharp convex creases - ridges - on a mesh, and the narrow regions they enclose.
///
/// <para>
/// The bodies a mould is built around are shells: a sheet of anatomy given thickness, so the outer
/// and inner surfaces meet in a rim that runs all the way round the piece. That rim is the model's
/// one unambiguous feature, and it is where a parting line naturally wants to sit. Nothing in the
/// draft classification can see it - a rim's facets face every which way, so shading by
/// normal-dot-pull scatters red, green and grey across it - which is why it is worth finding
/// separately and showing in its own colour.
/// </para>
///
/// <para>
/// A rim is usually not a knife edge but a wall: two creases with a band of surface between them.
/// Marking only the facets that touch a crease draws that as two hairlines with unshaded surface
/// trapped between, which reads as two separate features rather than as the one rim it is. So the
/// creases are treated as boundaries and the region they enclose is filled - the whole wall comes
/// out as a single band. Where a rim really is a knife edge the enclosed region is empty and there
/// is nothing to fill, so the crease-adjacent facets are marked as well and that case still reads.
/// </para>
///
/// <para>
/// Detection runs in four passes.
/// </para>
/// <list type="number">
///   <item><b>Measure.</b> Every edge gets a fold: the dihedral angle, and that angle over the
///     distance it turns through, which is curvature. Curvature is the main test because the angle
///     alone depends on tessellation - the same 5mm fillet reads as 25 degrees on a 2mm mesh and 39
///     degrees on a 3.5mm one. The angle is kept as a second test because curvature has the opposite
///     blind spot: a mesh cannot express a crease tighter than about half its own triangle spacing,
///     so a true 90 degree rim on a coarse mesh measures as a gentle fillet. An edge qualifies on
///     either count.</item>
///   <item><b>Threshold, with hysteresis.</b> Strong edges seed, weaker edges extend them, and a
///     connected run survives only if it is long relative to the model. A stair-stepped CT surface
///     is sharp all over, so sharpness cannot separate feature from noise on its own - length can,
///     because a rim runs several times the model's diagonal and noise is a handful of edges.</item>
///   <item><b>Bridge the breaks.</b> A rim that softens for a few triangles comes out of pass two as
///     two runs with a hole between them, and a hole is fatal to the next pass - the fill escapes
///     through it and floods the model. Loose ends are therefore joined across short gaps, following
///     whatever crease survives inside the gap.</item>
///   <item><b>Fill.</b> Flood the faces with the ridges as walls, then fill the regions that are
///     narrow and small enough to be a band rather than one of the surfaces the bands divide.</item>
/// </list>
/// </summary>
public static class RidgeDetection
{
    /// <summary>
    /// Grid size, in mm, that vertices are snapped to when working out which of them are the same
    /// point. Display geometry arrives un-welded (one vertex per corner per face) and has no edge
    /// adjacency at all until coincident corners are matched up, so this runs unconditionally.
    /// </summary>
    private const float WeldGridMm = 0.001f;

    /// <summary>
    /// The mesh's Euler characteristic and genus, from the same welded view detection is run on - so a
    /// caller comparing the two is not comparing answers from two different notions of which vertices
    /// are the same point.
    /// </summary>
    public static MeshTopology MeasureTopology(IMesh mesh)
    {
        if (mesh is null || mesh.Triangles.Length == 0) return MeshTopology.Empty;

        var surface = Surface.Build(mesh);
        return new MeshTopology(
            surface.Positions.Length, surface.Edges.Count, surface.FaceCount,
            surface.Edges.Count - surface.Folds.Count);
    }

    /// <summary>
    /// Classifies every triangle as lying on a ridge or not. The returned array is indexed by
    /// triangle, matching <see cref="IMesh.Triangles"/> in groups of three, and is all-false when
    /// the mesh has no ridges (or is too rough for the question to be meaningful).
    /// </summary>
    public static bool[] FindRidgeFaces(IMesh mesh, RidgeDetectionOptions options)
    {
        int triangleCount = mesh is null ? 0 : mesh!.Triangles.Length / 3;
        if (triangleCount == 0) return Array.Empty<bool>();

        var analysis = Analyse(mesh!, options);
        return analysis?.RidgeFaces ?? new bool[triangleCount];
    }

    /// <summary>
    /// The ridge as smooth closed curves - the outline of the surface <see cref="FindRidgeFaces"/>
    /// marks, traced and then relaxed off the triangles it was traced along.
    ///
    /// <para>
    /// Colouring facets can only ever be as smooth as the mesh is fine: a rim crossing a coarse
    /// surface at an angle comes out as a staircase of whole triangles, which reads as a jagged
    /// approximation of the feature rather than as the feature. A curve has no such limit. It is
    /// resampled to an even spacing and Taubin-relaxed, so the points leave the triangle edges they
    /// started on and the result follows where the rim actually runs rather than which facets happen
    /// to touch it.
    /// </para>
    ///
    /// <para>
    /// Each loop is lifted a little off the surface along its own normal. A relaxed curve cuts the
    /// corner on a convex feature, and a rim is convex by definition, so without the lift the curve
    /// would sink inside the model and be hidden by the very surface it describes.
    /// </para>
    /// </summary>
    /// <returns>The ridge as curves; empty when there is no ridge to trace.</returns>
    public static IReadOnlyList<RidgeContour> FindRidgeContours(IMesh mesh, RidgeDetectionOptions options)
    {
        if (mesh is null || mesh.Triangles.Length == 0) return Array.Empty<RidgeContour>();

        var analysis = Analyse(mesh, options);
        return analysis is null
            ? Array.Empty<RidgeContour>()
            : TraceContours(analysis.Surface, analysis.RidgeEdges, analysis.Filled, analysis.Territories);
    }

    /// <summary>
    /// Both forms of the answer from a single pass, for a caller that shows the ridge as shaded facets
    /// with the curve drawn over them.
    ///
    /// <para>
    /// Calling <see cref="FindRidgeFaces"/> and <see cref="FindRidgeContours"/> in turn runs the whole
    /// analysis twice, and on a twenty-thousand-face body that is the most expensive thing on the mesh
    /// load path. It also reintroduces the very disagreement the single internal pass exists to rule
    /// out - the two calls are separate runs, and nothing makes them agree.
    /// </para>
    /// </summary>
    public static RidgeSurfaces FindRidge(IMesh mesh, RidgeDetectionOptions options)
    {
        int triangleCount = mesh is null ? 0 : mesh!.Triangles.Length / 3;
        if (triangleCount == 0) return RidgeSurfaces.Empty;

        var analysis = Analyse(mesh!, options);
        if (analysis is null) return new RidgeSurfaces(new bool[triangleCount], Array.Empty<RidgeContour>());

        return new RidgeSurfaces(
            analysis.RidgeFaces,
            TraceContours(analysis.Surface, analysis.RidgeEdges, analysis.Filled, analysis.Territories))
        {
            FaceRims = FaceRims(analysis, triangleCount),
            Band = CloseEnclosedHoles(analysis.Surface, analysis.Filled),
        };
    }

    /// <summary>
    /// Fills any face outside the band whose every neighbour is inside it, repeating until a pass
    /// changes nothing.
    ///
    /// <para>
    /// A face enclosed on all sides by band is band - there is nothing else it can be, and the fill
    /// pass leaving it out is an artifact of working from ridge edges rather than a statement about the
    /// shape. Left in, each one is a speck of surface colour inside the rim in the Parting Split scene,
    /// and on a body being judged by eye a speckled band reads as a detector that cannot make its mind
    /// up.
    /// </para>
    ///
    /// <para>
    /// Applied to the reported band only, never to <c>Analysis.Filled</c>. The contours are traced from
    /// that, and the parting line is centred between the contours - so writing this back would let a
    /// cosmetic fix move the line, which is a great deal more than it was asked to do. Measured across
    /// the sample set it takes 0 to 32 faces, under 1% of the band, and converges in a single pass on
    /// every body; the loop is kept because converging in one pass is a property of these bodies rather
    /// than a guarantee.
    /// </para>
    /// </summary>
    /// <summary>
    /// How many of a face's three neighbours must already be band before it is taken into the band.
    ///
    /// <para>
    /// Three is the conservative reading - a face enclosed on every side can be nothing else. Two also
    /// takes in the notches, where the band's edge is locally concave and a face sits in the bite. It is
    /// the more aggressive rule and it is why this iterates: filling a face gives its own neighbours one
    /// more band neighbour each, so a concave stretch can fill inwards over several passes.
    /// </para>
    /// </summary>
    private const int BandNeighboursToFill = 2;

    private static bool[] CloseEnclosedHoles(Surface surface, bool[] band)
    {
        var closed = (bool[])band.Clone();
        var triangles = surface.Triangles;

        while (true)
        {
            var adding = new List<int>();

            for (int face = 0; face < closed.Length; face++)
            {
                if (closed[face]) continue;

                int inBand = 0;

                for (int e = 0; e < 3; e++)
                {
                    int a = triangles[(face * 3) + e];
                    int b = triangles[(face * 3) + ((e + 1) % 3)];
                    var key = a < b ? (a, b) : (b, a);

                    if (!surface.Edges.TryGetValue(key, out var pair)) continue;

                    int across = pair.First == face ? pair.Second : pair.First;
                    if (across >= 0 && closed[across]) inBand++;
                }

                if (inBand >= BandNeighboursToFill) adding.Add(face);
            }

            if (adding.Count == 0) break;
            foreach (int face in adding) closed[face] = true;
        }

        return closed;
    }

    /// <summary>
    /// The rim each face is on, taken from the band group behind it. A face on a shell surface, or on a
    /// crease that bounds no band, belongs to no rim and comes back as -1.
    /// </summary>
    private static int[] FaceRims(Analysis analysis, int triangleCount)
    {
        var rims = new int[triangleCount];
        Array.Fill(rims, -1);

        var territories = analysis.Territories;
        if (territories.First < 0) return rims;

        for (int f = 0; f < triangleCount && f < analysis.RidgeFaces.Length; f++)
        {
            if (!analysis.RidgeFaces[f]) continue;

            int region = territories.Region[f];
            if (region < 0 || region >= territories.IsBand.Length || !territories.IsBand[region]) continue;

            rims[f] = territories.BandGroup[region];
        }

        return rims;
    }

    /// <summary>
    /// Runs the same passes <see cref="FindRidgeFaces"/> and <see cref="FindRidgeContours"/> run, and
    /// reports what each one did as well as what it returned. Behaviour-identical to calling both -
    /// the analysis runs once and both forms are derived from it, exactly as the public pair does - so
    /// a report can never describe a different run from the one that produced the answer beside it.
    /// Diagnostic only; nothing in the app calls this.
    /// </summary>
    /// <param name="traceEdges">
    /// Also record what became of every edge, for diffing one run against another. Off by default: it
    /// is a record per interior edge, and only a comparison of two runs has any use for the refused
    /// ones.
    /// </param>
    internal static RidgeDiagnosis Diagnose(
        IMesh mesh, RidgeDetectionOptions options, bool traceEdges = false)
    {
        var diag = new RidgeDiagnostics { TracingEdges = traceEdges };
        int triangleCount = mesh is null ? 0 : mesh.Triangles.Length / 3;

        var analysis = triangleCount == 0 ? null : Analyse(mesh!, options, diag);

        return new RidgeDiagnosis(
            analysis?.RidgeFaces ?? new bool[triangleCount],
            analysis?.Filled ?? new bool[triangleCount],
            analysis is null
                ? Array.Empty<RidgeContour>()
                : TraceContours(
                    analysis.Surface, analysis.RidgeEdges, analysis.Filled, analysis.Territories, diag),
            diag.Build(),
            analysis is null ? RidgeBandProfileReport.Empty : DescribeBand(analysis),
            diag.EdgeTrace,
            analysis is null ? RidgeTerritoryReport.Empty : Describe(analysis.Territories));
    }

    /// <summary>Exposes the fill's partition for <see cref="Diagnose"/>.</summary>
    private static RidgeTerritoryReport Describe(Territories territories) =>
        territories.First < 0
            ? RidgeTerritoryReport.Empty
            : new RidgeTerritoryReport(
                true, territories.Region, territories.First, territories.Second,
                territories.IsBand, territories.BandGroup);

    /// <summary>Summarises the band's width profile for <see cref="Diagnose"/>.</summary>
    private static RidgeBandProfileReport DescribeBand(Analysis analysis)
    {
        var profile = MeasureBand(analysis.Surface, analysis.Territories, analysis.RidgeFaces);
        if (profile.Width.Length == 0) return RidgeBandProfileReport.Empty;

        var surface = analysis.Surface;
        var widths = new List<float>();
        float bandArea = 0f, suspectArea = 0f;
        int bandFaces = 0, suspectFaces = 0;

        for (int f = 0; f < profile.Width.Length; f++)
        {
            if (float.IsPositiveInfinity(profile.Width[f])) continue;

            widths.Add(profile.Width[f]);
            bandArea += surface.FaceArea[f];
            bandFaces++;

            if (!profile.Suspect[f]) continue;
            suspectArea += surface.FaceArea[f];
            suspectFaces++;
        }

        return new RidgeBandProfileReport(
            true, profile.MedianWidth, bandFaces, suspectFaces, suspectArea, bandArea,
            RidgeDistribution.From(widths, 0f, MathF.Max(profile.MedianWidth * 4f, 1f), 40),
            profile.Width, profile.Expected, profile.Suspect, profile.ToFirst, profile.ToSecond);
    }

    private sealed record Analysis(
        Surface Surface, HashSet<(int, int)> RidgeEdges, bool[] Filled, bool[] RidgeFaces,
        Territories Territories);

    /// <summary>
    /// How wide the band is at each of its faces, and where that width falls short of what the band is
    /// doing either side of it.
    ///
    /// <para>
    /// The width at a face is its distance across the band to one surface plus its distance to the
    /// other, so it is the wall's own width measured where the wall actually is. On every body tested
    /// this comes out within a few per cent of the shell's measured wall thickness, which is what makes
    /// it usable as the reference without a ray cast: the band is the wall seen edge-on, so the band's
    /// width is the wall's thickness.
    /// </para>
    ///
    /// <para>
    /// Shortfall is judged against a local expectation rather than one figure for the whole rim,
    /// because a shell that genuinely tapers has a band that genuinely narrows and there is nothing
    /// wrong with it. What distinguishes a fault is abruptness: a taper takes centimetres to halve,
    /// a stretch of missing crease collapses the width over a few triangles and recovers just as fast.
    /// Comparing each face to the median of the band around it sees the second and ignores the first.
    /// </para>
    /// </summary>
    /// <param name="ToFirst">Distance across the band to one of the two surfaces, and to the other in
    /// <paramref name="ToSecond"/>. Kept separately as well as summed, because which of the two
    /// collapsed is what names the boundary that moved: a band pinched from one side has one of these
    /// at its usual value and the other at nearly nothing.</param>
    private sealed record BandProfile(
        float[] Width, float[] Expected, bool[] Suspect, float MedianWidth,
        float[] ToFirst, float[] ToSecond)
    {
        public static BandProfile None { get; } = new(
            Array.Empty<float>(), Array.Empty<float>(), Array.Empty<bool>(), 0f,
            Array.Empty<float>(), Array.Empty<float>());
    }

    /// <summary>
    /// The shape of what the ridge edges cut the surface into: a region per face, the two largest
    /// regions - the outer and inner faces of the shell - and which of the rest are bands lying
    /// between those two.
    ///
    /// <para>
    /// This is what separates a rim from an ordinary crease. A rim's crease has one of the two big
    /// surfaces on one side and the rim's own band on the other. A crease that wanders across the
    /// inside of a nose or an ear has the same surface on both sides, because a line that does not
    /// close off an area does not divide one. The distinction needs no threshold, which is why it is
    /// worth preferring over any measure of how far a crease is from where a rim ought to be.
    /// </para>
    /// </summary>
    private sealed record Territories(int[] Region, int First, int Second, bool[] IsBand, int[] BandGroup)
    {
        public static Territories None { get; } =
            new(Array.Empty<int>(), -1, -1, Array.Empty<bool>(), Array.Empty<int>());

        /// <summary>
        /// Which rim a crease is on, as the surface it faces paired with the band group behind it.
        ///
        /// <para>
        /// The band group is what names the rim. A body with a hole through it has two rims, and both
        /// are walls between the same two surfaces, so the surfaces alone cannot tell them apart -
        /// but the wall of one rim is a different connected group of regions from the wall of the
        /// other. Following the group is therefore following the rim.
        /// </para>
        /// </summary>
        public (int Surface, int Group) Rim((int Left, int Right) regions)
        {
            int surface = regions.Left == First || regions.Left == Second ? regions.Left
                : regions.Right == First || regions.Right == Second ? regions.Right
                : -1;

            int group = regions.Left >= 0 && IsBand[regions.Left] ? BandGroup[regions.Left]
                : regions.Right >= 0 && IsBand[regions.Right] ? BandGroup[regions.Right]
                : -1;

            return (surface, group);
        }

        /// <summary>Whether an edge between these two regions is part of a rim rather than surface relief.</summary>
        public bool Divides(int left, int right)
        {
            if (First < 0 || left == right) return false;

            // One side has to be one of the two surfaces; the other has to be the remaining surface
            // or a band running between them.
            bool leftIsSurface = left == First || left == Second;
            bool rightIsSurface = right == First || right == Second;

            if (leftIsSurface && rightIsSurface) return true;
            if (leftIsSurface) return IsBand[right];
            if (rightIsSurface) return IsBand[left];

            // Two bands meeting is a crease inside the rim wall - a fillet step, or a bridge laid
            // across a break. The wall is already described by the two creases bounding it, so drawing
            // these as well would rule the wall with lines rather than outline it.
            return false;
        }
    }

    /// <summary>
    /// Runs the passes once and returns everything derived from them, so the facet and contour forms
    /// of the answer are the same answer rather than two runs that could disagree. Null when the mesh
    /// has no ridge at all.
    /// </summary>
    private static Analysis? Analyse(IMesh mesh, RidgeDetectionOptions options, RidgeDiagnostics? diag = null)
    {
        options ??= RidgeDetectionOptions.Default;

        var surface = Surface.Build(mesh);
        diag?.Surface(Describe(mesh, surface));
        if (surface.Edges.Count == 0) return null;

        var ridgeEdges = FindRidgeEdges(surface, options, diag);
        if (ridgeEdges.Count == 0) return null;

        BridgeBreaks(surface, ridgeEdges, options, diag);

        var filled = new bool[surface.FaceCount];
        var territories = FillEnclosedRegions(surface, ridgeEdges, options, filled, diag);
        var ridgeFaces = MarkFaces(surface, ridgeEdges, filled);

        if (diag is { TracingEdges: true }) FinaliseEdgeTrace(surface, ridgeEdges, diag);

        return new Analysis(surface, ridgeEdges, filled, ridgeFaces, territories);
    }

    /// <summary>
    /// Marks the traced edges that survived into the final ridge, and adds the ones bridging invented
    /// that no fold was ever measured for, so the trace accounts for the whole set rather than only the
    /// part the threshold pass saw.
    /// </summary>
    private static void FinaliseEdgeTrace(
        Surface surface, HashSet<(int, int)> ridgeEdges, RidgeDiagnostics diag)
    {
        foreach (var edge in ridgeEdges)
        {
            if (diag.EdgeTraceIndex.TryGetValue(edge, out int at))
            {
                diag.EdgeTrace[at] = diag.EdgeTrace[at] with { Final = true };
                continue;
            }

            var (first, second) = surface.Edges[edge];
            diag.EdgeTraceIndex[edge] = diag.EdgeTrace.Count;
            diag.EdgeTrace.Add(new RidgeEdgeAdmission(
                edge.Item1, edge.Item2, MidPoint(surface, edge), surface.EdgeLength(edge),
                first, second,
                float.NaN, float.NaN, false, false, null, 0, 0f, true));
        }
    }

    /// <summary>
    /// The facet form of the answer: what the fill covered, plus the facets the creases themselves
    /// touch. The second is needed because a knife-edge rim encloses nothing and would otherwise
    /// vanish from the facet form entirely. The contour form does not need it - it draws the crease
    /// directly - which is why the two are kept apart rather than the contour being traced round this.
    /// </summary>
    private static bool[] MarkFaces(Surface surface, HashSet<(int, int)> ridgeEdges, bool[] filled)
    {
        var ridgeFaces = (bool[])filled.Clone();
        foreach (var edge in ridgeEdges)
        {
            var (first, second) = surface.Edges[edge];
            ridgeFaces[first] = true;
            if (second >= 0) ridgeFaces[second] = true;
        }
        return ridgeFaces;
    }

    /// <summary>Summarises the welded surface and the fold spread across it, for <see cref="Diagnose"/>.</summary>
    private static RidgeSurfaceReport Describe(IMesh mesh, Surface surface)
    {
        var angles = new List<float>(surface.Folds.Count);
        var curvatures = new List<float>(surface.Folds.Count);
        foreach (var fold in surface.Folds.Values)
        {
            angles.Add(fold.AngleDegrees);
            curvatures.Add(fold.Curvature);
        }

        return new RidgeSurfaceReport(
            SourceVertices: mesh.Vertices.Length,
            WeldedVertices: surface.Positions.Length,
            Faces: surface.FaceCount,
            Edges: surface.Edges.Count,
            InteriorEdges: surface.Folds.Count,
            BoundaryEdges: surface.Edges.Count - surface.Folds.Count,
            Diagonal: surface.Diagonal,
            TotalArea: surface.TotalArea,
            MeanEdgeLength: surface.MeanEdgeLength,
            FoldAngleDegrees: RidgeDistribution.From(angles, -180f, 180f, 72),
            Curvature: RidgeDistribution.From(curvatures, -2f, 2f, 80));
    }

    // ---------------------------------------------------------------- pass 2: threshold

    /// <summary>
    /// The hysteresis: collect every edge that clears the grow level, split those into connected
    /// runs, and keep the runs that both contain a seed edge and are long enough to be a feature.
    /// </summary>
    private static HashSet<(int, int)> FindRidgeEdges(
        Surface surface, RidgeDetectionOptions options, RidgeDiagnostics? diag = null)
    {
        var kept = new HashSet<(int, int)>();
        float minLength = options.MinLengthFraction * surface.Diagonal;

        int seedTotal = 0, seedByCurvature = 0, seedByAngle = 0, growByCurvature = 0, growByAngle = 0;

        bool tracing = diag is { TracingEdges: true };

        var candidates = new List<(int, int)>();
        foreach (var (edge, fold) in surface.Folds)
        {
            // Convex only. A concave crease is a valley - the inside of a fold - and marking those
            // would paint the far side of every rim as well as the rim itself. Both measures are
            // signed, so testing them as greater-than does the convexity filtering for free.
            bool byCurvature = fold.Curvature > options.GrowCurvature;
            bool byAngle = fold.AngleDegrees > options.GrowAngleDegrees;

            // Recorded before the refusal below, because an edge that never became a candidate is
            // exactly the one a diff needs to account for.
            if (tracing)
            {
                var (left, right) = surface.Edges[edge];
                diag!.EdgeTraceIndex[edge] = diag.EdgeTrace.Count;
                diag.EdgeTrace.Add(new RidgeEdgeAdmission(
                    edge.Item1, edge.Item2, MidPoint(surface, edge), surface.EdgeLength(edge),
                    left, right,
                    fold.Curvature, fold.AngleDegrees,
                    Candidate: byCurvature || byAngle,
                    Seed: fold.Curvature > options.SeedCurvature
                        || fold.AngleDegrees > options.SeedAngleDegrees,
                    Verdict: null, RunEdges: 0, RunLength: 0f, Final: false));
            }

            if (!byCurvature && !byAngle) continue;

            candidates.Add(edge);

            if (diag is null) continue;
            if (byCurvature) growByCurvature++;
            if (byAngle) growByAngle++;
            bool seedCurvature = fold.Curvature > options.SeedCurvature;
            bool seedAngle = fold.AngleDegrees > options.SeedAngleDegrees;
            if (seedCurvature) seedByCurvature++;
            if (seedAngle) seedByAngle++;
            if (seedCurvature || seedAngle) seedTotal++;
        }

        if (candidates.Count == 0)
        {
            diag?.Threshold(0, 0, 0, 0, 0, 0, minLength, 0, surface.Edges.Count, false);
            return kept;
        }

        foreach (var run in ConnectedRuns(candidates))
        {
            bool hasSeed = false;
            int seedEdges = 0;
            float length = 0f;
            foreach (int index in run)
            {
                var edge = candidates[index];
                var fold = surface.Folds[edge];
                if (fold.Curvature > options.SeedCurvature || fold.AngleDegrees > options.SeedAngleDegrees)
                {
                    hasSeed = true;
                    seedEdges++;
                }
                length += surface.EdgeLength(edge);
            }

            var verdict = !hasSeed ? RidgeRunVerdict.NoSeed
                : length < minLength ? RidgeRunVerdict.TooShort
                : RidgeRunVerdict.Kept;
            diag?.Run(run.Count, length, hasSeed, seedEdges, verdict);

            if (tracing)
                foreach (int index in run)
                {
                    int at = diag!.EdgeTraceIndex[candidates[index]];
                    diag.EdgeTrace[at] = diag.EdgeTrace[at] with
                    {
                        Verdict = verdict,
                        RunEdges = run.Count,
                        RunLength = length,
                    };
                }

            if (verdict != RidgeRunVerdict.Kept) continue;

            foreach (int index in run) kept.Add(candidates[index]);
        }

        // Percolation guard. A surface rough enough that the grow pass links most of it into one
        // enormous "ridge" has no ridges to report, and saying so is more useful than colouring the
        // whole model.
        bool percolated = kept.Count > options.MaxRidgeEdgeFraction * surface.Edges.Count;
        diag?.Threshold(
            candidates.Count, seedTotal, seedByCurvature, seedByAngle, growByCurvature, growByAngle,
            minLength, kept.Count, surface.Edges.Count, percolated);
        if (percolated) kept.Clear();

        return kept;
    }

    /// <summary>
    /// Groups edge indices into runs connected through shared endpoints. Depth-first over an
    /// endpoint-to-edge index, so this is linear in the candidate set rather than in the mesh.
    /// </summary>
    private static List<List<int>> ConnectedRuns(List<(int, int)> edges)
    {
        var byEndpoint = new Dictionary<int, List<int>>(edges.Count * 2);
        for (int i = 0; i < edges.Count; i++)
        {
            Attach(byEndpoint, edges[i].Item1, i);
            Attach(byEndpoint, edges[i].Item2, i);
        }

        var visited = new bool[edges.Count];
        var runs = new List<List<int>>();
        var stack = new Stack<int>();

        // Hoisted out of the walk below. A stackalloc inside a loop is a fresh allocation per
        // iteration that is not reclaimed until the method returns, so the frame would grow by eight
        // bytes for every candidate edge processed.
        var endpoints = new int[2];

        for (int i = 0; i < edges.Count; i++)
        {
            if (visited[i]) continue;

            var run = new List<int>();
            stack.Push(i);
            visited[i] = true;

            while (stack.Count > 0)
            {
                int current = stack.Pop();
                run.Add(current);

                var edge = edges[current];
                endpoints[0] = edge.Item1;
                endpoints[1] = edge.Item2;

                foreach (int endpoint in endpoints)
                    foreach (int neighbour in byEndpoint[endpoint])
                        if (!visited[neighbour])
                        {
                            visited[neighbour] = true;
                            stack.Push(neighbour);
                        }
            }

            runs.Add(run);
        }

        return runs;

        static void Attach(Dictionary<int, List<int>> map, int endpoint, int edgeIndex)
        {
            if (!map.TryGetValue(endpoint, out var list)) map[endpoint] = list = new List<int>(4);
            list.Add(edgeIndex);
        }
    }

    // ---------------------------------------------------------------- pass 3: bridge

    /// <summary>
    /// Joins loose ends of the ridge network back to the rest of it across short gaps, adding the
    /// bridging mesh edges to <paramref name="ridgeEdges"/> in place.
    ///
    /// <para>
    /// A loose end is a vertex where exactly one ridge edge terminates - the point at which a rim
    /// faded below threshold. The bridge is the cheapest walk along mesh edges from there back to
    /// any other part of the ridge, with flat edges costing more than creased ones so the route
    /// follows the softened fold rather than cutting across open surface. Vertices within a few
    /// steps along the ridge are excluded as targets, or every loose end would simply reconnect to
    /// the edge it already belongs to.
    /// </para>
    /// </summary>
    private static void BridgeBreaks(
        Surface surface, HashSet<(int, int)> ridgeEdges, RidgeDetectionOptions options,
        RidgeDiagnostics? diag = null)
    {
        float maxGap = options.MaxGapFraction * surface.Diagonal;
        if (maxGap <= 0f)
        {
            diag?.BridgingSkipped("bridging disabled (MaxGapFraction is zero)", maxGap, ridgeEdges.Count, 0);
            return;
        }

        var ridgeNeighbours = new Dictionary<int, List<int>>();
        foreach (var edge in ridgeEdges)
        {
            Attach(ridgeNeighbours, edge.Item1, edge.Item2);
            Attach(ridgeNeighbours, edge.Item2, edge.Item1);
        }

        var looseEnds = ridgeNeighbours.Where(v => v.Value.Count == 1).Select(v => v.Key).ToList();
        if (looseEnds.Count == 0)
        {
            diag?.BridgingSkipped("no loose ends to bridge from", maxGap, ridgeEdges.Count, 0);
            return;
        }

        diag?.BridgingStart(maxGap, ridgeEdges.Count, looseEnds.Count);

        var ridgeVertices = new HashSet<int>(ridgeNeighbours.Keys);

        // Cost can never run ahead of distance by more than the penalty on a wholly flat route, so
        // once the cheapest thing left costs this much nothing within reach is left to find.
        float costCeiling = maxGap * (1f + options.GapFlatPenalty);

        // Reused across loose ends rather than reallocated per search: the searches are small and
        // local, so the clearing costs far less than the allocation would.
        var cheapest = new Dictionary<int, float>();
        var reach = new Dictionary<int, float>();
        var previous = new Dictionary<int, int>();
        var tooClose = new HashSet<int>();
        var queue = new PriorityQueue<int, float>();

        foreach (int start in looseEnds)
        {
            MarkTooClose(ridgeNeighbours, start, options.GapRidgeHops, tooClose);

            cheapest.Clear();
            reach.Clear();
            previous.Clear();
            queue.Clear();
            cheapest[start] = 0f;
            reach[start] = 0f;
            queue.Enqueue(start, 0f);

            int reached = -1;
            while (queue.TryDequeue(out int current, out float cost))
            {
                if (cost > cheapest.GetValueOrDefault(current, float.MaxValue) + 1e-6f) continue;
                if (cost > costCeiling) break;
                if (ridgeVertices.Contains(current) && !tooClose.Contains(current))
                {
                    reached = current;
                    break;
                }

                if (!surface.VertexNeighbours.TryGetValue(current, out var neighbours)) continue;
                foreach (int next in neighbours)
                {
                    var edge = current < next ? (current, next) : (next, current);
                    float length = surface.EdgeLength(edge);

                    // Past the budget in plain distance, so this is no longer a break being closed.
                    float span = reach[current] + length;
                    if (span > maxGap) continue;

                    float sharpness = surface.Folds.TryGetValue(edge, out var fold)
                        ? MathF.Max(0f, fold.Curvature)
                        : 0f;
                    float total = cost + (length * (1f + (options.GapFlatPenalty / (1f + sharpness))));
                    if (total >= cheapest.GetValueOrDefault(next, float.MaxValue)) continue;

                    cheapest[next] = total;
                    reach[next] = span;
                    previous[next] = current;
                    queue.Enqueue(next, total);
                }
            }

            if (reached < 0) continue;

            int added = 0;
            float bridgeLength = 0f;
            for (int at = reached; previous.TryGetValue(at, out int from); at = from)
            {
                var edge = at < from ? (at, from) : (from, at);
                if (ridgeEdges.Add(edge)) added++;
                bridgeLength += surface.EdgeLength(edge);
            }
            diag?.Bridge(added, bridgeLength);
        }

        if (diag is not null)
        {
            var degree = new Dictionary<int, int>();
            foreach (var edge in ridgeEdges)
            {
                degree[edge.Item1] = degree.GetValueOrDefault(edge.Item1) + 1;
                degree[edge.Item2] = degree.GetValueOrDefault(edge.Item2) + 1;
            }
            diag.BridgingDone(ridgeEdges.Count, degree.Count(d => d.Value == 1));
        }

        static void Attach(Dictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>(2);
            list.Add(value);
        }
    }

    /// <summary>Collects the vertices within <paramref name="hops"/> steps along the ridge network.</summary>
    private static void MarkTooClose(
        Dictionary<int, List<int>> ridgeNeighbours, int start, int hops, HashSet<int> tooClose)
    {
        tooClose.Clear();
        tooClose.Add(start);

        var frontier = new List<int> { start };
        var next = new List<int>();
        for (int hop = 0; hop < hops; hop++)
        {
            next.Clear();
            foreach (int vertex in frontier)
                if (ridgeNeighbours.TryGetValue(vertex, out var neighbours))
                    foreach (int neighbour in neighbours)
                        if (tooClose.Add(neighbour)) next.Add(neighbour);

            if (next.Count == 0) break;
            (frontier, next) = (next, frontier);
        }
    }

    // ---------------------------------------------------------------- pass 4: fill

    /// <summary>
    /// Floods the faces with the ridges as walls, then marks the regions narrow and small enough to
    /// be a band rather than one of the surfaces those bands divide.
    /// </summary>
    private static Territories FillEnclosedRegions(
        Surface surface, HashSet<(int, int)> ridgeEdges, RidgeDetectionOptions options, bool[] ridgeFaces,
        RidgeDiagnostics? diag = null)
    {
        int faceCount = surface.FaceCount;
        var region = new int[faceCount];
        Array.Fill(region, -1);

        var stack = new Stack<int>();
        int regionCount = 0;
        for (int seed = 0; seed < faceCount; seed++)
        {
            if (region[seed] >= 0) continue;

            region[seed] = regionCount;
            stack.Push(seed);
            while (stack.Count > 0)
            {
                int face = stack.Pop();
                for (int e = 0; e < 3; e++)
                {
                    var edge = surface.FaceEdge(face, e);
                    if (ridgeEdges.Contains(edge)) continue; // a ridge is a wall

                    var (first, second) = surface.Edges[edge];
                    int across = first == face ? second : first;
                    if (across < 0 || region[across] >= 0) continue;

                    region[across] = regionCount;
                    stack.Push(across);
                }
            }
            regionCount++;
        }

        var area = new float[regionCount];
        var perimeter = new float[regionCount];
        for (int face = 0; face < faceCount; face++) area[region[face]] += surface.FaceArea[face];
        foreach (var edge in ridgeEdges)
        {
            var (first, second) = surface.Edges[edge];
            float length = surface.EdgeLength(edge);
            perimeter[region[first]] += length;
            // A ridge with the same region on both sides does not enclose it, so its length counts
            // once rather than twice - otherwise a crease running into a region would read as
            // boundary and make the region look half as wide as it is.
            if (second >= 0 && region[second] != region[first]) perimeter[region[second]] += length;
        }

        float maxArea = options.MaxRegionAreaFraction * surface.TotalArea;
        float maxWidth = options.MaxRegionWidthFraction * surface.Diagonal;

        var fill = new bool[regionCount];
        for (int r = 0; r < regionCount; r++)
        {
            if (area[r] >= maxArea) continue;
            // No ridge on the boundary at all means nothing enclosed it; it is the whole surface.
            if (perimeter[r] < 1e-6f) continue;
            fill[r] = 2f * area[r] / perimeter[r] < maxWidth;
        }

        for (int face = 0; face < faceCount; face++)
            if (fill[region[face]]) ridgeFaces[face] = true;

        var holes = diag is null ? null : new List<RidgeHoleReport>();
        int closed = CloseBandHoles(
            surface, ridgeEdges, options, ridgeFaces, region, area, perimeter, fill, regionCount,
            holes, out float bandWidth, out float holeLimit);

        CompleteBand(surface, ridgeEdges, options, ridgeFaces, region, area, regionCount);

        var territories = Classify(surface, ridgeEdges, region, area, regionCount);

        if (diag is not null)
            ReportFill(surface, options, diag, region, area, perimeter, fill, regionCount, territories,
                closed, bandWidth, holeLimit, holes!);
        return territories;
    }

    /// <summary>
    /// Gives the band back the wall it lost where one of its boundaries rode up over surface that is
    /// still there, by growing it across the shortfall until it is as wide as the band beside it.
    ///
    /// <para>
    /// Done by moving faces into the band's region and adding the new outer edges to the ridge, which
    /// is the same handover <see cref="CloseBandHoles"/> relies on: the crease the boundary used to run
    /// along ends up with band on both sides, so <see cref="Territories.Divides"/> stops drawing it,
    /// while the edges at the new boundary have band on one side and a shell surface on the other and
    /// start being drawn. Nothing downstream has to know a repair happened.
    /// </para>
    ///
    /// <para>
    /// It grows only into the two shell surfaces and only on the side that collapsed - the side whose
    /// distance across the band is the short one - so a band bounded correctly on both sides is never
    /// touched even where it is narrow. On a body whose band is everywhere within half the local width,
    /// which is all five of the simple ones, nothing is marked and this returns without doing anything.
    /// </para>
    /// </summary>
    /// <returns>How many faces were given back to the band.</returns>
    private static int CompleteBand(
        Surface surface, HashSet<(int, int)> ridgeEdges, RidgeDetectionOptions options,
        bool[] filled, int[] region, float[] area, int regionCount)
    {
        if (options.BandShortfallFraction <= 0f || regionCount < 2) return 0;

        // Kept so the whole repair can be put back if it does not finish. See the convergence test
        // below for why abandoning it wholesale is the right answer rather than keeping what it did.
        var filledBefore = (bool[])filled.Clone();
        var regionBefore = (int[])region.Clone();
        var areaBefore = (float[])area.Clone();
        var edgesBefore = new HashSet<(int, int)>(ridgeEdges);

        var repaired = new List<int>();
        for (int pass = 0; pass < BandRepairPasses; pass++)
        {
            var grown = CompleteBandPass(
                surface, ridgeEdges, options, filled, region, area, regionCount);
            if (grown.Count == 0) break;

            repaired.AddRange(grown);
        }

        if (repaired.Count == 0) return 0;

        // Once, at the end, rather than after each pass. Adopting a face moves the band's edge out to
        // the far side of it, and the ring beyond then becomes the one touching the crease - so a
        // re-lay per pass walks the boundary outward once per iteration and carries the band off the
        // seam a ring at a time. The growth has to iterate, because each pass measures its deficit
        // against a truer band than the last; the boundary does not, because there is only ever one
        // band edge to lay it along.
        TwoLargest(area, regionCount, out int first, out int second);
        var band = MarkFaces(surface, ridgeEdges, filled);
        Rebound(
            surface, ridgeEdges, band, region, area, Zone(surface, band, repaired), first, second);

        if (Converged(surface, region, band, options, first, second)) return repaired.Count;

        // It did not settle, so it is not repairing what it was built to repair. A rim that is a knife
        // edge - where the shell tapers until its two creases meet - has no wall between them to give
        // back, but it does have a band that reads as narrow against the wider band either side of it,
        // so the shortfall test fires and each pass widens it further without ever satisfying the test.
        // Left half done that inflates the rim well past the wall it is supposed to be measuring.
        //
        // A repair that finishes has answered the question it asked; one that has not is working on a
        // body of a shape it does not model, and the honest result there is the band the detector found
        // rather than a partly grown one.
        Array.Copy(filledBefore, filled, filled.Length);
        Array.Copy(regionBefore, region, region.Length);
        Array.Copy(areaBefore, area, area.Length);
        ridgeEdges.Clear();
        foreach (var edge in edgesBefore) ridgeEdges.Add(edge);

        return 0;
    }

    /// <summary>Whether the band has any shortfall left in it.</summary>
    private static bool Converged(
        Surface surface, int[] region, bool[] band, RidgeDetectionOptions options, int first, int second)
    {
        var profile = MeasureBand(
            surface,
            new Territories(region, first, second, Array.Empty<bool>(), Array.Empty<int>()),
            band, options.BandShortfallFraction);

        foreach (bool suspect in profile.Suspect)
            if (suspect) return false;

        return true;
    }

    /// <summary>The two largest regions by area - the outer and inner faces of the shell.</summary>
    private static void TwoLargest(float[] area, int regionCount, out int first, out int second)
    {
        first = 0;
        second = -1;
        for (int r = 1; r < regionCount; r++)
            if (area[r] > area[first]) first = r;
        for (int r = 0; r < regionCount; r++)
            if (r != first && (second < 0 || area[r] > area[second])) second = r;
    }

    /// <summary>
    /// How many times the repair may re-measure and grow again.
    ///
    /// <para>
    /// One pass does not finish the job, because the shortfall it works from is measured against a band
    /// that is itself still short: where a stretch has collapsed, the local median it is compared to is
    /// dragged down by the collapse at its own edges, so the deficit comes out under the true one and
    /// the boundary lands part of the way out. Re-measuring against the widened band gives a truer
    /// figure each time. It converges in two or three - the correction shrinks with the remaining
    /// error - and the cap is there so a body that will not settle costs a bounded amount rather than
    /// looping.
    /// </para>
    /// </summary>
    private const int BandRepairPasses = 4;

    /// <summary>One measure-and-grow. Returns the faces it gave back.</summary>
    private static List<int> CompleteBandPass(
        Surface surface, HashSet<(int, int)> ridgeEdges, RidgeDetectionOptions options,
        bool[] filled, int[] region, float[] area, int regionCount)
    {
        var none = new List<int>();
        TwoLargest(area, regionCount, out int first, out int second);
        if (second < 0) return none;

        // The band as the answer would show it - filled surface plus the facets the creases touch -
        // because a rim that is a knife edge has no filled band and its width must still be measured.
        var band = MarkFaces(surface, ridgeEdges, filled);
        var profile = MeasureBand(
            surface,
            new Territories(region, first, second, Array.Empty<bool>(), Array.Empty<int>()),
            band, options.BandShortfallFraction);
        if (profile.Width.Length == 0) return none;

        int faceCount = surface.FaceCount;
        var centroid = Centroids(surface);

        var distance = new float[faceCount];
        var budget = new float[faceCount];
        var inherit = new int[faceCount];
        Array.Fill(distance, float.PositiveInfinity);
        Array.Fill(inherit, -1);

        var queue = new PriorityQueue<int, float>();

        for (int f = 0; f < faceCount; f++)
        {
            if (!profile.Suspect[f]) continue;

            float missing = profile.Expected[f] - profile.Width[f];
            if (missing <= 0f) continue;

            // The side that rode in is the side the band barely reaches, so that is the one to grow.
            int collapsed = profile.ToFirst[f] < profile.ToSecond[f] ? first : second;
            int home = BandRegion(surface, region, band, f, first, second);
            if (home < 0) continue;

            for (int e = 0; e < 3; e++)
            {
                var edge = surface.FaceEdge(f, e);
                var (left, right) = surface.Edges[edge];
                int across = left == f ? right : left;
                if (across < 0 || band[across] || region[across] != collapsed) continue;

                float step = Vector3.Distance(centroid[f], MidPoint(surface, edge));
                if (step >= distance[across]) continue;

                distance[across] = step;
                budget[across] = missing;
                inherit[across] = home;
                queue.Enqueue(across, step);
            }
        }

        // A face can be band by touching a crease while still belonging to a shell surface, and a
        // repaired stretch runs through plenty of those. Left on their surface, the boundary between
        // them and the faces given back reads as band against surface and goes on being drawn - the old
        // boundary and the new one both, which is worse than the pocket was. They move with the rest.
        var grown = new List<int>();
        while (queue.TryDequeue(out int current, out float cost))
        {
            if (cost > distance[current] + 1e-6f) continue;

            grown.Add(current);

            for (int e = 0; e < 3; e++)
            {
                var edge = surface.FaceEdge(current, e);
                var (left, right) = surface.Edges[edge];
                int across = left == current ? right : left;
                if (across < 0 || band[across] || region[across] != region[current]) continue;

                float step = cost + Vector3.Distance(centroid[current], centroid[across]);
                if (step > budget[current] || step >= distance[across]) continue;

                distance[across] = step;
                budget[across] = budget[current];
                inherit[across] = inherit[current];
                queue.Enqueue(across, step);
            }
        }

        // Applied only once the walk is finished. Moving a face into the band mid-walk would wall the
        // walk off from the rest of the surface it is still crossing.
        foreach (int face in grown)
        {
            area[region[face]] -= surface.FaceArea[face];
            area[inherit[face]] += surface.FaceArea[face];
            region[face] = inherit[face];
            filled[face] = true;
            band[face] = true;
        }

        return grown;
    }

    /// <summary>
    /// The whole rim a repair landed on: the band faces reachable from it across the band.
    ///
    /// <para>
    /// A few faces either side of the repair is not enough, and the reason is that the boundary has two
    /// definitions that only agree where they have both been tidied. Stop tidying partway along the rim
    /// and the far side of the cut still has the untidied one, so the curve ends there - which is a dead
    /// end in a new place rather than the one that was fixed. Taking the rim entire leaves no such cut:
    /// the boundary of a face set on a closed surface is closed curves, so this is what makes closure
    /// something the repair cannot break rather than something it has to be careful about.
    /// </para>
    /// </summary>
    private static HashSet<int> Zone(Surface surface, bool[] band, List<int> grown)
    {
        var zone = new HashSet<int>();
        var stack = new Stack<int>();

        foreach (int seed in grown)
            if (zone.Add(seed)) stack.Push(seed);

        while (stack.Count > 0)
        {
            int face = stack.Pop();
            for (int e = 0; e < 3; e++)
            {
                var (left, right) = surface.Edges[surface.FaceEdge(face, e)];
                int across = left == face ? right : left;
                if (across < 0 || !band[across] || !zone.Add(across)) continue;

                stack.Push(across);
            }
        }

        return zone;
    }

    /// <summary>
    /// Lays the crease back along the band's edge across a repair, so the new boundary and the crease
    /// that survived either side of it come back as one curve.
    ///
    /// <para>
    /// Two things have to be true at once for a boundary edge to be drawn, and patching the repair's
    /// own faces only ever gets one of them. The edge has to be in the ridge, and the band side of it
    /// has to be on a band region - but a face can be band by touching a crease while still belonging
    /// to a shell surface, and the old boundary is lined with those. Wherever one of them sat at the
    /// join, the new curve stopped an edge short of the old one and left a dead end, which is what
    /// broke the rim into eleven pieces rather than any junction doing it.
    /// </para>
    ///
    /// <para>
    /// The two are settled together but not over the same extent, and that distinction is the whole of
    /// it. Adopting a face moves the band's edge outward where it sits, so doing it along the whole rim
    /// walks the boundary out everywhere and carries the band off the seam it should straddle - which
    /// is what shifted one contour four and a half millimetres while the other stayed put. Adoption is
    /// therefore kept to the faces the repair actually touched. Laying the crease back along the band's
    /// edge moves nothing, so that runs over the whole rim, which is what closure needs.
    /// </para>
    ///
    /// <para>
    /// Adopting can expose another face to adopt, hence the repeat; it converges in a pass or two
    /// because the neighbourhood is finite.
    /// </para>
    /// </summary>
    /// <param name="zone">The whole rim, where the crease is re-laid along the band's edge.</param>
    /// <param name="repair">The faces the repair touched, the only ones that change hands.</param>
    private static void Rebound(
        Surface surface, HashSet<(int, int)> ridgeEdges, bool[] band, int[] region, float[] area,
        HashSet<int> zone, int first, int second)
    {
        for (int pass = 0; pass < 8; pass++)
        {
            bool moved = false;
            foreach (int face in zone)
            {
                if (!band[face] || (region[face] != first && region[face] != second)) continue;

                int home = BandRegion(surface, region, band, face, first, second);
                if (home < 0) continue;

                area[region[face]] -= surface.FaceArea[face];
                area[home] += surface.FaceArea[face];
                region[face] = home;
                moved = true;
            }

            if (!moved) break;
        }

        foreach (int face in zone)
        {
            if (!band[face]) continue;

            for (int e = 0; e < 3; e++)
            {
                var edge = surface.FaceEdge(face, e);
                var (left, right) = surface.Edges[edge];
                int across = left == face ? right : left;
                if (across >= 0 && !band[across]) ridgeEdges.Add(edge);
            }
        }
    }

    /// <summary>
    /// The band region a suspect face should hand its new surface to: its own, or a neighbour's where
    /// the face is on the band only by touching a crease and so still belongs to a shell surface.
    /// </summary>
    private static int BandRegion(
        Surface surface, int[] region, bool[] band, int face, int first, int second)
    {
        if (region[face] != first && region[face] != second) return region[face];

        for (int e = 0; e < 3; e++)
        {
            var edge = surface.FaceEdge(face, e);
            var (left, right) = surface.Edges[edge];
            int across = left == face ? right : left;
            if (across < 0 || !band[across]) continue;
            if (region[across] != first && region[across] != second) return region[across];
        }

        return -1;
    }

    private static Vector3[] Centroids(Surface surface)
    {
        var centroid = new Vector3[surface.FaceCount];
        for (int f = 0; f < surface.FaceCount; f++)
            centroid[f] = (surface.Positions[surface.Triangles[f * 3]]
                + surface.Positions[surface.Triangles[(f * 3) + 1]]
                + surface.Positions[surface.Triangles[(f * 3) + 2]]) / 3f;
        return centroid;
    }

    /// <summary>
    /// Measures the band's width at every face of it, and marks the faces where that width falls well
    /// below what the band is doing on either side.
    /// </summary>
    /// <param name="shortfall">
    /// How far below the local expectation a face has to fall to be suspect, as a fraction of it.
    /// </param>
    /// <param name="neighbourhood">
    /// How far along the band the local expectation is gathered from, in multiples of the band's own
    /// median width. Wide enough that a genuine taper is inside the window and so moves the
    /// expectation with it; narrow enough that a collapse is not.
    /// </param>
    private static BandProfile MeasureBand(
        Surface surface, Territories territories, bool[] band,
        float shortfall = 0.5f, float neighbourhood = 4f)
    {
        if (territories.First < 0) return BandProfile.None;

        int faceCount = surface.FaceCount;
        var toFirst = SpreadAcrossBand(surface, territories, band, territories.First);
        var toSecond = SpreadAcrossBand(surface, territories, band, territories.Second);

        var width = new float[faceCount];
        var measured = new List<float>();
        for (int f = 0; f < faceCount; f++)
        {
            if (!band[f] || float.IsPositiveInfinity(toFirst[f]) || float.IsPositiveInfinity(toSecond[f]))
            {
                width[f] = float.PositiveInfinity;
                continue;
            }

            width[f] = toFirst[f] + toSecond[f];
            measured.Add(width[f]);
        }

        if (measured.Count == 0) return BandProfile.None;

        measured.Sort();
        float median = measured[measured.Count / 2];

        // The local expectation: the median width of the band within a few widths along it. Median
        // rather than mean so the collapse being looked for cannot drag its own reference down.
        float radius = neighbourhood * median;
        var expected = new float[faceCount];
        var suspect = new bool[faceCount];
        var nearby = new List<float>();
        var visited = new HashSet<int>();
        var frontier = new List<int>();
        var next = new List<int>();

        for (int f = 0; f < faceCount; f++)
        {
            expected[f] = float.PositiveInfinity;
            if (float.IsPositiveInfinity(width[f])) continue;

            nearby.Clear();
            visited.Clear();
            frontier.Clear();
            visited.Add(f);
            frontier.Add(f);
            nearby.Add(width[f]);

            float walked = 0f;
            while (walked < radius && frontier.Count > 0)
            {
                next.Clear();
                foreach (int face in frontier)
                    for (int e = 0; e < 3; e++)
                    {
                        var edge = surface.FaceEdge(face, e);
                        var (first, second) = surface.Edges[edge];
                        int across = first == face ? second : first;
                        if (across < 0 || float.IsPositiveInfinity(width[across])) continue;
                        if (!visited.Add(across)) continue;

                        next.Add(across);
                        nearby.Add(width[across]);
                    }

                walked += surface.MeanEdgeLength;
                (frontier, next) = (next, frontier);
            }

            nearby.Sort();
            expected[f] = nearby[nearby.Count / 2];
            suspect[f] = width[f] < shortfall * expected[f];
        }

        return new BandProfile(width, expected, suspect, median, toFirst, toSecond);
    }

    /// <summary>
    /// Distance from every band face to the nearest band face touching <paramref name="territory"/>,
    /// walking only across the band. Dijkstra over face centroids rather than a hop count, so the
    /// answer is a length in millimetres and does not change with how finely the band is tessellated.
    /// </summary>
    private static float[] SpreadAcrossBand(
        Surface surface, Territories territories, bool[] band, int territory)
    {
        int faceCount = surface.FaceCount;
        var distance = new float[faceCount];
        Array.Fill(distance, float.PositiveInfinity);

        var queue = new PriorityQueue<int, float>();
        var centroid = new Vector3[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            var a = surface.Positions[surface.Triangles[f * 3]];
            var b = surface.Positions[surface.Triangles[(f * 3) + 1]];
            var c = surface.Positions[surface.Triangles[(f * 3) + 2]];
            centroid[f] = (a + b + c) / 3f;
        }

        // Seeded at the band faces that border the territory, at half their own size rather than at
        // zero: the boundary is at the face's edge, not at the point the distance is measured from.
        for (int f = 0; f < faceCount; f++)
        {
            if (!band[f]) continue;

            for (int e = 0; e < 3; e++)
            {
                var edge = surface.FaceEdge(f, e);
                var (first, second) = surface.Edges[edge];
                int across = first == f ? second : first;
                if (across < 0 || band[across] || territories.Region[across] != territory) continue;

                float seed = Vector3.Distance(centroid[f], MidPoint(surface, edge));
                if (seed >= distance[f]) continue;
                distance[f] = seed;
            }

            if (!float.IsPositiveInfinity(distance[f])) queue.Enqueue(f, distance[f]);
        }

        while (queue.TryDequeue(out int current, out float cost))
        {
            if (cost > distance[current] + 1e-6f) continue;

            for (int e = 0; e < 3; e++)
            {
                var edge = surface.FaceEdge(current, e);
                var (first, second) = surface.Edges[edge];
                int across = first == current ? second : first;
                if (across < 0 || !band[across]) continue;

                float step = cost + Vector3.Distance(centroid[current], centroid[across]);
                if (step >= distance[across]) continue;

                distance[across] = step;
                queue.Enqueue(across, step);
            }
        }

        return distance;
    }

    private static Vector3 MidPoint(Surface surface, (int, int) edge) =>
        (surface.Positions[edge.Item1] + surface.Positions[edge.Item2]) * 0.5f;

    /// <summary>
    /// Closes pockets punched through the rim band, by giving their faces to the band around them.
    ///
    /// <para>
    /// Done at the region level rather than by patching the face mask, so that everything downstream
    /// follows on its own: a crease that only ever bounded the pocket now has band on both sides,
    /// which is exactly the test <see cref="Territories.Divides"/> already applies to decide a crease
    /// is inside the wall rather than bounding it. Patching the mask alone would leave the band shaded
    /// solid with the contour still detouring through the middle of it.
    /// </para>
    /// </summary>
    /// <returns>How many pockets were closed.</returns>
    private static int CloseBandHoles(
        Surface surface, HashSet<(int, int)> ridgeEdges, RidgeDetectionOptions options, bool[] band,
        int[] region, float[] area, float[] perimeter, bool[] fill, int regionCount,
        List<RidgeHoleReport>? holes, out float bandWidth, out float holeLimit)
    {
        bandWidth = 0f;
        holeLimit = 0f;
        if (options.MaxBandHoleWidthFraction <= 0f) return 0;

        // The band's width, taken over the band as one object rather than per region.
        //
        // Averaging each region's own twice-area-over-perimeter looks equivalent and is not: bridging
        // chops the band into short strips, and for a strip whose length is not far greater than its
        // width the ends make up much of the perimeter, so the measure comes out well under the true
        // width. On this set it read 3.9mm against a real 11mm. Measured across the whole band the
        // ends cancel - a band of length L and width w has area Lw and two long sides of length L, so
        // twice the area over that perimeter is w exactly.
        float bandArea = 0f;
        for (int r = 0; r < regionCount; r++)
            if (fill[r]) bandArea += area[r];
        if (bandArea < 1e-6f) return 0;

        float bandSides = 0f;
        foreach (var edge in ridgeEdges)
        {
            var (first, second) = surface.Edges[edge];
            if (second < 0) continue;

            bool leftFilled = fill[region[first]];
            bool rightFilled = fill[region[second]];
            if (leftFilled != rightFilled) bandSides += surface.EdgeLength(edge);
        }
        if (bandSides < 1e-6f) return 0;

        bandWidth = 2f * bandArea / bandSides;
        float maxWidth = options.MaxBandHoleWidthFraction * bandWidth;
        holeLimit = maxWidth;

        // Area as well as width, because twice-the-area-over-the-perimeter only measures a width on
        // something strip-shaped. A large region with a ragged boundary has perimeter out of all
        // proportion to its area and reads narrow: a puck's cap, lace-edged where the fillet creases
        // reach into it, came out at 14mm across when it is 300mm wide, and closing swallowed the
        // whole cap. Requiring the pocket to fit inside a square of one band width as well is what
        // makes the pair of tests describe a blemish rather than either one alone.
        float maxArea = maxWidth * maxWidth;

        int faceCount = surface.FaceCount;

        // The wall the pocket is walled in by is not the filled band alone. A rim that is a knife edge
        // encloses nothing, so its facets are marked by touching a crease rather than by being filled,
        // and a pocket ringed by those is ringed by faces the fill never claimed. Flooding against the
        // filled set alone walks straight out through that ring and off across the whole surface,
        // which is why every pocket looked open.
        var barrier = new bool[faceCount];
        Array.Copy(band, barrier, faceCount);
        foreach (var edge in ridgeEdges)
        {
            var (first, second) = surface.Edges[edge];
            barrier[first] = true;
            if (second >= 0) barrier[second] = true;
        }

        var visited = new bool[faceCount];
        var component = new List<int>();
        var stack = new Stack<int>();
        int closed = 0;

        for (int seed = 0; seed < faceCount; seed++)
        {
            if (visited[seed] || barrier[seed]) continue;

            component.Clear();
            visited[seed] = true;
            stack.Push(seed);

            bool enclosed = true;
            float holeArea = 0f, holePerimeter = 0f;
            int touching = -1;

            while (stack.Count > 0)
            {
                int face = stack.Pop();
                component.Add(face);
                holeArea += surface.FaceArea[face];

                for (int e = 0; e < 3; e++)
                {
                    var edge = surface.FaceEdge(face, e);
                    var (first, second) = surface.Edges[edge];
                    int across = first == face ? second : first;

                    // A boundary edge has nothing on the far side, so the pocket is open to it and is
                    // not a pocket at all.
                    if (across < 0)
                    {
                        enclosed = false;
                        continue;
                    }

                    if (barrier[across])
                    {
                        holePerimeter += surface.EdgeLength(edge);
                        // Prefer a genuinely filled neighbour to inherit from: giving the pocket a
                        // band region is what makes the creases round it read as inside the wall.
                        if (touching < 0 || (!fill[touching] && fill[region[across]]))
                            touching = region[across];
                        continue;
                    }

                    if (visited[across]) continue;
                    visited[across] = true;
                    stack.Push(across);
                }
            }

            float holeWidth = holePerimeter > 1e-6f ? 2f * holeArea / holePerimeter : float.PositiveInfinity;
            bool ok = enclosed && touching >= 0 && holePerimeter > 1e-6f
                && holeWidth < maxWidth && holeArea < maxArea;

            string verdict =
                !enclosed ? "open to a boundary edge"
                : touching < 0 ? "nothing beside it to inherit from"
                : holePerimeter < 1e-6f ? "no perimeter"
                : holeWidth >= maxWidth ? "wider than the band"
                : holeArea >= maxArea ? "larger than the band is wide"
                : "closed";

            // Everything the walk found gets recorded, not only what was closed. A pocket left open is
            // the case worth reading, and only its own numbers say whether the limit is wrong or the
            // pocket is not the shape this closing assumes.
            holes?.Add(new RidgeHoleReport(
                component.Count, holeArea, holePerimeter, holeWidth, enclosed, ok, verdict));

            if (!ok) continue;

            foreach (int face in component)
            {
                band[face] = true;
                region[face] = touching;
            }
            closed++;
        }

        return closed;
    }

    /// <summary>
    /// Names the two largest regions as the surfaces the ridge divides, and marks the regions that
    /// span between them as band.
    ///
    /// <para>
    /// Largest by area rather than by any test of shape: on a shell the outer and inner surfaces are
    /// each 35-50% of the area while the widest band is a few per cent, so the gap between them is
    /// enormous and nothing in between needs deciding.
    /// </para>
    ///
    /// <para>
    /// Band membership is decided per connected group rather than per region, because a rim wall is
    /// hardly ever one region. A filleted corner divides it into a strip per fillet step, and on a
    /// real body the bridges laid across a break during pass three chop it into hundreds of them.
    /// Only the outermost of those strips touches a surface, so asking each region individually
    /// whether it borders both surfaces marks none of them and discards the entire rim. What makes a
    /// band a band is that the group it belongs to reaches from one surface to the other.
    /// </para>
    /// </summary>
    private static Territories Classify(
        Surface surface, HashSet<(int, int)> ridgeEdges, int[] region, float[] area, int regionCount)
    {
        if (regionCount < 2) return Territories.None;

        int first = 0, second = -1;
        for (int r = 1; r < regionCount; r++)
            if (area[r] > area[first]) first = r;
        for (int r = 0; r < regionCount; r++)
            if (r != first && (second < 0 || area[r] > area[second])) second = r;

        // Region adjacency across the ridge, plus which regions border each of the two surfaces.
        var neighbours = new Dictionary<int, HashSet<int>>();
        var touchesFirst = new bool[regionCount];
        var touchesSecond = new bool[regionCount];

        foreach (var edge in ridgeEdges)
        {
            var (a, b) = surface.Edges[edge];
            if (b < 0) continue;

            int left = region[a];
            int right = region[b];
            if (left == right) continue;

            if (left == first) touchesFirst[right] = true;
            if (left == second) touchesSecond[right] = true;
            if (right == first) touchesFirst[left] = true;
            if (right == second) touchesSecond[left] = true;

            if (left == first || left == second || right == first || right == second) continue;

            Link(neighbours, left, right);
            Link(neighbours, right, left);
        }

        // Group the non-surface regions, then keep the groups that reach from one surface to the other.
        var isBand = new bool[regionCount];
        var group = new int[regionCount];
        Array.Fill(group, -1);
        var stack = new Stack<int>();
        var members = new List<int>();

        for (int seed = 0; seed < regionCount; seed++)
        {
            if (seed == first || seed == second || group[seed] >= 0) continue;

            members.Clear();
            group[seed] = seed;
            stack.Push(seed);

            bool reachesFirst = false;
            bool reachesSecond = false;

            while (stack.Count > 0)
            {
                int current = stack.Pop();
                members.Add(current);
                reachesFirst |= touchesFirst[current];
                reachesSecond |= touchesSecond[current];

                if (!neighbours.TryGetValue(current, out var adjacent)) continue;
                foreach (int next in adjacent)
                {
                    if (group[next] >= 0) continue;
                    group[next] = seed;
                    stack.Push(next);
                }
            }

            if (!reachesFirst || !reachesSecond) continue;
            foreach (int member in members) isBand[member] = true;
        }

        // Groups that turned out not to span the two surfaces are not bands, so their group id would
        // only be noise to anything reading it.
        for (int r = 0; r < regionCount; r++)
            if (!isBand[r]) group[r] = -1;

        return new Territories(region, first, second, isBand, group);

        static void Link(Dictionary<int, HashSet<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var set)) map[key] = set = new HashSet<int>(2);
            set.Add(value);
        }
    }

    private static void ReportFill(
        Surface surface, RidgeDetectionOptions options, RidgeDiagnostics diag,
        int[] region, float[] area, float[] perimeter, bool[] fill, int regionCount,
        Territories territories, int closedHoles, float bandWidth, float holeLimit,
        List<RidgeHoleReport> holes)
    {
        int faceCount = surface.FaceCount;

        var faceCounts = new int[regionCount];
        for (int face = 0; face < faceCount; face++) faceCounts[region[face]]++;

        var regions = new List<RidgeRegionReport>(regionCount);
        int filledRegions = 0, filledFaces = 0;
        float filledArea = 0f;
        for (int r = 0; r < regionCount; r++)
        {
            float width = perimeter[r] > 1e-6f ? 2f * area[r] / perimeter[r] : float.PositiveInfinity;
            regions.Add(new RidgeRegionReport(
                faceCounts[r], area[r],
                surface.TotalArea > 0f ? area[r] / surface.TotalArea : 0f,
                perimeter[r], width,
                surface.Diagonal > 0f ? width / surface.Diagonal : 0f,
                fill[r]));

            if (!fill[r]) continue;
            filledRegions++;
            filledFaces += faceCounts[r];
            filledArea += area[r];
        }

        diag.Fill(new RidgeFillReport(
            regionCount,
            regions.OrderByDescending(r => r.Area).Take(64).ToList(),
            options.MaxRegionAreaFraction, options.MaxRegionWidthFraction,
            filledRegions, filledFaces,
            surface.TotalArea > 0f ? filledArea / surface.TotalArea : 0f,
            // One group per rim is the healthy answer. Two rims sharing a group means their walls
            // touch, and a walk cannot then tell one rim from the other.
            territories.BandGroup.Where(g => g >= 0).Distinct().Count(),
            closedHoles, bandWidth, holeLimit,
            holes.OrderByDescending(h => h.Area).Take(20).ToList()));
    }

    // ---------------------------------------------------------------- pass 5: contour

    /// <summary>Taubin shrink factor, and the inflate factor that cancels its shrinkage.</summary>
    private const float ContourLambda = 0.55f;
    private const float ContourMu = -0.58f;

    /// <summary>
    /// Relaxation passes over each contour. Enough to erase the triangle-scale staircase the trace
    /// starts as; far short of enough to round off a corner the rim genuinely turns.
    /// </summary>
    private const int ContourPasses = 24;

    /// <summary>
    /// Contour point spacing, as a multiple of the mesh's mean edge length. Resampling to about one
    /// edge is what lets relaxation work at all: left on the original vertices, a staircase's steps
    /// are its own neighbours and the Laplacian of a step is the step, so it barely moves.
    /// </summary>
    private const float ContourSpacingInEdges = 1.0f;

    /// <summary>How far a contour is lifted off the surface, as a fraction of the bounding diagonal.</summary>
    private const float ContourLiftFraction = 0.002f;

    /// <summary>Shortest contour worth drawing, as a fraction of the bounding diagonal.</summary>
    private const float MinContourFraction = 0.15f;

    /// <summary>
    /// Turns the ridge into curves. The curve follows the crease itself, not the outline of the
    /// facets the crease touches: a facet outline is a triangle away from the feature on whichever
    /// side the tessellation happened to fall, which is exactly the dependence a curve exists to
    /// escape.
    ///
    /// <para>
    /// Every crease is drawn except those buried inside filled surface, where both sides are part of
    /// the same band. Those are the bridges and stray creases within a rim wall - already described
    /// by the two creases bounding the wall, and only clutter if drawn again.
    /// </para>
    /// </summary>
    private static IReadOnlyList<RidgeContour> TraceContours(
        Surface surface, HashSet<(int, int)> ridgeEdges, bool[] filled, Territories territories,
        RidgeDiagnostics? diag = null)
    {
        var creases = new List<(int, int)>();

        // Which rim each crease is on, so a chain can be attributed to one once it has been walked.
        var rimOf = new Dictionary<(int, int), int>();

        foreach (var edge in ridgeEdges)
        {
            var (first, second) = surface.Edges[edge];

            // A crease is worth drawing only where it divides the two surfaces the ridge separates -
            // directly, or through a band running between them. Surface relief fails this without any
            // threshold having to say how much relief is too much: a crease that does not close off an
            // area has the same region on both sides, so it divides nothing.
            if (second >= 0 && territories.First >= 0)
            {
                if (!territories.Divides(territories.Region[first], territories.Region[second])) continue;

                rimOf[edge] = territories
                    .Rim((territories.Region[first], territories.Region[second])).Group;
            }
            else if (filled[first] && second >= 0 && filled[second]) continue;

            creases.Add(edge);
        }

        float minLength = MinContourFraction * surface.Diagonal;
        float spacing = ContourSpacingInEdges * surface.MeanEdgeLength;
        float lift = ContourLiftFraction * surface.Diagonal;

        if (diag is not null)
        {
            // The crease graph's shape is what decides how many pieces a rim comes back in: a chain
            // ends at every vertex that is not a plain two-way continuation, so junctions and dead
            // ends are the direct cause of fragmentation.
            var degree = new Dictionary<int, int>();
            foreach (var edge in creases)
            {
                degree[edge.Item1] = degree.GetValueOrDefault(edge.Item1) + 1;
                degree[edge.Item2] = degree.GetValueOrDefault(edge.Item2) + 1;
            }

            diag.Trace(new RidgeTraceReport(
                ridgeEdges.Count, creases.Count, ridgeEdges.Count - creases.Count,
                degree.Count(d => d.Value >= 3), degree.Count(d => d.Value == 1),
                minLength, spacing, lift, 0, Array.Empty<RidgeChainReport>()));
        }

        if (creases.Count == 0) return Array.Empty<RidgeContour>();

        var normals = surface.VertexNormals();

        var contours = new List<RidgeContour>();
        foreach (var (chain, closed) in ChainCreases(surface, creases, territories))
        {
            float length = 0f;
            for (int i = 0; i < chain.Count - 1; i++)
                length += Vector3.Distance(surface.Positions[chain[i]], surface.Positions[chain[i + 1]]);
            if (closed)
                length += Vector3.Distance(surface.Positions[chain[^1]], surface.Positions[chain[0]]);
            if (length < minLength)
            {
                diag?.Chain(chain.Count, 0, length, closed, RidgeChainVerdict.TooShort);
                continue;
            }

            var points = new Vector3[chain.Count];
            var direction = new Vector3[chain.Count];
            for (int i = 0; i < chain.Count; i++)
            {
                points[i] = surface.Positions[chain[i]];
                direction[i] = normals[chain[i]];
            }

            var resampled = Resample(points, direction, spacing, closed, out var resampledNormals);
            var smoothed = Relax(resampled, resampledNormals, lift, closed);

            diag?.Chain(chain.Count, smoothed.Length, length, closed,
                smoothed.Length >= 2 ? RidgeChainVerdict.Kept : RidgeChainVerdict.Degenerate);

            if (smoothed.Length >= 2)
                contours.Add(new RidgeContour(smoothed, closed) { Rim = RimOf(chain, rimOf) });
        }

        return contours;
    }

    /// <summary>
    /// Which rim a walked chain is on: whichever its edges agree on, by majority.
    ///
    /// <para>
    /// A majority rather than the first edge, because where two rims meet a chain can start on one and
    /// carry on along the other, and a single edge at the join is exactly the least reliable place to
    /// ask. Where the chain is unambiguous - which is nearly all of them - every edge answers the same
    /// and the majority is that answer.
    /// </para>
    /// </summary>
    private static int RimOf(List<int> chain, Dictionary<(int, int), int> rimOf)
    {
        var votes = new Dictionary<int, int>();
        for (int i = 0; i < chain.Count; i++)
        {
            int a = chain[i];
            int b = chain[(i + 1) % chain.Count];
            var key = a < b ? (a, b) : (b, a);

            if (!rimOf.TryGetValue(key, out int rim) || rim < 0) continue;
            votes[rim] = votes.GetValueOrDefault(rim) + 1;
        }

        int best = -1, most = 0;
        foreach (var (rim, count) in votes)
        {
            if (count <= most) continue;

            most = count;
            best = rim;
        }

        return best;
    }

    /// <summary>
    /// Walks the crease edges into chains. Runs that start and finish at a junction or a loose end
    /// come back open; runs that close on themselves come back closed. Where more than two creases
    /// meet the walk takes the straightest continuation, so a chain follows the run it is on rather
    /// than turning off down a spur.
    /// </summary>
    private static List<(List<int> Chain, bool Closed)> ChainCreases(
        Surface surface, List<(int, int)> creases, Territories territories)
    {
        var incident = new Dictionary<int, List<int>>(creases.Count);
        for (int i = 0; i < creases.Count; i++)
        {
            Attach(incident, creases[i].Item1, i);
            Attach(incident, creases[i].Item2, i);
        }

        var used = new bool[creases.Count];
        var chains = new List<(List<int>, bool)>();

        // Runs through junctions and dead ends first. Tracing loops first would swallow the run a
        // junction sits on and leave its spurs as fragments.
        //
        // A run started here can still come back closed, and must be reported that way: where two rims
        // meet, every branch of the junction is on a rim that returns to it. Forcing these to open was
        // enough on its own to stop a body with two rims ever closing either of them.
        foreach (var (vertex, edges) in incident)
        {
            if (edges.Count == 2) continue;
            foreach (int start in edges)
                if (!used[start])
                    chains.Add((Walk(surface, incident, used, creases, territories, vertex, start, out bool ends), ends));
        }

        for (int seed = 0; seed < creases.Count; seed++)
        {
            if (used[seed]) continue;

            var chain = Walk(
                surface, incident, used, creases, territories, creases[seed].Item1, seed, out bool closed);
            chains.Add((chain, closed));
        }

        return chains.Where(c => c.Item1.Count >= 2).ToList();

        static void Attach(Dictionary<int, List<int>> map, int key, int value)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>(2);
            list.Add(value);
        }
    }

    /// <summary>
    /// Follows creases from <paramref name="from"/> along <paramref name="firstEdge"/> for as far as
    /// they continue, consuming them as it goes.
    /// </summary>
    private static List<int> Walk(
        Surface surface, Dictionary<int, List<int>> incident, bool[] used,
        List<(int, int)> creases, Territories territories, int from, int firstEdge, out bool closed)
    {
        used[firstEdge] = true;
        var edge = creases[firstEdge];
        int current = edge.Item1 == from ? edge.Item2 : edge.Item1;

        var chain = new List<int> { from, current };
        closed = false;

        while (current != from)
        {
            int step = NextCrease(surface, incident, used, creases, chain[^2], current);
            if (step < 0) break;


            used[step] = true;
            var next = creases[step];
            current = next.Item1 == current ? next.Item2 : next.Item1;

            if (current == from)
            {
                closed = true;
                break;
            }
            chain.Add(current);
        }

        return chain;
    }

    /// <summary>Picks the unused crease at <paramref name="at"/> that turns least.</summary>
    private static int NextCrease(
        Surface surface, Dictionary<int, List<int>> incident, bool[] used,
        List<(int, int)> creases, int from, int at)
    {
        if (!incident.TryGetValue(at, out var candidates)) return -1;

        var heading = surface.Positions[at] - surface.Positions[from];
        float headingLength = heading.Length();
        if (headingLength > 1e-9f) heading /= headingLength;

        int best = -1;
        float straightest = float.MinValue;
        foreach (int index in candidates)
        {
            if (used[index]) continue;

            var edge = creases[index];
            int other = edge.Item1 == at ? edge.Item2 : edge.Item1;

            var direction = surface.Positions[other] - surface.Positions[at];
            float length = direction.Length();
            if (length < 1e-9f) continue;

            float turn = Vector3.Dot(heading, direction / length);
            if (turn <= straightest) continue;

            straightest = turn;
            best = index;
        }

        return best;
    }

    /// <summary>
    /// Resamples a chain to an even <paramref name="spacing"/>, carrying the surface normal along
    /// with the position so the lift at the end still points away from the surface.
    /// </summary>
    private static Vector3[] Resample(
        Vector3[] points, Vector3[] normals, float spacing, bool closed, out Vector3[] resampledNormals)
    {
        int n = points.Length;
        int spans = closed ? n : n - 1;
        if (spans < 1)
        {
            resampledNormals = normals;
            return points;
        }

        var cumulative = new float[spans + 1];
        for (int i = 0; i < spans; i++)
            cumulative[i + 1] = cumulative[i] + Vector3.Distance(points[i], points[(i + 1) % n]);

        float total = cumulative[spans];
        if (total < 1e-6f)
        {
            resampledNormals = normals;
            return points;
        }

        int count = Math.Clamp((int)MathF.Round(total / MathF.Max(spacing, 1e-4f)), 8, 20000);
        // An open chain needs a sample at each end; a closed one must not repeat its start.
        int samples = closed ? count : count + 1;

        var outPoints = new Vector3[samples];
        resampledNormals = new Vector3[samples];

        int segment = 0;
        for (int k = 0; k < samples; k++)
        {
            float target = total * k / count;
            while (segment < spans - 1 && cumulative[segment + 1] < target) segment++;

            float span = cumulative[segment + 1] - cumulative[segment];
            float t = span > 1e-6f ? Math.Clamp((target - cumulative[segment]) / span, 0f, 1f) : 0f;
            int next = (segment + 1) % n;

            outPoints[k] = Vector3.Lerp(points[segment], points[next], t);
            var normal = Vector3.Lerp(normals[segment], normals[next], t);
            resampledNormals[k] = normal.LengthSquared() < 1e-12f ? Vector3.Zero : Vector3.Normalize(normal);
        }

        return outPoints;
    }

    /// <summary>
    /// Taubin relaxation, then the lift off the surface. Taubin rather than a plain Laplacian because
    /// a Laplacian pass alone drags every point toward the chain's centre and shrinks it away from
    /// the crease it is meant to trace; alternating a shrinking pass with an inflating one smooths
    /// without that drift. An open chain's endpoints are pinned, so a run does not creep back from
    /// the junction it started at.
    /// </summary>
    private static Vector3[] Relax(Vector3[] points, Vector3[] normals, float lift, bool closed)
    {
        int n = points.Length;
        if (n < 4) return points;

        var work = points;
        var scratch = new Vector3[n];
        for (int pass = 0; pass < ContourPasses; pass++)
        {
            LaplacianPass(work, scratch, ContourLambda, closed);
            (work, scratch) = (scratch, work);
            LaplacianPass(work, scratch, ContourMu, closed);
            (work, scratch) = (scratch, work);
        }

        for (int i = 0; i < n; i++) work[i] += normals[i] * lift;
        return work;

        static void LaplacianPass(Vector3[] source, Vector3[] destination, float factor, bool closed)
        {
            int count = source.Length;
            for (int i = 0; i < count; i++)
            {
                if (!closed && (i == 0 || i == count - 1))
                {
                    destination[i] = source[i];
                    continue;
                }

                var midpoint = (source[(i - 1 + count) % count] + source[(i + 1) % count]) * 0.5f;
                destination[i] = source[i] + (factor * (midpoint - source[i]));
            }
        }
    }

    // ---------------------------------------------------------------- pass 1: measure

    /// <summary>
    /// How sharply the surface folds across one edge, measured both ways. Both are signed, positive
    /// where the surface folds away from its outward normals (a convex ridge) and negative where it
    /// folds towards them (a concave valley), so a convexity test is just a comparison against a
    /// positive threshold.
    /// </summary>
    private readonly record struct Fold(float AngleDegrees, float Curvature);

    /// <summary>
    /// A welded view of a mesh: coincident corners merged, face normals, centroids and areas
    /// computed once, and every interior edge's <see cref="Fold"/> cached.
    /// </summary>
    private sealed class Surface
    {
        public required Vector3[] Positions { get; init; }
        public required int[] Triangles { get; init; }
        public required Vector3[] FaceNormal { get; init; }
        public required float[] FaceArea { get; init; }
        public required Dictionary<(int, int), (int First, int Second)> Edges { get; init; }
        public required Dictionary<(int, int), Fold> Folds { get; init; }
        public required Dictionary<int, List<int>> VertexNeighbours { get; init; }
        public required float Diagonal { get; init; }
        public required float TotalArea { get; init; }
        public required float MeanEdgeLength { get; init; }

        public int FaceCount => FaceArea.Length;

        public float EdgeLength((int, int) edge) =>
            Vector3.Distance(Positions[edge.Item1], Positions[edge.Item2]);

        /// <summary>
        /// Area-weighted vertex normals, for lifting a contour clear of the surface. Weighted by area
        /// so a fan of slivers on one side of a vertex does not outvote the one broad face on the
        /// other, which on a remeshed rim would tilt the lift along the surface instead of off it.
        /// </summary>
        public Vector3[] VertexNormals()
        {
            var normals = new Vector3[Positions.Length];
            for (int t = 0; t < FaceCount; t++)
            {
                var weighted = FaceNormal[t] * FaceArea[t];
                for (int corner = 0; corner < 3; corner++)
                    normals[Triangles[(t * 3) + corner]] += weighted;
            }

            for (int i = 0; i < normals.Length; i++)
                normals[i] = normals[i].LengthSquared() < 1e-12f ? Vector3.Zero : Vector3.Normalize(normals[i]);

            return normals;
        }

        /// <summary>The <paramref name="corner"/>'th edge of a face, in the same key form as <see cref="Edges"/>.</summary>
        public (int, int) FaceEdge(int face, int corner)
        {
            int a = Triangles[(face * 3) + corner];
            int b = Triangles[(face * 3) + ((corner + 1) % 3)];
            return a < b ? (a, b) : (b, a);
        }

        public static Surface Build(IMesh mesh)
        {
            var sourceVertices = mesh.Vertices;
            var sourceTriangles = mesh.Triangles;
            int triangleCount = sourceTriangles.Length / 3;

            // --- weld ---
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

            // --- per-face normal, centroid and area ---
            var normals = new Vector3[triangleCount];
            var centroids = new Vector3[triangleCount];
            var areas = new float[triangleCount];
            float totalArea = 0f;
            for (int t = 0; t < triangleCount; t++)
            {
                var a = positions[triangles[t * 3]];
                var b = positions[triangles[(t * 3) + 1]];
                var c = positions[triangles[(t * 3) + 2]];

                var cross = Vector3.Cross(b - a, c - a);
                float length = cross.Length();
                normals[t] = length < 1e-12f ? Vector3.Zero : cross / length;
                centroids[t] = (a + b + c) / 3f;
                areas[t] = length * 0.5f;
                totalArea += areas[t];
            }

            // --- edge adjacency, and the vertex graph the bridging walks ---
            var edges = new Dictionary<(int, int), (int, int)>(triangleCount * 2);
            var vertexNeighbours = new Dictionary<int, List<int>>(positions.Length);
            for (int t = 0; t < triangleCount; t++)
                for (int e = 0; e < 3; e++)
                {
                    int a = triangles[(t * 3) + e];
                    int b = triangles[(t * 3) + ((e + 1) % 3)];
                    var key = a < b ? (a, b) : (b, a);

                    // A third face on the same edge means non-manifold geometry; the first pair wins,
                    // which is enough to give the edge a fold rather than skipping it entirely.
                    if (edges.TryGetValue(key, out var pair))
                    {
                        edges[key] = (pair.Item1, pair.Item2 < 0 ? t : pair.Item2);
                        continue;
                    }

                    edges[key] = (t, -1);
                    Attach(vertexNeighbours, a, b);
                    Attach(vertexNeighbours, b, a);
                }

            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in positions)
            {
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }

            var folds = new Dictionary<(int, int), Fold>(edges.Count);
            foreach (var (key, pair) in edges)
            {
                // A boundary edge borders one face, so there is no fold across it to measure.
                if (pair.Item2 < 0) continue;
                folds[key] = Measure(positions, triangles, normals, centroids, key, pair.Item1, pair.Item2);
            }

            double edgeTotal = 0d;
            foreach (var key in edges.Keys) edgeTotal += Vector3.Distance(positions[key.Item1], positions[key.Item2]);

            return new Surface
            {
                Positions = positions,
                Triangles = triangles,
                FaceNormal = normals,
                FaceArea = areas,
                Edges = edges,
                Folds = folds,
                VertexNeighbours = vertexNeighbours,
                Diagonal = (max - min).Length(),
                TotalArea = totalArea,
                MeanEdgeLength = edges.Count > 0 ? (float)(edgeTotal / edges.Count) : 1f,
            };

            static void Attach(Dictionary<int, List<int>> map, int key, int value)
            {
                if (!map.TryGetValue(key, out var list)) map[key] = list = new List<int>(6);
                list.Add(value);
            }
        }

        /// <summary>
        /// Measures the fold across one edge: the angle between the two face normals, and that angle
        /// divided by the distance the surface travels crossing from one face to the other - the
        /// reciprocal of the radius of the arc the pair describes.
        ///
        /// <para>
        /// That distance is measured perpendicular to the edge rather than straight between the face
        /// centroids. Only the part of the step that crosses the fold is the arc; the part that runs
        /// along it is not turning at all. On triangles stretched out along a crease - a common shape
        /// wherever a rim has been remeshed - the along-edge component is the larger of the two, and
        /// including it divides a genuine crease down into the noise.
        /// </para>
        /// </summary>
        private static Fold Measure(
            Vector3[] positions, int[] triangles, Vector3[] normals, Vector3[] centroids,
            (int, int) edge, int first, int second)
        {
            var n0 = normals[first];
            var n1 = normals[second];
            if (n0 == Vector3.Zero || n1 == Vector3.Zero) return default; // degenerate face, nothing to fold

            float angle = MathF.Acos(Math.Clamp(Vector3.Dot(n0, n1), -1f, 1f));

            // The corner of the second face that isn't on the shared edge tells us which way the fold
            // goes: behind the first face's plane is convex, in front of it is concave.
            var opposite = Vector3.Zero;
            for (int e = 0; e < 3; e++)
            {
                int id = triangles[(second * 3) + e];
                if (id == edge.Item1 || id == edge.Item2) continue;
                opposite = positions[id];
                break;
            }
            float sign = Vector3.Dot(opposite - positions[edge.Item1], n0) < 0f ? 1f : -1f;

            var step = centroids[second] - centroids[first];
            var along = positions[edge.Item2] - positions[edge.Item1];
            float alongLength = along.Length();
            if (alongLength > 1e-6f)
            {
                along /= alongLength;
                step -= along * Vector3.Dot(step, along);
            }

            float span = step.Length();
            return new Fold(
                AngleDegrees: sign * angle * 180f / MathF.PI,
                Curvature: span < 1e-6f ? 0f : sign * angle / span);
        }
    }
}
