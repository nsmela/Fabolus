using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.Messaging;
using Fabolus.Core.Geometry;
using Fabolus.Core.Geometry.Metadata;
using Fabolus.Wpf.Common;
using Fabolus.Wpf.Features.Main;
using Fabolus.Wpf.Features.Moulding;
using Moq;
using Xunit;

namespace Fabolus.Wpf.Tests.Features.Moulding;

/// <summary>
/// What the mould view puts in the viewport's info panel.
/// </summary>
/// <remarks>
/// It was the one tool that published nothing, so the panel sat empty while every other view
/// filled it. MainViewModel clears the panel as the view changes and the mould view publishes
/// from ActivateAsync afterwards, so these also pin that ordering down: a regression there shows
/// up as an empty panel, which is exactly what it used to be.
/// </remarks>
public class MouldInfoPanelTests
{
    private static readonly IGeometryEngine Engine = global::GeometryEngine.BspGeometryEngine.Create();

    [Fact]
    public void ActivatingTheMouldView_FillsTheInfoPanel() => UiThread.Run(async () =>
    {
        var items = await InfoAfterActivating();

        Assert.NotEmpty(items);
        Assert.Contains(items.OfType<TextInfoItem>(), i => i.Label == "Bolus Volume");
        Assert.Contains(items.OfType<TextInfoItem>(), i => i.Label == "Surface Area");
        Assert.Contains(items.OfType<TextInfoItem>(), i => i.Label == "Dimensions");
    });

    /// <summary>
    /// The bolus and the trough both report a volume in mL, so neither is labelled just "Volume"
    /// - one row of each pair would be meaningless on its own.
    /// </summary>
    [Fact]
    public void NoVolumeIsLabelledAmbiguously() => UiThread.Run(async () =>
    {
        var items = await InfoAfterActivating();

        Assert.DoesNotContain(items.OfType<TextInfoItem>(), i => i.Label == "Volume");
    });

    /// <summary>
    /// The engine measures in the mesh's own units - millimetres - so its volume is mm3. Two
    /// panels used to print that straight out under an "mL" label, putting every volume in the
    /// app out by a factor of a thousand.
    /// </summary>
    [Fact]
    public void TheVolumeIsReportedInMillilitres() => UiThread.Run(async () =>
    {
        var items = await InfoAfterActivating();

        var volume = Assert.Single(items.OfType<TextInfoItem>(), i => i.Label == "Bolus Volume");

        // The box is 60 x 60 x 20 mm = 72,000 mm3 = 72 mL.
        Assert.Equal("72.00 mL", volume.Value);
    });

    /// <summary>
    /// Nothing in the panel should carry a millilitre label with a cubic-millimetre number behind
    /// it. 72,000 would be the tell for this mesh.
    /// </summary>
    [Fact]
    public void NoVolumeIsLeftInCubicMillimetres() => UiThread.Run(async () =>
    {
        var items = await InfoAfterActivating();

        Assert.All(items.OfType<TextInfoItem>().Where(i => i.Value?.EndsWith("mL") == true), item =>
            Assert.False(item.Value!.StartsWith("72000") || item.Value.StartsWith("72,000"),
                $"'{item.Label}' reads {item.Value}, which is the mm3 figure under an mL label"));
    });

    /// <summary>
    /// Square centimetres, as every other panel reports it. This one was alone in showing mm2,
    /// which put the same mesh's area two orders of magnitude apart depending on which tool you
    /// happened to have open.
    /// </summary>
    [Fact]
    public void TheSurfaceAreaIsReportedInSquareCentimetres() => UiThread.Run(async () =>
    {
        var items = await InfoAfterActivating();

        var area = Assert.Single(items.OfType<TextInfoItem>(), i => i.Label == "Surface Area");

        // The box is 60 x 60 x 20mm: two 60x60 faces and four 60x20 sides = 12,000mm2 = 120cm2.
        Assert.Equal("120.00 cm²", area.Value);
    });

    private static async Task<List<MeshInfoItem>> InfoAfterActivating()
    {
        var messenger = new StrongReferenceMessenger();

        var published = new List<MeshInfoItem>();
        messenger.Register<UpdateMeshInfoMessage>(new object(), (_, m) =>
        {
            // Each publish replaces the panel's contents, so the last one is what is on screen.
            published.Clear();
            published.AddRange(m.Items);
        });

        var vm = new MouldViewModel(messenger, Mock.Of<IAlertDialog>(), Engine);
        await vm.ActivateAsync(WorkspaceWithBox());

        return published;
    }

    private static Workspace WorkspaceWithBox()
    {
        var mesh = Engine.Generators.GenerateBox(new Vector3(-30, -30, 0), new Vector3(30, 30, 20)).Value
            .WithMeasurements(Engine);
        return Workspace.CreateEmpty().AddMesh(mesh, MeshRecord.ForImport("box")).Value;
    }
}
