using System.Numerics;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using FluentAssertions;
using Xunit;

namespace Fabolus.Core.Tests.Features.PartingSplit;

/// <summary>
/// Guards <see cref="IPartingTools.GenerateMouldLoftFlangeMesh"/>'s mapping of the parting line round
/// the mould's outline. Every point's place on the outline comes from how far round the loop it is,
/// so the step from the last point back to the first has to be counted too: left out, the last point
/// is carried a full turn round to exactly where the first one sits, and the seam span between them
/// lofts out to an edge with no length.
/// </summary>
[Collection("GeometryEngine collection")]
public class MouldLoftFlangeTests
{
    private const float BodyRadius = 20f;
    private const int LinePoints = 120;

    private readonly IGeometryEngine _engine;

    public MouldLoftFlangeTests(GeometryEngineFixture fixture) => _engine = fixture.Engine;

    /// <summary>The body's equator, pull = +Y, wound whichever way is asked.</summary>
    private static List<Vector3> Equator(bool reversed)
    {
        var points = new List<Vector3>(LinePoints);
        for (int i = 0; i < LinePoints; i++)
        {
            double a = 2 * Math.PI * i / LinePoints;
            points.Add(new Vector3(BodyRadius * (float)Math.Cos(a), 0f, BodyRadius * (float)Math.Sin(a)));
        }

        if (reversed) points.Reverse();
        return points;
    }

    private static float ShortestEdge(IMesh mesh)
    {
        var v = mesh.Vertices;
        var t = mesh.Triangles;

        float shortest = float.MaxValue;
        for (int i = 0; i + 2 < t.Length; i += 3)
        {
            shortest = MathF.Min(shortest, Vector3.Distance(v[t[i]], v[t[i + 1]]));
            shortest = MathF.Min(shortest, Vector3.Distance(v[t[i + 1]], v[t[i + 2]]));
            shortest = MathF.Min(shortest, Vector3.Distance(v[t[i + 2]], v[t[i]]));
        }

        return shortest;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MouldLoft_SeamSpan_KeepsItsLength(bool reversed)
    {
        var body = _engine.Generators.GenerateSphere(Vector3.Zero, BodyRadius, 48).Value;
        var mould = _engine.Generators.GenerateSphere(Vector3.Zero, 35.0, 48).Value;

        var result = _engine.PartingTools.GenerateMouldLoftFlangeMesh(
            Equator(reversed), Vector3.UnitY, body, mould);
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Description : "");

        // Neighbouring points on the line sit about a millimetre apart and only spread further going
        // out, so nothing in a sound loft comes close to this. The seam span, mapped onto itself, was
        // exactly zero on the outermost ring.
        ShortestEdge(result.Value).Should().BeGreaterThan(0.3f,
            "every span of the line, the closing one included, should loft out to an edge with length");
    }
}
