using System.Collections.Generic;
using System.Linq;
using BasicResults;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Export;
using Fabolus.Wpf.Features.Main;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Export;

/// <summary>
/// The name the export panel offers when the save dialog opens.
/// </summary>
/// <remarks>
/// The dialog used to be given only a filter and an extension, so it opened blank and the name
/// the panel had already worked out - the one the mesh was imported under - was used for nothing
/// but the info panel's display.
/// </remarks>
public class ExportViewModelTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void ExportFiles_OffersTheNameTheMeshWasImportedUnder() => UiThread.Run(async () =>
    {
        var (vm, dialogue) = await PanelFor("ear_bolus_L2");

        vm.ExportFilesCommand.Execute(null);

        dialogue.Verify(d => d.ShowSaveFileDialog(
            It.IsAny<string>(), It.IsAny<string>(), "ear_bolus_L2"), Times.Once);
    });

    [Fact]
    public void ExportFiles_OffersWhateverTheUserRetypedInThePanel() => UiThread.Run(async () =>
    {
        var (vm, dialogue) = await PanelFor("ear_bolus_L2");

        // The panel's name box is editable, so the dialog has to follow the box rather than go
        // back to the record.
        vm.FileName = "ear_bolus_L2_rev3";
        vm.ExportFilesCommand.Execute(null);

        dialogue.Verify(d => d.ShowSaveFileDialog(
            It.IsAny<string>(), It.IsAny<string>(), "ear_bolus_L2_rev3"), Times.Once);
    });

    /// <summary>
    /// The name box is free text, so the user can type an extension the panel is about to add
    /// again. SaveFileDialog does not deduplicate: given "bolus.stl" under a *.3mf filter it
    /// appends its own, and the file lands as "bolus.stl.3mf".
    /// </summary>
    [Theory]
    [InlineData("bolus.stl", "bolus")]
    [InlineData("bolus.3mf", "bolus")]
    [InlineData("bolus.STL", "bolus")]   // the dialog does not care about case; neither should this
    [InlineData("bolus", "bolus")]
    public void ExportFiles_DoesNotOfferAnExtensionTwice(string typed, string expected) =>
        UiThread.Run(async () =>
        {
            var (vm, dialogue) = await PanelFor("bolus");

            vm.FileName = typed;
            vm.ExportFilesCommand.Execute(null);

            dialogue.Verify(d => d.ShowSaveFileDialog(
                It.IsAny<string>(), It.IsAny<string>(), expected), Times.Once);
        });

    /// <summary>
    /// A suffix that merely looks like an extension belongs to the name. Stripping whatever
    /// Path.GetExtension returned would silently export "ear_v1.5" as "ear_v1".
    /// </summary>
    [Fact]
    public void ExportFiles_KeepsASuffixThatIsNotAnExportExtension() => UiThread.Run(async () =>
    {
        var (vm, dialogue) = await PanelFor("ear_v1.5");

        vm.ExportFilesCommand.Execute(null);

        dialogue.Verify(d => d.ShowSaveFileDialog(
            It.IsAny<string>(), It.IsAny<string>(), "ear_v1.5"), Times.Once);
    });

    /// <summary>
    /// A bolus and the mould around it are two different quantities of two different materials,
    /// and an export is where both matter. Without a mould there is only the one.
    /// </summary>
    [Fact]
    public void WithoutAMould_OnlyTheBolusVolumeIsReported() => UiThread.Run(async () =>
    {
        var items = await InfoPanelFor(WorkspaceWith("bolus"));

        Assert.Contains(items.OfType<TextInfoItem>(), i => i.Label == "Bolus volume");
        Assert.DoesNotContain(items.OfType<TextInfoItem>(), i => i.Label == "Mould volume");
    });

    [Fact]
    public void WithAMould_BothVolumesAreReported() => UiThread.Run(async () =>
    {
        var items = await InfoPanelFor(WorkspaceWith("bolus", new ConcaveMouldDefinition()));

        var bolus = Assert.Single(items.OfType<TextInfoItem>(), i => i.Label == "Bolus volume");
        var mould = Assert.Single(items.OfType<TextInfoItem>(), i => i.Label == "Mould volume");

        Assert.EndsWith("mL", bolus.Value);
        Assert.EndsWith("mL", mould.Value);

        // Two readings off two different meshes, which is the whole point of the pair: the same
        // number twice would mean both had been read off the active mesh.
        //
        // Not a size comparison. A mould encases the bolus but is hollowed out by it - Apply
        // subtracts the bolus from the shell - so the mould is only the material around the
        // cavity and is routinely the smaller figure of the two.
        Assert.True(MillilitresIn(bolus.Value) > 0, $"bolus reads {bolus.Value}");
        Assert.True(MillilitresIn(mould.Value) > 0, $"mould reads {mould.Value}");
        Assert.NotEqual(bolus.Value, mould.Value);
    });

    private static double MillilitresIn(string value) =>
        double.Parse(value.Replace(" mL", string.Empty), System.Globalization.CultureInfo.CurrentCulture);

    private static async Task<List<MeshInfoItem>> InfoPanelFor(Workspace workspace)
    {
        var messenger = new StrongReferenceMessenger();

        var published = new List<MeshInfoItem>();
        messenger.Register<UpdateMeshInfoMessage>(new object(), (_, m) =>
        {
            published.Clear();
            published.AddRange(m.Items);
        });

        var vm = new ExportViewModel(
            messenger, Mock.Of<IAlertDialog>(), Engine, Mock.Of<IDialogueSystem>());

        await vm.ActivateAsync(workspace);
        return published;
    }

    private static Workspace WorkspaceWith(string name, params IMeshCommand[] commands)
    {
        var mesh = Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value
            .WithMeasurements(Engine);

        var record = MeshRecord.ForImport(name);
        var workspace = Workspace.CreateEmpty().AddMesh(mesh, record).Value;

        foreach (var command in commands)
        {
            var applied = command.Apply(Engine, workspace.GetActiveMesh().Value);
            Assert.True(applied.IsSuccess, applied.IsFailure ? applied.Error.Description : "");

            workspace = workspace.UpdateMesh(
                record.Id,
                applied.Value.WithMeasurements(Engine),
                workspace.GetActiveRecord().Value.WithCommand(command)).Value;
        }

        return workspace;
    }

    /// <summary>
    /// An activated export panel over a one-mesh workspace, with a dialogue system that cancels
    /// every save so nothing is written to disk.
    /// </summary>
    private static async Task<(ExportViewModel, Mock<IDialogueSystem>)> PanelFor(string meshName)
    {
        var dialogue = new Mock<IDialogueSystem>();
        dialogue
            .Setup(d => d.ShowSaveFileDialog(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Maybe<string>.None());

        var vm = new ExportViewModel(
            new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine, dialogue.Object);

        await vm.ActivateAsync(WorkspaceWith(meshName));
        return (vm, dialogue);
    }

    private static Workspace WorkspaceWith(string meshName)
    {
        // Measured, because activating the panel reads statistics off the mesh to fill the info
        // panel and an unmeasured mesh has none.
        var mesh = Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value
            .WithMeasurements(Engine);
        return Workspace.CreateEmpty().AddMesh(mesh, MeshRecord.ForImport(meshName)).Value;
    }
}
