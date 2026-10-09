using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.PartingSplit;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.PartingSplit;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.PartingSplit;

/// <summary>
/// The parting view's opening recipe. These are the two settings that decide whether the split the
/// user gets is the direction-free one, and neither is visible from
/// <c>Fabolus.Core</c> - the feature layer only ever sees whatever the view hands it, so a default
/// that drifted here would silently put every new split back on the pull direction with nothing in
/// the core tests noticing.
/// </summary>
public class PartingSplitViewModelTests
{
    private static PartingSplitViewModel CreateViewModel() => new(
        new StrongReferenceMessenger(),
        new Mock<IAlertDialog>().Object,
        new Mock<IGeometryEngine>().Object);

    [Fact]
    public void OpensOnTheExtrusionBorderLine()
    {
        Assert.Equal(PartingLineSource.ExtrusionBorder, CreateViewModel().LineSource);
    }

    [Fact]
    public void OpensOnAPartingMeshBuiltOnThePartingLinesOwnPlane()
    {
        Assert.Equal(PartingMeshAxisSource.PartingLine, CreateViewModel().MeshAxisSource);
    }


    /// <summary>
    /// A thin cutter, made solid by extruding rather than offsetting. The two go together: offsetting
    /// reads the cutter off a voxel grid, and a grid that resolves a tenth of a millimetre is not
    /// affordable, which is what forced the cutter to millimetres while that route was in use.
    ///
    /// <para>
    /// Asserted through the recipe the view hands the feature rather than against the constants,
    /// because the constants existing is not the claim - the claim is that the parameters leaving
    /// this view carry them, which is what decides the cutter the user is shown and the mould is
    /// broken with.
    /// </para>
    /// </summary>
    [Fact]
    public void BuildsAThinExtrudedCutter()
    {
        var parameters = CreateViewModel().MeshParameters;
        Assert.Equal(PartingMeshThickening.Extrude, parameters.Thickening);
        Assert.Equal(0.1, parameters.Depth);
    }

    /// <summary>
    /// And they must be the view's own values, not ones inherited from the record. The record
    /// defaults serve replayed recipes, so the two are free to diverge - if they ever do, this is
    /// what says the view kept the cutter it names.
    /// </summary>
    [Fact]
    public void StatesTheCutterRatherThanInheritingIt()
    {
        var altered = PartingMeshParameters.Default with { Depth = 2.0 };
        Assert.NotEqual(altered.Depth, CreateViewModel().MeshParameters.Depth);

        // Read off the view rather than off a constant: the cutter thickness became a slider, so the
        // claim is that whatever the view is showing is what the parameters carry.
        var viewModel = CreateViewModel();
        Assert.Equal(viewModel.CutterDepthMm, viewModel.MeshParameters.Depth);
    }

    /// <summary>
    /// The sweep and the split method, asserted together because they only make sense as a pair: the
    /// half-space split cuts with a tool it builds itself rather than with the slab step two shows, so
    /// moving one without the other has the user approving one solid and receiving another. The radio
    /// groups that used to carry them are gone from the view, so this is the only thing pinning them.
    ///
    /// <para>
    /// Lofted to the mould by default, so the mating face takes its shape from the mould rather than
    /// carrying the body's undulation; the marching sweep stays one switch away for comparison. Either
    /// way the cut is the thin severing one, which is what step two shows.
    /// </para>
    /// </summary>
    [Fact]
    public void LoftsTheFlangeToTheMouldByDefault()
    {
        var viewModel = CreateViewModel();
        Assert.Equal(PartingMeshSweep.MouldLoft, viewModel.MeshSweep);
        Assert.Equal(PartingSplitMethod.SeveredComponents, viewModel.SplitMethod);

        viewModel.UseMouldLoft = false;
        Assert.Equal(PartingMeshSweep.SurfaceSweep, viewModel.MeshSweep);
        Assert.Equal(PartingMeshSweep.SurfaceSweep, viewModel.MeshParameters.Sweep);
    }

    /// <summary>
    /// The recipe is fixed, not merely defaulted: nothing the user can reach changes it. A property
    /// that grew a setter again would be a step-one option coming back, which is what this catches.
    /// </summary>
    [Fact]
    public void ExposesTheRecipeAsReadOnly()
    {
        var type = typeof(PartingSplitViewModel);
        foreach (var name in new[]
                 { nameof(PartingSplitViewModel.LineSource), nameof(PartingSplitViewModel.MeshAxisSource),
                   nameof(PartingSplitViewModel.MeshSweep), nameof(PartingSplitViewModel.SplitMethod),
                   nameof(PartingSplitViewModel.Thickening) })
        {
            Assert.False(type.GetProperty(name)!.CanWrite, $"{name} must not be settable from the view");
        }
    }
}
