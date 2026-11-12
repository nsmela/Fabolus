using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.PartingSplit;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.PartingSplit;

/// <summary>
/// The line a split is committed along has to be the line the user was shown.
///
/// <para>
/// It was not, once the line had been touched. A drag, an added handle or a press of Smooth changed
/// the line on screen and every stage built from it - the parting mesh in step two, the halves in step
/// three - but Apply Split replays the recipe, and the recipe carried a source and a direction and
/// nothing about the edit. So Apply traced the automatic line again and cut along that: the user
/// reviewed one split and was handed another, with nothing to say so.
/// </para>
///
/// <para>
/// Driven through the view model with the real engine, because the gap was between the two and neither
/// shows it alone: the feature did what the recipe said, and the view model previewed what was on
/// screen.
/// </para>
/// </summary>
public class PartingLineEditCommitTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void AfterAnEdit_TheRecipeRebuildsTheLineThePreviewWasBuiltFrom() => UiThread.Run(async () =>
    {
        var (vm, mould) = await OpenedOn("chin_bolus.stl");

        vm.SmoothLineCommand.Execute(null);
        await vm.NextStateCommand.ExecuteAsync(null);
        Assert.NotNull(vm.PartingLine);

        // What Apply Split and every later replay will cut along: the recipe, and nothing else.
        var committed = new PartingMeshFeature(Engine).GeneratePartingLine(mould, vm.LineParameters);
        Assert.True(committed.IsSuccess, committed.IsFailure ? committed.Error.Description : "");

        AssertSameLine(vm.PartingLine!, committed.Value);

        // And the edit did move the line, or the two agreeing would prove nothing.
        var traced = new PartingMeshFeature(Engine).GeneratePartingLine(mould, vm.LineParameters with { Edited = null });
        Assert.True(Furthest(vm.PartingLine!, traced.Value) > 0.05, "smoothing should have moved the line off the trace");
    });

    [Fact]
    public void WithNoEdit_TheRecipeCarriesNone_AndStillRebuildsThePreviewedLine() => UiThread.Run(async () =>
    {
        var (vm, mould) = await OpenedOn("chin_bolus.stl");

        await vm.NextStateCommand.ExecuteAsync(null);

        Assert.Null(vm.LineParameters.Edited);
        var committed = new PartingMeshFeature(Engine).GeneratePartingLine(mould, vm.LineParameters);
        AssertSameLine(vm.PartingLine!, committed.Value);
    });

    [Fact]
    public void ResettingTheEdits_TakesThemOutOfTheRecipe() => UiThread.Run(async () =>
    {
        var (vm, _) = await OpenedOn("chin_bolus.stl");

        vm.SmoothLineCommand.Execute(null);
        Assert.NotNull(vm.LineParameters.Edited);

        vm.ResetLineEditsCommand.Execute(null);
        Assert.Null(vm.LineParameters.Edited);
    });

    /// <summary>
    /// An edit belongs to the body it was made on. Opening the view on a different mould used to leave
    /// the previous body's edited line in force, because nothing dropped it and a line with edits in
    /// hand is deliberately never rebuilt.
    /// </summary>
    [Fact]
    public void OpeningADifferentMould_DropsTheEditMadeOnTheLastOne() => UiThread.Run(async () =>
    {
        var (vm, _) = await OpenedOn("chin_bolus.stl");
        vm.SmoothLineCommand.Execute(null);
        Assert.NotNull(vm.LineParameters.Edited);

        var (other, _) = MouldAround("nose_bolus.stl");
        await vm.ActivateAsync(other);
        await Until(() => vm.CanEditLine);

        Assert.Null(vm.LineParameters.Edited);
    });

    /// <summary>
    /// Leaving the tab and coming back to the same mould is not a reason to lose a line somebody spent
    /// time on.
    /// </summary>
    [Fact]
    public void ReopeningTheSameMould_KeepsTheEdit() => UiThread.Run(async () =>
    {
        var (workspace, _) = MouldAround("chin_bolus.stl");
        var vm = new PartingSplitViewModel(new StrongReferenceMessenger(), new Mock<IAlertDialog>().Object, Engine);
        await vm.ActivateAsync(workspace);
        await Until(() => vm.CanEditLine);

        vm.SmoothLineCommand.Execute(null);
        var edited = vm.LineParameters.Edited;
        Assert.NotNull(edited);

        await vm.DeactivateAsync();
        await vm.ActivateAsync(workspace);
        await Until(() => vm.CanEditLine);

        Assert.Equal(edited, vm.LineParameters.Edited);
    });

    // ---------------------------------------------------------------- fixtures

    private static async Task<(PartingSplitViewModel ViewModel, MouldMesh Mould)> OpenedOn(string file)
    {
        var (workspace, mould) = MouldAround(file);
        var vm = new PartingSplitViewModel(new StrongReferenceMessenger(), new Mock<IAlertDialog>().Object, Engine);

        await vm.ActivateAsync(workspace);

        // The line is traced off the UI thread after a debounce, and only then cut into sections.
        await Until(() => vm.CanEditLine);
        return (vm, mould);
    }

    private static (Workspace Workspace, MouldMesh Mould) MouldAround(string file)
    {
        var imported = Engine.IO.Import(AssetPath(file)).Value;

        // Recorded as its own base, as an import does, so the mould's history can be replayed back to
        // the body the line is traced on.
        var record = MeshRecord.ForImport(file).WithBaseMesh(imported);
        var workspace = Workspace.CreateEmpty().AddMesh(imported, record).Value;

        var moulded = new GenerateMould(Engine).Execute(
            workspace, record.Id, new ConvexMouldDefinition(3.0, 3.0, 3.0) { TargetMeshId = record.Id }).Value;

        var mould = MouldMesh.Create(moulded.GetMesh(record.Id).Value, moulded.GetRecord(record.Id).Value).Value;
        return (moulded.SetActiveMesh(record.Id).Value, mould);
    }

    private static async Task Until(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("The view did not settle within a minute.");
            await Task.Delay(25);
        }
    }

    private static void AssertSameLine(PartingLine expected, PartingLine actual)
    {
        Assert.Equal(expected.Loops.Count, actual.Loops.Count);
        for (int l = 0; l < expected.Loops.Count; l++)
        {
            Assert.Equal(expected.Loops[l].Count, actual.Loops[l].Count);
            for (int i = 0; i < expected.Loops[l].Count; i++)
                Assert.Equal(expected.Loops[l][i], actual.Loops[l][i]);
        }
    }

    /// <summary>The furthest any point of one line sits from the nearest point of the other.</summary>
    private static double Furthest(PartingLine line, PartingLine other) =>
        line.Loops.SelectMany(l => l).Max(p => other.Loops.SelectMany(l => l).Min(q => p.DistanceTo(q)));

    private static string AssetPath(string name)
    {
        var directory = new System.IO.DirectoryInfo(System.AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = System.IO.Path.Combine(directory.FullName, "files", name);
            if (System.IO.File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new System.IO.FileNotFoundException($"Could not find tests/files/{name}.");
    }
}
