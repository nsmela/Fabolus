using System.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Rotatation;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Rotatation;

/// <summary>
/// What the rotation panel does while a rotation is actually being applied.
/// </summary>
/// <remarks>
/// The rotation runs off the UI thread, so the panel stays live underneath it. A drag also
/// finishes with the pointer still over the slider, so without holding them off, the hover ring
/// and the temp rotation would keep drawing against a mesh that is in the middle of being
/// replaced - on top of a rotation the user has already committed.
/// </remarks>
public class RotateViewModelTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void HoveringASliderWhileRotating_ShowsNoAxisRingAndNoPreview() => UiThread.Run(async () =>
    {
        var vm = new RotateViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        var visuals = 0;
        vm.SceneManager.VisualAddedOrUpdated += _ => visuals++;

        // Hovering normally raises the ring, so the assertion below is about the guard rather
        // than about nothing ever happening.
        vm.ShowAxisXRotationCommand.Execute(null);
        Assert.True(visuals > 0, "hovering should raise the axis ring when idle");

        int whileRotating = -1;
        vm.PropertyChanged += (_, e) => OnRotatingBegan(vm, e, () =>
        {
            // The pointer is still over the slider as the rotation is committed.
            var before = visuals;
            vm.ShowAxisXRotationCommand.Execute(null);
            vm.XAxisAngle = 42f;
            whileRotating = visuals - before;
        });

        vm.XAxisAngle = 15f;
        await vm.SaveAxisRotationCommand.ExecuteAsync(null);

        Assert.Equal(0, whileRotating);
    });

    [Fact]
    public void WhileRotating_TheSlidersAreDisabled() => UiThread.Run(async () =>
    {
        var vm = new RotateViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        Assert.True(vm.IsRotationEnabled);

        bool disabledDuring = false;
        vm.PropertyChanged += (_, e) => OnRotatingBegan(vm, e, () => disabledDuring = !vm.IsRotationEnabled);

        vm.XAxisAngle = 15f;
        await vm.SaveAxisRotationCommand.ExecuteAsync(null);

        Assert.True(disabledDuring, "the sliders should be disabled while the rotation is applied");
        Assert.True(vm.IsRotationEnabled, "and enabled again once it is done");
    });

    [Fact]
    public void ClearingRotations_AlsoHoldsOffTheSliders() => UiThread.Run(async () =>
    {
        var vm = new RotateViewModel(new StrongReferenceMessenger(), Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        vm.XAxisAngle = 15f;
        await vm.SaveAxisRotationCommand.ExecuteAsync(null);

        bool disabledDuring = false;
        vm.PropertyChanged += (_, e) => OnRotatingBegan(vm, e, () => disabledDuring = !vm.IsRotationEnabled);

        await vm.ClearRotationsCommand.ExecuteAsync(null);

        Assert.True(disabledDuring);
        Assert.True(vm.IsRotationEnabled);
    });

    /// <summary>
    /// Runs <paramref name="act"/> at the instant the view model reports it has started rotating.
    /// The property change is raised synchronously from inside the command, so this lands in the
    /// middle of the operation without racing it.
    /// </summary>
    private static void OnRotatingBegan(RotateViewModel vm, PropertyChangedEventArgs e, Action act)
    {
        // The same property change also reports the end of the rotation; only the start matters.
        if (e.PropertyName == nameof(RotateViewModel.IsRotating) && vm.IsRotating)
        {
            act();
        }
    }

    private static Workspace WorkspaceWithBox()
    {
        // Measured, because the rotation gizmo is sized from the mesh's bounds and refuses a
        // mesh that has never been measured.
        var mesh = Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value
            .WithMeasurements(Engine);
        return Workspace.CreateEmpty().AddMesh(mesh, MeshRecord.ForImport("box")).Value;
    }
}
