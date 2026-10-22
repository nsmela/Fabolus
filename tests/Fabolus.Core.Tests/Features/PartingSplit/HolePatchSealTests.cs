using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// A body with a hole through it is parted by two surfaces: the flange round its outer rim, and a patch
/// across the hole. The mould fills the hole, so the patch has to reach into the body all round to cut
/// that material free. It used to end exactly on the hole's parting loop - on the body's surface - and
/// on larynx_small, the first ring whose two rims are both found, that left the mould in one piece on
/// every opening tried, where carrying the edge a millimetre into the body split it 53/47 on all six.
/// </summary>
[Collection("GeometryEngine collection")]
public class HolePatchSealTests
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;

    public HolePatchSealTests(GeometryEngineFixture assets)
    {
        _assets = assets;
        _engine = assets.Engine;
    }

    [Fact]
    public void HolePatch_GivenTheBody_EndsInsideItAllRound()
    {
        var feature = new PartingMeshFeature(_engine);
        var mould = Mould("3mf/larynx_small.3mf");
        var body = feature.GetBodyMesh(mould).Value;
        var line = feature.GeneratePartingLineFromThickness(body).Value;
        line.Loops.Should().HaveCount(2, "larynx_small is a ring: an outer rim and a rim round the hole");
        var axis = PartingMeshFeature.ResolveAxis(line, ViewRecipe).Value.Axis;

        var patch = new PartingTools(_engine).GenerateHolePatch(line.Loops[1], axis, body.Mesh);

        patch.IsSuccess.Should().BeTrue(patch.IsFailure ? patch.Error.Description : "");
        using var index = _engine.Spatial.IndexFor(body.Mesh).Value;
        var outside = Edge(patch.Value).Select(v => index.SignedDistance(v)).Where(d => d >= 0.0).ToList();
        outside.Should().BeEmpty("every edge point outside the body is a film of mould left joined round the hole");
    }

    [Fact]
    public void Ring_CutWithTheViewRecipe_ComesApartInTwo()
    {
        var mould = Mould("3mf/larynx_small.3mf");

        var cut = new PartingMeshFeature(_engine).CutMould(mould, LineRecipe, ViewRecipe);
        cut.IsSuccess.Should().BeTrue(cut.IsFailure ? cut.Error.Description : "");

        var pieces = _engine.Evaluators.SeparateComponents(cut.Value).Value
            .Select(Volume).OrderByDescending(v => v).ToArray();
        double total = pieces.Sum();
        pieces.Length.Should().BeGreaterThanOrEqualTo(2);
        (pieces[1] / total).Should().BeGreaterThan(0.3, "the mould should part into two halves, not shed specks");
    }

    private MouldMesh Mould(string asset)
    {
        var opened = SavedMoulds.Open(_engine, _assets.GetAssetPath(asset));
        opened.IsSuccess.Should().BeTrue();
        return SavedMoulds.Mould(opened.Value).Value;
    }

    /// <summary>The patch's boundary: vertices on an edge only one of its faces uses.</summary>
    private static IEnumerable<Vector3> Edge(IMesh patch)
    {
        var uses = new Dictionary<(int, int), int>();
        var ts = patch.Triangles;
        for (int f = 0; f + 2 < ts.Length; f += 3)
        for (int e = 0; e < 3; e++)
        {
            int a = ts[f + e], b = ts[f + ((e + 1) % 3)];
            var key = a < b ? (a, b) : (b, a);
            uses[key] = uses.GetValueOrDefault(key) + 1;
        }
        return uses.Where(u => u.Value == 1).SelectMany(u => new[] { u.Key.Item1, u.Key.Item2 })
            .Distinct().Select(v => patch.Vertices[v]);
    }

    private static double Volume(IMesh mesh)
    {
        var vs = mesh.Vertices;
        var ts = mesh.Triangles;
        double total = 0;
        for (int i = 0; i + 2 < ts.Length; i += 3) total += vs[ts[i]].Dot(vs[ts[i + 1]].Cross(vs[ts[i + 2]])) / 6.0;
        return Math.Abs(total);
    }

    private static PartingLineParameters LineRecipe => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
        NeutralBand = PartingNeutralBand.FromDegrees(-5.0, 5.0),
    };

    /// <summary>The parting view's recipe for new work.</summary>
    private static PartingMeshParameters ViewRecipe => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = PartingMeshSweep.MouldLoft,
        SplitMethod = PartingSplitMethod.SeveredComponents,
        Thickening = PartingMeshThickening.Extrude,
        Depth = 0.1,
        FlangeMaxSlopeDeg = 80.0,
        NormalFollowMm = 100.0,
        RawFlange = false,
        NormalSmoothingPasses = 5,
        Axis = Vector3.UnitY,
    };
}
