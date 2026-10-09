using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.PartingSplit;
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
        var engine = global::GeometryEngine.BspGeometryEngine.Create();

        var imported = engine.IO.Import(AssetPath(file));
        Assert.True(imported.IsSuccess, imported.IsFailure ? imported.Error.Description : "");

        // Recorded as its own base, as an import does, so the mould's history can be replayed back
        // to the body the line is traced on.
        var record = MeshRecord.ForImport(file).WithBaseMesh(imported.Value);
        var workspace = Workspace.CreateEmpty().AddMesh(imported.Value, record).Value;
        var bodyId = record.Id;

        var mould = new GenerateMould(engine).Execute(
            workspace, bodyId, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = bodyId });
        Assert.True(mould.IsSuccess, mould.IsFailure ? mould.Error.Description : "");

        var validated = MouldMesh.Create(mould.Value.GetMesh(bodyId).Value, mould.Value.GetRecord(bodyId).Value);
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
        var crossings = engine.Evaluators.CountSelfIntersections(cutter.Value).Value;
        _out.WriteLine($"{file}: cutter selfInt={crossings} " +
                       $"tris={cutter.Value.Triangles.Length / 3} watertight={topology.Value.IsWatertight}");

        Assert.True(topology.Value.IsWatertight, "the boolean needs a closed cutter");
        Assert.Equal(0, crossings);

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
        Assert.Equal(0.1, p.Depth);
        Assert.Equal(PartingMeshSweep.MouldLoft, p.Sweep);
        Assert.Equal(PartingSplitMethod.SeveredComponents, p.SplitMethod);
        Assert.Equal(PartingMeshAxisSource.PartingLine, p.AxisSource);
        Assert.Equal(PartingLineSource.ExtrusionBorder, Recipe().LineParameters.Source);
    }

    /// <summary>
    /// The flange the view builds, on the body it is traced on, with the recipe resolved as the view
    /// resolves it - what the two tests below measure.
    /// </summary>
    private static (IMesh Flange, PartingLine Line, BodyMesh Body, PartingMeshParameters Parameters, PartingMeshFeature Feature)
        ViewsFlange(string file, bool loft = true)
    {
        var engine = global::GeometryEngine.BspGeometryEngine.Create();
        var imported = engine.IO.Import(AssetPath(file));
        Assert.True(imported.IsSuccess);

        var record = MeshRecord.ForImport(file).WithBaseMesh(imported.Value);
        var workspace = Workspace.CreateEmpty().AddMesh(imported.Value, record).Value;
        var mould = new GenerateMould(engine).Execute(
            workspace, record.Id, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = record.Id });
        var validated = MouldMesh.Create(mould.Value.GetMesh(record.Id).Value, mould.Value.GetRecord(record.Id).Value).Value;

        var view = Recipe();
        view.UseMouldLoft = loft;
        var feature = new PartingMeshFeature(engine);
        var body = feature.GetBodyMesh(validated).Value;
        var line = feature.GeneratePartingLineFromBody(body, view.LineParameters).Value;
        var parameters = PartingMeshFeature.ResolveAxis(line, view.MeshParameters).Value;
        var contour = feature.GenerateOuterContour(validated, parameters).Value;
        var flange = feature.GenerateFlangeSurface(line, contour, parameters, body);
        Assert.True(flange.IsSuccess, flange.IsFailure ? flange.Error.Description : "");

        return (flange.Value, line, body, parameters, feature);
    }

    /// <summary>
    /// What the loft is for: a mating face that lies close to the parting plane rather than carrying
    /// the body's undulation out to the mould's wall. Measured as each face's angle off that plane -
    /// the median, area-weighted so a swarm of slivers cannot outvote the face they sit on - and held
    /// both against the marching sweep beside it, which it has to beat, and against the 40 degrees the
    /// parameters already declare as the flange's slope ceiling, which it has to keep under.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void TheMatingFaceLiesCloseToThePartingPlane(string file)
    {
        double lofted = MedianSlopeDeg(ViewsFlange(file, loft: true));
        double marched = MedianSlopeDeg(ViewsFlange(file, loft: false));
        _out.WriteLine($"{file}: median face off the parting plane - loft {lofted:F1} deg, marching sweep {marched:F1} deg");

        Assert.True(lofted < marched, $"the loft ({lofted:F1} deg) is no flatter than the sweep it replaced ({marched:F1} deg)");
        Assert.True(lofted < 40, $"the median face is {lofted:F1} deg off the parting plane");
    }

    private static double MedianSlopeDeg(
        (IMesh Flange, PartingLine Line, BodyMesh Body, PartingMeshParameters Parameters, PartingMeshFeature Feature) built)
    {
        var (flange, _, _, parameters, _) = built;
        var axis = parameters.Axis.Normalize();
        var vertices = flange.Vertices;
        var triangles = flange.Triangles;

        var slopes = new List<(double Degrees, double Area)>(triangles.Length / 3);
        double total = 0;
        for (int f = 0; f + 2 < triangles.Length; f += 3)
        {
            var (a, b, c) = (vertices[triangles[f]], vertices[triangles[f + 1]], vertices[triangles[f + 2]]);
            var cross = (b - a).Cross(c - a);
            if (cross.Length < 1e-12) continue;

            double area = cross.Length / 2;
            double degrees = Math.Acos(Math.Clamp(Math.Abs((cross / cross.Length).Dot(axis)), 0, 1)) * 180 / Math.PI;
            slopes.Add((degrees, area));
            total += area;
        }

        slopes.Sort((x, y) => x.Degrees.CompareTo(y.Degrees));
        double running = 0, median = slopes[^1].Degrees;
        foreach (var (degrees, area) in slopes)
        {
            running += area;
            if (running >= total / 2) { median = degrees; break; }
        }

        return median;
    }

    /// <summary>
    /// And it still has to seal. A flatter face is only worth having if the flange still meets the
    /// body inside it all the way round: a rim point left outside is a hairline bridge of mould the
    /// cut leaves standing, holding the halves together.
    /// </summary>
    [Theory]
    [InlineData("chin_bolus.stl")]
    [InlineData("scalp_bolus.stl")]
    [InlineData("nose_bolus.stl")]
    public void TheFlangeStillSealsIntoTheBody(string file)
    {
        var (flange, line, body, parameters, feature) = ViewsFlange(file);

        var seal = feature.InspectFlangeSeal(flange, body, line, parameters);
        Assert.True(seal.IsSuccess, seal.IsFailure ? seal.Error.Description : "");

        int breached = seal.Value.Count(p => !p.IsSealed);
        _out.WriteLine($"{file}: {breached} of {seal.Value.Count} rim points outside the body");
        Assert.True(seal.Value.Count > 0, "no rim was found to inspect");
        Assert.Equal(0, breached);
    }

    private static double Volume(IMesh mesh)
    {
        var v = mesh.Vertices;
        var t = mesh.Triangles;
        double total = 0;
        for (int i = 0; i + 2 < t.Length; i += 3)
            total += v[t[i]].Dot(v[t[i + 1]].Cross(v[t[i + 2]])) / 6.0;
        return Math.Abs(total);
    }
}
