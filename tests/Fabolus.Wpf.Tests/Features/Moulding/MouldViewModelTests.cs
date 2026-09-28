using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Moulding;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Moulding;

/// <summary>
/// How the mould view spends the user's time. Rebuilding the shell is the most expensive thing
/// any tool does - most of a second on a real bolus - and every wall, base and trough slider
/// triggers it, so what these cover is that a drag costs one rebuild rather than one per step,
/// and that the user is told it is happening.
/// </summary>
/// <remarks>
/// All of these run on a dispatcher thread: the coalescing is built on a DispatcherTimer, which
/// only ticks while its dispatcher is pumping.
/// </remarks>
public class MouldViewModelTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    /// <summary>
    /// A drag raises a change per step. Before the rebuild was deferred, each one rebuilt the
    /// whole shell on the UI thread; twenty steps meant twenty rebuilds and a frozen window.
    /// </summary>
    [Fact]
    public void DraggingASlider_RebuildsTheShellOnce_NotOncePerStep() => UiThread.Run(async () =>
    {
        var (vm, loading) = await ActivatedViewModel();
        loading.Reset();
        var before = vm.MouldRebuildCount;

        for (int i = 0; i < 20; i++)
        {
            vm.WallThickness += 0.1;
        }

        // Nothing should have started yet: the whole point is that the steps are absorbed.
        Assert.Equal(0, loading.Raised);
        Assert.Equal(before, vm.MouldRebuildCount);

        await NextCompletion(loading);

        Assert.Equal(before + 1, vm.MouldRebuildCount);
        Assert.Equal(1, loading.Raised);
        Assert.Equal(1, loading.Lowered);
    });

    /// <summary>
    /// A change made once a rebuild is under way must not be dropped, or the shell is left showing
    /// parameters the user has already moved past.
    /// </summary>
    /// <remarks>
    /// Whether it lands while the rebuild is still running - folded into it by the re-entrancy
    /// guard - or just after it finished, and which of those happens depends on how long the
    /// build takes, both end in a second rebuild. That is what is asserted, so the test does not
    /// depend on winning a race against the mesh size.
    /// </remarks>
    [Fact]
    public void ChangingASliderDuringARebuild_RebuildsAgain() => UiThread.Run(async () =>
    {
        var (vm, loading) = await ActivatedViewModel();
        loading.Reset();
        var before = vm.MouldRebuildCount;

        vm.WallThickness += 0.1;

        // Wait for the rebuild to be under way, then move the slider again.
        while (loading.Raised == 0)
        {
            await Task.Delay(1);
        }
        vm.WallThickness += 0.1;

        await SettledAsync(vm, loading);

        Assert.Equal(before + 2, vm.MouldRebuildCount);
        Assert.Equal(loading.Raised, loading.Lowered);
    });

    [Fact]
    public void OpeningTheView_BuildsTheShellBeforeActivationReturns() => UiThread.Run(async () =>
    {
        var (_, loading) = await ActivatedViewModel();

        // Not left for the debounce timer to pick up after the view is already on screen.
        Assert.True(loading.Raised >= 1, "activation should have built the shell");
        Assert.Equal(loading.Raised, loading.Lowered);
    });

    [Fact]
    public void DeactivatingWithAChangePending_DoesNotRebuild() => UiThread.Run(async () =>
    {
        var (vm, loading) = await ActivatedViewModel();
        loading.Reset();

        var before = vm.MouldRebuildCount;

        vm.WallThickness += 0.1;
        await vm.DeactivateAsync();

        // Long enough that the debounce timer would have fired had it not been stopped.
        await Task.Delay(400);

        Assert.Equal(0, loading.Raised);
        Assert.Equal(before, vm.MouldRebuildCount);
    });

    // ---- plumbing -------------------------------------------------------------------------

    /// <summary>
    /// Waits for a rebuild to start and finish. Fails rather than hanging the run.
    /// </summary>
    private static async Task NextCompletion(LoadingLog loading)
    {
        for (int i = 0; i < 2000 && loading.Lowered == 0; i++)
        {
            await Task.Delay(5);
        }

        Assert.True(loading.Lowered > 0, "no rebuild completed within ten seconds");
    }

    /// <summary>
    /// Waits until nothing is building and nothing is queued: no new rebuild for comfortably
    /// longer than the debounce interval, with the overlay back down.
    /// </summary>
    private static async Task SettledAsync(MouldViewModel vm, LoadingLog loading)
    {
        int quiet = 0;

        for (int i = 0; i < 400 && quiet < 12; i++)
        {
            var seen = vm.MouldRebuildCount;
            await Task.Delay(25);

            quiet = vm.MouldRebuildCount == seen && loading.Raised == loading.Lowered
                ? quiet + 1
                : 0;
        }

        Assert.True(quiet >= 12, "the mould never stopped rebuilding");
    }

    private static async Task<(MouldViewModel Vm, LoadingLog Loading)> ActivatedViewModel()
    {
        var messenger = new StrongReferenceMessenger();
        var loading = LoadingLog.Watching(messenger);

        var vm = new MouldViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        return (vm, loading);
    }

    private static Workspace WorkspaceWithBox()
    {
        var mesh = Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value;
        return Workspace.CreateEmpty().AddMesh(mesh, MeshRecord.ForImport("bench box")).Value;
    }
}
