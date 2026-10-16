using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Common.Interfaces;
using Fabolus.Core.Geometry;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.MeshManager;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.MeshManager;

/// <summary>
/// The topology rows the mesh manager publishes. "Orphaned Vertices" used to be hardcoded to
/// "No", so it reassured whatever the mesh held.
/// </summary>
public class MeshManagerInfoPanelTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void AMeshWithOrphanedVertices_IsReportedAsHavingThem() => UiThread.Run(async () =>
    {
        var box = Box();
        // Two vertices no triangle refers to.
        var withOrphans = Engine.CreateMesh(
            box.Vertices.Add(new Vector3(100, 100, 100)).Add(new Vector3(200, 200, 200)),
            box.Triangles,
            box.Metadata).Value;

        var items = await InfoPanelFor(withOrphans);

        var row = Assert.Single(items.OfType<StatusInfoItem>(), i => i.Label == "Orphaned Vertices");
        Assert.Equal("Yes", row.Text);
        var count = Assert.Single(items.OfType<TextInfoItem>(), i => i.Label == "Orphaned");
        Assert.Equal("2", count.Value);
    });

    [Fact]
    public void AMeshWithoutOrphanedVertices_IsReportedAsClear() => UiThread.Run(async () =>
    {
        var items = await InfoPanelFor(Box());

        var row = Assert.Single(items.OfType<StatusInfoItem>(), i => i.Label == "Orphaned Vertices");
        Assert.Equal("No", row.Text);
        Assert.DoesNotContain(items.OfType<TextInfoItem>(), i => i.Label == "Orphaned");
    });

    private static IMesh Box() =>
        Engine.Generators.GenerateBox(new Vector3(-20, -30, 0), new Vector3(20, 30, 50)).Value;

    private static async Task<List<MeshInfoItem>> InfoPanelFor(IMesh mesh)
    {
        var messenger = new StrongReferenceMessenger();

        var published = new List<MeshInfoItem>();
        messenger.Register<UpdateMeshInfoMessage>(new object(), (_, m) =>
        {
            published.Clear();
            published.AddRange(m.Items);
        });

        var vm = new MeshManagerViewModel(
            messenger, Mock.Of<IDialogueSystem>(), Mock.Of<IAlertDialog>(), Engine);

        await vm.ActivateAsync(Workspace.CreateEmpty().AddMesh(mesh, MeshRecord.ForImport("bolus")).Value);
        return published;
    }
}
