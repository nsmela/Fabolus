using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// The wall a line is edited within has to contain the line it was handed. Every edit re-snaps the
/// points it carries across to that wall, so a line point off it is moved millimetres sideways by the
/// first edit, whatever the edit was.
///
/// <para>
/// On a GeometryEngine-smoothed body the ridge's band can miss stretches of the wall. On these saved
/// moulds the traced line, centred between its creases, ran up to 7mm over faces with no band face
/// beside them, and smoothing, dividing and dragging all went wrong from there.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
public class PartingLineEditWallTests
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;

    public PartingLineEditWallTests(GeometryEngineFixture assets)
    {
        _assets = assets;
        _engine = assets.Engine;
    }

    [Theory]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/larynx_large.3mf")]
    [InlineData("3mf/eye.3mf")]
    public void TheTracedLine_StartsOnTheWallItIsEditedWithin(string asset)
    {
        var feature = new PartingMeshFeature(_engine);

        var opened = SavedMoulds.Open(_engine, _assets.GetAssetPath(asset));
        opened.IsSuccess.Should().BeTrue();
        var body = feature.GetBodyMesh(SavedMoulds.Mould(opened.Value).Value).Value;

        var line = feature.GeneratePartingLineFromThickness(body);
        line.IsSuccess.Should().BeTrue(line.IsFailure ? line.Error.Description : "");

        var edit = feature.BeginPartingLineEdit(body, line.Value);
        edit.IsSuccess.Should().BeTrue(edit.IsFailure ? edit.Error.Description : "");

        foreach (var rim in edit.Value.Rims)
        {
            double worst = rim.Line.Flatten().Max(p => p.DistanceTo(rim.Graph.Snap(p)));
            worst.Should().BeLessThan(rim.Graph.MeanEdge * 0.05,
                "a point of the line it was handed is off the wall the editor confines it to");
        }
    }
}
