using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Tests.Diagnostics;

/// <summary>
/// What the relaxation stages actually buy, now that the parting line comes from the crease offset
/// rather than from the traced seam.
///
/// <para>
/// Here because <c>HoldingTheLineOnTheBodyDoesNotDisableTheSmoothing</c> began failing when the source
/// changed, and the useful question is not whether it fails but which statistic moved. That test reads
/// the <em>median</em> turn, which was the right measure for a traced seam: the trace arrives as a
/// staircase of triangle edges, so every sample is kinked and relaxing it moves the whole distribution.
/// The offset line does not arrive that way - it is walked at an even spacing across a smooth field -
/// so if the stages still earn their place it will show in the tail rather than in the middle.
/// </para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class SmoothingContribution
{
    private readonly GeometryEngineFixture _assets;
    private readonly IGeometryEngine _engine;
    private readonly ITestOutputHelper _log;

    public SmoothingContribution(GeometryEngineFixture assets, ITestOutputHelper log)
    {
        _assets = assets;
        _engine = assets.Engine;
        _log = log;
    }

    [DiagnosticFact]
    public void WhichEndOfTheTurnDistributionTheSmoothingMoves()
    {
        var feature = new PartingMeshFeature(_engine);

        foreach (string file in PartingLineCentringSweep.Bodies)
        {
            var body = BodyMesh.Create(_assets.LoadStl(file)).Value;

            var smoothed = feature.GeneratePartingLineFromThickness(body);
            var raw = feature.GeneratePartingLineFromThickness(
                body, ThicknessPartingOptions.Default with { SmoothingPasses = 0 });

            if (smoothed.IsFailure || raw.IsFailure)
            {
                _log.WriteLine($"{file}: no line");
                continue;
            }

            _log.WriteLine($"=== {Path.GetFileNameWithoutExtension(file)}");
            _log.WriteLine($"  raw       {Turns(raw.Value.Loops[0])}");
            _log.WriteLine($"  smoothed  {Turns(smoothed.Value.Loops[0])}");
        }
    }

    private static string Turns(IReadOnlyList<Vector3> loop)
    {
        int n = loop.Count;
        var turns = new double[n];
        var steps = new double[n];

        for (int i = 0; i < n; i++)
        {
            var incoming = loop[i] - loop[((i - 1) % n + n) % n];
            var outgoing = loop[(i + 1) % n] - loop[i];
            steps[i] = outgoing.Length;

            if (incoming.LengthSquared < 1e-12 || outgoing.LengthSquared < 1e-12) continue;
            turns[i] = Math.Acos(Math.Clamp(
                incoming.Normalize().Dot(outgoing.Normalize()), -1.0, 1.0))
                * 180.0 / Math.PI;
        }

        Array.Sort(turns);
        var sortedSteps = (double[])steps.Clone();
        Array.Sort(sortedSteps);

        return $"n {n,4}  turn med {turns[n / 2],5:F2}  p95 {turns[(int)(n * 0.95)],5:F1}  " +
               $"max {turns[^1],5:F1}  |  step spread {sortedSteps[^1] / sortedSteps[n / 2],4:F1}x";
    }
}
