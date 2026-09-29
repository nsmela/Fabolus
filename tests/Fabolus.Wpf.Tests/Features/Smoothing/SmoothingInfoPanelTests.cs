using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Features.Moulds;
using Fabolus.Core.Features.Smoothing;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Smoothing;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Smoothing;

/// <summary>
/// The figures the smoothing panel reports for a mesh that has been through more than smoothing.
/// </summary>
/// <remarks>
/// Both rows are about the bolus: the mesh as it was imported, and the mesh as smoothing left it.
/// Neither is about whatever the entry has become since - and once a mould is generated, the
/// active mesh is the mould.
/// </remarks>
public class SmoothingInfoPanelTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    // 40 x 60 x 50mm = 120,000mm3 = 120mL.
    private const double BoxMillilitres = 120.0;

    [Fact]
    public void AnUnsmoothedMesh_ReportsItsOriginalVolume() => UiThread.Run(async () =>
    {
        var items = await InfoPanelFor(WorkspaceWith());

        var original = Assert.Single(VolumesUnder(items, "Original Mesh"));
        Assert.Equal(BoxMillilitres, MillilitresIn(original), 1);
    });

    /// <summary>
    /// A mould is cut around the bolus and hollowed out by it, so its volume is nothing like the
    /// bolus's. Reading the active mesh here reported the mould under a "Smoothed Mesh" heading.
    /// </summary>
    [Fact]
    public void WithAMould_TheSmoothedFigureIsStillTheBolus() => UiThread.Run(async () =>
    {
        var withoutMould = await InfoPanelFor(WorkspaceWith(new SmoothSettings()));
        var withMould = await InfoPanelFor(WorkspaceWith(new SmoothSettings(), new ConcaveMouldDefinition()));

        var bolusAlone = Assert.Single(VolumesUnder(withoutMould, "Smoothed Mesh"));
        var bolusUnderMould = Assert.Single(VolumesUnder(withMould, "Smoothed Mesh"));

        Assert.Equal(MillilitresIn(bolusAlone), MillilitresIn(bolusUnderMould), 1);
    });

    /// <summary>
    /// The original figure comes off the record's base mesh, which a mould never touches.
    /// </summary>
    [Fact]
    public void WithAMould_TheOriginalFigureIsUnchanged() => UiThread.Run(async () =>
    {
        var items = await InfoPanelFor(WorkspaceWith(new SmoothSettings(), new ConcaveMouldDefinition()));

        var original = Assert.Single(VolumesUnder(items, "Original Mesh"));
        Assert.Equal(BoxMillilitres, MillilitresIn(original), 1);
    });

    /// <summary>The volume rows sitting under a given section heading.</summary>
    private static List<TextInfoItem> VolumesUnder(List<MeshInfoItem> items, string section)
    {
        var start = items.FindIndex(i => i is TitleInfoItem t && t.Label == section);
        Assert.True(start >= 0, $"no '{section}' section in the panel");

        return items
            .Skip(start + 1)
            .TakeWhile(i => i is not TitleInfoItem)
            .OfType<TextInfoItem>()
            .Where(i => i.Label == "Volume")
            .ToList();
    }

    private static double MillilitresIn(TextInfoItem item) =>
        double.Parse(item.Value!.Replace(" mL", string.Empty), System.Globalization.CultureInfo.CurrentCulture);

    private static async Task<List<MeshInfoItem>> InfoPanelFor(Workspace workspace)
    {
        var messenger = new StrongReferenceMessenger();

        var published = new List<MeshInfoItem>();
        messenger.Register<UpdateMeshInfoMessage>(new object(), (_, m) =>
        {
            published.Clear();
            published.AddRange(m.Items);
        });

        var vm = new SmoothingViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(workspace);

        return published;
    }

    /// <summary>
    /// A box with each command actually applied, so the active mesh really is what the commands
    /// say it is. Ascending priority order, since recording one clears anything above it.
    /// </summary>
    private static Workspace WorkspaceWith(params IMeshCommand[] commands)
    {
        var mesh = Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value
            .WithMeasurements(Engine);

        var record = MeshRecord.ForImport("bolus").WithBaseMesh(mesh);
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
}
