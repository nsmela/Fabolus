using Fabolus.Core.Features.MeshIO;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Tests.Fixtures;
using Xunit;
using Xunit.Abstractions;

namespace Fabolus.Core.Tests.Diagnostics;

/// <summary>
/// Does putting a line into editable form keep every loop it had? PartingSplitViewModel.SelectedLine
/// swaps the whole traced line for the edit the moment anything is edited, so a loop the edit does not
/// carry is a loop the parting mesh stops being built from.
/// </summary>
[Collection("GeometryEngine collection")]
[Trait("Category", "Diagnostics")]
public class EditLoopCoverage
{
    private readonly IGeometryEngine _engine;
    private readonly PartingMeshFeature _sut;
    private readonly ITestOutputHelper _out;

    public EditLoopCoverage(GeometryEngineFixture fixture, ITestOutputHelper output)
    {
        _engine = fixture.Engine;
        _sut = new PartingMeshFeature(_engine);
        _out = output;
    }

    [Theory]
    [InlineData("chin.3mf")]
    [InlineData("ear.3mf")]
    [InlineData("eye.3mf")]
    [InlineData("nose.3mf")]
    [InlineData("scalp.3mf")]
    [InlineData("larynx_large.3mf")]
    [InlineData("larynx_small.3mf")]
    [InlineData("test bolus standard.3mf")]
    public void ReportLoopsKeptByTheEdit(string file)
    {
        var path = Path.Combine(Assets(), "3mf", file);
        if (!File.Exists(path)) { _out.WriteLine($"{file}: absent"); return; }

        var imported = _engine.IO.Import(path);
        if (imported.IsFailure) { _out.WriteLine($"{file}: import failed"); return; }

        // The assets are saved moulds; the body is recovered by replaying the history back to before
        // the Mould command, which is the route the view takes.
        var mould = MouldMesh.Create(imported.Value);
        if (mould.IsFailure) { _out.WriteLine($"{file}: not a mould - {mould.Error.Description}"); return; }

        var made = BodyMesh.Create(_engine, mould.Value);
        if (made.IsFailure) { _out.WriteLine($"{file}: body failed - {made.Error.Description}"); return; }

        var body = made.Value;

        var traced = _sut.GeneratePartingLineFromThickness(body);
        if (traced.IsFailure) { _out.WriteLine($"{file}: trace failed - {traced.Error.Description}"); return; }

        int loops = traced.Value.Loops.Count;

        var edit = _sut.BeginPartingLineEdit(body, traced.Value);
        if (edit.IsFailure)
        {
            _out.WriteLine($"{file}: loops={loops}  edit refused ({edit.Error.Code}) - editing not offered");
            return;
        }

        int kept = edit.Value.ToPartingLine().Loops.Count;
        _out.WriteLine(
            $"{file}: traced loops={loops}  rims={edit.Value.Rims.Count}  loops after edit={kept}" +
            (kept == loops ? "" : $"   <-- {loops - kept} LOOP(S) DROPPED"));
    }

    private static string Assets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "files")))
            dir = dir.Parent;

        return dir is null ? "" : Path.Combine(dir.FullName, "tests", "files");
    }
}
