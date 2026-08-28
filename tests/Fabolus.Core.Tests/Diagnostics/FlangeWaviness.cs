using System.Numerics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// How far the flange departs from the plane the two halves are meant to meet on, which is what reads
/// as waviness on a printed part. Slope is the angle between a face's normal and the pull axis: zero is
/// a face square to the pull, and anything past the printer's overhang limit is a face that needs
/// support on whichever half it ends up on.
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class FlangeWaviness
{
    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public FlangeWaviness(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    [Theory]
    [InlineData("chin.3mf")]
    [InlineData("nose.3mf")]
    [InlineData("scalp.3mf")]
    [InlineData("ear.3mf")]
    public void ReportSlopeAgainstEachSweep(string file)
    {
        var path = Path.Combine(Assets(), "3mf", file);
        if (!File.Exists(path)) { _out.WriteLine($"{file}: absent"); return; }

        var imported = _engine.IO.Import(path);
        var mould = MouldMesh.Create(imported.Value);
        var body = BodyMesh.Create(_engine, mould.Value).Value;

        var traced = _sut.GeneratePartingLineFromThickness(body);
        if (traced.IsFailure) { _out.WriteLine($"{file}: trace failed"); return; }

        _out.WriteLine($"--- {file}");

        foreach (var sweep in new[]
                 {
                     PartingMeshSweep.SurfaceSweep,     // what the view uses today
                     PartingMeshSweep.TangentLaunch,    // what the comment above MeshSweep argues for
                 })
        {
            Measure(sweep.ToString(), traced.Value, mould.Value, body,
                PartingMeshParameters.Default with { Sweep = sweep });
        }
    }

    private void Measure(
        string label, PartingLine line, MouldMesh mould, BodyMesh body, PartingMeshParameters wanted)
    {
        var resolved = PartingMeshFeature.ResolveAxis(line, wanted);
        if (resolved.IsFailure) { _out.WriteLine($"  {label,-13} axis failed"); return; }

        var contour = _sut.GenerateOuterContour(mould, resolved.Value);
        if (contour.IsFailure) { _out.WriteLine($"  {label,-13} contour failed"); return; }

        var flange = _sut.GenerateFlangeSurface(line, contour.Value, resolved.Value, body);
        if (flange.IsFailure) { _out.WriteLine($"  {label,-13} failed - {flange.Error.Description}"); return; }

        var axis = Vector3.Normalize(resolved.Value.Axis);
        var mesh = flange.Value;
        var v = mesh.Vertices;
        var t = mesh.Triangles;

        // Area-weighted, because a swarm of slivers should not outvote the face they sit on.
        var slopes = new List<(float Deg, float Area)>(t.Length / 3);
        float totalArea = 0f;

        for (int f = 0; f < t.Length; f += 3)
        {
            var a = v[t[f]];
            var b = v[t[f + 1]];
            var c = v[t[f + 2]];

            var cross = Vector3.Cross(b - a, c - a);
            float length = cross.Length();
            if (length < 1e-12f) continue;

            float area = length * 0.5f;
            float deg = MathF.Acos(Math.Clamp(MathF.Abs(Vector3.Dot(cross / length, axis)), 0f, 1f))
                        * 180f / MathF.PI;

            slopes.Add((deg, area));
            totalArea += area;
        }

        if (totalArea <= 0f) { _out.WriteLine($"  {label,-13} no area"); return; }

        slopes.Sort((x, y) => x.Deg.CompareTo(y.Deg));

        float Percentile(float share)
        {
            float running = 0f;
            foreach (var (deg, area) in slopes)
            {
                running += area;
                if (running >= totalArea * share) return deg;
            }
            return slopes[^1].Deg;
        }

        float Beyond(float limit)
        {
            float running = 0f;
            foreach (var (deg, area) in slopes) if (deg > limit) running += area;
            return running / totalArea * 100f;
        }

        // The flange only earns a flatter mating face if it still seals: every rim point outside the
        // body is a hairline bridge the cut leaves behind, holding the mould in one piece.
        var seal = _sut.InspectFlangeSeal(mesh, body, line, resolved.Value);
        string sealed_ = seal.IsSuccess
            ? $"breached={seal.Value.Count(p => !p.IsSealed),3}/{seal.Value.Count,4}"
            : "seal unknown";

        _out.WriteLine(
            $"  {label,-13} slope: median={Percentile(0.5f),5:F1}  p90={Percentile(0.9f),5:F1}  " +
            $"p99={Percentile(0.99f),5:F1}  max={slopes[^1].Deg,5:F1} deg   " +
            $"area past 40deg={Beyond(40f),5:F1}%  past 60deg={Beyond(60f),5:F1}%  " +
            $"{sealed_}  tris={t.Length / 3}");
    }

    private static string Assets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return dir is null ? "" : Path.Combine(dir.FullName, "tests", "files");
    }
}
