using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.PartingSplit;
using GeometryMeshLib;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Wpf.Tests.Features.PartingSplit;

/// <summary>
/// Runs the parting split with the parameter objects the view itself produces, against real geometry.
///
/// <para>
/// The other tests in this folder assert the view's settings, and the core tests assert that a recipe
/// spelled out by hand splits a mould. Neither catches the two drifting apart - the core tests hardcode
/// their own values, so the view could be changed to something that does not work and every test would
/// still pass. This closes that by taking <see cref="PartingSplitViewModel.LineParameters"/> and
/// <see cref="PartingSplitViewModel.MeshParameters"/> straight off the view model and putting those
/// exact objects through the feature.
/// </para>
/// </summary>
public class PartingSplitRecipeEndToEndTests
{
    private readonly ITestOutputHelper _out;
    public PartingSplitRecipeEndToEndTests(ITestOutputHelper output) => _out = output;

    /// <summary>The view model built as the app builds it, but with the engine mocked - only its
    /// parameter objects are wanted here, and they depend on nothing the engine provides.</summary>
    private static PartingSplitViewModel Recipe() => new(
        new StrongReferenceMessenger(),
        new Mock<IAlertDialog>().Object,
        new Mock<IGeometryEngine>().Object);

    private static string AssetPath(string name)
    {
        // Fully qualified: Fabolus.Wpf.Common carries its own FileSystem type, so pulling System.IO
        // in wholesale here invites a collision for no benefit.
        var path = System.IO.Path.Combine(System.AppContext.BaseDirectory, name);
        if (!System.IO.File.Exists(path))
            path = System.IO.Path.Combine(System.AppContext.BaseDirectory, "../../../../files", name);
        return System.IO.Path.GetFullPath(path);
    }

    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void TheViewsOwnRecipeBuildsAThinCutterAndBreaksTheMould(string file)
    {
        var engine = new GeometryEngine(new Fabolus.Wpf.Common.FileSystem());

        var imported = engine.IO.Import(AssetPath(file));
        Assert.True(imported.IsSuccess, imported.IsFailure ? imported.Error.Description : "");

        var workspace = Workspace.CreateEmpty().AddMesh(imported.Value).Value;
        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;

        var mould = new GenerateMould(engine).Execute(
            workspace, bodyId, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = bodyId });
        Assert.True(mould.IsSuccess, mould.IsFailure ? mould.Error.Description : "");

        var validated = MouldMesh.Create(mould.Value.GetMesh(bodyId).Value);
        Assert.True(validated.IsSuccess);

        // The app's own parameters - not a copy of them.
        var view = Recipe();
        var lineParameters = view.LineParameters;
        var meshParameters = view.MeshParameters;

        var feature = new PartingMeshFeature(engine);
        var body = feature.GetBodyMesh(validated.Value);
        Assert.True(body.IsSuccess);

        var line = feature.GeneratePartingLineFromBody(body.Value, lineParameters);
        Assert.True(line.IsSuccess, line.IsFailure ? line.Error.Description : "");

        var resolved = PartingMeshFeature.ResolveAxis(line.Value, meshParameters);
        Assert.True(resolved.IsSuccess);

        var contour = feature.GenerateOuterContour(validated.Value, resolved.Value);
        Assert.True(contour.IsSuccess);

        var flange = feature.GenerateFlangeSurface(line.Value, contour.Value, resolved.Value, body.Value);
        Assert.True(flange.IsSuccess, flange.IsFailure ? flange.Error.Description : "");

        // The cutter the user is shown in step two.
        var cutter = feature.ExtrudeFlange(flange.Value, resolved.Value);
        Assert.True(cutter.IsSuccess, cutter.IsFailure ? cutter.Error.Description : "");

        var topology = engine.Evaluators.ValidateTopology(cutter.Value);
        Assert.True(topology.IsSuccess);
        _out.WriteLine($"{file}: cutter selfInt={topology.Value.SelfIntersectionCount} " +
                       $"tris={cutter.Value.Triangles.Length / 3} watertight={topology.Value.IsWatertight}");

        Assert.True(topology.Value.IsWatertight, "the boolean needs a closed cutter");
        Assert.Equal(0, topology.Value.SelfIntersectionCount);

        // And the mould really comes apart, into two pieces that are both real.
        var split = feature.SplitMould(validated.Value, lineParameters, meshParameters);
        Assert.True(split.IsSuccess, split.IsFailure ? split.Error.Description : "");

        double mouldVolume = Volume(validated.Value.Mesh);
        double positive = Volume(split.Value.Positive) / mouldVolume;
        double negative = Volume(split.Value.Negative) / mouldVolume;
        _out.WriteLine($"{file}: halves {positive:P1} / {negative:P1}");

        Assert.True(positive > 0.2, $"positive half is only {positive:P1} of the mould");
        Assert.True(negative > 0.2, $"negative half is only {negative:P1} of the mould");
    }

    /// <summary>
    /// The cutter is thin and extruded, which is the change this recipe turns on: no offset pass, so
    /// nothing samples a voxel grid and the wall is placed exactly where the flange is.
    /// </summary>
    [Fact]
    public void TheRecipeAsksForAThinExtrudedCutterAndNoOffset()
    {
        var p = Recipe().MeshParameters;

        Assert.Equal(PartingMeshThickening.Extrude, p.Thickening);
        Assert.Equal(0.1f, p.Depth);
        Assert.Equal(PartingMeshSweep.TangentLaunch, p.Sweep);
        Assert.Equal(PartingSplitMethod.SeveredComponents, p.SplitMethod);
        Assert.Equal(PartingMeshAxisSource.PartingLine, p.AxisSource);
        Assert.Equal(PartingLineSource.ExtrusionBorder, Recipe().LineParameters.Source);
    }

    /// <summary>
    /// The flange leaves the parting line going the way the body's surface normal goes - the thing the
    /// pink arrows in step one promise and, until the launch was made to survive the height
    /// propagation, did not deliver.
    ///
    /// <para>
    /// Measured as a slope comparison, which is the fair test for a planar footprint: the outward
    /// direction is fixed by the 2D offsetting, but the rise per mm of outward travel is free, and it
    /// is exactly what the launch sets. On the planar sweep this sat at 3-6 degrees against normals
    /// asking for 17-48, a mean disagreement of 33-43 degrees; the bar below is set where the launch
    /// puts it with room to spare rather than at the measured value, so ordinary drift does not fail it.
    /// </para>
    /// </summary>
    [Theory]
    [InlineData("scalp_bolus.stl")]
    [InlineData("chin_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void TheFlangeLeavesAlongTheNormalsTheViewDraws(string file)
    {
        var engine = new GeometryEngine(new Fabolus.Wpf.Common.FileSystem());
        var imported = engine.IO.Import(AssetPath(file));
        Assert.True(imported.IsSuccess);

        var workspace = Workspace.CreateEmpty().AddMesh(imported.Value).Value;
        var bodyId = workspace.GetActiveMesh().Value.Metadata.Id;
        var mould = new GenerateMould(engine).Execute(
            workspace, bodyId, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = bodyId });
        var validated = MouldMesh.Create(mould.Value.GetMesh(bodyId).Value).Value;

        var view = Recipe();
        var feature = new PartingMeshFeature(engine);
        var body = feature.GetBodyMesh(validated).Value;
        var line = feature.GeneratePartingLineFromBody(body, view.LineParameters).Value;
        var loop = line.Loops[0];
        var normals = feature.SampleSurfaceNormals(body, loop).Value;

        var p = PartingMeshFeature.ResolveAxis(line, view.MeshParameters).Value;
        var contour = feature.GenerateOuterContour(validated, p).Value;
        var flange = feature.GenerateFlangeSurface(line, contour, p, body).Value;

        // Sampled at several distances out, not just next to the line. A flange that leaves along the
        // normal and then peels off further out reads as perfect at 5mm - which is exactly what the
        // first version of this test did, and it passed while the flange was 30 degrees adrift by
        // 40mm. Every distance has to hold.
        foreach (float outMm in new[] { 5f, 10f, 20f, 40f })
        {
            double error = MeanSlopeDisagreementDeg(flange, loop, normals, p.Axis, outMm);
            if (error < 0)
            {
                _out.WriteLine($"{file}: at {outMm,2:F0}mm out - flange does not reach, skipped");
                continue;
            }

            _out.WriteLine($"{file}: at {outMm,2:F0}mm out, mean disagreement with the drawn normals = {error:F1} deg");

            Assert.True(error < 25.0,
                $"{outMm}mm out the flange departs {error:F1} deg from the normals the view draws - " +
                "the launch is being flattened, most likely by the height propagation, the slope " +
                "ceiling, or a normal-follow distance shorter than the flange");
        }
    }

    /// <summary>
    /// Mean angle between the slope the flange actually leaves at and the slope the body's normal
    /// implies, sampled around the parting line. Vertices are matched in the FOOTPRINT so a neighbour
    /// along the rim can never be mistaken for one further out.
    /// </summary>
    private static double MeanSlopeDisagreementDeg(
        IMesh flange, IReadOnlyList<System.Numerics.Vector3> loop,
        IReadOnlyList<System.Numerics.Vector3> normals, System.Numerics.Vector3 axis, float outMm)
    {
        var centre = System.Numerics.Vector2.Zero;
        foreach (var q in loop) centre += PartingFrame.ToPlane(q, axis);
        centre /= loop.Count;

        var (bu, bv) = PartingFrame.Basis(axis);
        var fv = flange.Vertices;
        double sum = 0; int n = 0;

        for (int i = 0; i < loop.Count; i += Math.Max(1, loop.Count / 60))
        {
            var fp0 = PartingFrame.ToPlane(loop[i], axis);
            float h0 = System.Numerics.Vector3.Dot(loop[i], axis);

            var outward = fp0 - centre;
            if (outward.LengthSquared() < 1e-6f) continue;
            outward = System.Numerics.Vector2.Normalize(outward);

            var target = fp0 + (outward * outMm);
            float best = float.MaxValue, hAt = 0; bool found = false;
            foreach (var v in fv)
            {
                float d = System.Numerics.Vector2.DistanceSquared(PartingFrame.ToPlane(v, axis), target);
                if (d < best) { best = d; hAt = System.Numerics.Vector3.Dot(v, axis); found = true; }
            }
            if (!found || best > 9f) continue;

            float actual = (hAt - h0) / outMm;
            var nrm = normals[i];
            var inPlane = new System.Numerics.Vector2(
                System.Numerics.Vector3.Dot(nrm, bu), System.Numerics.Vector3.Dot(nrm, bv));
            if (inPlane.Length() < 1e-3f) continue;
            float wanted = System.Numerics.Vector3.Dot(nrm, axis) / inPlane.Length();

            sum += Math.Abs(Math.Atan(actual) - Math.Atan(wanted)) * 180 / Math.PI;
            n++;
        }

        // Negative means nothing was in range at this distance - a body whose flange simply does
        // not reach that far. The caller skips those rather than failing on them.
        return n == 0 ? -1 : sum / n;
    }

    private static double Volume(IMesh mesh)
    {
        var v = mesh.Vertices;
        var t = mesh.Triangles;
        double total = 0;
        for (int i = 0; i + 2 < t.Length; i += 3)
            total += System.Numerics.Vector3.Dot(
                v[t[i]], System.Numerics.Vector3.Cross(v[t[i + 1]], v[t[i + 2]])) / 6.0;
        return Math.Abs(total);
    }
}
