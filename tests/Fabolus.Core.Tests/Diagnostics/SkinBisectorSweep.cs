using System.Collections.Immutable;
using System.Diagnostics;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// <see cref="PartingMeshSweep.SkinBisector"/> on every saved mould, through the view's own cut
/// (extrude 0.1 mm along the normals, subtract, pieces by size): how long the surface takes, whether
/// it crosses itself, how it sits against the axis, and what the cut leaves. Pictures of the surface
/// and the halves under <c>FABOLUS_TILT_IMAGES</c>.
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class SkinBisectorSweep
{
    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public SkinBisectorSweep(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    [DiagnosticTheory]
    [InlineData("3mf/larynx_large.3mf")]
    [InlineData("3mf/larynx_small.3mf")]
    [InlineData("3mf/chin.3mf")]
    [InlineData("3mf/nose.3mf")]
    [InlineData("3mf/eye.3mf")]
    [InlineData("3mf/ear.3mf")]
    [InlineData("3mf/scalp.3mf")]
    [InlineData("3mf/test bolus standard.3mf")]
    public void Measure(string file)
    {
        var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
        var body = _sut.GetBodyMesh(mould).Value;
        var line = _sut.GeneratePartingLineFromThickness(body).Value;
        var resolved = PartingMeshFeature.ResolveAxis(line, Recipe).Value;
        var axis = resolved.Axis;
        var contour = _sut.GenerateOuterContour(mould, resolved).Value;

        var clock = Stopwatch.StartNew();
        var built = _sut.GenerateFlangeSurface(line, contour, resolved, body);
        long surfaceMs = clock.ElapsedMilliseconds;
        if (built.IsFailure) { _out.WriteLine($"{file}: {built.Error.Description} ({surfaceMs} ms)"); return; }
        var surface = built.Value;
        int crossings = _engine.Evaluators.CountSelfIntersections(surface).Value;

        // How it sits against the axis, over the part of it in the mould's material - outside the body,
        // inside the mould - since that is the part the halves meet on.
        var bodyIndex = _engine.Spatial.IndexFor(body.Mesh).Value;
        var mouldIndex = _engine.Spatial.IndexFor(mould.Mesh).Value;
        var fv = surface.Vertices;
        var ft = surface.Triangles;
        var inMaterial = new List<int>();
        var faces = new List<(double Area, double Cos)>();
        for (int f = 0; f + 2 < ft.Length; f += 3)
        {
            var c = (fv[ft[f]] + fv[ft[f + 1]] + fv[ft[f + 2]]) / 3.0;
            if (bodyIndex.SignedDistance(c) < 0.5 || mouldIndex.SignedDistance(c) > 0) continue;
            inMaterial.Add(ft[f]); inMaterial.Add(ft[f + 1]); inMaterial.Add(ft[f + 2]);
            var n = (fv[ft[f + 1]] - fv[ft[f]]).Cross(fv[ft[f + 2]] - fv[ft[f]]);
            if (n.Length < 1e-12) continue;
            faces.Add((n.Length / 2, n.Normalize().Dot(axis)));
        }
        double total = faces.Sum(x => x.Area);
        double sign = faces.Sum(x => x.Area * x.Cos) >= 0 ? 1 : -1;
        double against = faces.Where(x => sign * x.Cos < -0.1).Sum(x => x.Area) / Math.Max(total, 1e-9);
        var tilt = faces.Select(x => Math.Acos(Math.Min(1, Math.Abs(x.Cos))) * 180 / Math.PI).OrderBy(x => x).ToArray();

        clock.Restart();
        var cutter = _sut.ExtrudeFlange(surface, resolved);
        var cut = cutter.IsSuccess ? _engine.Booleans.Subtract(mould.Mesh, cutter.Value) : cutter;
        var parts = cut.IsSuccess
            ? _engine.Evaluators.SeparateComponents(cut.Value).Value.Select(Volume).OrderByDescending(v => v).ToArray()
            : [];
        long cutMs = clock.ElapsedMilliseconds;
        var split = cutter.IsSuccess ? _sut.SplitMould(mould, cutter.Value, line, axis) : cutter.Error;

        double whole = Volume(mould.Mesh);
        _out.WriteLine(
            $"{file}: surface {surface.TriangleCount}t in {surfaceMs} ms, {crossings} crossings; in the mould's material {total:F0} mm², " +
            $"median {(tilt.Length > 0 ? tilt[tilt.Length / 2] : 0):F0}° off level, {100 * against:F0}% facing against the axis | " +
            $"cut {parts.Length} piece(s) {string.Join(" ", parts.Take(3).Select(v => $"{100 * v / whole:F1}%"))} ({cutMs} ms)");

        var directory = Environment.GetEnvironmentVariable("FABOLUS_TILT_IMAGES");
        if (string.IsNullOrWhiteSpace(directory)) return;

        const int size = 420;
        var options = RenderOptions.Default;
        var lineColour = new Rgb(80, 220, 255);
        var shown = Compact(fv, inMaterial);
        var tiles = new List<Tile>();
        foreach (var view in new[] { Views.Front, Views.Left, Views.Top, Views.OrbitA, Views.OrbitB })
        {
            var camera = Camera.Fit(mould.Mesh, view, size, size);
            var raster = MeshRasterizer.Render(shown, camera, size, size, options);
            foreach (var loop in line.Loops) MeshRasterizer.DrawPolyline(raster, camera, loop, true, lineColour, options);
            tiles.Add(new Tile($"surface {view.Name}", raster));
        }
        if (split.IsSuccess)
            foreach (var (name, half) in new[] { ("positive", split.Value.Positive), ("negative", split.Value.Negative) })
                foreach (var view in new[] { Views.OrbitA, Views.OrbitB })
                    tiles.Add(new Tile($"{name} {view.Name}", MeshRasterizer.Render(half, Camera.Fit(mould.Mesh, view, size, size), size, size, options)));

        var title = Path.GetFileNameWithoutExtension(file);
        var path = Path.Combine(directory, $"{title}-skin-bisector.png");
        ContactSheet.Save(path, tiles, 4, size, options.Background, $"{title}: skin bisector (surface shown inside the mould's material)", [("parting line", lineColour)]);
        _out.WriteLine($"  {path}");
    }

    private static PartingMeshParameters Recipe => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = PartingMeshSweep.SkinBisector,
        SplitMethod = PartingSplitMethod.SeveredComponents,
        Thickening = PartingMeshThickening.Extrude,
        Depth = 0.1,
    };

    private IMesh Compact(IReadOnlyList<Vector3> vertices, List<int> triangles)
    {
        var map = new Dictionary<int, int>();
        var outV = ImmutableArray.CreateBuilder<Vector3>();
        var outT = ImmutableArray.CreateBuilder<int>(triangles.Count);
        foreach (int v in triangles)
        {
            if (!map.TryGetValue(v, out int k)) { k = map[v] = outV.Count; outV.Add(vertices[v]); }
            outT.Add(k);
        }
        return _engine.CreateMesh(outV.ToImmutable(), outT.MoveToImmutable(), MeshMetadata.Named("shown")).Value;
    }

    private static double Volume(IMesh mesh)
    {
        var vs = mesh.Vertices;
        var ts = mesh.Triangles;
        double total = 0;
        for (int i = 0; i + 2 < ts.Length; i += 3) total += vs[ts[i]].Dot(vs[ts[i + 1]].Cross(vs[ts[i + 2]])) / 6.0;
        return Math.Abs(total);
    }

    private static string Asset(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return Path.Combine(dir!.FullName, "tests", "files", name);
    }
}
