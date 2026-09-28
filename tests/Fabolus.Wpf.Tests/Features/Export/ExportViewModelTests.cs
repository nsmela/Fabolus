using BasicResults;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Export;
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
