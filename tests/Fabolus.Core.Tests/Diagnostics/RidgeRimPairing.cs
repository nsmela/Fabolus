using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Whether each rim is a wall or a knife edge, by asking whether its contour has a partner.
///
/// <para>
/// The band model assumes a rim is a wall: two creases with the shell's thickness between them, drawn
/// as two contours running parallel a wall apart. Where a shell tapers to nothing that stops being
/// true. The two creases merge into one, the band has no interior left to fill, and the rim comes back
/// as a single contour with nothing beside it - which is not a defect but a different shape, and one
/// the pairing assumption has no answer for.
/// </para>
///
/// <para>
/// Told apart by distance alone: a contour bounding a wall has another running alongside it at roughly
/// the wall's thickness, and a contour on a knife edge has its nearest neighbour somewhere else on the
/// body entirely, which is far. There is a wide empty gap between the two on every body here, so this
/// needs no threshold argued finer than "about a wall".
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class RidgeRimPairing
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public RidgeRimPairing(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void WallOrKnifeEdge()
    {
        var models = new (string Id, string Asset)[]
        {
            ("chin", "3mf/chin.3mf"),
            ("ear", "3mf/ear.3mf"),
            ("eye", "3mf/eye.3mf"),
            ("larynx-large", "3mf/larynx_large.3mf"),
            ("larynx-small", "3mf/larynx_small.3mf"),
            ("nose", "3mf/nose.3mf"),
            ("scalp", "3mf/scalp.3mf"),
            ("standard", "3mf/test bolus standard.3mf"),
        };

        foreach (var (id, asset) in models)
        {
            var imported = _engine.IO.Import(_assets.GetAssetPath(asset));
            var mould = MouldMesh.Create(imported.Value);
            var body = new PartingMeshFeature(_engine).GetBodyMesh(mould.Value).Value.Mesh;

            var diagnosis = RidgeDetection.Diagnose(body, RidgeDetectionOptions.Default);
            var contours = diagnosis.Contours.Where(c => c.IsClosed).ToList();

            var thickness = _engine.Evaluators.MeasureWallThickness(body, WallThicknessOptions.Default);
            float wall = thickness.IsSuccess ? Median(thickness.Value) : float.NaN;

            // The filled band against the whole ridge. A wall encloses surface between its two creases
            // and the fill claims it; a knife edge encloses nothing, so its facets are on the ridge only
            // by touching a crease. The share of the ridge that is filled is therefore the same question
            // asked of the faces rather than of the curves, and a second reading of it is worth having.
            int ridgeFaces = diagnosis.RidgeFaces.Count(r => r);
            int filled = diagnosis.FilledFaces.Count(f => f);

            _log.WriteLine($"{id}  wall {wall:F2}mm  ridge {ridgeFaces} faces, {filled} filled " +
                           $"({(ridgeFaces > 0 ? (float)filled / ridgeFaces : 0f):P0})");

            // What the band believes its own width to be where it flags a shortfall, in mesh terms.
            // A wall that has lost a boundary still sits in a neighbourhood several triangles wide; a
            // knife edge has no width to lose anywhere, so if the two are distinguishable here the
            // repair can be gated without a ray cast.
            var profile = diagnosis.BandProfile;
            var suspectExpected = Enumerable.Range(0, profile.PerFaceSuspect.Length)
                .Where(f => profile.PerFaceSuspect[f])
                .Select(f => profile.PerFaceExpected[f])
                .Where(e => !float.IsPositiveInfinity(e))
                .OrderBy(e => e)
                .ToArray();

            float edge = diagnosis.Report.Surface.MeanEdgeLength;
            _log.WriteLine(suspectExpected.Length == 0
                ? $"  no suspect faces (mean edge {edge:F2}mm)"
                : $"  {suspectExpected.Length} suspect, local median width at them " +
                  $"{suspectExpected[0]:F1} / {suspectExpected[suspectExpected.Length / 2]:F1} / " +
                  $"{suspectExpected[^1]:F1}mm = " +
                  $"{suspectExpected[suspectExpected.Length / 2] / edge:F1} x mean edge " +
                  $"({edge:F2}mm), {suspectExpected[suspectExpected.Length / 2] / wall:F2} x wall");

            for (int i = 0; i < contours.Count; i++)
            {
                float nearest = float.MaxValue;
                int partner = -1;
                for (int j = 0; j < contours.Count; j++)
                {
                    if (i == j) continue;

                    float mean = contours[i].Points.Average(p => Distance(p, contours[j]));
                    if (mean >= nearest) continue;

                    nearest = mean;
                    partner = j;
                }

                string verdict = contours.Count < 2 ? "solo - only contour on the body"
                    : nearest < wall * 1.6f ? $"wall, paired with {partner}"
                    : "SOLO - nearest contour is far, so this rim is a single ridge";

                _log.WriteLine(
                    $"  contour {i}: {contours[i].Points.Count,4} pts, " +
                    $"nearest partner {nearest,7:F2}mm = {nearest / wall,5:F2} x wall | {verdict}");
            }

            _log.WriteLine("");
        }
    }

    private static float Median(WallThickness thickness)
    {
        var measured = thickness.PerFace
            .Where(t => !float.IsPositiveInfinity(t) && t > 0f)
            .OrderBy(t => t)
            .ToArray();
        return measured.Length == 0 ? float.NaN : measured[measured.Length / 2];
    }

    private static float Distance(Vector3 point, RidgeContour contour)
    {
        var pts = contour.Points;
        int spans = contour.IsClosed ? pts.Count : pts.Count - 1;

        float best = float.MaxValue;
        for (int i = 0; i < spans; i++)
        {
            var a = pts[i];
            var b = pts[(i + 1) % pts.Count];
            var ab = b - a;
            float len2 = ab.LengthSquared();
            float t = len2 < 1e-12f ? 0f : Math.Clamp(Vector3.Dot(point - a, ab) / len2, 0f, 1f);
            best = MathF.Min(best, Vector3.Distance(point, a + (ab * t)));
        }
        return best;
    }
}
