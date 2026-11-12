using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Diagnostics;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// Which line a split committed from the recipe is cut along, once the line has been edited: drawn,
/// because "the recipe carries the edit" and "the flange follows the edit" are different claims and
/// only the second is what the user is owed.
///
/// <para>
/// For each body the line is traced, smoothed as the view's Smooth button would, and put in a recipe.
/// The flange is then built twice from recipes alone - one holding the edit, one not, which is all a
/// recipe could hold before - and each is drawn with both lines over it, close in on the place the
/// edit moved the line furthest. The flange's inner edge should lie on the edited line in the first
/// and on the traced line in the second.
/// </para>
///
/// <para>Set <c>FABOLUS_SCORECARD_DIR</c> for the pictures; the distances are printed either way.</para>
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class EditedLineCommit
{
    private const int Size = 620;
    private static readonly RenderOptions Options = RenderOptions.Default;
    private static readonly Rgb TracedColour = new(255, 170, 60);
    private static readonly Rgb EditedColour = new(80, 220, 255);
    private static readonly Rgb Moved = new(230, 60, 220);

    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public EditedLineCommit(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    private static PartingLineParameters Traced => new()
    {
        Source = PartingLineSource.ExtrusionBorder,
        PullDirection = Vector3.UnitY,
    };

    private static PartingMeshParameters Recipe => PartingMeshParameters.Default with
    {
        AxisSource = PartingMeshAxisSource.PartingLine,
        Sweep = PartingMeshSweep.MouldLoft,
        Depth = PartingMeshParameters.DefaultDepth,
        Axis = Vector3.UnitY,
    };

    [DiagnosticTheory]
    [InlineData("chin.3mf")]
    [InlineData("scalp.3mf")]
    [InlineData("test bolus standard.3mf")]
    public void WhichLineTheCommittedFlangeFollows(string file)
    {
        string id = Path.GetFileNameWithoutExtension(file).Replace(' ', '_');
        var mould = SavedMoulds.Mould(SavedMoulds.Open(_engine, Asset(file)).Value).Value;
        var body = _sut.GetBodyMesh(mould).Value;

        var traced = _sut.GeneratePartingLineFromBody(body, Traced).Value;
        var edit = _sut.BeginPartingLineEdit(body, traced).Value;
        for (int rim = 0; rim < edit.Rims.Count; rim++)
            edit = edit.With(rim, PartingLineEditor.Smooth(edit.Rims[rim].Line, edit.Rims[rim].Graph));
        var edited = edit.ToPartingLine();

        // Where the edit moved the line furthest, which is where the two flanges will differ most.
        var furthest = edited.Loops[0]
            .Select(p => (Point: p, Off: traced.Loops[0].Min(q => p.DistanceTo(q))))
            .OrderByDescending(x => x.Off)
            .First();
        _out.WriteLine($"{id}: smoothing moved the line by up to {furthest.Off:F2} mm");

        var tiles = new List<Tile>();
        foreach (var (label, recipe) in new[]
                 {
                     ("recipe with the edit", Traced with { Edited = EditedPartingLine.From(edited) }),
                     ("recipe without it", Traced),
                 })
        {
            // From the recipe alone, as Apply and a replay build it.
            var line = _sut.GeneratePartingLine(mould, recipe).Value;
            var resolved = PartingMeshFeature.ResolveAxis(line, Recipe).Value;
            var contour = _sut.GenerateOuterContour(mould, resolved).Value;
            var flange = _sut.GenerateFlangeSurface(line, contour, resolved, body).Value;

            double toEdited = Nearest(flange, edited.Loops[0]);
            double toTraced = Nearest(flange, traced.Loops[0]);
            _out.WriteLine(
                $"  {label}: the flange comes within {toEdited:F3} mm of every point of the edited line, " +
                $"{toTraced:F3} mm of the traced one");

            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("FABOLUS_SCORECARD_DIR"))) continue;

            var along = new View("down the axis", -resolved.Axis);
            foreach (var (name, camera) in new[]
                     {
                         ("whole", Camera.Fit(body.Mesh, along, Size, Size)),
                         ("where the edit moved it most", Camera.Focus(body.Mesh, along, furthest.Point, 9.0, Size, Size)),
                     })
            {
                var raster = MeshRasterizer.Render(flange, camera, Size, Size, Options);
                MeshRasterizer.DrawPolyline(raster, camera, traced.Loops[0], true, TracedColour, Options);
                MeshRasterizer.DrawPolyline(raster, camera, edited.Loops[0], true, EditedColour, Options);
                MeshRasterizer.DrawMarker(raster, camera, furthest.Point, Moved, Options, 4);
                tiles.Add(new Tile($"{label}: {name}", raster, $"edited {toEdited:F2} / traced {toTraced:F2} mm"));
            }
        }

        string? directory = Environment.GetEnvironmentVariable("FABOLUS_SCORECARD_DIR");
        if (string.IsNullOrWhiteSpace(directory)) return;

        ContactSheet.Save(Path.Combine(directory, id + "-edited-line.png"), tiles, 2, Size, Options.Background,
            $"{id}: the flange a recipe rebuilds, with and without the edit in it",
            [("traced line", TracedColour), ("edited line", EditedColour), ("moved furthest", Moved)]);
    }

    /// <summary>
    /// The furthest any point of <paramref name="loop"/> lies from the flange's nearest vertex: small
    /// when the flange was swept from that loop, since the loop is one of its rings.
    /// </summary>
    private static double Nearest(IMesh flange, IReadOnlyList<Vector3> loop) =>
        loop.Max(p => flange.Vertices.Min(v => v.DistanceTo(p)));

    private static string Asset(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return Path.Combine(dir!.FullName, "tests", "files", "3mf", name);
    }
}
