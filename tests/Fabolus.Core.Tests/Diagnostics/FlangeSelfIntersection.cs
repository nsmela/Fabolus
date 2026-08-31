using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using GeometryMeshLib;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// Where the parting solid self-intersects, and what the parting line is doing there.
///
/// <para>
/// Built because the sweep was being treated as a black box. Six variants of
/// <see cref="PartingLineCentring"/> were chosen by whether <c>PartingSolid_OnARealBody</c> passed,
/// which says only that some cutter somewhere crossed itself - not where, not how badly, and not
/// whether the crossings had anything to do with the stretches of line that moved. That is not enough
/// to design against, and the variant that scored best on it turned out to have a visible defect the
/// suite never looked at.
/// </para>
///
/// <para>
/// The mechanism is already written down in <c>ExtrudeFlange</c>: extruding copies the flange to two
/// sheets a slab apart along the axis, so wherever the surface is steeper than the slab is thick the
/// two sheets pass through each other. So this measures the three quantities that claim implies - how
/// many crossings the surface has before it is ever extruded, how many the raw slab has, and how many
/// survive the repair - and then locates them against the line and against its slope.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class FlangeSelfIntersection
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public FlangeSelfIntersection(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [Fact]
    public void WhereTheCutterCrossesItself()
    {
        foreach (string file in new[] { "chin_bolus.stl", "nose_bolus.stl", "scalp_bolus.stl", "eye_bolus.stl" })
        {
            var mesh = _assets.LoadStl(file);
            var body = BodyMesh.Create(mesh);
            if (body.IsFailure) { _log.WriteLine($"{file}: no body"); continue; }

            var feature = new PartingMeshFeature(_engine);

            var traced = feature.GeneratePartingLineFromThickness(body.Value);
            if (traced.IsFailure) { _log.WriteLine($"{file}: {traced.Error.Description}"); continue; }

            var centred = feature.GeneratePartingLineFromThickness(
                body.Value, centring: PartingLineCentringOptions.Default);

            _log.WriteLine($"=== {file}");
            var tracedReport = Build(feature, mesh, body.Value, traced.Value, "traced");
            var centredReport = Build(feature, mesh, body.Value, centred.Value, "centred");

            if (tracedReport is not null && centredReport is not null)
            {
                float tilt = MathF.Acos(Math.Clamp(
                    Vector3.Dot(tracedReport.Axis, centredReport.Axis), -1f, 1f)) * 180f / MathF.PI;

                var a = traced.Value.Loops[0];
                var b = centred.Value.Loops[0];
                var moves = Enumerable.Range(0, Math.Min(a.Count, b.Count))
                    .Select(i => Vector3.Distance(a[i], b[i])).ToArray();

                _log.WriteLine(
                    $"    axis tilt {tilt:F3} deg; line moved median {Median(moves):F2} mm, " +
                    $"max {moves.Max():F2} mm, unmoved {(float)moves.Count(m => m < 0.05f) / moves.Length:P0}");
            }

            // Whether the crossings sit where the line moved. If they do, the correction is the cause
            // and the fix belongs in it; if they sit where the line did not move at all, the line is
            // only the trigger and the flange was already marginal there.
            if (tracedReport is not null && centredReport is not null)
                Compare(traced.Value, centred.Value, centredReport);

            if (tracedReport is not null)
                Build(feature, mesh, body.Value, centred.Value, "centred, traced axis",
                    holdAxis: tracedReport.Axis);

            // And whether any of it survives a slab thick enough not to be knife-edge. The default is
            // a tenth of a millimetre, which is thin enough that a flange only has to tilt slightly
            // for the two sheets to meet - so this says whether the crossings are a property of the
            // line at all or of the depth the test happens to run at.
            foreach (float thick in new[] { 1f, 3f })
            {
                Build(feature, mesh, body.Value, traced.Value, $"traced, depth {thick}mm", depth: thick);
                Build(feature, mesh, body.Value, centred.Value, $"centred, depth {thick}mm", depth: thick);
            }

            // The other thickening route, at the default depth. A distance field cannot represent a
            // crossing at all, so if the axis extrusion's folds are the whole story this comes back
            // clean where that one does not.
            Build(feature, mesh, body.Value, traced.Value, "traced, offset thickening",
                thickening: PartingMeshThickening.Offset);
            Build(feature, mesh, body.Value, centred.Value, "centred, offset thickening",
                thickening: PartingMeshThickening.Offset);

            // The flange's slope ceiling, at the default depth. Extruding along the axis folds where
            // the surface runs close to parallel with it, so if steepness is what manufactures the
            // crossings then easing this ceiling should remove them at source rather than leaving the
            // repair to chase them afterwards.
            foreach (float deg in new[] { 25f, 15f })
            {
                Build(feature, mesh, body.Value, traced.Value, $"traced, slope {deg}deg", slope: deg);
                Build(feature, mesh, body.Value, centred.Value, $"centred, slope {deg}deg", slope: deg);
            }
        }
    }

    private sealed record FlangeReport(IMesh Surface, IMesh Raw, Vector3[] Crossings, Vector3 Axis);

    private FlangeReport? Build(
        PartingMeshFeature feature, IMesh mesh, BodyMesh body, PartingLine line, string label,
        Vector3? holdAxis = null, float? depth = null, PartingMeshThickening? thickening = null,
        float? slope = null)
    {
        var parameters = PartingMeshFeature.ResolveAxis(
            line, PartingMeshParameters.Default with { AxisSource = PartingMeshAxisSource.PartingLine });
        if (parameters.IsFailure) { _log.WriteLine($"  {label}: axis - {parameters.Error.Description}"); return null; }

        // Pinning the axis is how the two effects are told apart. The axis is fitted to the line, so
        // centring re-fits it; building the centred line's flange on the traced line's axis leaves
        // only the change in where the rim sits, and any crossings that survive that are the
        // correction's own rather than the re-tilt's.
        var settings = parameters.Value;
        if (holdAxis is not null) settings = settings with { Axis = holdAxis.Value };
        if (depth is not null) settings = settings with { Depth = depth.Value };
        if (thickening is not null) settings = settings with { Thickening = thickening.Value };
        if (slope is not null) settings = settings with { FlangeMaxSlopeDeg = slope.Value };

        var contour = _engine.PartingTools.GenerateOuterBoxContour(
            mesh, settings.Axis, settings.OuterContourMargin);
        if (contour.IsFailure) { _log.WriteLine($"  {label}: contour - {contour.Error.Description}"); return null; }

        var surface = feature.GenerateFlangeSurface(line, contour.Value, settings, body);
        if (surface.IsFailure) { _log.WriteLine($"  {label}: surface - {surface.Error.Description}"); return null; }

        // The raw slab, before the cut-and-fill repair, which is where the mechanism shows. The
        // repaired count alone hides whether the repair had a little to do or a lot.
        var raw = _engine.PartingTools.ExtrudeFlange(
            surface.Value, settings.Axis, settings.Depth);
        var repaired = feature.ExtrudeFlange(surface.Value, settings);

        int onSurface = Count(surface.Value);
        int onRaw = raw.IsSuccess ? Count(raw.Value) : -1;
        int onRepaired = repaired.IsSuccess ? Count(repaired.Value) : -1;

        // The axis is fitted to the line, so a line that moved is a line that may have tilted the
        // plane the whole flange is built on. If it has, the two flanges being compared are not the
        // same flange with a different rim - they are different sweeps, and comparing their crossing
        // counts says nothing about the correction.
        var axis = settings.Axis;
        _log.WriteLine(
            $"  {label}: surface {onSurface}, raw slab {onRaw}, after repair {onRepaired}" +
            $"  (depth {settings.Depth:F3} mm, axis {axis.X:F4} {axis.Y:F4} {axis.Z:F4})");

        // What the repair actually produced, before PartingMeshFeature decides whether to keep it.
        // A repaired count equal to the raw one every single time is not a repair that tried and
        // failed - it is a repair whose result was thrown away, and the two guards that can throw it
        // away are "it did not reduce the count" and "it opened the solid".
        if (raw.IsSuccess && onRaw > 0)
        {
            var attempt = _engine.Modifiers.RepairSelfIntersections(
                raw.Value, SelfIntersectionRepair.CutAndFill);

            if (attempt.IsFailure)
            {
                _log.WriteLine($"      repair failed: {attempt.Error.Description}");
            }
            else
            {
                var before = _engine.Evaluators.ValidateTopology(raw.Value);
                var after = _engine.Evaluators.ValidateTopology(attempt.Value);
                _log.WriteLine(
                    $"      repair produced {Count(attempt.Value)} crossing(s), " +
                    $"watertight {before.Value.IsWatertight} -> {after.Value.IsWatertight}" +
                    $"{(after.Value.IsWatertight ? "" : "  <-- REJECTED, cutter left unrepaired")}");
            }
        }

        if (repaired.IsFailure || raw.IsFailure) return null;

        var crossings = Faces(repaired.Value);
        return new FlangeReport(surface.Value, raw.Value, crossings, parameters.Value.Axis);
    }

    /// <summary>
    /// Locates each surviving crossing against the parting line, and says whether the centring moved
    /// the line there.
    /// </summary>
    private void Compare(PartingLine traced, PartingLine centred, FlangeReport report)
    {
        if (report.Crossings.Length == 0)
        {
            _log.WriteLine("    no crossings survive the repair");
            return;
        }

        var loop = centred.Loops[0];
        var before = traced.Loops[0];

        var distances = new List<float>();
        var shifts = new List<float>();

        foreach (var point in report.Crossings)
        {
            int nearest = 0;
            float best = float.MaxValue;
            for (int i = 0; i < loop.Count; i++)
            {
                float d = Vector3.Distance(point, loop[i]);
                if (d >= best) continue;
                best = d;
                nearest = i;
            }

            distances.Add(best);
            if (nearest < before.Count) shifts.Add(Vector3.Distance(before[nearest], loop[nearest]));
        }

        var away = distances.ToArray();
        var shift = shifts.ToArray();

        _log.WriteLine(
            $"    {report.Crossings.Length} crossing face(s): distance to line median " +
            $"{Median(away):F1} mm, min {away.Min():F1}, max {away.Max():F1}");
        _log.WriteLine(
            $"    line moved at the nearest point: median {Median(shift):F2} mm, " +
            $"max {(shift.Length == 0 ? 0f : shift.Max()):F2} mm, " +
            $"unmoved {(float)shift.Count(s => s < 0.05f) / MathF.Max(shift.Length, 1):P0}");
    }

    // ---------------------------------------------------------------- MeshLib

    /// <summary>
    /// The same conversion <c>Geometry.MeshLib</c> uses internally. Repeated here rather than made
    /// public: this is a diagnostic reaching past the engine's interface on purpose, and widening that
    /// interface so a test can look inside would be the wrong way round.
    /// </summary>
    private static MR.Mesh ToMeshLib(IMesh mesh)
    {
        var ml = new MR.Mesh();
        var vertices = mesh.Vertices;
        var triangles = mesh.Triangles;

        ml.points.vec.resize((ulong)vertices.Length);
        for (int i = 0; i < vertices.Length; i++)
            ml.points.vec[(ulong)i] = new MR.Vector3f(vertices[i].X, vertices[i].Y, vertices[i].Z);

        using var triples = new MR.Std.Vector_MRVertId();
        triples.resize((ulong)triangles.Length);
        for (int i = 0; i < triangles.Length; i++) triples[(ulong)i] = new MR.VertId(triangles[i]);

        MR.MeshBuilder.addTriangles(ml.topology, triples, null);
        ml.invalidateCaches();
        return ml;
    }

    private static int Count(IMesh mesh)
    {
        using var ml = ToMeshLib(mesh);
        using var faces = MR.SelfIntersections.getFaces(ml);
        return (int)faces.count();
    }

    /// <summary>Centroid of every self-intersecting face, which is what gives the crossings a place.</summary>
    private static Vector3[] Faces(IMesh mesh)
    {
        using var ml = ToMeshLib(mesh);
        using var faces = MR.SelfIntersections.getFaces(ml);

        var found = new List<Vector3>();
        ulong capacity = ml.topology.faceCapacity();
        for (ulong i = 0; i < capacity; i++)
        {
            var id = new MR.FaceId((int)i);
            if (!faces.test(id)) continue;

            var centre = ml.triCenter(id);
            found.Add(new Vector3(centre.x, centre.y, centre.z));
        }

        return found.ToArray();
    }

    private static float Median(float[] values)
    {
        if (values.Length == 0) return 0f;
        var sorted = (float[])values.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }
}
