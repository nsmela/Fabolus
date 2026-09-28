using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.MeshManager;
using Fabolus.Wpf.Features.Rotatation;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features;

/// <summary>
/// Which slow commands tell the user they are working.
/// </summary>
/// <remarks>
/// On a hundred-thousand-triangle bolus each of these is around a quarter of a second. Rotating
/// raised the overlay only after the rotation had already run, and clearing a rotation and
/// repairing a mesh raised it not at all - so the window simply stopped responding. These cover
/// that the overlay goes up once and comes down once across the whole command; that the work
/// itself is off the UI thread, so the overlay can actually paint, is what the benchmarks
/// measure.
/// </remarks>
public class LoadingOverlayTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void SavingARotation_ShowsTheOverlay() => UiThread.Run(async () =>
    {
        var messenger = new StrongReferenceMessenger();
        var vm = new RotateViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        var log = LoadingLog.Watching(messenger);
        vm.XAxisAngle = 15f;

        await vm.SaveAxisRotationCommand.ExecuteAsync(null);

        Assert.Equal(1, log.Raised);
        Assert.Equal(1, log.Lowered);
    });

    [Fact]
    public void ClearingRotations_ShowsTheOverlay() => UiThread.Run(async () =>
    {
        var messenger = new StrongReferenceMessenger();
        var vm = new RotateViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        vm.XAxisAngle = 15f;
        await vm.SaveAxisRotationCommand.ExecuteAsync(null);

        var log = LoadingLog.Watching(messenger);

        await vm.ClearRotationsCommand.ExecuteAsync(null);

        Assert.Equal(1, log.Raised);
        Assert.Equal(1, log.Lowered);
    });

    [Fact]
    public void RepairingAMesh_ShowsTheOverlay() => UiThread.Run(async () =>
    {
        var messenger = new StrongReferenceMessenger();
        var workspace = WorkspaceWithBox();
        var vm = new MeshManagerViewModel(
            messenger, Mock.Of<IDialogueSystem>(), Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(workspace);

        var log = LoadingLog.Watching(messenger);

        await vm.RepairMeshCommand.ExecuteAsync(workspace.ActiveMeshId);

        Assert.Equal(1, log.Raised);
        Assert.Equal(1, log.Lowered);
    });

    private static Workspace WorkspaceWithBox()
    {
        var mesh = Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value;
        return Workspace.CreateEmpty().AddMesh(mesh, MeshRecord.ForImport("box")).Value;
    }
}
