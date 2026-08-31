using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using System.Numerics;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Covers the region fill: a rim is usually a wall between two creases rather than a single crease,
/// and what the user needs to see is the whole wall. These build a puck - a disc given thickness,
/// which is the same shape as a bolus shell in miniature - and check that its rim fills, that the
/// broad faces the rim separates do not, and that a rim whose crease fades out for a stretch still
/// fills because the break gets bridged first.
/// </summary>
[Collection("GeometryEngine collection")]
public class RidgeRegionTests
{
    private const float Radius = 150f;
    private const float Thickness = 20f;

    private readonly IGeometryEngine _engine;

    public RidgeRegionTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    [Fact]
    public void FindRidgeFaces_PuckRim_FillsTheWholeWall()
    {
        var puck = Puck(out var faces);

        var ridges = RidgeDetection.FindRidgeFaces(puck, RidgeDetectionOptions.Default);

        // Mid-wall faces touch no crease - they are more than a facet away from both rim edges - so
        // they can only be marked by the region fill, which is exactly what is under test.
        faces.MidWall.Should().NotBeEmpty("the fixture must have wall faces away from both rim edges");
        faces.MidWall.Should().OnlyContain(f => ridges[f], "the rim wall is one narrow enclosed band");
    }

    [Fact]
    public void FindRidgeFaces_PuckFaces_AreNotFilled()
    {
        var puck = Puck(out var faces);

        var ridges = RidgeDetection.FindRidgeFaces(puck, RidgeDetectionOptions.Default);

        faces.CapInterior.Should().NotBeEmpty();
        faces.CapInterior.Should().OnlyContain(f => !ridges[f],
            "the flat faces either side of the rim are the surfaces the rim divides, not the rim");
    }

    /// <summary>
    /// Establishes what bridging is for: a rim with its crease rounded away over a stretch no longer
    /// encloses the wall, the fill escapes into the face beyond, and the whole band goes unmarked.
    ///
    /// <para>
    /// The other half of that claim - that bridging closes such a break and restores the fill - is
    /// verified against real bodies rather than here, because a break this pass can reach across has
    /// to be a few millimetres long, and a break that short cannot be modelled smoothly: rounding a
    /// rim away over a few millimetres puts a fold across the wall steep enough to be a crease in its
    /// own right, which re-seals the region for the wrong reason and tests nothing. The breaks
    /// bridging actually fixes come from tessellation, not from shape - the stair-stepped rim of a CT
    /// surface dropping below threshold for an edge or two at a time.
    /// </para>
    /// </summary>
    [Fact]
    public void FindRidgeFaces_RimBrokenOpen_IsNotFilled()
    {
        var puck = Puck(out var faces, fadeRadius: 10f);
        var noBridging = RidgeDetectionOptions.Default with { MaxGapFraction = 0f };

        var ridges = RidgeDetection.FindRidgeFaces(puck, noBridging);

        faces.MidWallAwayFromFade.Should().NotBeEmpty();
        faces.MidWallAwayFromFade.Should().OnlyContain(f => !ridges[f],
            "an open rim lets the fill escape into the face beyond it, leaving one region too big to fill");
    }

    [Fact]
    public void FindRidgeFaces_Puck_LeavesMostOfTheSurfaceUnmarked()
    {
        var puck = Puck(out _);

        var ridges = RidgeDetection.FindRidgeFaces(puck, RidgeDetectionOptions.Default);

        // A guard on the whole pipeline rather than on one region: whatever else changes, a shape
        // whose rim is a tenth of its surface must not come back mostly purple. Measured as area
        // rather than as a count of faces, because the crease itself is tessellated into slivers
        // that are a large share of the faces and a negligible share of the model.
        MarkedAreaFraction(puck, ridges).Should().BeInRange(0.02f, 0.30f);
    }

    // --- contours --- //

    [Fact]
    public void FindRidgeContours_PuckRim_TracesTheTwoCreasesBoundingIt()
    {
        var puck = Puck(out _);

        var contours = RidgeDetection.FindRidgeContours(puck, RidgeDetectionOptions.Default);

        // The rim is a band, so what bounds it is the crease at its top and the crease at its bottom.
        contours.Should().HaveCount(2);
        foreach (var contour in contours)
        {
            contour.IsClosed.Should().BeTrue("each crease runs the whole way round");
            Length(contour).Should().BeApproximately(2f * MathF.PI * Radius, Radius * 0.15f);
        }
    }

    [Fact]
    public void FindRidgeContours_FollowTheCreaseNotTheFacetsBesideIt()
    {
        var puck = Puck(out _);

        var contours = RidgeDetection.FindRidgeContours(puck, RidgeDetectionOptions.Default);

        // The whole point of a curve over a facet colouring. Outlining the marked facets would put
        // the curve a triangle's width off the rim - out at the cap ring, a good 20% of the radius
        // inboard - on whichever side the tessellation happened to fall.
        foreach (var contour in contours)
            foreach (var point in contour.Points)
            {
                float radial = MathF.Sqrt((point.X * point.X) + (point.Z * point.Z));
                radial.Should().BeInRange(Radius * 0.98f, Radius * 1.02f, "the crease is at the rim radius");
            }
    }

    [Fact]
    public void FindRidgeContours_AreSmootherThanTheTrianglesTheyWereTracedAlong()
    {
        var puck = Puck(out _);

        var contours = RidgeDetection.FindRidgeContours(puck, RidgeDetectionOptions.Default);

        // The rim of a puck is a circle, so with the triangle-scale staircase relaxed away no turn
        // in the curve should read as a corner.
        foreach (var contour in contours)
            WorstTurnDegrees(contour).Should().BeLessThan(15f);
    }

    [Fact]
    public void FindRidgeContours_AreLiftedClearOfTheSurface()
    {
        var puck = Puck(out _);

        var contours = RidgeDetection.FindRidgeContours(puck, RidgeDetectionOptions.Default);

        // Relaxation cuts the corner on a convex feature, and a rim is convex, so without the lift
        // the curve would sink inside the model and be hidden by the surface it describes.
        foreach (var contour in contours)
            foreach (var point in contour.Points)
                MathF.Sqrt((point.X * point.X) + (point.Z * point.Z)).Should().BeGreaterThan(Radius);
    }

    [Fact]
    public void FindRidgeContours_Sphere_TracesNothing()
    {
        var sphere = _engine.Generators.GenerateSphere(Vector3.Zero, 30.0, 48);

        RidgeDetection.FindRidgeContours(sphere.Value, RidgeDetectionOptions.Default)
            .Should().BeEmpty("a sphere has no rim to trace");
    }

    private static float Length(RidgeContour contour)
    {
        var points = contour.Points;
        int spans = contour.IsClosed ? points.Count : points.Count - 1;

        float total = 0f;
        for (int i = 0; i < spans; i++) total += Vector3.Distance(points[i], points[(i + 1) % points.Count]);
        return total;
    }

    private static float WorstTurnDegrees(RidgeContour contour)
    {
        var points = contour.Points;
        int first = contour.IsClosed ? 0 : 1;
        int last = contour.IsClosed ? points.Count : points.Count - 1;

        float worst = 0f;
        for (int i = first; i < last; i++)
        {
            var incoming = points[i] - points[(i - 1 + points.Count) % points.Count];
            var outgoing = points[(i + 1) % points.Count] - points[i];
            if (incoming.Length() < 1e-6f || outgoing.Length() < 1e-6f) continue;

            float turn = MathF.Acos(Math.Clamp(
                Vector3.Dot(Vector3.Normalize(incoming), Vector3.Normalize(outgoing)), -1f, 1f));
            worst = MathF.Max(worst, turn * 180f / MathF.PI);
        }
        return worst;
    }

    private static float MarkedAreaFraction(IMesh mesh, bool[] ridges)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        float marked = 0f, total = 0f;
        for (int t = 0; t < ridges.Length; t++)
        {
            var a = vertices[triangles[t * 3]];
            var b = vertices[triangles[(t * 3) + 1]];
            var c = vertices[triangles[(t * 3) + 2]];
            float area = Vector3.Cross(b - a, c - a).Length() * 0.5f;

            total += area;
            if (ridges[t]) marked += area;
        }
        return total > 0f ? marked / total : 0f;
    }

    // --- fixture --- //

    private sealed record PuckFaces(
        IReadOnlyList<int> MidWall,
        IReadOnlyList<int> MidWallAwayFromFade,
        IReadOnlyList<int> CapInterior);

    /// <summary>
    /// A disc of <see cref="Radius"/> given <see cref="Thickness"/>, built as a profile swept round
    /// the axis: bottom cap, wall, a fillet rolling over the top corner, top cap. The bottom corner
    /// is left as a bare 90 degree edge; the top corner's fillet radius is what varies.
    /// </summary>
    /// <param name="fadeRadius">
    /// Top fillet radius, in mm, at the middle of the faded stretch. The default 0.3mm is a corner
    /// in all but name - 3.3/mm of curvature - while 6mm is a roll gentle enough (0.17/mm, and 22.5
    /// degrees per step) to fall under both detection thresholds and read as no crease at all. The
    /// radius eases between the two so the stretch does not simply swap one crease for another
    /// running across the wall.
    /// </param>
    private IMesh Puck(out PuckFaces faces, float fadeRadius = 0.3f)
    {
        const int Steps = 96;      // around the circumference
        const int CapRings = 4;    // rings per cap, so cap interiors sit clear of the rim
        const int WallBands = 4;   // bands up the wall, so mid-wall sits clear of both rim edges
        const int FilletSteps = 6; // segments rolling over the top corner
        const int FadeStart = 24;  // where the faded stretch begins, in steps
        const int FadeSpan = 12;   // how many steps it takes to ease in and out again

        const float SharpRadius = 0.3f;

        // profile: [0] bottom centre, cap rings out to the rim, wall up, fillet over, cap rings in,
        // [last] top centre. The two centres are shared by every step round the circumference.
        int profileLength = 1 + CapRings + WallBands + FilletSteps + CapRings;

        var vertices = new List<double>();
        var index = new int[Steps, profileLength];

        int bottomCentre = AddVertex(vertices, new Vector3(0, -Thickness / 2f, 0));
        int topCentre = -1;

        for (int s = 0; s < Steps; s++)
        {
            float angle = 2f * MathF.PI * s / Steps;
            float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
            float r = FilletRadiusAt(s, Steps, FadeStart, FadeSpan, SharpRadius, fadeRadius);

            int p = 0;
            index[s, p++] = bottomCentre;

            for (int m = 1; m <= CapRings; m++)
                index[s, p++] = Add(vertices, cos, sin, Radius * m / CapRings, -Thickness / 2f);

            for (int j = 1; j <= WallBands; j++)
                index[s, p++] = Add(vertices, cos, sin, Radius,
                    (-Thickness / 2f) + (j * (Thickness - r) / WallBands));

            for (int k = 1; k <= FilletSteps; k++)
            {
                float t = MathF.PI / 2f * k / FilletSteps;
                index[s, p++] = Add(vertices, cos, sin,
                    Radius - r + (r * MathF.Cos(t)),
                    (Thickness / 2f) - r + (r * MathF.Sin(t)));
            }

            for (int m = 1; m <= CapRings; m++)
            {
                float inner = (Radius - r) * (1f - ((float)m / CapRings));
                if (m == CapRings)
                {
                    if (topCentre < 0) topCentre = AddVertex(vertices, new Vector3(0, Thickness / 2f, 0));
                    index[s, p++] = topCentre;
                }
                else index[s, p++] = Add(vertices, cos, sin, inner, Thickness / 2f);
            }
        }

        // Stitch neighbouring profiles. Quads collapse to triangles at the shared centres.
        var triangles = new List<int>();
        for (int s = 0; s < Steps; s++)
        {
            int next = (s + 1) % Steps;
            for (int p = 0; p < profileLength - 1; p++)
            {
                int a = index[s, p], b = index[s, p + 1];
                int c = index[next, p], d = index[next, p + 1];

                // Wound so the normals face outward; inward-facing normals would make every crease
                // read as a concave valley and nothing would be detected at all.
                if (a != c) triangles.AddRange(new[] { a, b, c });
                if (b != d) triangles.AddRange(new[] { b, d, c });
            }
        }

        var result = _engine.CreateMesh(vertices.ToArray(), triangles.ToArray());
        result.IsSuccess.Should().BeTrue();
        var mesh = result.Value;

        faces = Classify(mesh, FadeStart, FadeSpan, Steps);
        return mesh;

        static int Add(List<double> into, float cos, float sin, float radial, float axial) =>
            AddVertex(into, new Vector3(radial * cos, axial, radial * sin));

        static int AddVertex(List<double> into, Vector3 v)
        {
            into.Add(v.X); into.Add(v.Y); into.Add(v.Z);
            return (into.Count / 3) - 1;
        }
    }

    /// <summary>
    /// Eases the top fillet radius from <paramref name="sharp"/> up to <paramref name="faded"/> and
    /// back across the faded stretch, on a raised cosine so no step is a crease in its own right.
    /// </summary>
    private static float FilletRadiusAt(int step, int steps, int fadeStart, int fadeSpan, float sharp, float faded)
    {
        if (faded <= sharp) return sharp;

        int offset = ((step - fadeStart) % steps + steps) % steps;
        if (offset >= fadeSpan) return sharp;

        float phase = (float)offset / fadeSpan;
        return sharp + ((faded - sharp) * 0.5f * (1f - MathF.Cos(2f * MathF.PI * phase)));
    }

    /// <summary>
    /// Picks out the faces the assertions talk about, by where their centroids sit: mid-wall faces
    /// (on the rim, clear of both its edges), the same excluding the faded stretch, and cap faces
    /// well inside the flat faces.
    /// </summary>
    private static PuckFaces Classify(IMesh mesh, int fadeStart, int fadeSpan, int steps)
    {
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        var midWall = new List<int>();
        var midWallAway = new List<int>();
        var capInterior = new List<int>();

        // The faded stretch, in radians, padded either side so "away from it" really is away.
        float fadeFrom = (2f * MathF.PI * (fadeStart - 4)) / steps;
        float fadeTo = (2f * MathF.PI * (fadeStart + fadeSpan + 4)) / steps;

        for (int t = 0; t < triangles.Length / 3; t++)
        {
            var centre = (vertices[triangles[t * 3]]
                        + vertices[triangles[(t * 3) + 1]]
                        + vertices[triangles[(t * 3) + 2]]) / 3f;

            float radial = MathF.Sqrt((centre.X * centre.X) + (centre.Z * centre.Z));

            // Mid-wall: out at the rim radius, and in the middle half of the thickness so the face
            // is a band or more clear of the top and bottom rim edges.
            if (radial > Radius * 0.97f && MathF.Abs(centre.Y) < Thickness * 0.25f)
            {
                midWall.Add(t);

                float angle = MathF.Atan2(centre.Z, centre.X);
                if (angle < 0) angle += 2f * MathF.PI;
                if (angle < fadeFrom || angle > fadeTo) midWallAway.Add(t);
            }

            // Cap interior: on a flat face, well in from the rim.
            if (radial < Radius * 0.6f && MathF.Abs(MathF.Abs(centre.Y) - (Thickness / 2f)) < 1e-3f)
                capInterior.Add(t);
        }

        return new PuckFaces(midWall, midWallAway, capInterior);
    }
}
