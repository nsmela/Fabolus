using System.Collections.Immutable;
using CommunityToolkit.Mvvm.Messaging;
using BasicResults;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Features.Decal;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Decal;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Decal;

public sealed class TestOutlineSource : IGlyphOutlineSource
{
    public Result<IReadOnlyList<Polygon2D>> GetOutlines(string text, DecalFont font, float capHeight, float tracking)
    {
        return Result.Success<IReadOnlyList<Polygon2D>>(new List<Polygon2D>
        {
            Polygon2D.FromOuter([new(-5, -3), new(5, -3), new(5, 3), new(-5, 3)])
        });
    }

    public TextMetrics MeasureText(string text, DecalFont font, float capHeight, float tracking)
    {
        return new TextMetrics(10f, capHeight, new float[] { 10f });
    }
}

/// <summary>
/// Behaviour of the decal view model: the decal list, target switching, preset snapping, applying
/// and clearing.
/// </summary>
/// <remarks>
/// Driven by the real geometry engine on a real box rather than a mocked IGeometryEngine. The
/// mock these replaced stubbed BuildTextPrism, GetRenderData, CloneMesh, Raycast and a
/// property-bag MeshStatistics - none of which exist any more - and it stopped compiling the
/// moment the engine moved beneath it. Nothing here cares how a boolean is computed, only what
/// the view model does with the answer, so the real engine costs milliseconds and cannot rot.
///
/// The box is 40 x 60 x 50 about the origin, which is what the preset-point assertions are
/// written against.
/// </remarks>
public class DecalViewModelTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    private static (DecalViewModel vm, IMessenger messenger) CreateViewModel()
    {
        var messenger = new StrongReferenceMessenger();

        // No preference store here: an unanswered section request falls back to that section's
        // Default, which is what this test wants anyway.
        var alertMock = new Mock<IAlertDialog>();
        var vm = new DecalViewModel(messenger, alertMock.Object, Engine, new TestOutlineSource());
        return (vm, messenger);
    }

    private static IMesh Box() =>
        Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value;

    /// <summary>
    /// A workspace holding one entry. Commands must be listed in ascending priority order, since
    /// recording one clears anything of a strictly greater priority.
    /// </summary>
    private static Workspace WorkspaceWith(string name, params IMeshCommand[] commands) =>
        WorkspaceWith(Box(), name, commands);

    private static Workspace WorkspaceWith(IMesh mesh, string name, params IMeshCommand[] commands)
    {
        var record = MeshRecord.ForImport(name);
        foreach (var command in commands) record = record.WithCommand(command);

        return Workspace.CreateEmpty().AddMesh(mesh, record).Value;
    }

    [Fact]
    public void Operation_ChangingToEngrave_UpdatesDepthLabel()
    {
        var (vm, _) = CreateViewModel();

        vm.Operation = EmbossOperation.Emboss;
        Assert.Equal("Height", vm.DepthLabel);
        Assert.Equal("Apply decals", vm.ApplyLabel);

        vm.Operation = EmbossOperation.Engrave;
        Assert.Equal("Depth", vm.DepthLabel);
        Assert.Equal("Apply decals", vm.ApplyLabel);
    }

    [Fact]
    public void AddDecalCommand_AddsNewDecal()
    {
        var (vm, _) = CreateViewModel();

        Assert.Equal(0, vm.DecalCount);

        vm.AddDecalCommand.Execute(null);
        Assert.Equal(1, vm.DecalCount);

        vm.AddDecalCommand.Execute(null);
        Assert.Equal(2, vm.DecalCount);
    }

    [Fact]
    public void ClearTextCommand_WhenNotApplied_DoesNothing()
    {
        var (vm, _) = CreateViewModel();
        Assert.False(vm.IsApplied);

        vm.ClearTextCommand.Execute(null);
        Assert.False(vm.IsApplied);
    }

    [Fact]
    public async Task ActivateAsync_WithImportedDecalCommand_InheritsDecalsAndSetsIsAppliedTrue()
    {
        var (vm, _) = CreateViewModel();

        var decal = new TextDecal
        {
            Text = "IMPORTED",
            CapHeight = 7.5f,
            Depth = 1.2f,
            Operation = EmbossOperation.Engrave,
            RotationDeg = 30f,
            Anchor = new Vector3(5, 10, 15),
            AnchorNormal = Vector3.UnitZ
        };

        await vm.ActivateAsync(WorkspaceWith("Test", new DecalCommand(new[] { decal })));

        Assert.True(vm.IsApplied);
        Assert.False(vm.HasMould);
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal("Applied", vm.StatusWord);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);

        vm.SelectDecalCommand.Execute(vm.Decals[0].Id);
        Assert.Equal("IMPORTED", vm.LabelText);
        Assert.Equal(7.5f, vm.CapHeight);
        Assert.Equal(1.2f, vm.Depth);
        Assert.Equal(EmbossOperation.Engrave, vm.Operation);
        Assert.Equal(30, vm.Rotation);
        Assert.Equal("Preview", vm.StatusWord);
        Assert.Equal(1, vm.DecalCount);
    }

    [Fact]
    public async Task ActivateAsync_WithMouldMesh_SetsHasMouldTrue()
    {
        var (vm, _) = CreateViewModel();

        await vm.ActivateAsync(WorkspaceWith("Mould Mesh", new ConcaveMouldDefinition(5, 5, 5)));

        Assert.True(vm.HasMould);
    }

    [Fact]
    public async Task ApplyCommand_OnBaseMesh_AppliesEmbossSuccessfully()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("Base Mesh"));

        vm.LabelText = "TEST";
        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsApplied);
        Assert.Equal("Applied", vm.StatusWord);
        Assert.Empty(vm.ErrorText);
    }

    [Fact]
    public async Task DeleteSelectedDecal_RemovesDecalAndUpdatesCount()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("Test"));

        Assert.Equal(1, vm.DecalCount);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);

        // Select decal first
        vm.SelectDecalCommand.Execute(vm.Decals[0].Id);
        Assert.Equal(vm.Decals[0].Id, vm.SelectedDecalId);

        vm.DeleteSelectedDecalCommand.Execute(null);
        Assert.Equal(0, vm.DecalCount);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
    }

    [Fact]
    public async Task ClearDecals_ClearsAllDecals()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("TestMesh"));

        Assert.Equal(1, vm.DecalCount);

        vm.ClearDecalsCommand.Execute(null);
        Assert.Equal(0, vm.DecalCount);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
    }

    [Fact]
    public async Task DecalList_SyncsWithDecalsAndSelection()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("TestMesh"));

        Assert.Single(vm.DecalList);
        Assert.Equal("FABOLUS", vm.DecalList[0].Text);
        Assert.False(vm.DecalList[0].IsSelected);

        // Select decal
        vm.SelectDecalCommand.Execute(vm.DecalList[0].Id);
        Assert.True(vm.DecalList[0].IsSelected);

        // Edit text
        vm.LabelText = "NEW TEXT";
        Assert.Equal("NEW TEXT", vm.DecalList[0].Text);

        // Delete item by ID
        var id = vm.DecalList[0].Id;
        vm.DeleteDecalByIdCommand.Execute(id);
        Assert.Empty(vm.DecalList);
        Assert.Equal(0, vm.DecalCount);
    }

    [Fact]
    public async Task ApplyCommand_CollapsesDecalsExpander()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("Test"));

        Assert.True(vm.IsDecalsExpanded);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsApplied);
        Assert.False(vm.IsDecalsExpanded);
    }

    [Fact]
    public async Task ActivateAsync_WithMould_SetsHasMouldAndTargetOnDecalList()
    {
        var (vm, _) = CreateViewModel();

        var decal1 = new TextDecal { Id = Guid.NewGuid(), Text = "BASE1", Target = EmbossTarget.Base, CapHeight = 5f, Operation = EmbossOperation.Emboss };
        var decal2 = new TextDecal { Id = Guid.NewGuid(), Text = "MOULD1", Target = EmbossTarget.Mould, CapHeight = 6f, Operation = EmbossOperation.Engrave };

        await vm.ActivateAsync(WorkspaceWith(
            "MouldMesh",
            new DecalCommand(new[] { decal1 }),
            new ConcaveMouldDefinition(),
            new MouldDecalCommand(new[] { decal2 })));

        Assert.True(vm.HasMould);
        Assert.True(vm.IsApplied);
        Assert.Equal(2, vm.DecalCount);
        Assert.Equal(2, vm.DecalList.Count);

        Assert.Equal(EmbossTarget.Base, vm.DecalList[0].Target);
        Assert.Equal("Base", vm.DecalList[0].TargetText);
        Assert.True(vm.DecalList[0].HasMould);

        Assert.Equal(EmbossTarget.Mould, vm.DecalList[1].Target);
        Assert.Equal("Mould", vm.DecalList[1].TargetText);
        Assert.True(vm.DecalList[1].HasMould);
    }

    [Fact]
    public async Task AddDecal_SwitchingTargetToMould_PreservesPreviousBaseDecalTarget()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("MouldMesh", new ConcaveMouldDefinition()));

        Assert.True(vm.HasMould);
        Assert.Equal(2, vm.DecalList.Count);
        Assert.Equal(EmbossTarget.Mould, vm.DecalList[0].Target);
        Assert.Equal(EmbossTarget.Mould, vm.DecalList[1].Target);

        // Click "+ Add decal"
        vm.AddDecalCommand.Execute(null);

        Assert.Equal(3, vm.DecalCount);
        Assert.Equal(3, vm.DecalList.Count);
        Assert.Equal(vm.DecalList[2].Id, vm.SelectedDecalId);

        // Switch target of newly added decal to Base
        vm.Target = EmbossTarget.Base;

        // Verify first decal remained Mould and 3rd decal is Base
        Assert.Equal(EmbossTarget.Mould, vm.DecalList[0].Target);
        Assert.Equal("Mould", vm.DecalList[0].TargetText);

        Assert.Equal(EmbossTarget.Base, vm.DecalList[2].Target);
        Assert.Equal("Base", vm.DecalList[2].TargetText);
    }

    [Fact]
    public async Task ClearText_PreservesDecalsAndRevertsToEditMode()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("Test"));

        Assert.Equal(1, vm.DecalCount);
        Assert.Equal("FABOLUS", vm.DecalList[0].Text);

        // Apply decals
        await vm.ApplyCommand.ExecuteAsync(null);
        Assert.True(vm.IsApplied);
        Assert.False(vm.IsDecalsExpanded);

        // Clear applied decals (reverts baked geometry, keeps decal definitions)
        vm.ClearTextCommand.Execute(null);

        Assert.False(vm.IsApplied);
        Assert.True(vm.IsDecalsExpanded);
        Assert.Equal(1, vm.DecalCount);
        Assert.Single(vm.DecalList);
        Assert.Equal("FABOLUS", vm.DecalList[0].Text);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
    }

    [Fact]
    public async Task ActivateAsync_WithMould_CalculatesPresetPointsAndAllowsPresetSnapping()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("MouldMesh", new ConcaveMouldDefinition()));

        Assert.True(vm.HasMould);
        Assert.Equal(6, vm.MouldPresetPoints.Count);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);

        var frontPreset = Assert.Single(vm.MouldPresetPoints, p => p.Name == "Front");
        var curve1Preset = Assert.Single(vm.MouldPresetPoints, p => p.Name == "Curve 1");

        vm.Target = EmbossTarget.Mould;

        // Apply "Front" preset when no decal is selected -> selects Front decal (which already exists at Front)
        vm.ApplyPresetByNameCommand.Execute("Front");

        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(frontPreset.Position, vm.Anchor);
        Assert.Equal(frontPreset.Normal, vm.AnchorNormal);
        Assert.Equal(0, vm.Rotation);
        Assert.True(vm.CapHeight > 0f && vm.CapHeight <= 10.0f);
        Assert.True(vm.HasSelectedDecal);

        // Apply "Curve 1" preset (vertical)
        vm.ApplyPresetByNameCommand.Execute("Curve 1");
        Assert.Equal(curve1Preset.Position, vm.Anchor);
        Assert.Equal(90, vm.Rotation);

        await vm.ApplyCommand.ExecuteAsync(null);

        Assert.True(vm.IsApplied);
        Assert.False(vm.IsDecalsExpanded);

        // Clear reverts to edit mode and removes translucent overlay
        vm.ClearTextCommand.Execute(null);
        Assert.False(vm.IsApplied);
        Assert.True(vm.IsDecalsExpanded);
    }

    [Fact]
    public async Task ActivateAsync_WithBaseMesh_CalculatesBasePresetPointsAndAllowsTopFrontBackSnapping()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("BaseMesh"));

        Assert.False(vm.HasMould);
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal(3, vm.BasePresetPoints.Count);
        Assert.Equal(3, vm.ActivePresetPoints.Count);

        var topPreset = Assert.Single(vm.BasePresetPoints, p => p.Name == "Top");
        var frontPreset = Assert.Single(vm.BasePresetPoints, p => p.Name == "Front");
        var backPreset = Assert.Single(vm.BasePresetPoints, p => p.Name == "Back");

        Assert.Equal(0, topPreset.RotationDeg);
        Assert.Equal(0, frontPreset.RotationDeg);
        Assert.Equal(0, backPreset.RotationDeg);

        // Apply "Top" preset
        vm.ApplyPresetByNameCommand.Execute("Top");
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal(topPreset.Position, vm.Anchor);
        Assert.Equal(topPreset.Normal, vm.AnchorNormal);
        Assert.Equal(0, vm.Rotation);
        Assert.True(vm.CapHeight > 0f && vm.CapHeight <= 10.0f);

        // Apply "Front" preset
        vm.ApplyPresetByNameCommand.Execute("Front");
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal(frontPreset.Position, vm.Anchor);
        Assert.Equal(0, vm.Rotation);
    }

    [Fact]
    public async Task AddDecal_GeneratesOnFirstFreeAnchorInViewedTarget()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("MouldMesh", new ConcaveMouldDefinition()));

        // Initial decals start on Mould target at Front (file name) and Back (volume)
        Assert.True(vm.HasMould);
        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(2, vm.DecalCount);
        var frontPreset = vm.MouldPresetPoints.First(p => p.Name == "Front");
        var backPreset = vm.MouldPresetPoints.First(p => p.Name == "Back");
        Assert.Equal(frontPreset.Position, vm.Decals[0].Anchor);
        Assert.Equal(EmbossTarget.Mould, vm.Decals[0].Target);
        Assert.Equal("MouldMesh", vm.Decals[0].Text);

        Assert.Equal(backPreset.Position, vm.Decals[1].Anchor);
        Assert.Equal(EmbossTarget.Mould, vm.Decals[1].Target);
        Assert.Contains("cc", vm.Decals[1].Text);

        // Add 3rd decal -> should place at Left (next free anchor on Mould target)
        vm.AddDecalCommand.Execute(null);
        Assert.Equal(3, vm.DecalCount);
        var leftPreset = vm.MouldPresetPoints.First(p => p.Name == "Left");
        Assert.Equal(leftPreset.Position, vm.Decals[2].Anchor);
        Assert.Equal(EmbossTarget.Mould, vm.Decals[2].Target);

        // Switch to Base target and add decal -> should place at Top (first free anchor on Base target)
        vm.Target = EmbossTarget.Base;
        vm.AddDecalCommand.Execute(null);
        Assert.Equal(4, vm.DecalCount);
        var topPreset = vm.BasePresetPoints.First(p => p.Name == "Top");
        Assert.Equal(topPreset.Position, vm.Decals[3].Anchor);
        Assert.Equal(EmbossTarget.Base, vm.Decals[3].Target);
    }

    [Fact]
    public async Task SelectionMode_DefaultsToNoSelection_AndAllowsSwitchingAndDeselecting()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("TestMesh"));

        // Verify default on activation: NO decal selected
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);
        Assert.Single(vm.Decals);
        Assert.False(vm.DecalList[0].IsSelected);

        // Add 2nd decal -> becomes selected
        vm.AddDecalCommand.Execute(null);
        Assert.Equal(2, vm.DecalCount);
        var secondDecalId = vm.Decals[1].Id;
        Assert.Equal(secondDecalId, vm.SelectedDecalId);
        Assert.True(vm.HasSelectedDecal);
        Assert.False(vm.DecalList[0].IsSelected);
        Assert.True(vm.DecalList[1].IsSelected);

        // Switch selection to 1st decal
        var firstDecalId = vm.Decals[0].Id;
        vm.SelectDecalCommand.Execute(firstDecalId);
        Assert.Equal(firstDecalId, vm.SelectedDecalId);
        Assert.True(vm.HasSelectedDecal);
        Assert.True(vm.DecalList[0].IsSelected);
        Assert.False(vm.DecalList[1].IsSelected);

        // Deselect by setting SelectedDecalId to Guid.Empty (e.g. from viewport click on background/mesh)
        vm.SelectedDecalId = Guid.Empty;
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);
        Assert.False(vm.DecalList[0].IsSelected);
        Assert.False(vm.DecalList[1].IsSelected);
    }

    [Fact]
    public async Task SwitchingTarget_DoesNotRequireSelectedDecal()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("MouldMesh", new ConcaveMouldDefinition()));

        // Starts with Mould target, NO decal selected
        Assert.True(vm.HasMould);
        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);

        // Switch to Base without selecting any decal
        vm.Target = EmbossTarget.Base;
        Assert.Equal(EmbossTarget.Base, vm.Target);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);

        // Switch back to Mould without selecting any decal
        vm.Target = EmbossTarget.Mould;
        Assert.Equal(EmbossTarget.Mould, vm.Target);
        Assert.Equal(Guid.Empty, vm.SelectedDecalId);
        Assert.False(vm.HasSelectedDecal);
    }

    [Fact]
    public async Task DraggingDecal_UpdatesPositionAndCompletesOnDragFinish()
    {
        var (vm, _) = CreateViewModel();
        await vm.ActivateAsync(WorkspaceWith("Test"));

        var decal = vm.DecalList[0];
        vm.SelectedDecalId = decal.Id;

        // Simulate decal move
        var newPos = new Vector3(15, 25, 35);
        var newNorm = new Vector3(0, 0, 1);

        // Raising DecalMoved
        var movedMethod = typeof(DecalViewModel).GetMethod("OnDecalMoved", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        movedMethod!.Invoke(vm, new object[] { decal.Id, newPos, newNorm });

        Assert.Equal(newPos, vm.Anchor);
        Assert.Equal(newNorm, vm.AnchorNormal);

        // Raising DecalDragCompleted
        var dragCompletedMethod = typeof(DecalViewModel).GetMethod("OnDecalDragCompleted", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        dragCompletedMethod!.Invoke(vm, new object[] { decal.Id });

        Assert.Equal(newPos, vm.Anchor);
    }
}
